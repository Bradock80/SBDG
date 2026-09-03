using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B5B — motor puro de sugestão comercial. Sem SQL, UI, PDV, composer ou write.
/// </summary>
public class InventoryPromotionSuggestionEngineTests
{
    [Fact]
    public void QueryCount_e_zero() =>
        Assert.Equal(0, InventoryPromotionSuggestionEngine.ExpectedQueryCount);

    [Fact]
    public void Excess30_Available_e_Suggested()
    {
        var light = Light();
        var moderate = Moderate();
        var b4 = Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, light, moderate);
        var result = Eval(b4);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ConsiderPromotion, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.ReduceProjectedExcess30, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess, result.PrimaryReason);
        Assert.Equal(2, result.Scenarios.Count);
        Assert.Same(light, result.Scenarios[0]);
        Assert.Same(moderate, result.Scenarios[1]);
        Assert.Equal(8, result.AttentionQuantity);
        Assert.Equal(InventoryCommercialAttentionQuantitySource.ProjectedExcess30, result.AttentionQuantitySource);
    }

    [Fact]
    public void ExpirySurplus_Available_e_Suggested()
    {
        var b4 = Available(
            InventoryCommercialScenarioThesis.ExpirySurplus,
            3.5,
            Light(),
            Moderate());
        var result = Eval(b4);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ConsiderPromotion, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.ReduceProjectedExpirySurplus, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus, result.PrimaryReason);
        Assert.Equal(3.5, result.AttentionQuantity);
        Assert.Equal(InventoryCommercialAttentionQuantitySource.ExpirySurplus, result.AttentionQuantitySource);
    }

    [Fact]
    public void ExpirySurplus_prevalece_quando_B4_ja_escolheu()
    {
        var b4 = Available(
            InventoryCommercialScenarioThesis.ExpirySurplus,
            4,
            Light());
        b4 = WithSecondary(b4, InventoryCommercialScenarioReason.ProjectedExcess30);
        var result = Eval(b4);
        Assert.Equal(InventoryCommercialScenarioThesis.ExpirySurplus, result.Thesis);
        Assert.Equal(InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus, result.PrimaryReason);
        Assert.DoesNotContain(
            InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess, result.SecondaryReasons);
    }

    [Fact]
    public void Available_com_dois_cenarios_preserva_dois_e_ordem()
    {
        var light = Light(9.40);
        var moderate = Moderate(8.80);
        var result = Eval(Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, light, moderate));
        Assert.Equal(2, result.Scenarios.Count);
        Assert.Equal(InventoryCommercialScenarioKind.Light, result.Scenarios[0].Kind);
        Assert.Equal(InventoryCommercialScenarioKind.Moderate, result.Scenarios[1].Kind);
        Assert.Equal(9.40, result.Scenarios[0].SimulatedCatalogPrice);
        Assert.Equal(8.80, result.Scenarios[1].SimulatedCatalogPrice);
        Assert.Equal(36.17, result.Scenarios[0].GrossMarginPercent);
        Assert.Same(light, result.Scenarios[0]);
    }

    [Fact]
    public void Available_com_um_cenario_preserva_um()
    {
        var light = Light();
        var result = Eval(Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, light));
        Assert.Same(light, Assert.Single(result.Scenarios));
    }

    [Fact]
    public void Idle_sozinho_nao_Suggested()
    {
        var result = Eval(Monitor(
            InventoryCommercialScenarioThesis.Idle,
            InventoryCommercialScenarioReason.Idle,
            scenarios: [Light()]));
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.PrioritizeExposure, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.IncreaseCommercialAttention, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.IdleOnly, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Idle_mais_Excess_Available_e_Suggested()
    {
        var b4 = Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate());
        b4 = WithSecondary(b4, InventoryCommercialScenarioReason.Idle);
        var result = Eval(b4);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ConsiderPromotion, result.Action);
        Assert.Equal(InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess, result.PrimaryReason);
        Assert.Contains(InventoryPromotionSuggestionReason.IdleOnly, result.SecondaryReasons);
        Assert.Equal(2, result.Scenarios.Count);
    }

    [Fact]
    public void Idle_mais_Expiry_Available_e_Suggested()
    {
        var b4 = Available(
            InventoryCommercialScenarioThesis.ExpirySurplus,
            2,
            Light());
        b4 = WithSecondary(b4, InventoryCommercialScenarioReason.Idle);
        var result = Eval(b4);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus, result.PrimaryReason);
        Assert.Contains(InventoryPromotionSuggestionReason.IdleOnly, result.SecondaryReasons);
    }

    [Fact]
    public void HighCoverage_sozinho_nao_Suggested()
    {
        var result = Eval(Monitor(
            InventoryCommercialScenarioThesis.HighCoverage,
            InventoryCommercialScenarioReason.HighCoverageMonitoring));
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.Monitor, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.MonitorTurnover, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.HighCoverageOnly, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Expired_e_absoluto()
    {
        var result = Eval(new InventoryCommercialScenarioResult
        {
            ProductId = 9,
            Status = InventoryCommercialScenarioStatus.Expired,
            PrimaryReason = InventoryCommercialScenarioReason.Expired,
            Thesis = InventoryCommercialScenarioThesis.None,
            Confidence = InventoryAttentionConfidence.Reliable,
            AttentionQuantity = 4,
            AttentionQuantitySource = InventoryCommercialAttentionQuantitySource.ExpirySurplus,
            Scenarios = [Light(), Moderate()],
        });
        Assert.Equal(InventoryPromotionSuggestionStatus.Expired, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.RemoveExpired, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.RemoveExpired, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.Expired, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
        Assert.Equal(4, result.AttentionQuantity);
        Assert.Equal(9, result.ProductId);
    }

    [Fact]
    public void Expired_vence_Available_inconsistente()
    {
        var b4 = Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light());
        var inconsistent = Clone(b4,
            status: InventoryCommercialScenarioStatus.Expired,
            primary: InventoryCommercialScenarioReason.Expired);
        var result = Eval(inconsistent);
        Assert.Equal(InventoryPromotionSuggestionStatus.Expired, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.NotEqual(InventoryPromotionSuggestionAction.ConsiderPromotion, result.Action);
        Assert.NotEqual(InventoryPromotionSuggestionAction.PrioritizeExposure, result.Action);
    }

    [Fact]
    public void ExpiresToday_PrioritizeExposure_sem_Suggested()
    {
        var result = Eval(Monitor(
            InventoryCommercialScenarioThesis.None,
            InventoryCommercialScenarioReason.ExpiresToday,
            scenarios: [Light()]));
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.PrioritizeExposure, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.IncreaseCommercialAttention, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.ExpiresToday, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void NearExpiry_sem_sobra_nao_Suggested()
    {
        var result = Eval(Monitor(
            InventoryCommercialScenarioThesis.None,
            InventoryCommercialScenarioReason.NearExpiryWithoutSurplus));
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.PrioritizeExposure, result.Action);
        Assert.Equal(InventoryPromotionSuggestionReason.NearExpiryWithoutSurplus, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Limited_nunca_Suggested()
    {
        var b4 = Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate());
        var limited = Clone(b4, confidence: InventoryAttentionConfidence.Limited,
            extraSecondary: InventoryCommercialScenarioReason.LimitedConfidence);
        var result = Eval(limited);
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, result.Status);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.Monitor, result.Action);
        Assert.Equal(InventoryPromotionSuggestionReason.LimitedConfidence, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
        Assert.DoesNotContain(
            InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess, result.SecondaryReasons);
    }

    [Fact]
    public void Unavailable_nunca_Suggested()
    {
        var result = Eval(new InventoryCommercialScenarioResult
        {
            ProductId = 3,
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.UnavailableConfidence,
            Confidence = InventoryAttentionConfidence.Unavailable,
            Scenarios = [Light()],
        });
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionStatus.FinancialDataUnavailable, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ReviewData, result.Action);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Unavailable_Available_inconsistente_nao_vira_Monitor_confiavel()
    {
        var b4 = Clone(
            Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light()),
            confidence: InventoryAttentionConfidence.Unavailable);
        var result = Eval(b4);
        Assert.Equal(InventoryPromotionSuggestionStatus.FinancialDataUnavailable, result.Status);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.MonitorOnly, result.Status);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void ReviewData_sem_cenarios()
    {
        var result = Eval(new InventoryCommercialScenarioResult
        {
            ProductId = 4,
            Status = InventoryCommercialScenarioStatus.ReviewData,
            PrimaryReason = InventoryCommercialScenarioReason.InvalidInput,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light(), Moderate()],
        });
        Assert.Equal(InventoryPromotionSuggestionStatus.ReviewData, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ReviewData, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.ReviewInformation, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.ReviewData, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void NoRecommendation_e_NotApplicable()
    {
        var result = Eval(new InventoryCommercialScenarioResult
        {
            ProductId = 5,
            Status = InventoryCommercialScenarioStatus.NoRecommendation,
            PrimaryReason = InventoryCommercialScenarioReason.NoRecommendation,
            Confidence = InventoryAttentionConfidence.Reliable,
        });
        Assert.Equal(InventoryPromotionSuggestionStatus.NotApplicable, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.None, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.None, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.NotApplicable, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void PolicyMissing()
    {
        var result = Eval(new InventoryCommercialScenarioResult
        {
            ProductId = 6,
            Status = InventoryCommercialScenarioStatus.PolicyMissing,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyMissing,
            Thesis = InventoryCommercialScenarioThesis.ProjectedExcess30,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        });
        Assert.Equal(InventoryPromotionSuggestionStatus.PolicyMissing, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ReviewData, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.ReviewInformation, result.Objective);
        Assert.Equal(InventoryPromotionSuggestionReason.PolicyMissing, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void PolicyInvalid()
    {
        var result = Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyInvalid,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyInvalid,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        });
        Assert.Equal(InventoryPromotionSuggestionStatus.PolicyInvalid, result.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ReviewData, result.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.ReviewInformation, result.Objective);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Policy_0_percent_Available_Suggested_com_warning()
    {
        var b4 = Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate());
        b4 = Clone(b4, minMargin: 0);
        var result = Eval(b4);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(0, b4.MinimumGrossMarginPercent);
        Assert.Contains(
            InventoryPromotionSuggestionWarning.MinimumMarginPolicyAllowsAtCost, result.Warnings);
        Assert.Equal(2, result.Scenarios.Count);
    }

    [Fact]
    public void Policy_0_percent_nao_e_Missing()
    {
        var result = Eval(Clone(
            Available(InventoryCommercialScenarioThesis.ExpirySurplus, 2, Light()),
            minMargin: 0));
        Assert.NotEqual(InventoryPromotionSuggestionStatus.PolicyMissing, result.Status);
        Assert.NotEqual(InventoryPromotionSuggestionReason.PolicyMissing, result.PrimaryReason);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, result.Status);
    }

    [Fact]
    public void Atacado_Available_Suggested_com_warning_sem_alterar_cenario()
    {
        var light = Light(9.40);
        var b4 = Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, light, Moderate());
        var result = InventoryPromotionSuggestionEngine.Evaluate(b4, hasWholesalePricing: true);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Contains(
            InventoryPromotionSuggestionWarning.WholesalePricingMayDiffer, result.Warnings);
        Assert.Same(light, result.Scenarios[0]);
        Assert.Equal(9.40, result.Scenarios[0].SimulatedCatalogPrice);
        Assert.Equal(0.60, result.Scenarios[0].ReductionAmount);
    }

    [Fact]
    public void Atacado_sem_Available_nao_inventa_warning()
    {
        var result = InventoryPromotionSuggestionEngine.Evaluate(
            Monitor(InventoryCommercialScenarioThesis.Idle, InventoryCommercialScenarioReason.Idle),
            hasWholesalePricing: true);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Scenarios);
    }

    [Theory]
    [InlineData(InventoryCommercialScenarioReason.UnknownCost)]
    [InlineData(InventoryCommercialScenarioReason.InvalidCost)]
    [InlineData(InventoryCommercialScenarioReason.MissingProduct)]
    [InlineData(InventoryCommercialScenarioReason.NotSellable)]
    [InlineData(InventoryCommercialScenarioReason.CompositionProduct)]
    [InlineData(InventoryCommercialScenarioReason.AmbiguousSaleUnit)]
    public void Financeiro_indisponivel_nao_Suggested(InventoryCommercialScenarioReason reason)
    {
        var result = Eval(new InventoryCommercialScenarioResult
        {
            ProductId = 11,
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = reason,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        });
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionStatus.FinancialDataUnavailable, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Equal(InventoryPromotionSuggestionAction.ReviewData, result.Action);
    }

    [Fact]
    public void LocationLimitation_e_ReviewData()
    {
        var result = Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.ReviewData,
            PrimaryReason = InventoryCommercialScenarioReason.LocationLimitation,
            Thesis = InventoryCommercialScenarioThesis.ProjectedExcess30,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        });
        Assert.Equal(InventoryPromotionSuggestionStatus.ReviewData, result.Status);
        Assert.Equal(InventoryPromotionSuggestionReason.LocationLimitation, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, result.Status);
    }

    [Fact]
    public void LocationLimitation_Available_inconsistente_nao_Suggested()
    {
        var b4 = Clone(
            Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light()),
            extraSecondary: InventoryCommercialScenarioReason.LocationLimitation);
        var result = Eval(b4);
        Assert.Equal(InventoryPromotionSuggestionStatus.ReviewData, result.Status);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Quantidade_decimal_e_source_preservadas()
    {
        var result = Eval(Available(
            InventoryCommercialScenarioThesis.ExpirySurplus,
            2.25,
            Light()));
        Assert.Equal(2.25, result.AttentionQuantity);
        Assert.Equal(InventoryCommercialAttentionQuantitySource.ExpirySurplus, result.AttentionQuantitySource);
    }

    [Fact]
    public void Prioridade_70E_copiada_sem_recalculo()
    {
        var result = InventoryPromotionSuggestionEngine.Evaluate(
            Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light()),
            InventoryAttentionPriority.High);
        Assert.Equal(InventoryAttentionPriority.High, result.AttentionPriority);
    }

    [Fact]
    public void Input_nulo()
    {
        var result = InventoryPromotionSuggestionEngine.Evaluate((InventoryPromotionSuggestionInput?)null);
        Assert.Equal(0, result.ProductId);
        Assert.Equal(InventoryPromotionSuggestionStatus.NotApplicable, result.Status);
        Assert.Equal(InventoryPromotionSuggestionReason.InvalidInput, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void ProductId_preservado()
    {
        var b4 = Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light());
        Assert.Equal(17, Eval(Clone(b4, productId: 17)).ProductId);
    }

    [Fact]
    public void Primary_secondary_deterministico()
    {
        var b4 = WithSecondary(
            Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate()),
            InventoryCommercialScenarioReason.Idle,
            InventoryCommercialScenarioReason.HighCoverageMonitoring);
        var a = Eval(b4);
        var b = Eval(b4);
        Assert.Equal(a.PrimaryReason, b.PrimaryReason);
        Assert.Equal(a.SecondaryReasons, b.SecondaryReasons);
        Assert.DoesNotContain(a.PrimaryReason, a.SecondaryReasons);
        Assert.Equal(
            InventoryPromotionSuggestionEngine.ReasonPrecedence.Where(a.SecondaryReasons.Contains),
            a.SecondaryReasons);
    }

    [Fact]
    public void Warnings_deterministico_0_depois_atacado()
    {
        var b4 = Clone(
            Available(InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light()),
            minMargin: 0);
        var result = InventoryPromotionSuggestionEngine.Evaluate(b4, hasWholesalePricing: true);
        Assert.Equal(
            [
                InventoryPromotionSuggestionWarning.MinimumMarginPolicyAllowsAtCost,
                InventoryPromotionSuggestionWarning.WholesalePricingMayDiffer,
            ],
            result.Warnings);
        Assert.Equal(result.Warnings, InventoryPromotionSuggestionEngine.Evaluate(
            new InventoryPromotionSuggestionInput { Scenario = b4, HasWholesalePricing = true }).Warnings);
    }

    [Fact]
    public void Available_com_tese_Idle_nao_abre_segundo_funil()
    {
        var result = Eval(Clone(
            Available(InventoryCommercialScenarioThesis.Idle, 0, Light()),
            sourceQty: InventoryCommercialAttentionQuantitySource.None));
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, result.Status);
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, result.Status);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Contratos_nao_tem_cenario_preferido_nem_score()
    {
        var model = ReadSource("src", "SGDB.App", "Models", "InventoryPromotionSuggestion.cs");
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPromotionSuggestionEngine.cs");
        foreach (var text in new[] { model, engine })
        {
            Assert.DoesNotContain("PreferredScenario", text, StringComparison.Ordinal);
            Assert.DoesNotContain("RecommendedScenario", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PromotionUrgencyScore", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ApplyPromotion", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ActivatePromotion", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ValiditySuggestedAction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PromotionQuantity", text, StringComparison.Ordinal);
            Assert.DoesNotContain("QuantityToSell", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Engine_e_puro_sem_sql_write_pdv_promo_meta_combo_compra()
    {
        var engine = ReadSource("src", "SGDB.App", "Services", "InventoryPromotionSuggestionEngine.cs");
        Assert.DoesNotContain("DatabaseService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT ", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppSettingsService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("MarginSettingsService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentCulture", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSession", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreNetwork", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvCartHelper", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preco_promocional", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promo_inicio", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promo_fim", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desconto_percent", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProductCompositionService", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("meta mensal", engine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fornecedor", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("Random", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialScenarioEngine.Evaluate", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialPriceFloorEngine", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductPriceCalculator", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("MonetaryRounding", engine, StringComparison.Ordinal);
    }

    [Fact]
    public void Pipeline_query_budget_permanece_9()
    {
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryPromotionSuggestionEngine.ExpectedQueryCount);
    }

    static InventoryPromotionSuggestionResult Eval(InventoryCommercialScenarioResult scenario) =>
        InventoryPromotionSuggestionEngine.Evaluate(scenario);

    static InventoryCommercialScenarioResult Available(
        InventoryCommercialScenarioThesis thesis,
        double? quantity,
        params InventoryCommercialScenario[] scenarios) =>
        Available(thesis, quantity, DefaultSource(thesis), scenarios);

    static InventoryCommercialScenarioResult Available(
        InventoryCommercialScenarioThesis thesis,
        double? quantity,
        InventoryCommercialAttentionQuantitySource source,
        params InventoryCommercialScenario[] scenarios) =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.Available,
            PrimaryReason = thesis == InventoryCommercialScenarioThesis.ExpirySurplus
                ? InventoryCommercialScenarioReason.ExpirySurplus
                : InventoryCommercialScenarioReason.ProjectedExcess30,
            Thesis = thesis,
            Confidence = InventoryAttentionConfidence.Reliable,
            CurrentCatalogPrice = 10,
            CurrentGrossMarginPercent = 40,
            MinimumAllowedCatalogPrice = 8.20,
            MinimumGrossMarginPercent = 20,
            FinancialRoomAmount = 1.80,
            AttentionQuantity = quantity,
            AttentionQuantitySource = source,
            Scenarios = scenarios,
        };

    static InventoryCommercialAttentionQuantitySource DefaultSource(
        InventoryCommercialScenarioThesis thesis) =>
        thesis == InventoryCommercialScenarioThesis.ExpirySurplus
            ? InventoryCommercialAttentionQuantitySource.ExpirySurplus
            : InventoryCommercialAttentionQuantitySource.ProjectedExcess30;

    static InventoryCommercialScenarioResult Monitor(
        InventoryCommercialScenarioThesis thesis,
        InventoryCommercialScenarioReason primary,
        IReadOnlyList<InventoryCommercialScenario>? scenarios = null) =>
        new()
        {
            ProductId = 2,
            Status = InventoryCommercialScenarioStatus.MonitorOnly,
            PrimaryReason = primary,
            Thesis = thesis,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = scenarios ?? [],
        };

    static InventoryCommercialScenarioResult WithSecondary(
        InventoryCommercialScenarioResult source,
        params InventoryCommercialScenarioReason[] extra) =>
        Clone(source, extraSecondary: extra.Length == 1 ? extra[0] : null, extraSecondaries: extra);

    static InventoryCommercialScenarioResult Clone(
        InventoryCommercialScenarioResult source,
        InventoryCommercialScenarioStatus? status = null,
        InventoryCommercialScenarioReason? primary = null,
        InventoryAttentionConfidence? confidence = null,
        double? minMargin = null,
        int? productId = null,
        double? quantity = null,
        InventoryCommercialAttentionQuantitySource? sourceQty = null,
        InventoryCommercialScenarioReason? extraSecondary = null,
        InventoryCommercialScenarioReason[]? extraSecondaries = null)
    {
        var secondary = new List<InventoryCommercialScenarioReason>(source.SecondaryReasons ?? []);
        foreach (var reason in extraSecondaries ?? (extraSecondary is { } one ? [one] : []))
        {
            if (!secondary.Contains(reason))
                secondary.Add(reason);
        }

        return new InventoryCommercialScenarioResult
        {
            ProductId = productId ?? source.ProductId,
            Status = status ?? source.Status,
            PrimaryReason = primary ?? source.PrimaryReason,
            SecondaryReasons = secondary,
            Thesis = source.Thesis,
            Confidence = confidence ?? source.Confidence,
            CurrentCatalogPrice = source.CurrentCatalogPrice,
            CurrentGrossMarginPercent = source.CurrentGrossMarginPercent,
            MinimumAllowedCatalogPrice = source.MinimumAllowedCatalogPrice,
            MinimumGrossMarginPercent = minMargin ?? source.MinimumGrossMarginPercent,
            FinancialRoomAmount = source.FinancialRoomAmount,
            CatalogPriceIsAboveMinimumAllowed = source.CatalogPriceIsAboveMinimumAllowed,
            AttentionQuantity = quantity ?? source.AttentionQuantity,
            AttentionQuantitySource = sourceQty ?? source.AttentionQuantitySource,
            Scenarios = source.Scenarios,
        };
    }

    static InventoryCommercialScenario Light(double price = 9.40) =>
        new()
        {
            Kind = InventoryCommercialScenarioKind.Light,
            SimulatedCatalogPrice = price,
            ReductionAmount = 0.60,
            ReductionPercent = 6,
            GrossMarginPercent = 36.17,
        };

    static InventoryCommercialScenario Moderate(double price = 8.80) =>
        new()
        {
            Kind = InventoryCommercialScenarioKind.Moderate,
            SimulatedCatalogPrice = price,
            ReductionAmount = 1.20,
            ReductionPercent = 12,
            GrossMarginPercent = 31.82,
        };

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
