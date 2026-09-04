using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 71A-B4: filtra, ordena e limita Top 3 âncoras por alvo.
/// Consome B1/B2/B3/70C prontos. Sem SQL, recálculo, UI ou ranking por margem.
/// QueryCount = 0.
/// </summary>
public static class InventoryComboSuggestionEngine
{
    public const int ExpectedQueryCount = 0;
    public const int MaxSuggestionsPerTarget = 3;

    public static InventoryComboSuggestionSnapshot BuildForTarget(
        InventoryComboTargetEligibility? target,
        IReadOnlyList<InventoryComboCandidate>? candidates)
    {
        if (!IsEligibleTarget(target))
            return Empty(target?.ProductId ?? 0);

        var unique = CollectUnique(target!, candidates);
        var ordered = SelectTop(unique);
        var rows = new List<InventoryComboSuggestion>(ordered.Count);
        foreach (var item in ordered)
            rows.Add(ToSuggestion(target!, item));

        return new InventoryComboSuggestionSnapshot
        {
            QueryCount = ExpectedQueryCount,
            TargetProductId = target!.ProductId,
            Rows = rows,
        };
    }

    static bool IsEligibleTarget(InventoryComboTargetEligibility? target)
    {
        if (target is null)
            return false;
        if (target.Status != ComboEligibilityStatus.Eligible)
            return false;
        if (target.Confidence == InventoryAttentionConfidence.Unavailable)
            return false;
        if (target.Reason is ComboTargetEligibilityReason.TargetExpired
            or ComboTargetEligibilityReason.TargetExpiresToday)
            return false;
        return target.Reason is ComboTargetEligibilityReason.ExpirySurplus
            or ComboTargetEligibilityReason.ProjectedExcess
            or ComboTargetEligibilityReason.Idle;
    }

    static List<RankedCandidate> CollectUnique(
        InventoryComboTargetEligibility target,
        IReadOnlyList<InventoryComboCandidate>? candidates)
    {
        var unique = new List<RankedCandidate>();
        if (candidates is null || candidates.Count == 0)
            return unique;

        var groups = new Dictionary<int, List<(InventoryComboCandidate Candidate, int Index)>>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate is null)
                continue;
            var anchorId = candidate.AnchorEligibility?.ProductId
                ?? candidate.PairEvidenceFacts?.AnchorProductId
                ?? 0;
            if (anchorId <= 0)
                continue;
            if (!groups.TryGetValue(anchorId, out var list))
            {
                list = [];
                groups[anchorId] = list;
            }

            list.Add((candidate, i));
        }

        foreach (var list in groups.Values)
        {
            var ordered = list.OrderBy(x => x.Index).ToList();
            var fingerprint = CandidateFingerprintOf(ordered[0].Candidate);
            var identical = true;
            for (var i = 1; i < ordered.Count; i++)
            {
                if (CandidateFingerprintOf(ordered[i].Candidate) != fingerprint)
                {
                    identical = false;
                    break;
                }
            }

            if (!identical)
                continue;
            if (TryAccept(target, ordered[0].Candidate, ordered[0].Index, out var ranked))
                unique.Add(ranked);
        }

        return unique;
    }

    static bool TryAccept(
        InventoryComboTargetEligibility target,
        InventoryComboCandidate? candidate,
        int index,
        out RankedCandidate ranked)
    {
        ranked = default;
        if (candidate is null)
            return false;

        var anchor = candidate.AnchorEligibility;
        var pair = candidate.PairEvidenceFacts;
        var financial = candidate.FinancialFacts;
        var targetFacts = candidate.TargetFacts;
        var anchorFacts = candidate.AnchorFacts;
        if (anchor is null
            || pair is null
            || financial is null
            || targetFacts is null
            || anchorFacts is null)
            return false;

        if (candidate.TargetEligibility is { } innerTarget
            && (innerTarget.ProductId != target.ProductId
                || innerTarget.Status != target.Status
                || innerTarget.Reason != target.Reason
                || innerTarget.Confidence != target.Confidence))
            return false;

        if (anchor.Status != ComboEligibilityStatus.Eligible)
            return false;
        if (anchor.Confidence == InventoryAttentionConfidence.Unavailable)
            return false;
        if (anchor.ProductId == target.ProductId)
            return false;
        if (pair.TargetProductId != target.ProductId || pair.AnchorProductId != anchor.ProductId)
            return false;
        if (targetFacts.ProductId != target.ProductId || anchorFacts.ProductId != anchor.ProductId)
            return false;

        if (pair.Evidence is InventoryComboPairEvidence.NoneObserved
            or InventoryComboPairEvidence.InvalidCounts)
            return false;
        if (pair.Evidence is not (InventoryComboPairEvidence.Observed
            or InventoryComboPairEvidence.Weak
            or InventoryComboPairEvidence.InsufficientHistory))
            return false;

        if (pair.PairTransactions < 0
            || pair.TargetTransactions < 0
            || pair.PairTransactions > pair.TargetTransactions)
            return false;
        if (pair.ConfidenceTargetToAnchor is double pairConfidence)
        {
            if (!InventoryIntelligenceEngine.IsFinite(pairConfidence)
                || pairConfidence < 0
                || pairConfidence > 1)
                return false;
        }
        else if (pair.Evidence != InventoryComboPairEvidence.InsufficientHistory)
        {
            return false;
        }

        if (financial.Status != InventoryComboPairFinancialStatus.Available)
            return false;
        if (financial.NormalPairPrice is not double normal
            || financial.PairCost is not double cost
            || financial.PairFloorPrice is not double floor
            || !InventoryIntelligenceEngine.IsFinite(normal)
            || !InventoryIntelligenceEngine.IsFinite(cost)
            || !InventoryIntelligenceEngine.IsFinite(floor)
            || normal <= 0
            || cost < 0
            || floor <= 0)
            return false;
        if (!HasCurrentPrices(financial, out var currentPrice))
            return false;
        if (InventoryCommercialPriceFloorEngine.ToCents(currentPrice)
            < InventoryCommercialPriceFloorEngine.ToCents(floor))
            return false;
        if (!ScenariosRespectFloor(financial.Scenarios, floor))
            return false;

        if (!InventoryIntelligenceEngine.IsFinite(targetFacts.TotalStock)
            || !InventoryIntelligenceEngine.IsFinite(anchorFacts.TotalStock)
            || !InventoryIntelligenceEngine.IsFinite(anchorFacts.Vmv30))
            return false;
        if (anchorFacts.CoverageDays is double coverage
            && !InventoryIntelligenceEngine.IsFinite(coverage))
            return false;

        ranked = new RankedCandidate
        {
            Index = index,
            Anchor = anchor,
            Pair = pair,
            Financial = financial,
            TargetFacts = targetFacts,
            AnchorFacts = anchorFacts,
        };
        return true;
    }

    static List<RankedCandidate> SelectTop(List<RankedCandidate> unique)
    {
        var observed = SortBucket(unique, InventoryComboPairEvidence.Observed);
        var weak = SortBucket(unique, InventoryComboPairEvidence.Weak);
        var insufficient = SortBucket(unique, InventoryComboPairEvidence.InsufficientHistory);
        var selected = new List<RankedCandidate>(MaxSuggestionsPerTarget);
        Append(selected, observed);
        Append(selected, weak);
        Append(selected, insufficient);
        return selected;
    }

    static List<RankedCandidate> SortBucket(
        List<RankedCandidate> source,
        InventoryComboPairEvidence evidence) =>
        source
            .Where(x => x.Pair.Evidence == evidence)
            .OrderByDescending(x => x.Pair.PairTransactions)
            .ThenBy(x => x.Pair.ConfidenceTargetToAnchor, NullableDesc)
            .ThenBy(x => x.AnchorFacts.CoverageDays, NullableDesc)
            .ThenByDescending(x => x.AnchorFacts.Vmv30)
            .ThenBy(x => x.Anchor.ProductId)
            .ThenBy(x => x.Index)
            .ToList();

    static readonly IComparer<double?> NullableDesc = Comparer<double?>.Create((left, right) =>
    {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;
        return right.Value.CompareTo(left.Value);
    });

    static void Append(List<RankedCandidate> selected, List<RankedCandidate> bucket)
    {
        foreach (var item in bucket)
        {
            if (selected.Count >= MaxSuggestionsPerTarget)
                return;
            selected.Add(item);
        }
    }

    static InventoryComboSuggestion ToSuggestion(
        InventoryComboTargetEligibility target,
        RankedCandidate item)
    {
        var limitations = new List<InventoryComboSuggestionLimitation>(4);
        if (item.Pair.Evidence == InventoryComboPairEvidence.Weak)
            limitations.Add(InventoryComboSuggestionLimitation.WeakPairEvidence);
        if (item.Pair.Evidence == InventoryComboPairEvidence.InsufficientHistory)
            limitations.Add(InventoryComboSuggestionLimitation.InsufficientPairHistory);
        if (target.Confidence == InventoryAttentionConfidence.Limited)
            limitations.Add(InventoryComboSuggestionLimitation.TargetLimitedConfidence);
        if (item.Anchor.Confidence == InventoryAttentionConfidence.Limited)
            limitations.Add(InventoryComboSuggestionLimitation.AnchorLimitedConfidence);
        if (item.AnchorFacts.CoverageDays is null)
            limitations.Add(InventoryComboSuggestionLimitation.OtherDataLimitation);

        var confidence = InventoryAttentionConfidence.Reliable;
        if (limitations.Count > 0
            || item.Pair.Evidence != InventoryComboPairEvidence.Observed
            || target.Confidence != InventoryAttentionConfidence.Reliable
            || item.Anchor.Confidence != InventoryAttentionConfidence.Reliable)
        {
            confidence = InventoryAttentionConfidence.Limited;
        }

        return new InventoryComboSuggestion
        {
            TargetProductId = target.ProductId,
            AnchorProductId = item.Anchor.ProductId,
            TargetReason = target.Reason,
            AnchorReason = item.Anchor.Reason,
            PairEvidence = item.Pair.Evidence,
            NormalPairPrice = item.Financial.NormalPairPrice!.Value,
            PairCost = item.Financial.PairCost!.Value,
            PairFloorPrice = item.Financial.PairFloorPrice!.Value,
            Scenarios = item.Financial.Scenarios,
            TargetStock = item.TargetFacts.TotalStock,
            AnchorStock = item.AnchorFacts.TotalStock,
            AnchorCoverageDays = item.AnchorFacts.CoverageDays,
            PairTransactions = item.Pair.PairTransactions,
            TargetTransactions = item.Pair.TargetTransactions,
            ConfidenceTargetToAnchor = item.Pair.ConfidenceTargetToAnchor,
            Confidence = confidence,
            Limitations = limitations,
        };
    }

    static bool HasCurrentPrices(
        InventoryComboPairFinancialFacts financial,
        out double currentPrice)
    {
        currentPrice = 0;
        foreach (var scenario in financial.Scenarios)
        {
            if (scenario.Kind != InventoryComboPairFinancialScenarioKind.CurrentPrices)
                continue;
            if (!InventoryIntelligenceEngine.IsFinite(scenario.PairPrice) || scenario.PairPrice <= 0)
                return false;
            currentPrice = scenario.PairPrice;
            return true;
        }

        return false;
    }

    static bool ScenariosRespectFloor(
        IReadOnlyList<InventoryComboPairFinancialScenario> scenarios,
        double floor)
    {
        var floorCents = InventoryCommercialPriceFloorEngine.ToCents(floor);
        foreach (var scenario in scenarios)
        {
            if (!InventoryIntelligenceEngine.IsFinite(scenario.PairPrice)
                || !InventoryIntelligenceEngine.IsFinite(scenario.GrossProfit)
                || !InventoryIntelligenceEngine.IsFinite(scenario.GrossMargin)
                || !InventoryIntelligenceEngine.IsFinite(scenario.ReductionFromCurrent))
                return false;
            if (InventoryCommercialPriceFloorEngine.ToCents(scenario.PairPrice) < floorCents)
                return false;
        }

        return true;
    }

    static CandidateFingerprint CandidateFingerprintOf(InventoryComboCandidate candidate)
    {
        var target = candidate.TargetEligibility;
        var anchor = candidate.AnchorEligibility;
        var pair = candidate.PairEvidenceFacts;
        var financial = candidate.FinancialFacts;
        var targetFacts = candidate.TargetFacts;
        var anchorFacts = candidate.AnchorFacts;
        return new CandidateFingerprint(
            target?.ProductId,
            target?.Status,
            target?.Reason,
            target?.Confidence,
            anchor?.ProductId,
            anchor?.Status,
            anchor?.Reason,
            anchor?.Confidence,
            pair?.TargetProductId,
            pair?.AnchorProductId,
            pair?.Evidence,
            pair?.PairTransactions,
            pair?.TargetTransactions,
            pair?.ConfidenceTargetToAnchor,
            financial?.Status,
            financial?.NormalPairPrice,
            financial?.PairCost,
            financial?.PairFloorPrice,
            ScenarioKey(financial?.Scenarios),
            targetFacts?.ProductId,
            targetFacts?.TotalStock,
            anchorFacts?.ProductId,
            anchorFacts?.CoverageDays,
            anchorFacts?.Vmv30,
            anchorFacts?.TotalStock);
    }

    static string ScenarioKey(IReadOnlyList<InventoryComboPairFinancialScenario>? scenarios)
    {
        if (scenarios is null || scenarios.Count == 0)
            return "";
        var parts = new string[scenarios.Count];
        for (var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            parts[i] = $"{(int)s.Kind}:{s.PairPrice}:{s.GrossProfit}:{s.GrossMargin}:{s.ReductionFromCurrent}";
        }

        return string.Join("|", parts);
    }

    static InventoryComboSuggestionSnapshot Empty(int targetProductId) =>
        new()
        {
            QueryCount = ExpectedQueryCount,
            TargetProductId = targetProductId,
        };

    readonly struct RankedCandidate
    {
        public int Index { get; init; }
        public InventoryComboAnchorEligibility Anchor { get; init; }
        public InventoryComboPairCoOccurrenceFacts Pair { get; init; }
        public InventoryComboPairFinancialFacts Financial { get; init; }
        public ProductTurnoverRow TargetFacts { get; init; }
        public ProductTurnoverRow AnchorFacts { get; init; }
    }

    readonly record struct CandidateFingerprint(
        int? TargetId,
        ComboEligibilityStatus? TargetStatus,
        ComboTargetEligibilityReason? TargetReason,
        InventoryAttentionConfidence? TargetConfidence,
        int? AnchorId,
        ComboEligibilityStatus? AnchorStatus,
        ComboAnchorEligibilityReason? AnchorReason,
        InventoryAttentionConfidence? AnchorConfidence,
        int? PairTargetId,
        int? PairAnchorId,
        InventoryComboPairEvidence? Evidence,
        int? PairTransactions,
        int? TargetTransactions,
        double? PairConfidence,
        InventoryComboPairFinancialStatus? FinancialStatus,
        double? NormalPairPrice,
        double? PairCost,
        double? PairFloorPrice,
        string ScenarioKey,
        int? TargetFactsId,
        double? TargetStock,
        int? AnchorFactsId,
        double? CoverageDays,
        double? Vmv30,
        double? AnchorStock);
}
