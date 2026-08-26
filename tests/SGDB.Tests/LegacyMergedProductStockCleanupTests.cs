using System.IO;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69T-E — saneamento de stock residual em ABSORB já unificado.
/// Bancos isolados (TempDatabase / .tmp); nunca AppData/deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class LegacyMergedProductStockCleanupTests
{
    private const string EanKeep = "7891000001001";
    private const string PackKeep = "7891000001023";
    private const string EanAbsorb = "7891000002002";
    private const string PackAbsorb = "7891000002023";

    private static TempDatabase Begin()
    {
        LegacyMergedProductStockCleanupService.TestBeforeWriteAudit = null;
        ProductService.TestBeforeApplyMergeCost = null;
        ProductService.TestAfterRemapProductIds = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void AbsorbComprovado_Positivo303_ZeraSemMexerKeep()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 288, keepCost: 3.30, keepSale: 5, keepCompra: 3.30,
            absorbStock: 303, absorbCost: 3.27,
            auditKeepBefore: -15, auditAbsorbBefore: 303, auditAfter: 288);

        var keepBefore = Snapshot(keep);
        var result = LegacyMergedProductStockCleanupService.Sanitize(absorb);

        Assert.False(result.AlreadyClean);
        Assert.Equal(0, ProductService.GetById(absorb)!.Stock, 4);
        Assert.Equal(0, ProductService.GetById(absorb)!.StockFridge, 4);
        AssertKeepUnchanged(keep, keepBefore);
        Assert.Equal(303, result.AbsorbStockBefore, 4);
        AssertSanitizeAudit(absorb, keep, 303, keepBefore.Stock, keepBefore.Cost);
    }

    [Fact]
    public void AbsorbNegativo_322_ZeraKeepIntacto()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 106, keepCost: 2.80, keepSale: 4, keepCompra: 4,
            absorbStock: -322, absorbCost: 2.63,
            auditKeepBefore: 69, auditAbsorbBefore: -322, auditAfter: -253);

        var keepBefore = Snapshot(keep);
        LegacyMergedProductStockCleanupService.Sanitize(absorb);
        Assert.Equal(0, ProductService.GetById(absorb)!.Stock, 4);
        AssertKeepUnchanged(keep, keepBefore);
    }

    [Fact]
    public void AbsorbPositivo_353_ZeraKeepIntacto()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 1843, keepCost: 10.12, keepSale: 11.5, keepCompra: 10.12,
            absorbStock: 353, absorbCost: 10.12,
            auditKeepBefore: 2000, auditAbsorbBefore: 353, auditAfter: 2353);

        var keepBefore = Snapshot(keep);
        LegacyMergedProductStockCleanupService.Sanitize(absorb);
        Assert.Equal(0, ProductService.GetById(absorb)!.Stock, 4);
        AssertKeepUnchanged(keep, keepBefore);
    }

    [Fact]
    public void JaZerado_Idempotente_NaoRegravaAudit()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 10, keepCost: 1, keepSale: 2, keepCompra: 1,
            absorbStock: 0, absorbCost: 1,
            auditKeepBefore: 5, auditAbsorbBefore: 8, auditAfter: 13);

        var keepBefore = Snapshot(keep);
        var first = LegacyMergedProductStockCleanupService.Sanitize(absorb);
        Assert.True(first.AlreadyClean);
        Assert.Equal(0, CountSanitizeAudits(absorb));

        var second = LegacyMergedProductStockCleanupService.Sanitize(absorb);
        Assert.True(second.AlreadyClean);
        Assert.Equal(0, CountSanitizeAudits(absorb));
        AssertKeepUnchanged(keep, keepBefore);
    }

    [Fact]
    public void InativoSemAudit_Bloqueia()
    {
        using var _ = Begin();
        var absorb = SeedProduct("H32", "HALLS MORANGO",  -120, 1.33, 2, active: false);
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMergedProductStockCleanupService.Sanitize(absorb));
        Assert.Equal(LegacyMergedProductStockCleanupService.InsufficientMessage, ex.Message);
        Assert.Equal(-120, ProductService.GetById(absorb)!.Stock, 4);
    }

    [Fact]
    public void ProdutoAtivo_Bloqueia()
    {
        using var _ = Begin();
        var keep = SeedProduct("K1", "KEEP ATIVO", 10, 1, 2, active: true, barcode: EanKeep);
        var absorb = SeedProduct("A1", "ABS ATIVO", 5, 1, 2, active: true, barcode: EanAbsorb);
        InsertLegacyAudit(absorb, keep, "ABS ATIVO", "KEEP ATIVO", 10, 5, 15);
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMergedProductStockCleanupService.Sanitize(absorb));
        Assert.Equal(LegacyMergedProductStockCleanupService.ConflictingMessage, ex.Message);
        Assert.True(ProductService.GetById(absorb)!.Active);
        Assert.Equal(5, ProductService.GetById(absorb)!.Stock, 4);
    }

    [Fact]
    public void AuditInsuficiente_Bloqueia()
    {
        using var _ = Begin();
        var keep = SeedProduct("K2", "KEEP", 10, 1, 2, active: true);
        var absorb = SeedProduct("A2", "ABS", 7, 1, 2, active: false);
        InsertRawAudit(keep, $"#{absorb} ABS mexeu com #{keep} KEEP sem composicao");
        var cand = LegacyMergedProductStockCleanupService.ListCandidates()
            .FirstOrDefault(c => c.AbsorbId == absorb);
        Assert.True(cand is null || cand.Kind == LegacyMergeEvidenceKind.Insuficiente);
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMergedProductStockCleanupService.Sanitize(absorb));
        Assert.Equal(LegacyMergedProductStockCleanupService.InsufficientMessage, ex.Message);
        Assert.Equal(7, ProductService.GetById(absorb)!.Stock, 4);
    }

    [Fact]
    public void SaldoConflitanteComAudit_Bloqueia()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 10, keepCost: 1, keepSale: 2, keepCompra: 1,
            absorbStock: 50, absorbCost: 1,
            auditKeepBefore: 10, auditAbsorbBefore: 303, auditAfter: 313);
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMergedProductStockCleanupService.Sanitize(absorb));
        Assert.Equal(LegacyMergedProductStockCleanupService.ConflictingMessage, ex.Message);
        Assert.Equal(50, ProductService.GetById(absorb)!.Stock, 4);
        Assert.Equal(10, ProductService.GetById(keep)!.Stock, 4);
    }

    [Fact]
    public void FalhaNoAudit_RollbackNaoZera()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 288, keepCost: 3.3, keepSale: 5, keepCompra: 3.3,
            absorbStock: 303, absorbCost: 3.27,
            auditKeepBefore: -15, auditAbsorbBefore: 303, auditAfter: 288);
        LegacyMergedProductStockCleanupService.TestBeforeWriteAudit =
            () => throw new InvalidOperationException("falha de audit simulada");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => LegacyMergedProductStockCleanupService.Sanitize(absorb));
            Assert.Equal("falha de audit simulada", ex.Message);
        }
        finally
        {
            LegacyMergedProductStockCleanupService.TestBeforeWriteAudit = null;
        }
        Assert.Equal(303, ProductService.GetById(absorb)!.Stock, 4);
        Assert.Equal(288, ProductService.GetById(keep)!.Stock, 4);
        Assert.Equal(0, CountSanitizeAudits(absorb));
    }

    [Fact]
    public void AliasesNfPdv_ContinuamKeep_AposSanear()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 100, keepCost: 2, keepSale: 4, keepCompra: 2,
            absorbStock: 12, absorbCost: 4,
            auditKeepBefore: 99, auditAbsorbBefore: 12, auditAfter: 111,
            jsonAudit: true);
        AttachAlias(keep, EanAbsorb, ProductBarcodeKinds.Alias, 1);
        AttachAlias(keep, PackAbsorb, ProductBarcodeKinds.Pack, 12);

        Assert.Equal(keep, ProductService.FindByBarcodeOrPack(EanAbsorb)!.Id);
        Assert.Equal(keep, NfeXmlImportService.ResolveExistingProduct(
            new NfeImportItem { Barcode = EanAbsorb })!.Id);
        Assert.Equal(keep, PdvService.ResolveExactBarcode(EanAbsorb)!.Product.Id);

        var keepBefore = Snapshot(keep);
        LegacyMergedProductStockCleanupService.Sanitize(absorb);

        Assert.Equal(0, ProductService.GetById(absorb)!.Stock, 4);
        AssertKeepUnchanged(keep, keepBefore);
        Assert.Equal(keep, ProductService.FindByBarcodeOrPack(EanAbsorb)!.Id);
        Assert.Equal(keep, ProductService.FindByBarcodeOrPack(PackAbsorb)!.Id);
        Assert.Equal(keep, NfeXmlImportService.ResolveExistingProduct(
            new NfeImportItem { Barcode = EanAbsorb, PackBarcode = PackAbsorb })!.Id);
        Assert.Equal(keep, PdvService.ResolveExactBarcode(EanAbsorb)!.Product.Id);
        Assert.Contains(ProductBarcodeService.ListActiveBarcodes(keep), b => b == EanAbsorb);
        Assert.DoesNotContain(ProductBarcodeService.ListActiveBarcodes(absorb), b => b == EanAbsorb);
    }

    [Fact]
    public void RelatoriosAtivos_NaoMudam()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 80, keepCost: 2, keepSale: 3, keepCompra: 2,
            absorbStock: 20, absorbCost: 2,
            auditKeepBefore: 60, auditAbsorbBefore: 20, auditAfter: 80);
        var before = ActiveReportSnapshot();
        LegacyMergedProductStockCleanupService.Sanitize(absorb);
        var after = ActiveReportSnapshot();
        Assert.Equal(before.Stock, after.Stock, 4);
        Assert.Equal(before.Valor, after.Valor, 2);
        Assert.Equal(before.NegativoRegs, after.NegativoRegs);
        Assert.Equal(before.MinimoRegs, after.MinimoRegs);
        Assert.Equal(before.DreCmv, after.DreCmv, 2);
        Assert.Equal(before.DreReceita, after.DreReceita, 2);
        Assert.Equal(before.OpenInventory, after.OpenInventory);
    }

    [Fact]
    public void ExecucaoRepetidaAposSanear_NaoAltera()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 40, keepCost: 1.5, keepSale: 3, keepCompra: 1.5,
            absorbStock: 9, absorbCost: 1.5,
            auditKeepBefore: 31, auditAbsorbBefore: 9, auditAfter: 40);
        LegacyMergedProductStockCleanupService.Sanitize(absorb);
        var keepAfter = Snapshot(keep);
        var audits = CountSanitizeAudits(absorb);
        var again = LegacyMergedProductStockCleanupService.Sanitize(absorb);
        Assert.True(again.AlreadyClean);
        AssertKeepUnchanged(keep, keepAfter);
        Assert.Equal(audits, CountSanitizeAudits(absorb));
        Assert.Equal(0, ProductService.GetById(absorb)!.Stock, 4);
    }

    [Fact]
    public void MultiplosAbsorbsDoMesmoKeep_Independentes()
    {
        using var _ = Begin();
        var keep = SeedProduct("OGV", "ORIGINAL KEEP", 111, 2.80, 4, active: true, barcode: EanKeep, compra: 4);
        var abs1 = SeedProduct("A128", "ORIGINAL VELHO", -322, 2.63, 4, active: false);
        var abs2 = SeedProduct("A538", "ONE WAY", 12, 4, 5.16, active: false);
        InsertLegacyAudit(abs1, keep, "ORIGINAL VELHO", "ORIGINAL KEEP", 69, -322, -253);
        InsertJsonAudit(abs2, keep, "ONE WAY", "ORIGINAL KEEP", 99, 12, 111, 2.66, 4, 2.80);

        var keepBefore = Snapshot(keep);
        var r1 = LegacyMergedProductStockCleanupService.Sanitize(abs1);
        AssertKeepUnchanged(keep, keepBefore);
        Assert.Equal(0, ProductService.GetById(abs1)!.Stock, 4);
        Assert.Equal(12, ProductService.GetById(abs2)!.Stock, 4);

        var r2 = LegacyMergedProductStockCleanupService.Sanitize(abs2);
        AssertKeepUnchanged(keep, keepBefore);
        Assert.Equal(0, ProductService.GetById(abs2)!.Stock, 4);
        Assert.False(r1.AlreadyClean);
        Assert.False(r2.AlreadyClean);
        Assert.Equal(2, CountSanitizeAudits(abs1) + CountSanitizeAudits(abs2));
    }

    [Fact]
    public void SanitizeAllProven_IgnoraSemMerge()
    {
        using var _ = Begin();
        var (keep, absorb) = SeedLeftover(
            keepStock: 10, keepCost: 1, keepSale: 2, keepCompra: 1,
            absorbStock: 3, absorbCost: 1,
            auditKeepBefore: 7, auditAbsorbBefore: 3, auditAfter: 10);
        var halls = SeedProduct("H32", "BALA HALLS MORANGO", -120, 1.33, 2, active: false);
        var results = LegacyMergedProductStockCleanupService.SanitizeAllProven();
        Assert.Contains(results, r => r.AbsorbId == absorb && !r.AlreadyClean);
        Assert.DoesNotContain(results, r => r.AbsorbId == halls);
        Assert.Equal(0, ProductService.GetById(absorb)!.Stock, 4);
        Assert.Equal(-120, ProductService.GetById(halls)!.Stock, 4);
    }

    private static (int Keep, int Absorb) SeedLeftover(
        double keepStock, double keepCost, double keepSale, double keepCompra,
        double absorbStock, double absorbCost,
        double auditKeepBefore, double auditAbsorbBefore, double auditAfter,
        bool jsonAudit = false)
    {
        var keep = SeedProduct("KEEP", "PRODUTO KEEP", keepStock, keepCost, keepSale,
            active: true, barcode: EanKeep, pack: PackKeep, compra: keepCompra);
        var absorb = SeedProduct("ABS", "PRODUTO ABSORB", absorbStock, absorbCost, keepSale,
            active: false);
        if (jsonAudit)
            InsertJsonAudit(absorb, keep, "PRODUTO ABSORB", "PRODUTO KEEP",
                auditKeepBefore, auditAbsorbBefore, auditAfter, keepCost, absorbCost, keepCost);
        else
            InsertLegacyAudit(absorb, keep, "PRODUTO ABSORB", "PRODUTO KEEP",
                auditKeepBefore, auditAbsorbBefore, auditAfter);
        return (keep, absorb);
    }

    private static int SeedProduct(
        string code, string name, double stock, double cost, double sale,
        bool active, string? barcode = null, string? pack = null, double compra = 0)
    {
        var extra = new ProductExtra
        {
            BarcodeEmbalagem = pack,
            FatorEmbalagem = string.IsNullOrWhiteSpace(pack) ? 1 : 12,
            PrecoCompra = compra > 0 ? compra : cost,
        };
        var id = ProductService.Create(new ProductInput
        {
            Code = code,
            Barcode = barcode,
            Name = name,
            GroupName = "GERAL",
            Unit = "UN",
            CostPrice = cost,
            SalePrice = sale,
            Stock = stock,
            Extra = extra,
            Active = true,
        }).Id;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE products
            SET stock = $s, cost_price = $c, sale_price = $sale, active = $a,
                barcode = CASE WHEN $a = 0 THEN NULL ELSE barcode END
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$s", stock);
        cmd.Parameters.AddWithValue("$c", cost);
        cmd.Parameters.AddWithValue("$sale", sale);
        cmd.Parameters.AddWithValue("$a", active ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void InsertLegacyAudit(
        int absorbId, int keepId, string absorbName, string keepName,
        double keepBefore, double absorbBefore, double after)
    {
        var details = $"#{absorbId} {absorbName} → #{keepId} {keepName} · estoque {Fmt(keepBefore)}+{Fmt(absorbBefore)}={Fmt(after)}";
        InsertRawAudit(keepId, details);
    }

    private static void InsertJsonAudit(
        int absorbId, int keepId, string absorbName, string keepName,
        double keepBefore, double absorbBefore, double after,
        double keepCost, double absorbCost, double costAfter)
    {
        var details = AuditPayloadBuilder.Serialize(
            $"#{absorbId} {absorbName} → #{keepId} {keepName} · custo R$ {keepCost:N2}/{absorbCost:N2} → R$ {costAfter:N2}",
            AuditPayloadBuilder.ProductMerge(
                keepId, absorbId, keepName, absorbName,
                keepBefore, 0, absorbBefore, 0, after, 0,
                keepCost, absorbCost, costAfter,
                keepCost, absorbCost, keepCost));
        InsertRawAudit(keepId, details);
    }

    private static void InsertRawAudit(int keepId, string details)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO audit_log (user_login, user_name, action, entity, entity_id, details)
            VALUES ('hg', 'DEPOSITO', 'unificar', 'produto', $eid, $d);
            """;
        cmd.Parameters.AddWithValue("$eid", keepId.ToString());
        cmd.Parameters.AddWithValue("$d", details);
        cmd.ExecuteNonQuery();
    }

    private static string Fmt(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-9)
            return ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return v.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AttachAlias(int keepId, string barcode, string kind, double factor)
    {
        using var conn = DatabaseService.OpenConnection();
        ProductBarcodeService.Upsert(conn, null, keepId, barcode, kind, factor, "test_alias");
    }

    private sealed record KeepSnap(double Stock, double Fridge, double Cost, double Sale, double PrecoCompra);

    private static KeepSnap Snapshot(int keepId)
    {
        var p = ProductService.GetById(keepId)!;
        return new KeepSnap(p.Stock, p.StockFridge, p.CostPrice, p.SalePrice,
            ProductExtra.Parse(p.ExtraJson).PrecoCompra);
    }

    private static void AssertKeepUnchanged(int keepId, KeepSnap before)
    {
        var after = Snapshot(keepId);
        Assert.Equal(before.Stock, after.Stock, 4);
        Assert.Equal(before.Fridge, after.Fridge, 4);
        Assert.Equal(before.Cost, after.Cost, 4);
        Assert.Equal(before.Sale, after.Sale, 4);
        Assert.Equal(before.PrecoCompra, after.PrecoCompra, 4);
    }

    private static void AssertSanitizeAudit(
        int absorbId, int keepId, double absorbBefore, double keepStock, double keepCost)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT details FROM audit_log
            WHERE action = $a AND entity = $e AND entity_id = $id
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$a", LegacyMergedProductStockCleanupService.AuditAction);
        cmd.Parameters.AddWithValue("$e", LegacyMergedProductStockCleanupService.AuditEntity);
        cmd.Parameters.AddWithValue("$id", absorbId.ToString());
        var details = cmd.ExecuteScalar()?.ToString() ?? "";
        Assert.True(AuditPayloadBuilder.TryParse(details, out var doc));
        Assert.Equal(LegacyMergedProductStockCleanupService.AuditOp, doc.Payload.GetProperty("op").GetString());
        Assert.Equal(absorbId, doc.Payload.GetProperty("absorb_id").GetInt32());
        Assert.Equal(keepId, doc.Payload.GetProperty("keep_id").GetInt32());
        Assert.Equal(absorbBefore, doc.Payload.GetProperty("stock_absorb_before").GetDouble(), 4);
        Assert.Equal(0, doc.Payload.GetProperty("stock_absorb_after").GetDouble(), 4);
        Assert.Equal(keepStock, doc.Payload.GetProperty("keep_stock_before").GetDouble(), 4);
        Assert.Equal(keepStock, doc.Payload.GetProperty("keep_stock_after").GetDouble(), 4);
        Assert.Equal(keepCost, doc.Payload.GetProperty("keep_cost_before").GetDouble(), 4);
        Assert.Equal(keepCost, doc.Payload.GetProperty("keep_cost_after").GetDouble(), 4);
    }

    private static int CountSanitizeAudits(int absorbId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM audit_log
            WHERE action = $a AND entity = $e AND entity_id = $id;
            """;
        cmd.Parameters.AddWithValue("$a", LegacyMergedProductStockCleanupService.AuditAction);
        cmd.Parameters.AddWithValue("$e", LegacyMergedProductStockCleanupService.AuditEntity);
        cmd.Parameters.AddWithValue("$id", absorbId.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private sealed record ReportSnap(
        double Stock, double Valor, int NegativoRegs, int MinimoRegs,
        double DreCmv, double DreReceita, bool OpenInventory);

    private static ReportSnap ActiveReportSnapshot()
    {
        var ativos = ProductService.List(ativo: "ativos");
        var stock = ativos.Sum(p => p.Stock + p.StockFridge);
        var valor = ativos.Sum(p => (p.Stock + p.StockFridge) * p.CostPrice);
        var neg = StockService.ListReport(StockReportKind.Negativo);
        var minRegs = ReportsService.ListEstoqueMinimo().Registros;
        var dre = DreService.GetDre();
        return new ReportSnap(
            stock, valor, neg.Registros, minRegs,
            dre.Cmv, dre.ReceitaLiquida, InventoryService.GetOpenSession() is not null);
    }
}

/// <summary>
/// Execução única no banco descartável da 69T-C2. Só roda com
/// SGDB_SANITIZE_EXECUCAO_TESTE=1. A suíte normal não altera esse arquivo.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class LegacyMergeCleanupExecucaoTesteLojaTests
{
    [Fact]
    public void SanearAbsorbsComprovados_BancoDescartavel()
    {
        if (Environment.GetEnvironmentVariable("SGDB_SANITIZE_EXECUCAO_TESTE") != "1")
            return;

        LegacyMergedProductStockCleanupService.TestBeforeWriteAudit = null;
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SGDB_TESTE_LOJA_NOTEBOOK", "execucao_teste", "deposito.db");
        Assert.True(File.Exists(path), path);
        DatabaseService.Initialize(path);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");

        var candidates = LegacyMergedProductStockCleanupService.ListCandidates();
        var proven = candidates.Where(c => c.Kind == LegacyMergeEvidenceKind.Comprovado).ToList();
        var blocked = candidates.Where(c => c.Kind != LegacyMergeEvidenceKind.Comprovado).ToList();
        Assert.Equal(22, proven.Count);
        Assert.Empty(blocked);
        Assert.DoesNotContain(candidates, c => c.AbsorbId is 32 or 10 or 353 or 5);

        var keepSnap = proven.Select(c => c.KeepId).Distinct()
            .ToDictionary(id => id, id => ProductService.GetById(id)!);

        var halls = ProductService.GetById(32)!.Stock;
        var mentos = ProductService.GetById(10)!.Stock;
        var vinho = ProductService.GetById(353)!.Stock;

        var results = LegacyMergedProductStockCleanupService.SanitizeAllProven();
        Assert.Equal(22, results.Count);

        foreach (var c in proven)
        {
            var absorb = ProductService.GetById(c.AbsorbId)!;
            Assert.False(absorb.Active);
            Assert.Equal(0, absorb.Stock, 4);
            Assert.Equal(0, absorb.StockFridge, 4);
            var keep = ProductService.GetById(c.KeepId)!;
            var before = keepSnap[c.KeepId];
            Assert.Equal(before.Stock, keep.Stock, 4);
            Assert.Equal(before.StockFridge, keep.StockFridge, 4);
            Assert.Equal(before.CostPrice, keep.CostPrice, 4);
            Assert.Equal(before.SalePrice, keep.SalePrice, 4);
            Assert.Equal(
                ProductExtra.Parse(before.ExtraJson).PrecoCompra,
                ProductExtra.Parse(keep.ExtraJson).PrecoCompra, 4);
        }

        Assert.Equal(halls, ProductService.GetById(32)!.Stock, 4);
        Assert.Equal(mentos, ProductService.GetById(10)!.Stock, 4);
        Assert.Equal(vinho, ProductService.GetById(353)!.Stock, 4);
    }
}
