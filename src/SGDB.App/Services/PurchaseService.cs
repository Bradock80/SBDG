using Microsoft.Data.Sqlite;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public sealed class PurchaseItemInput
{
    /// <summary>Preenchido após INSERT em purchase_items (mesma transação).</summary>
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string ProductName { get; set; }
    public double Quantity { get; set; }
    public double UnitPrice { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    /// <summary>Preço de venda escolhido na compra (cadastro só muda se UpdateSalePrice).</summary>
    public double SalePrice { get; set; }
    /// <summary>Intenção explícita do operador de gravar SalePrice em products.sale_price.</summary>
    public bool UpdateSalePrice { get; set; }
}

public sealed class PurchaseInput
{
    public int SupplierId { get; set; }
    /// <summary>CNPJ/CPF do fornecedor — na rede a loja resolve o ID certo por documento.</summary>
    public string? SupplierCnpj { get; set; }
    public required string EmissionDate { get; set; }
    public required string EntryDate { get; set; }
    public string Series { get; set; } = "1";
    public required string Number { get; set; }
    public string? NfeKey { get; set; }
    public bool GerarEstoque { get; set; } = true;
    public string? Notes { get; set; }
    public List<PurchaseItemInput> Items { get; set; } = [];
}

/// <summary>Última entrada de compra de um produto (não cancelada).</summary>
public sealed record ProductLastEntry(double Quantity, string EntryDate, int PurchaseId, double UnitPrice);

public static class PurchaseService
{
    /// <summary>
    /// Somente testes: invocado imediatamente antes de gravar purchase_item_lots.
    /// Deve permanecer null em produção. Sem regra por productId.
    /// </summary>
    public static Action? TestBeforeInsertPurchaseItemLot { get; set; }

    /// <summary>
    /// Somente testes: invocado imediatamente antes de atualizar products.sale_price.
    /// Deve permanecer null em produção.
    /// </summary>
    public static Action? TestBeforeApplySalePrice { get; set; }

    /// <summary>
    /// Somente testes: invocado depois de atualizar sale_price e antes do commit.
    /// Deve permanecer null em produção.
    /// </summary>
    public static Action? TestAfterApplySalePrice { get; set; }

    /// <summary>Última entrada do produto pela data de entrada da compra.</summary>
    public static ProductLastEntry? GetLastEntry(int productId)
    {
        if (productId <= 0)
            return null;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pi.quantity, IFNULL(p.entry_date,''), p.id, pi.unit_price
            FROM purchase_items pi
            INNER JOIN purchases p ON p.id = pi.purchase_id
            WHERE pi.product_id = $pid
              AND p.status != 'cancelada'
            ORDER BY p.entry_date DESC, p.id DESC, pi.id DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return new ProductLastEntry(
            reader.GetDouble(0),
            reader.IsDBNull(1) ? "" : reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? 0 : reader.GetDouble(3));
    }

    /// <summary>Última entrada por produto (primeiro registro por id após ORDER BY).</summary>
    public static Dictionary<int, ProductLastEntry> GetLastEntries(IEnumerable<int> productIds)
    {
        var ids = productIds.Where(id => id > 0).Distinct().ToList();
        var map = new Dictionary<int, ProductLastEntry>();
        if (ids.Count == 0)
            return map;

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var parms = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            var name = $"$id{i}";
            parms.Add(name);
            cmd.Parameters.AddWithValue(name, ids[i]);
        }

        cmd.CommandText = $"""
            SELECT pi.product_id, pi.quantity, IFNULL(p.entry_date,''), p.id, pi.unit_price
            FROM purchase_items pi
            INNER JOIN purchases p ON p.id = pi.purchase_id
            WHERE p.status != 'cancelada'
              AND pi.product_id IN ({string.Join(",", parms)})
            ORDER BY p.entry_date DESC, p.id DESC, pi.id DESC;
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var pid = reader.GetInt32(0);
            if (map.ContainsKey(pid))
                continue;
            map[pid] = new ProductLastEntry(
                reader.GetDouble(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? 0 : reader.GetDouble(4));
        }

        return map;
    }

    public static IReadOnlyList<PurchaseItemLot> ListPurchaseItemLots(int purchaseId)
    {
        if (purchaseId <= 0)
            return [];

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, purchase_item_id, purchase_id, product_id,
                   IFNULL(lot_number,''), expiry_date, quantity, product_lot_id, IFNULL(created_at,'')
            FROM purchase_item_lots
            WHERE purchase_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return ReadPurchaseItemLots(cmd);
    }

    public static IReadOnlyList<PurchaseItemLot> ListPurchaseItemLotsByItem(int purchaseItemId)
    {
        if (purchaseItemId <= 0)
            return [];

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, purchase_item_id, purchase_id, product_id,
                   IFNULL(lot_number,''), expiry_date, quantity, product_lot_id, IFNULL(created_at,'')
            FROM purchase_item_lots
            WHERE purchase_item_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", purchaseItemId);
        return ReadPurchaseItemLots(cmd);
    }

    private static IReadOnlyList<PurchaseItemLot> ReadPurchaseItemLots(SqliteCommand cmd)
    {
        var list = new List<PurchaseItemLot>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            DateTime? expiry = null;
            if (!reader.IsDBNull(5))
            {
                var iso = reader.GetString(5);
                if (DateTime.TryParse(iso, out var d))
                    expiry = d.Date;
            }

            list.Add(new PurchaseItemLot
            {
                Id = reader.GetInt32(0),
                PurchaseItemId = reader.GetInt32(1),
                PurchaseId = reader.GetInt32(2),
                ProductId = reader.GetInt32(3),
                LotNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ExpiryDate = expiry,
                Quantity = reader.GetDouble(6),
                ProductLotId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                CreatedAt = reader.IsDBNull(8) ? "" : reader.GetString(8),
            });
        }
        return list;
    }


    public static string FormatLastEntryDisplay(ProductLastEntry? entry, string? unit = "UN")
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.EntryDate))
            return "";
        var u = Product.ResolveStockUnitLabel(unit);
        return $"{entry.Quantity:G} {u} · {DateBrHelper.FormatIso(entry.EntryDate)}";
    }

    public static string FormatLastEntryLong(ProductLastEntry? entry, string? unit = "UN")
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.EntryDate))
            return "Sem entrada de compra";
        var u = Product.ResolveStockUnitLabel(unit);
        return $"{entry.Quantity:G} {u} em {DateBrHelper.FormatIso(entry.EntryDate)}";
    }

    public static IReadOnlyList<Purchase> List(
        string? search = null,
        string status = "todas",
        string? dateFrom = null,
        string? dateTo = null)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.ListPurchases(search, status, dateFrom, dateTo);
        return ListLocal(search, status, dateFrom, dateTo);
    }

    public static IReadOnlyList<Purchase> ListLocal(
        string? search = null,
        string status = "todas",
        string? dateFrom = null,
        string? dateTo = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();

        var sql = """
            SELECT p.id, p.supplier_id, pe.name, pe.cpf_cnpj, pe.state,
                   p.emission_date, p.entry_date, p.series, p.number, p.nfe_key,
                   p.status, p.total, p.gerar_estoque, p.notes, p.created_at
            FROM purchases p
            INNER JOIN people pe ON pe.id = p.supplier_id
            WHERE 1=1
            """;

        if (status is "aberta" or "fechada" or "cancelada")
        {
            sql += " AND p.status = $status";
            cmd.Parameters.AddWithValue("$status", status);
        }

        var isoFrom = DateBrHelper.ToIso(dateFrom);
        if (!string.IsNullOrEmpty(isoFrom))
        {
            sql += " AND p.emission_date >= $from";
            cmd.Parameters.AddWithValue("$from", isoFrom);
        }

        var isoTo = DateBrHelper.ToIso(dateTo);
        if (!string.IsNullOrEmpty(isoTo))
        {
            sql += " AND p.emission_date <= $to";
            cmd.Parameters.AddWithValue("$to", isoTo);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var raw = search.Trim();
            sql += """
                 AND (
                    UPPER(IFNULL(p.series,'')) LIKE $like ESCAPE '\'
                    OR UPPER(IFNULL(p.number,'')) LIKE $like ESCAPE '\'
                    OR UPPER(pe.name) LIKE $like ESCAPE '\'
                    OR IFNULL(pe.cpf_cnpj,'') LIKE $like ESCAPE '\'
                    OR IFNULL(p.nfe_key,'') LIKE $like ESCAPE '\'
                 )
                """;
            var escaped = raw.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            cmd.Parameters.AddWithValue("$like", $"%{escaped.ToUpperInvariant()}%");
        }

        sql += " ORDER BY p.id DESC LIMIT 2000";
        cmd.CommandText = sql;

        var list = new List<Purchase>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadHeader(reader));
        return list;
    }

    public static double SumTotal(
        string? search = null,
        string status = "todas",
        string? dateFrom = null,
        string? dateTo = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();

        var sql = """
            SELECT COALESCE(SUM(p.total), 0)
            FROM purchases p
            INNER JOIN people pe ON pe.id = p.supplier_id
            WHERE p.status != 'cancelada'
            """;

        if (status is "aberta" or "fechada" or "cancelada")
        {
            sql += " AND p.status = $status";
            cmd.Parameters.AddWithValue("$status", status);
        }

        var isoFrom = DateBrHelper.ToIso(dateFrom);
        if (!string.IsNullOrEmpty(isoFrom))
        {
            sql += " AND p.emission_date >= $from";
            cmd.Parameters.AddWithValue("$from", isoFrom);
        }

        var isoTo = DateBrHelper.ToIso(dateTo);
        if (!string.IsNullOrEmpty(isoTo))
        {
            sql += " AND p.emission_date <= $to";
            cmd.Parameters.AddWithValue("$to", isoTo);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var raw = search.Trim();
            sql += """
                 AND (
                    UPPER(IFNULL(p.series,'')) LIKE $like ESCAPE '\'
                    OR UPPER(IFNULL(p.number,'')) LIKE $like ESCAPE '\'
                    OR UPPER(pe.name) LIKE $like ESCAPE '\'
                    OR IFNULL(pe.cpf_cnpj,'') LIKE $like ESCAPE '\'
                    OR IFNULL(p.nfe_key,'') LIKE $like ESCAPE '\'
                 )
                """;
            var escaped = raw.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            cmd.Parameters.AddWithValue("$like", $"%{escaped.ToUpperInvariant()}%");
        }

        cmd.CommandText = sql;
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    public static Purchase? GetById(int id)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetPurchase(id);
        return GetByIdLocal(id);
    }

    public static Purchase? GetByIdLocal(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.supplier_id, pe.name, pe.cpf_cnpj, pe.state,
                   p.emission_date, p.entry_date, p.series, p.number, p.nfe_key,
                   p.status, p.total, p.gerar_estoque, p.notes, p.created_at
            FROM purchases p
            INNER JOIN people pe ON pe.id = p.supplier_id
            WHERE p.id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var purchase = ReadHeader(reader);
        var items = LoadItems(conn, id);
        return CopyWithItems(purchase, items);
    }

    public static bool NfeKeyExists(string chave)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.NfeKeyExists(chave);
        return NfeKeyExistsLocal(chave);
    }

    public static bool NfeKeyExistsLocal(string chave)
    {
        var key = (chave ?? "").Trim();
        if (key.Length < 20) return false;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM purchases
            WHERE nfe_key = $k AND status != 'cancelada'
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() is not null;
    }

    public static int Create(PurchaseInput input, bool closeOnSave = false)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.CreatePurchase(input, closeOnSave);
        return CreateLocal(input, closeOnSave);
    }

    public static int CreateLocal(PurchaseInput input, bool closeOnSave = false)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("criar compra");
        ValidateInput(input);
        input.SupplierId = ResolveSupplierIdForSave(input);
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        LegacySupplierBridge.EnsureMirrored(input.SupplierId, conn, tx);

        var total = input.Items.Sum(i => i.Quantity * i.UnitPrice);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO purchases (
                    supplier_id, emission_date, entry_date, series, number, nfe_key,
                    status, total, gerar_estoque, notes, created_at
                ) VALUES (
                    $supplier, $emission, $entry, $series, $number, $nfe,
                    'aberta', $total, $gerar, $notes, datetime('now','localtime')
                );
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$supplier", input.SupplierId);
            cmd.Parameters.AddWithValue("$emission", input.EmissionDate);
            cmd.Parameters.AddWithValue("$entry", input.EntryDate);
            cmd.Parameters.AddWithValue("$series", input.Series.Trim());
            cmd.Parameters.AddWithValue("$number", input.Number.Trim());
            cmd.Parameters.AddWithValue("$nfe", (object?)input.NfeKey?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$total", total);
            cmd.Parameters.AddWithValue("$gerar", input.GerarEstoque ? 1 : 0);
            cmd.Parameters.AddWithValue("$notes", (object?)input.Notes ?? DBNull.Value);
            var id = Convert.ToInt32(cmd.ExecuteScalar());
            InsertItems(conn, tx, id, input.Items);

            if (closeOnSave && input.GerarEstoque)
                ApplyStock(conn, tx, id, input.Items, reverse: false);

            List<SalePriceAuditPending> saleAudits = [];
            if (closeOnSave)
            {
                using var close = conn.CreateCommand();
                close.Transaction = tx;
                close.CommandText = "UPDATE purchases SET status = 'fechada', lot_origin_recorded = 1 WHERE id = $id;";
                close.Parameters.AddWithValue("$id", id);
                close.ExecuteNonQuery();
                PayableService.SyncFromPurchase(conn, tx, id);
                saleAudits = ApplySalePricesInTx(conn, tx, input.Items);
            }

            tx.Commit();
            if (closeOnSave)
            {
                LogPurchaseAudit(id, input, total);
                LogSalePriceAudits(id, saleAudits);
            }
            return id;
        }
    }

    public static void Update(int id, PurchaseInput input, bool closeOnSave = false)
    {
        if (StoreNetworkMode.IsClient)
        {
            StoreNetworkClient.UpdatePurchase(id, input, closeOnSave);
            return;
        }
        UpdateLocal(id, input, closeOnSave);
    }

    public static void UpdateLocal(int id, PurchaseInput input, bool closeOnSave = false)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("atualizar compra");
        ValidateInput(input);
        input.SupplierId = ResolveSupplierIdForSave(input);
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var current = GetStatus(conn, tx, id)
            ?? throw new InvalidOperationException("Compra não encontrada.");

        if (current != "aberta")
            throw new InvalidOperationException("Somente compras abertas podem ser alteradas.");

        LegacySupplierBridge.EnsureMirrored(input.SupplierId, conn, tx);

        var total = input.Items.Sum(i => i.Quantity * i.UnitPrice);

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM purchase_items WHERE purchase_id = $id;";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE purchases SET
                    supplier_id = $supplier,
                    emission_date = $emission,
                    entry_date = $entry,
                    series = $series,
                    number = $number,
                    nfe_key = $nfe,
                    total = $total,
                    gerar_estoque = $gerar,
                    notes = $notes
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$supplier", input.SupplierId);
            cmd.Parameters.AddWithValue("$emission", input.EmissionDate);
            cmd.Parameters.AddWithValue("$entry", input.EntryDate);
            cmd.Parameters.AddWithValue("$series", input.Series.Trim());
            cmd.Parameters.AddWithValue("$number", input.Number.Trim());
            cmd.Parameters.AddWithValue("$nfe", (object?)input.NfeKey?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$total", total);
            cmd.Parameters.AddWithValue("$gerar", input.GerarEstoque ? 1 : 0);
            cmd.Parameters.AddWithValue("$notes", (object?)input.Notes ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        InsertItems(conn, tx, id, input.Items);

        if (closeOnSave && input.GerarEstoque)
            ApplyStock(conn, tx, id, input.Items, reverse: false);

        List<SalePriceAuditPending> saleAudits = [];
        if (closeOnSave)
        {
            using var close = conn.CreateCommand();
            close.Transaction = tx;
            close.CommandText = "UPDATE purchases SET status = 'fechada', lot_origin_recorded = 1 WHERE id = $id;";
            close.Parameters.AddWithValue("$id", id);
            close.ExecuteNonQuery();
            PayableService.SyncFromPurchase(conn, tx, id);
            saleAudits = ApplySalePricesInTx(conn, tx, input.Items);
        }

        tx.Commit();
        if (closeOnSave)
        {
            LogPurchaseAudit(id, input, total);
            LogSalePriceAudits(id, saleAudits);
        }
    }

    private static void LogPurchaseAudit(int purchaseId, PurchaseInput input, double total)
    {
        var supplier = PersonService.GetById(input.SupplierId);
        var source = !string.IsNullOrWhiteSpace(input.NfeKey)
            || input.Notes?.Contains("Importado via XML", StringComparison.OrdinalIgnoreCase) == true
            ? "nfe_xml"
            : "manual";
        var nfLabel = string.IsNullOrWhiteSpace(input.Number) ? "s/n" : input.Number.Trim();
        AuditService.LogJson("entrada", "compra", purchaseId.ToString(),
            AuditPayloadBuilder.PurchaseEntry(
                purchaseId, input.SupplierId, supplier?.Name,
                input.Number, input.NfeKey, total, input.Items.Count,
                input.GerarEstoque, source),
            $"NF {nfLabel} · {supplier?.Name ?? "Fornecedor"} · R$ {total:N2} · {input.Items.Count} item(ns)");
    }

    public static void Delete(int id)
    {
        if (StoreNetworkMode.IsClient)
        {
            StoreNetworkClient.DeletePurchase(id);
            return;
        }
        DeleteLocal(id);
    }

    public static void DeleteLocal(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("excluir compra");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var status = GetStatus(conn, tx, id)
            ?? throw new InvalidOperationException("Compra não encontrada.");

        if (status != "aberta")
            throw new InvalidOperationException("Somente compras abertas podem ser excluídas.");

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public static void Cancel(int id)
    {
        if (StoreNetworkMode.IsClient)
        {
            StoreNetworkClient.CancelPurchase(id);
            return;
        }
        CancelLocal(id);
    }

    public static void CancelLocal(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("cancelar compra");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var status = GetStatus(conn, tx, id)
            ?? throw new InvalidOperationException("Compra não encontrada.");

        if (status == "cancelada")
            return;

        if (status == "fechada")
        {
            ReverseClosedPurchaseEffects(conn, tx, id);
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE purchases SET status = 'cancelada' WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// Reabre compra fechada: estorna estoque/custo médio e volta para status aberta (editável).
    /// </summary>
    public static void Reopen(int id)
    {
        if (StoreNetworkMode.IsClient)
        {
            StoreNetworkClient.ReopenPurchase(id);
            return;
        }
        ReopenLocal(id);
    }

    public static void ReopenLocal(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("reabrir compra");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        var status = GetStatus(conn, tx, id)
            ?? throw new InvalidOperationException("Compra não encontrada.");

        if (status == "aberta")
            return;

        if (status == "cancelada")
            throw new InvalidOperationException("Compra cancelada não pode ser reaberta. Lance uma nova compra.");

        if (status != "fechada")
            throw new InvalidOperationException("Somente compras fechadas podem ser reabertas.");

        ReverseClosedPurchaseEffects(conn, tx, id);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE purchases SET status = 'aberta' WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        tx.Commit();

        AuditService.Log("compra_reabrir", "purchase", id.ToString(), "Reaberta para correção");
    }

    /// <summary>
    /// Estorno atômico de compra fechada: valida origem/estoque, depois aplica custo, global, lotes exatos e títulos.
    /// Compras anteriores à rastreabilidade (lot_origin_recorded=0) são bloqueadas.
    /// </summary>
    private static void ReverseClosedPurchaseEffects(
        SqliteConnection conn, SqliteTransaction tx, int purchaseId)
    {
        PayableService.ThrowIfPaidInstallmentsForPurchase(conn, tx, purchaseId);

        var lotOriginRecorded = 0;
        using (var meta = conn.CreateCommand())
        {
            meta.Transaction = tx;
            meta.CommandText = """
                SELECT IFNULL(lot_origin_recorded, 0)
                FROM purchases WHERE id = $id LIMIT 1;
                """;
            meta.Parameters.AddWithValue("$id", purchaseId);
            lotOriginRecorded = Convert.ToInt32(meta.ExecuteScalar() ?? 0);
        }

        if (lotOriginRecorded == 0)
            throw new InvalidOperationException(
                "Não é possível cancelar esta compra: ela foi lançada antes da rastreabilidade de lotes. A origem exata do estoque não está registrada. Ajuste estoque/lotes manualmente se necessário.");

        var items = LoadItemsForStock(conn, tx, purchaseId);
        var origins = LoadPurchaseItemLotsInTx(conn, tx, purchaseId);

        ValidateExactReverse(conn, tx, items, origins);

        ReversePurchaseCostEffects(conn, tx, items);
        ApplyStock(conn, tx, purchaseId, items, reverse: true);
        foreach (var origin in origins)
        {
            ProductLotService.DeductExact(
                conn, tx,
                origin.ProductId,
                origin.ProductLotId,
                origin.LotNumber,
                origin.ExpiryDate,
                origin.Quantity);
        }

        PayableService.RemoveUnpaidTitlesForPurchase(conn, tx, purchaseId);
    }

    private static void ValidateExactReverse(
        SqliteConnection conn,
        SqliteTransaction tx,
        List<PurchaseItemInput> items,
        IReadOnlyList<PurchaseItemLot> origins)
    {
        var needByLot = new Dictionary<int, (int ProductId, string Lot, DateTime? Exp, double Qty)>();
        foreach (var origin in origins)
        {
            var resolved = ProductLotService.ResolveExactLotId(
                conn, tx, origin.ProductId, origin.ProductLotId, origin.LotNumber, origin.ExpiryDate);
            if (!needByLot.TryGetValue(resolved, out var acc))
                acc = (origin.ProductId, origin.LotNumber, origin.ExpiryDate, 0);
            acc.Qty += origin.Quantity;
            needByLot[resolved] = acc;
        }

        foreach (var (lotId, acc) in needByLot)
        {
            using var lotCmd = conn.CreateCommand();
            lotCmd.Transaction = tx;
            lotCmd.CommandText = "SELECT quantity FROM product_lots WHERE id = $id LIMIT 1;";
            lotCmd.Parameters.AddWithValue("$id", lotId);
            var o = lotCmd.ExecuteScalar();
            if (o is null or DBNull)
                throw new InvalidOperationException(
                    "Não é possível cancelar: o lote originado por esta compra não foi encontrado. Não é seguro adivinhar outro lote.");
            var available = Convert.ToDouble(o);
            if (available + 1e-4 < acc.Qty)
            {
                var lot = string.IsNullOrWhiteSpace(acc.Lot) ? "(sem número)" : acc.Lot;
                throw new InvalidOperationException(
                    $"Não é possível cancelar: parte do estoque desta compra já foi vendida ou movimentada (lote {lot}: disponível {available:0.####}, origem {acc.Qty:0.####}).");
            }
        }

        var needByProduct = new Dictionary<int, double>();
        foreach (var item in items)
        {
            if (item.ProductId <= 0 || item.Quantity <= 0.0001)
                continue;
            needByProduct[item.ProductId] = needByProduct.GetValueOrDefault(item.ProductId) + item.Quantity;
        }

        foreach (var (productId, need) in needByProduct)
        {
            using var stockCmd = conn.CreateCommand();
            stockCmd.Transaction = tx;
            stockCmd.CommandText = """
                SELECT IFNULL(stock,0), IFNULL(stock_fridge,0)
                FROM products WHERE id = $id LIMIT 1;
                """;
            stockCmd.Parameters.AddWithValue("$id", productId);
            using var reader = stockCmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("Não é possível cancelar: produto da compra não foi encontrado.");
            var warehouse = reader.GetDouble(0);
            var fridge = reader.GetDouble(1);
            reader.Close();

            if (warehouse + 1e-4 >= need)
                continue;

            var total = warehouse + fridge;
            if (total + 1e-4 >= need)
                throw new InvalidOperationException(
                    "Não é possível cancelar esta compra porque parte da quantidade está na geladeira.\n\nRetorne a quantidade necessária da geladeira para o estoque antes de cancelar a compra.");

            throw new InvalidOperationException(
                $"Não é possível cancelar: estoque atual ({total:0.####}) é menor que a quantidade da compra ({need:0.####}). O estorno deixaria o estoque negativo.");
        }
    }

    private static List<PurchaseItemLot> LoadPurchaseItemLotsInTx(
        SqliteConnection conn, SqliteTransaction tx, int purchaseId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, purchase_item_id, purchase_id, product_id,
                   IFNULL(lot_number,''), expiry_date, quantity, product_lot_id, IFNULL(created_at,'')
            FROM purchase_item_lots
            WHERE purchase_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return ReadPurchaseItemLots(cmd).ToList();
    }

    /// <summary>
    /// Desfaz a média ponderada aplicada no finalize (best-effort se houve vendas depois).
    /// </summary>
    private static void ReversePurchaseCostEffects(
        SqliteConnection conn, SqliteTransaction tx, List<PurchaseItemInput> items)
    {
        foreach (var item in items)
        {
            using var get = conn.CreateCommand();
            get.Transaction = tx;
            get.CommandText = """
                SELECT IFNULL(stock,0), IFNULL(cost_price,0), IFNULL(name,''), IFNULL(group_name,''),
                       IFNULL(extra_json,'')
                FROM products WHERE id = $id LIMIT 1;
                """;
            get.Parameters.AddWithValue("$id", item.ProductId);
            using var reader = get.ExecuteReader();
            if (!reader.Read())
                continue;

            var stock = reader.GetDouble(0);
            var cost = reader.GetDouble(1);
            var name = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var group = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var extraJson = reader.IsDBNull(4) ? "" : reader.GetString(4);
            reader.Close();

            var extra = ProductExtra.Parse(extraJson);
            var packFactor = extra.FatorEmbalagem >= 1 ? extra.FatorEmbalagem : 1;
            var lineTotal = ProductPriceCalculator.RoundPrice(item.Quantity * item.UnitPrice);
            var lineCost = ProductPriceHelper.ResolveCatalogCost(
                item.UnitPrice, packFactor, name, group, lineTotal, item.Quantity);

            double qtyForAvg = item.Quantity;
            if (ProductClassificationHelper.UsesPackPurchasePrice(name, group))
            {
                var cigs = ProductPriceHelper.ResolveCigarettesPerPack(name, packFactor);
                if (cigs >= 2)
                    qtyForAvg = item.Quantity / cigs;
            }

            var restored = ProductPriceHelper.RemoveFromWeightedAverage(
                stock, cost, qtyForAvg, lineCost);

            extra.PrecoCompra = restored;

            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE products
                SET cost_price = $cost, extra_json = $extra
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$cost", restored);
            upd.Parameters.AddWithValue("$extra", extra.ToJson());
            upd.Parameters.AddWithValue("$id", item.ProductId);
            upd.ExecuteNonQuery();
        }
    }

    private sealed class SalePriceAuditPending
    {
        public int ProductId { get; init; }
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public double From { get; init; }
        public double To { get; init; }
    }

    /// <summary>
    /// Grava products.sale_price na mesma transação da compra, só nos itens
    /// com UpdateSalePrice. Não altera unit_price da NF nem a fórmula de custo.
    /// </summary>
    private static List<SalePriceAuditPending> ApplySalePricesInTx(
        SqliteConnection conn, SqliteTransaction tx, List<PurchaseItemInput> items)
    {
        TestBeforeApplySalePrice?.Invoke();
        var pending = new List<SalePriceAuditPending>();

        foreach (var item in items)
        {
            if (!item.UpdateSalePrice)
                continue;

            PurchaseSalePriceRules.RequireValidSalePrice(item.SalePrice);
            var requested = ProductPriceHelper.RoundPrice(item.SalePrice);

            using var get = conn.CreateCommand();
            get.Transaction = tx;
            get.CommandText = """
                SELECT IFNULL(code,''), IFNULL(name,''), IFNULL(group_name,''),
                       IFNULL(sale_price,0), IFNULL(cost_price,0), IFNULL(extra_json,'')
                FROM products WHERE id = $id LIMIT 1;
                """;
            get.Parameters.AddWithValue("$id", item.ProductId);

            string code;
            string name;
            string? group;
            double oldSale;
            double costPrice;
            string extraJson;
            using (var reader = get.ExecuteReader())
            {
                if (!reader.Read())
                    throw new InvalidOperationException(
                        $"Produto #{item.ProductId} não encontrado para atualizar o preço de venda.");
                code = reader.GetString(0);
                name = reader.GetString(1);
                group = reader.IsDBNull(2) ? "" : reader.GetString(2);
                oldSale = reader.GetDouble(3);
                costPrice = reader.GetDouble(4);
                extraJson = reader.IsDBNull(5) ? "" : reader.GetString(5);
            }

            var extra = ProductExtra.Parse(extraJson);
            ProductClassificationHelper.FillMissing(name, ref group, extra);

            var packFactor = extra.FatorEmbalagem > 1 ? extra.FatorEmbalagem
                : extra.QtdAtacado > 1 ? extra.QtdAtacado : 1;
            var isCigPack = ProductClassificationHelper.UsesPackPurchasePrice(name, group);
            var cigsPerPack = isCigPack
                ? ProductPriceHelper.ResolveCigarettesPerPack(name, packFactor)
                : packFactor;
            if (isCigPack && cigsPerPack >= 2)
                packFactor = cigsPerPack;

            var sale = ProductPriceHelper.ResolveCatalogSale(
                requested, item.UnitPrice, packFactor, name, group);
            PurchaseSalePriceRules.RequireValidSalePrice(sale);

            if (packFactor > 1 && sale > 0)
            {
                extra.PrecoAtacado = isCigPack
                    ? sale
                    : ProductPriceCalculator.RoundPrice(sale * packFactor);
            }

            if (sale > 0 && costPrice > 0)
                extra.LucroPercent = ProductPriceHelper.MarginOnSale(costPrice, sale);

            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE products
                SET sale_price = $sale, extra_json = $extra
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$sale", sale);
            upd.Parameters.AddWithValue("$extra", extra.ToJson());
            upd.Parameters.AddWithValue("$id", item.ProductId);
            if (upd.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException(
                    $"Falha ao gravar o preço de venda do produto #{item.ProductId}.");
            }

            pending.Add(new SalePriceAuditPending
            {
                ProductId = item.ProductId,
                Code = code,
                Name = name,
                From = oldSale,
                To = sale,
            });
        }

        TestAfterApplySalePrice?.Invoke();
        return pending;
    }

    private static void LogSalePriceAudits(int purchaseId, List<SalePriceAuditPending> pending)
    {
        foreach (var row in pending)
        {
            if (PurchaseSalePriceRules.SameMoney(row.From, row.To))
                continue;

            var changes = new Dictionary<string, object>
            {
                ["preco_venda"] = new { de = row.From, para = row.To },
            };
            AuditService.LogJson("alterar", "produto", row.ProductId.ToString(),
                AuditPayloadBuilder.ProductChange(
                    row.ProductId, row.Code, row.Name, changes, "compra", purchaseId),
                $"{row.Name}: preço R$ {row.From:N2} → R$ {row.To:N2}");
        }
    }

    private static void ValidateInput(PurchaseInput input)
    {
        if (input.SupplierId <= 0 && string.IsNullOrWhiteSpace(input.SupplierCnpj))
            throw new InvalidOperationException("Selecione o fornecedor.");
        if (string.IsNullOrWhiteSpace(input.Number))
            throw new InvalidOperationException("Informe o número da nota.");
        if (input.Items.Count == 0)
            throw new InvalidOperationException("Adicione ao menos um item à compra.");
        foreach (var item in input.Items)
        {
            if (item.ProductId <= 0)
                throw new InvalidOperationException("Item sem produto válido.");
            if (item.Quantity <= 0)
                throw new InvalidOperationException("Quantidade deve ser maior que zero.");
            if (item.UnitPrice < 0)
                throw new InvalidOperationException("Preço unitário inválido (use 0,00 para brinde/prêmio).");
            if (item.UpdateSalePrice)
                item.SalePrice = PurchaseSalePriceRules.NormalizeSalePrice(item.SalePrice);
        }
    }

    /// <summary>
    /// Prioriza CNPJ/CPF (estável entre note e loja). ID sozinho pode apontar
    /// para outra pessoa se o notebook tiver cadastro dessincronizado.
    /// </summary>
    private static int ResolveSupplierIdForSave(PurchaseInput input)
    {
        var digits = TextNorm.DigitsOnly(input.SupplierCnpj);
        if (!string.IsNullOrWhiteSpace(digits))
        {
            var byDoc = PersonService.FindByCnpjDigitsLocal(digits);
            if (byDoc is not null)
                return byDoc.Id;
        }

        if (input.SupplierId > 0)
        {
            var byId = PersonService.GetByIdLocal(input.SupplierId);
            if (byId is not null)
                return byId.Id;
        }

        throw new InvalidOperationException(
            "Fornecedor não encontrado na loja. Selecione o fornecedor de novo na lista (pelo CNPJ).");
    }

    private static string? GetStatus(SqliteConnection conn, SqliteTransaction tx, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT status FROM purchases WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() as string;
    }

    private static void InsertItems(SqliteConnection conn, SqliteTransaction tx, int purchaseId, List<PurchaseItemInput> items)
    {
        foreach (var item in items)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO purchase_items (purchase_id, product_id, product_name, quantity, unit_price, subtotal)
                VALUES ($pid, $product, $name, $qty, $price, $sub);
                SELECT last_insert_rowid();
                """;
            var sub = item.Quantity * item.UnitPrice;
            cmd.Parameters.AddWithValue("$pid", purchaseId);
            cmd.Parameters.AddWithValue("$product", item.ProductId);
            cmd.Parameters.AddWithValue("$name", item.ProductName);
            cmd.Parameters.AddWithValue("$qty", item.Quantity);
            cmd.Parameters.AddWithValue("$price", item.UnitPrice);
            cmd.Parameters.AddWithValue("$sub", sub);
            item.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    private static bool HasLotOrigin(PurchaseItemInput item) =>
        !string.IsNullOrWhiteSpace(item.LotNumber) || item.ExpiryDate is not null;

    private static void InsertPurchaseItemLot(
        SqliteConnection conn,
        SqliteTransaction tx,
        PurchaseItemInput item,
        int purchaseId,
        double physicalQty,
        int? productLotId)
    {
        if (item.Id <= 0 || purchaseId <= 0 || item.ProductId <= 0)
            return;

        TestBeforeInsertPurchaseItemLot?.Invoke();

        var lot = (item.LotNumber ?? "").Trim();
        var expiry = item.ExpiryDate?.Date.ToString("yyyy-MM-dd");

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO purchase_item_lots (
                purchase_item_id, purchase_id, product_id, lot_number, expiry_date,
                quantity, product_lot_id, created_at
            ) VALUES (
                $item, $purchase, $product, $lot, $exp,
                $qty, $lotid, datetime('now','localtime')
            );
            """;
        cmd.Parameters.AddWithValue("$item", item.Id);
        cmd.Parameters.AddWithValue("$purchase", purchaseId);
        cmd.Parameters.AddWithValue("$product", item.ProductId);
        cmd.Parameters.AddWithValue("$lot", lot);
        cmd.Parameters.AddWithValue("$exp", (object?)expiry ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$qty", Math.Round(physicalQty, 4));
        cmd.Parameters.AddWithValue("$lotid", productLotId is int lid and > 0 ? lid : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void ApplyStock(SqliteConnection conn, SqliteTransaction tx, int purchaseId, List<PurchaseItemInput> items, bool reverse)
    {
        string? number = null;
        string? nfeKey = null;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT IFNULL(number,''), IFNULL(nfe_key,'') FROM purchases WHERE id = $id LIMIT 1;";
            q.Parameters.AddWithValue("$id", purchaseId);
            using var reader = q.ExecuteReader();
            if (reader.Read())
            {
                number = reader.IsDBNull(0) ? "" : reader.GetString(0);
                nfeKey = reader.IsDBNull(1) ? "" : reader.GetString(1);
            }
        }

        var isNfe = !string.IsNullOrWhiteSpace(nfeKey);
        var op = reverse
            ? "estorno_compra"
            : isNfe ? "entrada_nfe" : "entrada_compra";
        var note = reverse
            ? $"Estorno compra #{purchaseId}" + (string.IsNullOrWhiteSpace(number) ? "" : $" NF {number}")
            : isNfe
                ? $"XML NF {number}"
                : $"Compra #{purchaseId}" + (string.IsNullOrWhiteSpace(number) ? "" : $" — NF {number}");

        foreach (var item in items)
        {
            using (var get = conn.CreateCommand())
            {
                get.Transaction = tx;
                get.CommandText = """
                    SELECT IFNULL(stock,0) + IFNULL(stock_fridge,0), IFNULL(cost_price,0), IFNULL(unit,'UN')
                    FROM products WHERE id = $id LIMIT 1;
                    """;
                get.Parameters.AddWithValue("$id", item.ProductId);
                using var reader = get.ExecuteReader();
                if (!reader.Read())
                    continue;
                var before = reader.GetDouble(0);
                var cost = reader.GetDouble(1);
                var unit = reader.IsDBNull(2) ? "UN" : reader.GetString(2);
                reader.Close();

                var sign = reverse ? -1 : 1;
                var delta = sign * item.Quantity;
                var after = before + delta;
                var movType = delta >= 0 ? "entrada" : "saida";
                var qty = Math.Abs(item.Quantity);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        UPDATE products
                        SET stock = stock + $delta
                        WHERE id = $id;
                        """;
                    cmd.Parameters.AddWithValue("$delta", delta);
                    cmd.Parameters.AddWithValue("$id", item.ProductId);
                    cmd.ExecuteNonQuery();
                }

                StockService.RegisterMovement(
                    conn, tx, item.ProductId, movType, qty, cost, note,
                    stockBefore: before, stockAfter: after,
                    operation: op, unit: unit,
                    refType: "purchase", refId: purchaseId);

                if (!reverse)
                {
                    var lotNote = note;
                    if (HasLotOrigin(item))
                    {
                        var productLotId = ProductLotService.Receive(conn, tx, new ProductLotReceiveInput
                        {
                            ProductId = item.ProductId,
                            Quantity = qty,
                            LotNumber = item.LotNumber,
                            ExpiryDate = item.ExpiryDate,
                            PurchaseId = purchaseId,
                            UnitCost = item.UnitPrice > 0 ? item.UnitPrice : cost,
                            Notes = lotNote,
                        });
                        InsertPurchaseItemLot(
                            conn, tx, item, purchaseId, qty, productLotId > 0 ? productLotId : null);
                    }
                }
                else
                {
                    // Estorno exato é aplicado em ReverseClosedPurchaseEffects (não FEFO).
                }
            }
        }
    }

    private static List<PurchaseItemInput> LoadItemsForStock(SqliteConnection conn, SqliteTransaction tx, int purchaseId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT product_id, product_name, quantity, unit_price
            FROM purchase_items WHERE purchase_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", purchaseId);
        var list = new List<PurchaseItemInput>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PurchaseItemInput
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                Quantity = reader.GetDouble(2),
                UnitPrice = reader.GetDouble(3),
            });
        }
        return list;
    }

    private static IReadOnlyList<PurchaseItem> LoadItems(SqliteConnection conn, int purchaseId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pi.id, pi.purchase_id, pi.product_id, IFNULL(pr.code,''), pi.product_name,
                   pi.quantity, pi.unit_price, pi.subtotal
            FROM purchase_items pi
            LEFT JOIN products pr ON pr.id = pi.product_id
            WHERE pi.purchase_id = $id
            ORDER BY pi.id;
            """;
        cmd.Parameters.AddWithValue("$id", purchaseId);
        var list = new List<PurchaseItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PurchaseItem
            {
                Id = reader.GetInt32(0),
                PurchaseId = reader.GetInt32(1),
                ProductId = reader.GetInt32(2),
                ProductCode = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ProductName = reader.GetString(4),
                Quantity = reader.GetDouble(5),
                UnitPrice = reader.GetDouble(6),
                Subtotal = reader.GetDouble(7),
            });
        }
        return list;
    }

    private static Purchase CopyWithItems(Purchase purchase, IReadOnlyList<PurchaseItem> items) => new()
    {
        Id = purchase.Id,
        SupplierId = purchase.SupplierId,
        SupplierName = purchase.SupplierName,
        SupplierCnpj = purchase.SupplierCnpj,
        SupplierState = purchase.SupplierState,
        EmissionDate = purchase.EmissionDate,
        EntryDate = purchase.EntryDate,
        Series = purchase.Series,
        Number = purchase.Number,
        NfeKey = purchase.NfeKey,
        Status = purchase.Status,
        Total = purchase.Total,
        GerarEstoque = purchase.GerarEstoque,
        Notes = purchase.Notes,
        CreatedAt = purchase.CreatedAt,
        Items = items,
    };

    private static Purchase ReadHeader(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        SupplierId = reader.GetInt32(1),
        SupplierName = reader.GetString(2),
        SupplierCnpj = reader.IsDBNull(3) ? null : reader.GetString(3),
        SupplierState = reader.IsDBNull(4) ? null : reader.GetString(4),
        EmissionDate = reader.GetString(5),
        EntryDate = reader.GetString(6),
        Series = reader.GetString(7),
        Number = reader.GetString(8),
        NfeKey = reader.IsDBNull(9) ? null : reader.GetString(9),
        Status = reader.GetString(10),
        Total = reader.GetDouble(11),
        GerarEstoque = reader.GetInt32(12) == 1,
        Notes = reader.IsDBNull(13) ? null : reader.GetString(13),
        CreatedAt = reader.GetString(14),
    };
}
