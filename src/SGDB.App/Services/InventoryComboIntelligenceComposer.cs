using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Carrega coocorrência B2 em lote. O composer chama no máximo uma vez por Compose.
/// </summary>
public delegate InventoryComboCoOccurrenceSnapshot InventoryComboCoOccurrenceLoader(
    IReadOnlyList<int> targetIds,
    IReadOnlyList<int> anchorIds,
    IReadOnlyDictionary<int, int> targetHistoryDays,
    DateTime today);

/// <summary>
/// Orquestrador 71A-B5: consome 70C–70G, seleciona alvos/âncoras B1,
/// executa B2 uma vez, B3/B4 em memória. Sem regra comercial nova, UI ou persistência.
/// QueryCount = 9 sem B2, 10 com B2. ExpectedQueryCount do composer = 0.
/// </summary>
public static class InventoryComboIntelligenceComposer
{
    public const int ExpectedQueryCount = 0;
    public const int MaxPreselectedAnchorsPerTarget = 20;

    public const int ExpectedBasePipelineQueryCount =
        InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount;

    public const int ExpectedPipelineQueryCount =
        ExpectedBasePipelineQueryCount + InventoryComboCoOccurrenceService.ExpectedQueryCount;

    /// <summary>
    /// SQLite ≥ 3.32 / Microsoft.Data.Sqlite 8: SQLITE_MAX_VARIABLE_NUMBER = 32766.
    /// B2 usa 2 datas + |targets| + |unionAnchors|. 70F já faz IN com o catálogo.
    /// </summary>
    public const int SqliteMaxVariableNumber = 32766;

    static readonly IComparer<double?> CoverageDescending = Comparer<double?>.Create((left, right) =>
    {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;
        return right.Value.CompareTo(left.Value);
    });

    public static InventoryComboIntelligenceSnapshot Compose(
        InventoryComboIntelligenceComposeInput? input) =>
        Compose(input, loadCoOccurrence: null);

    public static InventoryComboIntelligenceSnapshot Compose(
        InventoryIntelligenceSnapshot? intelligence,
        InventoryAttentionSnapshot? attention,
        InventoryCommercialFactsSnapshot? facts,
        InventoryPurchaseGuidanceSnapshot? guidance,
        InventoryCommercialMarginPolicyResolution? policyResolution,
        DateTime? today = null) =>
        Compose(new InventoryComboIntelligenceComposeInput
        {
            Today = today,
            Intelligence = intelligence,
            Attention = attention,
            Facts = facts,
            Guidance = guidance,
            PolicyResolution = policyResolution,
        });

    public static InventoryComboIntelligenceSnapshot Compose(
        InventoryComboIntelligenceComposeInput? input,
        InventoryComboCoOccurrenceLoader? loadCoOccurrence)
    {
        input ??= new InventoryComboIntelligenceComposeInput();
        var intelligence = input.Intelligence ?? new InventoryIntelligenceSnapshot();
        var turnovers = Index(intelligence.Rows, static x => x.ProductId, out var turnoverConflicts);
        var attentions = Index(
            input.Attention?.Results,
            input.Attention?.ByProductId,
            static x => x.ProductId,
            out var attentionConflicts);
        var facts = Index(
            input.Facts?.Rows,
            input.Facts?.ByProductId,
            static x => x.ProductId,
            out var factsConflicts);
        var guidance = Index(
            input.Guidance?.Results,
            input.Guidance?.ByProductId,
            static x => x.ProductId,
            out var guidanceConflicts);
        var policy = InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(
            input.PolicyResolution);
        var today = ResolveToday(input.Today, intelligence.Today);

        var evaluated = 0;
        var eligibleTargets = new List<EligibleTarget>();
        var eligibleAnchors = new List<EligibleAnchor>();

        foreach (var productId in turnovers.Keys.OrderBy(id => id))
        {
            if (turnoverConflicts.Contains(productId))
                continue;

            evaluated++;
            var turnover = turnovers[productId];
            var eligibilityInput = new InventoryComboEligibilityInput
            {
                Turnover = turnover,
                Attention = Resolve(productId, attentions, attentionConflicts),
                Facts = Resolve(productId, facts, factsConflicts),
                Guidance = Resolve(productId, guidance, guidanceConflicts),
            };

            var target = InventoryComboTargetEligibilityEngine.Evaluate(eligibilityInput);
            if (target.Status == ComboEligibilityStatus.Eligible)
            {
                eligibleTargets.Add(new EligibleTarget
                {
                    ProductId = productId,
                    Turnover = turnover,
                    Eligibility = target,
                    Facts = eligibilityInput.Facts,
                });
            }

            var anchor = InventoryComboAnchorEligibilityEngine.Evaluate(eligibilityInput);
            if (anchor.Status == ComboEligibilityStatus.Eligible)
            {
                eligibleAnchors.Add(new EligibleAnchor
                {
                    ProductId = productId,
                    Turnover = turnover,
                    Eligibility = anchor,
                    Facts = eligibilityInput.Facts,
                });
            }
        }

        var sortedAnchors = eligibleAnchors
            .OrderBy(a => a.Turnover.CoverageDays, CoverageDescending)
            .ThenByDescending(a => a.Turnover.Vmv30)
            .ThenBy(a => a.ProductId)
            .ToList();
        var anchorById = new Dictionary<int, EligibleAnchor>(sortedAnchors.Count);
        foreach (var anchor in sortedAnchors)
            anchorById.TryAdd(anchor.ProductId, anchor);

        var preselected = new Dictionary<int, List<int>>(eligibleTargets.Count);
        var pairCount = 0;
        foreach (var target in eligibleTargets)
        {
            var ids = PreselectAnchorIds(target.ProductId, sortedAnchors);
            preselected[target.ProductId] = ids;
            pairCount += ids.Count;
        }

        InventoryComboCoOccurrenceSnapshot? coOccurrence = null;
        var coOccurrenceCalls = 0;
        if (eligibleTargets.Count > 0 && sortedAnchors.Count > 0 && pairCount > 0)
        {
            var targetIds = eligibleTargets.Select(t => t.ProductId).ToList();
            var unionAnchorIds = UnionAnchorIds(preselected);
            var history = new Dictionary<int, int>(targetIds.Count);
            foreach (var target in eligibleTargets)
                history[target.ProductId] = target.Turnover.HistoryDays;

            var loader = loadCoOccurrence ?? DefaultLoadCoOccurrence;
            coOccurrence = loader(targetIds, unionAnchorIds, history, today);
            coOccurrenceCalls = 1;
        }

        var pairMap = IndexPairs(coOccurrence);
        var groups = new List<InventoryComboTargetSuggestionGroup>(eligibleTargets.Count);
        var map = new Dictionary<int, InventoryComboTargetSuggestionGroup>(eligibleTargets.Count);
        var pairCandidates = 0;
        var financialEvals = 0;

        foreach (var target in eligibleTargets)
        {
            var candidates = new List<InventoryComboCandidate>();
            foreach (var anchorId in preselected[target.ProductId])
            {
                if (!anchorById.TryGetValue(anchorId, out var anchor))
                    continue;
                if (!pairMap.TryGetValue((target.ProductId, anchorId), out var evidence))
                    continue;
                if (evidence.Evidence is InventoryComboPairEvidence.NoneObserved
                    or InventoryComboPairEvidence.InvalidCounts)
                    continue;

                financialEvals++;
                var financial = InventoryComboPairFinancialEngine.Evaluate(
                    new InventoryComboPairFinancialInput
                    {
                        TargetFacts = target.Facts,
                        AnchorFacts = anchor.Facts,
                        MinGrossMarginPolicy = policy,
                    });

                candidates.Add(new InventoryComboCandidate
                {
                    TargetEligibility = target.Eligibility,
                    AnchorEligibility = anchor.Eligibility,
                    PairEvidenceFacts = evidence,
                    FinancialFacts = financial,
                    TargetFacts = target.Turnover,
                    AnchorFacts = anchor.Turnover,
                });
            }

            pairCandidates += candidates.Count;
            var suggestion = InventoryComboSuggestionEngine.BuildForTarget(
                target.Eligibility, candidates);
            var group = new InventoryComboTargetSuggestionGroup
            {
                ProductId = target.ProductId,
                Code = target.Turnover.Code ?? "",
                Name = target.Turnover.Name ?? "",
                Eligibility = target.Eligibility,
                Suggestions = suggestion.Rows,
            };
            groups.Add(group);
            map.TryAdd(target.ProductId, group);
        }

        var coOccurrenceQueryCount = coOccurrence?.QueryCount ?? 0;
        var titles = new Dictionary<int, InventoryComboProductTitle>(turnovers.Count);
        foreach (var pair in turnovers)
        {
            titles.TryAdd(pair.Key, new InventoryComboProductTitle
            {
                ProductId = pair.Key,
                Code = pair.Value.Code ?? "",
                Name = pair.Value.Name ?? "",
            });
        }

        return new InventoryComboIntelligenceSnapshot
        {
            QueryCount = ExpectedBasePipelineQueryCount + coOccurrenceQueryCount,
            CoOccurrenceQueryCount = coOccurrenceQueryCount,
            CoOccurrenceCalls = coOccurrenceCalls,
            TargetsEvaluated = evaluated,
            EligibleTargets = eligibleTargets.Count,
            EligibleAnchors = eligibleAnchors.Count,
            PairCandidatesEvaluated = pairCandidates,
            PairFinancialEvaluations = financialEvals,
            RequestedTargetIds = coOccurrence?.RequestedTargetIds ?? [],
            RequestedAnchorIds = coOccurrence?.RequestedAnchorIds ?? [],
            ProductTitles = titles,
            Targets = groups,
            ByProductId = map,
        };
    }

    public static int EstimateCoOccurrenceParameterCount(int targetCount, int unionAnchorCount) =>
        2 + Math.Max(0, targetCount) + Math.Max(0, unionAnchorCount);

    static InventoryComboCoOccurrenceSnapshot DefaultLoadCoOccurrence(
        IReadOnlyList<int> targetIds,
        IReadOnlyList<int> anchorIds,
        IReadOnlyDictionary<int, int> targetHistoryDays,
        DateTime today) =>
        InventoryComboCoOccurrenceService.Load(targetIds, anchorIds, targetHistoryDays, today);

    static List<int> PreselectAnchorIds(int targetProductId, List<EligibleAnchor> sortedAnchors)
    {
        var ids = new List<int>(Math.Min(MaxPreselectedAnchorsPerTarget, sortedAnchors.Count));
        foreach (var anchor in sortedAnchors)
        {
            if (anchor.ProductId == targetProductId)
                continue;
            ids.Add(anchor.ProductId);
            if (ids.Count >= MaxPreselectedAnchorsPerTarget)
                break;
        }

        return ids;
    }

    static List<int> UnionAnchorIds(Dictionary<int, List<int>> preselected)
    {
        var seen = new HashSet<int>();
        var ordered = new List<int>();
        foreach (var targetId in preselected.Keys.OrderBy(id => id))
        {
            foreach (var anchorId in preselected[targetId])
            {
                if (!seen.Add(anchorId))
                    continue;
                ordered.Add(anchorId);
            }
        }

        ordered.Sort();
        return ordered;
    }

    static Dictionary<(int TargetId, int AnchorId), InventoryComboPairCoOccurrenceFacts> IndexPairs(
        InventoryComboCoOccurrenceSnapshot? snapshot)
    {
        var map = new Dictionary<(int, int), InventoryComboPairCoOccurrenceFacts>();
        if (snapshot?.Rows is null)
            return map;

        foreach (var row in snapshot.Rows)
        {
            if (row is null)
                continue;
            map.TryAdd((row.TargetProductId, row.AnchorProductId), row);
        }

        return map;
    }

    static DateTime ResolveToday(DateTime? requested, DateTime intelligenceToday)
    {
        if (requested is DateTime day)
            return day.Date;
        if (intelligenceToday != default)
            return intelligenceToday.Date;
        return DateTime.Today.Date;
    }

    static Dictionary<int, T> Index<T>(
        IReadOnlyList<T>? rows,
        IReadOnlyDictionary<int, T>? fallback,
        Func<T, int> idOf,
        out HashSet<int> conflicts)
        where T : class
    {
        if (rows is { Count: > 0 })
            return Index(rows, idOf, out conflicts);

        conflicts = [];
        if (fallback is null || fallback.Count == 0)
            return [];

        var map = new Dictionary<int, T>(fallback.Count);
        foreach (var pair in fallback)
            map.TryAdd(pair.Key, pair.Value);
        return map;
    }

    static Dictionary<int, T> Index<T>(
        IReadOnlyList<T>? items,
        Func<T, int> idOf,
        out HashSet<int> conflicts)
        where T : class
    {
        var map = new Dictionary<int, T>();
        conflicts = [];
        if (items is null)
            return map;

        foreach (var item in items)
        {
            if (item is null)
                continue;
            var id = idOf(item);
            if (conflicts.Contains(id))
                continue;
            if (map.TryAdd(id, item))
                continue;
            map.Remove(id);
            conflicts.Add(id);
        }

        return map;
    }

    static T? Resolve<T>(
        int productId,
        Dictionary<int, T> lookup,
        HashSet<int> conflicts)
        where T : class
    {
        if (conflicts.Contains(productId))
            return null;
        return lookup.TryGetValue(productId, out var value) ? value : null;
    }

    sealed class EligibleTarget
    {
        public int ProductId { get; init; }
        public ProductTurnoverRow Turnover { get; init; } = new();
        public InventoryComboTargetEligibility Eligibility { get; init; } = new();
        public InventoryCommercialFacts? Facts { get; init; }
    }

    sealed class EligibleAnchor
    {
        public int ProductId { get; init; }
        public ProductTurnoverRow Turnover { get; init; } = new();
        public InventoryComboAnchorEligibility Eligibility { get; init; } = new();
        public InventoryCommercialFacts? Facts { get; init; }
    }
}
