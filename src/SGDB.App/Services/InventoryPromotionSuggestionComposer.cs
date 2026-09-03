using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Composer puro 70F-B5C: junta 70C + snapshot B4 e chama B5B em memória.
/// Autoridade da população: Intelligence.Rows. Join O(n) por ProductId.
/// Não recalcula giro, cenário, piso, margem nem quantidade.
/// QueryCount herdado = 9. ExpectedQueryCount = 0.
/// </summary>
public static class InventoryPromotionSuggestionComposer
{
    public const int ExpectedQueryCount = 0;

    public const int ExpectedPipelineQueryCount =
        InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount;

    public static InventoryPromotionSuggestionSnapshot Compose(
        InventoryIntelligenceSnapshot? intelligence,
        InventoryCommercialScenarioSnapshot? scenarios) =>
        Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = intelligence,
            Scenarios = scenarios,
        });

    public static InventoryPromotionSuggestionSnapshot Compose(
        InventoryPromotionSuggestionComposeInput? input)
    {
        input ??= new InventoryPromotionSuggestionComposeInput();
        var rows = input.Intelligence?.Rows ?? [];
        var scenarios = IndexScenarios(input.Scenarios, out var conflicts);
        var composed = new List<InventoryPromotionSuggestionRow>(rows.Count);
        var map = new Dictionary<int, InventoryPromotionSuggestionRow>(rows.Count);

        foreach (var turnover in rows)
        {
            var productId = turnover.ProductId;
            InventoryPromotionSuggestionResult suggestion;
            if (conflicts.Contains(productId))
            {
                suggestion = CompositionFailure(
                    productId, InventoryPromotionSuggestionReason.DuplicateScenario);
            }
            else if (!scenarios.TryGetValue(productId, out var scenarioRow) || scenarioRow is null)
            {
                suggestion = CompositionFailure(
                    productId, InventoryPromotionSuggestionReason.ScenarioMissing);
            }
            else
            {
                suggestion = WithProductId(
                    InventoryPromotionSuggestionEngine.Evaluate(
                        scenarioRow.ScenarioResult,
                        AttentionPriorityOf(productId, scenarioRow.Attention),
                        HasWholesalePricing(scenarioRow.Facts)),
                    productId);
            }

            var row = new InventoryPromotionSuggestionRow
            {
                ProductId = productId,
                Suggestion = suggestion,
            };
            composed.Add(row);
            map.TryAdd(productId, row);
        }

        return new InventoryPromotionSuggestionSnapshot
        {
            QueryCount = ExpectedPipelineQueryCount,
            Rows = composed,
            ByProductId = map,
        };
    }

    static Dictionary<int, InventoryCommercialScenarioRow> IndexScenarios(
        InventoryCommercialScenarioSnapshot? snapshot,
        out HashSet<int> conflicts)
    {
        var items = snapshot?.Rows;
        var map = new Dictionary<int, InventoryCommercialScenarioRow>();
        conflicts = [];
        if (items is null)
            return map;

        foreach (var item in items)
        {
            if (item is null)
                continue;
            var id = item.ProductId;
            if (conflicts.Contains(id))
                continue;
            if (map.TryAdd(id, item))
                continue;
            map.Remove(id);
            conflicts.Add(id);
        }

        return map;
    }

    static InventoryAttentionPriority? AttentionPriorityOf(
        int productId,
        InventoryAttentionResult? attention)
    {
        if (attention is null || attention.ProductId != productId)
            return null;
        return attention.Priority;
    }

    static bool HasWholesalePricing(InventoryCommercialFacts? facts)
    {
        if (facts is null)
            return false;
        if (facts.HasWholesalePricing)
            return true;
        foreach (var reason in facts.LimitationReasons ?? [])
        {
            if (reason == InventoryCommercialFactsReason.WholesalePricingConfigured)
                return true;
        }

        return false;
    }

    static InventoryPromotionSuggestionResult WithProductId(
        InventoryPromotionSuggestionResult source,
        int productId)
    {
        if (source.ProductId == productId)
            return source;
        return new InventoryPromotionSuggestionResult
        {
            ProductId = productId,
            Status = source.Status,
            Action = source.Action,
            Thesis = source.Thesis,
            Objective = source.Objective,
            Confidence = source.Confidence,
            AttentionPriority = source.AttentionPriority,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = source.SecondaryReasons,
            Warnings = source.Warnings,
            AttentionQuantity = source.AttentionQuantity,
            AttentionQuantitySource = source.AttentionQuantitySource,
            Scenarios = source.Scenarios,
        };
    }

    static InventoryPromotionSuggestionResult CompositionFailure(
        int productId,
        InventoryPromotionSuggestionReason reason) =>
        new()
        {
            ProductId = productId,
            Status = InventoryPromotionSuggestionStatus.ReviewData,
            Action = InventoryPromotionSuggestionAction.ReviewData,
            Thesis = InventoryCommercialScenarioThesis.None,
            Objective = InventoryPromotionSuggestionObjective.ReviewInformation,
            Confidence = InventoryAttentionConfidence.Unavailable,
            PrimaryReason = reason,
            SecondaryReasons = [],
            Warnings = [],
            AttentionQuantity = null,
            AttentionQuantitySource = InventoryCommercialAttentionQuantitySource.None,
            Scenarios = [],
        };
}
