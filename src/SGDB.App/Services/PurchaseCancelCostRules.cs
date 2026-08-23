using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69D-C2-B1 — cancelamento/reabertura só reverte custo quando a conta é segura.
/// extra.preco_compra volta a ser o último custo da última compra fechada não cancelada.
/// </summary>
public static class PurchaseCancelCostRules
{
    public const string AtomicFeature = "purchase_cancel_cost_safe";

    public const string UnsafePostMovementMessage =
        "Esta compra não pode ser cancelada porque houve movimentações posteriores que alteraram o estoque deste produto.";

    public const string HostNeedsUpgradeBeforeCancelMessage =
        "O PC da loja precisa ser atualizado antes de cancelar ou reabrir uma compra.";

    public const string InsufficientHistoryMessage =
        "Esta compra não pode ser cancelada porque não há registro suficiente para restaurar o custo.";

    public static bool SupportsCancelCostSafe(IEnumerable<string>? features) =>
        features is not null
        && features.Any(f => string.Equals(f, AtomicFeature, StringComparison.OrdinalIgnoreCase));

    public static PostPurchaseMovementKind ClassifyOperation(string? operation)
    {
        var op = (operation ?? "").Trim().ToLowerInvariant();
        return op switch
        {
            "transferencia_geladeira" or "retorno_geladeira" => PostPurchaseMovementKind.SafeLocation,
            "entrada_compra" or "entrada_nfe" or "estorno_compra" => PostPurchaseMovementKind.SafePurchase,
            "venda" => PostPurchaseMovementKind.Sale,
            "cancelamento_venda" => PostPurchaseMovementKind.SaleRestore,
            "unificacao_produto" => PostPurchaseMovementKind.Unsafe,
            _ => PostPurchaseMovementKind.Unsafe,
        };
    }

    public static void ThrowIfUnsafeToReverse(
        SqliteConnection conn,
        SqliteTransaction tx,
        int purchaseId,
        int productId,
        bool gerarEstoque,
        string? purchaseCreatedAt)
    {
        if (productId <= 0)
            return;

        if (HasIncompatibleLaterPurchase(conn, tx, purchaseId, productId, gerarEstoque)
            || HasUnsafePhysicalMovements(conn, tx, purchaseId, productId, gerarEstoque, purchaseCreatedAt))
            throw new InvalidOperationException(UnsafePostMovementMessage);
    }

    public static bool HasLaterClosedPurchase(
        SqliteConnection conn, SqliteTransaction tx, int purchaseId, int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT 1
            FROM purchase_items pi
            INNER JOIN purchases p ON p.id = pi.purchase_id
            WHERE pi.product_id = $pid
              AND p.id > $id
              AND p.status = 'fechada'
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return cmd.ExecuteScalar() is not null and not DBNull;
    }

    /// <summary>
    /// Último custo de catálogo da última compra fechada, excluindo a que está sendo cancelada.
    /// gerar_estoque não entra no critério — o campo é o último custo da NF válida.
    /// Ordem por id (inserção), não pela data digitada.
    /// </summary>
    public static double LastValidCatalogCost(
        SqliteConnection conn,
        SqliteTransaction tx,
        int productId,
        int excludePurchaseId,
        string name,
        string? group,
        double packFactor)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT pi.quantity, pi.unit_price
            FROM purchase_items pi
            INNER JOIN purchases p ON p.id = pi.purchase_id
            WHERE pi.product_id = $pid
              AND p.id != $exclude
              AND p.status = 'fechada'
            ORDER BY p.id DESC, pi.id DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$exclude", excludePurchaseId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return 0;

        var qty = reader.GetDouble(0);
        var unit = reader.GetDouble(1);
        reader.Close();
        if (qty <= 0.0001)
            return 0;

        PurchaseAverageCostRules.ToAverageUnits(
            name, group, packFactor, qty, unit, out _, out var lineCost);
        return Math.Round(lineCost, 4);
    }

    /// <summary>
    /// Último custo de catálogo entre vários produtos (ex.: keep+absorb antes do remap do merge).
    /// </summary>
    public static double LastValidCatalogCostAmong(
        SqliteConnection conn,
        SqliteTransaction tx,
        IReadOnlyList<int> productIds,
        string name,
        string? group,
        double packFactor)
    {
        var ids = productIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return 0;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var parms = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            var p = $"$id{i}";
            parms.Add(p);
            cmd.Parameters.AddWithValue(p, ids[i]);
        }

        cmd.CommandText = $"""
            SELECT pi.quantity, pi.unit_price
            FROM purchase_items pi
            INNER JOIN purchases p ON p.id = pi.purchase_id
            WHERE pi.product_id IN ({string.Join(",", parms)})
              AND p.status = 'fechada'
            ORDER BY p.id DESC, pi.id DESC
            LIMIT 1;
            """;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return 0;

        var qty = reader.GetDouble(0);
        var unit = reader.GetDouble(1);
        reader.Close();
        if (qty <= 0.0001)
            return 0;

        PurchaseAverageCostRules.ToAverageUnits(
            name, group, packFactor, qty, unit, out _, out var lineCost);
        return Math.Round(lineCost, 4);
    }

    public static bool TryReadCostFromAtPurchase(
        SqliteConnection conn,
        SqliteTransaction tx,
        int purchaseId,
        int productId,
        out double costFrom)
    {
        costFrom = 0;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT details
            FROM audit_log
            WHERE action = 'alterar' AND entity = 'produto' AND entity_id = $eid
            ORDER BY id DESC
            LIMIT 80;
            """;
        cmd.Parameters.AddWithValue("$eid", productId.ToString());
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var details = reader.IsDBNull(0) ? "" : reader.GetString(0);
            if (!TryParseCostFrom(details, purchaseId, productId, out var value))
                continue;
            costFrom = value;
            return true;
        }

        return false;
    }

    internal static bool TryParseCostFrom(string? details, int purchaseId, int productId, out double costFrom)
    {
        costFrom = 0;
        if (!AuditPayloadBuilder.TryParse(details, out var doc)
            || doc.Payload.ValueKind != JsonValueKind.Object)
            return false;

        var payload = doc.Payload;
        if (!TryGetInt(payload, "purchase_id", out var pid) || pid != purchaseId)
            return false;
        if (TryGetInt(payload, "product_id", out var prod) && prod != productId)
            return false;
        var source = GetString(payload, "source");
        if (!string.Equals(source, "compra", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!payload.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Object)
            return false;
        if (!changes.TryGetProperty("preco_custo", out var costChange) || costChange.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryGetDouble(costChange, "de", out costFrom))
            return false;
        return double.IsFinite(costFrom);
    }

    private static bool HasIncompatibleLaterPurchase(
        SqliteConnection conn, SqliteTransaction tx, int purchaseId, int productId, bool gerarEstoque)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT IFNULL(p.gerar_estoque, 1)
            FROM purchase_items pi
            INNER JOIN purchases p ON p.id = pi.purchase_id
            WHERE pi.product_id = $pid
              AND p.id > $id
              AND p.status = 'fechada';
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$id", purchaseId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var laterGera = reader.GetInt32(0) == 1;
            if (gerarEstoque && !laterGera)
                return true;
            if (!gerarEstoque && laterGera)
                return true;
        }

        return false;
    }

    private static bool HasUnsafePhysicalMovements(
        SqliteConnection conn,
        SqliteTransaction tx,
        int purchaseId,
        int productId,
        bool gerarEstoque,
        string? purchaseCreatedAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        if (gerarEstoque)
        {
            var anchor = ResolveAnchorMovementId(conn, tx, purchaseId, productId);
            cmd.CommandText = """
                SELECT IFNULL(operation,''), IFNULL(quantity,0)
                FROM movements
                WHERE product_id = $pid AND id > $anchor
                ORDER BY id;
                """;
            cmd.Parameters.AddWithValue("$pid", productId);
            cmd.Parameters.AddWithValue("$anchor", anchor);
        }
        else
        {
            var createdAt = (purchaseCreatedAt ?? "").Trim();
            if (string.IsNullOrWhiteSpace(createdAt))
                throw new InvalidOperationException(InsufficientHistoryMessage);

            cmd.CommandText = """
                SELECT IFNULL(operation,''), IFNULL(quantity,0)
                FROM movements
                WHERE product_id = $pid AND created_at >= $at
                ORDER BY id;
                """;
            cmd.Parameters.AddWithValue("$pid", productId);
            cmd.Parameters.AddWithValue("$at", createdAt);
        }

        double sold = 0;
        double restored = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var kind = ClassifyOperation(reader.IsDBNull(0) ? "" : reader.GetString(0));
            var qty = Math.Abs(reader.GetDouble(1));
            switch (kind)
            {
                case PostPurchaseMovementKind.SafeLocation:
                case PostPurchaseMovementKind.SafePurchase:
                    continue;
                case PostPurchaseMovementKind.Sale:
                    sold += qty;
                    break;
                case PostPurchaseMovementKind.SaleRestore:
                    restored += qty;
                    break;
                default:
                    return true;
            }
        }

        return Math.Abs(sold - restored) > 1e-4;
    }

    private static long ResolveAnchorMovementId(
        SqliteConnection conn,
        SqliteTransaction tx,
        int purchaseId,
        int productId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT IFNULL(MAX(id), 0)
            FROM movements
            WHERE product_id = $pid
              AND IFNULL(ref_type,'') = 'purchase'
              AND ref_id = $rid;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$rid", purchaseId);
        var anchor = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        if (anchor > 0)
            return anchor;

        throw new InvalidOperationException(InsufficientHistoryMessage);
    }

    private static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value))
            return true;
        return p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value);
    }

    private static bool TryGetDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out value))
            return true;
        return p.ValueKind == JsonValueKind.String
            && double.TryParse(p.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}

public enum PostPurchaseMovementKind
{
    SafeLocation,
    SafePurchase,
    Sale,
    SaleRestore,
    Unsafe,
}
