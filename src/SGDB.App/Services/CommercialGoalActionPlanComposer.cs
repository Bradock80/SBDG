using SGDB.Domain.Commercial;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Compositor puro 71B-B7. Consome 70E/70F/70G/71A + snapshot da meta.
/// Sem I/O, SQL, recálculo de giro/piso/combo/reposição ou causalidade financeira.
/// </summary>
public static class CommercialGoalActionPlanComposer
{
    public const int OwnQueryCount = CommercialGoalActionPlanSnapshot.OwnQueryCount;
    public const int MaxActions = CommercialGoalActionPlanSnapshot.MaxActions;

    public static bool ShouldSkipIntelligence(CommercialGoalSnapshot goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return goal.Progress?.Status == CommercialGoalStatus.NotStarted
            || goal.Progress?.PeriodState == CommercialGoalPeriodState.Future;
    }

    public static CommercialGoalActionPlanSnapshot Compose(
        CommercialGoalSnapshot goal,
        CommercialGoalActionPlanSources? sources = null)
    {
        ArgumentNullException.ThrowIfNull(goal);
        sources ??= new CommercialGoalActionPlanSources();

        var mode = ResolveMode(goal);
        var planLimitations = ResolvePlanLimitations(goal);

        if (mode == CommercialGoalActionPlanMode.FutureCompetence)
        {
            return Finish(goal, mode, planLimitations, [], candidateCount: 0, sources.QueryCount);
        }

        var items = BuildCandidates(goal, sources);
        var ranked = Rank(items, BoostCommercial(goal, mode));
        var top = Take(ranked, MaxActions);
        return Finish(goal, mode, planLimitations, top, items.Count, sources.QueryCount);
    }

    static CommercialGoalActionPlanMode ResolveMode(CommercialGoalSnapshot goal)
    {
        if (ShouldSkipIntelligence(goal))
            return CommercialGoalActionPlanMode.FutureCompetence;

        if (goal.ProgressSkipReason.HasFlag(CommercialGoalProgressSkipReason.GrossProfitUnavailable)
            || goal.ProgressSkipReason.HasFlag(CommercialGoalProgressSkipReason.InvalidGoalConfiguration)
            || goal.Status is null or CommercialGoalStatus.NoGoal or CommercialGoalStatus.InvalidGoal
            || !goal.HasValidGoal)
        {
            return CommercialGoalActionPlanMode.InventoryOnly;
        }

        return CommercialGoalActionPlanMode.Operational;
    }

    static CommercialGoalActionLimitation ResolvePlanLimitations(CommercialGoalSnapshot goal)
    {
        var flags = CommercialGoalActionLimitation.None;
        if (goal.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate)
            || goal.FinancialQuality == CommercialGoalCostQuality.EstimatedLegacy)
        {
            flags |= CommercialGoalActionLimitation.LegacyCostEstimate;
        }

        if (goal.ProgressSkipReason.HasFlag(CommercialGoalProgressSkipReason.GrossProfitUnavailable)
            || goal.FinancialQuality == CommercialGoalCostQuality.Unavailable
            || !goal.GrossProfitAvailable)
        {
            flags |= CommercialGoalActionLimitation.FinancialUnavailable;
        }

        return flags;
    }

    static List<CommercialGoalActionItem> BuildCandidates(
        CommercialGoalSnapshot goal,
        CommercialGoalActionPlanSources sources)
    {
        var attentionById = sources.Attention?.ByProductId
            ?? new Dictionary<int, InventoryAttentionResult>();
        var promotionById = sources.Promotion?.ByProductId
            ?? new Dictionary<int, InventoryPromotionSuggestionRow>();
        var guidanceById = sources.Guidance?.ByProductId
            ?? new Dictionary<int, InventoryPurchaseGuidanceResult>();
        var comboById = sources.Combos?.ByProductId
            ?? new Dictionary<int, InventoryComboTargetSuggestionGroup>();
        var titles = sources.Combos?.ProductTitles
            ?? new Dictionary<int, InventoryComboProductTitle>();
        var turnoverById = IndexTurnover(sources.Intelligence);

        var productIds = CollectProductIds(
            sources.Intelligence,
            sources.Attention,
            attentionById,
            promotionById,
            guidanceById,
            comboById);

        var items = new List<CommercialGoalActionItem>(productIds.Count);
        foreach (var productId in productIds)
        {
            attentionById.TryGetValue(productId, out var attention);
            promotionById.TryGetValue(productId, out var promotionRow);
            guidanceById.TryGetValue(productId, out var guidance);
            comboById.TryGetValue(productId, out var combo);
            turnoverById.TryGetValue(productId, out var turnover);
            titles.TryGetValue(productId, out var title);

            var item = TryBuildItem(
                productId,
                attention,
                promotionRow?.Suggestion,
                guidance,
                combo,
                turnover,
                title,
                goal);
            if (item is not null)
                items.Add(item);
        }

        return items;
    }

    static CommercialGoalActionItem? TryBuildItem(
        int productId,
        InventoryAttentionResult? attention,
        InventoryPromotionSuggestionResult? promotion,
        InventoryPurchaseGuidanceResult? guidance,
        InventoryComboTargetSuggestionGroup? combo,
        ProductTurnoverRow? turnover,
        InventoryComboProductTitle? title,
        CommercialGoalSnapshot goal)
    {
        var actionType = ResolveActionType(attention, guidance);
        if (actionType is null)
            return null;

        var suppressCommercial = MustSuppressCommercial(actionType.Value, guidance);
        var hasPromotion = !suppressCommercial && IsSafePromotion(promotion);
        var comboCount = !suppressCommercial && IsSafeCombo(combo)
            ? combo!.Suggestions.Count
            : 0;
        var hasCombo = comboCount > 0;

        var confidence = ResolveConfidence(
            attention,
            guidance,
            actionType.Value,
            hasPromotion ? promotion : null,
            hasCombo ? combo : null);

        var limitations = ResolveItemLimitations(attention, promotion, guidance, combo, goal);
        var origin = ResolveOrigin(actionType.Value, attention, guidance);
        var sources = ResolveSources(
            attention,
            guidance,
            hasPromotion,
            hasCombo,
            origin);
        var priority = ResolvePriority(actionType.Value, attention, guidance);

        return new CommercialGoalActionItem
        {
            ProductId = productId,
            ProductCode = FirstNonEmpty(turnover?.Code, title?.Code),
            ProductName = FirstNonEmpty(turnover?.Name, title?.Name),
            ActionType = actionType.Value,
            AttentionReason = attention?.PrimaryReason ?? InventoryAttentionReason.None,
            Priority = priority,
            Confidence = confidence,
            Source = origin,
            Sources = sources,
            CurrentStock = FiniteOrNull(turnover?.TotalStock),
            CoverageDays = FiniteOrNull(turnover?.CoverageDays),
            ProjectedExcess = attention?.ProjectedExcessQuantity,
            ProjectedExpirySurplus = attention?.ProjectedExpirySurplusQuantity,
            DaysWithoutSale = turnover?.DaysWithoutSale,
            NearestDatedDaysUntilExpiry = attention?.NearestDatedDaysUntilExpiry,
            HasPromotionSuggestion = hasPromotion,
            HasComboSuggestion = hasCombo,
            ComboSuggestionCount = comboCount,
            PurchaseGuidanceAction = guidance?.Action ?? InventoryPurchaseGuidanceAction.None,
            Limitations = limitations,
        };
    }

    static CommercialGoalActionType? ResolveActionType(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance)
    {
        if (guidance?.Action == InventoryPurchaseGuidanceAction.ReviewData)
            return CommercialGoalActionType.ReviewData;

        var fromAttention = ActionFromAttention(attention);
        var replenish = guidance?.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment;

        if (replenish
            && fromAttention is CommercialGoalActionType.PrioritizeExcess
                or CommercialGoalActionType.PrioritizeIdle
                or CommercialGoalActionType.PrioritizeExpiryRisk
                or CommercialGoalActionType.RemoveExpired)
        {
            return CommercialGoalActionType.ReviewData;
        }

        if (fromAttention is not null)
            return fromAttention;

        if (replenish)
            return CommercialGoalActionType.ProtectAvailability;

        return null;
    }

    static CommercialGoalActionType? ActionFromAttention(InventoryAttentionResult? attention)
    {
        if (attention is null)
            return null;

        return attention.Action switch
        {
            InventoryOperatorAction.ReviewData => CommercialGoalActionType.ReviewData,
            InventoryOperatorAction.RemoveExpired => CommercialGoalActionType.RemoveExpired,
            InventoryOperatorAction.PrioritizeSale => CommercialGoalActionType.PrioritizeExpiryRisk,
            InventoryOperatorAction.EvaluateExcess => CommercialGoalActionType.PrioritizeExcess,
            InventoryOperatorAction.Monitor when attention.PrimaryReason == InventoryAttentionReason.Idle
                || attention.Family == InventoryAttentionFamily.Turnover =>
                CommercialGoalActionType.PrioritizeIdle,
            InventoryOperatorAction.Monitor => CommercialGoalActionType.Monitor,
            _ => null,
        };
    }

    static bool MustSuppressCommercial(
        CommercialGoalActionType actionType,
        InventoryPurchaseGuidanceResult? guidance) =>
        actionType is CommercialGoalActionType.ReviewData
            or CommercialGoalActionType.RemoveExpired
            or CommercialGoalActionType.ProtectAvailability
        || guidance?.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment;

    static bool IsSafePromotion(InventoryPromotionSuggestionResult? promotion) =>
        promotion is
        {
            Status: InventoryPromotionSuggestionStatus.Suggested,
            Action: InventoryPromotionSuggestionAction.ConsiderPromotion,
        };

    static bool IsSafeCombo(InventoryComboTargetSuggestionGroup? combo) =>
        combo is { Eligibility.Status: ComboEligibilityStatus.Eligible }
        && combo.Suggestions.Count > 0;

    static InventoryAttentionConfidence ResolveConfidence(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance,
        CommercialGoalActionType actionType,
        InventoryPromotionSuggestionResult? promotion,
        InventoryComboTargetSuggestionGroup? combo)
    {
        var confidence = attention?.Confidence ?? InventoryAttentionConfidence.Unavailable;

        if (actionType == CommercialGoalActionType.ProtectAvailability
            || actionType == CommercialGoalActionType.ReviewData && guidance is not null)
        {
            confidence = Weaker(confidence, guidance?.Confidence ?? InventoryAttentionConfidence.Unavailable);
        }

        if (promotion is not null)
            confidence = Weaker(confidence, promotion.Confidence);

        if (combo is not null)
        {
            foreach (var suggestion in combo.Suggestions)
                confidence = Weaker(confidence, suggestion.Confidence);
        }

        return confidence;
    }

    static InventoryAttentionConfidence Weaker(
        InventoryAttentionConfidence left,
        InventoryAttentionConfidence right) =>
        left >= right ? left : right;

    static CommercialGoalActionLimitation ResolveItemLimitations(
        InventoryAttentionResult? attention,
        InventoryPromotionSuggestionResult? promotion,
        InventoryPurchaseGuidanceResult? guidance,
        InventoryComboTargetSuggestionGroup? combo,
        CommercialGoalSnapshot goal)
    {
        var flags = CommercialGoalActionLimitation.None;

        if (HasAttentionReason(attention, InventoryAttentionReason.InsufficientHistory)
            || HasGuidanceReason(guidance, InventoryPurchaseGuidanceReason.InsufficientHistory))
        {
            flags |= CommercialGoalActionLimitation.InsufficientHistory;
        }

        if (HasAttentionReason(attention, InventoryAttentionReason.NoPhysicalEvidence)
            || HasGuidanceReason(guidance, InventoryPurchaseGuidanceReason.NoPhysicalEvidence))
        {
            flags |= CommercialGoalActionLimitation.NoPhysicalEvidence;
        }

        if (IsStructuralAttention(attention)
            || HasGuidanceReason(guidance, InventoryPurchaseGuidanceReason.StructuralDataIssue)
            || promotion?.PrimaryReason is InventoryPromotionSuggestionReason.DuplicateScenario
                or InventoryPromotionSuggestionReason.ScenarioMissing)
        {
            flags |= CommercialGoalActionLimitation.StructuralDataIssue;
        }

        if (HasGuidanceReason(guidance, InventoryPurchaseGuidanceReason.LocationLimitation)
            || promotion?.PrimaryReason == InventoryPromotionSuggestionReason.LocationLimitation
            || promotion?.SecondaryReasons.Contains(InventoryPromotionSuggestionReason.LocationLimitation) == true)
        {
            flags |= CommercialGoalActionLimitation.LocationLimitation;
        }

        if (goal.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate)
            || goal.FinancialQuality == CommercialGoalCostQuality.EstimatedLegacy)
        {
            flags |= CommercialGoalActionLimitation.LegacyCostEstimate;
        }

        if (goal.ProgressSkipReason.HasFlag(CommercialGoalProgressSkipReason.GrossProfitUnavailable)
            || goal.FinancialQuality == CommercialGoalCostQuality.Unavailable)
        {
            flags |= CommercialGoalActionLimitation.FinancialUnavailable;
        }

        if (combo is not null)
        {
            foreach (var suggestion in combo.Suggestions)
            {
                foreach (var limitation in suggestion.Limitations)
                {
                    if (limitation == InventoryComboSuggestionLimitation.InsufficientPairHistory)
                        flags |= CommercialGoalActionLimitation.InsufficientHistory;
                }
            }
        }

        return flags;
    }

    static bool HasAttentionReason(InventoryAttentionResult? attention, InventoryAttentionReason reason)
    {
        if (attention is null)
            return false;
        if (attention.PrimaryReason == reason)
            return true;
        foreach (var secondary in attention.SecondaryReasons)
        {
            if (secondary == reason)
                return true;
        }

        return false;
    }

    static bool HasGuidanceReason(
        InventoryPurchaseGuidanceResult? guidance,
        InventoryPurchaseGuidanceReason reason)
    {
        if (guidance is null)
            return false;
        if (guidance.PrimaryReason == reason)
            return true;
        foreach (var secondary in guidance.SecondaryReasons)
        {
            if (secondary == reason)
                return true;
        }

        return false;
    }

    static bool IsStructuralAttention(InventoryAttentionResult? attention)
    {
        if (attention is null)
            return false;
        return attention.Action == InventoryOperatorAction.ReviewData
            && attention.Family == InventoryAttentionFamily.DataQuality
            && attention.PrimaryReason is InventoryAttentionReason.InvalidInput
                or InventoryAttentionReason.NegativeStock
                or InventoryAttentionReason.NegativeLocationStock
                or InventoryAttentionReason.NegativeWarehouseStock
                or InventoryAttentionReason.InconsistentStockTotals
                or InventoryAttentionReason.TrackedQuantityExceedsWarehouse
                or InventoryAttentionReason.DuplicateLotId
                or InventoryAttentionReason.InvalidLotQuantity
                or InventoryAttentionReason.InvalidExpiryDate
                or InventoryAttentionReason.ProjectionMissing
                or InventoryAttentionReason.DuplicateProjection;
    }

    static CommercialGoalActionOrigin ResolveOrigin(
        CommercialGoalActionType actionType,
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance)
    {
        if (actionType == CommercialGoalActionType.ProtectAvailability)
            return CommercialGoalActionOrigin.PurchaseGuidance;

        if (actionType == CommercialGoalActionType.ReviewData
            && attention?.Action != InventoryOperatorAction.ReviewData
            && guidance?.Action == InventoryPurchaseGuidanceAction.ReviewData)
        {
            return CommercialGoalActionOrigin.PurchaseGuidance;
        }

        return CommercialGoalActionOrigin.InventoryAttention;
    }

    static CommercialGoalActionSource ResolveSources(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance,
        bool hasPromotion,
        bool hasCombo,
        CommercialGoalActionOrigin origin)
    {
        var flags = CommercialGoalActionSource.None;
        if (origin == CommercialGoalActionOrigin.InventoryAttention || attention is not null)
            flags |= CommercialGoalActionSource.InventoryAttention;
        if (origin == CommercialGoalActionOrigin.PurchaseGuidance
            || guidance is { Action: not InventoryPurchaseGuidanceAction.None })
        {
            flags |= CommercialGoalActionSource.PurchaseGuidance;
        }

        if (hasPromotion)
            flags |= CommercialGoalActionSource.PromotionSuggestion;
        if (hasCombo)
            flags |= CommercialGoalActionSource.SmartCombo;
        return flags;
    }

    static InventoryAttentionPriority ResolvePriority(
        CommercialGoalActionType actionType,
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance)
    {
        if (attention is not null
            && attention.Priority != InventoryAttentionPriority.Normal)
        {
            return attention.Priority;
        }

        if (actionType == CommercialGoalActionType.ProtectAvailability)
        {
            return guidance?.PrimaryReason is InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand
                or InventoryPurchaseGuidanceReason.CriticalCoverage
                ? InventoryAttentionPriority.High
                : InventoryAttentionPriority.Medium;
        }

        if (actionType == CommercialGoalActionType.ReviewData)
            return InventoryAttentionPriority.Critical;

        return attention?.Priority ?? InventoryAttentionPriority.Normal;
    }

    static bool BoostCommercial(CommercialGoalSnapshot goal, CommercialGoalActionPlanMode mode) =>
        mode == CommercialGoalActionPlanMode.Operational
        && goal.Status == CommercialGoalStatus.BelowPace;

    static List<CommercialGoalActionItem> Rank(
        List<CommercialGoalActionItem> items,
        bool boostCommercial)
    {
        items.Sort((left, right) => CompareItems(left, right, boostCommercial));
        return items;
    }

    static int CompareItems(
        CommercialGoalActionItem left,
        CommercialGoalActionItem right,
        bool boostCommercial)
    {
        var gravity = left.ActionType.CompareTo(right.ActionType);
        if (gravity != 0)
            return gravity;

        var priority = left.Priority.CompareTo(right.Priority);
        if (priority != 0)
            return priority;

        var expiry = CompareNullableIntAscending(
            left.NearestDatedDaysUntilExpiry,
            right.NearestDatedDaysUntilExpiry);
        if (expiry != 0)
            return expiry;

        var expirySurplus = CompareNullableDoubleDescending(
            left.ProjectedExpirySurplus,
            right.ProjectedExpirySurplus);
        if (expirySurplus != 0)
            return expirySurplus;

        var excess = CompareNullableDoubleDescending(left.ProjectedExcess, right.ProjectedExcess);
        if (excess != 0)
            return excess;

        var idle = CompareNullableIntDescending(left.DaysWithoutSale, right.DaysWithoutSale);
        if (idle != 0)
            return idle;

        if (boostCommercial)
        {
            var commercial = CommercialSignal(right).CompareTo(CommercialSignal(left));
            if (commercial != 0)
                return commercial;
        }

        var confidence = left.Confidence.CompareTo(right.Confidence);
        if (confidence != 0)
            return confidence;

        return left.ProductId.CompareTo(right.ProductId);
    }

    static int CommercialSignal(CommercialGoalActionItem item)
    {
        var signal = 0;
        if (item.HasPromotionSuggestion)
            signal += 2;
        if (item.HasComboSuggestion)
            signal += 1;
        return signal;
    }

    static int CompareNullableIntAscending(int? left, int? right)
    {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;
        return left.Value.CompareTo(right.Value);
    }

    static int CompareNullableIntDescending(int? left, int? right)
    {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;
        return right.Value.CompareTo(left.Value);
    }

    static int CompareNullableDoubleDescending(double? left, double? right)
    {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;
        return right.Value.CompareTo(left.Value);
    }

    static List<CommercialGoalActionItem> Take(
        List<CommercialGoalActionItem> ranked,
        int max)
    {
        if (ranked.Count <= max)
            return ranked;
        return ranked.GetRange(0, max);
    }

    static CommercialGoalActionPlanSnapshot Finish(
        CommercialGoalSnapshot goal,
        CommercialGoalActionPlanMode mode,
        CommercialGoalActionLimitation limitations,
        IReadOnlyList<CommercialGoalActionItem> items,
        int candidateCount,
        int inheritedQueryCount) =>
        new()
        {
            Competence = goal.Competence,
            ReferenceDate = goal.ReferenceDate,
            GoalStatus = goal.Status,
            FinancialQuality = goal.FinancialQuality,
            ProgressSkipReason = goal.ProgressSkipReason,
            HasValidGoal = goal.HasValidGoal,
            Mode = mode,
            Items = items,
            Limitations = limitations,
            CandidateCount = candidateCount,
            QueryCount = inheritedQueryCount,
        };

    static Dictionary<int, ProductTurnoverRow> IndexTurnover(
        InventoryIntelligenceSnapshot? intelligence)
    {
        var rows = intelligence?.Rows ?? [];
        var map = new Dictionary<int, ProductTurnoverRow>(rows.Count);
        foreach (var row in rows)
            map.TryAdd(row.ProductId, row);
        return map;
    }

    static List<int> CollectProductIds(
        InventoryIntelligenceSnapshot? intelligence,
        InventoryAttentionSnapshot? attention,
        IReadOnlyDictionary<int, InventoryAttentionResult> attentionById,
        IReadOnlyDictionary<int, InventoryPromotionSuggestionRow> promotionById,
        IReadOnlyDictionary<int, InventoryPurchaseGuidanceResult> guidanceById,
        IReadOnlyDictionary<int, InventoryComboTargetSuggestionGroup> comboById)
    {
        var ids = new List<int>();
        var seen = new HashSet<int>();

        foreach (var row in intelligence?.Rows ?? [])
            Add(row.ProductId);
        foreach (var result in attention?.Results ?? [])
            Add(result.ProductId);
        foreach (var id in attentionById.Keys)
            Add(id);
        foreach (var id in promotionById.Keys)
            Add(id);
        foreach (var id in guidanceById.Keys)
            Add(id);
        foreach (var id in comboById.Keys)
            Add(id);

        return ids;

        void Add(int productId)
        {
            if (seen.Add(productId))
                ids.Add(productId);
        }
    }

    static string FirstNonEmpty(string? left, string? right)
    {
        if (!string.IsNullOrWhiteSpace(left))
            return left;
        if (!string.IsNullOrWhiteSpace(right))
            return right;
        return "";
    }

    static double? FiniteOrNull(double? value)
    {
        if (value is not double number)
            return null;
        return double.IsFinite(number) ? number : null;
    }
}
