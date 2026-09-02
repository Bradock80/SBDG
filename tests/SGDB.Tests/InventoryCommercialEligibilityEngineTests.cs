using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B1 — motor puro de elegibilidade comercial. Sem SQLite, UI, preço,
/// promoção, combo ou compra. Entrada: 70C + 70D + 70E já calculados.
/// </summary>
public class InventoryCommercialEligibilityEngineTests
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
        InventoryCoverageBand band = InventoryCoverageBand.Attention) =>
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
            CoverageDays = vmv30 > InventoryIntelligenceEngine.Epsilon
                ? (total ?? stock + fridge) / vmv30
                : null,
        };

    static InventoryProjectionLotInput Lot(
        int id,
        double qty,
        int? daysUntilExpiry,
        double? cost = 2,
        bool invalidExpiry = false) =>
        new()
        {
            LotId = id,
            Quantity = qty,
            ExpiryDate = daysUntilExpiry is int d ? Today.AddDays(d) : null,
            UnitCost = cost,
            HasInvalidExpiryText = invalidExpiry,
        };

    static (ProductTurnoverRow Row, InventoryProjectedProduct Projected, InventoryAttentionResult Attention)
        Pipeline(ProductTurnoverRow row, params InventoryProjectionLotInput[] lots)
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
                    ? LotCostSource.LotRecorded
                    : LotCostSource.Unavailable,
            });
        }

        var projected = new InventoryProjectedProduct
        {
            ProductId = row.ProductId,
            Projection = projection,
            LotCosts = costs,
        };
        var attention = InventoryAttentionEngine.Evaluate(row, projected);
        return (row, projected, attention);
    }

    static InventoryCommercialEligibilityResult Eval(
        ProductTurnoverRow row,
        params InventoryProjectionLotInput[] lots)
    {
        var pipe = Pipeline(row, lots);
        return InventoryCommercialEligibilityEngine.Evaluate(pipe.Row, pipe.Projected, pipe.Attention);
    }

    static InventoryProjectionLotInput FarLot(double qty) => Lot(1, qty, 90);

    [Fact]
    public void QueryCount_e_zero()
    {
        Assert.Equal(0, InventoryCommercialEligibilityEngine.ExpectedQueryCount);
        Assert.Equal(InventoryIntelligenceEngine.Epsilon, InventoryCommercialEligibilityEngine.Epsilon);
    }

    [Fact]
    public void Expired_com_excesso_nao_e_candidato()
    {
        var row = Turnover(stock: 50, vmv30: 1, band: InventoryCoverageBand.Normal);
        var result = Eval(row, Lot(1, 50, -2));
        Assert.Equal(InventoryCommercialEligibilityKind.NoCommercialRecommendation, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.Expired, result.PrimaryReason);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
    }

    [Fact]
    public void Expired_com_Idle_nao_e_candidato()
    {
        var row = Turnover(stock: 40, vmv30: 0.2, idle: true, history: 120);
        var result = Eval(row, Lot(1, 40, -1));
        Assert.Equal(InventoryCommercialEligibilityKind.NoCommercialRecommendation, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.Expired, result.PrimaryReason);
    }

    [Fact]
    public void Expired_com_excesso_e_Idle_nao_e_candidato()
    {
        var row = Turnover(stock: 80, vmv30: 1, idle: true, history: 120, band: InventoryCoverageBand.Normal);
        var result = Eval(row, Lot(1, 80, -5));
        Assert.Equal(InventoryCommercialEligibilityKind.NoCommercialRecommendation, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.Expired, result.PrimaryReason);
        Assert.DoesNotContain(
            InventoryCommercialEligibilityReason.ProjectedExcess,
            new[] { result.PrimaryReason });
    }

    [Fact]
    public void ExpiresToday_e_MonitorOnly()
    {
        var row = Turnover(stock: 20, vmv30: 1);
        var result = Eval(row, Lot(1, 20, 0));
        Assert.Equal(InventoryCommercialEligibilityKind.MonitorOnly, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ExpiresToday, result.PrimaryReason);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
    }

    [Fact]
    public void ExpiresToday_com_excesso_permanece_MonitorOnly_nao_promocao()
    {
        var pipe = Pipeline(
            Turnover(stock: 80, vmv30: 1, band: InventoryCoverageBand.Normal),
            Lot(1, 80, 0));
        var result = InventoryCommercialEligibilityEngine.Evaluate(
            pipe.Row, pipe.Projected, pipe.Attention);

        Assert.True(
            pipe.Projected.Projection.ProjectedExcessQuantity is double excess && excess > InventoryCommercialEligibilityEngine.Epsilon);
        Assert.Equal(InventoryCommercialEligibilityKind.MonitorOnly, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ExpiresToday, result.PrimaryReason);
        Assert.Contains(InventoryCommercialEligibilityReason.ProjectedExcess, result.SecondaryReasons);
        Assert.NotEqual(InventoryOperatorAction.EvaluateExcess, InventoryOperatorAction.None);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
    }

    [Fact]
    public void Sobra_ate_validade_e_candidato_quando_seguro()
    {
        var row = Turnover(stock: 20, vmv30: 1, band: InventoryCoverageBand.Low);
        var result = Eval(row, Lot(1, 20, 5));
        Assert.Equal(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ProjectedExpirySurplus, result.PrimaryReason);
        Assert.NotEqual(InventoryAttentionConfidence.Unavailable, result.Confidence);
    }

    [Fact]
    public void Excesso_30d_e_candidato_quando_seguro()
    {
        var pipe = Pipeline(
            Turnover(stock: 80, vmv30: 1, band: InventoryCoverageBand.Normal),
            FarLot(80));
        var result = InventoryCommercialEligibilityEngine.Evaluate(
            pipe.Row, pipe.Projected, pipe.Attention);

        Assert.Equal(InventoryOperatorAction.EvaluateExcess, pipe.Attention.Action);
        Assert.Equal(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ProjectedExcess, result.PrimaryReason);
    }

    [Fact]
    public void Idle_e_candidato_quando_seguro()
    {
        var row = Turnover(
            stock: 12,
            vmv30: 1,
            idle: true,
            history: 120,
            band: InventoryCoverageBand.Attention);
        var result = Eval(row, FarLot(12));
        Assert.Equal(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.Idle, result.PrimaryReason);
        Assert.DoesNotContain(InventoryCommercialEligibilityReason.ProjectedExcess, result.SecondaryReasons);
    }

    [Fact]
    public void Excesso_mais_Idle_candidato_com_razoes_deterministicas()
    {
        var result = Eval(
            Turnover(stock: 80, vmv30: 1, idle: true, history: 120, band: InventoryCoverageBand.Normal),
            FarLot(80));
        Assert.Equal(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ProjectedExcess, result.PrimaryReason);
        Assert.Contains(InventoryCommercialEligibilityReason.Idle, result.SecondaryReasons);
        Assert.True(
            Array.IndexOf(InventoryCommercialEligibilityEngine.ReasonPrecedence, result.PrimaryReason)
            < Array.IndexOf(InventoryCommercialEligibilityEngine.ReasonPrecedence, InventoryCommercialEligibilityReason.Idle));
    }

    [Fact]
    public void CoverageBand_Normal_sozinho_nao_e_candidato()
    {
        var result = Eval(
            Turnover(stock: 20, vmv30: 1, band: InventoryCoverageBand.Normal),
            FarLot(20));
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.Equal(InventoryCoverageBand.Normal, InventoryCoverageBand.Normal);
    }

    [Fact]
    public void Cobertura_elevada_sem_excesso_e_MonitorOnly()
    {
        var result = Eval(
            Turnover(stock: 20, vmv30: 1, band: InventoryCoverageBand.Normal),
            FarLot(20));
        Assert.Equal(InventoryCommercialEligibilityKind.MonitorOnly, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.HighCoverageWithoutExcess, result.PrimaryReason);
    }

    [Fact]
    public void Zero_estoque_sem_recomendacao()
    {
        var result = Eval(
            Turnover(stock: 0, vmv30: 1, band: InventoryCoverageBand.Zero),
            FarLot(0));
        Assert.Equal(InventoryCommercialEligibilityKind.NoCommercialRecommendation, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ZeroStock, result.PrimaryReason);
    }

    [Fact]
    public void Estoque_negativo_e_ReviewData()
    {
        var result = Eval(
            Turnover(stock: -4, vmv30: 1, total: -4, band: InventoryCoverageBand.Negative),
            FarLot(0));
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.NegativeStock, result.PrimaryReason);
    }

    [Fact]
    public void Estoque_inconsistente_e_ReviewData()
    {
        var row = Turnover(stock: 10, fridge: 5, total: 10);
        var pipe = Pipeline(row, FarLot(10));
        var projection = pipe.Projected.Projection;
        var blocked = new InventoryProjectionResult
        {
            SkuBlockedReason = InventorySkuProjectionBlockedReason.InconsistentStockTotals,
            ExpiryBlockedReason = InventoryExpiryProjectionBlockedReason.InconsistentStockTotals,
            HorizonDays = 30,
            Lots = projection.Lots,
            TrackedLotQuantity = projection.TrackedLotQuantity,
            UntrackedWarehouseQuantity = projection.UntrackedWarehouseQuantity,
        };
        var projected = new InventoryProjectedProduct
        {
            ProductId = row.ProductId,
            Projection = blocked,
            LotCosts = pipe.Projected.LotCosts,
        };
        var attention = InventoryAttentionEngine.Evaluate(row, projected);
        var result = InventoryCommercialEligibilityEngine.Evaluate(row, projected, attention);
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.InconsistentStockTotals, result.PrimaryReason);
    }

    [Fact]
    public void Historico_insuficiente_e_ReviewData()
    {
        var result = Eval(
            Turnover(stock: 40, vmv30: 1, history: 10, insufficient30: true),
            FarLot(40));
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.InsufficientHistory, result.PrimaryReason);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
    }

    [Fact]
    public void Sem_evidencia_fisica_e_ReviewData()
    {
        var result = Eval(
            Turnover(stock: 40, vmv30: 1, evidence: false),
            FarLot(40));
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.NoPhysicalEvidence, result.PrimaryReason);
    }

    [Fact]
    public void CompositionProduct_nao_e_candidato()
    {
        var result = Eval(
            Turnover(stock: 80, vmv30: 1, composition: true, band: InventoryCoverageBand.Normal),
            FarLot(80));
        Assert.Equal(InventoryCommercialEligibilityKind.NoCommercialRecommendation, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.CompositionProduct, result.PrimaryReason);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
    }

    [Fact]
    public void ProjectionMissing_e_ReviewData()
    {
        var row = Turnover();
        var attention = new InventoryAttentionResult
        {
            ProductId = row.ProductId,
            Family = InventoryAttentionFamily.DataQuality,
            PrimaryReason = InventoryAttentionReason.ProjectionMissing,
            Action = InventoryOperatorAction.ReviewData,
            Confidence = InventoryAttentionConfidence.Unavailable,
        };
        var result = InventoryCommercialEligibilityEngine.Evaluate(
            row, new InventoryProjectedProduct { ProductId = row.ProductId }, attention);
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ProjectionMissing, result.PrimaryReason);
    }

    [Fact]
    public void DuplicateProjection_e_ReviewData()
    {
        var row = Turnover();
        var attention = new InventoryAttentionResult
        {
            ProductId = row.ProductId,
            Family = InventoryAttentionFamily.DataQuality,
            PrimaryReason = InventoryAttentionReason.DuplicateProjection,
            Action = InventoryOperatorAction.ReviewData,
            Confidence = InventoryAttentionConfidence.Unavailable,
        };
        var result = InventoryCommercialEligibilityEngine.Evaluate(
            row, new InventoryProjectedProduct { ProductId = row.ProductId }, attention);
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.DuplicateProjection, result.PrimaryReason);
    }

    [Fact]
    public void Confidence_Unavailable_nunca_e_candidato()
    {
        var row = Turnover(stock: 80, vmv30: 1, band: InventoryCoverageBand.Normal);
        var pipe = Pipeline(row, FarLot(80));
        var attention = new InventoryAttentionResult
        {
            ProductId = row.ProductId,
            Family = pipe.Attention.Family,
            PrimaryReason = pipe.Attention.PrimaryReason,
            SecondaryReasons = pipe.Attention.SecondaryReasons,
            Action = pipe.Attention.Action,
            Confidence = InventoryAttentionConfidence.Unavailable,
            ProjectedExcessQuantity = pipe.Attention.ProjectedExcessQuantity,
            ProjectedExpirySurplusQuantity = pipe.Attention.ProjectedExpirySurplusQuantity,
        };
        var result = InventoryCommercialEligibilityEngine.Evaluate(row, pipe.Projected, attention);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
    }

    [Fact]
    public void Confidence_Limited_com_tese_permanece_Limited_nao_vira_Reliable()
    {
        var pipe = Pipeline(
            Turnover(stock: 20, vmv30: 1, band: InventoryCoverageBand.Low),
            Lot(1, 20, 5, cost: 0));
        Assert.Equal(InventoryAttentionConfidence.Limited, pipe.Attention.Confidence);
        var result = InventoryCommercialEligibilityEngine.Evaluate(
            pipe.Row, pipe.Projected, pipe.Attention);
        Assert.Equal(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ProjectedExpirySurplus, result.PrimaryReason);
        Assert.Equal(InventoryAttentionConfidence.Limited, result.Confidence);
        Assert.NotEqual(InventoryAttentionConfidence.Reliable, result.Confidence);
    }

    [Fact]
    public void Confidence_Limited_por_geladeira_e_conservador_ReviewData()
    {
        var result = Eval(
            Turnover(stock: 70, fridge: 10, vmv30: 1, band: InventoryCoverageBand.Normal),
            FarLot(70));
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.LocationLimitation, result.PrimaryReason);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
    }

    [Fact]
    public void NoObservableDemand_sem_tese_sem_recomendacao()
    {
        var result = Eval(
            Turnover(stock: 20, vmv30: 0, vmv7: 0, vmv90: 0, idle: false),
            FarLot(20));
        Assert.Equal(InventoryCommercialEligibilityKind.NoCommercialRecommendation, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.NoObservableDemand, result.PrimaryReason);
    }

    [Fact]
    public void Produto_normal_sem_recomendacao()
    {
        var result = Eval(
            Turnover(stock: 10, vmv30: 1, band: InventoryCoverageBand.Attention),
            FarLot(10));
        Assert.Equal(InventoryCommercialEligibilityKind.NoCommercialRecommendation, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.None, result.PrimaryReason);
    }

    [Fact]
    public void ProjectedExcess_ate_epsilon_nao_candidato_por_excesso()
    {
        var row = Turnover(stock: 10, vmv30: 1, band: InventoryCoverageBand.Attention);
        var pipe = Pipeline(row, FarLot(10));
        var attention = new InventoryAttentionResult
        {
            ProductId = row.ProductId,
            Family = InventoryAttentionFamily.Normal,
            PrimaryReason = InventoryAttentionReason.None,
            Action = InventoryOperatorAction.None,
            Confidence = InventoryAttentionConfidence.Reliable,
            ProjectedExcessQuantity = InventoryCommercialEligibilityEngine.Epsilon,
            ProjectedExpirySurplusQuantity = null,
        };
        var result = InventoryCommercialEligibilityEngine.Evaluate(row, pipe.Projected, attention);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.NotEqual(InventoryCommercialEligibilityReason.ProjectedExcess, result.PrimaryReason);
    }

    [Fact]
    public void Surplus_expiry_ate_epsilon_nao_candidato_por_validade()
    {
        var row = Turnover(stock: 10, vmv30: 1, band: InventoryCoverageBand.Attention);
        var pipe = Pipeline(row, FarLot(10));
        var attention = new InventoryAttentionResult
        {
            ProductId = row.ProductId,
            Family = InventoryAttentionFamily.Normal,
            PrimaryReason = InventoryAttentionReason.None,
            Action = InventoryOperatorAction.None,
            Confidence = InventoryAttentionConfidence.Reliable,
            ProjectedExcessQuantity = 0,
            ProjectedExpirySurplusQuantity = InventoryCommercialEligibilityEngine.Epsilon,
        };
        var result = InventoryCommercialEligibilityEngine.Evaluate(row, pipe.Projected, attention);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.NotEqual(InventoryCommercialEligibilityReason.ProjectedExpirySurplus, result.PrimaryReason);
    }

    [Fact]
    public void Multiplas_razoes_respeitam_precedencia()
    {
        var result = Eval(
            Turnover(stock: 40, vmv30: 1, idle: true, history: 120, band: InventoryCoverageBand.Low),
            Lot(1, 40, 5));
        Assert.Equal(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.ProjectedExpirySurplus, result.PrimaryReason);
        Assert.Contains(InventoryCommercialEligibilityReason.Idle, result.SecondaryReasons);
        var surplusIdx = Array.IndexOf(
            InventoryCommercialEligibilityEngine.ReasonPrecedence,
            InventoryCommercialEligibilityReason.ProjectedExpirySurplus);
        var idleIdx = Array.IndexOf(
            InventoryCommercialEligibilityEngine.ReasonPrecedence,
            InventoryCommercialEligibilityReason.Idle);
        Assert.True(surplusIdx < idleIdx);
        foreach (var reason in result.SecondaryReasons)
        {
            Assert.True(
                Array.IndexOf(InventoryCommercialEligibilityEngine.ReasonPrecedence, result.PrimaryReason)
                < Array.IndexOf(InventoryCommercialEligibilityEngine.ReasonPrecedence, reason));
        }
    }

    [Fact]
    public void Input_repetido_resultado_identico()
    {
        var row = Turnover(stock: 80, vmv30: 1, band: InventoryCoverageBand.Normal);
        var pipe = Pipeline(row, FarLot(80));
        var a = InventoryCommercialEligibilityEngine.Evaluate(pipe.Row, pipe.Projected, pipe.Attention);
        var b = InventoryCommercialEligibilityEngine.Evaluate(pipe.Row, pipe.Projected, pipe.Attention);
        Assert.Equal(a.Kind, b.Kind);
        Assert.Equal(a.PrimaryReason, b.PrimaryReason);
        Assert.Equal(a.Confidence, b.Confidence);
        Assert.Equal(a.SecondaryReasons, b.SecondaryReasons);
    }

    [Fact]
    public void Engine_nao_muta_input()
    {
        var row = Turnover(stock: 80, vmv30: 1, idle: true, history: 120, band: InventoryCoverageBand.Normal);
        var pipe = Pipeline(row, FarLot(80));
        var originalLots = pipe.Projected.Projection.Lots.Count;
        var mutableSecondary = new List<InventoryAttentionReason>(pipe.Attention.SecondaryReasons);
        var attention = new InventoryAttentionResult
        {
            ProductId = pipe.Attention.ProductId,
            Priority = pipe.Attention.Priority,
            Family = pipe.Attention.Family,
            PrimaryReason = pipe.Attention.PrimaryReason,
            SecondaryReasons = mutableSecondary,
            Action = pipe.Attention.Action,
            Confidence = pipe.Attention.Confidence,
            ProjectedExcessQuantity = pipe.Attention.ProjectedExcessQuantity,
            ProjectedExpirySurplusQuantity = pipe.Attention.ProjectedExpirySurplusQuantity,
            NearestDatedDaysUntilExpiry = pipe.Attention.NearestDatedDaysUntilExpiry,
            SurplusValueQuality = pipe.Attention.SurplusValueQuality,
        };
        var beforeCount = mutableSecondary.Count;
        _ = InventoryCommercialEligibilityEngine.Evaluate(row, pipe.Projected, attention);
        Assert.Equal(originalLots, pipe.Projected.Projection.Lots.Count);
        Assert.Equal(beforeCount, mutableSecondary.Count);
        Assert.Equal(pipe.Attention.Action, attention.Action);
        Assert.Equal(InventoryOperatorAction.EvaluateExcess, attention.Action);
    }

    [Fact]
    public void EvaluateExcess_nao_significa_promocao()
    {
        var pipe = Pipeline(
            Turnover(stock: 80, vmv30: 1, band: InventoryCoverageBand.Normal),
            FarLot(80));
        var result = InventoryCommercialEligibilityEngine.Evaluate(
            pipe.Row, pipe.Projected, pipe.Attention);
        Assert.Equal(InventoryOperatorAction.EvaluateExcess, pipe.Attention.Action);
        Assert.Equal(InventoryCommercialEligibilityKind.CommercialCandidate, result.Kind);
        Assert.DoesNotContain("Promot", pipe.Attention.Action.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Promot", result.Kind.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Promot", result.PrimaryReason.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsiderPromotion_continua_nao_emitido_e_irrelevante_no_B1()
    {
        Assert.DoesNotContain(
            "ConsiderPromotion",
            Enum.GetNames<InventoryCommercialEligibilityKind>());
        Assert.DoesNotContain(
            "ConsiderPromotion",
            Enum.GetNames<InventoryCommercialEligibilityReason>());
        Assert.Contains("ConsiderPromotion", Enum.GetNames<ValiditySuggestedAction>());
        Assert.NotEqual(
            ValiditySuggestedAction.ConsiderPromotion,
            ValidityControlEngine.ResolveSuggestedAction(
                ValidityControlRowKind.Lot, ProductExpiryStatusKind.Expired, 1));
        Assert.NotEqual(
            ValiditySuggestedAction.ConsiderPromotion,
            ValidityControlEngine.ResolveSuggestedAction(
                ValidityControlRowKind.Lot, ProductExpiryStatusKind.Today, 1));
        Assert.NotEqual(
            ValiditySuggestedAction.ConsiderPromotion,
            ValidityControlEngine.ResolveSuggestedAction(
                ValidityControlRowKind.Lot, ProductExpiryStatusKind.Ok, 1));
    }

    [Fact]
    public void Enums_nao_tem_semantica_de_execucao_automatica()
    {
        var names = Enum.GetNames<InventoryCommercialEligibilityKind>()
            .Concat(Enum.GetNames<InventoryCommercialEligibilityReason>())
            .ToArray();
        foreach (var name in names)
        {
            Assert.DoesNotContain("Execute", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Apply", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Discount", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Promot", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Combo", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Buy", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Purchase", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Replenish", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NearExpiry_sem_sobra_e_MonitorOnly()
    {
        var result = Eval(Turnover(stock: 4, vmv30: 1, band: InventoryCoverageBand.Low), Lot(1, 4, 4));
        Assert.Equal(InventoryCommercialEligibilityKind.MonitorOnly, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.NearExpiryWithoutSurplus, result.PrimaryReason);
    }

    [Fact]
    public void Dated_sem_sobra_na_janela_e_MonitorOnly()
    {
        var result = Eval(Turnover(stock: 8, vmv30: 1, band: InventoryCoverageBand.Attention), Lot(1, 8, 20));
        Assert.Equal(InventoryCommercialEligibilityKind.MonitorOnly, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.DatedWithoutSurplusInWindow, result.PrimaryReason);
    }

    [Fact]
    public void Input_nulo_e_ReviewData()
    {
        var result = InventoryCommercialEligibilityEngine.Evaluate(null, null, null);
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.InvalidInput, result.PrimaryReason);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
    }

    [Fact]
    public void Anomalia_de_localizacao_e_ReviewData()
    {
        var result = Eval(
            Turnover(stock: 80, vmv30: 1, anomaly: true, band: InventoryCoverageBand.Normal),
            FarLot(80));
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, result.Kind);
        Assert.Equal(InventoryCommercialEligibilityReason.NegativeLocationStock, result.PrimaryReason);
    }

    [Fact]
    public void Engine_e_modelo_nao_leem_preco_nem_fazem_io()
    {
        var engine = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "InventoryCommercialEligibilityEngine.cs"));
        var model = File.ReadAllText(FindSource("src", "SGDB.App", "Models", "InventoryCommercialEligibility.cs"));
        foreach (var source in new[] { engine, model })
        {
            Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Microsoft.Data", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
            Assert.DoesNotContain("sale_price", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cost_price", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_promocional", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_inicio", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_fim", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("desconto_percent", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Today", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ConsiderPromotion", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DatabaseService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("StoreNetwork", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("InventoryProjectionService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryIntelligenceService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionEngine.Evaluate", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("combo", engine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EvaluateExcess não é promoção", engine, StringComparison.Ordinal);
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

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
