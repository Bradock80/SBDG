using System.IO;
using System.Reflection;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70G-B2 — composer de orientação de reposição. Sem I/O, SQL, UI, quantidade,
/// fornecedor, B5, PurchaseService ou recálculo de autoridades.
/// </summary>
public class InventoryPurchaseGuidanceComposerTests
{
    private static readonly DateTime Today = new(2026, 9, 1);

    #region Population

    [Fact]
    public void Populacao_vazia_produz_snapshot_vazio()
    {
        var snap = Compose();
        Assert.Empty(snap.Results);
        Assert.Empty(snap.ByProductId);
    }

    [Fact]
    public void Um_produto_produz_um_resultado()
    {
        var snap = Compose(Row(1));
        Assert.Single(snap.Results);
        Assert.True(snap.ByProductId.ContainsKey(1));
    }

    [Fact]
    public void Varios_produtos_produzem_N_resultados()
    {
        var snap = Compose(Row(1), Row(2), Row(3));
        Assert.Equal(3, snap.Results.Count);
        Assert.Equal(3, snap.ByProductId.Count);
    }

    [Fact]
    public void N_70C_produz_N_guidance()
    {
        var rows = Enumerable.Range(1, 20).Select(i => Row(i)).ToArray();
        var snap = Compose(rows);
        Assert.Equal(20, snap.Results.Count);
        Assert.Equal(20, snap.ByProductId.Count);
    }

    [Fact]
    public void Ordem_preservada_conforme_70C()
    {
        var snap = Compose(Row(5), Row(3), Row(1));
        Assert.Equal(5, snap.Results[0].ProductId);
        Assert.Equal(3, snap.Results[1].ProductId);
        Assert.Equal(1, snap.Results[2].ProductId);
    }

    [Fact]
    public void ProductId_correto_no_resultado()
    {
        var snap = Compose(Row(42));
        Assert.Equal(42, snap.Results[0].ProductId);
        Assert.Equal(42, snap.ByProductId[42].ProductId);
    }

    [Fact]
    public void Lookup_O1_por_ProductId()
    {
        var snap = Compose(Row(10), Row(20));
        Assert.True(snap.ByProductId.ContainsKey(10));
        Assert.True(snap.ByProductId.ContainsKey(20));
        Assert.False(snap.ByProductId.ContainsKey(30));
    }

    [Fact]
    public void NotApplicable_preservado()
    {
        var snap = Compose(Row(1, composition: true));
        Assert.Equal(InventoryPurchaseGuidanceStatus.NotApplicable, snap.Results[0].Status);
        Assert.Equal(InventoryPurchaseGuidanceAction.None, snap.Results[0].Action);
        Assert.True(snap.ByProductId.ContainsKey(1));
    }

    [Fact]
    public void ReviewData_preservado()
    {
        var snap = Compose(Row(1, stock: -5, coverageBand: InventoryCoverageBand.Negative));
        Assert.Equal(InventoryPurchaseGuidanceStatus.ReviewData, snap.Results[0].Status);
    }

    [Fact]
    public void Monitor_preservado()
    {
        var snap = Compose(Row(1));
        Assert.Equal(InventoryPurchaseGuidanceStatus.Monitor, snap.Results[0].Status);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, snap.Results[0].Action);
    }

    #endregion

    #region Join

    [Fact]
    public void Missing_70D_produz_ReviewData()
    {
        var r1 = Row(1);
        var r2 = Row(2);
        var map = ProjectMap(r1);
        var snap = ComposeRaw([r1, r2], map);
        Assert.Equal(InventoryPurchaseGuidanceStatus.ReviewData, snap.ByProductId[2].Status);
        Assert.Equal(InventoryPurchaseGuidanceReason.StructuralDataIssue, snap.ByProductId[2].PrimaryReason);
        Assert.NotEqual(InventoryPurchaseGuidanceStatus.ReviewData, snap.ByProductId[1].Status);
    }

    [Fact]
    public void Duplicate_70D_produz_ReviewData()
    {
        var r1 = Row(1);
        var projections = new[]
        {
            Project(r1),
            Project(r1),
        };
        var snap = InventoryPurchaseGuidanceComposer.Compose([r1], projections);
        Assert.Equal(InventoryPurchaseGuidanceReason.StructuralDataIssue, snap.Results[0].PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceStatus.ReviewData, snap.Results[0].Status);
    }

    [Fact]
    public void Extra_70D_ignorado()
    {
        var r1 = Row(1);
        var r2 = Row(2);
        var projections = new[] { Project(r1), Project(r2) };
        var snap = InventoryPurchaseGuidanceComposer.Compose([r1], projections);
        Assert.Single(snap.Results);
        Assert.Equal(1, snap.Results[0].ProductId);
        Assert.False(snap.ByProductId.ContainsKey(2));
    }

    [Fact]
    public void Duplicate_70C_primeiro_ganha()
    {
        var r1 = Row(1);
        var snap = Compose(r1, r1);
        Assert.Equal(2, snap.Results.Count);
        Assert.True(snap.ByProductId.ContainsKey(1));
    }

    [Fact]
    public void Null_70C_snapshot_produz_vazio()
    {
        var snap = InventoryPurchaseGuidanceComposer.Compose(
            (InventoryProjectionSnapshot?)null);
        Assert.Empty(snap.Results);
    }

    [Fact]
    public void Null_70D_lista_produz_ReviewData_para_cada_70C()
    {
        var snap = InventoryPurchaseGuidanceComposer.Compose(
            [Row(1), Row(2)], null);
        Assert.Equal(2, snap.Results.Count);
        Assert.All(snap.Results, r =>
        {
            Assert.Equal(InventoryPurchaseGuidanceStatus.ReviewData, r.Status);
            Assert.Equal(InventoryPurchaseGuidanceReason.StructuralDataIssue, r.PrimaryReason);
        });
    }

    [Fact]
    public void Empty_70D_com_populacao_70C_produz_ReviewData()
    {
        var snap = InventoryPurchaseGuidanceComposer.Compose(
            [Row(1)], []);
        Assert.Single(snap.Results);
        Assert.Equal(InventoryPurchaseGuidanceReason.StructuralDataIssue, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Produtos_fora_de_ordem_entre_70C_e_70D()
    {
        var r1 = Row(1);
        var r2 = Row(2);
        var r3 = Row(3);
        var projections = new[] { Project(r3), Project(r1), Project(r2) };
        var snap = InventoryPurchaseGuidanceComposer.Compose([r1, r2, r3], projections);
        Assert.Equal(3, snap.Results.Count);
        Assert.Equal(1, snap.Results[0].ProductId);
        Assert.Equal(2, snap.Results[1].ProductId);
        Assert.Equal(3, snap.Results[2].ProductId);
        Assert.All(snap.Results, r =>
            Assert.NotEqual(InventoryPurchaseGuidanceReason.StructuralDataIssue, r.PrimaryReason));
    }

    [Fact]
    public void Join_por_ProductId_nao_por_indice()
    {
        var r1 = Row(10);
        var r2 = Row(20);
        var projections = new[] { Project(r2), Project(r1) };
        var snap = InventoryPurchaseGuidanceComposer.Compose([r1, r2], projections);
        Assert.Equal(2, snap.Results.Count);
        Assert.NotEqual(InventoryPurchaseGuidanceReason.StructuralDataIssue, snap.ByProductId[10].PrimaryReason);
        Assert.NotEqual(InventoryPurchaseGuidanceReason.StructuralDataIssue, snap.ByProductId[20].PrimaryReason);
    }

    #endregion

    #region Mapping

    [Fact]
    public void Zero_com_giro_considera_reposicao()
    {
        var snap = Compose(Row(1, stock: 0, fridge: 0, vmv30: 2,
            coverageBand: InventoryCoverageBand.Zero,
            isZeroStockWithTurnover: true));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Critical_considera_reposicao()
    {
        var snap = Compose(Row(1, stock: 2, vmv30: 1,
            coverageBand: InventoryCoverageBand.Critical, coverageDays: 2));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.CriticalCoverage, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Low_considera_reposicao()
    {
        var snap = Compose(Row(1, stock: 5, vmv30: 1,
            coverageBand: InventoryCoverageBand.Low, coverageDays: 5));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.LowCoverage, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Attention_monitora()
    {
        var snap = Compose(Row(1, stock: 12, vmv30: 1,
            coverageBand: InventoryCoverageBand.Attention, coverageDays: 12));
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, snap.Results[0].Action);
    }

    [Fact]
    public void Normal_monitora()
    {
        var snap = Compose(Row(1));
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, snap.Results[0].Action);
    }

    [Fact]
    public void Excess30_nao_repor()
    {
        var snap = Compose(Row(1, stock: 80, vmv30: 1,
            coverageBand: InventoryCoverageBand.Normal, coverageDays: 80),
            excessQty: 50);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExcess30, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void ExpirySurplus_nao_repor()
    {
        var snap = Compose(Row(1, stock: 20, vmv30: 1), surplusLot: true);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Expiry_com_location_limitation_monitora()
    {
        var snap = Compose(Row(1, stock: 20, fridge: 5, vmv30: 0.3), surplusLot: true, fridge: true);
        var result = snap.Results[0];
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, result.Action);
    }

    [Fact]
    public void Excess_com_location_limitation_nao_bloqueia()
    {
        var snap = Compose(Row(1, stock: 70, fridge: 10, vmv30: 1,
            coverageBand: InventoryCoverageBand.Normal, coverageDays: 80),
            excessQty: 40, fridge: true);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExcess30, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Idle_nao_repor()
    {
        var snap = Compose(Row(1, vmv30: 0, idle: true,
            coverageBand: InventoryCoverageBand.NotCalculable));
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.IdleStock, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Vmv0_nao_idle_monitora()
    {
        var snap = Compose(Row(1, vmv30: 0, history: 45,
            coverageBand: InventoryCoverageBand.NotCalculable));
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.NoObservableDemand, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Expired_nao_repor()
    {
        var snap = Compose(Row(1), expired: true);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.Expired, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void ExpiresToday_nao_repor()
    {
        var snap = Compose(Row(1), expiresToday: true);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.ExpiresToday, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void InsufficientHistory_monitora()
    {
        var snap = Compose(Row(1, history: 12, insufficient30: true,
            coverageBand: InventoryCoverageBand.Normal, coverageDays: 20));
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.InsufficientHistory, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void NoEvidence_revisa()
    {
        var snap = Compose(Row(1, evidence: false));
        Assert.Equal(InventoryPurchaseGuidanceAction.ReviewData, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.NoPhysicalEvidence, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Structural_flags_passados_corretamente()
    {
        var snap = Compose(Row(1, stock: -2,
            coverageBand: InventoryCoverageBand.Negative,
            hasLocationStockAnomaly: true));
        Assert.Equal(InventoryPurchaseGuidanceAction.ReviewData, snap.Results[0].Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.StructuralDataIssue, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Composition_nao_aplicavel()
    {
        var snap = Compose(Row(1, composition: true));
        Assert.Equal(InventoryPurchaseGuidanceStatus.NotApplicable, snap.Results[0].Status);
        Assert.Equal(InventoryPurchaseGuidanceReason.CompositionProduct, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Conflito_Excess_mais_Low_vira_ReviewData()
    {
        var snap = Compose(Row(1, stock: 5, vmv30: 1,
            coverageBand: InventoryCoverageBand.Low, coverageDays: 5),
            excessQty: 20);
        Assert.Equal(InventoryPurchaseGuidanceReason.StructuralDataIssue, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void Conflito_Idle_mais_Low_vira_ReviewData()
    {
        var snap = Compose(Row(1, vmv30: 1, idle: true,
            coverageBand: InventoryCoverageBand.Low, coverageDays: 6));
        Assert.Equal(InventoryPurchaseGuidanceReason.StructuralDataIssue, snap.Results[0].PrimaryReason);
    }

    [Fact]
    public void CanProjectSku_false_ignora_excesso()
    {
        var snap = Compose(Row(1),
            excessQty: 40,
            canProjectSku: false);
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExcess30, snap.Results[0].PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, snap.Results[0].Action);
    }

    #endregion

    #region Purity

    [Fact]
    public void ExpectedQueryCount_e_zero() =>
        Assert.Equal(0, InventoryPurchaseGuidanceComposer.ExpectedQueryCount);

    [Fact]
    public void Sem_SQLite_no_composer()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLiteConnection", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_SQL_no_composer()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("INSERT ", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sem_PurchaseService()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("PurchaseService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("purchases", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sem_Supplier()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("Supplier", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SupplierId", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_WPF()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_DateTime_Now()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_B5()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("PromotionSuggestion", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialScenario", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_min_stock()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("min_stock", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MinStock", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_margem()
    {
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceComposer.cs");
        Assert.DoesNotContain("Margin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GrossMargin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sem_quantidade_no_snapshot()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidanceSnapshot),
            "SuggestedQuantity", "TargetQuantity", "OrderQuantity", "Quantity");
    }

    [Fact]
    public void Sem_fornecedor_no_snapshot()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidanceSnapshot),
            "SupplierId", "SupplierName", "Supplier");
    }

    [Fact]
    public void Sem_score_no_snapshot()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidanceSnapshot),
            "PurchaseScore", "BuyScore", "Score");
    }

    [Fact]
    public void Determinismo()
    {
        var rows = new[] { Row(1), Row(2, stock: 0, fridge: 0, vmv30: 2,
            coverageBand: InventoryCoverageBand.Zero, isZeroStockWithTurnover: true) };
        var a = Compose(rows);
        var b = Compose(rows);
        for (int i = 0; i < a.Results.Count; i++)
        {
            Assert.Equal(a.Results[i].ProductId, b.Results[i].ProductId);
            Assert.Equal(a.Results[i].Status, b.Results[i].Status);
            Assert.Equal(a.Results[i].Action, b.Results[i].Action);
            Assert.Equal(a.Results[i].PrimaryReason, b.Results[i].PrimaryReason);
        }
    }

    [Fact]
    public void Engine_chamado_como_autoridade_nao_regras_duplicadas()
    {
        var snap1 = Compose(Row(1, stock: 5, vmv30: 1,
            coverageBand: InventoryCoverageBand.Low, coverageDays: 5));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, snap1.Results[0].Action);

        var snap2 = Compose(Row(1, stock: 50, vmv30: 1,
            coverageBand: InventoryCoverageBand.Normal, coverageDays: 50));
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, snap2.Results[0].Action);
    }

    #endregion

    #region Helpers

    static ProductTurnoverRow Row(
        int id = 1,
        double stock = 30,
        double fridge = 0,
        double vmv30 = 1,
        int history = 120,
        bool evidence = true,
        bool composition = false,
        bool idle = false,
        bool insufficient30 = false,
        InventoryCoverageBand coverageBand = InventoryCoverageBand.Normal,
        double? coverageDays = 30,
        bool isZeroStockWithTurnover = false,
        bool hasLocationStockAnomaly = false) =>
        new()
        {
            ProductId = id,
            Name = "P" + id,
            Code = "C" + id,
            Stock = stock,
            StockFridge = fridge,
            TotalStock = stock + fridge,
            Vmv30 = vmv30,
            HistoryDays = history,
            HasPhysicalAvailabilityEvidence = evidence,
            IsCompositionProduct = composition,
            IsIdle = idle,
            IsHistoryInsufficient30 = insufficient30 || history < 30,
            CoverageBand = coverageBand,
            CoverageDays = coverageDays,
            IsZeroStockWithTurnover = isZeroStockWithTurnover,
            HasLocationStockAnomaly = hasLocationStockAnomaly,
        };

    static InventoryProjectedProduct Project(
        ProductTurnoverRow row,
        double? excessQty = null,
        bool canProjectSku = true,
        bool expired = false,
        bool expiresToday = false,
        bool surplusLot = false,
        bool fridge = false)
    {
        var lots = new List<InventoryProjectionLotResult>();
        if (expired)
            lots.Add(new() { LotId = 901, Kind = InventoryProjectionLotKind.AlreadyExpired,
                AlreadyExpired = true, Quantity = 1 });
        if (expiresToday)
            lots.Add(new() { LotId = 902, Kind = InventoryProjectionLotKind.ExpiresToday,
                Quantity = 1 });
        if (surplusLot)
            lots.Add(new() { LotId = 903, Kind = InventoryProjectionLotKind.Dated,
                Quantity = 5, DaysUntilExpiry = 10, ProjectedSurplusAtExpiry = 3 });

        var skuBlocked = canProjectSku
            ? (row.IsCompositionProduct
                ? InventorySkuProjectionBlockedReason.CompositionProduct
                : InventorySkuProjectionBlockedReason.None)
            : InventorySkuProjectionBlockedReason.InsufficientHistory;

        var expiryBlocked = row.IsCompositionProduct
            ? InventoryExpiryProjectionBlockedReason.CompositionProduct
            : InventoryExpiryProjectionBlockedReason.None;

        if (row.HasLocationStockAnomaly)
            skuBlocked = InventorySkuProjectionBlockedReason.NegativeLocationStock;

        if (row.CoverageBand == InventoryCoverageBand.Negative)
            skuBlocked = InventorySkuProjectionBlockedReason.NegativeStock;

        return new InventoryProjectedProduct
        {
            ProductId = row.ProductId,
            Projection = new InventoryProjectionResult
            {
                SkuBlockedReason = skuBlocked,
                ExpiryBlockedReason = expiryBlocked,
                ProjectedExcessQuantity = excessQty,
                HasLotLocationLimitation = fridge,
                Lots = lots,
            },
        };
    }

    static Dictionary<int, InventoryProjectedProduct> ProjectMap(
        params ProductTurnoverRow[] rows)
    {
        var map = new Dictionary<int, InventoryProjectedProduct>();
        foreach (var r in rows)
            map[r.ProductId] = Project(r);
        return map;
    }

    static InventoryPurchaseGuidanceSnapshot Compose(params ProductTurnoverRow[] rows)
    {
        var map = new Dictionary<int, InventoryProjectedProduct>();
        foreach (var r in rows)
            map[r.ProductId] = Project(r);
        return ComposeRaw(rows, map);
    }

    static InventoryPurchaseGuidanceSnapshot Compose(
        ProductTurnoverRow row,
        double? excessQty = null,
        bool canProjectSku = true,
        bool expired = false,
        bool expiresToday = false,
        bool surplusLot = false,
        bool fridge = false)
    {
        var projected = Project(row, excessQty, canProjectSku, expired, expiresToday, surplusLot, fridge);
        var map = new Dictionary<int, InventoryProjectedProduct>
            { [row.ProductId] = projected };
        return ComposeRaw([row], map);
    }

    static InventoryPurchaseGuidanceSnapshot ComposeRaw(
        IReadOnlyList<ProductTurnoverRow> rows,
        IReadOnlyDictionary<int, InventoryProjectedProduct> map) =>
        InventoryPurchaseGuidanceComposer.Compose(
            new InventoryProjectionSnapshot
            {
                Today = Today,
                QueryCount = 7,
                Intelligence = new InventoryIntelligenceSnapshot
                {
                    Today = Today,
                    QueryCount = 6,
                    Rows = rows.ToList(),
                },
                ByProductId = map,
            });

    static void AssertNoMember(Type type, params string[] names)
    {
        var members = type
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in names)
            Assert.False(members.Contains(name), $"{type.Name} não deve expor {name}");
    }

    static string ReadSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }

    #endregion
}
