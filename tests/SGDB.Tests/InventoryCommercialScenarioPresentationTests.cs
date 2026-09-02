using System.IO;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 70F-B4D — presentation pura. Sem XAML, SQL, PDV, promoção ou recálculo comercial.
/// </summary>
public class InventoryCommercialScenarioPresentationTests
{
    static readonly string[] Forbidden =
    [
        "preço recomendado",
        "preco recomendado",
        "desconto recomendado",
        "lucro garantido",
        "preço ideal",
        "preco ideal",
        "preço promocional",
        "promoção leve",
        "promocao leve",
        "quantidade a promover",
        "dar desconto",
        "fazer promoção",
        "fazer promocao",
        "fazer combo",
    ];

    [Fact]
    public void QueryCount_e_zero() =>
        Assert.Equal(0, InventoryCommercialScenarioPresentation.ExpectedQueryCount);

    [Fact]
    public void Available_com_dois_cenarios()
    {
        var presented = Present(Available());
        Assert.True(presented.IsScenarioAvailable);
        Assert.True(presented.ShowScenarioOptions);
        Assert.True(presented.ShowFinancialAnalysis);
        Assert.Equal(InventoryCommercialScenarioPresentation.StatusAvailable, presented.StatusLabel);
        Assert.Equal(2, presented.Scenarios.Count);
        Assert.Equal(InventoryCommercialScenarioPresentation.KindLight, presented.Scenarios[0].KindLabel);
        Assert.Equal(InventoryCommercialScenarioPresentation.KindModerate, presented.Scenarios[1].KindLabel);
        Assert.Contains("não altera preços automaticamente", presented.SimulationDisclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InventoryCommercialScenarioPresentation.OperatorFooterText, presented.OperatorFooter);
        Assert.Equal(InventoryCommercialScenarioPresentation.OperatorFooterText, presented.ActionGuidance);
    }

    [Fact]
    public void Available_com_um_cenario()
    {
        var result = Available();
        result = WithScenarios(result, Light());
        var presented = Present(result);
        Assert.True(presented.IsScenarioAvailable);
        Assert.Single(presented.Scenarios);
        Assert.Equal(InventoryCommercialScenarioPresentation.KindLight, presented.Scenarios[0].KindLabel);
        Assert.DoesNotContain(presented.Scenarios, s => s.Kind == InventoryCommercialScenarioKind.Moderate);
    }

    [Fact]
    public void Light_e_Moderate_labels()
    {
        Assert.Equal("Cenário leve", InventoryCommercialScenarioPresentation.KindLabel(InventoryCommercialScenarioKind.Light));
        Assert.Equal("Cenário moderado", InventoryCommercialScenarioPresentation.KindLabel(InventoryCommercialScenarioKind.Moderate));
    }

    [Fact]
    public void Preco_catalogo()
    {
        var presented = Present(Available());
        Assert.Equal("Preço atual (catálogo)", presented.CurrentCatalogPriceLabel);
        Assert.Equal(ProductPriceHelper.MoneyBr(10), presented.CurrentCatalogPriceText);
        Assert.StartsWith("R$", presented.CurrentCatalogPriceText);
        Assert.NotEqual("Preço atual", presented.CurrentCatalogPriceLabel);
    }

    [Fact]
    public void Margem_atual()
    {
        var presented = Present(Available(margin: 40));
        Assert.Equal("Margem atual", presented.CurrentGrossMarginLabel);
        Assert.Equal("40%", presented.CurrentGrossMarginText);
    }

    [Fact]
    public void Margem_minima()
    {
        var presented = Present(Available(minMargin: 20));
        Assert.Equal("Margem mínima", presented.MinimumGrossMarginLabel);
        Assert.Equal("20%", presented.MinimumGrossMarginText);
    }

    [Fact]
    public void Policy_0_porcento_mostra_zero()
    {
        var presented = Present(Available(minMargin: 0));
        Assert.Equal("0%", presented.MinimumGrossMarginText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, presented.MinimumGrossMarginText);
        Assert.DoesNotContain("não configurada", presented.MinimumGrossMarginText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Piso_financeiro()
    {
        var presented = Present(Available());
        Assert.Equal("Piso financeiro", presented.FloorPriceLabel);
        Assert.Equal(ProductPriceHelper.MoneyBr(8.20), presented.FloorPriceText);
        Assert.Contains("margem mínima configurada", presented.FloorExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recomendado", presented.FloorExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promocional", presented.FloorExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ideal", presented.FloorExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Espaco_ate_o_piso()
    {
        var presented = Present(Available());
        Assert.Equal("Espaço até o piso", presented.FinancialRoomLabel);
        Assert.Equal(ProductPriceHelper.MoneyBr(1.80), presented.FinancialRoomText);
        Assert.DoesNotContain("desconto disponível", presented.FinancialRoomLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AttentionQuantity_Excess()
    {
        var presented = Present(Available());
        Assert.Equal(
            InventoryCommercialScenarioPresentation.AttentionExcessCaption,
            presented.AttentionQuantityLabel);
        Assert.Equal(InventoryProjectionPresentation.FormatCalculatedQty(8), presented.AttentionQuantityText);
        Assert.DoesNotContain("promover", presented.AttentionQuantityLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AttentionQuantity_Expiry()
    {
        var presented = Present(Available(
            thesis: InventoryCommercialScenarioThesis.ExpirySurplus,
            reason: InventoryCommercialScenarioReason.ExpirySurplus,
            qty: 3.5,
            source: InventoryCommercialAttentionQuantitySource.ExpirySurplus));
        Assert.Equal(
            InventoryCommercialScenarioPresentation.AttentionExpiryCaption,
            presented.AttentionQuantityLabel);
        Assert.Equal(InventoryProjectionPresentation.FormatCalculatedQty(3.5), presented.AttentionQuantityText);
    }

    [Fact]
    public void Quantidade_decimal_nao_vira_inteiro()
    {
        var presented = Present(Available(
            thesis: InventoryCommercialScenarioThesis.ExpirySurplus,
            reason: InventoryCommercialScenarioReason.ExpirySurplus,
            qty: 3.25,
            source: InventoryCommercialAttentionQuantitySource.ExpirySurplus));
        Assert.Contains("3,25", presented.AttentionQuantityText);
        Assert.NotEqual("3", presented.AttentionQuantityText);
    }

    [Fact]
    public void ReductionAmount_e_percent()
    {
        var option = Present(Available()).Scenarios[0];
        Assert.Equal(ProductPriceHelper.MoneyBr(0.60), option.ReductionAmountText);
        Assert.Equal("6%", option.ReductionPercentText);
        Assert.Equal($"{ProductPriceHelper.MoneyBr(0.60)} (6%)", option.ReductionSummaryText);
        Assert.Equal(ProductPriceHelper.MoneyBr(9.40), option.SimulatedPriceText);
    }

    [Fact]
    public void GrossMargin_do_cenario()
    {
        var option = Present(Available()).Scenarios[0];
        Assert.Equal("36,17%", option.GrossMarginText);
        Assert.Equal("Margem bruta no cenário", InventoryCommercialScenarioPresentation.ScenarioMarginCaption);
        Assert.Contains("Simulação", option.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("catálogo", option.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Lucro", option.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("garantida", option.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expired_sem_cenario_nem_incentivo()
    {
        var presented = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.Expired,
            PrimaryReason = InventoryCommercialScenarioReason.Expired,
            Thesis = InventoryCommercialScenarioThesis.None,
            CurrentCatalogPrice = 10,
            MinimumAllowedCatalogPrice = 8.20,
            Scenarios = [Light(), Moderate()],
        });
        Assert.Equal(InventoryCommercialScenarioPresentation.ExpiredExplanation, presented.Explanation);
        Assert.Equal("Produto vencido", presented.StatusLabel);
        Assert.False(presented.IsScenarioAvailable);
        Assert.False(presented.ShowFinancialAnalysis);
        Assert.False(presented.ShowScenarioOptions);
        Assert.Empty(presented.Scenarios);
        Assert.Contains("retirar", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("redução", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpiresToday_sem_desconto()
    {
        var presented = Present(Monitor(InventoryCommercialScenarioReason.ExpiresToday));
        Assert.Equal(InventoryCommercialScenarioPresentation.ExpiresTodayExplanation, presented.Explanation);
        Assert.False(presented.IsScenarioAvailable);
        Assert.Empty(presented.Scenarios);
        Assert.Contains("priorizar saída", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desconto", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promoção", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não calcula redução automática", presented.ActionGuidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Idle_contexto_sem_reducao()
    {
        var presented = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.MonitorOnly,
            PrimaryReason = InventoryCommercialScenarioReason.Idle,
            Thesis = InventoryCommercialScenarioThesis.Idle,
            CurrentCatalogPrice = 10,
            CurrentGrossMarginPercent = 40,
            MinimumGrossMarginPercent = 20,
            MinimumAllowedCatalogPrice = 8.20,
            FinancialRoomAmount = 1.80,
            AttentionQuantitySource = InventoryCommercialAttentionQuantitySource.None,
        });
        Assert.Equal(InventoryCommercialScenarioPresentation.IdleExplanation, presented.Explanation);
        Assert.Equal(InventoryCommercialScenarioPresentation.IdleGuidance, presented.ActionGuidance);
        Assert.True(presented.ShowFinancialAnalysis);
        Assert.Empty(presented.Scenarios);
        Assert.Equal(ProductPriceHelper.MoneyBr(10), presented.CurrentCatalogPriceText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.AttentionQuantityText);
        Assert.Contains("parado", presented.ActionGuidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HighCoverage_MonitorOnly()
    {
        var presented = Present(Monitor(
            InventoryCommercialScenarioReason.HighCoverageMonitoring,
            InventoryCommercialScenarioThesis.HighCoverage));
        Assert.Equal("Acompanhar", presented.StatusLabel);
        Assert.Equal("Cobertura elevada", presented.ThesisLabel);
        Assert.Empty(presented.Scenarios);
        Assert.Contains("acompanhamento", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Limited_mostra_limitacao()
    {
        var presented = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.MonitorOnly,
            PrimaryReason = InventoryCommercialScenarioReason.LimitedConfidence,
            Confidence = InventoryAttentionConfidence.Limited,
            Thesis = InventoryCommercialScenarioThesis.ProjectedExcess30,
            AttentionQuantity = 20,
            AttentionQuantitySource = InventoryCommercialAttentionQuantitySource.ProjectedExcess30,
        });
        Assert.Equal(InventoryCommercialScenarioPresentation.LimitedExplanation, presented.Explanation);
        Assert.Contains(presented.Warnings, w => w.Contains("limitações", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(presented.Scenarios);
        Assert.Equal("Análise com limitações", presented.ConfidenceDisplay);
    }

    [Fact]
    public void Unavailable_sem_zero()
    {
        var presented = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.UnavailableConfidence,
            Confidence = InventoryAttentionConfidence.Unavailable,
            CurrentCatalogPrice = null,
            CurrentGrossMarginPercent = null,
        });
        Assert.Equal("Análise financeira indisponível", presented.StatusLabel);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.CurrentCatalogPriceText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.CurrentGrossMarginText);
        Assert.Empty(presented.Scenarios);
        Assert.NotEqual("R$ 0,00", presented.CurrentCatalogPriceText);
        Assert.NotEqual("0%", presented.CurrentGrossMarginText);
    }

    [Fact]
    public void PolicyMissing()
    {
        var presented = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyMissing,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyMissing,
            MinimumGrossMarginPercent = null,
            CurrentCatalogPrice = 10,
        });
        Assert.Equal(InventoryCommercialScenarioPresentation.PolicyMissingExplanation, presented.Explanation);
        Assert.Equal(InventoryCommercialScenarioPresentation.PolicyMissingGuidance, presented.ActionGuidance);
        Assert.Contains("Sistema → Política comercial", presented.ActionGuidance, StringComparison.Ordinal);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.MinimumGrossMarginText);
        Assert.NotEqual("0%", presented.MinimumGrossMarginText);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void PolicyInvalid()
    {
        var presented = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.PolicyInvalid,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyInvalid,
            MinimumGrossMarginPercent = null,
        });
        Assert.Equal(InventoryCommercialScenarioPresentation.PolicyInvalidExplanation, presented.Explanation);
        Assert.Equal(InventoryCommercialScenarioPresentation.PolicyInvalidGuidance, presented.ActionGuidance);
        Assert.Empty(presented.Scenarios);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.MinimumGrossMarginText);
    }

    [Fact]
    public void UnknownCost()
    {
        var presented = Present(Financial(InventoryCommercialScenarioReason.UnknownCost));
        Assert.Contains("custo", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("insuficiente", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
        Assert.Contains(presented.Warnings, w => w.Contains("custo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidCost()
    {
        var presented = Present(Financial(InventoryCommercialScenarioReason.InvalidCost));
        Assert.Contains("inválido", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void UnusablePrice()
    {
        var presented = Present(Financial(InventoryCommercialScenarioReason.UnusablePrice));
        Assert.Contains("catálogo", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void InvalidPrice()
    {
        var presented = Present(Financial(InventoryCommercialScenarioReason.InvalidPrice));
        Assert.Contains("inválido", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void Kit_sem_cenario()
    {
        var presented = Present(Financial(InventoryCommercialScenarioReason.CompositionProduct));
        Assert.Contains("composto", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BOM", presented.Explanation, StringComparison.Ordinal);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void Cigarro_ambiguo()
    {
        var presented = Present(Financial(InventoryCommercialScenarioReason.AmbiguousSaleUnit));
        Assert.Contains("ambígua", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não divide", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void Nao_vendavel()
    {
        var presented = Present(Financial(InventoryCommercialScenarioReason.NotSellable));
        Assert.Contains("não vendável", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void LocationLimitation()
    {
        var presented = Present(Review(InventoryCommercialScenarioReason.LocationLimitation));
        Assert.Equal("Revisar dados", presented.StatusLabel);
        Assert.False(presented.ShowFinancialAnalysis);
        Assert.Empty(presented.Scenarios);
        Assert.Contains("localização", presented.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InsufficientHistory()
    {
        var presented = Present(Review(InventoryCommercialScenarioReason.InsufficientHistory));
        Assert.Contains("histórico suficiente", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void Price_at_floor()
    {
        var presented = Present(Monitor(
            InventoryCommercialScenarioReason.PriceAtFloor,
            room: 0,
            floor: 10,
            sale: 10));
        Assert.Contains("piso", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("R$ 0,00", presented.FinancialRoomText);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void Price_below_floor()
    {
        var presented = Present(Monitor(InventoryCommercialScenarioReason.PriceBelowFloor, sale: 8, floor: 10));
        Assert.Contains("abaixo do piso", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void No_room()
    {
        var presented = Present(Monitor(InventoryCommercialScenarioReason.NoFinancialRoom, room: 0));
        Assert.Contains("espaço financeiro", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void Rounding_collapse()
    {
        var presented = Present(Monitor(InventoryCommercialScenarioReason.ScenarioCollapsedByRounding));
        Assert.Contains("centavos", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void PrimaryReason_label()
    {
        var presented = Present(Available());
        Assert.Equal(
            InventoryCommercialScenarioPresentation.ThesisExcess30,
            presented.PrimaryReasonLabel);
        Assert.Equal(InventoryCommercialScenarioReason.ProjectedExcess30, presented.PrimaryReason);
    }

    [Fact]
    public void SecondaryReasons_preservados()
    {
        var presented = Present(Available(secondary: [
            InventoryCommercialScenarioReason.Idle,
            InventoryCommercialScenarioReason.HighCoverageMonitoring,
        ]));
        Assert.Equal(2, presented.SecondaryReasonLabels.Count);
        Assert.Equal("Produto parado", presented.SecondaryReasonLabels[0]);
        Assert.Equal("Cobertura elevada", presented.SecondaryReasonLabels[1]);
    }

    [Fact]
    public void Secondary_nao_duplica_primary()
    {
        var presented = Present(Available(secondary: [
            InventoryCommercialScenarioReason.ProjectedExcess30,
            InventoryCommercialScenarioReason.Idle,
        ]));
        Assert.DoesNotContain(presented.PrimaryReasonLabel, presented.SecondaryReasonLabels);
        Assert.Equal(new[] { "Produto parado" }, presented.SecondaryReasonLabels);
    }

    [Fact]
    public void Todos_reasons_tem_label()
    {
        foreach (var reason in Enum.GetValues<InventoryCommercialScenarioReason>())
        {
            var label = InventoryCommercialScenarioPresentation.ReasonLabel(reason);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(reason.ToString(), label);
        }
    }

    [Fact]
    public void Todos_reasons_tem_explanation()
    {
        foreach (var reason in Enum.GetValues<InventoryCommercialScenarioReason>())
        {
            var text = InventoryCommercialScenarioPresentation.ReasonExplanation(reason);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.NotEqual(reason.ToString(), text);
        }
    }

    [Fact]
    public void Zero_moeda_nao_e_traco()
    {
        var presented = Present(Available(room: 0));
        Assert.Equal("R$ 0,00", presented.FinancialRoomText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, presented.FinancialRoomText);
    }

    [Fact]
    public void Zero_percentual_nao_e_traco()
    {
        var presented = Present(Available(minMargin: 0, margin: 0));
        Assert.Equal("0%", presented.MinimumGrossMarginText);
        Assert.Equal("0%", presented.CurrentGrossMarginText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, presented.MinimumGrossMarginText);
    }

    [Fact]
    public void Null_e_traco()
    {
        var presented = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.UnknownCost,
        });
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.CurrentCatalogPriceText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.CurrentGrossMarginText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.MinimumGrossMarginText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.FloorPriceText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.FinancialRoomText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.ThesisLabel);
        Assert.Equal(InventoryProjectionPresentation.EmDash, presented.AttentionQuantityText);
    }

    [Fact]
    public void Margem_negativa_aparece()
    {
        var presented = Present(Available(margin: -10));
        Assert.Equal("-10%", presented.CurrentGrossMarginText);
        Assert.StartsWith("-", presented.CurrentGrossMarginText);
    }

    [Fact]
    public void Sem_preco_recomendado()
    {
        AssertNoForbidden(Present(Available()));
        foreach (var reason in Enum.GetValues<InventoryCommercialScenarioReason>())
            AssertNoForbiddenBlob(
                InventoryCommercialScenarioPresentation.ReasonLabel(reason) + " " +
                InventoryCommercialScenarioPresentation.ReasonExplanation(reason));
    }

    [Fact]
    public void Sem_desconto_recomendado_nem_lucro_garantido()
    {
        var presented = Present(Available());
        AssertNoForbidden(presented);
        Assert.DoesNotContain("Desconto recomendado", presented.Scenarios[0].Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Redução na simulação", InventoryCommercialScenarioPresentation.ReductionCaption);
        Assert.Equal("Margem bruta no cenário", InventoryCommercialScenarioPresentation.ScenarioMarginCaption);
    }

    [Fact]
    public void Texto_nao_altera_precos()
    {
        var presented = Present(Available());
        Assert.Contains(
            "não altera preços automaticamente",
            presented.SimulationDisclaimer,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Orientacao_final()
    {
        var presented = Present(Available());
        Assert.Equal(
            "Analise o cenário antes de alterar qualquer preço.",
            presented.OperatorFooter);
        Assert.Equal(presented.OperatorFooter, presented.ActionGuidance);
    }

    [Fact]
    public void Determinismo()
    {
        var result = Available(secondary: [InventoryCommercialScenarioReason.Idle]);
        var a = Present(result);
        var b = Present(result);
        Assert.Equal(a.StatusLabel, b.StatusLabel);
        Assert.Equal(a.Explanation, b.Explanation);
        Assert.Equal(a.Scenarios[0].SimulatedPriceText, b.Scenarios[0].SimulatedPriceText);
        Assert.Equal(a.SecondaryReasonLabels, b.SecondaryReasonLabels);
        Assert.Equal(a.AttentionQuantityText, b.AttentionQuantityText);
    }

    [Fact]
    public void Pureza_sem_IO_nem_XAML()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Models", "InventoryCommercialScenarioPresentation.cs"));
        Assert.DoesNotContain("DatabaseService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppSettingsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarginSettingsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreNetwork", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentCulture", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".xaml", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InventoryIntelligenceWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionDetailWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaleFromCostAndMargin", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Evaluate(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Nao_recalcula_reducao()
    {
        var presented = Present(WithScenarios(Available(), new InventoryCommercialScenario
        {
            Kind = InventoryCommercialScenarioKind.Light,
            SimulatedCatalogPrice = 9.40,
            ReductionAmount = 0.77,
            ReductionPercent = 12.5,
            GrossMarginPercent = 33.3,
        }));
        Assert.Equal(ProductPriceHelper.MoneyBr(0.77), presented.Scenarios[0].ReductionAmountText);
        Assert.Equal("12,5%", presented.Scenarios[0].ReductionPercentText);
        Assert.Equal("33,3%", presented.Scenarios[0].GrossMarginText);
    }

    [Fact]
    public void FromRow_usa_ProductId_70C()
    {
        var presented = InventoryCommercialScenarioPresentation.FromRow(new InventoryCommercialScenarioRow
        {
            ProductId = 42,
            ScenarioResult = Available(),
        });
        Assert.Equal(42, presented.ProductId);
        Assert.True(presented.IsScenarioAvailable);
    }

    [Fact]
    public void Apply_preserva_ordem()
    {
        var snapshot = InventoryCommercialScenarioPresentation.Apply(new InventoryCommercialScenarioSnapshot
        {
            QueryCount = 9,
            Rows =
            [
                new() { ProductId = 10, ScenarioResult = Available() },
                new() { ProductId = 2, ScenarioResult = Monitor(InventoryCommercialScenarioReason.Idle) },
            ],
        });
        Assert.Equal(9, snapshot.QueryCount);
        Assert.Equal(new[] { 10, 2 }, snapshot.Rows.Select(r => r.ProductId));
    }

    [Fact]
    public void NoRecommendation_neutro()
    {
        var presented = Present(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.NoRecommendation,
            PrimaryReason = InventoryCommercialScenarioReason.NoRecommendation,
        });
        Assert.Equal(InventoryCommercialScenarioPresentation.NoRecommendationExplanation, presented.Explanation);
        Assert.DoesNotContain("estoque está bom", presented.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(presented.Scenarios);
    }

    [Fact]
    public void ReviewData_oculta_analise_financeira()
    {
        var presented = Present(Review(InventoryCommercialScenarioReason.InvalidInput, sale: 10));
        Assert.False(presented.ShowFinancialAnalysis);
        Assert.False(presented.IsScenarioAvailable);
        Assert.Empty(presented.Scenarios);
    }

    static InventoryCommercialScenarioPresentationRow Present(InventoryCommercialScenarioResult result) =>
        InventoryCommercialScenarioPresentation.FromResult(result);

    static InventoryCommercialScenarioResult Available(
        InventoryCommercialScenarioThesis thesis = InventoryCommercialScenarioThesis.ProjectedExcess30,
        InventoryCommercialScenarioReason reason = InventoryCommercialScenarioReason.ProjectedExcess30,
        double? qty = 8,
        InventoryCommercialAttentionQuantitySource source =
            InventoryCommercialAttentionQuantitySource.ProjectedExcess30,
        double? margin = 40,
        double? minMargin = 20,
        double? room = 1.80,
        IReadOnlyList<InventoryCommercialScenarioReason>? secondary = null) =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.Available,
            PrimaryReason = reason,
            SecondaryReasons = secondary ?? [],
            Thesis = thesis,
            Confidence = InventoryAttentionConfidence.Reliable,
            CurrentCatalogPrice = 10,
            CurrentGrossMarginPercent = margin,
            MinimumGrossMarginPercent = minMargin,
            MinimumAllowedCatalogPrice = 8.20,
            FinancialRoomAmount = room,
            CatalogPriceIsAboveMinimumAllowed = true,
            AttentionQuantity = qty,
            AttentionQuantitySource = source,
            Scenarios = [Light(), Moderate()],
        };

    static InventoryCommercialScenarioResult Monitor(
        InventoryCommercialScenarioReason reason,
        InventoryCommercialScenarioThesis thesis = InventoryCommercialScenarioThesis.None,
        double? room = 1.80,
        double? floor = 8.20,
        double? sale = 10) =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.MonitorOnly,
            PrimaryReason = reason,
            Thesis = thesis,
            CurrentCatalogPrice = sale,
            MinimumAllowedCatalogPrice = floor,
            FinancialRoomAmount = room,
            MinimumGrossMarginPercent = 20,
            CurrentGrossMarginPercent = 40,
        };

    static InventoryCommercialScenarioResult Financial(InventoryCommercialScenarioReason reason) =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = reason,
            Confidence = InventoryAttentionConfidence.Unavailable,
        };

    static InventoryCommercialScenarioResult Review(
        InventoryCommercialScenarioReason reason,
        double? sale = null) =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.ReviewData,
            PrimaryReason = reason,
            CurrentCatalogPrice = sale,
        };

    static InventoryCommercialScenario Light() =>
        new()
        {
            Kind = InventoryCommercialScenarioKind.Light,
            SimulatedCatalogPrice = 9.40,
            ReductionAmount = 0.60,
            ReductionPercent = 6,
            GrossMarginPercent = 36.17,
        };

    static InventoryCommercialScenario Moderate() =>
        new()
        {
            Kind = InventoryCommercialScenarioKind.Moderate,
            SimulatedCatalogPrice = 8.80,
            ReductionAmount = 1.20,
            ReductionPercent = 12,
            GrossMarginPercent = 31.82,
        };

    static InventoryCommercialScenarioResult WithScenarios(
        InventoryCommercialScenarioResult result,
        params InventoryCommercialScenario[] scenarios) =>
        new()
        {
            ProductId = result.ProductId,
            Status = result.Status,
            PrimaryReason = result.PrimaryReason,
            SecondaryReasons = result.SecondaryReasons,
            Thesis = result.Thesis,
            Confidence = result.Confidence,
            CurrentCatalogPrice = result.CurrentCatalogPrice,
            CurrentGrossMarginPercent = result.CurrentGrossMarginPercent,
            MinimumGrossMarginPercent = result.MinimumGrossMarginPercent,
            MinimumAllowedCatalogPrice = result.MinimumAllowedCatalogPrice,
            FinancialRoomAmount = result.FinancialRoomAmount,
            CatalogPriceIsAboveMinimumAllowed = result.CatalogPriceIsAboveMinimumAllowed,
            AttentionQuantity = result.AttentionQuantity,
            AttentionQuantitySource = result.AttentionQuantitySource,
            Scenarios = scenarios,
        };

    static void AssertNoForbidden(InventoryCommercialScenarioPresentationRow presented) =>
        AssertNoForbiddenBlob(string.Join(' ',
            presented.StatusLabel,
            presented.ThesisLabel,
            presented.PrimaryReasonLabel,
            presented.Explanation,
            presented.ActionGuidance,
            presented.SimulationDisclaimer,
            presented.OperatorFooter,
            presented.FloorExplanation,
            presented.AttentionQuantityLabel,
            string.Join(' ', presented.SecondaryReasonLabels),
            string.Join(' ', presented.Warnings),
            string.Join(' ', presented.Scenarios.Select(s =>
                s.KindLabel + " " + s.Explanation))));

    static void AssertNoForbiddenBlob(string blob)
    {
        foreach (var phrase in Forbidden)
            Assert.DoesNotContain(phrase, blob, StringComparison.OrdinalIgnoreCase);
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
