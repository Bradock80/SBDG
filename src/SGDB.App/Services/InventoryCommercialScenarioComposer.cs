using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Composer puro 70F-B4C: junta autoridades já carregadas e chama B3/B4B em memória.
/// Autoridade da população: Intelligence.Rows (ProductTurnoverRow). Join O(n) por ProductId.
/// Ordem: 70C → 70D → 70E → B1 → B2 → policy global → B3 → B4B.
/// Não recalcula VMV, projeção, atenção, elegibilidade nem fatos.
/// QueryCount herdado = 9. ExpectedQueryCount = 0.
/// </summary>
public static class InventoryCommercialScenarioComposer
{
    public const int ExpectedQueryCount = 0;

    /// <summary>
    /// 70C 6 + 70D lots 1 + B2 1 + B3B Load 1. Composer/B1/B3/B3C/B4B = 0.
    /// </summary>
    public const int ExpectedPipelineQueryCount = 9;

    public static InventoryCommercialScenarioSnapshot Compose(
        InventoryIntelligenceSnapshot? intelligence,
        InventoryProjectionSnapshot? projection,
        InventoryAttentionSnapshot? attention,
        IReadOnlyList<InventoryCommercialEligibilityResult>? eligibility,
        InventoryCommercialFactsSnapshot? facts,
        InventoryCommercialMarginPolicyResolution? policyResolution) =>
        Compose(new InventoryCommercialScenarioComposeInput
        {
            Intelligence = intelligence,
            Projection = projection,
            Attention = attention,
            Eligibility = eligibility,
            Facts = facts,
            PolicyResolution = policyResolution,
        });

    public static InventoryCommercialScenarioSnapshot Compose(
        InventoryCommercialScenarioComposeInput? input)
    {
        input ??= new InventoryCommercialScenarioComposeInput();
        var intelligence = input.Intelligence ?? input.Projection?.Intelligence;
        var rows = intelligence?.Rows ?? [];
        var policy = input.PolicyResolution ?? MissingPolicy();
        var floorPolicy = InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(policy);

        var projections = IndexProjections(input, out var projectionConflicts);
        var attentions = Index(
            input.Attention?.Results,
            input.Attention?.ByProductId,
            static item => item.ProductId,
            out var attentionConflicts);
        var eligibilities = IndexList(
            input.Eligibility,
            static item => item.ProductId,
            out var eligibilityConflicts);
        var facts = Index(
            input.Facts?.Rows,
            input.Facts?.ByProductId,
            static item => item.ProductId,
            out var factsConflicts);

        var composed = new List<InventoryCommercialScenarioRow>(rows.Count);
        var map = new Dictionary<int, InventoryCommercialScenarioRow>(rows.Count);

        foreach (var turnover in rows)
        {
            var productId = turnover.ProductId;
            var projectionDuplicate = projectionConflicts.Contains(productId);
            var projection = Resolve(productId, projections, projectionConflicts);
            var attention = Resolve(productId, attentions, attentionConflicts);
            var eligibility = Resolve(productId, eligibilities, eligibilityConflicts);
            var commercialFacts = Resolve(productId, facts, factsConflicts);

            if (MustReplaceEligibility(eligibility, projection is null, projectionDuplicate, attention is null))
            {
                eligibility = FallbackEligibility(
                    productId,
                    projectionDuplicate
                        ? InventoryCommercialEligibilityReason.DuplicateProjection
                        : projection is null
                            ? InventoryCommercialEligibilityReason.ProjectionMissing
                            : InventoryCommercialEligibilityReason.InvalidInput);
            }

            attention ??= FallbackAttention(productId);
            commercialFacts ??= FallbackFacts(productId);
            eligibility ??= FallbackEligibility(
                productId, InventoryCommercialEligibilityReason.InvalidInput);
            var floor = InventoryCommercialPriceFloorEngine.Evaluate(commercialFacts, floorPolicy);
            var scenario = InventoryCommercialScenarioEngine.Evaluate(
                new InventoryCommercialScenarioInput
                {
                    Eligibility = eligibility,
                    Facts = commercialFacts,
                    PolicyResolution = policy,
                    Floor = floor,
                    Turnover = turnover,
                    Projection = projection,
                    Attention = attention,
                });

            var row = new InventoryCommercialScenarioRow
            {
                ProductId = productId,
                Turnover = turnover,
                Projection = projection,
                Attention = attention,
                Eligibility = eligibility,
                Facts = commercialFacts,
                PriceFloor = floor,
                ScenarioResult = scenario,
            };
            composed.Add(row);
            map.TryAdd(productId, row);
        }

        return new InventoryCommercialScenarioSnapshot
        {
            QueryCount = ExpectedPipelineQueryCount,
            PolicyResolution = policy,
            Rows = composed,
            ByProductId = map,
        };
    }

    static Dictionary<int, InventoryProjectedProduct> IndexProjections(
        InventoryCommercialScenarioComposeInput input,
        out HashSet<int> conflicts)
    {
        if (input.ProjectionRows is not null)
            return IndexList(input.ProjectionRows, static item => item.ProductId, out conflicts);

        conflicts = [];
        var snapshot = input.Projection?.ByProductId;
        if (snapshot is null || snapshot.Count == 0)
            return [];

        var map = new Dictionary<int, InventoryProjectedProduct>(snapshot.Count);
        foreach (var pair in snapshot)
            map.TryAdd(pair.Key, pair.Value);
        return map;
    }

    static Dictionary<int, T> Index<T>(
        IReadOnlyList<T>? rows,
        IReadOnlyDictionary<int, T>? fallback,
        Func<T, int> idOf,
        out HashSet<int> conflicts)
        where T : class
    {
        if (rows is { Count: > 0 })
            return IndexList(rows, idOf, out conflicts);

        conflicts = [];
        if (fallback is null || fallback.Count == 0)
            return [];

        var map = new Dictionary<int, T>(fallback.Count);
        foreach (var pair in fallback)
            map.TryAdd(pair.Key, pair.Value);
        return map;
    }

    static Dictionary<int, T> IndexList<T>(
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

    static InventoryCommercialEligibilityResult FallbackEligibility(
        int productId,
        InventoryCommercialEligibilityReason reason) =>
        new()
        {
            ProductId = productId,
            Kind = InventoryCommercialEligibilityKind.ReviewData,
            PrimaryReason = reason,
            SecondaryReasons = [],
            Confidence = InventoryAttentionConfidence.Unavailable,
        };

    static bool MustReplaceEligibility(
        InventoryCommercialEligibilityResult? eligibility,
        bool projectionMissing,
        bool projectionDuplicate,
        bool attentionMissing)
    {
        if (eligibility is null)
            return true;
        if (eligibility.PrimaryReason == InventoryCommercialEligibilityReason.Expired)
            return false;
        if (eligibility.Kind is InventoryCommercialEligibilityKind.ReviewData
            or InventoryCommercialEligibilityKind.NoCommercialRecommendation)
            return false;
        return projectionDuplicate || projectionMissing || attentionMissing;
    }

    static InventoryAttentionResult FallbackAttention(int productId) =>
        new()
        {
            ProductId = productId,
            Priority = InventoryAttentionPriority.Low,
            Family = InventoryAttentionFamily.DataQuality,
            PrimaryReason = InventoryAttentionReason.InvalidInput,
            SecondaryReasons = [],
            Action = InventoryOperatorAction.ReviewData,
            Confidence = InventoryAttentionConfidence.Unavailable,
            SurplusValueQuality = InventoryProjectionSurplusValueQuality.Unavailable,
        };

    static InventoryCommercialFacts FallbackFacts(int productId) =>
        InventoryCommercialFactsEngine.Classify(new InventoryCommercialFactsInput
        {
            ProductId = productId,
            ProductFound = false,
        });

    static InventoryCommercialMarginPolicyResolution MissingPolicy() =>
        new()
        {
            Status = InventoryCommercialMarginPolicyResolutionStatus.Missing,
            Source = InventoryCommercialMarginPolicySource.None,
            Reasons = [InventoryCommercialMarginSettingReason.Missing],
        };
}
