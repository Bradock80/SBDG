using System.IO;
using System.Reflection;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70G-B1 — motor puro de orientação qualitativa de reposição.
/// Sem SQL, UI, quantidade, fornecedor, B5, PurchaseService ou min_stock.
/// </summary>
public class InventoryPurchaseGuidanceEngineTests
{
    [Fact]
    public void QueryCount_e_zero() =>
        Assert.Equal(0, InventoryPurchaseGuidanceEngine.ExpectedQueryCount);

    [Fact]
    public void Epsilon_reusa_70C() =>
        Assert.Equal(InventoryIntelligenceEngine.Epsilon, InventoryPurchaseGuidanceEngine.Epsilon);

    [Fact]
    public void Zero_com_giro_considera_reposicao_limited()
    {
        var result = Eval(In(
            stock: 0,
            stockFridge: 0,
            vmv30: 2,
            coverageBand: InventoryCoverageBand.Zero,
            coverageDays: null,
            isZeroStockWithTurnover: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand);
    }

    [Fact]
    public void Zero_sem_giro_monitora_demanda_nao_observavel()
    {
        var result = Eval(In(
            stock: 0,
            stockFridge: 0,
            vmv30: 0,
            coverageBand: InventoryCoverageBand.Zero,
            coverageDays: null));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.Monitor,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.NoObservableDemand);
    }

    [Fact]
    public void Critical_considera_reposicao_limited()
    {
        var result = Eval(In(
            stock: 2,
            vmv30: 1,
            coverageBand: InventoryCoverageBand.Critical,
            coverageDays: 2));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.CriticalCoverage);
    }

    [Fact]
    public void Low_considera_reposicao_limited()
    {
        var result = Eval(In(
            stock: 5,
            vmv30: 1,
            coverageBand: InventoryCoverageBand.Low,
            coverageDays: 5));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.LowCoverage);
    }

    [Fact]
    public void Attention_isola_monitor_limited_sem_reason_artificial()
    {
        var result = Eval(In(
            stock: 10,
            vmv30: 1,
            coverageBand: InventoryCoverageBand.Attention,
            coverageDays: 10));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.Monitor,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.None);
        Assert.Empty(result.SecondaryReasons);
    }

    [Fact]
    public void Normal_isola_monitor_reliable_sem_excesso()
    {
        var result = Eval(In());
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.Monitor,
            InventoryAttentionConfidence.Reliable,
            InventoryPurchaseGuidanceReason.None);
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
        Assert.NotEqual(InventoryPurchaseGuidanceAction.ConsiderReplenishment, result.Action);
        Assert.NotEqual(InventoryPurchaseGuidanceAction.DoNotReplenishNow, result.Action);
    }

    [Fact]
    public void Excess30_valido_nao_repor_reliable()
    {
        var result = Eval(In(
            stock: 80,
            vmv30: 1,
            coverageBand: InventoryCoverageBand.Normal,
            coverageDays: 80,
            projectedExcessQuantity: 50));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryAttentionConfidence.Reliable,
            InventoryPurchaseGuidanceReason.ProjectedExcess30);
    }

    [Fact]
    public void Excess30_zero_nao_dispara()
    {
        var result = Eval(In(projectedExcessQuantity: 0));
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, result.Action);
    }

    [Fact]
    public void Excess30_igual_ao_epsilon_nao_dispara()
    {
        var result = Eval(In(projectedExcessQuantity: InventoryPurchaseGuidanceEngine.Epsilon));
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, result.Action);
    }

    [Fact]
    public void CanProjectSku_false_ignora_excesso()
    {
        var result = Eval(In(
            canProjectSku: false,
            projectedExcessQuantity: 40,
            coverageBand: InventoryCoverageBand.Normal,
            coverageDays: 20));
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, result.Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.None, result.PrimaryReason);
    }

    [Fact]
    public void ExpirySurplus_valido_nao_repor_reliable()
    {
        var result = Eval(In(projectedExpirySurplus: 4));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryAttentionConfidence.Reliable,
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus);
    }

    [Fact]
    public void ExpirySurplus_zero_nao_dispara()
    {
        var result = Eval(In(projectedExpirySurplus: 0));
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus, result.PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceReason.None, result.PrimaryReason);
    }

    [Fact]
    public void ExpirySurplus_igual_ao_epsilon_nao_dispara()
    {
        var result = Eval(In(projectedExpirySurplus: InventoryPurchaseGuidanceEngine.Epsilon));
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus, result.PrimaryReason);
    }

    [Fact]
    public void ExpirySurplus_com_limitacao_de_local_monitora()
    {
        var result = Eval(In(
            stock: 8,
            stockFridge: 2,
            projectedExpirySurplus: 5,
            hasLotLocationLimitation: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.Monitor,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.LocationLimitation);
        Assert.NotEqual(InventoryPurchaseGuidanceAction.DoNotReplenishNow, result.Action);
        Assert.DoesNotContain(
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus, result.SecondaryReasons);
    }

    [Fact]
    public void Excess30_com_limitacao_de_local_nao_bloqueia()
    {
        var result = Eval(In(
            stock: 70,
            stockFridge: 10,
            coverageDays: 80,
            projectedExcessQuantity: 40,
            hasLotLocationLimitation: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryAttentionConfidence.Reliable,
            InventoryPurchaseGuidanceReason.ProjectedExcess30);
        Assert.Equal(
            [InventoryPurchaseGuidanceReason.LocationLimitation],
            result.SecondaryReasons);
    }

    [Fact]
    public void Idle_nao_repor_reliable()
    {
        var result = Eval(In(isIdle: true, vmv30: 0, coverageDays: null));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryAttentionConfidence.Reliable,
            InventoryPurchaseGuidanceReason.IdleStock);
    }

    [Fact]
    public void Vmv0_nao_idle_monitora_demanda_nao_observavel()
    {
        var result = Eval(In(
            historyDays: 45,
            vmv30: 0,
            coverageBand: InventoryCoverageBand.NotCalculable,
            coverageDays: null));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.Monitor,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.NoObservableDemand);
        Assert.NotEqual(InventoryPurchaseGuidanceReason.IdleStock, result.PrimaryReason);
    }

    [Fact]
    public void Expired_nao_repor_reliable()
    {
        var result = Eval(In(hasExpiredLot: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryAttentionConfidence.Reliable,
            InventoryPurchaseGuidanceReason.Expired);
    }

    [Fact]
    public void ExpiresToday_nao_repor_reliable()
    {
        var result = Eval(In(hasExpiresTodayLot: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryAttentionConfidence.Reliable,
            InventoryPurchaseGuidanceReason.ExpiresToday);
    }

    [Fact]
    public void InsufficientHistory_isolado_monitora()
    {
        var result = Eval(In(
            historyDays: 12,
            isHistoryInsufficient30: true,
            coverageBand: InventoryCoverageBand.Normal,
            coverageDays: 20));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.Monitor,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.InsufficientHistory);
    }

    [Fact]
    public void InsufficientHistory_com_Low_permanece_considerar()
    {
        var result = Eval(In(
            stock: 5,
            coverageBand: InventoryCoverageBand.Low,
            coverageDays: 5,
            historyDays: 10,
            isHistoryInsufficient30: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.LowCoverage);
        Assert.Equal(
            [InventoryPurchaseGuidanceReason.InsufficientHistory],
            result.SecondaryReasons);
    }

    [Fact]
    public void InsufficientHistory_com_Critical_permanece_considerar()
    {
        var result = Eval(In(
            stock: 2,
            coverageBand: InventoryCoverageBand.Critical,
            coverageDays: 2,
            historyDays: 8,
            isHistoryInsufficient30: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryAttentionConfidence.Limited,
            InventoryPurchaseGuidanceReason.CriticalCoverage);
        Assert.Equal(
            [InventoryPurchaseGuidanceReason.InsufficientHistory],
            result.SecondaryReasons);
    }

    [Fact]
    public void Sem_evidencia_fisica_revisa_dados()
    {
        var result = Eval(In(hasPhysicalAvailabilityEvidence: false));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.ReviewData,
            InventoryPurchaseGuidanceAction.ReviewData,
            InventoryAttentionConfidence.Unavailable,
            InventoryPurchaseGuidanceReason.NoPhysicalEvidence);
        Assert.NotEqual(InventoryPurchaseGuidanceAction.ConsiderReplenishment, result.Action);
    }

    [Fact]
    public void Sem_evidencia_e_estoque_zero_nao_considera_reposicao()
    {
        var result = Eval(In(
            stock: 0,
            stockFridge: 0,
            vmv30: 3,
            coverageBand: InventoryCoverageBand.Zero,
            coverageDays: null,
            isZeroStockWithTurnover: true,
            hasPhysicalAvailabilityEvidence: false));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.ReviewData,
            InventoryPurchaseGuidanceAction.ReviewData,
            InventoryAttentionConfidence.Unavailable,
            InventoryPurchaseGuidanceReason.NoPhysicalEvidence);
    }

    [Fact]
    public void Estoque_negativo_e_estrutural()
    {
        var result = Eval(In(
            stock: -2,
            coverageBand: InventoryCoverageBand.Negative,
            coverageDays: null,
            skuBlockedReason: InventorySkuProjectionBlockedReason.NegativeStock));
        AssertStructural(result);
    }

    [Fact]
    public void Local_negativo_e_estrutural()
    {
        var result = Eval(In(
            stock: 12,
            stockFridge: -2,
            hasLocationStockAnomaly: true,
            skuBlockedReason: InventorySkuProjectionBlockedReason.NegativeLocationStock));
        AssertStructural(result);
    }

    [Fact]
    public void Totais_inconsistentes_sao_estruturais()
    {
        var result = Eval(In(
            stock: 10,
            stockFridge: 5,
            totalStock: 20,
            skuBlockedReason: InventorySkuProjectionBlockedReason.InconsistentStockTotals));
        AssertStructural(result);
    }

    [Fact]
    public void Tracked_excede_deposito_e_estrutural()
    {
        var result = Eval(In(
            hasTrackedQuantityExceedsWarehouse: true,
            expiryBlockedReason: InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse));
        AssertStructural(result);
    }

    [Fact]
    public void Quantidade_de_lote_invalida_e_estrutural()
    {
        var result = Eval(In(
            hasInvalidLotQuantity: true,
            expiryBlockedReason: InventoryExpiryProjectionBlockedReason.InvalidLotQuantity));
        AssertStructural(result);
    }

    [Fact]
    public void Lote_duplicado_e_estrutural()
    {
        var result = Eval(In(
            hasDuplicateLot: true,
            expiryBlockedReason: InventoryExpiryProjectionBlockedReason.DuplicateLotId));
        AssertStructural(result);
    }

    [Fact]
    public void Validade_invalida_e_estrutural()
    {
        var result = Eval(In(
            hasInvalidExpiry: true,
            expiryBlockedReason: InventoryExpiryProjectionBlockedReason.InvalidExpiryDate));
        AssertStructural(result);
    }

    [Fact]
    public void Input_invalido_e_estrutural()
    {
        var result = Eval(In(
            isInvalidInput: true,
            skuBlockedReason: InventorySkuProjectionBlockedReason.InvalidInput));
        AssertStructural(result);
    }

    [Fact]
    public void Composicao_nao_aplicavel()
    {
        var result = Eval(In(isCompositionProduct: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.NotApplicable,
            InventoryPurchaseGuidanceAction.None,
            InventoryAttentionConfidence.Unavailable,
            InventoryPurchaseGuidanceReason.CompositionProduct);
        Assert.Empty(result.SecondaryReasons);
    }

    [Fact]
    public void Composicao_domina_excesso()
    {
        var result = Eval(In(
            isCompositionProduct: true,
            projectedExcessQuantity: 30));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.NotApplicable,
            InventoryPurchaseGuidanceAction.None,
            InventoryAttentionConfidence.Unavailable,
            InventoryPurchaseGuidanceReason.CompositionProduct);
    }

    [Fact]
    public void Estrutural_domina_excesso()
    {
        var result = Eval(In(
            stock: -1,
            coverageBand: InventoryCoverageBand.Negative,
            coverageDays: null,
            projectedExcessQuantity: 12,
            skuBlockedReason: InventorySkuProjectionBlockedReason.NegativeStock));
        AssertStructural(result);
        Assert.DoesNotContain(
            InventoryPurchaseGuidanceReason.ProjectedExcess30, result.SecondaryReasons);
    }

    [Fact]
    public void Conflito_impossivel_Excess_mais_Low_revisa_dados()
    {
        var result = Eval(In(
            stock: 5,
            coverageBand: InventoryCoverageBand.Low,
            coverageDays: 5,
            projectedExcessQuantity: 20));
        AssertStructural(result);
    }

    [Fact]
    public void Conflito_impossivel_Excess_mais_Critical_revisa_dados()
    {
        var result = Eval(In(
            stock: 2,
            coverageBand: InventoryCoverageBand.Critical,
            coverageDays: 2,
            projectedExcessQuantity: 15));
        AssertStructural(result);
    }

    [Fact]
    public void Conflito_impossivel_Idle_mais_Low_revisa_dados()
    {
        var result = Eval(In(
            isIdle: true,
            coverageBand: InventoryCoverageBand.Low,
            coverageDays: 6,
            vmv30: 1));
        AssertStructural(result);
    }

    [Fact]
    public void Conflito_impossivel_Idle_mais_Critical_revisa_dados()
    {
        var result = Eval(In(
            isIdle: true,
            coverageBand: InventoryCoverageBand.Critical,
            coverageDays: 2,
            vmv30: 1));
        AssertStructural(result);
    }

    [Fact]
    public void Expired_domina_Low_sem_secondary_de_cobertura()
    {
        var result = Eval(In(
            stock: 5,
            coverageBand: InventoryCoverageBand.Low,
            coverageDays: 5,
            hasExpiredLot: true));
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryAttentionConfidence.Reliable,
            InventoryPurchaseGuidanceReason.Expired);
        Assert.DoesNotContain(InventoryPurchaseGuidanceReason.LowCoverage, result.SecondaryReasons);
    }

    [Fact]
    public void ExpiresToday_domina_Low_sem_secondary_de_cobertura()
    {
        var result = Eval(In(
            stock: 5,
            coverageBand: InventoryCoverageBand.Low,
            coverageDays: 5,
            hasExpiresTodayLot: true));
        Assert.Equal(InventoryPurchaseGuidanceReason.ExpiresToday, result.PrimaryReason);
        Assert.DoesNotContain(InventoryPurchaseGuidanceReason.LowCoverage, result.SecondaryReasons);
    }

    [Fact]
    public void Excess_com_ExpirySurplus_confiavel_coloca_surplus_em_secondary()
    {
        var result = Eval(In(
            stock: 80,
            coverageDays: 80,
            projectedExcessQuantity: 40,
            projectedExpirySurplus: 6));
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Equal(
            [InventoryPurchaseGuidanceReason.ProjectedExpirySurplus],
            result.SecondaryReasons);
        Assert.DoesNotContain(result.PrimaryReason, result.SecondaryReasons);
    }

    [Fact]
    public void Primary_e_deterministico_pela_precedencia()
    {
        Assert.Equal(
            InventoryPurchaseGuidanceReason.CompositionProduct,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[0]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.StructuralDataIssue,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[1]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.NoPhysicalEvidence,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[2]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.Expired,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[3]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.ExpiresToday,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[4]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.ProjectedExcess30,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[5]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[6]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.IdleStock,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[7]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[8]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.CriticalCoverage,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[9]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.LowCoverage,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[10]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.NoObservableDemand,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[11]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.InsufficientHistory,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[12]);
        Assert.Equal(
            InventoryPurchaseGuidanceReason.LocationLimitation,
            InventoryPurchaseGuidanceEngine.ReasonPrecedence[13]);

        var result = Eval(In(hasExpiredLot: true, hasExpiresTodayLot: true, isIdle: true));
        Assert.Equal(InventoryPurchaseGuidanceReason.Expired, result.PrimaryReason);
    }

    [Fact]
    public void Secondary_tem_ordem_estavel()
    {
        var result = Eval(In(
            stock: 70,
            stockFridge: 10,
            coverageDays: 80,
            projectedExcessQuantity: 30,
            hasLotLocationLimitation: true,
            historyDays: 10,
            isHistoryInsufficient30: true));
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Equal(
            new[]
            {
                InventoryPurchaseGuidanceReason.InsufficientHistory,
                InventoryPurchaseGuidanceReason.LocationLimitation,
            },
            result.SecondaryReasons);
        var again = Eval(In(
            stock: 70,
            stockFridge: 10,
            coverageDays: 80,
            projectedExcessQuantity: 30,
            hasLotLocationLimitation: true,
            historyDays: 10,
            isHistoryInsufficient30: true));
        Assert.Equal(result.SecondaryReasons, again.SecondaryReasons);
    }

    [Fact]
    public void Primary_nunca_duplica_em_secondary()
    {
        var samples = new[]
        {
            In(isZeroStockWithTurnover: true, stock: 0, stockFridge: 0, coverageBand: InventoryCoverageBand.Zero, coverageDays: null, vmv30: 2, historyDays: 10, isHistoryInsufficient30: true),
            In(projectedExcessQuantity: 20, projectedExpirySurplus: 3),
            In(hasExpiredLot: true, coverageBand: InventoryCoverageBand.Low, coverageDays: 5, stock: 5),
        };
        foreach (var input in samples)
        {
            var result = Eval(input);
            Assert.DoesNotContain(result.PrimaryReason, result.SecondaryReasons);
        }
    }

    [Fact]
    public void ConsiderReplenishment_sempre_Limited()
    {
        foreach (var input in new[]
        {
            In(stock: 0, stockFridge: 0, vmv30: 2, coverageBand: InventoryCoverageBand.Zero, coverageDays: null, isZeroStockWithTurnover: true),
            In(stock: 2, coverageBand: InventoryCoverageBand.Critical, coverageDays: 2),
            In(stock: 5, coverageBand: InventoryCoverageBand.Low, coverageDays: 5),
        })
        {
            var result = Eval(input);
            Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, result.Action);
            Assert.Equal(InventoryAttentionConfidence.Limited, result.Confidence);
        }
    }

    [Fact]
    public void Excess_confianca_Reliable()
    {
        var result = Eval(In(projectedExcessQuantity: 9));
        Assert.Equal(InventoryAttentionConfidence.Reliable, result.Confidence);
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
    }

    [Fact]
    public void Idle_confianca_Reliable()
    {
        var result = Eval(In(isIdle: true, vmv30: 0, coverageDays: null));
        Assert.Equal(InventoryAttentionConfidence.Reliable, result.Confidence);
    }

    [Fact]
    public void ExpirySurplus_confianca_Reliable()
    {
        var result = Eval(In(projectedExpirySurplus: 2.5));
        Assert.Equal(InventoryAttentionConfidence.Reliable, result.Confidence);
    }

    [Fact]
    public void ReviewData_confianca_Unavailable()
    {
        var result = Eval(In(hasPhysicalAvailabilityEvidence: false));
        Assert.Equal(InventoryPurchaseGuidanceAction.ReviewData, result.Action);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
    }

    [Fact]
    public void NotApplicable_confianca_Unavailable()
    {
        var result = Eval(In(isCompositionProduct: true));
        Assert.Equal(InventoryPurchaseGuidanceStatus.NotApplicable, result.Status);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
    }

    [Fact]
    public void Resultado_nao_tem_propriedade_de_quantidade()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidanceResult),
            "SuggestedQuantity", "TargetQuantity", "OrderQuantity", "BoxQuantity", "UnitsToBuy",
            "AttentionQuantity", "Quantity");
        AssertNoMember(typeof(InventoryPurchaseGuidanceInput),
            "SuggestedQuantity", "TargetQuantity", "OrderQuantity", "BoxQuantity", "UnitsToBuy");
    }

    [Fact]
    public void Resultado_nao_tem_propriedade_de_fornecedor()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidanceResult),
            "SupplierId", "SupplierName", "PreferredSupplier", "RecommendedSupplier", "Supplier");
        AssertNoMember(typeof(InventoryPurchaseGuidanceInput),
            "SupplierId", "SupplierName", "PreferredSupplier", "RecommendedSupplier", "Supplier");
    }

    [Fact]
    public void Resultado_nao_tem_propriedade_de_score()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidanceResult),
            "PurchaseScore", "BuyScore", "ReorderScore", "PriorityScore", "Score");
        AssertNoMember(typeof(InventoryPurchaseGuidanceEngine),
            "PurchaseScore", "BuyScore", "ReorderScore", "PriorityScore");
    }

    [Fact]
    public void Engine_nao_depende_de_tipos_de_banco()
    {
        AssertNoTypeReference(typeof(InventoryPurchaseGuidanceEngine), "Sqlite", "SQLite", "DatabaseService");
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceEngine.cs");
        Assert.DoesNotContain("Sqlite", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLiteConnection", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseService", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void Engine_nao_depende_de_WPF()
    {
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceEngine.cs");
        var model = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidance.cs");
        foreach (var text in new[] { engine, model })
        {
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MessageBox", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Engine_nao_depende_de_PurchaseService()
    {
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceEngine.cs");
        Assert.DoesNotContain("PurchaseService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("purchases.status", engine, StringComparison.Ordinal);
        AssertNoMember(typeof(InventoryPurchaseGuidanceInput), "PurchaseStatus", "DraftPurchase", "OpenPurchase");
    }

    [Fact]
    public void Engine_nao_depende_de_B5()
    {
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceEngine.cs");
        var model = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidance.cs");
        foreach (var text in new[] { engine, model })
        {
            Assert.DoesNotContain("InventoryPromotionSuggestion", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ConsiderPromotion", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PromotionSuggestion", text, StringComparison.Ordinal);
        }
        AssertNoMember(typeof(InventoryPurchaseGuidanceInput), "PromotionSuggestion", "B5Status");
    }

    [Fact]
    public void Engine_nao_depende_de_min_stock()
    {
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceEngine.cs");
        var model = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidance.cs");
        foreach (var text in new[] { engine, model })
        {
            Assert.DoesNotContain("min_stock", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MinStock", text, StringComparison.Ordinal);
        }
        AssertNoMember(typeof(InventoryPurchaseGuidanceInput), "MinStock", "MinimumStock");
    }

    [Fact]
    public void Engine_nao_depende_de_margem()
    {
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceEngine.cs");
        var model = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidance.cs");
        foreach (var text in new[] { engine, model })
        {
            Assert.DoesNotContain("Margin", text, StringComparison.Ordinal);
            Assert.DoesNotContain("GrossMargin", text, StringComparison.Ordinal);
            Assert.DoesNotContain("sale_price", text, StringComparison.OrdinalIgnoreCase);
        }
        AssertNoMember(typeof(InventoryPurchaseGuidanceInput), "Margin", "GrossMarginPercent", "Price", "Cost");
    }

    [Fact]
    public void Mesmo_input_mesmo_output()
    {
        var input = In(
            stock: 70,
            stockFridge: 10,
            projectedExcessQuantity: 25,
            projectedExpirySurplus: 4,
            hasLotLocationLimitation: true);
        var a = Eval(input);
        var b = Eval(input);
        Assert.Equal(a.ProductId, b.ProductId);
        Assert.Equal(a.Status, b.Status);
        Assert.Equal(a.Action, b.Action);
        Assert.Equal(a.Confidence, b.Confidence);
        Assert.Equal(a.PrimaryReason, b.PrimaryReason);
        Assert.Equal(a.SecondaryReasons, b.SecondaryReasons);
    }

    [Fact]
    public void Semantica_zero_versus_epsilon()
    {
        var excessAtEpsilon = Eval(In(projectedExcessQuantity: InventoryPurchaseGuidanceEngine.Epsilon));
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExcess30, excessAtEpsilon.PrimaryReason);

        var excessAbove = Eval(In(projectedExcessQuantity: InventoryPurchaseGuidanceEngine.Epsilon + 0.00001));
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExcess30, excessAbove.PrimaryReason);

        var idleAtEpsilon = Eval(In(
            stock: InventoryPurchaseGuidanceEngine.Epsilon,
            stockFridge: 0,
            isIdle: true,
            vmv30: 0,
            coverageDays: null));
        Assert.NotEqual(InventoryPurchaseGuidanceReason.IdleStock, idleAtEpsilon.PrimaryReason);

        var idleAbove = Eval(In(
            stock: InventoryPurchaseGuidanceEngine.Epsilon + 0.00001,
            isIdle: true,
            vmv30: 0,
            coverageDays: null));
        Assert.Equal(InventoryPurchaseGuidanceReason.IdleStock, idleAbove.PrimaryReason);
    }

    [Fact]
    public void Normal_nao_e_excesso()
    {
        var result = Eval(In(coverageBand: InventoryCoverageBand.Normal, coverageDays: 40, projectedExcessQuantity: 0));
        Assert.Equal(InventoryPurchaseGuidanceReason.None, result.PrimaryReason);
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, result.Action);
    }

    [Fact]
    public void Attention_nao_e_ConsiderReplenishment()
    {
        var result = Eval(In(coverageBand: InventoryCoverageBand.Attention, coverageDays: 12));
        Assert.NotEqual(InventoryPurchaseGuidanceAction.ConsiderReplenishment, result.Action);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, result.Action);
    }

    [Fact]
    public void Tendencia_VMV_ausente_no_contrato()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidanceInput), "Vmv7", "Vmv90", "VmvTrend");
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceEngine.cs");
        Assert.DoesNotContain("Vmv7", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("Vmv90", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void Cigarro_nao_tem_bloqueio_especial()
    {
        Assert.Null(typeof(InventoryPurchaseGuidanceReason).GetField("AmbiguousPurchaseUnit"));
        var result = Eval(In(
            productId: 99,
            stock: 2,
            coverageBand: InventoryCoverageBand.Critical,
            coverageDays: 2));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, result.Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.CriticalCoverage, result.PrimaryReason);
    }

    [Fact]
    public void Draft_purchase_ausente_do_contrato()
    {
        Assert.Null(typeof(InventoryPurchaseGuidanceReason).GetField("DraftPurchaseExists"));
        AssertNoMember(typeof(InventoryPurchaseGuidanceInput), "HasDraftPurchase", "HasOpenPurchase", "IncomingQuantity");
        var result = Eval(In(projectedExcessQuantity: 18));
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
    }

    [Fact]
    public void Acoes_proibidas_nao_existem()
    {
        var names = Enum.GetNames<InventoryPurchaseGuidanceAction>();
        Assert.DoesNotContain("Replenish", names);
        Assert.DoesNotContain("Buy", names);
        Assert.DoesNotContain("BuyNow", names);
        Assert.DoesNotContain("OrderNow", names);
    }

    [Fact]
    public void Reasons_proibidas_nao_existem()
    {
        var names = Enum.GetNames<InventoryPurchaseGuidanceReason>();
        foreach (var forbidden in new[]
        {
            "LeadTimeUnknown",
            "PurchaseHistoryUnavailable",
            "DraftPurchaseExists",
            "AmbiguousPurchaseUnit",
            "NewProduct",
            "HighMargin",
            "LowPrice",
            "PromotionSuggested",
            "AttentionCoverage",
        })
            Assert.DoesNotContain(forbidden, names);
    }

    [Fact]
    public void Confianca_reusa_enum_70E()
    {
        var result = Eval(In());
        Assert.IsType<InventoryAttentionConfidence>(result.Confidence);
        Assert.Null(typeof(InventoryPurchaseGuidanceResult).Assembly
            .GetType("SGDB.Models.InventoryPurchaseGuidanceConfidence"));
    }

    [Fact]
    public void Input_nulo_nao_lanca_e_revisa_dados()
    {
        var result = InventoryPurchaseGuidanceEngine.Evaluate(null);
        Assert.Equal(InventoryPurchaseGuidanceStatus.ReviewData, result.Status);
        Assert.Equal(InventoryPurchaseGuidanceAction.ReviewData, result.Action);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, result.Confidence);
        Assert.Equal(InventoryPurchaseGuidanceReason.NoPhysicalEvidence, result.PrimaryReason);
    }

    [Fact]
    public void Numero_nao_finito_e_estrutural()
    {
        var result = Eval(In(stock: double.NaN, totalStock: double.NaN));
        AssertStructural(result);
    }

    [Fact]
    public void Excess_acima_do_epsilon_dispara()
    {
        var result = Eval(In(projectedExcessQuantity: InventoryPurchaseGuidanceEngine.Epsilon + 0.0001));
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExcess30, result.PrimaryReason);
    }

    [Fact]
    public void ExpirySurplus_acima_do_epsilon_dispara()
    {
        var result = Eval(In(projectedExpirySurplus: InventoryPurchaseGuidanceEngine.Epsilon + 0.0001));
        Assert.Equal(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus, result.PrimaryReason);
    }

    [Fact]
    public void InsufficientHistory_sem_evidencia_nao_domina()
    {
        var result = Eval(In(
            hasPhysicalAvailabilityEvidence: false,
            historyDays: 3,
            isHistoryInsufficient30: true));
        Assert.Equal(InventoryPurchaseGuidanceReason.NoPhysicalEvidence, result.PrimaryReason);
    }

    [Fact]
    public void Expired_vence_ExpiresToday()
    {
        var result = Eval(In(hasExpiredLot: true, hasExpiresTodayLot: true));
        Assert.Equal(InventoryPurchaseGuidanceReason.Expired, result.PrimaryReason);
    }

    [Fact]
    public void Pipeline_de_consulta_nao_cresce()
    {
        Assert.Equal(0, InventoryPurchaseGuidanceEngine.ExpectedQueryCount);
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryPromotionSuggestionEngine.ExpectedQueryCount
            + InventoryPurchaseGuidanceEngine.ExpectedQueryCount);
    }

    [Fact]
    public void Contratos_nao_tem_quantidade_fornecedor_score_sql_wpf()
    {
        var model = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidance.cs");
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPurchaseGuidanceEngine.cs");
        foreach (var text in new[] { model, engine })
        {
            Assert.DoesNotContain("SuggestedQuantity", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SupplierId", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PurchaseScore", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PurchaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SQLiteConnection", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AppSettings", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AppSession", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Today", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Random", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MessageBox", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LeadTime", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SafetyStock", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TargetStock", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Zero_estoque_com_giro_nao_usa_Replenish()
    {
        var result = Eval(In(
            stock: 0,
            stockFridge: 0,
            vmv30: 1.5,
            coverageBand: InventoryCoverageBand.Zero,
            coverageDays: null,
            isZeroStockWithTurnover: true));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, result.Action);
        Assert.NotEqual("Replenish", result.Action.ToString());
    }

    [Fact]
    public void LocationLimitation_isolada_nao_e_DoNotReplenishNow()
    {
        var result = Eval(In(
            stock: 8,
            stockFridge: 2,
            hasLotLocationLimitation: true,
            projectedExpirySurplus: 0));
        Assert.Equal(InventoryPurchaseGuidanceReason.LocationLimitation, result.PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, result.Action);
    }

    [Fact]
    public void Expiry_bloqueada_nao_usa_surplus()
    {
        var result = Eval(In(
            projectedExpirySurplus: 7,
            expiryBlockedReason: InventoryExpiryProjectionBlockedReason.InsufficientHistory));
        Assert.NotEqual(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus, result.PrimaryReason);
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, result.Action);
    }

    static void AssertGuidance(
        InventoryPurchaseGuidanceResult result,
        InventoryPurchaseGuidanceStatus status,
        InventoryPurchaseGuidanceAction action,
        InventoryAttentionConfidence confidence,
        InventoryPurchaseGuidanceReason primary)
    {
        Assert.Equal(status, result.Status);
        Assert.Equal(action, result.Action);
        Assert.Equal(confidence, result.Confidence);
        Assert.Equal(primary, result.PrimaryReason);
        Assert.DoesNotContain(primary, result.SecondaryReasons);
    }

    static void AssertStructural(InventoryPurchaseGuidanceResult result) =>
        AssertGuidance(
            result,
            InventoryPurchaseGuidanceStatus.ReviewData,
            InventoryPurchaseGuidanceAction.ReviewData,
            InventoryAttentionConfidence.Unavailable,
            InventoryPurchaseGuidanceReason.StructuralDataIssue);

    static InventoryPurchaseGuidanceResult Eval(InventoryPurchaseGuidanceInput input) =>
        InventoryPurchaseGuidanceEngine.Evaluate(input);

    static InventoryPurchaseGuidanceInput In(
        int productId = 7,
        double stock = 10,
        double stockFridge = 0,
        double? totalStock = null,
        double vmv30 = 1,
        InventoryCoverageBand coverageBand = InventoryCoverageBand.Normal,
        double? coverageDays = 20,
        bool isIdle = false,
        bool isZeroStockWithTurnover = false,
        bool hasPhysicalAvailabilityEvidence = true,
        int historyDays = 120,
        bool isHistoryInsufficient30 = false,
        bool isCompositionProduct = false,
        bool hasLocationStockAnomaly = false,
        bool canProjectSku = true,
        double? projectedExcessQuantity = 0,
        double? projectedExpirySurplus = 0,
        bool hasLotLocationLimitation = false,
        InventorySkuProjectionBlockedReason skuBlockedReason = InventorySkuProjectionBlockedReason.None,
        InventoryExpiryProjectionBlockedReason expiryBlockedReason = InventoryExpiryProjectionBlockedReason.None,
        bool hasExpiredLot = false,
        bool hasExpiresTodayLot = false,
        bool hasTrackedQuantityExceedsWarehouse = false,
        bool hasInvalidLotQuantity = false,
        bool hasDuplicateLot = false,
        bool hasInvalidExpiry = false,
        bool isInvalidInput = false) =>
        new()
        {
            ProductId = productId,
            Stock = stock,
            StockFridge = stockFridge,
            TotalStock = totalStock ?? stock + stockFridge,
            Vmv30 = vmv30,
            CoverageBand = coverageBand,
            CoverageDays = coverageDays,
            IsIdle = isIdle,
            IsZeroStockWithTurnover = isZeroStockWithTurnover,
            HasPhysicalAvailabilityEvidence = hasPhysicalAvailabilityEvidence,
            HistoryDays = historyDays,
            IsHistoryInsufficient30 = isHistoryInsufficient30,
            IsCompositionProduct = isCompositionProduct,
            HasLocationStockAnomaly = hasLocationStockAnomaly,
            CanProjectSku = canProjectSku,
            ProjectedExcessQuantity = projectedExcessQuantity,
            ProjectedExpirySurplus = projectedExpirySurplus,
            HasLotLocationLimitation = hasLotLocationLimitation,
            SkuBlockedReason = skuBlockedReason,
            ExpiryBlockedReason = expiryBlockedReason,
            HasExpiredLot = hasExpiredLot,
            HasExpiresTodayLot = hasExpiresTodayLot,
            HasTrackedQuantityExceedsWarehouse = hasTrackedQuantityExceedsWarehouse,
            HasInvalidLotQuantity = hasInvalidLotQuantity,
            HasDuplicateLot = hasDuplicateLot,
            HasInvalidExpiry = hasInvalidExpiry,
            IsInvalidInput = isInvalidInput,
        };

    static void AssertNoMember(Type type, params string[] names)
    {
        var members = type
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in names)
            Assert.False(members.Contains(name), $"{type.Name} não deve expor {name}");
    }

    static void AssertNoTypeReference(Type type, params string[] tokens)
    {
        var text = type.ToString() + string.Join(
            " ",
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.ToString() ?? ""));
        foreach (var token in tokens)
            Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
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
}
