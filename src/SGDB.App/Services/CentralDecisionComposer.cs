using SGDB.Domain.Commercial;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Composer puro 71C-B2. Orquestra B7 + B8 + cobertura 70C já carregada.
/// Não ranqueia ActNow, não recalcula ação, meta, CMV nem cobertura.
/// </summary>
public static class CentralDecisionComposer
{
    public const int OwnQueryCount = CentralDecisionSnapshot.OwnQueryCount;

    public static CentralDecisionSnapshot Compose(
        CommercialGoalSnapshot goal,
        CommercialGoalActionPlanSnapshot? plan,
        CommercialGoalProductContributionSnapshot? contribution = null,
        CommercialGoalActionPlanSources? sources = null,
        CentralDecisionLimitation additionalLimitations = CentralDecisionLimitation.None)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (plan is not null && plan.Competence != goal.Competence)
        {
            throw new ArgumentException(
                "A competência do plano B7 deve coincidir com a da meta.",
                nameof(plan));
        }

        if (contribution is not null && contribution.Competence != goal.Competence)
        {
            throw new ArgumentException(
                "A competência da contribuição B8 deve coincidir com a da meta.",
                nameof(contribution));
        }

        var limitations = EnvelopeLimitations(plan, contribution, sources) | additionalLimitations;
        var inheritedQueries = goal.QueryCount
            + (plan?.QueryCount ?? 0)
            + (contribution?.QueryCount ?? 0);

        if (IsFuture(goal, plan))
        {
            return Finish(
                CentralDecisionState.Future,
                goal,
                plan,
                contribution,
                [],
                [],
                limitations,
                inheritedQueries);
        }

        if (plan is null)
        {
            return Finish(
                CentralDecisionState.Unavailable,
                goal,
                plan: null,
                contribution,
                [],
                [],
                limitations | CentralDecisionLimitation.IntelligenceUnavailable,
                inheritedQueries);
        }

        var actNow = BuildActNow(plan, contribution);
        var preserve = BuildPreserve(actNow, contribution, sources);
        var state = ResolveState(actNow, preserve, limitations);
        return Finish(state, goal, plan, contribution, actNow, preserve, limitations, inheritedQueries);
    }

    static bool IsFuture(CommercialGoalSnapshot goal, CommercialGoalActionPlanSnapshot? plan) =>
        plan?.Mode == CommercialGoalActionPlanMode.FutureCompetence
        || goal.Status == CommercialGoalStatus.NotStarted
        || goal.Progress?.PeriodState == CommercialGoalPeriodState.Future;

    static CentralDecisionLimitation EnvelopeLimitations(
        CommercialGoalActionPlanSnapshot? plan,
        CommercialGoalProductContributionSnapshot? contribution,
        CommercialGoalActionPlanSources? sources)
    {
        var flags = CentralDecisionLimitation.None;
        if (contribution is null || !contribution.GrossProfitAvailable)
            flags |= CentralDecisionLimitation.ContributionUnavailable;

        if (sources is not null && sources.Intelligence is null)
            flags |= CentralDecisionLimitation.IntelligenceUnavailable;

        if (plan is null)
            flags |= CentralDecisionLimitation.IntelligenceUnavailable;

        return flags;
    }

    static IReadOnlyList<CentralDecisionActNowItem> BuildActNow(
        CommercialGoalActionPlanSnapshot plan,
        CommercialGoalProductContributionSnapshot? contribution)
    {
        var byProduct = IndexContribution(contribution);
        var seen = new HashSet<int>();
        var list = new List<CentralDecisionActNowItem>(
            Math.Min(plan.Items.Count, CentralDecisionSnapshot.MaxActNowItems));

        foreach (var action in plan.Items)
        {
            if (list.Count >= CentralDecisionSnapshot.MaxActNowItems)
                break;
            if (!seen.Add(action.ProductId))
                continue;

            byProduct.TryGetValue(action.ProductId, out var row);
            list.Add(new CentralDecisionActNowItem
            {
                Action = action,
                Contribution = row,
            });
        }

        return list;
    }

    static IReadOnlyList<CentralDecisionPreserveItem> BuildPreserve(
        IReadOnlyList<CentralDecisionActNowItem> actNow,
        CommercialGoalProductContributionSnapshot? contribution,
        CommercialGoalActionPlanSources? sources)
    {
        if (contribution is null
            || !contribution.GrossProfitAvailable
            || sources?.Intelligence is null)
        {
            return [];
        }

        var actNowIds = new HashSet<int>(actNow.Count);
        for (var i = 0; i < actNow.Count; i++)
            actNowIds.Add(actNow[i].ProductId);

        var turnover = IndexTurnover(sources.Intelligence);
        var attention = sources.Attention?.ByProductId;
        var guidance = sources.Guidance?.ByProductId;
        var eligible = new List<(CentralDecisionPreserveItem Item, decimal Gp)>();

        foreach (var row in contribution.Rows)
        {
            if (actNowIds.Contains(row.ProductId))
                continue;
            if (row.GrossProfit is not decimal gp || gp <= 0m)
                continue;
            if (!turnover.TryGetValue(row.ProductId, out var physical))
                continue;
            if (HasBlockingThesis(physical, Lookup(attention, row.ProductId), Lookup(guidance, row.ProductId)))
                continue;

            eligible.Add((
                new CentralDecisionPreserveItem
                {
                    ProductId = row.ProductId,
                    ProductCode = FirstNonEmpty(physical.Code, row.ProductCode),
                    ProductName = FirstNonEmpty(physical.Name, row.ProductName),
                    CoverageBand = physical.CoverageBand,
                    Contribution = row,
                },
                gp));
        }

        eligible.Sort(ComparePreserve);
        var take = Math.Min(CentralDecisionSnapshot.MaxPreserveItems, eligible.Count);
        if (take == eligible.Count)
            return eligible.ConvertAll(x => x.Item);

        var top = new CentralDecisionPreserveItem[take];
        for (var i = 0; i < take; i++)
            top[i] = eligible[i].Item;
        return top;
    }

    static bool HasBlockingThesis(
        ProductTurnoverRow row,
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance)
    {
        if (row.CoverageBand != InventoryCoverageBand.Normal)
            return true;
        if (row.IsIdle || row.IsCompositionProduct || row.HasLocationStockAnomaly)
            return true;
        if (!row.HasPhysicalAvailabilityEvidence)
            return true;

        if (guidance?.Action is InventoryPurchaseGuidanceAction.ConsiderReplenishment
            or InventoryPurchaseGuidanceAction.ReviewData)
        {
            return true;
        }

        if (attention is null)
            return false;

        if (attention.Action is InventoryOperatorAction.ReviewData
            or InventoryOperatorAction.RemoveExpired
            or InventoryOperatorAction.PrioritizeSale
            or InventoryOperatorAction.EvaluateExcess)
        {
            return true;
        }

        return attention.PrimaryReason is InventoryAttentionReason.Idle
            or InventoryAttentionReason.Expired
            or InventoryAttentionReason.ExpiresToday
            or InventoryAttentionReason.SurplusAtExpiry
            or InventoryAttentionReason.NearExpiryWithoutSurplus
            or InventoryAttentionReason.ProjectedExcess30
            or InventoryAttentionReason.NegativeStock
            or InventoryAttentionReason.NegativeLocationStock
            or InventoryAttentionReason.NegativeWarehouseStock
            or InventoryAttentionReason.InconsistentStockTotals
            or InventoryAttentionReason.NoPhysicalEvidence
            or InventoryAttentionReason.CompositionProduct
            or InventoryAttentionReason.InvalidInput;
    }

    static int ComparePreserve(
        (CentralDecisionPreserveItem Item, decimal Gp) left,
        (CentralDecisionPreserveItem Item, decimal Gp) right)
    {
        var gp = right.Gp.CompareTo(left.Gp);
        if (gp != 0)
            return gp;
        return left.Item.ProductId.CompareTo(right.Item.ProductId);
    }

    static CentralDecisionState ResolveState(
        IReadOnlyList<CentralDecisionActNowItem> actNow,
        IReadOnlyList<CentralDecisionPreserveItem> preserve,
        CentralDecisionLimitation limitations)
    {
        if (limitations.HasFlag(CentralDecisionLimitation.IntelligenceUnavailable)
            && actNow.Count == 0)
        {
            return CentralDecisionState.Unavailable;
        }

        var limited = limitations != CentralDecisionLimitation.None;
        if (actNow.Count == 0 && preserve.Count == 0)
            return limited ? CentralDecisionState.Limited : CentralDecisionState.Empty;
        return limited ? CentralDecisionState.Limited : CentralDecisionState.Operational;
    }

    static CentralDecisionSnapshot Finish(
        CentralDecisionState state,
        CommercialGoalSnapshot goal,
        CommercialGoalActionPlanSnapshot? plan,
        CommercialGoalProductContributionSnapshot? contribution,
        IReadOnlyList<CentralDecisionActNowItem> actNow,
        IReadOnlyList<CentralDecisionPreserveItem> preserve,
        CentralDecisionLimitation limitations,
        int queryCount) =>
        new()
        {
            State = state,
            Competence = goal.Competence,
            ReferenceDate = goal.ReferenceDate,
            Goal = goal,
            ActionPlan = plan,
            Contribution = contribution,
            ActNow = actNow,
            Preserve = preserve,
            Limitations = limitations,
            QueryCount = queryCount,
        };

    static Dictionary<int, CommercialGoalProductContributionRow> IndexContribution(
        CommercialGoalProductContributionSnapshot? contribution)
    {
        var map = new Dictionary<int, CommercialGoalProductContributionRow>();
        if (contribution is null)
            return map;
        foreach (var row in contribution.Rows)
            map.TryAdd(row.ProductId, row);
        return map;
    }

    static Dictionary<int, ProductTurnoverRow> IndexTurnover(InventoryIntelligenceSnapshot intelligence)
    {
        var map = new Dictionary<int, ProductTurnoverRow>(intelligence.Rows.Count);
        foreach (var row in intelligence.Rows)
            map.TryAdd(row.ProductId, row);
        return map;
    }

    static T? Lookup<T>(IReadOnlyDictionary<int, T>? map, int productId)
        where T : class =>
        map is not null && map.TryGetValue(productId, out var value) ? value : null;

    static string FirstNonEmpty(string left, string right) =>
        string.IsNullOrWhiteSpace(left) ? right : left;
}
