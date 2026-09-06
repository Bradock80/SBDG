using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Traduz autoridades 70E/70F/70G/71A/B7 em uma orientação principal por produto.
/// Sem SQL, sem ranking novo, sem causalidade B8. QueryCount = 0.
/// </summary>
public static class InventorySmartRecommendationComposer
{
    public const int ExpectedQueryCount = 0;

    public static InventorySmartRecommendation? ForProduct(
        int productId,
        ProductTurnoverRow? turnover,
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance,
        InventoryPromotionSuggestionResult? promotion,
        InventoryComboTargetSuggestionGroup? combo,
        InventoryCommercialFacts? facts,
        CommercialGoalActionItem? planItem = null,
        DateTime? today = null)
    {
        if (productId <= 0)
            return null;

        var data = InventoryDataQuality.Describe(attention, guidance, facts, turnover);
        var pricing = InventorySmartPromotionPricing.Evaluate(facts, turnover, attention, promotion, today);
        var hasCombo = combo is { Eligibility.Status: ComboEligibilityStatus.Eligible, Suggestions.Count: > 0 };
        var rejections = combo?.RejectionReasons ?? [];

        var principal = ResolvePrincipal(
            attention, guidance, promotion, hasCombo, data, pricing, planItem);
        var alternatives = BuildAlternatives(principal, guidance, pricing, hasCombo);

        var qtyText = QuantityText(principal, guidance, pricing, attention);
        return new InventorySmartRecommendation
        {
            ProductId = productId,
            ProductCode = First(turnover?.Code, planItem?.ProductCode),
            ProductName = First(turnover?.Name, planItem?.ProductName),
            PrincipalAction = principal,
            Area = AreaOf(principal),
            Urgency = UrgencyOf(principal, attention, guidance),
            Confidence = planItem?.Confidence
                ?? attention?.Confidence
                ?? guidance?.Confidence
                ?? InventoryAttentionConfidence.Unavailable,
            ActionText = InventorySmartPresentation.ActionLabel(principal, guidance, pricing),
            ReasonText = ReasonText(principal, attention, guidance, data, pricing, combo),
            QuantityOrDeadlineText = qtyText,
            FixLocationText = data.LocationText,
            FixLocation = data.Location,
            CurrentStock = FiniteOrNull(turnover?.TotalStock),
            RecommendedQuantity = guidance?.RecommendedQuantity,
            PackFactor = guidance?.PackFactor,
            PackCount = guidance?.PackCount,
            EstimatedCost = guidance?.EstimatedCost,
            CoverageAfterPurchaseDays = guidance?.CoverageAfterPurchaseDays,
            RecommendedOrderDate = guidance?.RecommendedOrderDate,
            ReevaluationDate = guidance?.ReevaluationDate ?? pricing.ReevaluationDate,
            SuggestedPromoPrice = pricing.SuggestedPrice,
            DiscountAmount = pricing.DiscountAmount,
            DiscountPercent = pricing.DiscountPercent,
            ResultingMarginPercent = pricing.ResultingMarginPercent,
            PromoDurationDays = pricing.DurationDays,
            HasSafePromotion = pricing.Action == InventorySmartPrincipalAction.PromoteIndividual,
            HasSafeCombo = hasCombo,
            AnalysisPartial = data.Partial,
            AnalysisUnavailable = data.Unavailable && !data.HasFinancial,
            Alternatives = alternatives,
            ComboRejections = rejections,
            DataQualityReasons = data.Reasons,
        };
    }

    public static InventorySmartRecommendationSnapshot FromGuidance(
        InventoryPurchaseGuidanceSnapshot? guidance,
        InventoryIntelligenceSnapshot? intelligence,
        InventoryAttentionSnapshot? attention = null,
        InventoryPromotionSuggestionSnapshot? promotion = null,
        InventoryComboIntelligenceSnapshot? combos = null,
        InventoryCommercialFactsSnapshot? facts = null,
        DateTime? today = null)
    {
        var rows = new List<InventorySmartRecommendation>();
        var map = new Dictionary<int, InventorySmartRecommendation>();
        var turnovers = intelligence?.Rows ?? [];
        foreach (var turnover in turnovers)
        {
            InventoryPurchaseGuidanceResult? g = null;
            InventoryAttentionResult? att = null;
            InventoryPromotionSuggestionRow? promoRow = null;
            InventoryComboTargetSuggestionGroup? combo = null;
            InventoryCommercialFacts? commercial = null;
            guidance?.ByProductId.TryGetValue(turnover.ProductId, out g);
            if (g is null || g.Action == InventoryPurchaseGuidanceAction.None)
                continue;
            attention?.ByProductId.TryGetValue(turnover.ProductId, out att);
            promotion?.ByProductId.TryGetValue(turnover.ProductId, out promoRow);
            combos?.ByProductId.TryGetValue(turnover.ProductId, out combo);
            facts?.ByProductId.TryGetValue(turnover.ProductId, out commercial);
            var rec = ForProduct(
                turnover.ProductId, turnover, att, g, promoRow?.Suggestion, combo, commercial,
                today: today);
            if (rec is null)
                continue;
            rows.Add(rec);
            map.TryAdd(rec.ProductId, rec);
        }

        return new InventorySmartRecommendationSnapshot
        {
            QueryCount = ExpectedQueryCount,
            ViewMode = InventorySmartViewPreference.Current,
            Rows = rows,
            ByProductId = map,
        };
    }

    static InventorySmartPrincipalAction ResolvePrincipal(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance,
        InventoryPromotionSuggestionResult? promotion,
        bool hasCombo,
        InventoryDataQuality.Info data,
        InventorySmartPromotionPricing.Result pricing,
        CommercialGoalActionItem? planItem)
    {
        if (data.Blocking || guidance?.Action == InventoryPurchaseGuidanceAction.ReviewData
            || attention?.Action == InventoryOperatorAction.ReviewData
            || planItem?.ActionType == CommercialGoalActionType.ReviewData)
            return InventorySmartPrincipalAction.ReviewData;

        if (attention?.Action == InventoryOperatorAction.RemoveExpired
            || planItem?.ActionType == CommercialGoalActionType.RemoveExpired)
            return InventorySmartPrincipalAction.RemoveExpired;

        if (attention?.Family == InventoryAttentionFamily.Expiry
            || planItem?.ActionType == CommercialGoalActionType.PrioritizeExpiryRisk)
        {
            if (pricing.Action == InventorySmartPrincipalAction.KeepPrice)
                return InventorySmartPrincipalAction.KeepPrice;
            if (pricing.Action == InventorySmartPrincipalAction.PromoteIndividual)
                return InventorySmartPrincipalAction.PromoteIndividual;
            if (hasCombo)
                return InventorySmartPrincipalAction.EvaluateCombo;
            return InventorySmartPrincipalAction.HighlightWithoutDiscount;
        }

        if (guidance?.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment
            || planItem?.ActionType == CommercialGoalActionType.ProtectAvailability)
        {
            return guidance?.Urgency == InventoryPurchaseGuidanceUrgency.BuySoon
                ? InventorySmartPrincipalAction.BuySoon
                : InventorySmartPrincipalAction.BuyNow;
        }

        if (guidance?.Action == InventoryPurchaseGuidanceAction.DoNotReplenishNow
            || attention?.Action == InventoryOperatorAction.EvaluateExcess
            || attention?.Family == InventoryAttentionFamily.Turnover
            || planItem?.ActionType is CommercialGoalActionType.PrioritizeExcess
                or CommercialGoalActionType.PrioritizeIdle)
        {
            if (guidance?.PrimaryReason is InventoryPurchaseGuidanceReason.IdleStock
                or InventoryPurchaseGuidanceReason.ProjectedExcess30
                or InventoryPurchaseGuidanceReason.ProjectedExpirySurplus
                or InventoryPurchaseGuidanceReason.Expired
                or InventoryPurchaseGuidanceReason.ExpiresToday)
                return InventorySmartPrincipalAction.SuspendPurchase;
            if (planItem?.ActionType is CommercialGoalActionType.PrioritizeExcess
                or CommercialGoalActionType.PrioritizeIdle)
                return InventorySmartPrincipalAction.SuspendPurchase;
            return InventorySmartPrincipalAction.DoNotBuy;
        }

        if (pricing.Action == InventorySmartPrincipalAction.PromoteIndividual
            && promotion is { Action: InventoryPromotionSuggestionAction.ConsiderPromotion })
            return InventorySmartPrincipalAction.PromoteIndividual;

        if (hasCombo)
            return InventorySmartPrincipalAction.EvaluateCombo;

        if (guidance?.Action == InventoryPurchaseGuidanceAction.Monitor
            || planItem?.ActionType == CommercialGoalActionType.Monitor)
            return InventorySmartPrincipalAction.Monitor;

        return InventorySmartPrincipalAction.Preserve;
    }

    static IReadOnlyList<string> BuildAlternatives(
        InventorySmartPrincipalAction principal,
        InventoryPurchaseGuidanceResult? guidance,
        InventorySmartPromotionPricing.Result pricing,
        bool hasCombo)
    {
        var list = new List<string>(3);
        if (principal is InventorySmartPrincipalAction.BuyNow or InventorySmartPrincipalAction.BuySoon)
            return list;
        if (principal != InventorySmartPrincipalAction.SuspendPurchase
            && guidance?.Action == InventoryPurchaseGuidanceAction.DoNotReplenishNow)
            list.Add("Suspender compra");
        if (principal != InventorySmartPrincipalAction.PromoteIndividual
            && pricing.Action == InventorySmartPrincipalAction.PromoteIndividual)
            list.Add("Fazer promoção individual");
        if (principal != InventorySmartPrincipalAction.EvaluateCombo && hasCombo)
            list.Add("Avaliar combo");
        if (principal != InventorySmartPrincipalAction.KeepPrice
            && pricing.Action == InventorySmartPrincipalAction.KeepPrice)
            list.Add("Manter preço");
        return list;
    }

    static InventorySmartArea AreaOf(InventorySmartPrincipalAction action) =>
        action switch
        {
            InventorySmartPrincipalAction.RemoveExpired => InventorySmartArea.Urgent,
            InventorySmartPrincipalAction.PromoteIndividual
                or InventorySmartPrincipalAction.HighlightWithoutDiscount
                or InventorySmartPrincipalAction.EvaluateCombo
                or InventorySmartPrincipalAction.UnsafeLiquidation => InventorySmartArea.SellFaster,
            InventorySmartPrincipalAction.BuyNow
                or InventorySmartPrincipalAction.BuySoon
                or InventorySmartPrincipalAction.DoNotBuy
                or InventorySmartPrincipalAction.SuspendPurchase => InventorySmartArea.Buy,
            InventorySmartPrincipalAction.ReviewData => InventorySmartArea.FixData,
            _ => InventorySmartArea.Preserve,
        };

    static InventorySmartUrgency UrgencyOf(
        InventorySmartPrincipalAction action,
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance)
    {
        if (action is InventorySmartPrincipalAction.ReviewData
            or InventorySmartPrincipalAction.RemoveExpired
            or InventorySmartPrincipalAction.BuyNow)
            return InventorySmartUrgency.Critical;
        if (attention?.Priority == InventoryAttentionPriority.High
            || action is InventorySmartPrincipalAction.PromoteIndividual
                or InventorySmartPrincipalAction.BuySoon)
            return InventorySmartUrgency.High;
        if (guidance?.Action == InventoryPurchaseGuidanceAction.DoNotReplenishNow
            || action == InventorySmartPrincipalAction.SuspendPurchase)
            return InventorySmartUrgency.Attention;
        return InventorySmartUrgency.None;
    }

    static string QuantityText(
        InventorySmartPrincipalAction action,
        InventoryPurchaseGuidanceResult? guidance,
        InventorySmartPromotionPricing.Result pricing,
        InventoryAttentionResult? attention)
    {
        if (action is InventorySmartPrincipalAction.BuyNow or InventorySmartPrincipalAction.BuySoon
            && guidance?.RecommendedQuantity is double qty)
        {
            return InventorySmartPresentation.PackagingText(qty, guidance.PackCount, guidance.PackFactor);
        }

        if (action == InventorySmartPrincipalAction.PromoteIndividual
            && pricing.DurationDays is int days)
            return $"por {days} dias";

        if (attention?.NearestDatedDaysUntilExpiry is int left && left >= 0)
            return left == 0 ? "vence hoje" : $"vence em {left} dia(s)";

        return "";
    }

    static string ReasonText(
        InventorySmartPrincipalAction action,
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance,
        InventoryDataQuality.Info data,
        InventorySmartPromotionPricing.Result pricing,
        InventoryComboTargetSuggestionGroup? combo)
    {
        if (action == InventorySmartPrincipalAction.ReviewData && data.Reasons.Count > 0)
            return data.Reasons[0];
        if (action == InventorySmartPrincipalAction.PromoteIndividual
            || action == InventorySmartPrincipalAction.KeepPrice
            || action == InventorySmartPrincipalAction.NoSafePromotion
            || action == InventorySmartPrincipalAction.HighlightWithoutDiscount)
            return pricing.Message;
        if (action == InventorySmartPrincipalAction.EvaluateCombo)
            return "Há combinação segura para ajudar o giro.";
        if (combo is { Suggestions.Count: 0, RejectionReasons.Count: > 0 }
            && action != InventorySmartPrincipalAction.EvaluateCombo)
        {
            return InventorySmartPresentation.RejectionLabel(combo.RejectionReasons[0]);
        }

        if (guidance is not null)
            return InventoryPurchaseGuidancePresentation.ShortExplanation(
                guidance.PrimaryReason, guidance.Action, guidance.Confidence);
        if (attention is not null)
            return InventoryAttentionPresentation.ReasonExplanation(attention.PrimaryReason);
        return "Acompanhe este produto.";
    }

    static string First(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a))
            return a.Trim();
        return (b ?? "").Trim();
    }

    static double? FiniteOrNull(double? value) =>
        value is double n && InventoryIntelligenceEngine.IsFinite(n) ? n : null;
}
