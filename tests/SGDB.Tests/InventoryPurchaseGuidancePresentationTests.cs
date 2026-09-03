using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70G-B3 — presentation PT-BR da orientação de reposição.
/// Sem XAML, SQL, quantidade sugerida, fornecedor ou recálculo B1/B2.
/// </summary>
public class InventoryPurchaseGuidancePresentationTests
{
    static readonly string[] Forbidden =
    [
        "compre agora",
        "você precisa comprar",
        "voce precisa comprar",
        "faça o pedido",
        "faca o pedido",
        "fornecedor recomendado",
        "melhor fornecedor",
        "estoque ideal",
        "quantidade ideal",
        "compra garantida",
        "evitará ruptura",
        "evitara ruptura",
        "garantido",
        "certeza",
        "produto não vende",
        "produto nao vende",
    ];

    #region Action labels

    [Fact]
    public void Action_ConsiderReplenishment() =>
        Assert.Equal(
            "Considerar reposição",
            InventoryPurchaseGuidancePresentation.ActionLabel(
                InventoryPurchaseGuidanceAction.ConsiderReplenishment));

    [Fact]
    public void Action_DoNotReplenishNow() =>
        Assert.Equal(
            "Não repor agora",
            InventoryPurchaseGuidancePresentation.ActionLabel(
                InventoryPurchaseGuidanceAction.DoNotReplenishNow));

    [Fact]
    public void Action_Monitor() =>
        Assert.Equal(
            "Acompanhar",
            InventoryPurchaseGuidancePresentation.ActionLabel(InventoryPurchaseGuidanceAction.Monitor));

    [Fact]
    public void Action_ReviewData() =>
        Assert.Equal(
            "Revisar dados",
            InventoryPurchaseGuidancePresentation.ActionLabel(InventoryPurchaseGuidanceAction.ReviewData));

    [Fact]
    public void Action_None() =>
        Assert.Equal(
            "Não aplicável",
            InventoryPurchaseGuidancePresentation.ActionLabel(InventoryPurchaseGuidanceAction.None));

    #endregion

    #region Confidence

    [Fact]
    public void Confidence_Reliable_reusa_70E() =>
        Assert.Equal(
            InventoryAttentionPresentation.ConfidenceReliable,
            InventoryAttentionPresentation.ConfidenceLabel(InventoryAttentionConfidence.Reliable));

    [Fact]
    public void Confidence_Limited_reusa_70E() =>
        Assert.Equal(
            InventoryAttentionPresentation.ConfidenceLimited,
            InventoryAttentionPresentation.ConfidenceLabel(InventoryAttentionConfidence.Limited));

    [Fact]
    public void Confidence_Unavailable_reusa_70E() =>
        Assert.Equal(
            InventoryAttentionPresentation.ConfidenceUnavailable,
            InventoryAttentionPresentation.ConfidenceLabel(InventoryAttentionConfidence.Unavailable));

    [Fact]
    public void Confidence_unknown_fallback() =>
        Assert.Equal(
            "Confiança não classificada",
            InventoryAttentionPresentation.ConfidenceLabel((InventoryAttentionConfidence)99));

    [Fact]
    public void Confidence_nao_promete_certeza()
    {
        var label = InventoryAttentionPresentation.ConfidenceLabel(InventoryAttentionConfidence.Reliable);
        Assert.NotEqual("Confiável", label);
        Assert.DoesNotContain("garantido", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certeza", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("100%", label, StringComparison.Ordinal);
    }

    #endregion

    #region Reason labels

    [Theory]
    [InlineData(InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand, "Sem estoque com giro observado")]
    [InlineData(InventoryPurchaseGuidanceReason.CriticalCoverage, "Cobertura crítica")]
    [InlineData(InventoryPurchaseGuidanceReason.LowCoverage, "Cobertura baixa")]
    [InlineData(InventoryPurchaseGuidanceReason.ProjectedExcess30, "Excesso projetado")]
    [InlineData(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus, "Sobra projetada antes da validade")]
    [InlineData(InventoryPurchaseGuidanceReason.IdleStock, "Estoque parado")]
    [InlineData(InventoryPurchaseGuidanceReason.NoObservableDemand, "Sem giro observado no período")]
    [InlineData(InventoryPurchaseGuidanceReason.InsufficientHistory, "Histórico ainda insuficiente")]
    [InlineData(InventoryPurchaseGuidanceReason.NoPhysicalEvidence, "Sem evidência física suficiente")]
    [InlineData(InventoryPurchaseGuidanceReason.StructuralDataIssue, "Inconsistência nos dados")]
    [InlineData(InventoryPurchaseGuidanceReason.LocationLimitation, "Limitação na leitura por local")]
    [InlineData(InventoryPurchaseGuidanceReason.CompositionProduct, "Produto composto")]
    [InlineData(InventoryPurchaseGuidanceReason.Expired, "Produto vencido")]
    [InlineData(InventoryPurchaseGuidanceReason.ExpiresToday, "Vence hoje")]
    [InlineData(InventoryPurchaseGuidanceReason.None, "Situação de acompanhamento")]
    public void Reason_label(InventoryPurchaseGuidanceReason reason, string expected) =>
        Assert.Equal(expected, InventoryPurchaseGuidancePresentation.ReasonLabel(reason));

    #endregion

    #region Explanations

    [Fact]
    public void Explicacao_zero_giro()
    {
        var row = Present(Consider(InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand));
        Assert.Contains("sem estoque", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("giro", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("considerar a reposição", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vai faltar", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(InventoryPurchaseGuidancePresentation.ConsiderLimitationNote, row.DetailExplanation);
    }

    [Fact]
    public void Explicacao_Critical()
    {
        var row = Present(Consider(InventoryPurchaseGuidanceReason.CriticalCoverage));
        Assert.Contains("poucos dias", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("considerar a reposição", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_Low()
    {
        var row = Present(Consider(InventoryPurchaseGuidanceReason.LowCoverage));
        Assert.Contains("baixa", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("considerar a reposição", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_Excess()
    {
        var row = Present(DoNot(InventoryPurchaseGuidanceReason.ProjectedExcess30));
        Assert.Contains("não se justifica agora", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nunca", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_ExpirySurplus()
    {
        var row = Present(DoNot(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus));
        Assert.Contains("validade", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promova", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.ValidityExpiryRisk, row.ValidityLabel);
    }

    [Fact]
    public void Explicacao_Idle()
    {
        var row = Present(DoNot(InventoryPurchaseGuidanceReason.IdleStock));
        Assert.Contains("sem giro", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("não vende", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_Expired()
    {
        var row = Present(DoNot(InventoryPurchaseGuidanceReason.Expired));
        Assert.Contains("vencido", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InventoryPurchaseGuidancePresentation.ValidityExpired, row.ValidityLabel);
        Assert.DoesNotContain("retirar", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_ExpiresToday()
    {
        var row = Present(DoNot(InventoryPurchaseGuidanceReason.ExpiresToday));
        Assert.Contains("hoje", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InventoryPurchaseGuidancePresentation.ValidityExpiresToday, row.ValidityLabel);
    }

    [Fact]
    public void Explicacao_VMV0()
    {
        var row = Present(Monitor(InventoryPurchaseGuidanceReason.NoObservableDemand));
        Assert.Contains("Acompanhe", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("não vende", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_InsufficientHistory()
    {
        var row = Present(Monitor(InventoryPurchaseGuidanceReason.InsufficientHistory));
        Assert.Contains("insuficiente", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("acompanhando", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_NoEvidence()
    {
        var row = Present(Review(InventoryPurchaseGuidanceReason.NoPhysicalEvidence));
        Assert.Contains("evidência física", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Revise os dados", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_Structural()
    {
        var row = Present(Review(InventoryPurchaseGuidanceReason.StructuralDataIssue));
        Assert.Contains("inconsistências", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("corrija o estoque", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_Location()
    {
        var row = Present(Monitor(InventoryPurchaseGuidanceReason.LocationLimitation));
        Assert.Contains("locais de estoque", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Acompanhe", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicacao_Composition()
    {
        var row = Present(None(InventoryPurchaseGuidanceReason.CompositionProduct));
        Assert.Contains("componentes", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.True(row.IsNotApplicable);
        Assert.Equal("Não aplicável", row.ActionLabel);
    }

    [Fact]
    public void Explicacao_Attention_None()
    {
        var row = Present(new InventoryPurchaseGuidanceResult
        {
            Action = InventoryPurchaseGuidanceAction.Monitor,
            Status = InventoryPurchaseGuidanceStatus.Monitor,
            Confidence = InventoryAttentionConfidence.Limited,
            PrimaryReason = InventoryPurchaseGuidanceReason.None,
        });
        Assert.Contains("acompanhamento", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não justificam uma reposição", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Situação de acompanhamento", row.PrimaryReasonLabel);
        Assert.DoesNotContain("None", row.PrimaryReasonLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicacao_Normal_None()
    {
        var row = Present(new InventoryPurchaseGuidanceResult
        {
            Action = InventoryPurchaseGuidanceAction.Monitor,
            Status = InventoryPurchaseGuidanceStatus.Monitor,
            Confidence = InventoryAttentionConfidence.Reliable,
            PrimaryReason = InventoryPurchaseGuidanceReason.None,
        });
        Assert.Contains("sem indicação atual de reposição", row.ShortExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("None", row.PrimaryReasonLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("Desconhecido", row.PrimaryReasonLabel, StringComparison.Ordinal);
    }

    #endregion

    #region Forbidden language

    [Fact]
    public void Linguagem_proibida_ausente()
    {
        foreach (var text in AllPresentationTexts())
        {
            var lower = text.ToLowerInvariant();
            foreach (var phrase in Forbidden)
                Assert.DoesNotContain(phrase, lower, StringComparison.Ordinal);

            Assert.False(
                Regex.IsMatch(lower, @"\bcompre\b"),
                $"Texto contém 'compre': {text}");
        }
    }

    [Fact]
    public void Titulos_oficiais()
    {
        Assert.Equal("Reposição Inteligente", InventoryPurchaseGuidancePresentation.ModuleTitle);
        Assert.Equal("Reposição", InventoryPurchaseGuidancePresentation.ToolbarTitle);
        Assert.DoesNotContain(
            "Compra Automática",
            InventoryPurchaseGuidancePresentation.ModuleTitle,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cards_usam_labels_canonicos()
    {
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.ActionConsiderReplenishment,
            InventoryPurchaseGuidancePresentation.CardConsiderReplenishment);
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.ActionDoNotReplenishNow,
            InventoryPurchaseGuidancePresentation.CardDoNotReplenishNow);
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.ActionMonitor,
            InventoryPurchaseGuidancePresentation.CardMonitor);
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.ActionReviewData,
            InventoryPurchaseGuidancePresentation.CardReviewData);
    }

    #endregion

    #region Numeric

    [Fact]
    public void Estoque_zero_aparece()
    {
        var row = Present(
            Monitor(InventoryPurchaseGuidanceReason.None),
            Turnover(stock: 0, vmv: 0, coverage: null));
        Assert.Equal("0", row.TotalStockDisplay);
    }

    [Fact]
    public void Estoque_decimal()
    {
        var row = Present(
            Monitor(InventoryPurchaseGuidanceReason.None),
            Turnover(stock: 1.5, vmv: 1, coverage: 1.5));
        Assert.Equal(InventoryIntelligencePresentation.FormatQty(1.5), row.TotalStockDisplay);
        Assert.DoesNotContain("NaN", row.TotalStockDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VMV_decimal_ptBR()
    {
        var row = Present(
            Monitor(InventoryPurchaseGuidanceReason.None),
            Turnover(stock: 10, vmv: 2.3, coverage: 4.3));
        Assert.Equal(InventoryIntelligencePresentation.FormatVmv30(2.3), row.Vmv30Display);
        Assert.StartsWith("Giro médio:", row.Vmv30Text, StringComparison.Ordinal);
        Assert.Contains("un./dia", row.Vmv30Text, StringComparison.Ordinal);
        Assert.Contains(",", row.Vmv30Display, StringComparison.Ordinal);
    }

    [Fact]
    public void Cobertura_decimal()
    {
        var row = Present(
            Monitor(InventoryPurchaseGuidanceReason.None),
            Turnover(stock: 10, vmv: 1, coverage: 1.8));
        Assert.Equal("1,8 dias", row.CoverageDisplay);
    }

    [Fact]
    public void Cobertura_nao_calculavel()
    {
        var row = Present(
            Monitor(InventoryPurchaseGuidanceReason.None),
            Turnover(stock: 10, vmv: 0, coverage: null));
        Assert.Equal("Não calculável", row.CoverageDisplay);
        Assert.DoesNotContain("Infinity", row.CoverageDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Excess_factual_nao_e_quantidade_de_compra()
    {
        var row = Present(
            DoNot(InventoryPurchaseGuidanceReason.ProjectedExcess30),
            Turnover(stock: 80, vmv: 1, coverage: 80),
            Projected(excess: 24));
        Assert.Equal("Excesso projetado: 24 un.", row.ExcessQuantityText);
        Assert.DoesNotContain("comprar", row.ExcessQuantityText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pedido", row.ExcessQuantityText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expiry_surplus_factual()
    {
        var row = Present(
            DoNot(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus),
            Turnover(stock: 20, vmv: 1, coverage: 20),
            Projected(surplus: 8));
        Assert.Equal("Sobra projetada até a validade: 8 un.", row.ExpirySurplusText);
        Assert.DoesNotContain("promova", row.ExpirySurplusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("venda 8", row.ExpirySurplusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NaN_nao_aparece()
    {
        var row = Present(
            Review(InventoryPurchaseGuidanceReason.StructuralDataIssue),
            Turnover(stock: double.NaN, vmv: double.NaN, coverage: double.NaN));
        Assert.DoesNotContain("NaN", AllRowText(row), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InventoryPurchaseGuidancePresentation.EmDash, row.TotalStockDisplay);
        Assert.Equal("Não calculável", row.CoverageDisplay);
    }

    [Fact]
    public void Infinity_nao_aparece()
    {
        var row = Present(
            Review(InventoryPurchaseGuidanceReason.StructuralDataIssue),
            Turnover(stock: 1, vmv: 1, coverage: double.PositiveInfinity));
        Assert.DoesNotContain("Infinity", AllRowText(row), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Não calculável", row.CoverageDisplay);
    }

    #endregion

    #region Primary / secondary

    [Fact]
    public void Primary_label()
    {
        var row = Present(DoNot(
            InventoryPurchaseGuidanceReason.ProjectedExcess30,
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus));
        Assert.Equal("Excesso projetado", row.PrimaryReasonLabel);
    }

    [Fact]
    public void Secondary_labels()
    {
        var row = Present(DoNot(
            InventoryPurchaseGuidanceReason.ProjectedExcess30,
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus,
            InventoryPurchaseGuidanceReason.LocationLimitation));
        Assert.Equal(
            new[] { "Sobra projetada antes da validade", "Limitação na leitura por local" },
            row.SecondaryReasonLabels);
    }

    [Fact]
    public void Primary_nao_duplicado_em_secondary()
    {
        var row = Present(DoNot(
            InventoryPurchaseGuidanceReason.ProjectedExcess30,
            InventoryPurchaseGuidanceReason.ProjectedExcess30,
            InventoryPurchaseGuidanceReason.IdleStock));
        Assert.DoesNotContain("Excesso projetado", row.SecondaryReasonLabels);
        Assert.Equal(new[] { "Estoque parado" }, row.SecondaryReasonLabels);
    }

    [Fact]
    public void Secondary_ordem_deterministica()
    {
        var result = DoNot(
            InventoryPurchaseGuidanceReason.ProjectedExcess30,
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus,
            InventoryPurchaseGuidanceReason.LocationLimitation);
        var a = Present(result);
        var b = Present(result);
        Assert.Equal(a.SecondaryReasonLabels, b.SecondaryReasonLabels);
    }

    [Fact]
    public void None_nao_aparece_literalmente()
    {
        var row = Present(Monitor(InventoryPurchaseGuidanceReason.None));
        Assert.DoesNotContain("None", row.PrimaryReasonLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("Motivo: 0", AllRowText(row), StringComparison.Ordinal);
        Assert.DoesNotContain("Desconhecido", row.PrimaryReasonLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Secondary_vazio()
    {
        var row = Present(Monitor(InventoryPurchaseGuidanceReason.None));
        Assert.Empty(row.SecondaryReasonLabels);
    }

    #endregion

    #region Purity

    [Fact]
    public void ExpectedQueryCount_e_zero() =>
        Assert.Equal(0, InventoryPurchaseGuidancePresentation.ExpectedQueryCount);

    [Fact]
    public void Pipeline_permanece_9() =>
        Assert.Equal(9, InventoryPurchaseGuidancePresentation.ExpectedPipelineQueryCount);

    [Fact]
    public void Sem_SQL_SQLite_Purchase_WPF()
    {
        var text = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidancePresentation.cs");
        Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT ", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PurchaseService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SupplierService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Random", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MinStock", text, StringComparison.Ordinal);
        Assert.DoesNotContain("min_stock", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PromotionSuggestion", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SuggestedQuantity", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchaseScore", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GrossMargin", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_sem_quantidade_fornecedor_score()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidancePresentationRow),
            "SuggestedQuantity", "TargetQuantity", "OrderQuantity", "SupplierId",
            "SupplierName", "PurchaseScore", "MinStock", "Margin");
        AssertNoMember(typeof(InventoryPurchaseGuidancePresentationSnapshot),
            "SuggestedQuantity", "SupplierId", "PurchaseScore");
    }

    [Fact]
    public void Sem_prioridade_de_compra()
    {
        var text = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidancePresentation.cs");
        Assert.DoesNotContain("PriorityHigh", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchasePriority", text, StringComparison.Ordinal);
        AssertNoMember(typeof(InventoryPurchaseGuidancePresentationRow), "Priority", "PriorityLabel");
    }

    #endregion

    #region Fallback / culture / snapshot

    [Fact]
    public void Unknown_Action_fallback()
    {
        var row = Present(new InventoryPurchaseGuidanceResult
        {
            Action = (InventoryPurchaseGuidanceAction)99,
            PrimaryReason = InventoryPurchaseGuidanceReason.StructuralDataIssue,
        });
        Assert.Equal(InventoryPurchaseGuidancePresentation.SituationUnavailable, row.ActionLabel);
    }

    [Fact]
    public void Unknown_Reason_fallback()
    {
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.SituationUnavailable,
            InventoryPurchaseGuidancePresentation.ReasonLabel((InventoryPurchaseGuidanceReason)99));
        var row = Present(new InventoryPurchaseGuidanceResult
        {
            Action = InventoryPurchaseGuidanceAction.ReviewData,
            Status = InventoryPurchaseGuidanceStatus.ReviewData,
            PrimaryReason = (InventoryPurchaseGuidanceReason)99,
        });
        Assert.Equal(InventoryPurchaseGuidancePresentation.SituationUnavailable, row.PrimaryReasonLabel);
        Assert.Equal(InventoryPurchaseGuidancePresentation.SituationUnavailable, row.ShortExplanation);
    }

    [Fact]
    public void Null_result_e_seguro()
    {
        var row = InventoryPurchaseGuidancePresentation.FromResult(null);
        Assert.True(row.IsJoinMissing);
        Assert.True(row.IsReviewData);
        Assert.Equal("Revisar dados", row.ActionLabel);
    }

    [Fact]
    public void Determinismo_cultural()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var row = Present(
                Monitor(InventoryPurchaseGuidanceReason.None),
                Turnover(stock: 1.8, vmv: 2.3, coverage: 1.8));
            Assert.Equal("1,8 dias", row.CoverageDisplay);
            Assert.Contains(",", row.Vmv30Display, StringComparison.Ordinal);
            Assert.DoesNotContain(".", row.CoverageDisplay, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Apply_preserva_ordem_e_lookup()
    {
        var snapshot = InventoryPurchaseGuidancePresentation.Apply(
            new InventoryPurchaseGuidanceSnapshot
            {
                QueryCount = 0,
                Results =
                [
                    Consider(InventoryPurchaseGuidanceReason.LowCoverage, productId: 10),
                    Monitor(InventoryPurchaseGuidanceReason.None, productId: 2),
                ],
            });
        Assert.Equal(0, snapshot.QueryCount);
        Assert.Equal(new[] { 10, 2 }, snapshot.Rows.Select(r => r.ProductId));
        Assert.True(snapshot.ByProductId[10].IsConsiderReplenishment);
        Assert.True(snapshot.ByProductId[2].IsMonitor);
        Assert.Equal(
            snapshot.ByProductId[10],
            InventoryPurchaseGuidancePresentation.ResolveForDetail(snapshot, 10));
    }

    [Fact]
    public void Apply_null_produz_vazio()
    {
        var snapshot = InventoryPurchaseGuidancePresentation.Apply(null);
        Assert.Empty(snapshot.Rows);
    }

    [Fact]
    public void ResolveForDetail_ausente_e_ReviewData()
    {
        var missing = InventoryPurchaseGuidancePresentation.ResolveForDetail(null, 7);
        Assert.Equal(7, missing.ProductId);
        Assert.True(missing.IsJoinMissing);
        Assert.True(missing.IsReviewData);
    }

    [Fact]
    public void Disclaimer_geral()
    {
        var row = Present(Monitor(InventoryPurchaseGuidanceReason.None));
        Assert.Equal(InventoryPurchaseGuidancePresentation.GuidanceDisclaimer, row.DisclaimerText);
        Assert.Contains("apoio à decisão", row.DisclaimerText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não cria pedidos", row.DisclaimerText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_nao_gera_coluna_redundante()
    {
        AssertNoMember(typeof(InventoryPurchaseGuidancePresentationRow), "StatusLabel");
        var row = Present(Monitor(InventoryPurchaseGuidanceReason.None));
        Assert.Equal("Acompanhar", row.ActionLabel);
        Assert.Equal(InventoryPurchaseGuidanceStatus.Monitor, row.Status);
    }

    [Fact]
    public void Engine_continua_autoridade_da_action()
    {
        var engine = InventoryPurchaseGuidanceEngine.Evaluate(new InventoryPurchaseGuidanceInput
        {
            ProductId = 1,
            Stock = 5,
            TotalStock = 5,
            Vmv30 = 1,
            CoverageBand = InventoryCoverageBand.Low,
            CoverageDays = 5,
            HasPhysicalAvailabilityEvidence = true,
            HistoryDays = 120,
            CanProjectSku = true,
            ProjectedExcessQuantity = 0,
            ProjectedExpirySurplus = 0,
        });
        var row = Present(engine);
        Assert.Equal(engine.Action, row.Action);
        Assert.Equal("Considerar reposição", row.ActionLabel);
        Assert.Equal("Cobertura baixa", row.PrimaryReasonLabel);
    }

    [Fact]
    public void Short_cabe_em_grid()
    {
        foreach (var reason in Enum.GetValues<InventoryPurchaseGuidanceReason>())
        {
            var text = InventoryPurchaseGuidancePresentation.ShortExplanation(reason);
            Assert.InRange(text.Length, 1, 220);
        }
    }

    [Fact]
    public void Detail_de_Consider_explica_limitacao()
    {
        var detail = InventoryPurchaseGuidancePresentation.DetailExplanation(
            InventoryPurchaseGuidanceReason.CriticalCoverage,
            InventoryPurchaseGuidanceAction.ConsiderReplenishment);
        Assert.Contains("Prazo do fornecedor", detail, StringComparison.Ordinal);
        Assert.Contains("trânsito", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quantidade sugerida",
            InventoryPurchaseGuidancePresentation.ShortExplanation(
                InventoryPurchaseGuidanceReason.CriticalCoverage),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_formata_fatos_70C_70D_sem_recalcular()
    {
        var guidance = new InventoryPurchaseGuidanceSnapshot
        {
            Results = [DoNot(InventoryPurchaseGuidanceReason.ProjectedExcess30, productId: 4)],
        };
        var intelligence = new InventoryIntelligenceSnapshot
        {
            Rows =
            [
                new ProductTurnoverRow
                {
                    ProductId = 4,
                    Stock = 80,
                    TotalStock = 80,
                    Vmv30 = 1,
                    CoverageDays = 80,
                    CoverageBand = InventoryCoverageBand.Normal,
                },
            ],
        };
        var projection = new InventoryProjectionSnapshot
        {
            ByProductId = new Dictionary<int, InventoryProjectedProduct>
            {
                [4] = Projected(excess: 24, productId: 4),
            },
        };
        var presented = InventoryPurchaseGuidancePresentation.Apply(guidance, intelligence, projection);
        Assert.Equal("80", presented.Rows[0].TotalStockDisplay);
        Assert.Equal("80 dias", presented.Rows[0].CoverageDisplay);
        Assert.Equal("Excesso projetado: 24 un.", presented.Rows[0].ExcessQuantityText);
    }

    [Fact]
    public void Todos_actions_e_reasons_mapeados()
    {
        foreach (var action in Enum.GetValues<InventoryPurchaseGuidanceAction>())
        {
            var label = InventoryPurchaseGuidancePresentation.ActionLabel(action);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(action.ToString(), label);
        }

        foreach (var reason in Enum.GetValues<InventoryPurchaseGuidanceReason>())
        {
            var label = InventoryPurchaseGuidancePresentation.ReasonLabel(reason);
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(reason.ToString(), label);
        }
    }

    [Fact]
    public void Nenhum_ToString_de_enum()
    {
        var source = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidancePresentation.cs");
        Assert.DoesNotContain(".ToString()", source, StringComparison.Ordinal);
    }

    #endregion

    static InventoryPurchaseGuidancePresentationRow Present(
        InventoryPurchaseGuidanceResult result,
        ProductTurnoverRow? turnover = null,
        InventoryProjectedProduct? projected = null) =>
        InventoryPurchaseGuidancePresentation.FromResult(result, turnover, projected);

    static InventoryPurchaseGuidanceResult Consider(
        InventoryPurchaseGuidanceReason primary,
        int productId = 1) =>
        new()
        {
            ProductId = productId,
            Status = InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            Action = InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            Confidence = InventoryAttentionConfidence.Limited,
            PrimaryReason = primary,
            SecondaryReasons = [],
        };

    static InventoryPurchaseGuidanceResult DoNot(
        InventoryPurchaseGuidanceReason primary,
        params InventoryPurchaseGuidanceReason[] secondary) =>
        DoNot(primary, 1, secondary);

    static InventoryPurchaseGuidanceResult DoNot(
        InventoryPurchaseGuidanceReason primary,
        int productId,
        params InventoryPurchaseGuidanceReason[] secondary) =>
        new()
        {
            ProductId = productId,
            Status = InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            Action = InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            Confidence = InventoryAttentionConfidence.Reliable,
            PrimaryReason = primary,
            SecondaryReasons = secondary,
        };

    static InventoryPurchaseGuidanceResult Monitor(
        InventoryPurchaseGuidanceReason primary,
        int productId = 1) =>
        new()
        {
            ProductId = productId,
            Status = InventoryPurchaseGuidanceStatus.Monitor,
            Action = InventoryPurchaseGuidanceAction.Monitor,
            Confidence = InventoryAttentionConfidence.Limited,
            PrimaryReason = primary,
            SecondaryReasons = [],
        };

    static InventoryPurchaseGuidanceResult Review(
        InventoryPurchaseGuidanceReason primary,
        int productId = 1) =>
        new()
        {
            ProductId = productId,
            Status = InventoryPurchaseGuidanceStatus.ReviewData,
            Action = InventoryPurchaseGuidanceAction.ReviewData,
            Confidence = InventoryAttentionConfidence.Unavailable,
            PrimaryReason = primary,
            SecondaryReasons = [],
        };

    static InventoryPurchaseGuidanceResult None(
        InventoryPurchaseGuidanceReason primary,
        int productId = 1) =>
        new()
        {
            ProductId = productId,
            Status = InventoryPurchaseGuidanceStatus.NotApplicable,
            Action = InventoryPurchaseGuidanceAction.None,
            Confidence = InventoryAttentionConfidence.Unavailable,
            PrimaryReason = primary,
            SecondaryReasons = [],
        };

    static ProductTurnoverRow Turnover(double stock, double vmv, double? coverage) =>
        new()
        {
            ProductId = 1,
            Stock = stock,
            TotalStock = stock,
            Vmv30 = vmv,
            CoverageDays = coverage,
            CoverageBand = coverage is double ? InventoryCoverageBand.Normal : InventoryCoverageBand.NotCalculable,
        };

    static InventoryProjectedProduct Projected(
        double? excess = null,
        double? surplus = null,
        int productId = 1)
    {
        var lots = surplus is double qty
            ? new[]
            {
                new InventoryProjectionLotResult
                {
                    LotId = 1,
                    Kind = InventoryProjectionLotKind.Dated,
                    Quantity = qty,
                    ProjectedSurplusAtExpiry = qty,
                },
            }
            : Array.Empty<InventoryProjectionLotResult>();

        return new InventoryProjectedProduct
        {
            ProductId = productId,
            Projection = new InventoryProjectionResult
            {
                SkuBlockedReason = InventorySkuProjectionBlockedReason.None,
                ProjectedExcessQuantity = excess,
                Lots = lots,
            },
        };
    }

    static IEnumerable<string> AllPresentationTexts()
    {
        yield return InventoryPurchaseGuidancePresentation.ModuleTitle;
        yield return InventoryPurchaseGuidancePresentation.ToolbarTitle;
        yield return InventoryPurchaseGuidancePresentation.GuidanceDisclaimer;
        yield return InventoryPurchaseGuidancePresentation.ConsiderLimitationNote;
        yield return InventoryPurchaseGuidancePresentation.MissingAnalysis;
        foreach (var action in Enum.GetValues<InventoryPurchaseGuidanceAction>())
            yield return InventoryPurchaseGuidancePresentation.ActionLabel(action);
        foreach (var reason in Enum.GetValues<InventoryPurchaseGuidanceReason>())
        {
            yield return InventoryPurchaseGuidancePresentation.ReasonLabel(reason);
            yield return InventoryPurchaseGuidancePresentation.ShortExplanation(reason);
            yield return InventoryPurchaseGuidancePresentation.DetailExplanation(
                reason, InventoryPurchaseGuidanceAction.ConsiderReplenishment);
            yield return InventoryPurchaseGuidancePresentation.DetailExplanation(
                reason, InventoryPurchaseGuidanceAction.DoNotReplenishNow);
        }

        yield return AllRowText(Present(Consider(InventoryPurchaseGuidanceReason.CriticalCoverage)));
        yield return AllRowText(Present(DoNot(InventoryPurchaseGuidanceReason.ProjectedExcess30)));
        yield return AllRowText(Present(Monitor(InventoryPurchaseGuidanceReason.None)));
        yield return AllRowText(Present(Review(InventoryPurchaseGuidanceReason.StructuralDataIssue)));
        yield return AllRowText(Present(None(InventoryPurchaseGuidanceReason.CompositionProduct)));
    }

    static string AllRowText(InventoryPurchaseGuidancePresentationRow row) =>
        string.Join('\n',
            row.ActionLabel,
            row.ConfidenceLabel,
            row.PrimaryReasonLabel,
            row.ShortExplanation,
            row.DetailExplanation,
            string.Join('|', row.SecondaryReasonLabels),
            row.TotalStockDisplay,
            row.Vmv30Display,
            row.Vmv30Text,
            row.CoverageDisplay,
            row.ExcessQuantityText,
            row.ExpirySurplusText,
            row.ValidityLabel,
            row.ConsiderLimitationNote,
            row.DisclaimerText);

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
}
