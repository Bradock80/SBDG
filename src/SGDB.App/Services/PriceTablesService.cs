using System.Text.Json;
using Microsoft.Data.Sqlite;
using SGDB.Domain.Finance;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

public sealed class PriceTableInput
{
    public required string Description { get; init; }
    public double SurchargePercent { get; init; }
    public double SurchargeFixed { get; init; }
    public IReadOnlyList<string> ApplyPaymentMethods { get; init; } = ["debito", "credito", "pix"];
    public bool Active { get; init; } = true;
}

public static class PriceTablesService
{
    public static string[] AllMethods =>
        PaymentMethodsService.List().Select(m => m.Id).ToArray();

    public static readonly string[] DefaultMethods = ["debito", "credito", "pix"];

    public static IReadOnlyList<PriceTable> List(bool? onlyActive = null, bool includeProductCounts = true)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        var sql = """
            SELECT id, description, surcharge_percent, surcharge_fixed,
                   apply_payment_methods, active, created_at
            FROM price_tables
            """;
        if (onlyActive == true) sql += " WHERE active = 1";
        else if (onlyActive == false) sql += " WHERE active = 0";
        sql += " ORDER BY description;";
        cmd.CommandText = sql;
        var rows = ReadAll(cmd);
        if (includeProductCounts)
        {
            var counts = CountProductsByTable();
            foreach (var r in rows)
                r.ProductCount = counts.GetValueOrDefault(r.Id);
        }
        return rows;
    }

    public static PriceTable? GetById(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, description, surcharge_percent, surcharge_fixed,
                   apply_payment_methods, active, created_at
            FROM price_tables WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        return ReadAll(cmd).FirstOrDefault();
    }

    public static PriceTable Create(PriceTableInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("tabelas de preço");
        var desc = (input.Description ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(desc))
            throw new InvalidOperationException("Informe a descrição da tabela.");

        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM price_tables WHERE UPPER(description) = $d;";
            check.Parameters.AddWithValue("$d", desc);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe uma tabela com esta descrição.");
        }

        var methods = NormalizeMethods(input.ApplyPaymentMethods);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO price_tables (
              description, surcharge_percent, surcharge_fixed,
              apply_on_card_only, apply_payment_methods, active)
            VALUES ($desc, $pct, $fix, $card, $methods, $active);
            SELECT last_insert_rowid();
            """;
        Bind(cmd, desc, input, methods);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetById(id)!;
    }

    public static PriceTable Update(int id, PriceTableInput input)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("tabelas de preço");
        _ = GetById(id) ?? throw new InvalidOperationException("Tabela de preço não encontrada.");
        var desc = (input.Description ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(desc))
            throw new InvalidOperationException("Informe a descrição da tabela.");

        using var conn = DatabaseService.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(1) FROM price_tables WHERE UPPER(description) = $d AND id <> $id;";
            check.Parameters.AddWithValue("$d", desc);
            check.Parameters.AddWithValue("$id", id);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("Já existe uma tabela com esta descrição.");
        }

        var methods = NormalizeMethods(input.ApplyPaymentMethods);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE price_tables SET
              description = $desc, surcharge_percent = $pct, surcharge_fixed = $fix,
              apply_on_card_only = $card, apply_payment_methods = $methods, active = $active
            WHERE id = $id;
            """;
        Bind(cmd, desc, input, methods);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return GetById(id)!;
    }

    public static void Delete(int id)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("tabelas de preço");
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM price_tables WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Tabela de preço não encontrada.");
    }

    public static IReadOnlyList<(string Code, string Name, bool Active)> ListProductsForTable(int tableId)
    {
        var list = new List<(string, string, bool)>();
        try
        {
            using var conn = DatabaseService.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT IFNULL(code,''), name, active, IFNULL(extra_json,'') FROM products ORDER BY name;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var extra = ProductExtra.Parse(reader.IsDBNull(3) ? null : reader.GetString(3));
                if (extra.PriceTableId != tableId)
                    continue;
                list.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0));
            }
        }
        catch
        {
            // ignore
        }
        return list;
    }

    public static PriceTable? ResolveForProduct(ProductExtra extra)
    {
        if (extra.PriceTableId is null or <= 0)
            return null;
        var table = GetById(extra.PriceTableId.Value);
        return table is { Active: true } ? table : null;
    }

    public static double CalcUnitSurcharge(double basePrice, PriceTable? table, string paymentMethodId)
    {
        if (table is null)
            return 0;
        var method = (paymentMethodId ?? "dinheiro").Trim().ToLowerInvariant();
        if (!MethodTriggersTable(table, method))
            return 0;
        return FinancialCalculator.CalculateUnitSurcharge(
            basePrice, table.SurchargePercent, table.SurchargeFixed);
    }

    public static bool MethodTriggersTable(PriceTable? table, string methodId)
    {
        if (table is null)
            return false;
        var method = NormalizeMethodIdForTable(methodId);
        if (method.Length == 0)
            return false;

        // Só a forma exatamente marcada na tabela dispara acréscimo.
        // PIX QR ("pix") e PIX Chave ("pix_chave") são independentes.
        return table.ApplyPaymentMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Normaliza id/rótulo de pagamento para o id usado em apply_payment_methods.</summary>
    private static string NormalizeMethodIdForTable(string? methodId)
    {
        var raw = (methodId ?? "").Trim();
        if (raw.Length == 0)
            return "";
        var asId = ApiLabelToMethodId(raw);
        return string.IsNullOrWhiteSpace(asId) ? raw.ToLowerInvariant() : asId.ToLowerInvariant();
    }

    /// <summary>
    /// Soma acréscimo das linhas do carrinho para as formas usadas no pagamento.
    /// Se várias formas, aplica se QUALQUER forma da venda estiver na tabela.
    /// </summary>
    public static double CalcCartSurcharge(
        IEnumerable<(int ProductId, double UnitPrice, double Qty)> lines,
        IEnumerable<string> paymentApiLabels)
    {
        var methodIds = paymentApiLabels
            .Select(ApiLabelToMethodId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return CalcCartSurchargeByMethodIds(lines, methodIds);
    }

    public static double CalcCartSurchargeByMethodIds(
        IEnumerable<(int ProductId, double UnitPrice, double Qty)> lines,
        IEnumerable<string> methodIds)
    {
        var ids = methodIds
            .Select(m => (m ?? "").Trim().ToLowerInvariant())
            .Where(m => m.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
            return 0;

        // Compat: trata como se cada forma tivesse o total (comportamento antigo “qualquer forma”).
        var amounts = ids.ToDictionary(id => id, _ => 1.0, StringComparer.OrdinalIgnoreCase);
        return CalcCartSurchargeAllocated(lines, amounts);
    }

    /// <summary>
    /// Acréscimo no pagamento misto:
    /// Só entra produto cuja tabela é disparada por ALGUMA forma usada na venda
    /// (ex.: cerveja só débito/crédito → PIX não gera acréscimo na cerveja).
    /// Dinheiro/formas “soft” cobrem primeiro esses produtos; se cobriu 100% → R$ 0.
    /// </summary>
    public static double CalcCartSurchargeAllocated(
        IEnumerable<(int ProductId, double UnitPrice, double Qty)> lines,
        IReadOnlyDictionary<string, double> paymentAmountsByMethodId)
    {
        var payments = paymentAmountsByMethodId
            .Where(kv => kv.Value > 0.009)
            .Select(kv => (Method: NormalizeMethodIdForTable(kv.Key), Amount: kv.Value))
            .Where(p => p.Method.Length > 0)
            .ToList();
        if (payments.Count == 0)
            return 0;

        var lineInfos = new List<PremiumLine>();
        double normalBase = 0;

        foreach (var (productId, unitPrice, qty) in lines)
        {
            if (productId <= 0 || qty <= 0)
                continue;
            var baseAmt = ProductPriceHelper.RoundPrice(unitPrice * qty);
            if (baseAmt <= 0.009)
                continue;

            var product = ProductService.GetById(productId);
            if (product is null)
            {
                normalBase = ProductPriceHelper.RoundPrice(normalBase + baseAmt);
                continue;
            }

            var extra = ProductExtra.Parse(product.ExtraJson);
            var table = ResolveForProduct(extra);
            if (table is null)
            {
                normalBase = ProductPriceHelper.RoundPrice(normalBase + baseAmt);
                continue;
            }

            // Sem forma na venda que dispare ESTA tabela → sem acréscimo neste produto
            // (ex.: tabela CERVEJA só débito/crédito + pagamento em PIX/dinheiro).
            if (!payments.Any(p => MethodTriggersTable(table, p.Method)))
            {
                normalBase = ProductPriceHelper.RoundPrice(normalBase + baseAmt);
                continue;
            }

            var fullUnit = 0.0;
            foreach (var mid in table.ApplyPaymentMethods)
                fullUnit = Math.Max(fullUnit, CalcUnitSurcharge(unitPrice, table, mid));
            var fullSur = ProductPriceHelper.RoundPrice(fullUnit * qty);
            if (fullSur <= 0.009)
            {
                normalBase = ProductPriceHelper.RoundPrice(normalBase + baseAmt);
                continue;
            }

            lineInfos.Add(new PremiumLine(baseAmt, fullSur, table));
        }

        if (lineInfos.Count == 0)
            return 0;

        // Soft = forma que NÃO dispara nenhuma das linhas que ainda podem ter acréscimo
        double softPool = 0;
        foreach (var (method, amount) in payments)
        {
            var triggersAny = lineInfos.Any(l => MethodTriggersTable(l.Table, method));
            if (!triggersAny)
                softPool = ProductPriceHelper.RoundPrice(softPool + amount);
        }

        var softLeft = softPool;
        foreach (var line in lineInfos)
        {
            var cover = Math.Min(softLeft, line.BaseAmount);
            line.SoftCover = cover;
            softLeft = ProductPriceHelper.RoundPrice(softLeft - cover);
        }

        _ = ProductPriceHelper.RoundPrice(Math.Max(0, softLeft - normalBase));

        double total = 0;
        foreach (var line in lineInfos)
        {
            var coveredFullyBySoft = line.SoftCover + 0.02 >= line.BaseAmount;
            if (!coveredFullyBySoft)
                total += line.FullSurcharge;
        }

        return ProductPriceHelper.RoundPrice(total);
    }

    private sealed class PremiumLine(double baseAmount, double fullSurcharge, PriceTable table)
    {
        public double BaseAmount { get; } = baseAmount;
        public double FullSurcharge { get; } = fullSurcharge;
        public PriceTable Table { get; } = table;
        public double SoftCover { get; set; }
    }

    public static string ApiLabelToMethodId(string? apiLabel)
    {
        var s = (apiLabel ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return "dinheiro";

        // Match exato pelo rótulo/id (evita "PIX QR CODE" virar "pix")
        var hit = PaymentMethodsService.List().FirstOrDefault(m =>
            m.ApiLabel.Equals(s, StringComparison.OrdinalIgnoreCase)
            || m.Id.Equals(s, StringComparison.OrdinalIgnoreCase)
            || m.Name.Equals(s, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
            return hit.Id;

        var low = s.ToLowerInvariant();
        return low switch
        {
            "dinheiro" or "cash" or "a" => "dinheiro",
            "pix" or "d" or "pix qr code" or "pix qr" => "pix",
            "pix chave" or "pix_chave" or "pixchave" or "f" => "pix_chave",
            "cartão débito" or "cartao debito" or "debito" or "b" => "debito",
            "cartão crédito" or "cartao credito" or "credito" or "c" => "credito",
            "fiado" or "à prazo" or "a prazo" or "e" => "fiado",
            _ when low.Contains("chave") && low.Contains("pix") => "pix_chave",
            _ when low.Contains("pix") => "pix",
            _ => low,
        };
    }

    private static List<string> NormalizeMethods(IReadOnlyList<string>? methods)
    {
        var known = new HashSet<string>(AllMethods, StringComparer.OrdinalIgnoreCase);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in methods ?? DefaultMethods)
        {
            var id = (m ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id))
                continue;
            // Aceita ids do catálogo atual e também ids legados ainda salvos na tabela.
            if (known.Contains(id) || id is "dinheiro" or "debito" or "credito" or "pix" or "pix_chave" or "fiado")
                set.Add(id);
            else if (id.StartsWith("custom_", StringComparison.Ordinal))
                set.Add(id);
        }
        return set.Count > 0 ? set.ToList() : DefaultMethods.ToList();
    }

    private static void Bind(SqliteCommand cmd, string desc, PriceTableInput input, List<string> methods)
    {
        var cardOnly = methods.Count == 3
                       && methods.Contains("debito")
                       && methods.Contains("credito")
                       && methods.Contains("pix");
        cmd.Parameters.AddWithValue("$desc", desc[..Math.Min(80, desc.Length)]);
        cmd.Parameters.AddWithValue("$pct", Math.Round(Math.Clamp(input.SurchargePercent, 0, 100), 4));
        cmd.Parameters.AddWithValue("$fix", ProductPriceCalculator.RoundPrice(Math.Max(0, input.SurchargeFixed)));
        cmd.Parameters.AddWithValue("$card", cardOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("$methods", JsonSerializer.Serialize(methods));
        cmd.Parameters.AddWithValue("$active", input.Active ? 1 : 0);
    }

    private static Dictionary<int, int> CountProductsByTable()
    {
        var map = new Dictionary<int, int>();
        try
        {
            using var conn = DatabaseService.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, IFNULL(extra_json,'') FROM products WHERE active = 1;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var extra = ProductExtra.Parse(reader.IsDBNull(1) ? null : reader.GetString(1));
                if (extra.PriceTableId is > 0)
                    map[extra.PriceTableId.Value] = map.GetValueOrDefault(extra.PriceTableId.Value) + 1;
            }
        }
        catch
        {
            // ignore
        }
        return map;
    }

    private static List<PriceTable> ReadAll(SqliteCommand cmd)
    {
        var list = new List<PriceTable>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PriceTable
            {
                Id = reader.GetInt32(0),
                Description = reader.GetString(1),
                SurchargePercent = reader.GetDouble(2),
                SurchargeFixed = reader.GetDouble(3),
                ApplyPaymentMethods = ParseMethods(reader.IsDBNull(4) ? null : reader.GetString(4)),
                Active = reader.GetInt32(5) != 0,
                CreatedAt = reader.IsDBNull(6) ? "" : reader.GetString(6),
            });
        }
        return list;
    }

    private static List<string> ParseMethods(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return DefaultMethods.ToList();
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list is null || list.Count == 0)
                return DefaultMethods.ToList();

            // Não chamar NormalizeMethods aqui: ele acessa PaymentMethodsService.List()
            // (abre outra conexão / pode gravar) enquanto o reader de price_tables
            // ainda está aberto — no SQLite isso trava o app (“não responde”).
            return list
                .Select(m => (m ?? "").Trim().ToLowerInvariant())
                .Where(m => m.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return DefaultMethods.ToList();
        }
    }
}
