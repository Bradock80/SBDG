using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>ETAPA 69E-B2 — DRE, dashboard, ranking, PDV e fechamento usam cost_at_sale.</summary>
[Collection(TempDatabaseCollection.Name)]
public class HistoricalSaleCostReportTests
{
    private static TempDatabase BeginDb()
    {
        PdvService.TestBeforeInsertSaleItems = null;
        PdvService.TestAfterInsertSaleItems = null;
        PdvService.TestAfterSwapItemUpdate = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(80, "cmv-hist");
        return db;
    }

    private static DateTime Today => DateTime.Today;

    [Fact]
    public void Dre_VendaCusto5_Cadastro6_Continua5()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "D5", "DRE 5");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetCost(pid, 6);
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(5, dre.Cmv);
        Assert.Equal(5, dre.CmvHistorico);
        Assert.Equal(0, dre.CmvEstimado);
        Assert.False(dre.HasEstimatedLegacyCost);
        Assert.True(dre.CmvUsesHistoricalSnapshot);
        Assert.Null(dre.CmvReliabilityNote);
    }

    [Fact]
    public void Dre_NovaVendaCusto6_Usa6()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "D6", "DRE 6");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetCost(pid, 6);
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(11, dre.Cmv);
        Assert.Equal(11, dre.CmvHistorico);
        Assert.Equal(0, dre.CmvEstimado);
        Assert.False(dre.HasEstimatedLegacyCost);
    }

    [Fact]
    public void Dre_Cancelada_Exclui()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "DC", "DRE CAN");
        var keep = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        var cancel = TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        PdvService.CancelSale(cancel.SaleId);
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(5, dre.Cmv);
        Assert.Equal(1, dre.QtdVendas);
        Assert.Equal(1, dre.QtdCanceladas);
        Assert.Equal(keep.SaleId, keep.SaleId);
    }

    [Fact]
    public void Dre_MixSnapshotELegado_ComAviso()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "MX", "MIX");
        InsertLegacySale(pid, 1, 8);
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetCost(pid, 9);
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(5, dre.CmvHistorico);
        Assert.Equal(9, dre.CmvEstimado);
        Assert.Equal(14, dre.Cmv);
        Assert.True(dre.HasEstimatedLegacyCost);
        Assert.True(dre.ProfitIsEstimated);
        Assert.True(dre.MarginIsEstimated);
        Assert.Equal(HistoricalSaleCostRules.EstimatedLegacyPeriodNote, dre.CmvReliabilityNote);
        Assert.Contains(dre.CascadeLines, l => l.Label.Contains("estimada", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dre.CascadeLines, l => l.Label.Contains("histórico confiável", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dre_SomenteSnapshot_SemAviso()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "SS", "SO SNAP");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        var dre = DreService.GetDre(Today, Today);
        Assert.False(dre.HasEstimatedLegacyCost);
        Assert.DoesNotContain(dre.CascadeLines, l =>
            l.Label.Contains("estimada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dre_SomenteLegado_ComAviso()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "LG", "SO LEG");
        InsertLegacySale(pid, 2, 8);
        var dre = DreService.GetDre(Today, Today);
        Assert.True(dre.HasEstimatedLegacyCost);
        Assert.Equal(10, dre.CmvEstimado);
        Assert.Equal(0, dre.CmvHistorico);
        Assert.Equal(10, dre.Cmv);
        Assert.Contains(dre.CascadeLines, l => l.Label.Contains("estimada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dre_CustoZeroSnapshot()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(10, 8, 0, "Z", "BRINDE");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetCost(pid, 9);
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(0, dre.Cmv);
        Assert.False(dre.HasEstimatedLegacyCost);
        Assert.True(dre.CmvUsesHistoricalSnapshot);
    }

    [Fact]
    public void Dre_CigarroAvulso_NaoConverteDuasVezes()
    {
        using var _ = BeginDb();
        var cig = SeedCig(200, 10, 20);
        FinalizeCig(cig, PdvCigaretteSaleMode.Avulso, 5);
        SetCost(cig, 20);
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(2.50, dre.Cmv);
        Assert.False(dre.HasEstimatedLegacyCost);
    }

    [Fact]
    public void Dre_CigarroMaco_Snapshot10x2()
    {
        using var _ = BeginDb();
        var cig = SeedCig(200, 10, 20);
        FinalizeCig(cig, PdvCigaretteSaleMode.Maco, 2);
        SetCost(cig, 99);
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(20, dre.Cmv);
    }

    [Fact]
    public void Dre_Pack_120x2()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(100, 8, 5, "PK", "FARDO 24");
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [new PdvCartLine { ProductId = pid, Quantity = 2, UnitPrice = 120, StockUnitsPerSale = 24 }],
            PaymentType = "Dinheiro",
            CashReceived = 240,
        });
        SetCost(pid, 9);
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(240, dre.Cmv);
    }

    [Fact]
    public void Dre_Fracionado_2e5x10()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 12, 10, "KG", "AÇÚCAR KG");
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [new PdvCartLine { ProductId = pid, Quantity = 2.5, UnitPrice = 12, Unit = "KG" }],
            PaymentType = "Dinheiro",
            CashReceived = 30,
        });
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(25, dre.Cmv);
        Assert.Equal(30, dre.ReceitaLiquida);
        Assert.Equal(5, dre.LucroBruto);
    }

    [Fact]
    public void Dre_DescontoNaoAlteraCmv()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "DS", "DESC");
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [new PdvCartLine { ProductId = pid, Quantity = 2, UnitPrice = 8 }],
            PaymentType = "Pix",
            Discount = 1,
            Surcharge = 0.50,
            CashReceived = 0,
        });
        var dre = DreService.GetDre(Today, Today);
        Assert.Equal(10, dre.Cmv);
        Assert.Equal(ProductPriceHelper.RoundPrice(16 - 1 + 0.50), dre.ReceitaLiquida);
        Assert.Equal(ProductPriceHelper.RoundPrice(dre.ReceitaLiquida - 10), dre.LucroBruto);
    }

    [Fact]
    public void Dashboard_MesmoCmvQueDre()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "EQ", "EQ CMV");
        TestDataHelper.FinalizeSimpleCashSale(pid, 2, 8, 16);
        InsertLegacySale(pid, 1, 8);
        SetCost(pid, 9);
        var dre = DreService.GetDre(Today, Today);
        var dash = BusinessDashboardService.GetDashboardLocal(Today, Today, "session");
        Assert.Equal(dre.Cmv, dash.Cmv);
        Assert.Equal(dre.HasEstimatedLegacyCost, dash.HasEstimatedLegacyCost);
        Assert.Equal(dre.CmvHistorico, dash.CmvHistorico);
        Assert.Equal(dre.CmvEstimado, dash.CmvEstimado);
        Assert.True(dash.CmvUsesHistoricalSnapshot);
    }

    [Fact]
    public void CompraPosterior_NaoMudaDrePassado()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(0, 8, 5, "CP", "COMPRA DEPOIS");
        var supplier = SeedSupplier();
        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = Today.ToString("yyyy-MM-dd"),
            EntryDate = Today.ToString("yyyy-MM-dd"),
            Number = "NF-B2-1",
            GerarEstoque = true,
            Items = [new PurchaseItemInput { ProductId = pid, ProductName = "COMPRA DEPOIS", Quantity = 10, UnitPrice = 5, SalePrice = 8 }],
        }, closeOnSave: true);
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        var dreAntes = DreService.GetDre(Today, Today).Cmv;
        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = Today.ToString("yyyy-MM-dd"),
            EntryDate = Today.ToString("yyyy-MM-dd"),
            Number = "NF-B2-2",
            GerarEstoque = true,
            Items = [new PurchaseItemInput { ProductId = pid, ProductName = "COMPRA DEPOIS", Quantity = 10, UnitPrice = 9, SalePrice = 8 }],
        }, closeOnSave: true);
        Assert.Equal(dreAntes, DreService.GetDre(Today, Today).Cmv);
        Assert.Equal(dreAntes, BusinessDashboardService.GetDashboardLocal(Today, Today).Cmv);
    }

    [Fact]
    public void CancelCompraPosterior_ContinuaSnapshot()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(0, 8, 5, "CC", "CANCEL COMPRA");
        var supplier = SeedSupplier();
        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = Today.ToString("yyyy-MM-dd"),
            EntryDate = Today.ToString("yyyy-MM-dd"),
            Number = "NF-B2-A",
            GerarEstoque = true,
            Items = [new PurchaseItemInput { ProductId = pid, ProductName = "CANCEL COMPRA", Quantity = 10, UnitPrice = 5, SalePrice = 8 }],
        }, closeOnSave: true);
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        var second = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = Today.ToString("yyyy-MM-dd"),
            EntryDate = Today.ToString("yyyy-MM-dd"),
            Number = "NF-B2-B",
            GerarEstoque = true,
            Items = [new PurchaseItemInput { ProductId = pid, ProductName = "CANCEL COMPRA", Quantity = 10, UnitPrice = 9, SalePrice = 8 }],
        }, closeOnSave: true);
        Assert.Equal(5, DreService.GetDre(Today, Today).Cmv);
        PurchaseService.Cancel(second);
        Assert.Equal(5, DreService.GetDre(Today, Today).Cmv);
    }

    [Fact]
    public void CustoManual_NaoMudaPassado()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "CM", "MANUAL");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        SetCost(pid, 9);
        Assert.Equal(5, DreService.GetDre(Today, Today).Cmv);
        Assert.Equal(5, BusinessDashboardService.GetDashboardLocal(Today, Today).Cmv);
    }

    [Fact]
    public void Merge_NaoMudaCmvPassado()
    {
        using var _ = BeginDb();
        var keep = TestDataHelper.SeedSimpleProduct(20, 8, 7, "MK", "KEEP");
        var absorb = TestDataHelper.SeedSimpleProduct(20, 8, 5, "MA", "ABS");
        TestDataHelper.FinalizeSimpleCashSale(absorb, 1, 8, 8);
        ProductService.MergeProducts(keep, absorb);
        Assert.Equal(5, DreService.GetDre(Today, Today).Cmv);
        Assert.Equal(5, BusinessDashboardService.GetDashboardLocal(Today, Today).Cmv);
    }

    [Fact]
    public void Swap_UsaSnapshotResultante()
    {
        using var _ = BeginDb();
        var a = TestDataHelper.SeedSimpleProduct(20, 8, 5, "SWA", "SWAP A");
        var b = TestDataHelper.SeedSimpleProduct(20, 12, 7, "SWB", "SWAP B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 8, 8);
        var itemId = ReadItemId(sale.SaleId);
        PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 12 }],
            cashReceived: 12);
        SetCost(b, 99);
        Assert.Equal(7, DreService.GetDre(Today, Today).Cmv);
    }

    [Fact]
    public void Ranking_UsaSnapshotELegadoEstimado()
    {
        using var _ = BeginDb();
        var snap = TestDataHelper.SeedSimpleProduct(20, 10, 4, "RS", "RANK SNAP");
        var leg = TestDataHelper.SeedSimpleProduct(20, 10, 3, "RL", "RANK LEG");
        TestDataHelper.FinalizeSimpleCashSale(snap, 1, 10, 10);
        InsertLegacySale(leg, 1, 10);
        SetCost(snap, 50);
        SetCost(leg, 2);
        var report = StockService.ListReportLocal(StockReportKind.MaisLucrativos, Today, Today, 10);
        Assert.True(report.CmvUsesHistoricalSnapshot);
        Assert.True(report.HasEstimatedLegacyCost);
        var snapRow = report.Rows.Single(r => r.ProductId == snap);
        var legRow = report.Rows.Single(r => r.ProductId == leg);
        Assert.Equal(4, snapRow.CostTotal);
        Assert.Equal(6, snapRow.Lucro);
        Assert.Equal(2, legRow.CostTotal);
        Assert.Equal(8, legRow.Lucro);
        Assert.Equal(ProductPriceHelper.RoundPrice(snapRow.Total - snapRow.CostTotal), snapRow.Lucro);
    }

    [Fact]
    public void PdvResumo_SnapshotELegado()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "PD", "PDV CMV");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 8, 8);
        InsertLegacySale(pid, 1, 8);
        SetCost(pid, 9);
        var resumo = PdvQueryService.GetResumoDiaLocal();
        Assert.True(resumo.CmvUsesHistoricalSnapshot);
        Assert.True(resumo.HasEstimatedLegacyCost);
        Assert.Equal(ProductPriceHelper.RoundPrice(16 - 5 - 9), resumo.LucroReal);
    }

    [Fact]
    public void Fechamento_UsaSnapshot()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "FC", "FECH CMV");
        TestDataHelper.FinalizeSimpleCashSale(pid, 2, 8, 16);
        SetCost(pid, 9);
        var fc = ReportsService.GetFechamentoConsolidado(Today, Today);
        Assert.Equal(10, fc.Cmv);
        Assert.Equal(10, fc.CmvHistorico);
        Assert.False(fc.HasEstimatedLegacyCost);
        Assert.True(fc.CmvUsesHistoricalSnapshot);
        Assert.Equal(ProductPriceHelper.RoundPrice(fc.TotalFaturado - fc.Cmv), fc.LucroEstimado);
    }

    [Fact]
    public void Fechamento_LegadoEstimado()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "FL", "FECH LEG");
        InsertLegacySale(pid, 1, 8);
        SetCost(pid, 7);
        var fc = ReportsService.GetFechamentoConsolidado(Today, Today);
        Assert.Equal(7, fc.CmvEstimado);
        Assert.True(fc.HasEstimatedLegacyCost);
        Assert.Equal(7, fc.Cmv);
    }

    [Fact]
    public void Movimentacao_UsaHelperNaLinhaDeVenda()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "MV", "MOV CMV");
        TestDataHelper.FinalizeSimpleCashSale(pid, 2, 8, 16);
        SetCost(pid, 9);
        var result = MovimentacaoService.ListProdutosLocal(Today, Today);
        var row = Assert.Single(result.Produtos);
        Assert.Equal(5, row.UnitCost);
        Assert.Equal(ProductPriceHelper.RoundPrice(16 - 10), row.LucroBruto);
    }

    [Fact]
    public void SemBackfill_LegadoPermaneceNull()
    {
        using var _ = BeginDb();
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "NB", "NO BACKFILL");
        var saleId = InsertLegacySale(pid, 1, 8);
        DreService.GetDre(Today, Today);
        BusinessDashboardService.GetDashboardLocal(Today, Today);
        ReportsService.GetFechamentoConsolidado(Today, Today);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cost_at_sale IS NULL FROM sale_items WHERE sale_id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
    }

    private static void SetCost(int productId, double cost)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET cost_price = $c WHERE id = $id;";
        cmd.Parameters.AddWithValue("$c", cost);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static int InsertLegacySale(int productId, double qty, double unitPrice)
    {
        var total = ProductPriceHelper.RoundPrice(qty * unitPrice);
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ($d, $t, 'Dinheiro', 0, datetime('now','localtime'));
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$d", Today.ToString("yyyy-MM-dd"));
        ins.Parameters.AddWithValue("$t", total);
        var saleId = Convert.ToInt32(ins.ExecuteScalar());
        using var item = conn.CreateCommand();
        item.CommandText = """
            INSERT INTO sale_items (sale_id, product_id, product_name, quantity, unit_price, subtotal)
            VALUES ($s, $p, 'LEGADO B2', $q, $u, $t);
            """;
        item.Parameters.AddWithValue("$s", saleId);
        item.Parameters.AddWithValue("$p", productId);
        item.Parameters.AddWithValue("$q", qty);
        item.Parameters.AddWithValue("$u", unitPrice);
        item.Parameters.AddWithValue("$t", total);
        item.ExecuteNonQuery();
        return saleId;
    }

    private static int SeedCig(double stock, double cost, double fator)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAvulso = 1.50,
            PrecoAtacado = 10,
            PrecoCompra = cost,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIGB2', 'Rothmans Blue', 'Cigarros', 'UN', 10, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static PdvFinalizeResult FinalizeCig(int productId, string mode, double qty)
    {
        var product = ProductService.GetById(productId)!;
        var resolved = PdvService.ResolveManualSale(product, mode);
        var line = new PdvCartLine
        {
            ProductId = productId,
            Quantity = qty,
            UnitPrice = resolved.UnitPrice,
            StockUnitsPerSale = resolved.StockUnitsPerSale,
        };
        var total = ProductPriceHelper.RoundPrice(line.Quantity * line.UnitPrice);
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items = [line],
            PaymentType = "Dinheiro",
            CashReceived = total,
        });
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN B2', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int ReadItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
