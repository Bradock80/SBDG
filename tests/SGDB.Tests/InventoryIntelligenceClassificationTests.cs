using System.Globalization;
using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 70C-B2 — classificações derivadas (cobertura, silêncio, parado, anomalia local).
/// Bancos isolados em %TEMP%\SGDB.Tests. Não toca deposito.db nem o EXE da loja.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryIntelligenceClassificationTests
{
    private static readonly DateTime Today = DateTime.Today;
    private const double Tol = 0.0001;

    private static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    private static TempDatabase BeginWithCash()
    {
        var db = Begin();
        CashService.OpenSession(100, "70c-b2");
        return db;
    }

    private static ProductTurnoverRow Row(int productId, DateTime? today = null)
    {
        var row = InventoryIntelligenceService.GetByProductId(productId, today ?? Today);
        Assert.NotNull(row);
        return row!;
    }

    private static InventoryIntelligenceEngine.LifeStartDecision Life(
        int daysAgo, bool evidence) =>
        new(Today.AddDays(-daysAgo), "test", "test", evidence);

    private static ProductTurnoverRow Build(
        double stock,
        double fridge,
        InventoryIntelligenceEngine.LifeStartDecision life,
        IReadOnlyList<InventoryIntelligenceEngine.DailyFlow>? daily = null,
        bool isComposition = false) =>
        InventoryIntelligenceEngine.BuildRow(
            1, "T", "T", stock, fridge, Today, life, daily ?? [], isComposition);

    private static void SetProductCreated(int productId, DateTime date)
    {
        var utc = date.Date - DateBrHelper.BrazilOffset;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET created_at = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static void StampInbound(int productId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at, operation
            ) VALUES (
              $pid, 'entrada', 1, 0, '70c-b2 inbound', $at, 'entrada_compra'
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$at", date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static void SetSaleDate(int saleId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET session_date = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$id", saleId);
        cmd.ExecuteNonQuery();
    }

    private static void SetStock(int productId, double stock)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock = $s WHERE id = $id;";
        cmd.Parameters.AddWithValue("$s", stock);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static int SeedKit(int componentId)
    {
        var extra = new ProductExtra
        {
            Composicao = true,
            ComposicaoItens =
            [
                new ProductCompositionItem
                {
                    ProductId = componentId,
                    Quantity = 2,
                    Code = "CMP",
                    Name = "Componente",
                    Unit = "UN",
                },
            ],
        }.ToJson();

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'KITB2', 'Kit B2', 'UN', 20, 10, 0, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static (InventoryCoverageState State, double? Days, InventoryCoverageBand Band)
        BandFromStockAndVmv(double stock, double vmv30)
    {
        var (state, days) = InventoryIntelligenceEngine.ClassifyCoverage(stock, vmv30);
        return (state, days, InventoryIntelligenceEngine.ClassifyCoverageBand(state, days));
    }

    [Fact]
    public void AmbienteIsolado_NaoUsaBancoDaLoja()
    {
        using var db = Begin();
        Assert.Contains("SGDB.Tests", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deposito.db", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoverageBand_Critical_WhenCoverageDaysAtMost3()
    {
        var (_, days, band) = BandFromStockAndVmv(3, 1);
        Assert.Equal(3, days!.Value, Tol);
        Assert.Equal(InventoryCoverageBand.Critical, band);
        Assert.True(InventoryIntelligenceEngine.ClassifyInsufficientStock(band));
    }

    [Fact]
    public void CoverageBand_Low_WhenCoverageDaysBetween3And7()
    {
        var (_, days, band) = BandFromStockAndVmv(5, 1);
        Assert.Equal(5, days!.Value, Tol);
        Assert.Equal(InventoryCoverageBand.Low, band);
        Assert.False(InventoryIntelligenceEngine.ClassifyInsufficientStock(band));
    }

    [Fact]
    public void CoverageBand_Attention_WhenCoverageDaysBetween7And15()
    {
        var (_, days, band) = BandFromStockAndVmv(10, 1);
        Assert.Equal(10, days!.Value, Tol);
        Assert.Equal(InventoryCoverageBand.Attention, band);
    }

    [Fact]
    public void CoverageBand_Normal_WhenCoverageDaysAbove15()
    {
        var (_, days, band) = BandFromStockAndVmv(20, 1);
        Assert.Equal(20, days!.Value, Tol);
        Assert.Equal(InventoryCoverageBand.Normal, band);
        Assert.False(InventoryIntelligenceEngine.ClassifyInsufficientStock(band));
    }

    [Theory]
    [InlineData(3.0, InventoryCoverageBand.Critical)]
    [InlineData(3.00005, InventoryCoverageBand.Critical)]
    [InlineData(3.0001, InventoryCoverageBand.Critical)]
    [InlineData(3.00011, InventoryCoverageBand.Low)]
    [InlineData(7.0, InventoryCoverageBand.Low)]
    [InlineData(7.0001, InventoryCoverageBand.Low)]
    [InlineData(7.00011, InventoryCoverageBand.Attention)]
    [InlineData(15.0, InventoryCoverageBand.Attention)]
    [InlineData(15.0001, InventoryCoverageBand.Attention)]
    [InlineData(15.00011, InventoryCoverageBand.Normal)]
    public void CoverageBand_Boundaries(double coverageDays, InventoryCoverageBand expected)
    {
        var band = InventoryIntelligenceEngine.ClassifyCoverageBand(
            InventoryCoverageState.Calculable, coverageDays);
        Assert.Equal(expected, band);
        Assert.Equal(
            expected == InventoryCoverageBand.Critical,
            InventoryIntelligenceEngine.ClassifyInsufficientStock(band));
    }

    [Fact]
    public void ZeroStock_IsNotInsufficientStock()
    {
        var (state, days, band) = BandFromStockAndVmv(0, 2);
        Assert.Equal(InventoryCoverageState.ZeroStock, state);
        Assert.Null(days);
        Assert.Equal(InventoryCoverageBand.Zero, band);
        Assert.False(InventoryIntelligenceEngine.ClassifyInsufficientStock(band));
    }

    [Fact]
    public void InsufficientStock_RequiresPositiveStockAndCriticalCoverage()
    {
        var row = Build(
            stock: 3, fridge: 0, Life(40, evidence: true),
            [new InventoryIntelligenceEngine.DailyFlow(Today, 30, 0, true)]);
        Assert.True(row.TotalStock > InventoryIntelligenceEngine.Epsilon);
        Assert.Equal(InventoryCoverageState.Calculable, row.CoverageState);
        Assert.NotNull(row.CoverageDays);
        Assert.True(row.CoverageDays <= 3 + InventoryIntelligenceEngine.Epsilon);
        Assert.Equal(InventoryCoverageBand.Critical, row.CoverageBand);
        Assert.True(row.IsInsufficientStock);
        Assert.False(row.IsZeroStockWithTurnover);
    }

    [Fact]
    public void ZeroStockWithTurnover_WhenVmv30Positive()
    {
        var row = Build(
            stock: 0, fridge: 0, Life(40, evidence: true),
            [new InventoryIntelligenceEngine.DailyFlow(Today, 6, 0, true)]);
        Assert.Equal(0, row.TotalStock, Tol);
        Assert.True(row.Vmv30 > InventoryIntelligenceEngine.Epsilon);
        Assert.Equal(InventoryCoverageBand.Zero, row.CoverageBand);
        Assert.True(row.IsZeroStockWithTurnover);
        Assert.False(row.IsInsufficientStock);
        Assert.Null(row.CoverageDays);
    }

    [Fact]
    public void ZeroStockWithoutTurnover_IsNotZeroWithTurnover()
    {
        var row = Build(stock: 0, fridge: 0, Life(40, evidence: false));
        Assert.Equal(InventoryCoverageBand.Zero, row.CoverageBand);
        Assert.False(row.IsZeroStockWithTurnover);
        Assert.False(row.IsInsufficientStock);
    }

    [Fact]
    public void NegativeTotalStock_BandNegative_NoCoverageDays()
    {
        var (state, days, band) = BandFromStockAndVmv(-4, 1);
        Assert.Equal(InventoryCoverageState.NegativeStock, state);
        Assert.Null(days);
        Assert.Equal(InventoryCoverageBand.Negative, band);
        var row = Build(stock: -4, fridge: 0, Life(40, evidence: true));
        Assert.Equal(InventoryCoverageBand.Negative, row.CoverageBand);
        Assert.Null(row.CoverageDays);
        Assert.False(row.IsInsufficientStock);
    }

    [Fact]
    public void NoTurnover_DoesNotReturnInfinityOrNaN()
    {
        var (state, days) = InventoryIntelligenceEngine.ClassifyCoverage(10, 0);
        Assert.Equal(InventoryCoverageState.NoTurnover, state);
        Assert.Null(days);
        var band = InventoryIntelligenceEngine.ClassifyCoverageBand(state, days);
        Assert.Equal(InventoryCoverageBand.NotCalculable, band);
        Assert.Null(InventoryIntelligenceEngine.SafeRatio(10, 0));
        var row = Build(stock: 12, fridge: 0, Life(40, evidence: true));
        Assert.Equal(0, row.Vmv30, Tol);
        Assert.Equal(InventoryCoverageBand.NotCalculable, row.CoverageBand);
        Assert.Null(row.CoverageDays);
        Assert.False(double.IsInfinity(row.Vmv30));
        Assert.False(double.IsNaN(row.Vmv30));
    }

    [Fact]
    public void HistoryInsufficient_7_30_90_Flags()
    {
        var d6 = Build(10, 0, Life(5, evidence: true));
        Assert.Equal(6, d6.HistoryDays);
        Assert.True(d6.IsHistoryInsufficient7);
        Assert.True(d6.IsHistoryInsufficient30);
        Assert.True(d6.IsHistoryInsufficient90);

        var d20 = Build(10, 0, Life(19, evidence: true));
        Assert.Equal(20, d20.HistoryDays);
        Assert.False(d20.IsHistoryInsufficient7);
        Assert.True(d20.IsHistoryInsufficient30);
        Assert.True(d20.IsHistoryInsufficient90);

        var d40 = Build(10, 0, Life(39, evidence: true));
        Assert.Equal(40, d40.HistoryDays);
        Assert.False(d40.IsHistoryInsufficient7);
        Assert.False(d40.IsHistoryInsufficient30);
        Assert.True(d40.IsHistoryInsufficient90);

        var d100 = Build(10, 0, Life(99, evidence: true));
        Assert.Equal(100, d100.HistoryDays);
        Assert.False(d100.IsHistoryInsufficient90);
    }

    [Theory]
    [InlineData(28, 30, false)]
    [InlineData(29, 30, true)]
    [InlineData(58, 60, false)]
    [InlineData(59, 60, true)]
    [InlineData(88, 90, false)]
    [InlineData(89, 90, true)]
    public void Silence_HistoryDaysExactBoundary_NeverSoldWithEvidence(
        int lifeDaysAgo, int silenceDays, bool expected)
    {
        var row = Build(10, 0, Life(lifeDaysAgo, evidence: true));
        Assert.Equal(lifeDaysAgo + 1, row.HistoryDays);
        Assert.True(row.HasPhysicalAvailabilityEvidence);
        Assert.False(row.IsCompositionProduct);
        Assert.Null(row.LastValidSaleDate);
        Assert.Equal(expected, row.QualifiesSilence(silenceDays));
    }

    [Theory]
    [InlineData(29, 30, false)]
    [InlineData(30, 30, true)]
    [InlineData(59, 60, false)]
    [InlineData(60, 60, true)]
    [InlineData(89, 90, false)]
    [InlineData(90, 90, true)]
    public void Silence_DaysWithoutSaleExactBoundary_WithEvidence(
        int daysWithoutSale, int silenceDays, bool expected)
    {
        var historyAgo = Math.Max(daysWithoutSale, silenceDays) + 10;
        var row = Build(
            10, 0, Life(historyAgo, evidence: true),
            [new InventoryIntelligenceEngine.DailyFlow(Today.AddDays(-daysWithoutSale), 1, 0, true)]);
        Assert.True(row.HistoryDays > silenceDays);
        Assert.Equal(daysWithoutSale, row.DaysWithoutSale);
        Assert.True(row.HasPhysicalAvailabilityEvidence);
        Assert.False(row.IsCompositionProduct);
        Assert.Equal(expected, row.QualifiesSilence(silenceDays));
    }

    [Fact]
    public void NeverSold_HistoryDaysExactly90_WithEvidence_IsIdle()
    {
        var row = Build(10, 0, Life(89, evidence: true));
        Assert.Equal(90, row.HistoryDays);
        Assert.True(row.HasPhysicalAvailabilityEvidence);
        Assert.Null(row.LastValidSaleDate);
        Assert.True(row.QualifiesSilence90);
        Assert.True(row.IsIdle);
        Assert.True(row.HasUnobservedSale);
    }

    [Fact]
    public void NeverSold_HistoryDaysExactly90_WithoutEvidence_IsNotIdle()
    {
        var row = Build(10, 0, Life(89, evidence: false));
        Assert.Equal(90, row.HistoryDays);
        Assert.False(row.HasPhysicalAvailabilityEvidence);
        Assert.Null(row.LastValidSaleDate);
        Assert.False(row.QualifiesSilence90);
        Assert.False(row.IsIdle);
        Assert.False(row.HasUnobservedSale);
    }

    [Fact]
    public void Silence30_RequiresEvidenceAndHistory()
    {
        var row = Build(
            8, 0, Life(50, evidence: true),
            [new InventoryIntelligenceEngine.DailyFlow(Today.AddDays(-40), 1, 0, true)]);
        Assert.Equal(40, row.DaysWithoutSale);
        Assert.True(row.QualifiesSilence30);
        Assert.False(row.QualifiesSilence60);
        Assert.False(row.QualifiesSilence90);
        Assert.False(row.IsIdle);
    }

    [Fact]
    public void Silence60_And90()
    {
        var d70 = Build(
            8, 0, Life(80, evidence: true),
            [new InventoryIntelligenceEngine.DailyFlow(Today.AddDays(-70), 1, 0, true)]);
        Assert.True(d70.QualifiesSilence30);
        Assert.True(d70.QualifiesSilence60);
        Assert.False(d70.QualifiesSilence90);
        Assert.False(d70.IsIdle);

        var d90 = Build(
            8, 0, Life(100, evidence: true),
            [new InventoryIntelligenceEngine.DailyFlow(Today.AddDays(-90), 1, 0, true)]);
        Assert.True(d90.QualifiesSilence90);
        Assert.True(d90.IsIdle);
    }

    [Fact]
    public void CatalogOldWithoutPhysicalEvidence_IsNotIdle_AndNotSilence()
    {
        var row = Build(10, 0, Life(120, evidence: false));
        Assert.False(row.HasPhysicalAvailabilityEvidence);
        Assert.True(row.HistoryDays >= 90);
        Assert.False(row.IsIdle);
        Assert.False(row.QualifiesSilence30);
        Assert.False(row.QualifiesSilence60);
        Assert.False(row.QualifiesSilence90);
        Assert.False(row.HasUnobservedSale);
    }

    [Fact]
    public void NeverSold_WithPhysicalEvidence_And90Days_IsIdleAndUnobserved()
    {
        var row = Build(10, 0, Life(100, evidence: true));
        Assert.True(row.HasPhysicalAvailabilityEvidence);
        Assert.Null(row.LastValidSaleDate);
        Assert.True(row.HasUnobservedSale);
        Assert.True(row.QualifiesSilence90);
        Assert.True(row.IsIdle);
    }

    [Fact]
    public void SoldAtLeast90DaysAgo_WithPositiveStock_IsIdle()
    {
        var row = Build(
            10, 0, Life(120, evidence: true),
            [new InventoryIntelligenceEngine.DailyFlow(Today.AddDays(-90), 2, 0, true)]);
        Assert.Equal(90, row.DaysWithoutSale);
        Assert.True(row.TotalStock > InventoryIntelligenceEngine.Epsilon);
        Assert.True(row.IsIdle);
        Assert.False(row.HasUnobservedSale);
    }

    [Fact]
    public void CoverageAbove15_IsNotAutomaticLowTurnover_AndNotIdleIfSoldRecently()
    {
        var row = Build(
            80, 0, Life(100, evidence: true),
            [new InventoryIntelligenceEngine.DailyFlow(Today, 10, 0, true)]);
        Assert.Equal(InventoryCoverageBand.Normal, row.CoverageBand);
        Assert.True(row.CoverageDays > 15);
        Assert.True(row.Vmv90 > InventoryIntelligenceEngine.Epsilon);
        Assert.False(row.IsIdle);
        Assert.False(row.IsInsufficientStock);

        var engine = File.ReadAllText(FindSource("InventoryIntelligenceEngine.cs"));
        var model = File.ReadAllText(FindModelSource());
        Assert.DoesNotContain("IsLowTurnover", engine);
        Assert.DoesNotContain("IsLowTurnover", model);
        Assert.DoesNotContain("LowTurnover", engine);
        Assert.DoesNotContain("enum InventoryLowTurnover", model);
    }

    [Fact]
    public void LocationAnomaly_WarehouseNegativeFridgePositive()
    {
        Assert.True(InventoryIntelligenceEngine.ClassifyLocationStockAnomaly(-3, 8));
        var row = Build(-3, 8, Life(40, evidence: true));
        Assert.True(row.HasLocationStockAnomaly);
        Assert.Equal(5, row.TotalStock, Tol);
        Assert.NotEqual(InventoryCoverageBand.Negative, row.CoverageBand);
    }

    [Fact]
    public void LocationAnomaly_WarehousePositiveFridgeNegative()
    {
        Assert.True(InventoryIntelligenceEngine.ClassifyLocationStockAnomaly(8, -3));
        var row = Build(8, -3, Life(40, evidence: true));
        Assert.True(row.HasLocationStockAnomaly);
        Assert.Equal(5, row.TotalStock, Tol);
    }

    [Fact]
    public void LocationAnomaly_TotalZero_IsNotHidden()
    {
        var row = Build(-5, 5, Life(40, evidence: true));
        Assert.Equal(0, row.TotalStock, Tol);
        Assert.True(row.HasLocationStockAnomaly);
        Assert.Equal(InventoryCoverageBand.Zero, row.CoverageBand);
        Assert.False(row.IsInsufficientStock);
        Assert.False(row.IsIdle);
        Assert.Null(row.CoverageDays);
    }

    [Fact]
    public void CriticalBand_AlignsWithInsufficientStock_ZeroNeverInsufficient()
    {
        var critical = InventoryIntelligenceEngine.ClassifyCoverageBand(
            InventoryCoverageState.Calculable, 3);
        Assert.Equal(InventoryCoverageBand.Critical, critical);
        Assert.True(InventoryIntelligenceEngine.ClassifyInsufficientStock(critical));

        var zero = Build(0, 0, Life(40, evidence: true));
        Assert.Equal(InventoryCoverageBand.Zero, zero.CoverageBand);
        Assert.False(zero.IsInsufficientStock);
    }

    [Fact]
    public void Idle_NeverTrueWithoutPhysicalEvidence()
    {
        var row = Build(10, 0, Life(200, evidence: false));
        Assert.False(row.HasPhysicalAvailabilityEvidence);
        Assert.False(row.IsIdle);
    }

    [Fact]
    public void LocationAnomaly_BothNonNegative_IsFalse()
    {
        Assert.False(InventoryIntelligenceEngine.ClassifyLocationStockAnomaly(4, 2));
        Assert.False(InventoryIntelligenceEngine.ClassifyLocationStockAnomaly(0, 0));
        var row = Build(4, 2, Life(40, evidence: true));
        Assert.False(row.HasLocationStockAnomaly);
        Assert.Equal(6, row.TotalStock, Tol);
    }

    [Fact]
    public void CompositionSku_WithIdleShape_IsNotIdle_AndNotSilence()
    {
        var row = Build(10, 0, Life(100, evidence: true), isComposition: true);
        Assert.True(row.IsCompositionProduct);
        Assert.False(row.IsIdle);
        Assert.False(row.QualifiesSilence90);
        Assert.False(row.HasUnobservedSale);
    }

    [Fact]
    public void QueryCount_RemainsSix_NoNewQuery()
    {
        using var db = BeginWithCash();
        for (var i = 0; i < 8; i++)
        {
            var id = TestDataHelper.SeedSimpleProduct(20, 10, 4, $"B2Q{i}", $"B2Q{i}");
            TestDataHelper.FinalizeSimpleCashSale(id, 1, 10, 10);
        }
        var snap = InventoryIntelligenceService.Load(Today);
        Assert.Equal(6, InventoryIntelligenceService.ExpectedQueryCount);
        Assert.Equal(InventoryIntelligenceService.ExpectedQueryCount, snap.QueryCount);
        Assert.True(snap.Rows.Count >= 8);
        Assert.All(snap.Rows, r =>
        {
            Assert.True(InventoryIntelligenceEngine.IsFinite(r.Vmv30));
            if (r.CoverageDays is double c)
                Assert.True(InventoryIntelligenceEngine.IsFinite(c));
        });
    }

    [Fact]
    public void Integration_CatalogOldWithoutEvidence_NotIdle()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(8, 10, 4, "CAT", "So Cadastro");
        SetProductCreated(id, Today.AddDays(-120));
        var row = Row(id);
        Assert.False(row.HasPhysicalAvailabilityEvidence);
        Assert.False(row.IsIdle);
        Assert.False(row.QualifiesSilence90);
        Assert.DoesNotContain("deposito.db", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Integration_NeverSoldWithInbound_IsIdle()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(10, 10, 4, "INB", "Entrada Sem Venda");
        StampInbound(id, Today.AddDays(-100));
        SetProductCreated(id, Today.AddDays(-100));
        var row = Row(id);
        Assert.True(row.HasPhysicalAvailabilityEvidence);
        Assert.Null(row.LastValidSaleDate);
        Assert.True(row.HasUnobservedSale);
        Assert.True(row.IsIdle);
        Assert.True(row.QualifiesSilence90);
    }

    [Fact]
    public void Integration_Sold90DaysAgo_IsIdle()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(40, 10, 4, "OLD", "Venda Antiga");
        StampInbound(id, Today.AddDays(-120));
        SetProductCreated(id, Today.AddDays(-120));
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 2, 10, 20);
        SetSaleDate(sale.SaleId, Today.AddDays(-90));
        SetStock(id, 10);
        var row = Row(id);
        Assert.Equal(90, row.DaysWithoutSale);
        Assert.True(row.TotalStock > InventoryIntelligenceEngine.Epsilon);
        Assert.True(row.IsIdle);
    }

    [Fact]
    public void Integration_LocationAnomaly_DoesNotChangeCoverageDaysFormula()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(10, 10, 4, "LOC", "Local");
        StampInbound(id, Today.AddDays(-40));
        TestDataHelper.FinalizeSimpleCashSale(id, 10, 10, 100);
        SetStock(id, -3);
        TestDataHelper.SetProductFridge(id, 8);
        var row = Row(id);
        Assert.True(row.HasLocationStockAnomaly);
        Assert.Equal(5, row.TotalStock, Tol);
        Assert.NotNull(row.CoverageDays);
        Assert.Equal(5 / row.Vmv30, row.CoverageDays!.Value, 0.05);
    }

    [Fact]
    public void Integration_KitSku_NotIdleDespiteCatalogStock()
    {
        using var db = Begin();
        var comp = TestDataHelper.SeedSimpleProduct(50, 2, 1, "CMPB2", "Comp B2");
        var kit = SeedKit(comp);
        StampInbound(kit, Today.AddDays(-100));
        SetProductCreated(kit, Today.AddDays(-100));
        var row = Row(kit);
        Assert.True(row.IsCompositionProduct);
        Assert.False(row.IsIdle);
        Assert.False(row.QualifiesSilence90);
    }

    [Fact]
    public void CodigoNaoAdicionaQueryNemBaixoGiro()
    {
        var svc = File.ReadAllText(FindSource("InventoryIntelligenceService.cs"));
        Assert.Contains("ExpectedQueryCount = 6", svc);
        Assert.DoesNotContain("CREATE INDEX", svc, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsLowTurnover", svc);
        Assert.DoesNotContain("ListCurvaAbc", svc);
    }

    private static string FindSource(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SGDB.App", "Services", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(fileName);
    }

    private static string FindModelSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SGDB.App", "Models", "InventoryIntelligence.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("InventoryIntelligence.cs");
    }
}
