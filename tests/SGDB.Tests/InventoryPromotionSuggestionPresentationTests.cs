using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 70F-B5D — presentation pura. Sem XAML, SQL, PDV, promoção ativa ou recálculo.
/// </summary>
public class InventoryPromotionSuggestionPresentationTests
{
    static readonly string[] Forbidden =
    [
        "promoção recomendada",
        "promocao recomendada",
        "preço recomendado",
        "preco recomendado",
        "preço ideal",
        "preco ideal",
        "melhor preço",
        "melhor preco",
        "garantido",
        "você precisa vender",
        "voce precisa vender",
        "faça promoção",
        "faca promocao",
        "baixe para",
        "ative agora",
        "ative promoção",
        "ative promocao",
        "lucro garantido",
        "venda garantida",
        "promoção perfeita",
        "promocao perfeita",
        "quantidade da promoção",
        "quantidade da promocao",
        "quantidade que precisa vender",
        "fazer combo",
        "produto complementar",
        "meta mensal",
        "pedido sugerido",
    ];

    [Fact]
    public void QueryCount_e_zero() =>
        Assert.Equal(0, InventoryPromotionSuggestionPresentation.ExpectedQueryCount);

    [Fact]
    public void Suggested_Excess_label()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Equal(InventoryPromotionSuggestionPresentation.StatusSuggested, presented.StatusLabel);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.ThesisExcess30,
            presented.PrimaryReasonLabel);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.ThesisExcess30,
            presented.ThesisLabel);
        Assert.True(presented.IsSuggested);
        Assert.False(presented.IsExpired);
    }

    [Fact]
    public void Suggested_Expiry_label()
    {
        var presented = Present(Eval(AvailableExpiry()));
        Assert.Equal(InventoryPromotionSuggestionPresentation.StatusSuggested, presented.StatusLabel);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.ThesisExpirySurplus,
            presented.PrimaryReasonLabel);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.ThesisExpirySurplus,
            presented.ThesisLabel);
    }

    [Fact]
    public void ConsiderPromotion_label()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.ActionConsiderPromotion,
            presented.ActionLabel);
        Assert.Equal("Considerar promoção", presented.ActionLabel);
        Assert.DoesNotContain("Faça promoção", presented.ActionLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ative", presented.ActionLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Objetivo_Excess()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.ObjectiveReduceExcess30,
            presented.ObjectiveLabel);
        Assert.Equal("Reduzir o excesso projetado em 30 dias", presented.ObjectiveLabel);
    }

    [Fact]
    public void Objetivo_Expiry()
    {
        var presented = Present(Eval(AvailableExpiry()));
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.ObjectiveReduceExpiry,
            presented.ObjectiveLabel);
        Assert.Equal("Reduzir a sobra projetada até a validade", presented.ObjectiveLabel);
    }

    [Fact]
    public void Confidence_Reliable()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Equal(InventoryAttentionPresentation.ConfidenceReliable, presented.ConfidenceLabel);
        Assert.Equal("Análise disponível", presented.ConfidenceLabel);
        Assert.DoesNotContain("certeza", presented.ConfidenceLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("garantia", presented.ConfidenceLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("probabilidade", presented.ConfidenceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(InventoryAttentionPriority.Critical, "Crítica")]
    [InlineData(InventoryAttentionPriority.High, "Alta")]
    [InlineData(InventoryAttentionPriority.Medium, "Média")]
    [InlineData(InventoryAttentionPriority.Low, "Baixa")]
    [InlineData(InventoryAttentionPriority.Normal, "Normal")]
    public void Priority_labels_70E(InventoryAttentionPriority priority, string expected)
    {
        var result = WithPriority(Eval(AvailableExcess()), priority);
        var presented = Present(result);
        Assert.Equal(expected, presented.PriorityLabel);
        Assert.Equal(InventoryAttentionPresentation.PriorityLabel(priority), presented.PriorityLabel);
    }

    [Fact]
    public void Priority_ausente_e_traco()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Null(presented.AttentionPriority);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.PriorityLabel);
        Assert.NotEqual("Normal", presented.PriorityLabel);
    }

    [Fact]
    public void AttentionQuantity_null_e_traco()
    {
        var result = WithQuantity(Eval(AvailableExcess()), null, InventoryCommercialAttentionQuantitySource.None);
        var presented = Present(result);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.AttentionQuantityText);
        Assert.Equal(InventoryPromotionSuggestionPresentation.AttentionQuantityCaption, presented.AttentionQuantityLabel);
        Assert.Equal("Quantidade em atenção", presented.AttentionQuantityLabel);
        Assert.DoesNotContain("promoção", presented.AttentionQuantityLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("precisa vender", presented.AttentionQuantityLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AttentionQuantity_zero_mostra_zero()
    {
        var result = WithQuantity(
            Eval(AvailableExcess()),
            0,
            InventoryCommercialAttentionQuantitySource.ProjectedExcess30);
        var presented = Present(result);
        Assert.Equal(InventoryProjectionPresentation.FormatQty(0), presented.AttentionQuantityText);
        Assert.Equal("0", presented.AttentionQuantityText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, presented.AttentionQuantityText);
    }

    [Fact]
    public void Quantidade_decimal_ptBR()
    {
        var presented = Present(Eval(AvailableExpiry(3.25)));
        Assert.Contains("3,25", presented.AttentionQuantityText);
        Assert.NotEqual("3", presented.AttentionQuantityText);
        Assert.Equal(InventoryProjectionPresentation.FormatQty(3.25), presented.AttentionQuantityText);
    }

    [Fact]
    public void Source_Expiry()
    {
        var presented = Present(Eval(AvailableExpiry()));
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.QuantitySourceExpiry,
            presented.AttentionQuantitySourceLabel);
        Assert.Equal("Projeção até a validade", presented.AttentionQuantitySourceLabel);
    }

    [Fact]
    public void Source_Excess()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.QuantitySourceExcess30,
            presented.AttentionQuantitySourceLabel);
        Assert.Equal("Projeção de excesso em 30 dias", presented.AttentionQuantitySourceLabel);
    }

    [Fact]
    public void Dois_cenarios()
    {
        var presented = Present(Eval(AvailableExcess(Light(), Moderate())));
        Assert.Equal(2, presented.ScenarioOptions.Count);
        Assert.Equal(InventoryCommercialScenarioPresentation.KindLight, presented.ScenarioOptions[0].KindLabel);
        Assert.Equal(InventoryCommercialScenarioPresentation.KindModerate, presented.ScenarioOptions[1].KindLabel);
    }

    [Fact]
    public void Um_cenario()
    {
        var presented = Present(Eval(AvailableExcess(Light())));
        Assert.Single(presented.ScenarioOptions);
        Assert.Equal(InventoryCommercialScenarioPresentation.KindLight, presented.ScenarioOptions[0].KindLabel);
        Assert.DoesNotContain(presented.ScenarioOptions, s => s.Kind == InventoryCommercialScenarioKind.Moderate);
    }

    [Fact]
    public void Ordem_Light_Moderate_preservada()
    {
        var cheaperModerateFirst = Moderate(7.00);
        var light = Light(9.40);
        var presented = Present(Eval(AvailableExcess(cheaperModerateFirst, light)));
        Assert.Equal(2, presented.ScenarioOptions.Count);
        Assert.Equal(InventoryCommercialScenarioKind.Moderate, presented.ScenarioOptions[0].Kind);
        Assert.Equal(InventoryCommercialScenarioKind.Light, presented.ScenarioOptions[1].Kind);
        Assert.Equal(ProductPriceHelper.MoneyBr(7.00), presented.ScenarioOptions[0].SimulatedPriceText);
        Assert.Equal(ProductPriceHelper.MoneyBr(9.40), presented.ScenarioOptions[1].SimulatedPriceText);
    }

    [Fact]
    public void Cenario_nao_recalculado()
    {
        var custom = new InventoryCommercialScenario
        {
            Kind = InventoryCommercialScenarioKind.Light,
            SimulatedCatalogPrice = 9.40,
            ReductionAmount = 0.77,
            ReductionPercent = 12.5,
            GrossMarginPercent = 33.3,
        };
        var presented = Present(Eval(AvailableExcess(custom)));
        var option = Assert.Single(presented.ScenarioOptions);
        Assert.Equal(ProductPriceHelper.MoneyBr(0.77), option.ReductionAmountText);
        Assert.Equal("12,5%", option.ReductionPercentText);
        Assert.Equal("33,3%", option.GrossMarginText);
        Assert.Equal(ProductPriceHelper.MoneyBr(9.40), option.SimulatedPriceText);
        Assert.Equal("Cenário leve", option.KindLabel);
        Assert.DoesNotContain("Recomendado", option.KindLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Melhor opção", option.KindLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ideal", option.KindLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_sem_cenario()
    {
        var presented = Present(Eval(new InventoryCommercialScenarioResult
        {
            ProductId = 9,
            Status = InventoryCommercialScenarioStatus.Expired,
            PrimaryReason = InventoryCommercialScenarioReason.Expired,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light(), Moderate()],
        }));
        Assert.True(presented.IsExpired);
        Assert.False(presented.IsSuggested);
        Assert.Empty(presented.ScenarioOptions);
        Assert.Equal("Produto vencido", presented.StatusLabel);
        Assert.DoesNotContain("Considerar promoção", presented.ActionLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("incentivo", presented.DisclaimerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_RemoveExpired()
    {
        var presented = Present(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.Expired,
            PrimaryReason = InventoryCommercialScenarioReason.Expired,
            Confidence = InventoryAttentionConfidence.Reliable,
        }));
        Assert.Equal(InventoryPromotionSuggestionPresentation.ActionRemoveExpired, presented.ActionLabel);
        Assert.Equal("Retirar / conferir", presented.ActionLabel);
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.ObjectiveRemoveExpired,
            presented.ObjectiveLabel);
        Assert.Contains("retirado", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpiresToday_sem_promocao()
    {
        var presented = Present(Eval(Monitor(
            InventoryCommercialScenarioReason.ExpiresToday,
            scenarios: [Light()])));
        Assert.False(presented.IsSuggested);
        Assert.Empty(presented.ScenarioOptions);
        Assert.Equal(InventoryPromotionSuggestionPresentation.ActionPrioritizeExposure, presented.ActionLabel);
        Assert.Equal("Vence hoje", presented.PrimaryReasonLabel);
        Assert.Contains("sem sugerir redução de preço", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Considerar promoção", presented.ActionLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Idle_sem_promocao()
    {
        var presented = Present(Eval(Monitor(
            InventoryCommercialScenarioReason.Idle,
            InventoryCommercialScenarioThesis.Idle,
            [Light()])));
        Assert.False(presented.IsSuggested);
        Assert.Empty(presented.ScenarioOptions);
        Assert.Equal("Produto parado", presented.PrimaryReasonLabel);
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.ObjectiveIncreaseAttention,
            presented.ObjectiveLabel);
        Assert.Contains("isoladamente", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("redução de preço", presented.ActionLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HighCoverage_sem_promocao()
    {
        var presented = Present(Eval(Monitor(
            InventoryCommercialScenarioReason.HighCoverageMonitoring,
            InventoryCommercialScenarioThesis.HighCoverage)));
        Assert.False(presented.IsSuggested);
        Assert.Empty(presented.ScenarioOptions);
        Assert.Equal("Monitorar", presented.ActionLabel);
        Assert.Equal("Cobertura elevada", presented.PrimaryReasonLabel);
        Assert.Equal("Cobertura elevada", presented.ThesisLabel);
        Assert.Contains("acompanhamento", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não para redução de preço", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Limited_sem_promocao()
    {
        var b4 = AvailableExcess();
        b4 = Clone(b4, confidence: InventoryAttentionConfidence.Limited);
        var presented = Present(Eval(b4));
        Assert.False(presented.IsSuggested);
        Assert.Empty(presented.ScenarioOptions);
        Assert.Equal(InventoryAttentionPresentation.ConfidenceLimited, presented.ConfidenceLabel);
        Assert.Equal("Análise com limitações", presented.ConfidenceLabel);
        Assert.Equal("Análise limitada", presented.PrimaryReasonLabel);
        Assert.Contains("não sustentam uma sugestão numérica", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewData()
    {
        var presented = Present(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.ReviewData,
            PrimaryReason = InventoryCommercialScenarioReason.InvalidInput,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        }));
        Assert.True(presented.IsReviewData);
        Assert.Equal("Revisar dados", presented.StatusLabel);
        Assert.Equal("Revisar dados", presented.ActionLabel);
        Assert.Equal("Revisar dados", presented.PrimaryReasonLabel);
        Assert.Empty(presented.ScenarioOptions);
        Assert.Contains("conferidos", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScenarioMissing()
    {
        var presented = Present(new InventoryPromotionSuggestionResult
        {
            ProductId = 4,
            Status = InventoryPromotionSuggestionStatus.ReviewData,
            Action = InventoryPromotionSuggestionAction.ReviewData,
            Objective = InventoryPromotionSuggestionObjective.ReviewInformation,
            PrimaryReason = InventoryPromotionSuggestionReason.ScenarioMissing,
            Confidence = InventoryAttentionConfidence.Unavailable,
        });
        Assert.True(presented.IsReviewData);
        Assert.Equal("Cenário comercial ausente", presented.PrimaryReasonLabel);
        Assert.Contains("estrutural", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não como diagnóstico de quantidade", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.ScenarioOptions);
    }

    [Fact]
    public void DuplicateScenario()
    {
        var presented = Present(new InventoryPromotionSuggestionResult
        {
            ProductId = 5,
            Status = InventoryPromotionSuggestionStatus.ReviewData,
            Action = InventoryPromotionSuggestionAction.ReviewData,
            Objective = InventoryPromotionSuggestionObjective.ReviewInformation,
            PrimaryReason = InventoryPromotionSuggestionReason.DuplicateScenario,
            Confidence = InventoryAttentionConfidence.Unavailable,
        });
        Assert.Equal("Conflito de cenários comerciais", presented.PrimaryReasonLabel);
        Assert.Contains("mais de um cenário", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não escolher", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("estoque está baixo", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.ScenarioOptions);
    }

    [Fact]
    public void PolicyMissing()
    {
        var presented = Present(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyMissing,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyMissing,
            Thesis = InventoryCommercialScenarioThesis.ProjectedExcess30,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        }));
        Assert.Equal("Margem mínima não configurada", presented.StatusLabel);
        Assert.Equal("Margem mínima não configurada", presented.PrimaryReasonLabel);
        Assert.Contains("Sistema → Política comercial", presented.Explanation, StringComparison.Ordinal);
        Assert.Empty(presented.ScenarioOptions);
        Assert.Empty(presented.WarningLabels);
        Assert.NotEqual("0%", presented.Explanation);
        Assert.DoesNotContain("margem mínima de 0%", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PolicyInvalid()
    {
        var presented = Present(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyInvalid,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyInvalid,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        }));
        Assert.Equal("Margem mínima inválida", presented.StatusLabel);
        Assert.Contains("inválida", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sistema → Política comercial", presented.Explanation, StringComparison.Ordinal);
        Assert.Empty(presented.ScenarioOptions);
    }

    [Fact]
    public void FinancialUnavailable()
    {
        var presented = Present(Eval(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.UnknownCost,
            Confidence = InventoryAttentionConfidence.Reliable,
            CurrentCatalogPrice = null,
            CurrentGrossMarginPercent = null,
            Scenarios = [Light()],
        }));
        Assert.Equal("Análise financeira indisponível", presented.StatusLabel);
        Assert.Equal("Custo atual desconhecido", presented.PrimaryReasonLabel);
        Assert.Contains("não é zero", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margem é 0%", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.ScenarioOptions);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.ThesisLabel);
    }

    [Fact]
    public void Policy_0_warning()
    {
        var presented = Present(Eval(Clone(AvailableExcess(), minMargin: 0)));
        Assert.True(presented.IsSuggested);
        Assert.Contains(
            InventoryPromotionSuggestionPresentation.WarningMinimumMarginAllowsAtCost,
            presented.WarningLabels);
        Assert.Contains("margem mínima de 0%", presented.WarningLabels[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inválida", presented.WarningLabels[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("não configurada", presented.WarningLabels[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, presented.ScenarioOptions.Count);
    }

    [Fact]
    public void Policy_0_nao_e_Missing()
    {
        var presented = Present(Eval(Clone(AvailableExpiry(), minMargin: 0)));
        Assert.NotEqual(InventoryPromotionSuggestionStatus.PolicyMissing, presented.Status);
        Assert.NotEqual("Margem mínima não configurada", presented.StatusLabel);
        Assert.NotEqual("Margem mínima inválida", presented.StatusLabel);
        Assert.NotEqual("Análise financeira indisponível", presented.StatusLabel);
        Assert.Equal(InventoryPromotionSuggestionPresentation.StatusSuggested, presented.StatusLabel);
        Assert.Contains(
            InventoryPromotionSuggestionWarning.MinimumMarginPolicyAllowsAtCost,
            presented.Warnings);
    }

    [Fact]
    public void Wholesale_warning()
    {
        var presented = Present(InventoryPromotionSuggestionEngine.Evaluate(
            AvailableExcess(),
            hasWholesalePricing: true));
        Assert.Contains(
            InventoryPromotionSuggestionPresentation.WarningWholesalePricingMayDiffer,
            presented.WarningLabels);
        Assert.Contains("pode", presented.WarningLabels[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" vai ", " " + presented.WarningLabels[0] + " ", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vai cobrar", presented.WarningLabels[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Atacado_usa_pode()
    {
        var label = InventoryPromotionSuggestionPresentation.WarningLabel(
            InventoryPromotionSuggestionWarning.WholesalePricingMayDiffer);
        Assert.Contains("pode ser diferente", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("será diferente", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vai ser", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vai cobrar", label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Primary_reason_label_e_explanation()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Equal(
            InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess,
            presented.PrimaryReason);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.ThesisExcess30,
            presented.PrimaryReasonLabel);
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.ReasonExplanation(
                InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess),
            presented.Explanation);
        Assert.Contains("próximos 30 dias", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Secondary_reasons_preservados()
    {
        var b4 = Clone(AvailableExcess(), extraSecondary: InventoryCommercialScenarioReason.Idle);
        b4 = Clone(b4, extraSecondary: InventoryCommercialScenarioReason.HighCoverageMonitoring);
        var presented = Present(Eval(b4));
        Assert.Equal(2, presented.SecondaryReasonLabels.Count);
        Assert.Equal("Produto parado", presented.SecondaryReasonLabels[0]);
        Assert.Equal("Cobertura elevada", presented.SecondaryReasonLabels[1]);
        Assert.Equal(
            presented.SecondaryReasons.Select(InventoryPromotionSuggestionPresentation.ReasonLabel),
            presented.SecondaryReasonLabels);
    }

    [Fact]
    public void Secondary_nao_repete_primary()
    {
        var b4 = Clone(AvailableExcess(), extraSecondary: InventoryCommercialScenarioReason.Idle);
        var presented = Present(Eval(b4));
        Assert.DoesNotContain(presented.PrimaryReasonLabel, presented.SecondaryReasonLabels);
        Assert.DoesNotContain(
            InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess,
            presented.SecondaryReasons);
        Assert.Equal(new[] { "Produto parado" }, presented.SecondaryReasonLabels);
    }

    [Fact]
    public void Warnings_separados_de_reasons()
    {
        var presented = Present(InventoryPromotionSuggestionEngine.Evaluate(
            Clone(AvailableExcess(), minMargin: 0),
            hasWholesalePricing: true));
        Assert.Equal(2, presented.WarningLabels.Count);
        Assert.DoesNotContain(presented.PrimaryReasonLabel, presented.WarningLabels);
        foreach (var secondary in presented.SecondaryReasonLabels)
            Assert.DoesNotContain(secondary, presented.WarningLabels);
        Assert.Contains("0%", presented.WarningLabels[0]);
        Assert.Contains("atacado", presented.WarningLabels[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disclaimer_Suggested()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Equal(InventoryPromotionSuggestionPresentation.SuggestedDisclaimer, presented.DisclaimerText);
        Assert.Contains("simulação", presented.DisclaimerText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("apoio à decisão", presented.DisclaimerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disclaimer_nao_altera_preco_automaticamente()
    {
        var presented = Present(Eval(AvailableExcess()));
        Assert.Contains(
            "não altera preços nem ativa promoções automaticamente",
            presented.DisclaimerText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Linguagem_proibida_ausente()
    {
        var presented = Present(Eval(AvailableExcess()));
        var blob = Blob(presented);
        Assert.DoesNotContain("promoção recomendada", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preço recomendado", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preço ideal", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("melhor preço", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("garantido", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("você precisa vender", blob, StringComparison.OrdinalIgnoreCase);
        AssertNoForbidden(presented);
        foreach (var reason in Enum.GetValues<InventoryPromotionSuggestionReason>())
            AssertNoForbiddenBlob(
                InventoryPromotionSuggestionPresentation.ReasonLabel(reason) + " " +
                InventoryPromotionSuggestionPresentation.ReasonExplanation(reason));
        foreach (var warning in Enum.GetValues<InventoryPromotionSuggestionWarning>())
            AssertNoForbiddenBlob(InventoryPromotionSuggestionPresentation.WarningLabel(warning));
    }

    [Fact]
    public void Zero_nao_e_traco()
    {
        var qty = Present(WithQuantity(
            Eval(AvailableExcess()),
            0,
            InventoryCommercialAttentionQuantitySource.ProjectedExcess30));
        Assert.Equal("0", qty.AttentionQuantityText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, qty.AttentionQuantityText);

        var option = Present(Eval(AvailableExcess(new InventoryCommercialScenario
        {
            Kind = InventoryCommercialScenarioKind.Light,
            SimulatedCatalogPrice = 10,
            ReductionAmount = 0,
            ReductionPercent = 0,
            GrossMarginPercent = 0,
        }))).ScenarioOptions[0];
        Assert.Equal("R$ 0,00", option.ReductionAmountText);
        Assert.Equal("0%", option.ReductionPercentText);
        Assert.Equal("0%", option.GrossMarginText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, option.ReductionAmountText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, option.ReductionPercentText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, option.GrossMarginText);
    }

    [Fact]
    public void Todos_status_mapeados()
    {
        foreach (var status in Enum.GetValues<InventoryPromotionSuggestionStatus>())
        {
            var label = InventoryPromotionSuggestionPresentation.StatusLabel(status);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(status.ToString(), label);
        }
    }

    [Fact]
    public void Todas_actions_mapeadas()
    {
        foreach (var action in Enum.GetValues<InventoryPromotionSuggestionAction>())
        {
            var label = InventoryPromotionSuggestionPresentation.ActionLabel(action);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(action.ToString(), label);
        }
    }

    [Fact]
    public void Todos_objectives_mapeados()
    {
        foreach (var objective in Enum.GetValues<InventoryPromotionSuggestionObjective>())
        {
            var label = InventoryPromotionSuggestionPresentation.ObjectiveLabel(objective);
            Assert.False(string.IsNullOrWhiteSpace(label));
            if (objective != InventoryPromotionSuggestionObjective.None)
                Assert.NotEqual(objective.ToString(), label);
            else
                Assert.Equal(InventoryProjectionPresentation.EmDash, label);
        }
    }

    [Fact]
    public void Todos_reasons_mapeados()
    {
        foreach (var reason in Enum.GetValues<InventoryPromotionSuggestionReason>())
        {
            var label = InventoryPromotionSuggestionPresentation.ReasonLabel(reason);
            var explanation = InventoryPromotionSuggestionPresentation.ReasonExplanation(reason);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.False(string.IsNullOrWhiteSpace(explanation));
            Assert.NotEqual(reason.ToString(), label);
            Assert.NotEqual(reason.ToString(), explanation);
        }
    }

    [Fact]
    public void Todos_warnings_mapeados()
    {
        foreach (var warning in Enum.GetValues<InventoryPromotionSuggestionWarning>())
        {
            var label = InventoryPromotionSuggestionPresentation.WarningLabel(warning);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(warning.ToString(), label);
        }
    }

    [Fact]
    public void Todas_teses_mapeadas()
    {
        foreach (var thesis in Enum.GetValues<InventoryCommercialScenarioThesis>())
        {
            var label = InventoryPromotionSuggestionPresentation.ThesisLabel(thesis);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(thesis.ToString(), label);
        }
    }

    [Fact]
    public void Todos_quantity_sources_mapeados()
    {
        foreach (var source in Enum.GetValues<InventoryCommercialAttentionQuantitySource>())
        {
            var label = InventoryPromotionSuggestionPresentation.QuantitySourceLabel(source);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(source.ToString(), label);
        }
    }

    [Fact]
    public void Nenhum_ToString_fallback()
    {
        var source = ReadSource("src", "SGDB.App", "Models", "InventoryPromotionSuggestionPresentation.cs");
        Assert.DoesNotContain(".ToString()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("enum.ToString", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Determinismo()
    {
        var result = Eval(Clone(AvailableExcess(), extraSecondary: InventoryCommercialScenarioReason.Idle));
        var a = Present(result);
        var b = Present(result);
        Assert.Equal(a.StatusLabel, b.StatusLabel);
        Assert.Equal(a.Explanation, b.Explanation);
        Assert.Equal(a.ActionLabel, b.ActionLabel);
        Assert.Equal(a.ObjectiveLabel, b.ObjectiveLabel);
        Assert.Equal(a.AttentionQuantityText, b.AttentionQuantityText);
        Assert.Equal(a.SecondaryReasonLabels, b.SecondaryReasonLabels);
        Assert.Equal(a.ScenarioOptions[0].SimulatedPriceText, b.ScenarioOptions[0].SimulatedPriceText);
        Assert.Equal(a.DisclaimerText, b.DisclaimerText);
    }

    [Fact]
    public void FromRow_usa_ProductId_70C()
    {
        var presented = InventoryPromotionSuggestionPresentation.FromRow(
            new InventoryPromotionSuggestionRow
            {
                ProductId = 42,
                Suggestion = Eval(AvailableExcess()),
            });
        Assert.Equal(42, presented.ProductId);
        Assert.True(presented.IsSuggested);
    }

    [Fact]
    public void Apply_preserva_ordem()
    {
        var snapshot = InventoryPromotionSuggestionPresentation.Apply(
            new InventoryPromotionSuggestionSnapshot
            {
                QueryCount = 9,
                Rows =
                [
                    new() { ProductId = 10, Suggestion = Eval(AvailableExcess()) },
                    new() { ProductId = 2, Suggestion = Eval(Monitor(InventoryCommercialScenarioReason.Idle)) },
                ],
            });
        Assert.Equal(9, snapshot.QueryCount);
        Assert.Equal(new[] { 10, 2 }, snapshot.Rows.Select(r => r.ProductId));
        Assert.True(snapshot.Rows[0].IsSuggested);
        Assert.False(snapshot.Rows[1].IsSuggested);
    }

    [Fact]
    public void Kind_labels_sao_possibilidades()
    {
        Assert.Equal("Cenário leve", InventoryCommercialScenarioPresentation.KindLight);
        Assert.Equal("Cenário moderado", InventoryCommercialScenarioPresentation.KindModerate);
        var presented = Present(Eval(AvailableExcess()));
        Assert.DoesNotContain("Recomendado", presented.ScenarioOptions[0].KindLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("escolha este", Blob(presented), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Monitor_disclaimer_curto()
    {
        var presented = Present(Eval(Monitor(InventoryCommercialScenarioReason.HighCoverageMonitoring)));
        Assert.Equal(InventoryPromotionSuggestionPresentation.ShortDisclaimer, presented.DisclaimerText);
        Assert.Contains("não altera preços automaticamente", presented.DisclaimerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Light_Moderate_nao_sao_favoritos()
    {
        var presented = Present(Eval(AvailableExcess()));
        foreach (var option in presented.ScenarioOptions)
        {
            Assert.DoesNotContain("melhor", option.KindLabel, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ideal", option.KindLabel, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recomendado", option.KindLabel, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recomendado", option.Explanation, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Pureza_sem_sql_write_pdv_xaml_combo_meta_compra()
    {
        var source = ReadSource("src", "SGDB.App", "Models", "InventoryPromotionSuggestionPresentation.cs");
        Assert.DoesNotContain("DatabaseService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppSettingsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarginSettingsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentCulture", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreNetwork", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvCartHelper", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preco_promocional", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promo_inicio", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promo_fim", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desconto_percent", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProductCompositionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Fornecedor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Pedido sugerido", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meta mensal", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".xaml", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InventoryIntelligenceModuleView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionDetailWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Random", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Evaluate(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialScenarioEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPromotionSuggestionEngine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaleFromCostAndMargin", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Comprar", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Não comprar", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Repor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("combo", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("produto complementar", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contribuição para meta", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nao_integrado_na_Window()
    {
        var view = ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml.cs");
        var xaml = ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml");
        var detailCs = ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml.cs");
        var detailXaml = ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml");
        foreach (var text in new[] { view, xaml, detailCs, detailXaml })
        {
            Assert.DoesNotContain("InventoryPromotionSuggestionPresentation", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryPromotionSuggestionComposer", text, StringComparison.Ordinal);
        }
    }

    static InventoryPromotionSuggestionPresentationRow Present(
        InventoryPromotionSuggestionResult result) =>
        InventoryPromotionSuggestionPresentation.FromResult(result);

    static InventoryPromotionSuggestionResult Eval(InventoryCommercialScenarioResult scenario) =>
        InventoryPromotionSuggestionEngine.Evaluate(scenario);

    static InventoryCommercialScenarioResult AvailableExcess(
        params InventoryCommercialScenario[] scenarios) =>
        Available(
            InventoryCommercialScenarioThesis.ProjectedExcess30,
            8,
            InventoryCommercialAttentionQuantitySource.ProjectedExcess30,
            scenarios.Length == 0 ? [Light(), Moderate()] : scenarios);

    static InventoryCommercialScenarioResult AvailableExpiry(double qty = 3.5) =>
        Available(
            InventoryCommercialScenarioThesis.ExpirySurplus,
            qty,
            InventoryCommercialAttentionQuantitySource.ExpirySurplus,
            Light(),
            Moderate());

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

    static InventoryCommercialScenarioResult Monitor(
        InventoryCommercialScenarioReason primary,
        InventoryCommercialScenarioThesis thesis = InventoryCommercialScenarioThesis.None,
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

    static InventoryCommercialScenarioResult Clone(
        InventoryCommercialScenarioResult source,
        InventoryAttentionConfidence? confidence = null,
        double? minMargin = null,
        InventoryCommercialScenarioReason? extraSecondary = null)
    {
        var secondary = new List<InventoryCommercialScenarioReason>(source.SecondaryReasons ?? []);
        if (extraSecondary is { } extra && !secondary.Contains(extra))
            secondary.Add(extra);

        return new InventoryCommercialScenarioResult
        {
            ProductId = source.ProductId,
            Status = source.Status,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = secondary,
            Thesis = source.Thesis,
            Confidence = confidence ?? source.Confidence,
            CurrentCatalogPrice = source.CurrentCatalogPrice,
            CurrentGrossMarginPercent = source.CurrentGrossMarginPercent,
            MinimumAllowedCatalogPrice = source.MinimumAllowedCatalogPrice,
            MinimumGrossMarginPercent = minMargin ?? source.MinimumGrossMarginPercent,
            FinancialRoomAmount = source.FinancialRoomAmount,
            CatalogPriceIsAboveMinimumAllowed = source.CatalogPriceIsAboveMinimumAllowed,
            AttentionQuantity = source.AttentionQuantity,
            AttentionQuantitySource = source.AttentionQuantitySource,
            Scenarios = source.Scenarios,
        };
    }

    static InventoryPromotionSuggestionResult WithPriority(
        InventoryPromotionSuggestionResult source,
        InventoryAttentionPriority priority) =>
        new()
        {
            ProductId = source.ProductId,
            Status = source.Status,
            Action = source.Action,
            Thesis = source.Thesis,
            Objective = source.Objective,
            Confidence = source.Confidence,
            AttentionPriority = priority,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = source.SecondaryReasons,
            Warnings = source.Warnings,
            AttentionQuantity = source.AttentionQuantity,
            AttentionQuantitySource = source.AttentionQuantitySource,
            Scenarios = source.Scenarios,
        };

    static InventoryPromotionSuggestionResult WithQuantity(
        InventoryPromotionSuggestionResult source,
        double? quantity,
        InventoryCommercialAttentionQuantitySource qtySource) =>
        new()
        {
            ProductId = source.ProductId,
            Status = source.Status,
            Action = source.Action,
            Thesis = source.Thesis,
            Objective = source.Objective,
            Confidence = source.Confidence,
            AttentionPriority = source.AttentionPriority,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = source.SecondaryReasons,
            Warnings = source.Warnings,
            AttentionQuantity = quantity,
            AttentionQuantitySource = qtySource,
            Scenarios = source.Scenarios,
        };

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

    static void AssertNoForbidden(InventoryPromotionSuggestionPresentationRow presented) =>
        AssertNoForbiddenBlob(Blob(presented));

    static string Blob(InventoryPromotionSuggestionPresentationRow presented) =>
        string.Join(' ',
            presented.StatusLabel,
            presented.ActionLabel,
            presented.ThesisLabel,
            presented.ObjectiveLabel,
            presented.ConfidenceLabel,
            presented.PriorityLabel,
            presented.PrimaryReasonLabel,
            presented.Explanation,
            presented.DisclaimerText,
            presented.AttentionQuantityLabel,
            presented.AttentionQuantitySourceLabel,
            string.Join(' ', presented.SecondaryReasonLabels),
            string.Join(' ', presented.WarningLabels),
            string.Join(' ', presented.ScenarioOptions.Select(s =>
                s.KindLabel + " " + s.Explanation + " " + s.SimulatedPriceText)));

    static void AssertNoForbiddenBlob(string blob)
    {
        foreach (var phrase in Forbidden)
            Assert.DoesNotContain(phrase, blob, StringComparison.OrdinalIgnoreCase);
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
