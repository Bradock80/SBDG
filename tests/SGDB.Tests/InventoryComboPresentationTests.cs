using System.Globalization;
using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71A-B6 — presentation PT-BR do Combo Inteligente. Sem XAML, SQL, recálculo ou UI.
/// </summary>
public class InventoryComboPresentationTests
{
    static readonly string[] Forbidden =
    [
        "vai vender",
        "garante giro",
        "garante venda",
        "lucro adicional",
        "ganho esperado",
        "desconto garantido",
        "desconto aplicado",
        "economia garantida",
        "promoção ativada",
        "promocao ativada",
        "melhor combo",
        "combinação perfeita",
        "combinacao perfeita",
        "3 melhores combinações",
        "3 melhores combinacoes",
        "preço do combo",
        "preco do combo",
        "produto ruim",
        "encalhado",
        "lucro esperado",
        "ganho extra",
        "lucro previsto",
        "preço mínimo legal",
        "preco minimo legal",
        "preço obrigatório",
        "preco obrigatorio",
        "produto que vende garantido",
        "promoção ativa",
        "promocao ativa",
    ];

    [Fact]
    public void QueryCount_e_zero_e_pipeline_inalterado()
    {
        Assert.Equal(0, InventoryComboPresentation.ExpectedQueryCount);
        Assert.Equal(
            InventoryComboIntelligenceComposer.ExpectedPipelineQueryCount,
            InventoryComboPresentation.ExpectedPipelineQueryCount);
        Assert.Equal(10, InventoryComboPresentation.ExpectedPipelineQueryCount);
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            QueryCount = 9,
        });
        Assert.Equal(9, presented.QueryCount);
        Assert.Equal(0, InventoryComboPresentation.ExpectedQueryCount);
    }

    [Theory]
    [InlineData(ComboTargetEligibilityReason.ExpirySurplus, InventoryComboPresentation.TargetReasonExpirySurplus)]
    [InlineData(ComboTargetEligibilityReason.ProjectedExcess, InventoryComboPresentation.TargetReasonProjectedExcess)]
    [InlineData(ComboTargetEligibilityReason.Idle, InventoryComboPresentation.TargetReasonIdle)]
    public void Target_reason_ptbr(ComboTargetEligibilityReason reason, string expected)
    {
        Assert.Equal(expected, InventoryComboPresentation.TargetReasonText(reason));
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.ReasonProjectedExpirySurplus,
            InventoryComboPresentation.TargetReasonExpirySurplus);
        Assert.Equal(
            InventoryCommercialScenarioPresentation.ThesisIdle,
            InventoryComboPresentation.TargetReasonIdle);
    }

    [Fact]
    public void Anchor_reason_saudavel_sem_promessa()
    {
        Assert.Equal(
            InventoryComboPresentation.AnchorReasonHealthy,
            InventoryComboPresentation.AnchorReasonText(ComboAnchorEligibilityReason.HealthyNormalCoverage));
        Assert.DoesNotContain("garantido", InventoryComboPresentation.AnchorReasonHealthy, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(InventoryComboPairEvidence.Observed, InventoryComboPresentation.EvidenceObserved)]
    [InlineData(InventoryComboPairEvidence.Weak, InventoryComboPresentation.EvidenceWeak)]
    [InlineData(InventoryComboPairEvidence.InsufficientHistory, InventoryComboPresentation.EvidenceInsufficient)]
    public void Evidence_labels(InventoryComboPairEvidence evidence, string expected) =>
        Assert.Equal(expected, InventoryComboPresentation.EvidenceText(evidence));

    [Fact]
    public void Evidence_defensiva_nao_sugerida_tem_fallback()
    {
        Assert.Equal(
            InventoryComboPresentation.EvidenceUnavailable,
            InventoryComboPresentation.EvidenceText(InventoryComboPairEvidence.NoneObserved));
        Assert.Equal(
            InventoryComboPresentation.EvidenceUnavailable,
            InventoryComboPresentation.EvidenceText(InventoryComboPairEvidence.InvalidCounts));
    }

    [Theory]
    [InlineData(0, "0 vendas conjuntas nos últimos 90 dias")]
    [InlineData(1, "1 venda conjunta nos últimos 90 dias")]
    [InlineData(2, "2 vendas conjuntas nos últimos 90 dias")]
    [InlineData(3, "3 vendas conjuntas nos últimos 90 dias")]
    public void Pluralizacao_vendas_conjuntas(int n, string expected) =>
        Assert.Equal(expected, InventoryComboPresentation.JointSalesText(n));

    [Fact]
    public void Observed_detalhe_com_percentual()
    {
        var row = PresentSuggestion(Suggestion(
            evidence: InventoryComboPairEvidence.Observed,
            pairTx: 4,
            targetTx: 10,
            share: 0.4));
        Assert.Equal(InventoryComboPresentation.EvidenceObserved, row.EvidenceText);
        Assert.Contains("4 vendas conjuntas nos últimos 90 dias", row.EvidenceDetailText, StringComparison.Ordinal);
        Assert.Contains("40% das vendas do produto-alvo também incluíram esta âncora", row.EvidenceDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void Weak_uma_venda_sem_plural()
    {
        var row = PresentSuggestion(Suggestion(
            evidence: InventoryComboPairEvidence.Weak,
            pairTx: 1,
            targetTx: 10,
            share: 0.1));
        Assert.Contains("1 venda conjunta nos últimos 90 dias", row.EvidenceDetailText, StringComparison.Ordinal);
        Assert.Contains("10%", row.EvidenceDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void Insufficient_nao_mostra_percentual()
    {
        var row = PresentSuggestion(Suggestion(
            evidence: InventoryComboPairEvidence.InsufficientHistory,
            pairTx: 1,
            targetTx: 3,
            share: 0.33));
        Assert.Equal(InventoryComboPresentation.EvidenceInsufficientDetail, row.EvidenceDetailText);
        Assert.DoesNotContain("%", row.EvidenceDetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("venda conjunta", row.EvidenceDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void Observed_sem_share_nao_inventa_percentual()
    {
        var row = PresentSuggestion(Suggestion(
            evidence: InventoryComboPairEvidence.Observed,
            pairTx: 3,
            targetTx: 8,
            share: null));
        Assert.Equal("3 vendas conjuntas nos últimos 90 dias", row.EvidenceDetailText);
        Assert.DoesNotContain("%", row.EvidenceDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void Precos_atuais_e_piso()
    {
        var row = PresentSuggestion(Suggestion(price: 30, floor: 20, profit: 14, margin: 14d / 30d));
        Assert.Equal(InventoryComboPresentation.CurrentPriceLabel, row.CurrentPriceLabel);
        Assert.Equal("R$ 30,00", row.CurrentPriceText);
        Assert.Equal(InventoryComboPresentation.FloorPriceLabel, row.FloorPriceLabel);
        Assert.Equal("R$ 20,00", row.FloorPriceText);
        Assert.Equal(InventoryComboPresentation.FloorExplanation, row.FloorExplanation);
        Assert.DoesNotContain("legal", row.FloorExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("obrigatório", row.FloorExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("combo", row.CurrentPriceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Um_cenario_precos_atuais()
    {
        var row = PresentSuggestion(Suggestion(price: 30, floor: 20, profit: 14, margin: 14d / 30d));
        var only = Assert.Single(row.Scenarios);
        Assert.Equal(InventoryComboPairFinancialScenarioKind.CurrentPrices, only.Kind);
        Assert.Equal(InventoryComboPresentation.CurrentPriceLabel, only.Title);
        Assert.Equal("R$ 30,00", only.PairPriceText);
        Assert.Equal("R$ 14,00", only.GrossProfitText);
        Assert.Equal("46,67%", only.GrossMarginText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, only.ReductionText);
        Assert.False(row.HasReferenceScenario);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ReferencePriceText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ReductionText);
    }

    [Fact]
    public void Dois_cenarios_referencia_protege_ancora()
    {
        var row = PresentSuggestion(Suggestion(
            price: 30,
            floor: 20,
            profit: 14,
            margin: 14d / 30d,
            referencePrice: 27.5,
            referenceProfit: 11.5,
            referenceMargin: 11.5 / 27.5,
            reduction: 2.5));
        Assert.Equal(2, row.Scenarios.Count);
        Assert.True(row.HasReferenceScenario);
        Assert.Equal(InventoryComboPresentation.ReferencePriceLabel, row.ReferencePriceLabel);
        Assert.Equal("R$ 27,50", row.ReferencePriceText);
        Assert.Equal(InventoryComboPresentation.ReferenceSubtitle, row.ReferenceSubtitle);
        Assert.Contains("produto que precisa girar", row.ReferenceSubtitle, StringComparison.Ordinal);
        Assert.Contains("âncora permanece no preço atual", row.ReferenceSubtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("promoção", row.ReferenceSubtitle, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(InventoryComboPresentation.ReductionLabel, row.ReductionLabel);
        Assert.Equal("R$ 2,50", row.ReductionText);
        Assert.DoesNotContain("desconto aplicado", row.ReductionLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("R$ 14,00", row.GrossProfitText);
        Assert.Equal(InventoryComboPresentation.GrossProfitLabel, row.GrossProfitLabel);
        Assert.Equal(InventoryComboPresentation.GrossMarginLabel, row.GrossMarginLabel);
        Assert.Equal("46,67%", row.GrossMarginText);

        var reference = row.Scenarios[1];
        Assert.Equal(InventoryComboPairFinancialScenarioKind.TargetReductionReference, reference.Kind);
        Assert.Equal(InventoryComboPresentation.ReferencePriceLabel, reference.Title);
        Assert.Equal(InventoryComboPresentation.ReferenceSubtitle, reference.Subtitle);
        Assert.Equal("R$ 27,50", reference.PairPriceText);
        Assert.Equal("R$ 2,50", reference.ReductionText);
    }

    [Fact]
    public void Estoque_e_cobertura_formatados()
    {
        var row = PresentSuggestion(Suggestion(targetStock: 12.5, anchorStock: 40, coverage: 22.4));
        Assert.Equal(InventoryComboPresentation.TargetStockLabel, row.TargetStockLabel);
        Assert.Equal(InventoryIntelligencePresentation.FormatQty(12.5), row.TargetStockText);
        Assert.Equal(InventoryComboPresentation.AnchorStockLabel, row.AnchorStockLabel);
        Assert.Equal(InventoryIntelligencePresentation.FormatQty(40), row.AnchorStockText);
        Assert.Equal(InventoryComboPresentation.AnchorCoverageLabel, row.AnchorCoverageLabel);
        Assert.Equal("22,4 dias", row.AnchorCoverageText);
    }

    [Fact]
    public void Cobertura_ausente_e_emdash()
    {
        var row = PresentSuggestion(Suggestion(coverage: null));
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.AnchorCoverageText);
    }

    [Theory]
    [InlineData(InventoryAttentionConfidence.Reliable, InventoryAttentionPresentation.ConfidenceReliable)]
    [InlineData(InventoryAttentionConfidence.Limited, InventoryAttentionPresentation.ConfidenceLimited)]
    [InlineData(InventoryAttentionConfidence.Unavailable, InventoryAttentionPresentation.ConfidenceUnavailable)]
    public void Confidence_reusa_70E(InventoryAttentionConfidence confidence, string expected)
    {
        var row = PresentSuggestion(Suggestion(confidence: confidence));
        Assert.Equal(expected, row.ConfidenceText);
        Assert.Equal(expected, InventoryAttentionPresentation.ConfidenceLabel(confidence));
    }

    [Theory]
    [InlineData(InventoryComboSuggestionLimitation.WeakPairEvidence, InventoryComboPresentation.LimitationWeak)]
    [InlineData(InventoryComboSuggestionLimitation.InsufficientPairHistory, InventoryComboPresentation.LimitationInsufficientHistory)]
    [InlineData(InventoryComboSuggestionLimitation.TargetLimitedConfidence, InventoryComboPresentation.LimitationTargetLimited)]
    [InlineData(InventoryComboSuggestionLimitation.AnchorLimitedConfidence, InventoryComboPresentation.LimitationAnchorLimited)]
    [InlineData(InventoryComboSuggestionLimitation.OtherDataLimitation, InventoryComboPresentation.LimitationOtherData)]
    public void Limitation_tem_texto(InventoryComboSuggestionLimitation limitation, string expected)
    {
        Assert.False(string.IsNullOrWhiteSpace(expected));
        Assert.Equal(expected, InventoryComboPresentation.LimitationText(limitation));
        var row = PresentSuggestion(Suggestion(limitations: [limitation]));
        Assert.Equal(expected, Assert.Single(row.LimitationsText));
        Assert.Equal(InventoryAttentionPresentation.ConfidenceReliable, row.ConfidenceText);
    }

    [Fact]
    public void Limitation_nao_muda_status_de_sugestao()
    {
        var row = PresentSuggestion(Suggestion(
            limitations: [InventoryComboSuggestionLimitation.WeakPairEvidence],
            confidence: InventoryAttentionConfidence.Limited));
        Assert.Equal(InventoryAttentionPresentation.ConfidenceLimited, row.ConfidenceText);
        Assert.Equal(22, row.AnchorProductId);
    }

    [Fact]
    public void Target_sem_sugestao_nao_e_erro()
    {
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            QueryCount = 10,
            Targets =
            [
                new InventoryComboTargetSuggestionGroup
                {
                    ProductId = 7,
                    Code = "T7",
                    Name = "Alvo",
                    Eligibility = new InventoryComboTargetEligibility
                    {
                        ProductId = 7,
                        Status = ComboEligibilityStatus.Eligible,
                        Reason = ComboTargetEligibilityReason.Idle,
                    },
                    Suggestions = [],
                },
            ],
        });
        var group = Assert.Single(presented.Targets);
        Assert.Equal(InventoryComboPresentation.EmptyTargetMessage, group.EmptyMessage);
        Assert.Equal("Nenhuma combinação", group.SuggestionCountText);
        Assert.Empty(group.Suggestions);
        Assert.Equal("", presented.EmptySnapshotMessage);
    }

    [Fact]
    public void Snapshot_sem_targets()
    {
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            QueryCount = 9,
        });
        Assert.Empty(presented.Targets);
        Assert.Equal(InventoryComboPresentation.EmptySnapshotMessage, presented.EmptySnapshotMessage);
        Assert.Equal(InventoryComboPresentation.DisclaimerText, presented.DisclaimerText);
    }

    [Theory]
    [InlineData(1, "1 combinação")]
    [InlineData(2, "2 combinações")]
    [InlineData(3, "3 combinações")]
    public void Contagem_sem_melhor(int n, string expected)
    {
        Assert.Equal(expected, InventoryComboPresentation.SuggestionCountText(n));
        Assert.DoesNotContain("melhores", expected, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preco_ausente_vira_emdash_nao_zero()
    {
        var row = InventoryComboPresentation.PresentSuggestion(new InventoryComboSuggestion
        {
            TargetProductId = 1,
            AnchorProductId = 2,
            PairEvidence = InventoryComboPairEvidence.Observed,
            NormalPairPrice = double.NaN,
            PairFloorPrice = double.PositiveInfinity,
            Scenarios = [],
        });
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.CurrentPriceText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.FloorPriceText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.GrossProfitText);
        Assert.Equal(InventoryProjectionPresentation.EmDash, row.ReferencePriceText);
        Assert.DoesNotContain("R$ 0,00", row.CurrentPriceText, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordem_B4_preservada()
    {
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            QueryCount = 10,
            ProductTitles = Titles((1, "T1", "Alvo"), (8, "A8", "Oito"), (3, "A3", "Tres"), (20, "A20", "Vinte")),
            Targets =
            [
                new InventoryComboTargetSuggestionGroup
                {
                    ProductId = 1,
                    Code = "T1",
                    Name = "Alvo",
                    Eligibility = new InventoryComboTargetEligibility
                    {
                        ProductId = 1,
                        Status = ComboEligibilityStatus.Eligible,
                        Reason = ComboTargetEligibilityReason.ExpirySurplus,
                    },
                    Suggestions =
                    [
                        Suggestion(anchorId: 8),
                        Suggestion(anchorId: 3),
                        Suggestion(anchorId: 20),
                    ],
                },
            ],
        });
        var ids = Assert.Single(presented.Targets).Suggestions.Select(s => s.AnchorProductId).ToArray();
        Assert.Equal(new[] { 8, 3, 20 }, ids);
        Assert.Equal("A8 — Oito", presented.Targets[0].Suggestions[0].AnchorTitle);
    }

    [Fact]
    public void Titulos_usam_code_e_name_70C()
    {
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot
        {
            ProductTitles = Titles((11, "C11", "Alvo X"), (22, "C22", "Ancora Y")),
            Targets =
            [
                new InventoryComboTargetSuggestionGroup
                {
                    ProductId = 11,
                    Code = "C11",
                    Name = "Alvo X",
                    Eligibility = new InventoryComboTargetEligibility
                    {
                        ProductId = 11,
                        Reason = ComboTargetEligibilityReason.ProjectedExcess,
                        Status = ComboEligibilityStatus.Eligible,
                    },
                    Suggestions = [Suggestion(targetId: 11, anchorId: 22)],
                },
            ],
        });
        var group = Assert.Single(presented.Targets);
        Assert.Equal("C11 — Alvo X", group.TargetTitle);
        Assert.Equal("C22 — Ancora Y", Assert.Single(group.Suggestions).AnchorTitle);
    }

    [Fact]
    public void Cultura_enUS_ainda_ptBR()
    {
        using var _ = new CultureScope("en-US");
        var row = PresentSuggestion(Suggestion(
            price: 10,
            floor: 7.5,
            profit: 4,
            margin: 0.4,
            coverage: 22.4,
            share: 0.4,
            pairTx: 4));
        Assert.Equal("R$ 10,00", row.CurrentPriceText);
        Assert.Equal("R$ 7,50", row.FloorPriceText);
        Assert.Equal("40%", row.GrossMarginText);
        Assert.Equal("22,4 dias", row.AnchorCoverageText);
        Assert.Contains("40%", row.EvidenceDetailText, StringComparison.Ordinal);
        Assert.DoesNotContain(".", row.CurrentPriceText.Replace("R$ ", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void Disclaimer_canonico()
    {
        var presented = InventoryComboPresentation.Apply(new InventoryComboIntelligenceSnapshot());
        Assert.Equal(InventoryComboPresentation.DisclaimerText, presented.DisclaimerText);
        Assert.Contains("apoio à decisão", presented.DisclaimerText, StringComparison.Ordinal);
        Assert.Contains("não cria promoções", presented.DisclaimerText, StringComparison.Ordinal);
        Assert.Contains("não altera preços", presented.DisclaimerText, StringComparison.Ordinal);
        Assert.Contains("não movimenta estoque automaticamente", presented.DisclaimerText, StringComparison.Ordinal);
        Assert.Contains("não representam previsão de venda", presented.DisclaimerText, StringComparison.Ordinal);
    }

    [Fact]
    public void Linguagem_proibida_ausente()
    {
        var texts = new List<string>
        {
            InventoryComboPresentation.DisclaimerText,
            InventoryComboPresentation.EmptyTargetMessage,
            InventoryComboPresentation.EmptySnapshotMessage,
            InventoryComboPresentation.TargetReasonExpirySurplus,
            InventoryComboPresentation.TargetReasonProjectedExcess,
            InventoryComboPresentation.TargetReasonIdle,
            InventoryComboPresentation.AnchorReasonHealthy,
            InventoryComboPresentation.EvidenceObserved,
            InventoryComboPresentation.EvidenceWeak,
            InventoryComboPresentation.EvidenceInsufficient,
            InventoryComboPresentation.EvidenceInsufficientDetail,
            InventoryComboPresentation.CurrentPriceLabel,
            InventoryComboPresentation.FloorPriceLabel,
            InventoryComboPresentation.FloorExplanation,
            InventoryComboPresentation.ReferencePriceLabel,
            InventoryComboPresentation.ReferenceSubtitle,
            InventoryComboPresentation.ReductionLabel,
            InventoryComboPresentation.GrossProfitLabel,
            InventoryComboPresentation.GrossMarginLabel,
            InventoryComboPresentation.LimitationWeak,
            InventoryComboPresentation.LimitationInsufficientHistory,
            InventoryComboPresentation.LimitationTargetLimited,
            InventoryComboPresentation.LimitationAnchorLimited,
            InventoryComboPresentation.LimitationOtherData,
            InventoryComboPresentation.SuggestionCountText(3),
            InventoryComboPresentation.JointSalesText(2),
            InventoryComboPresentation.AssociationShareText(0.4),
        };
        var row = PresentSuggestion(Suggestion(
            price: 30,
            referencePrice: 27.5,
            referenceProfit: 11.5,
            referenceMargin: 0.4,
            reduction: 2.5));
        texts.Add(row.CurrentPriceLabel);
        texts.Add(row.ReferenceSubtitle);
        texts.Add(row.GrossProfitLabel);
        texts.Add(row.ReductionLabel);
        texts.Add(row.EvidenceDetailText);
        foreach (var text in texts)
        {
            foreach (var banned in Forbidden)
                Assert.DoesNotContain(banned, text ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Presentation_nao_abre_banco_nem_ui()
    {
        var source = ReadSource("src", "SGDB.App", "Models", "InventoryComboPresentation.cs");
        Assert.DoesNotContain("DatabaseService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchaseService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductCompositionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ExpectedQueryCount = 0", source, StringComparison.Ordinal);
        Assert.Contains("InventoryAttentionPresentation.ConfidenceLabel", source, StringComparison.Ordinal);
        Assert.Contains("InventoryProjectionPresentation.FormatMoney", source, StringComparison.Ordinal);
    }

    static InventoryComboSuggestionPresentationRow PresentSuggestion(InventoryComboSuggestion suggestion) =>
        InventoryComboPresentation.PresentSuggestion(suggestion, titles: Titles(
            (suggestion.TargetProductId, "T" + suggestion.TargetProductId, "Alvo"),
            (suggestion.AnchorProductId, "A" + suggestion.AnchorProductId, "Ancora")));

    static InventoryComboSuggestion Suggestion(
        int targetId = 11,
        int anchorId = 22,
        InventoryComboPairEvidence evidence = InventoryComboPairEvidence.Observed,
        int pairTx = 4,
        int targetTx = 10,
        double? share = 0.4,
        double price = 30,
        double floor = 20,
        double profit = 14,
        double margin = 14d / 30d,
        double? referencePrice = null,
        double? referenceProfit = null,
        double? referenceMargin = null,
        double? reduction = null,
        double targetStock = 80,
        double anchorStock = 40,
        double? coverage = 20,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable,
        InventoryComboSuggestionLimitation[]? limitations = null)
    {
        var scenarios = new List<InventoryComboPairFinancialScenario>
        {
            new()
            {
                Kind = InventoryComboPairFinancialScenarioKind.CurrentPrices,
                PairPrice = price,
                GrossProfit = profit,
                GrossMargin = margin,
                ReductionFromCurrent = 0,
            },
        };
        if (referencePrice is double refPrice)
        {
            scenarios.Add(new InventoryComboPairFinancialScenario
            {
                Kind = InventoryComboPairFinancialScenarioKind.TargetReductionReference,
                PairPrice = refPrice,
                GrossProfit = referenceProfit ?? 0,
                GrossMargin = referenceMargin ?? 0,
                ReductionFromCurrent = reduction ?? 0,
            });
        }

        return new InventoryComboSuggestion
        {
            TargetProductId = targetId,
            AnchorProductId = anchorId,
            TargetReason = ComboTargetEligibilityReason.ExpirySurplus,
            AnchorReason = ComboAnchorEligibilityReason.HealthyNormalCoverage,
            PairEvidence = evidence,
            NormalPairPrice = price,
            PairCost = 16,
            PairFloorPrice = floor,
            Scenarios = scenarios,
            TargetStock = targetStock,
            AnchorStock = anchorStock,
            AnchorCoverageDays = coverage,
            PairTransactions = pairTx,
            TargetTransactions = targetTx,
            ConfidenceTargetToAnchor = share,
            Confidence = confidence,
            Limitations = limitations ?? [],
        };
    }

    static Dictionary<int, InventoryComboProductTitle> Titles(
        params (int Id, string Code, string Name)[] items)
    {
        var map = new Dictionary<int, InventoryComboProductTitle>();
        foreach (var item in items)
        {
            map[item.Id] = new InventoryComboProductTitle
            {
                ProductId = item.Id,
                Code = item.Code,
                Name = item.Name,
            };
        }

        return map;
    }

    static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
