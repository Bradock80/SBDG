using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70E-B1 — motor puro de priorização. Sem SQLite, UI, promoção ou compra.
/// Entrada: ProductTurnoverRow + InventoryProjectedProduct (resultado 70D).
/// </summary>
public class InventoryAttentionEngineTests
{
    private static readonly DateTime Today = new(2026, 9, 1);

    static ProductTurnoverRow Turnover(
        int id = 1,
        double stock = 30,
        double fridge = 0,
        double? total = null,
        double vmv7 = 1,
        double vmv30 = 1,
        double vmv90 = 1,
        int history = 45,
        bool evidence = true,
        bool composition = false,
        bool idle = false,
        bool anomaly = false,
        bool insufficient30 = false,
        InventoryCoverageBand band = InventoryCoverageBand.Normal) =>
        new()
        {
            ProductId = id,
            Name = "P",
            Code = "P1",
            Stock = stock,
            StockFridge = fridge,
            TotalStock = total ?? stock + fridge,
            Vmv7 = vmv7,
            Vmv30 = vmv30,
            Vmv90 = vmv90,
            HistoryDays = history,
            HasPhysicalAvailabilityEvidence = evidence,
            IsCompositionProduct = composition,
            IsIdle = idle,
            HasLocationStockAnomaly = anomaly,
            IsHistoryInsufficient7 = history < 7,
            IsHistoryInsufficient30 = insufficient30 || history < 30,
            IsHistoryInsufficient90 = history < 90,
            CoverageBand = band,
        };

    static InventoryProjectionLotInput Lot(
        int id,
        double qty,
        int? daysUntilExpiry,
        double? cost = null,
        bool invalidExpiry = false) =>
        new()
        {
            LotId = id,
            Quantity = qty,
            ExpiryDate = daysUntilExpiry is int d ? Today.AddDays(d) : null,
            UnitCost = cost,
            HasInvalidExpiryText = invalidExpiry,
        };

    static InventoryAttentionResult Eval(
        ProductTurnoverRow row,
        params InventoryProjectionLotInput[] lots) =>
        Eval(row, LotCostSource.LotRecorded, lots);

    static InventoryAttentionResult Eval(
        ProductTurnoverRow row,
        LotCostSource costSource,
        params InventoryProjectionLotInput[] lots)
    {
        var projection = InventoryProjectionEngine.Project(new InventoryProjectionRequest
        {
            Today = Today,
            Vmv30 = InventoryIntelligenceEngine.IsFinite(row.Vmv30) ? row.Vmv30 : 0,
            HistoryDays = row.HistoryDays,
            IsHistoryInsufficient30 = row.IsHistoryInsufficient30,
            HasPhysicalAvailabilityEvidence = row.HasPhysicalAvailabilityEvidence,
            IsCompositionProduct = row.IsCompositionProduct,
            TotalStock = InventoryIntelligenceEngine.IsFinite(row.TotalStock) ? row.TotalStock : 0,
            WarehouseStock = InventoryIntelligenceEngine.IsFinite(row.Stock) ? row.Stock : 0,
            FridgeStock = InventoryIntelligenceEngine.IsFinite(row.StockFridge) ? row.StockFridge : 0,
            HorizonDays = 30,
            Lots = lots,
        });

        var costs = new List<InventoryProjectedLotCost>(lots.Length);
        foreach (var lot in lots)
        {
            costs.Add(new InventoryProjectedLotCost
            {
                LotId = lot.LotId,
                UsedCost = lot.UnitCost,
                CostSource = lot.UnitCost is double c && c > 0.009
                    ? costSource
                    : LotCostSource.Unavailable,
            });
        }

        return InventoryAttentionEngine.Evaluate(row, new InventoryProjectedProduct
        {
            ProductId = row.ProductId,
            Projection = projection,
            LotCosts = costs,
        });
    }

    static InventoryAttentionResult EvalDirect(
        ProductTurnoverRow row,
        InventoryProjectionResult projection,
        IReadOnlyList<InventoryProjectedLotCost>? costs = null) =>
        InventoryAttentionEngine.Evaluate(row, new InventoryProjectedProduct
        {
            ProductId = row.ProductId,
            Projection = projection,
            LotCosts = costs ?? [],
        });

    static void AssertAttention(
        InventoryAttentionResult result,
        InventoryAttentionPriority priority,
        InventoryAttentionFamily family,
        InventoryAttentionReason primary,
        InventoryOperatorAction action,
        InventoryAttentionConfidence confidence)
    {
        Assert.Equal(priority, result.Priority);
        Assert.Equal(family, result.Family);
        Assert.Equal(primary, result.PrimaryReason);
        Assert.Equal(action, result.Action);
        Assert.Equal(confidence, result.Confidence);
        Assert.DoesNotContain(primary, result.SecondaryReasons);
        Assert.Equal(result.SecondaryReasons.Count, result.SecondaryReasons.Distinct().Count());
        Assert.False(double.IsNaN(result.ProjectedExcessQuantity ?? 0));
        Assert.False(double.IsInfinity(result.ProjectedExcessQuantity ?? 0));
        Assert.False(double.IsNaN(result.ProjectedExpirySurplusQuantity ?? 0));
        Assert.False(double.IsInfinity(result.ProjectedExpirySurplusQuantity ?? 0));
    }

    [Fact]
    public void Normal_giro_compativel_sem_sobra_e_validade_distante()
    {
        var result = Eval(Turnover(stock: 30, vmv30: 1), Lot(1, 30, 90));
        AssertAttention(
            result,
            InventoryAttentionPriority.Normal,
            InventoryAttentionFamily.Normal,
            InventoryAttentionReason.None,
            InventoryOperatorAction.None,
            InventoryAttentionConfidence.Reliable);
        Assert.Empty(result.SecondaryReasons);
        Assert.Equal(0, result.ProjectedExcessQuantity);
        Assert.Equal(0, result.ProjectedExpirySurplusQuantity);
    }

    [Fact]
    public void Vencido_RemoveExpired_nunca_EvaluateExcess()
    {
        var result = Eval(Turnover(stock: 10, vmv30: 1), Lot(1, 10, -1));
        AssertAttention(
            result,
            InventoryAttentionPriority.Critical,
            InventoryAttentionFamily.Expiry,
            InventoryAttentionReason.Expired,
            InventoryOperatorAction.RemoveExpired,
            InventoryAttentionConfidence.Reliable);
        Assert.NotEqual(InventoryOperatorAction.EvaluateExcess, result.Action);
        Assert.DoesNotContain(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
    }

    [Fact]
    public void Vence_hoje_PrioritizeSale()
    {
        var result = Eval(Turnover(stock: 10, vmv30: 1), Lot(1, 10, 0));
        AssertAttention(
            result,
            InventoryAttentionPriority.High,
            InventoryAttentionFamily.Expiry,
            InventoryAttentionReason.ExpiresToday,
            InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionConfidence.Reliable);
        Assert.Equal(0, result.NearestDatedDaysUntilExpiry);
    }

    [Theory]
    [InlineData(7, InventoryAttentionPriority.Medium, InventoryAttentionReason.NearExpiryWithoutSurplus, InventoryOperatorAction.PrioritizeSale)]
    [InlineData(8, InventoryAttentionPriority.Low, InventoryAttentionReason.DatedWithoutSurplusInWindow, InventoryOperatorAction.Monitor)]
    [InlineData(30, InventoryAttentionPriority.Low, InventoryAttentionReason.DatedWithoutSurplusInWindow, InventoryOperatorAction.Monitor)]
    public void Dated_sem_sobra_respeita_limites_7_e_30(
        int days,
        InventoryAttentionPriority priority,
        InventoryAttentionReason reason,
        InventoryOperatorAction action)
    {
        var result = Eval(Turnover(stock: 10, vmv30: 10), Lot(1, 10, days));
        Assert.Equal(0, result.ProjectedExpirySurplusQuantity);
        AssertAttention(
            result,
            priority,
            InventoryAttentionFamily.Expiry,
            reason,
            action,
            InventoryAttentionConfidence.Reliable);
        Assert.Equal(days, result.NearestDatedDaysUntilExpiry);
    }

    [Fact]
    public void Dated_31_dias_sem_sobra_nao_entra_na_70E()
    {
        var result = Eval(Turnover(stock: 10, vmv30: 10), Lot(1, 10, 31));
        AssertAttention(
            result,
            InventoryAttentionPriority.Normal,
            InventoryAttentionFamily.Normal,
            InventoryAttentionReason.None,
            InventoryOperatorAction.None,
            InventoryAttentionConfidence.Reliable);
    }

    [Fact]
    public void Validade_proxima_com_sobra_e_High_SurplusAtExpiry()
    {
        var result = Eval(Turnover(stock: 40, vmv30: 1), Lot(1, 40, 7, cost: 2));
        AssertAttention(
            result,
            InventoryAttentionPriority.High,
            InventoryAttentionFamily.Expiry,
            InventoryAttentionReason.SurplusAtExpiry,
            InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionConfidence.Reliable);
        Assert.True(result.ProjectedExpirySurplusQuantity > InventoryAttentionEngine.Epsilon);
        Assert.Equal(
            InventoryProjectionSurplusValueQuality.CompleteRecorded,
            result.SurplusValueQuality);
    }

    [Fact]
    public void Validade_distante_com_sobra_ate_validade_permanece_High()
    {
        var result = Eval(Turnover(stock: 100, vmv30: 1), Lot(1, 100, 90, cost: 2));
        AssertAttention(
            result,
            InventoryAttentionPriority.High,
            InventoryAttentionFamily.Expiry,
            InventoryAttentionReason.SurplusAtExpiry,
            InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionConfidence.Reliable);
        Assert.Contains(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
    }

    [Fact]
    public void Excesso_30d_sem_pressao_de_validade_EvaluateExcess()
    {
        var result = Eval(Turnover(stock: 100, vmv30: 1), Lot(1, 100, 120));
        AssertAttention(
            result,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Excess,
            InventoryAttentionReason.ProjectedExcess30,
            InventoryOperatorAction.EvaluateExcess,
            InventoryAttentionConfidence.Reliable);
        Assert.Equal(70, result.ProjectedExcessQuantity);
        Assert.NotEqual(ValiditySuggestedAction.ConsiderPromotion.ToString(), result.Action.ToString());
    }

    [Fact]
    public void Excesso_exatamente_zero_nao_gera_ProjectedExcess30()
    {
        var result = Eval(Turnover(stock: 30, vmv30: 1), Lot(1, 30, 90));
        Assert.Equal(0, result.ProjectedExcessQuantity);
        Assert.NotEqual(InventoryAttentionReason.ProjectedExcess30, result.PrimaryReason);
        Assert.DoesNotContain(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
    }

    [Fact]
    public void Excesso_igual_ao_epsilon_nao_gera_atencao()
    {
        var row = Turnover(stock: 30 + InventoryAttentionEngine.Epsilon, vmv30: 1);
        var result = EvalDirect(row, new InventoryProjectionResult
        {
            SkuBlockedReason = InventorySkuProjectionBlockedReason.None,
            ProjectedExcessQuantity = InventoryAttentionEngine.Epsilon,
            Lots = [new InventoryProjectionLotResult
            {
                LotId = 1,
                Kind = InventoryProjectionLotKind.Dated,
                Quantity = 30,
                DaysUntilExpiry = 90,
                ProjectedSurplusAtExpiry = 0,
            }],
        });
        Assert.NotEqual(InventoryAttentionReason.ProjectedExcess30, result.PrimaryReason);
        Assert.DoesNotContain(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
    }

    [Fact]
    public void Parado_usa_IsIdle_e_familia_Turnover()
    {
        var result = Eval(Turnover(stock: 30, vmv30: 1, idle: true, history: 90), Lot(1, 30, 90));
        AssertAttention(
            result,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Turnover,
            InventoryAttentionReason.Idle,
            InventoryOperatorAction.Monitor,
            InventoryAttentionConfidence.Reliable);
        Assert.NotEqual(InventoryAttentionFamily.Expiry, result.Family);
    }

    [Fact]
    public void Idle_sem_excesso_usa_Monitor()
    {
        var result = Eval(Turnover(stock: 30, vmv30: 1, idle: true, history: 90), Lot(1, 30, 90));
        Assert.Equal(InventoryAttentionReason.Idle, result.PrimaryReason);
        Assert.Equal(InventoryAttentionFamily.Turnover, result.Family);
        Assert.Equal(InventoryAttentionPriority.Medium, result.Priority);
        Assert.Equal(InventoryOperatorAction.Monitor, result.Action);
        Assert.DoesNotContain(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
        Assert.NotEqual(InventoryOperatorAction.EvaluateExcess, result.Action);
    }

    [Fact]
    public void Idle_com_excesso_preserva_EvaluateExcess()
    {
        var result = Eval(Turnover(stock: 100, vmv30: 1, idle: true, history: 90), Lot(1, 100, 120));
        Assert.Equal(InventoryAttentionReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Contains(InventoryAttentionReason.Idle, result.SecondaryReasons);
        Assert.Equal(InventoryOperatorAction.EvaluateExcess, result.Action);
        Assert.Equal(InventoryAttentionFamily.Excess, result.Family);
    }

    [Fact]
    public void Parado_mais_excesso_excesso_vence()
    {
        var result = Eval(Turnover(stock: 100, vmv30: 1, idle: true, history: 90), Lot(1, 100, 120));
        AssertAttention(
            result,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Excess,
            InventoryAttentionReason.ProjectedExcess30,
            InventoryOperatorAction.EvaluateExcess,
            InventoryAttentionConfidence.Reliable);
        Assert.Equal(new[] { InventoryAttentionReason.Idle }, result.SecondaryReasons);
    }

    [Fact]
    public void Parado_mais_validade_proxima_validade_vence()
    {
        var result = Eval(
            Turnover(stock: 10, vmv30: 10, idle: true, history: 90),
            Lot(1, 10, 5));
        AssertAttention(
            result,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Expiry,
            InventoryAttentionReason.NearExpiryWithoutSurplus,
            InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionConfidence.Reliable);
        Assert.Contains(InventoryAttentionReason.Idle, result.SecondaryReasons);
    }

    [Theory]
    [InlineData(InventorySkuProjectionBlockedReason.NegativeStock, InventoryExpiryProjectionBlockedReason.None, InventoryAttentionReason.NegativeStock)]
    [InlineData(InventorySkuProjectionBlockedReason.NegativeLocationStock, InventoryExpiryProjectionBlockedReason.None, InventoryAttentionReason.NegativeLocationStock)]
    [InlineData(InventorySkuProjectionBlockedReason.InconsistentStockTotals, InventoryExpiryProjectionBlockedReason.None, InventoryAttentionReason.InconsistentStockTotals)]
    [InlineData(InventorySkuProjectionBlockedReason.None, InventoryExpiryProjectionBlockedReason.NegativeWarehouseStock, InventoryAttentionReason.NegativeWarehouseStock)]
    [InlineData(InventorySkuProjectionBlockedReason.None, InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse, InventoryAttentionReason.TrackedQuantityExceedsWarehouse)]
    [InlineData(InventorySkuProjectionBlockedReason.None, InventoryExpiryProjectionBlockedReason.DuplicateLotId, InventoryAttentionReason.DuplicateLotId)]
    [InlineData(InventorySkuProjectionBlockedReason.None, InventoryExpiryProjectionBlockedReason.InvalidLotQuantity, InventoryAttentionReason.InvalidLotQuantity)]
    [InlineData(InventorySkuProjectionBlockedReason.None, InventoryExpiryProjectionBlockedReason.InvalidExpiryDate, InventoryAttentionReason.InvalidExpiryDate)]
    [InlineData(InventorySkuProjectionBlockedReason.InvalidInput, InventoryExpiryProjectionBlockedReason.None, InventoryAttentionReason.InvalidInput)]
    public void Blocked_estrutural_e_Critical_ReviewData_Unavailable(
        InventorySkuProjectionBlockedReason sku,
        InventoryExpiryProjectionBlockedReason expiry,
        InventoryAttentionReason expected)
    {
        var result = EvalDirect(Turnover(), new InventoryProjectionResult
        {
            SkuBlockedReason = sku,
            ExpiryBlockedReason = expiry,
        });
        AssertAttention(
            result,
            InventoryAttentionPriority.Critical,
            InventoryAttentionFamily.DataQuality,
            expected,
            InventoryOperatorAction.ReviewData,
            InventoryAttentionConfidence.Unavailable);
    }

    [Fact]
    public void Estoque_negativo_via_70D()
    {
        var result = Eval(Turnover(stock: -8, vmv30: 1, band: InventoryCoverageBand.Negative));
        Assert.Equal(InventoryAttentionReason.NegativeStock, result.PrimaryReason);
        Assert.Equal(InventoryOperatorAction.ReviewData, result.Action);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
    }

    [Fact]
    public void Localizacao_negativa_via_70D()
    {
        var result = Eval(Turnover(stock: 20, fridge: -5, vmv30: 1, anomaly: true));
        Assert.Equal(InventoryAttentionReason.NegativeLocationStock, result.PrimaryReason);
        Assert.Equal(InventoryAttentionFamily.DataQuality, result.Family);
        Assert.Equal(InventoryOperatorAction.ReviewData, result.Action);
    }

    [Fact]
    public void Total_inconsistente_via_70D()
    {
        var result = Eval(Turnover(stock: 10, fridge: 0, total: 40, vmv30: 1));
        Assert.Equal(InventoryAttentionReason.InconsistentStockTotals, result.PrimaryReason);
    }

    [Fact]
    public void Tracked_maior_que_deposito_via_70D()
    {
        var result = Eval(Turnover(stock: 20, vmv30: 1), Lot(1, 40, 10));
        Assert.Equal(InventoryAttentionReason.TrackedQuantityExceedsWarehouse, result.PrimaryReason);
        Assert.Equal(InventoryAttentionPriority.Critical, result.Priority);
    }

    [Fact]
    public void Duplicate_lot_id_via_70D()
    {
        var result = Eval(Turnover(stock: 40, vmv30: 1), Lot(7, 20, 10), Lot(7, 20, 20));
        Assert.Equal(InventoryAttentionReason.DuplicateLotId, result.PrimaryReason);
        Assert.Empty(Eval(Turnover(stock: 40, vmv30: 1), Lot(7, 20, 10), Lot(7, 20, 20))
            .SecondaryReasons.Where(r => r == InventoryAttentionReason.DuplicateLotId).Skip(1));
    }

    [Fact]
    public void Invalid_lot_quantity_via_70D()
    {
        var result = Eval(Turnover(stock: 10, vmv30: 1), Lot(1, -4, 10));
        Assert.Equal(InventoryAttentionReason.InvalidLotQuantity, result.PrimaryReason);
    }

    [Fact]
    public void Invalid_expiry_nao_e_Undated()
    {
        var result = Eval(Turnover(stock: 40, vmv30: 1), Lot(1, 40, null, invalidExpiry: true));
        Assert.Equal(InventoryAttentionReason.InvalidExpiryDate, result.PrimaryReason);
        Assert.NotEqual(InventoryAttentionReason.Undated, result.PrimaryReason);
        Assert.DoesNotContain(InventoryAttentionReason.Undated, result.SecondaryReasons);
        Assert.Equal(InventoryOperatorAction.ReviewData, result.Action);
    }

    [Fact]
    public void Historico_insuficiente_nao_e_encalhado_nem_excesso()
    {
        var result = Eval(
            Turnover(stock: 40, vmv30: 1, history: 10, insufficient30: true),
            Lot(1, 40, 90));
        AssertAttention(
            result,
            InventoryAttentionPriority.Normal,
            InventoryAttentionFamily.Normal,
            InventoryAttentionReason.InsufficientHistory,
            InventoryOperatorAction.None,
            InventoryAttentionConfidence.Unavailable);
        Assert.False(result.PrimaryReason == InventoryAttentionReason.Idle);
        Assert.DoesNotContain(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
        Assert.Null(result.ProjectedExcessQuantity);
    }

    [Fact]
    public void Produto_novo_mesmo_tratamento_de_historico_insuficiente()
    {
        var result = Eval(Turnover(stock: 5, vmv30: 0.5, history: 3, insufficient30: true));
        Assert.Equal(InventoryAttentionReason.InsufficientHistory, result.PrimaryReason);
        Assert.Equal(InventoryOperatorAction.None, result.Action);
        Assert.NotEqual(InventoryAttentionReason.Idle, result.PrimaryReason);
    }

    [Fact]
    public void Sem_evidencia_fisica_nao_e_encalhado()
    {
        var result = Eval(Turnover(stock: 0, vmv30: 0, history: 5, evidence: false, insufficient30: true));
        Assert.Equal(InventoryAttentionReason.NoPhysicalEvidence, result.PrimaryReason);
        Assert.Equal(InventoryAttentionPriority.Normal, result.Priority);
        Assert.Equal(InventoryOperatorAction.None, result.Action);
        Assert.NotEqual(InventoryAttentionReason.Idle, result.PrimaryReason);
    }

    [Fact]
    public void Historico_insuficiente_com_vencido_observavel_validade_vence()
    {
        var result = Eval(
            Turnover(stock: 10, vmv30: 1, history: 8, insufficient30: true),
            Lot(1, 10, -2));
        AssertAttention(
            result,
            InventoryAttentionPriority.Critical,
            InventoryAttentionFamily.Expiry,
            InventoryAttentionReason.Expired,
            InventoryOperatorAction.RemoveExpired,
            InventoryAttentionConfidence.Limited);
        Assert.Contains(InventoryAttentionReason.InsufficientHistory, result.SecondaryReasons);
    }

    [Fact]
    public void Vmv30_zero_nao_projeta_excesso()
    {
        var result = Eval(Turnover(stock: 40, vmv30: 0, vmv7: 0, vmv90: 0), Lot(1, 40, 90));
        Assert.Equal(InventoryAttentionReason.NoObservableDemand, result.PrimaryReason);
        Assert.Equal(InventoryAttentionPriority.Normal, result.Priority);
        Assert.Equal(InventoryOperatorAction.None, result.Action);
        Assert.Null(result.ProjectedExcessQuantity);
        Assert.DoesNotContain(InventoryAttentionReason.Idle, result.SecondaryReasons);
    }

    [Fact]
    public void Kit_nao_herda_giro_nem_IsIdle()
    {
        var result = Eval(
            Turnover(stock: 40, vmv30: 1, composition: true, idle: true, history: 90),
            Lot(1, 40, 90));
        AssertAttention(
            result,
            InventoryAttentionPriority.Normal,
            InventoryAttentionFamily.Normal,
            InventoryAttentionReason.CompositionProduct,
            InventoryOperatorAction.None,
            InventoryAttentionConfidence.Unavailable);
        Assert.DoesNotContain(InventoryAttentionReason.Idle, result.SecondaryReasons);
        Assert.DoesNotContain(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
    }

    [Fact]
    public void Kit_com_lote_vencido_nao_esconde_Expired()
    {
        var result = Eval(
            Turnover(stock: 10, vmv30: 1, composition: true),
            Lot(1, 10, -1));
        Assert.Equal(InventoryAttentionReason.Expired, result.PrimaryReason);
        Assert.Contains(InventoryAttentionReason.CompositionProduct, result.SecondaryReasons);
        Assert.Equal(InventoryOperatorAction.RemoveExpired, result.Action);
    }

    [Fact]
    public void Undated_ReviewData_nao_Monitor()
    {
        var result = Eval(Turnover(stock: 20, vmv30: 10), Lot(1, 20, null));
        AssertAttention(
            result,
            InventoryAttentionPriority.Low,
            InventoryAttentionFamily.DataQuality,
            InventoryAttentionReason.Undated,
            InventoryOperatorAction.ReviewData,
            InventoryAttentionConfidence.Reliable);
        Assert.NotEqual(InventoryOperatorAction.Monitor, result.Action);
    }

    [Fact]
    public void NoLot_com_deposito_ReviewData()
    {
        var result = Eval(Turnover(stock: 25, vmv30: 10));
        AssertAttention(
            result,
            InventoryAttentionPriority.Low,
            InventoryAttentionFamily.DataQuality,
            InventoryAttentionReason.NoLot,
            InventoryOperatorAction.ReviewData,
            InventoryAttentionConfidence.Reliable);
        Assert.NotEqual(InventoryOperatorAction.Monitor, result.Action);
    }

    [Fact]
    public void Undated_mais_excesso_excesso_e_primario()
    {
        var result = Eval(Turnover(stock: 100, vmv30: 1), Lot(1, 100, null));
        Assert.Equal(InventoryAttentionReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Contains(InventoryAttentionReason.Undated, result.SecondaryReasons);
        Assert.Equal(InventoryOperatorAction.EvaluateExcess, result.Action);
    }

    [Fact]
    public void NoLot_mais_Idle_Idle_e_primario()
    {
        var result = Eval(Turnover(stock: 30, vmv30: 1, idle: true, history: 90));
        Assert.Equal(InventoryAttentionReason.Idle, result.PrimaryReason);
        Assert.Contains(InventoryAttentionReason.NoLot, result.SecondaryReasons);
    }

    [Fact]
    public void Geladeira_com_excesso_Limited_sem_mudar_prioridade()
    {
        var withoutFridge = Eval(Turnover(stock: 100, fridge: 0, vmv30: 1), Lot(1, 100, 120));
        var withFridge = Eval(Turnover(stock: 80, fridge: 20, vmv30: 1), Lot(1, 80, 120));
        Assert.Equal(withoutFridge.Priority, withFridge.Priority);
        Assert.Equal(InventoryAttentionReason.ProjectedExcess30, withFridge.PrimaryReason);
        Assert.Equal(InventoryAttentionConfidence.Limited, withFridge.Confidence);
        Assert.Equal(InventoryAttentionConfidence.Reliable, withoutFridge.Confidence);
    }

    [Fact]
    public void Custo_registrado_Reliable_quando_ha_sobra_ate_validade()
    {
        var result = Eval(
            Turnover(stock: 40, vmv30: 1),
            LotCostSource.LotRecorded,
            Lot(1, 40, 10, cost: 3));
        Assert.Equal(InventoryAttentionReason.SurplusAtExpiry, result.PrimaryReason);
        Assert.Equal(InventoryProjectionSurplusValueQuality.CompleteRecorded, result.SurplusValueQuality);
        Assert.Equal(InventoryAttentionConfidence.Reliable, result.Confidence);
    }

    [Fact]
    public void Custo_estimado_Limited()
    {
        var result = Eval(
            Turnover(stock: 40, vmv30: 1),
            LotCostSource.CurrentAverageEstimate,
            Lot(1, 40, 10, cost: 3));
        Assert.Equal(InventoryAttentionReason.SurplusAtExpiry, result.PrimaryReason);
        Assert.Equal(
            InventoryProjectionSurplusValueQuality.CompleteWithEstimate,
            result.SurplusValueQuality);
        Assert.Equal(InventoryAttentionConfidence.Limited, result.Confidence);
    }

    [Fact]
    public void Custo_parcial_Limited()
    {
        var row = Turnover(stock: 80, vmv30: 1);
        var projection = InventoryProjectionEngine.Project(new InventoryProjectionRequest
        {
            Today = Today,
            Vmv30 = 1,
            HistoryDays = 45,
            HasPhysicalAvailabilityEvidence = true,
            TotalStock = 80,
            WarehouseStock = 80,
            FridgeStock = 0,
            HorizonDays = 30,
            Lots = [Lot(1, 40, 10, cost: 2), Lot(2, 40, 10)],
        });
        var result = EvalDirect(row, projection,
        [
            new InventoryProjectedLotCost
            {
                LotId = 1,
                UsedCost = 2,
                CostSource = LotCostSource.LotRecorded,
            },
            new InventoryProjectedLotCost
            {
                LotId = 2,
                UsedCost = null,
                CostSource = LotCostSource.Unavailable,
            },
        ]);
        Assert.Equal(InventoryAttentionReason.SurplusAtExpiry, result.PrimaryReason);
        Assert.Equal(InventoryProjectionSurplusValueQuality.Partial, result.SurplusValueQuality);
        Assert.Equal(InventoryAttentionConfidence.Limited, result.Confidence);
    }

    [Fact]
    public void Custo_indisponivel_nao_vira_prejuizo_e_Limited()
    {
        var result = Eval(Turnover(stock: 40, vmv30: 1), Lot(1, 40, 10));
        Assert.Equal(InventoryAttentionReason.SurplusAtExpiry, result.PrimaryReason);
        Assert.Equal(InventoryProjectionSurplusValueQuality.Unavailable, result.SurplusValueQuality);
        Assert.Equal(InventoryAttentionConfidence.Limited, result.Confidence);
        Assert.Equal(InventoryOperatorAction.PrioritizeSale, result.Action);
    }

    [Fact]
    public void Multiplos_motivos_preservados_sem_duplicata()
    {
        var result = Eval(
            Turnover(stock: 100, vmv30: 1, idle: true, history: 90),
            Lot(1, 40, 10, cost: 2),
            Lot(2, 60, 90, cost: 2));
        Assert.Equal(InventoryAttentionReason.SurplusAtExpiry, result.PrimaryReason);
        Assert.Contains(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
        Assert.Contains(InventoryAttentionReason.Idle, result.SecondaryReasons);
        Assert.Equal(result.SecondaryReasons.Distinct().Count(), result.SecondaryReasons.Count);
        var ordered = InventoryAttentionEngine.ReasonPrecedence
            .Where(r => result.SecondaryReasons.Contains(r) || r == result.PrimaryReason)
            .ToList();
        Assert.Equal(result.PrimaryReason, ordered[0]);
        Assert.Equal(result.SecondaryReasons, ordered.Skip(1).ToList());
    }

    [Fact]
    public void Dado_inconsistente_nao_esconde_vencido()
    {
        var result = EvalDirect(
            Turnover(stock: -4, vmv30: 1, band: InventoryCoverageBand.Negative),
            new InventoryProjectionResult
            {
                SkuBlockedReason = InventorySkuProjectionBlockedReason.NegativeStock,
                ExpiryBlockedReason = InventoryExpiryProjectionBlockedReason.None,
                Lots =
                [
                    new InventoryProjectionLotResult
                    {
                        LotId = 1,
                        Kind = InventoryProjectionLotKind.AlreadyExpired,
                        AlreadyExpired = true,
                        Quantity = 4,
                        DaysUntilExpiry = -3,
                    },
                ],
            });
        Assert.Equal(InventoryAttentionReason.NegativeStock, result.PrimaryReason);
        Assert.Contains(InventoryAttentionReason.Expired, result.SecondaryReasons);
        Assert.Equal(InventoryOperatorAction.ReviewData, result.Action);
        Assert.NotEqual(InventoryOperatorAction.RemoveExpired, result.Action);
    }

    [Fact]
    public void Vencido_com_excesso_RemoveExpired()
    {
        var result = Eval(Turnover(stock: 100, vmv30: 1), Lot(1, 100, -5));
        Assert.Equal(InventoryAttentionReason.Expired, result.PrimaryReason);
        Assert.Equal(InventoryOperatorAction.RemoveExpired, result.Action);
        Assert.Contains(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
    }

    [Fact]
    public void Vence_hoje_com_excesso_PrioritizeSale()
    {
        var result = Eval(Turnover(stock: 100, vmv30: 1), Lot(1, 100, 0));
        Assert.Equal(InventoryAttentionReason.ExpiresToday, result.PrimaryReason);
        Assert.Contains(InventoryAttentionReason.ProjectedExcess30, result.SecondaryReasons);
        Assert.Equal(InventoryOperatorAction.PrioritizeSale, result.Action);
    }

    [Fact]
    public void CoverageBand_Critical_nao_sobe_prioridade_70E()
    {
        var result = Eval(
            Turnover(stock: 2, vmv30: 1, band: InventoryCoverageBand.Critical),
            Lot(1, 2, 90));
        Assert.Equal(InventoryAttentionPriority.Normal, result.Priority);
        Assert.Equal(InventoryAttentionReason.None, result.PrimaryReason);
    }

    [Fact]
    public void Vmv7_nao_altera_Priority()
    {
        var slow = Eval(Turnover(stock: 100, vmv30: 1, vmv7: 0.1), Lot(1, 100, 120));
        var fast = Eval(Turnover(stock: 100, vmv30: 1, vmv7: 5), Lot(1, 100, 120));
        Assert.Equal(slow.Priority, fast.Priority);
        Assert.Equal(slow.PrimaryReason, fast.PrimaryReason);
        Assert.Equal(slow.Action, fast.Action);
        Assert.Equal(InventoryAttentionEngine.VmvRecentAccelerationRatio, 1.25);
        Assert.Equal(InventoryAttentionEngine.VmvRecentDecelerationRatio, 0.5);
        Assert.Equal(InventoryAttentionEngine.Vmv30ExceptionalVs90Ratio, 1.5);
    }

    [Fact]
    public void Determinismo_ordem_de_lotes_equivalente()
    {
        var row = Turnover(stock: 80, vmv30: 1, idle: true, history: 90);
        var a = Eval(row, Lot(2, 40, 90), Lot(1, 40, 10, cost: 2));
        var b = Eval(row, Lot(1, 40, 10, cost: 2), Lot(2, 40, 90));
        Assert.Equal(a.Priority, b.Priority);
        Assert.Equal(a.Family, b.Family);
        Assert.Equal(a.PrimaryReason, b.PrimaryReason);
        Assert.Equal(a.SecondaryReasons, b.SecondaryReasons);
        Assert.Equal(a.Action, b.Action);
        Assert.Equal(a.Confidence, b.Confidence);
    }

    [Fact]
    public void Apply_preserva_ordem_das_linhas_e_nao_muda_QueryCount()
    {
        var first = Turnover(id: 11, stock: 30, vmv30: 1);
        var second = Turnover(id: 22, stock: 10, vmv30: 1);
        var snapshot = new InventoryProjectionSnapshot
        {
            QueryCount = 7,
            Intelligence = new InventoryIntelligenceSnapshot { Rows = [second, first] },
            ByProductId = new Dictionary<int, InventoryProjectedProduct>
            {
                [11] = new()
                {
                    ProductId = 11,
                    Projection = InventoryProjectionEngine.Project(new InventoryProjectionRequest
                    {
                        Today = Today,
                        Vmv30 = 1,
                        HistoryDays = 45,
                        HasPhysicalAvailabilityEvidence = true,
                        TotalStock = 30,
                        WarehouseStock = 30,
                        HorizonDays = 30,
                        Lots = [Lot(1, 30, 90)],
                    }),
                },
                [22] = new()
                {
                    ProductId = 22,
                    Projection = InventoryProjectionEngine.Project(new InventoryProjectionRequest
                    {
                        Today = Today,
                        Vmv30 = 1,
                        HistoryDays = 45,
                        HasPhysicalAvailabilityEvidence = true,
                        TotalStock = 10,
                        WarehouseStock = 10,
                        HorizonDays = 30,
                        Lots = [Lot(2, 10, -1)],
                    }),
                },
            },
        };

        var results = InventoryAttentionEngine.Apply(snapshot);
        Assert.Equal(7, snapshot.QueryCount);
        Assert.Equal(new[] { 22, 11 }, results.Select(r => r.ProductId));
        Assert.Equal(InventoryAttentionReason.Expired, results[0].PrimaryReason);
        Assert.Equal(InventoryAttentionReason.None, results[1].PrimaryReason);
    }

    [Fact]
    public void Nan_no_giro_vira_InvalidInput()
    {
        var result = EvalDirect(
            Turnover(stock: double.NaN, vmv30: 1),
            new InventoryProjectionResult());
        Assert.Equal(InventoryAttentionReason.InvalidInput, result.PrimaryReason);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
        Assert.Null(result.ProjectedExcessQuantity);
    }

    [Fact]
    public void Infinity_no_excesso_nao_vaza()
    {
        var result = EvalDirect(Turnover(), new InventoryProjectionResult
        {
            SkuBlockedReason = InventorySkuProjectionBlockedReason.None,
            ProjectedExcessQuantity = double.PositiveInfinity,
        });
        Assert.Equal(InventoryAttentionReason.InvalidInput, result.PrimaryReason);
        Assert.Null(result.ProjectedExcessQuantity);
        Assert.False(double.IsInfinity(result.ProjectedExcessQuantity ?? 0));
    }

    [Fact]
    public void OperatorAction_nao_inclui_ConsiderPromotion()
    {
        Assert.DoesNotContain(
            "ConsiderPromotion",
            Enum.GetNames<InventoryOperatorAction>());
        Assert.Contains("EvaluateExcess", Enum.GetNames<InventoryOperatorAction>());
    }

    [Fact]
    public void Engine_source_nao_tem_io_promocao_nem_ui()
    {
        var path = FindSource("src", "SGDB.App", "Services", "InventoryAttentionEngine.cs");
        Assert.True(File.Exists(path), path);
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Data", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryIntelligenceService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidityControlService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsiderPromotion", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreNetwork", source, StringComparison.Ordinal);
        Assert.DoesNotContain("combo", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Model_source_nao_emite_promocao()
    {
        var path = FindSource("src", "SGDB.App", "Models", "InventoryAttention.cs");
        Assert.True(File.Exists(path), path);
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("ConsiderPromotion", source, StringComparison.Ordinal);
        Assert.Contains("EvaluateExcess", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
    }

    static string FindSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return "";
    }
}
