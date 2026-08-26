using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69T-E — zera stock/stock_fridge de ABSORB já unificado, sem somar de novo no KEEP.
/// Só age com evidência inequívoca em audit_log de unificar/produto.
/// </summary>
public static class LegacyMergedProductStockCleanupService
{
    public const string AuditAction = "sanear_merge_legado";
    public const string AuditEntity = "produto";
    public const string AuditOp = "sanear_merge_legado";

    public const string ActiveMessage =
        "Não é possível sanear: o produto ainda está ativo.";
    public const string NotFoundMessage = "Produto não encontrado.";
    public const string InsufficientMessage =
        "Não é possível sanear: não há unificação comprovada para este produto.";
    public const string ConflictingMessage =
        "Não é possível sanear: o saldo residual não confere com o audit da unificação.";
    public const string KeepChangedMessage =
        "Abortado: o produto principal mudou durante o saneamento.";

    /// <summary>Somente testes: invocado após zerar o ABSORB e antes de gravar o audit.</summary>
    public static Action? TestBeforeWriteAudit { get; set; }

    private static readonly Regex LegacyText = new(
        @"#(\d+)\s+.+?\s+(?:\u2192|->)\s+#(\d+)\s+.+?·\s*estoque\s+(-?\d+(?:[.,]\d+)?)\+(-?\d+(?:[.,]\d+)?)=(-?\d+(?:[.,]\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static IReadOnlyList<LegacyMergeAbsorbCandidate> ListCandidates()
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("consultar merges legados");
        using var conn = DatabaseService.OpenConnection();
        return ListCandidates(conn);
    }

    public static LegacyMergeSanitizeResult Sanitize(int absorbId)
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("sanear merge legado");
        if (StoreNetworkMode.IsClient)
            throw new InvalidOperationException("Saneamento de merge legado só pode ser feito neste PC (não no cliente de rede).");
        if (absorbId <= 0)
            throw new InvalidOperationException(NotFoundMessage);

        using var conn = DatabaseService.OpenConnection();
        var candidate = ListCandidates(conn).FirstOrDefault(c => c.AbsorbId == absorbId)
            ?? throw new InvalidOperationException(InsufficientMessage);

        return SanitizeCandidate(conn, candidate);
    }

    public static IReadOnlyList<LegacyMergeSanitizeResult> SanitizeAllProven()
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("sanear merge legado");
        if (StoreNetworkMode.IsClient)
            throw new InvalidOperationException("Saneamento de merge legado só pode ser feito neste PC (não no cliente de rede).");

        using var conn = DatabaseService.OpenConnection();
        var proven = ListCandidates(conn)
            .Where(c => c.Kind == LegacyMergeEvidenceKind.Comprovado)
            .ToList();

        var results = new List<LegacyMergeSanitizeResult>();
        foreach (var candidate in proven)
            results.Add(SanitizeCandidate(conn, candidate));
        return results;
    }

    private static LegacyMergeSanitizeResult SanitizeCandidate(
        SqliteConnection conn, LegacyMergeAbsorbCandidate candidate)
    {
        if (candidate.Kind == LegacyMergeEvidenceKind.Insuficiente)
            throw new InvalidOperationException(InsufficientMessage);
        if (candidate.Kind == LegacyMergeEvidenceKind.Conflitante)
            throw new InvalidOperationException(ConflictingMessage);
        if (candidate.AbsorbActive)
            throw new InvalidOperationException(ActiveMessage);
        if (candidate.Kind != LegacyMergeEvidenceKind.Comprovado)
            throw new InvalidOperationException(InsufficientMessage);

        if (!candidate.HasResidual)
        {
            return new LegacyMergeSanitizeResult
            {
                AlreadyClean = true,
                AbsorbId = candidate.AbsorbId,
                KeepId = candidate.KeepId,
                AbsorbStockBefore = candidate.AbsorbStock,
                AbsorbFridgeBefore = candidate.AbsorbFridge,
            };
        }

        using var tx = conn.BeginTransaction();
        try
        {
            var absorb = LoadProduct(conn, tx, candidate.AbsorbId)
                ?? throw new InvalidOperationException(NotFoundMessage);
            var keep = LoadProduct(conn, tx, candidate.KeepId)
                ?? throw new InvalidOperationException(NotFoundMessage);

            if (absorb.Active)
                throw new InvalidOperationException(ActiveMessage);

            var keepCostBefore = keep.CostPrice;
            var keepStockBefore = keep.Stock;
            var keepFridgeBefore = keep.StockFridge;
            var keepSaleBefore = keep.SalePrice;
            var keepPrecoBefore = ProductExtra.Parse(keep.ExtraJson).PrecoCompra;
            var absorbStockBefore = absorb.Stock;
            var absorbFridgeBefore = absorb.StockFridge;

            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = """
                    UPDATE products
                    SET stock = 0, stock_fridge = 0
                    WHERE id = $id AND IFNULL(active, 1) = 0;
                    """;
                upd.Parameters.AddWithValue("$id", absorb.Id);
                if (upd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(ActiveMessage);
            }

            TestBeforeWriteAudit?.Invoke();

            var details = AuditPayloadBuilder.Serialize(
                $"#{absorb.Id} {absorb.Name} residual {absorbStockBefore:G}/{absorbFridgeBefore:G} → 0 (KEEP #{keep.Id} inalterado)",
                AuditPayloadBuilder.LegacyMergeSanitize(
                    absorb.Id, absorb.Name, keep.Id, keep.Name,
                    absorbStockBefore, absorbFridgeBefore,
                    keepStockBefore, keepStockBefore,
                    keepCostBefore, keepCostBefore,
                    candidate.MergeAuditId, candidate.MergedAt));

            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO audit_log (user_login, user_name, action, entity, entity_id, details)
                    VALUES ($login, $nome, $action, $entity, $eid, $details);
                    """;
                ins.Parameters.AddWithValue("$login", AppSession.UserLogin);
                ins.Parameters.AddWithValue("$nome", AppSession.CurrentUser?.Nome ?? "Sistema");
                ins.Parameters.AddWithValue("$action", AuditAction);
                ins.Parameters.AddWithValue("$entity", AuditEntity);
                ins.Parameters.AddWithValue("$eid", absorb.Id.ToString());
                ins.Parameters.AddWithValue("$details", details);
                ins.ExecuteNonQuery();
            }

            var keepAfter = LoadProduct(conn, tx, keep.Id)
                ?? throw new InvalidOperationException(KeepChangedMessage);
            if (!Same(keepAfter.Stock, keepStockBefore)
                || !Same(keepAfter.StockFridge, keepFridgeBefore)
                || !Same(keepAfter.CostPrice, keepCostBefore)
                || !Same(keepAfter.SalePrice, keepSaleBefore)
                || !Same(ProductExtra.Parse(keepAfter.ExtraJson).PrecoCompra, keepPrecoBefore))
                throw new InvalidOperationException(KeepChangedMessage);

            tx.Commit();
            return new LegacyMergeSanitizeResult
            {
                AlreadyClean = false,
                AbsorbId = absorb.Id,
                KeepId = keep.Id,
                AbsorbStockBefore = absorbStockBefore,
                AbsorbFridgeBefore = absorbFridgeBefore,
            };
        }
        catch
        {
            try { tx.Rollback(); } catch { /* already rolled back */ }
            throw;
        }
    }

    private static IReadOnlyList<LegacyMergeAbsorbCandidate> ListCandidates(SqliteConnection conn)
    {
        var parsed = new List<ParsedMergeAudit>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, created_at, IFNULL(user_login,''), IFNULL(user_name,''),
                       IFNULL(entity_id,''), IFNULL(details,'')
                FROM audit_log
                WHERE action = 'unificar' AND entity = 'produto'
                ORDER BY id;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var rec = TryParseMergeAudit(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5));
                if (rec is not null)
                    parsed.Add(rec);
            }
        }

        var byAbsorb = parsed
            .GroupBy(p => p.AbsorbId)
            .ToList();

        var list = new List<LegacyMergeAbsorbCandidate>();
        foreach (var group in byAbsorb)
        {
            var keepIds = group.Select(g => g.KeepId).Distinct().ToList();
            var latest = group.OrderByDescending(g => g.AuditId).First();
            var absorb = LoadProduct(conn, null, latest.AbsorbId);
            var keep = LoadProduct(conn, null, latest.KeepId);
            var hasMov = HasUnificacaoMovement(conn, latest.KeepId, latest.AbsorbId);

            LegacyMergeEvidenceKind kind;
            string reason;
            if (keepIds.Count > 1)
            {
                kind = LegacyMergeEvidenceKind.Conflitante;
                reason = "O mesmo ABSORB aparece em unificações com KEEP diferentes.";
            }
            else if (!latest.HasComposition)
            {
                kind = LegacyMergeEvidenceKind.Insuficiente;
                reason = "Audit de unificação sem composição de estoque KEEP+ABSORB.";
            }
            else if (absorb is null || keep is null)
            {
                kind = LegacyMergeEvidenceKind.Insuficiente;
                reason = absorb is null ? "ABSORB não encontrado." : "KEEP não encontrado.";
            }
            else if (absorb.Active)
            {
                kind = LegacyMergeEvidenceKind.Conflitante;
                reason = "O produto absorvido ainda está ativo.";
            }
            else if (!ResidualMatchesAudit(absorb, latest))
            {
                kind = LegacyMergeEvidenceKind.Conflitante;
                reason = "Saldo atual do ABSORB não confere com o estoque absorvido no audit.";
            }
            else
            {
                kind = LegacyMergeEvidenceKind.Comprovado;
                reason = "Unificação comprovada; residual é cópia não zerada.";
            }

            list.Add(new LegacyMergeAbsorbCandidate
            {
                AbsorbId = latest.AbsorbId,
                AbsorbName = absorb?.Name ?? latest.AbsorbName,
                KeepId = latest.KeepId,
                KeepName = keep?.Name ?? latest.KeepName,
                MergedAt = latest.CreatedAt,
                UserLogin = latest.UserLogin,
                UserName = latest.UserName,
                AbsorbStock = absorb?.Stock ?? 0,
                AbsorbFridge = absorb?.StockFridge ?? 0,
                AbsorbActive = absorb?.Active ?? false,
                KeepStock = keep?.Stock ?? 0,
                KeepFridge = keep?.StockFridge ?? 0,
                AuditKeepStockBefore = latest.KeepStockBefore,
                AuditAbsorbStockBefore = latest.AbsorbStockBefore,
                AuditStockAfter = latest.StockAfter,
                AuditAbsorbFridgeBefore = latest.AbsorbFridgeBefore,
                MergeAuditId = latest.AuditId,
                HasUnificacaoMovement = hasMov,
                Kind = kind,
                Reason = reason,
            });
        }

        return list
            .OrderByDescending(c => Math.Abs(c.AbsorbStock) + Math.Abs(c.AbsorbFridge))
            .ThenBy(c => c.AbsorbId)
            .ToList();
    }

    private static bool ResidualMatchesAudit(Product absorb, ParsedMergeAudit audit)
    {
        var stockOk = Same(absorb.Stock, 0) || Same(absorb.Stock, audit.AbsorbStockBefore ?? double.NaN);
        var expectedFridge = audit.AbsorbFridgeBefore ?? 0;
        var fridgeOk = Same(absorb.StockFridge, 0) || Same(absorb.StockFridge, expectedFridge);
        return stockOk && fridgeOk;
    }

    private static ParsedMergeAudit? TryParseMergeAudit(
        int auditId, string createdAt, string userLogin, string userName,
        string entityId, string details)
    {
        if (AuditPayloadBuilder.TryParse(details, out var doc)
            && doc.Payload.ValueKind == JsonValueKind.Object)
        {
            var p = doc.Payload;
            if (!TryInt(p, "absorb_id", out var absorbId) || !TryInt(p, "keep_id", out var keepId))
                return FromLegacyText(auditId, createdAt, userLogin, userName, entityId, details)
                    ?? FromLegacyText(auditId, createdAt, userLogin, userName, entityId, doc.Summary);
            TryDouble(p, "stock_keep_before", out var keepBefore);
            TryDouble(p, "stock_absorb_before", out var absorbBefore);
            TryDouble(p, "stock_after", out var after);
            TryDouble(p, "fridge_absorb_before", out var fridgeAbsorb);
            var hasComp = keepBefore is not null && absorbBefore is not null && after is not null
                && Same(keepBefore.Value + absorbBefore.Value, after.Value);
            return new ParsedMergeAudit
            {
                AuditId = auditId,
                CreatedAt = createdAt,
                UserLogin = userLogin,
                UserName = userName,
                AbsorbId = absorbId,
                KeepId = keepId,
                AbsorbName = TryString(p, "absorb_name"),
                KeepName = TryString(p, "keep_name"),
                KeepStockBefore = keepBefore,
                AbsorbStockBefore = absorbBefore,
                StockAfter = after,
                AbsorbFridgeBefore = fridgeAbsorb,
                HasComposition = hasComp,
            };
        }

        return FromLegacyText(auditId, createdAt, userLogin, userName, entityId, details);
    }

    private static ParsedMergeAudit? FromLegacyText(
        int auditId, string createdAt, string userLogin, string userName,
        string entityId, string details)
    {
        var m = LegacyText.Match(details ?? "");
        if (!m.Success)
            return null;
        var absorbId = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var keepId = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var keepBefore = ParseQty(m.Groups[3].Value);
        var absorbBefore = ParseQty(m.Groups[4].Value);
        var after = ParseQty(m.Groups[5].Value);
        if (int.TryParse(entityId, out var eid) && eid > 0 && eid != keepId)
            return null;
        return new ParsedMergeAudit
        {
            AuditId = auditId,
            CreatedAt = createdAt,
            UserLogin = userLogin,
            UserName = userName,
            AbsorbId = absorbId,
            KeepId = keepId,
            KeepStockBefore = keepBefore,
            AbsorbStockBefore = absorbBefore,
            StockAfter = after,
            AbsorbFridgeBefore = 0,
            HasComposition = Same(keepBefore + absorbBefore, after),
        };
    }

    private static bool HasUnificacaoMovement(SqliteConnection conn, int keepId, int absorbId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM movements
            WHERE product_id = $keep
              AND IFNULL(operation,'') = $op
              AND IFNULL(ref_type,'') = $rt
              AND IFNULL(ref_id, 0) = $abs
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$keep", keepId);
        cmd.Parameters.AddWithValue("$op", ProductMergeRules.MergeOperation);
        cmd.Parameters.AddWithValue("$rt", ProductMergeRules.MergeRefType);
        cmd.Parameters.AddWithValue("$abs", absorbId);
        return cmd.ExecuteScalar() is not null;
    }

    private static Product? LoadProduct(SqliteConnection conn, SqliteTransaction? tx, int id)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null)
            cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at,
                   IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return new Product
        {
            Id = reader.GetInt32(0),
            Code = reader.IsDBNull(1) ? null : reader.GetString(1),
            Barcode = reader.IsDBNull(2) ? null : reader.GetString(2),
            Name = reader.GetString(3),
            GroupName = reader.IsDBNull(4) ? null : reader.GetString(4),
            Unit = reader.IsDBNull(5) ? "UN" : reader.GetString(5),
            CostPrice = reader.GetDouble(6),
            SalePrice = reader.GetDouble(7),
            MinStock = reader.GetInt32(8),
            Stock = reader.GetDouble(9),
            Location = reader.IsDBNull(10) ? null : reader.GetString(10),
            ExtraJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11),
            Active = !reader.IsDBNull(12) && reader.GetInt32(12) != 0,
            CreatedAt = reader.IsDBNull(13) ? "" : reader.GetString(13),
            StockFridge = reader.GetDouble(14),
            StockFridgeMin = reader.GetInt32(15),
        };
    }

    private static bool TryInt(JsonElement p, string name, out int value)
    {
        value = 0;
        if (!p.TryGetProperty(name, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value))
            return true;
        return el.ValueKind == JsonValueKind.String
            && int.TryParse(el.GetString(), CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDouble(JsonElement p, string name, out double? value)
    {
        value = null;
        if (!p.TryGetProperty(name, out var el))
            return false;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
        {
            value = d;
            return true;
        }
        if (el.ValueKind == JsonValueKind.String && TryParseQty(el.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static string TryString(JsonElement p, string name) =>
        p.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";

    private static double ParseQty(string raw) =>
        TryParseQty(raw, out var v) ? v : 0;

    private static bool TryParseQty(string? raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-4;

    private sealed class ParsedMergeAudit
    {
        public int AuditId { get; init; }
        public string CreatedAt { get; init; } = "";
        public string UserLogin { get; init; } = "";
        public string UserName { get; init; } = "";
        public int AbsorbId { get; init; }
        public int KeepId { get; init; }
        public string AbsorbName { get; init; } = "";
        public string KeepName { get; init; } = "";
        public double? KeepStockBefore { get; init; }
        public double? AbsorbStockBefore { get; init; }
        public double? StockAfter { get; init; }
        public double? AbsorbFridgeBefore { get; init; }
        public bool HasComposition { get; init; }
    }
}
