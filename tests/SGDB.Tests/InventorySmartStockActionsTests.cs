using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 72 — estoque inteligente simples e acionável.
/// Sem banco real. Sem segundo motor. QueryCount = 0 nos composers puros.
/// </summary>
public class InventorySmartStockActionsTests
{
    static readonly DateTime Today = new(2026, 9, 6);

    [Fact]
    public void Margem_25_desejada_20_minima()
    {
        Assert.Equal(25, InventorySmartMargin.DesiredPercent);
        Assert.Equal(20, InventorySmartMargin.AbsoluteMinimumPercent);
        Assert.Equal(20, InventorySmartMargin.EffectiveMinimumPercent((double?)null));
        Assert.Equal(20, InventorySmartMargin.EffectiveMinimumPercent(10));
        Assert.Equal(30, InventorySmartMargin.EffectiveMinimumPercent(30));
        Assert.True(InventorySmartMargin.MeetsAbsoluteMinimum(20));
        Assert.False(InventorySmartMargin.MeetsAbsoluteMinimum(19.99));
    }

    [Fact]
    public void Estoque_zerado_com_giro_compra_agora_com_quantidade()
    {
        var turnover = Turnover(stock: 0, vmv: 2, band: InventoryCoverageBand.Zero, minStock: 12, pack: 6);
        var guidance = InventoryPurchaseGuidanceEngine.Evaluate(new InventoryPurchaseGuidanceInput
        {
            ProductId = 1,
            TotalStock = 0,
            Vmv30 = 2,
            CoverageBand = InventoryCoverageBand.Zero,
            IsZeroStockWithTurnover = true,
            HasPhysicalAvailabilityEvidence = true,
            HistoryDays = 90,
        });
        guidance = InventoryPurchaseGuidanceQuantityEngine.Attach(guidance, turnover, Facts(), Today);
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, guidance.Action);
        Assert.Equal(InventoryPurchaseGuidanceUrgency.BuyNow, guidance.Urgency);
        Assert.True(guidance.RecommendedQuantity >= 6);
        Assert.Equal(6, guidance.PackFactor);
        var rec = Rec(turnover, guidance: guidance, facts: Facts());
        Assert.Equal(InventorySmartPrincipalAction.BuyNow, rec.PrincipalAction);
        Assert.Contains("unidades", rec.ActionText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Estoque_suficiente_nao_compra()
    {
        var turnover = Turnover(stock: 80, vmv: 2, band: InventoryCoverageBand.Normal, days: 40);
        var guidance = InventoryPurchaseGuidanceEngine.Evaluate(new InventoryPurchaseGuidanceInput
        {
            ProductId = 1,
            TotalStock = 80,
            Vmv30 = 2,
            CoverageBand = InventoryCoverageBand.Normal,
            CoverageDays = 40,
            HasPhysicalAvailabilityEvidence = true,
            HistoryDays = 90,
        });
        var rec = Rec(turnover, guidance: guidance, facts: Facts());
        Assert.NotEqual(InventorySmartPrincipalAction.BuyNow, rec.PrincipalAction);
        Assert.NotEqual(InventorySmartPrincipalAction.BuySoon, rec.PrincipalAction);
    }

    [Fact]
    public void Excesso_projetado_suspende_compra()
    {
        var turnover = Turnover(stock: 100, vmv: 1, band: InventoryCoverageBand.Normal, days: 100);
        var attention = Att(InventoryAttentionReason.ProjectedExcess30, InventoryAttentionFamily.Excess,
            InventoryOperatorAction.EvaluateExcess, excess: 40);
        var guidance = InventoryPurchaseGuidanceEngine.Evaluate(new InventoryPurchaseGuidanceInput
        {
            ProductId = 1,
            TotalStock = 100,
            Stock = 100,
            Vmv30 = 1,
            CoverageBand = InventoryCoverageBand.Normal,
            CoverageDays = 100,
            CanProjectSku = true,
            ProjectedExcessQuantity = 40,
            HasPhysicalAvailabilityEvidence = true,
            HistoryDays = 90,
        });
        var rec = Rec(turnover, attention, guidance, facts: Facts());
        Assert.Equal(InventorySmartPrincipalAction.SuspendPurchase, rec.PrincipalAction);
        Assert.DoesNotContain("Comprar", rec.ActionText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Produto_parado_suspende_compra()
    {
        var turnover = Turnover(stock: 50, vmv: 0, band: InventoryCoverageBand.NotCalculable, idle: true);
        var attention = Att(InventoryAttentionReason.Idle, InventoryAttentionFamily.Turnover,
            InventoryOperatorAction.Monitor);
        var guidance = InventoryPurchaseGuidanceEngine.Evaluate(new InventoryPurchaseGuidanceInput
        {
            ProductId = 1,
            TotalStock = 50,
            Stock = 50,
            Vmv30 = 0,
            CoverageBand = InventoryCoverageBand.NotCalculable,
            IsIdle = true,
            HasPhysicalAvailabilityEvidence = true,
            HistoryDays = 90,
        });
        var rec = Rec(turnover, attention, guidance, facts: Facts());
        Assert.Equal(InventorySmartPrincipalAction.SuspendPurchase, rec.PrincipalAction);
    }

    [Fact]
    public void Validade_proxima_com_giro_suficiente_mantem_preco()
    {
        var turnover = Turnover(stock: 10, vmv: 5, band: InventoryCoverageBand.Low, days: 2);
        var attention = Att(InventoryAttentionReason.NearExpiryWithoutSurplus, InventoryAttentionFamily.Expiry,
            InventoryOperatorAction.PrioritizeSale, daysUntil: 7);
        var pricing = InventorySmartPromotionPricing.Evaluate(Facts(cost: 6, price: 10), turnover, attention, null, Today);
        Assert.Equal(InventorySmartPrincipalAction.KeepPrice, pricing.Action);
    }

    [Fact]
    public void Validade_proxima_com_sobra_promove()
    {
        var turnover = Turnover(stock: 40, vmv: 1, band: InventoryCoverageBand.Normal, days: 40);
        var attention = Att(InventoryAttentionReason.SurplusAtExpiry, InventoryAttentionFamily.Expiry,
            InventoryOperatorAction.PrioritizeSale, surplus: 20, daysUntil: 10);
        var promo = new InventoryPromotionSuggestionResult
        {
            ProductId = 1,
            Status = InventoryPromotionSuggestionStatus.Suggested,
            Action = InventoryPromotionSuggestionAction.ConsiderPromotion,
            Thesis = InventoryCommercialScenarioThesis.ExpirySurplus,
            Confidence = InventoryAttentionConfidence.Reliable,
        };
        var rec = Rec(turnover, attention, promotion: promo, facts: Facts(cost: 6, price: 10));
        Assert.Equal(InventorySmartPrincipalAction.PromoteIndividual, rec.PrincipalAction);
        Assert.True(rec.SuggestedPromoPrice >= 6);
        Assert.True(rec.ResultingMarginPercent >= 20);
    }

    [Fact]
    public void Promocao_segura_a_25_por_cento()
    {
        var pricing = InventorySmartPromotionPricing.Evaluate(
            Facts(cost: 75, price: 120),
            Turnover(stock: 40, vmv: 0.5, band: InventoryCoverageBand.Normal),
            Att(InventoryAttentionReason.ProjectedExcess30, InventoryAttentionFamily.Excess,
                InventoryOperatorAction.EvaluateExcess, excess: 20),
            null,
            Today);
        Assert.Equal(InventorySmartPrincipalAction.PromoteIndividual, pricing.Action);
        Assert.True(pricing.ResultingMarginPercent >= 25 - 0.05);
    }

    [Fact]
    public void Promocao_urgente_entre_20_e_25()
    {
        var pricing = InventorySmartPromotionPricing.Evaluate(
            Facts(cost: 80, price: 110),
            Turnover(stock: 30, vmv: 0.2, band: InventoryCoverageBand.Normal),
            Att(InventoryAttentionReason.SurplusAtExpiry, InventoryAttentionFamily.Expiry,
                InventoryOperatorAction.PrioritizeSale, surplus: 20, daysUntil: 5),
            new InventoryPromotionSuggestionResult
            {
                Thesis = InventoryCommercialScenarioThesis.ExpirySurplus,
            },
            Today);
        Assert.Equal(InventorySmartPrincipalAction.PromoteIndividual, pricing.Action);
        Assert.InRange(pricing.ResultingMarginPercent ?? 0, 20, 25.05);
    }

    [Fact]
    public void Promocao_abaixo_de_20_recusada()
    {
        var pricing = InventorySmartPromotionPricing.Evaluate(
            Facts(cost: 90, price: 100),
            Turnover(stock: 20, vmv: 0.1),
            Att(InventoryAttentionReason.ProjectedExcess30, InventoryAttentionFamily.Excess,
                InventoryOperatorAction.EvaluateExcess, excess: 10),
            null,
            Today);
        Assert.NotEqual(InventorySmartPrincipalAction.PromoteIndividual, pricing.Action);
        Assert.True(
            pricing.Action is InventorySmartPrincipalAction.NoSafePromotion
                or InventorySmartPrincipalAction.KeepPrice
                or InventorySmartPrincipalAction.HighlightWithoutDiscount
                or InventorySmartPrincipalAction.UnsafeLiquidation);
        if (pricing.Action == InventorySmartPrincipalAction.PromoteIndividual)
            Assert.True(pricing.ResultingMarginPercent >= 20);
    }

    [Fact]
    public void Preco_abaixo_do_custo_recusado()
    {
        var pricing = InventorySmartPromotionPricing.Evaluate(
            Facts(cost: 12, price: 10), Turnover(), null, null, Today);
        Assert.Equal(InventorySmartPrincipalAction.ReviewData, pricing.Action);
        Assert.Contains("custo", pricing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Combo_seguro_e_estoque_real()
    {
        var group = new InventoryComboTargetSuggestionGroup
        {
            ProductId = 1,
            Code = "A",
            Name = "Alvo",
            TotalStock = 18,
            Eligibility = new InventoryComboTargetEligibility
            {
                ProductId = 1,
                Status = ComboEligibilityStatus.Eligible,
                Reason = ComboTargetEligibilityReason.Idle,
                Confidence = InventoryAttentionConfidence.Reliable,
            },
            Suggestions =
            [
                new InventoryComboSuggestion
                {
                    TargetProductId = 1,
                    AnchorProductId = 2,
                    TargetStock = 18,
                    AnchorStock = 40,
                    MaxSafeQuantity = 18,
                    Confidence = InventoryAttentionConfidence.Reliable,
                },
            ],
        };
        var presented = InventoryComboPresentation.PresentTarget(group);
        Assert.Equal(InventoryIntelligencePresentation.FormatQty(18), presented.TargetStockText);
        Assert.NotEqual(InventoryProjectionPresentation.EmDash, presented.TargetStockText);
        Assert.Equal(18, group.Suggestions[0].MaxSafeQuantity);
    }

    [Fact]
    public void Combo_sem_sugestao_mostra_estoque_e_motivo()
    {
        var group = new InventoryComboTargetSuggestionGroup
        {
            ProductId = 9,
            Code = "Z",
            Name = "Parado",
            TotalStock = 24,
            Eligibility = new InventoryComboTargetEligibility
            {
                ProductId = 9,
                Status = ComboEligibilityStatus.Eligible,
                Reason = ComboTargetEligibilityReason.Idle,
                Confidence = InventoryAttentionConfidence.Reliable,
            },
            Suggestions = [],
            RejectionReasons =
            [
                InventoryComboRejectionReason.MarginBelowMinimum,
                InventoryComboRejectionReason.CompanionShortageRisk,
            ],
        };
        var presented = InventoryComboPresentation.PresentTarget(group);
        Assert.Equal(InventoryIntelligencePresentation.FormatQty(24), presented.TargetStockText);
        Assert.Contains("20%", presented.EmptyMessage, StringComparison.Ordinal);
        Assert.Contains("falta", presented.EmptyMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(InventoryComboPresentation.EmptyTargetMessage, presented.EmptyMessage);
    }

    [Fact]
    public void Combo_acompanhante_em_falta_recusado()
    {
        var anchor = InventoryComboAnchorEligibilityEngine.Evaluate(new InventoryComboEligibilityInput
        {
            Turnover = ComboEligibilityHarness.Turnover(stock: 0, band: InventoryCoverageBand.Zero),
            Attention = ComboEligibilityHarness.Attention(),
            Facts = ComboEligibilityHarness.Facts(),
            Guidance = ComboEligibilityHarness.Guidance(
                InventoryPurchaseGuidanceAction.ConsiderReplenishment,
                InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand,
                InventoryPurchaseGuidanceStatus.GuidanceAvailable),
        });
        Assert.Equal(ComboEligibilityStatus.Blocked, anchor.Status);
        Assert.True(anchor.Reason is ComboAnchorEligibilityReason.AnchorStockUnsafe
            or ComboAnchorEligibilityReason.AnchorConsiderReplenishment
            or ComboAnchorEligibilityReason.AnchorCoverageUnsafe);
    }

    [Fact]
    public void Combo_abaixo_de_20_e_incompativel_mapeados()
    {
        Assert.Equal("Margem abaixo de 20%",
            InventorySmartPresentation.RejectionLabel(InventoryComboRejectionReason.MarginBelowMinimum));
        Assert.Equal("Combinação comercialmente incompatível",
            InventorySmartPresentation.RejectionLabel(InventoryComboRejectionReason.CommerciallyIncompatible));
        Assert.Equal("Acompanhante com risco de falta",
            InventorySmartPresentation.RejectionLabel(InventoryComboRejectionReason.CompanionShortageRisk));
    }

    [Fact]
    public void Produto_inativo_ou_sem_venda_excluido()
    {
        var blocked = InventoryComboTargetEligibilityEngine.Evaluate(new InventoryComboEligibilityInput
        {
            Turnover = ComboEligibilityHarness.Turnover(idle: true),
            Attention = ComboEligibilityHarness.Attention(
                InventoryAttentionReason.Idle, InventoryAttentionFamily.Turnover,
                InventoryOperatorAction.Monitor),
            Facts = new InventoryCommercialFacts
            {
                ProductId = ComboEligibilityHarness.ProductId,
                ProductFound = true,
                CatalogSalePrice = 10,
                CurrentAverageCost = 6,
                PriceQuality = InventoryCommercialPriceQuality.Usable,
                CostQuality = InventoryCommercialCostQuality.Known,
                CanEvaluateFinancialScenario = true,
                AllowsSale = false,
                LimitationReasons = [InventoryCommercialFactsReason.SaleNotAllowed],
            },
            Guidance = ComboEligibilityHarness.Guidance(),
        });
        Assert.Equal(ComboEligibilityStatus.Blocked, blocked.Status);
        Assert.Equal(ComboTargetEligibilityReason.TargetNotSellable, blocked.Reason);
    }

    [Fact]
    public void Produto_sem_saldo_excluido()
    {
        var blocked = InventoryComboTargetEligibilityEngine.Evaluate(ComboEligibilityHarness.Input(
            turnover: ComboEligibilityHarness.Turnover(stock: 0, vmv30: 0, band: InventoryCoverageBand.Zero)));
        Assert.Equal(ComboEligibilityStatus.Blocked, blocked.Status);
        Assert.Equal(ComboTargetEligibilityReason.TargetStockUnsafe, blocked.Reason);
    }

    [Fact]
    public void Reposicao_em_caixas_e_unidades()
    {
        var turnover = Turnover(stock: 2, vmv: 3, band: InventoryCoverageBand.Critical, days: 0.6, pack: 6, minStock: 24);
        var guidance = new InventoryPurchaseGuidanceResult
        {
            ProductId = 1,
            Action = InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            Status = InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            PrimaryReason = InventoryPurchaseGuidanceReason.CriticalCoverage,
            Confidence = InventoryAttentionConfidence.Limited,
        };
        var attached = InventoryPurchaseGuidanceQuantityEngine.Attach(guidance, turnover, Facts(cost: 2), Today);
        Assert.True(attached.PackCount >= 1);
        Assert.Equal(6, attached.PackFactor);
        Assert.Equal(attached.PackCount * 6, attached.RecommendedQuantity);
        var text = InventorySmartPresentation.PackagingText(
            attached.RecommendedQuantity!.Value, attached.PackCount, attached.PackFactor);
        Assert.Contains("unidades", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fardos", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Conflito_comprar_versus_promover_nao_simultaneo()
    {
        var turnover = Turnover(stock: 0, vmv: 2, band: InventoryCoverageBand.Zero);
        var guidance = new InventoryPurchaseGuidanceResult
        {
            ProductId = 1,
            Action = InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            Status = InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            PrimaryReason = InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand,
            Urgency = InventoryPurchaseGuidanceUrgency.BuyNow,
            RecommendedQuantity = 12,
        };
        var promo = new InventoryPromotionSuggestionResult
        {
            Status = InventoryPromotionSuggestionStatus.Suggested,
            Action = InventoryPromotionSuggestionAction.ConsiderPromotion,
        };
        var rec = Rec(turnover, guidance: guidance, promotion: promo, facts: Facts());
        Assert.Equal(InventorySmartPrincipalAction.BuyNow, rec.PrincipalAction);
        Assert.DoesNotContain(rec.Alternatives, a => a.Contains("promoção", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReviewData_prevalece()
    {
        var turnover = Turnover(stock: -3, vmv: 1, band: InventoryCoverageBand.Negative);
        var attention = Att(InventoryAttentionReason.NegativeStock, InventoryAttentionFamily.DataQuality,
            InventoryOperatorAction.ReviewData);
        var guidance = new InventoryPurchaseGuidanceResult
        {
            ProductId = 1,
            Action = InventoryPurchaseGuidanceAction.ReviewData,
            Status = InventoryPurchaseGuidanceStatus.ReviewData,
            PrimaryReason = InventoryPurchaseGuidanceReason.StructuralDataIssue,
        };
        var rec = Rec(turnover, attention, guidance, facts: Facts());
        Assert.Equal(InventorySmartPrincipalAction.ReviewData, rec.PrincipalAction);
        Assert.Contains("negativo", rec.ReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Ajuste de estoque", rec.FixLocationText);
    }

    [Fact]
    public void Estoque_negativo_e_divergencia_de_lote()
    {
        var neg = InventoryDataQuality.Describe(
            Att(InventoryAttentionReason.NegativeStock, InventoryAttentionFamily.DataQuality,
                InventoryOperatorAction.ReviewData),
            null, Facts(), Turnover(stock: -2, band: InventoryCoverageBand.Negative));
        Assert.True(neg.Blocking);
        Assert.Contains(neg.Reasons, r => r.Contains("negativo", StringComparison.OrdinalIgnoreCase));

        var lot = InventoryDataQuality.Describe(
            Att(InventoryAttentionReason.TrackedQuantityExceedsWarehouse, InventoryAttentionFamily.DataQuality,
                InventoryOperatorAction.ReviewData),
            null, Facts(), Turnover(stock: 10));
        Assert.Contains(lot.Reasons, r => r.Contains("lote", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(InventoryDataFixLocation.LotsAndExpiry, lot.Location);
    }

    [Fact]
    public void Visao_simples_e_detalhada_sem_schema()
    {
        InventorySmartViewPreference.ResetForTests(InventorySmartViewMode.Simple);
        Assert.Equal(InventorySmartViewMode.Simple, InventorySmartViewPreference.Current);
        InventorySmartViewPreference.ResetForTests(InventorySmartViewMode.Detailed);
        Assert.Equal(InventorySmartViewMode.Detailed, InventorySmartViewPreference.Current);
        Assert.Equal("inventory_smart_view_mode", InventorySmartViewPreference.SettingKey);
        var card = InventorySmartPresentation.ToCard(Rec(Turnover(), facts: Facts())!);
        Assert.False(string.IsNullOrWhiteSpace(card.ActionText));
        Assert.Equal(InventorySmartPresentation.DetailsButton, "Ver detalhes");
    }

    [Fact]
    public void Ver_detalhes_nao_gera_query_e_composer_e_zero()
    {
        Assert.Equal(0, InventorySmartRecommendationComposer.ExpectedQueryCount);
        Assert.Equal(0, InventoryPurchaseGuidanceQuantityEngine.ExpectedQueryCount);
        Assert.Equal(0, InventorySmartPromotionPricing.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboPresentation.ExpectedQueryCount);
        Assert.Equal(0, CentralDecisionPresentation.ExpectedQueryCount);
        var view = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "SGDB.App", "Views", "InventoryPurchaseGuidanceModuleView.xaml.cs"));
        var apply = view[view.IndexOf("private void ApplyView()", StringComparison.Ordinal)..];
        apply = apply[..apply.IndexOf("private void ShowEmpty", StringComparison.Ordinal)];
        Assert.DoesNotContain("InventoryProjectionService.Load", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenConnection", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Central_preserva_5_mais_3_e_titulo()
    {
        Assert.Equal(5, CentralDecisionSnapshot.MaxActNowItems);
        Assert.Equal(3, CentralDecisionSnapshot.MaxPreserveItems);
        Assert.Equal("O que fazer hoje", CentralDecisionPresentation.ActNowTitle);
        Assert.Equal(0, CentralDecisionComposer.OwnQueryCount);
        Assert.Equal(15, CentralDecisionLoader.MaxInheritedQueryCount);
    }

    [Fact]
    public void B8_sem_causalidade_e_future_sem_pipeline()
    {
        var loader = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "SGDB.App", "Services", "CentralDecisionLoader.cs"));
        Assert.Contains("CommercialGoalActionPlanComposer.ShouldSkipIntelligence", loader, StringComparison.Ordinal);
        var composer = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "SGDB.App", "Services", "InventorySmartRecommendationComposer.cs"));
        Assert.Contains("sem causalidade B8", composer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UnattributedGrossProfit", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("GrossProfitShare", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Banco_real_nao_utilizado_e_query_budget()
    {
        Assert.Equal(0, Environment.GetEnvironmentVariable("SGDB_DATABASE_PATH")?.Length ?? 0);
        Assert.Equal(10, InventoryComboIntelligenceComposer.ExpectedPipelineQueryCount);
        Assert.True(InventoryComboIntelligenceComposer.ExpectedPipelineQueryCount
            <= CentralDecisionLoader.MaxInheritedQueryCount);
        Assert.DoesNotContain("deposito.db",
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "SGDB.App", "Services",
                "InventorySmartRecommendationComposer.cs")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nao_ha_segundo_motor_de_decisao()
    {
        Assert.Contains("Não cria segundo motor",
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "SGDB.App", "Models", "InventorySmartAction.cs")),
            StringComparison.Ordinal);
        Assert.Equal(0, InventoryPurchaseGuidanceEngine.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboSuggestionEngine.ExpectedQueryCount);
    }

    static InventorySmartRecommendation Rec(
        ProductTurnoverRow turnover,
        InventoryAttentionResult? attention = null,
        InventoryPurchaseGuidanceResult? guidance = null,
        InventoryPromotionSuggestionResult? promotion = null,
        InventoryComboTargetSuggestionGroup? combo = null,
        InventoryCommercialFacts? facts = null) =>
        InventorySmartRecommendationComposer.ForProduct(
            turnover.ProductId, turnover, attention, guidance, promotion, combo, facts, today: Today)!;

    static ProductTurnoverRow Turnover(
        double stock = 20,
        double vmv = 2,
        InventoryCoverageBand band = InventoryCoverageBand.Normal,
        double? days = 10,
        bool idle = false,
        double pack = 1,
        double minStock = 0) =>
        new()
        {
            ProductId = 1,
            Code = "P1",
            Name = "Produto",
            Stock = stock,
            TotalStock = stock,
            Vmv30 = vmv,
            CoverageBand = band,
            CoverageDays = days,
            IsIdle = idle,
            HasPhysicalAvailabilityEvidence = true,
            HistoryDays = 90,
            PackFactor = pack,
            MinStock = minStock,
        };

    static InventoryAttentionResult Att(
        InventoryAttentionReason reason,
        InventoryAttentionFamily family,
        InventoryOperatorAction action,
        double? surplus = null,
        double? excess = null,
        int? daysUntil = null) =>
        new()
        {
            ProductId = 1,
            PrimaryReason = reason,
            Family = family,
            Action = action,
            Confidence = InventoryAttentionConfidence.Reliable,
            Priority = InventoryAttentionPriority.Normal,
            ProjectedExpirySurplusQuantity = surplus,
            ProjectedExcessQuantity = excess,
            NearestDatedDaysUntilExpiry = daysUntil,
            SecondaryReasons = [],
        };

    static InventoryCommercialFacts Facts(double cost = 6, double price = 10) =>
        new()
        {
            ProductId = 1,
            ProductFound = true,
            CurrentAverageCost = cost,
            CatalogSalePrice = price,
            CostQuality = InventoryCommercialCostQuality.Known,
            PriceQuality = InventoryCommercialPriceQuality.Usable,
            CanEvaluateFinancialScenario = true,
            AllowsSale = true,
        };

    static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "src", "SGDB.App", "SGDB.App.csproj")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
