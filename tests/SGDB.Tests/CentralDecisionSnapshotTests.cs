using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// 71C-B1 — contrato do snapshot da Central. Sem composer, ranking, SQL ou UI.
/// </summary>
public class CentralDecisionSnapshotTests
{
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly DateOnly Sep10 = new(2026, 9, 10);

    [Fact]
    public void QueryCount_proprio_e_zero()
    {
        Assert.Equal(0, CentralDecisionSnapshot.OwnQueryCount);
        Assert.Equal(CommercialGoalActionPlanSnapshot.MaxActions, CentralDecisionSnapshot.MaxActNowItems);
        Assert.Equal(5, CentralDecisionSnapshot.MaxActNowItems);
        Assert.Equal(3, CentralDecisionSnapshot.MaxPreserveItems);
    }

    [Fact]
    public void Operational()
    {
        var snap = Envelope(CentralDecisionState.Operational, ActNow(Excess(1)));
        Assert.Equal(CentralDecisionState.Operational, snap.State);
        Assert.Equal(Sep2026, snap.Competence);
        Assert.Equal(Sep10, snap.ReferenceDate);
        Assert.Single(snap.ActNow);
        Assert.Empty(snap.Preserve);
        Assert.Equal(CommercialGoalActionLimitation.None, snap.ActionPlan!.Limitations);
        Assert.False(snap.HasLimitation(CentralDecisionLimitation.ContributionUnavailable));
    }

    [Fact]
    public void Limited_nao_finge_vazio()
    {
        var snap = Envelope(
            CentralDecisionState.Limited,
            ActNow(Excess(1)),
            limitations: CentralDecisionLimitation.ContributionUnavailable
                | CentralDecisionLimitation.PromotionSourceUnavailable);
        Assert.Equal(CentralDecisionState.Limited, snap.State);
        Assert.NotEqual(CentralDecisionState.Empty, snap.State);
        Assert.NotEqual(CentralDecisionState.Unavailable, snap.State);
        Assert.Single(snap.ActNow);
        Assert.True(snap.HasLimitation(CentralDecisionLimitation.ContributionUnavailable));
        Assert.True(snap.HasLimitation(CentralDecisionLimitation.PromotionSourceUnavailable));
        Assert.Null(snap.Contribution);
    }

    [Fact]
    public void Empty()
    {
        var snap = Envelope(CentralDecisionState.Empty);
        Assert.Equal(CentralDecisionState.Empty, snap.State);
        Assert.Empty(snap.ActNow);
        Assert.Empty(snap.Preserve);
        Assert.NotEqual(CentralDecisionState.Unavailable, snap.State);
        Assert.NotEqual(CentralDecisionState.Future, snap.State);
    }

    [Fact]
    public void Unavailable_nao_finge_vazio()
    {
        var snap = new CentralDecisionSnapshot
        {
            State = CentralDecisionState.Unavailable,
            Competence = Sep2026,
            ReferenceDate = Sep10,
            Limitations = CentralDecisionLimitation.IntelligenceUnavailable,
        };
        Assert.Equal(CentralDecisionState.Unavailable, snap.State);
        Assert.NotEqual(CentralDecisionState.Empty, snap.State);
        Assert.Empty(snap.ActNow);
        Assert.Empty(snap.Preserve);
        Assert.Null(snap.Goal);
        Assert.Null(snap.ActionPlan);
        Assert.True(snap.HasLimitation(CentralDecisionLimitation.IntelligenceUnavailable));
    }

    [Fact]
    public void Future()
    {
        var snap = Envelope(CentralDecisionState.Future);
        Assert.Equal(CentralDecisionState.Future, snap.State);
        Assert.Equal(CommercialGoalActionPlanMode.FutureCompetence, snap.PlanMode);
        Assert.Empty(snap.ActNow);
        Assert.NotEqual(CentralDecisionState.Empty, snap.State);
        Assert.NotEqual(CentralDecisionState.Unavailable, snap.State);
    }

    [Fact]
    public void Agir_agora_conserva_CommercialGoalActionType()
    {
        var action = Excess(9, InventoryAttentionConfidence.Limited, promotion: true, combo: true);
        var item = new CentralDecisionActNowItem { Action = action, Contribution = HistRow(9) };
        Assert.Same(action, item.Action);
        Assert.Equal(9, item.ProductId);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, item.ActionType);
        Assert.Equal(action.ActionType, item.ActionType);
        Assert.True(item.Action.HasPromotionSuggestion);
        Assert.True(item.Action.HasComboSuggestion);
        Assert.Equal(InventoryAttentionConfidence.Limited, item.Action.Confidence);
        Assert.NotNull(item.Contribution);
        Assert.Equal(9, item.Contribution!.ProductId);
    }

    [Fact]
    public void Nenhuma_nova_acao_primaria()
    {
        var expected = new[]
        {
            nameof(CommercialGoalActionType.ReviewData),
            nameof(CommercialGoalActionType.RemoveExpired),
            nameof(CommercialGoalActionType.PrioritizeExpiryRisk),
            nameof(CommercialGoalActionType.PrioritizeExcess),
            nameof(CommercialGoalActionType.PrioritizeIdle),
            nameof(CommercialGoalActionType.ProtectAvailability),
            nameof(CommercialGoalActionType.Monitor),
        };
        Assert.Equal(expected, Enum.GetNames<CommercialGoalActionType>());

        var src = ReadModelSource();
        foreach (var forbidden in new[]
                 {
                     "AccelerateTurnover", "CommercialOpportunity", "Promote",
                     "CreateCombo", "Buy", "ReplenishNow", "Preserve",
                 })
        {
            Assert.DoesNotContain("enum " + forbidden, src, StringComparison.Ordinal);
            Assert.DoesNotContain("CentralDecisionAction", src, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "Preserve",
            Enum.GetNames<CommercialGoalActionType>(),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Contexto_B8_opcional_no_item()
    {
        var withRow = new CentralDecisionActNowItem
        {
            Action = Excess(1),
            Contribution = HistRow(1, 40m),
        };
        Assert.Equal(40m, withRow.Contribution!.GrossProfit);
        Assert.Equal(CommercialGoalCostQuality.Exact, withRow.Contribution.CostQuality);

        var without = new CentralDecisionActNowItem { Action = Excess(2) };
        Assert.Null(without.Contribution);
    }

    [Fact]
    public void Preservar_separado_da_fila()
    {
        var snap = Envelope(
            CentralDecisionState.Operational,
            ActNow(Protect(1)),
            [
                new CentralDecisionPreserveItem
                {
                    ProductId = 80,
                    ProductCode = "P80",
                    ProductName = "Preservar",
                    CoverageBand = InventoryCoverageBand.Normal,
                    Contribution = HistRow(80, 200m),
                },
            ]);

        Assert.Equal(CommercialGoalActionType.ProtectAvailability, snap.ActNow[0].ActionType);
        var keep = Assert.Single(snap.Preserve);
        Assert.Equal(80, keep.ProductId);
        Assert.Equal(InventoryCoverageBand.Normal, keep.CoverageBand);
        Assert.Equal(200m, keep.Contribution!.GrossProfit);
        Assert.DoesNotContain(80, snap.ActNow.Select(x => x.ProductId));
    }

    [Fact]
    public void Snapshot_sem_Preservar()
    {
        var snap = Envelope(CentralDecisionState.Operational, ActNow(Excess(1)));
        Assert.NotNull(snap.Preserve);
        Assert.Empty(snap.Preserve);
    }

    [Fact]
    public void Snapshot_sem_B8()
    {
        var snap = Envelope(CentralDecisionState.Limited, ActNow(Excess(1)));
        Assert.Null(snap.Contribution);
        Assert.Null(snap.ActNow[0].Contribution);
        Assert.Empty(snap.Preserve);
    }

    [Fact]
    public void Competencia_e_meta_compacta_preservadas()
    {
        var goal = GoalSnap(12_000m, 3_000m);
        var snap = Envelope(CentralDecisionState.Operational, goal: goal);
        Assert.Equal(Sep2026, snap.Competence);
        Assert.Equal(Sep2026, snap.Goal!.Competence);
        Assert.Equal(12_000m, snap.GoalAmount);
        Assert.Equal(3_000m, snap.RealizedGrossProfit);
        Assert.Equal(9_000m, snap.RemainingAmount);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.GoalStatus);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.FinancialQuality);
        Assert.Equal(CommercialGoalProgressSkipReason.None, snap.ProgressSkipReason);
        Assert.Same(goal, snap.Goal);
    }

    [Fact]
    public void Limitacoes_B7_e_envelope_preservadas()
    {
        var plan = new CommercialGoalActionPlanSnapshot
        {
            Competence = Sep2026,
            ReferenceDate = Sep10,
            Mode = CommercialGoalActionPlanMode.InventoryOnly,
            Limitations = CommercialGoalActionLimitation.FinancialUnavailable
                | CommercialGoalActionLimitation.LocationLimitation,
            Items = [Protect(3)],
        };
        var snap = new CentralDecisionSnapshot
        {
            State = CentralDecisionState.Limited,
            Competence = Sep2026,
            ReferenceDate = Sep10,
            ActionPlan = plan,
            ActNow = [new CentralDecisionActNowItem { Action = plan.Items[0] }],
            Limitations = CentralDecisionLimitation.ContributionUnavailable
                | CentralDecisionLimitation.ComboSourceUnavailable,
        };
        Assert.True(snap.ActionPlan!.HasLimitation(CommercialGoalActionLimitation.FinancialUnavailable));
        Assert.True(snap.ActionPlan.HasLimitation(CommercialGoalActionLimitation.LocationLimitation));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, snap.PlanMode);
        Assert.True(snap.HasLimitation(CentralDecisionLimitation.ContributionUnavailable));
        Assert.True(snap.HasLimitation(CentralDecisionLimitation.ComboSourceUnavailable));
        Assert.Equal(
            CommercialGoalActionLimitation.LocationLimitation,
            snap.ActNow[0].Action.Limitations);
    }

    [Fact]
    public void Colecoes_nao_nulas()
    {
        var snap = new CentralDecisionSnapshot
        {
            State = CentralDecisionState.Empty,
            Competence = Sep2026,
            ReferenceDate = Sep10,
        };
        Assert.NotNull(snap.ActNow);
        Assert.NotNull(snap.Preserve);
        Assert.Empty(snap.ActNow);
        Assert.Empty(snap.Preserve);
        Assert.Null(snap.Goal);
        Assert.Null(snap.ActionPlan);
        Assert.Null(snap.Contribution);
        Assert.Null(snap.GoalAmount);
        Assert.Null(snap.GoalStatus);
        Assert.Null(snap.PlanMode);
        Assert.Null(snap.FinancialQuality);
    }

    [Fact]
    public void Contrato_nao_depende_de_WPF()
    {
        var src = ReadModelSource();
        Assert.DoesNotContain("System.Windows", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Brush", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility", src, StringComparison.Ordinal);
        Assert.DoesNotContain("UserControl", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Window", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Ausencia_de_SQL_IO_e_causalidade()
    {
        var src = ReadModelSource();
        foreach (var token in new[]
                 {
                     "SqliteConnection", "AppSettingsService", "DateTime.Now", "DateTime.Today",
                     "File.", "Http", "ExpectedProfit", "IncrementalProfit",
                     "GoalContributionForecast", "ExpectedSales", "PromotionUplift",
                 })
        {
            Assert.DoesNotContain(token, src, StringComparison.Ordinal);
        }
    }

    static CentralDecisionSnapshot Envelope(
        CentralDecisionState state,
        IReadOnlyList<CentralDecisionActNowItem>? actNow = null,
        IReadOnlyList<CentralDecisionPreserveItem>? preserve = null,
        CentralDecisionLimitation limitations = CentralDecisionLimitation.None,
        CommercialGoalSnapshot? goal = null)
    {
        goal ??= GoalSnap(12_000m, 3_000m);
        var items = (actNow ?? []).Select(x => x.Action).ToArray();
        var mode = state == CentralDecisionState.Future
            ? CommercialGoalActionPlanMode.FutureCompetence
            : CommercialGoalActionPlanMode.Operational;
        return new CentralDecisionSnapshot
        {
            State = state,
            Competence = Sep2026,
            ReferenceDate = Sep10,
            Goal = goal,
            ActionPlan = new CommercialGoalActionPlanSnapshot
            {
                Competence = Sep2026,
                ReferenceDate = Sep10,
                Mode = mode,
                GoalStatus = goal.Status,
                Items = items,
            },
            ActNow = actNow ?? [],
            Preserve = preserve ?? [],
            Limitations = limitations,
        };
    }

    static IReadOnlyList<CentralDecisionActNowItem> ActNow(params CommercialGoalActionItem[] actions) =>
        actions.Select(a => new CentralDecisionActNowItem { Action = a }).ToArray();

    static CommercialGoalActionItem Excess(
        int id,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable,
        bool promotion = false,
        bool combo = false) =>
        new()
        {
            ProductId = id,
            ProductCode = "X" + id,
            ProductName = "Excesso " + id,
            ActionType = CommercialGoalActionType.PrioritizeExcess,
            Priority = InventoryAttentionPriority.Medium,
            Confidence = confidence,
            HasPromotionSuggestion = promotion,
            HasComboSuggestion = combo,
            Limitations = CommercialGoalActionLimitation.None,
        };

    static CommercialGoalActionItem Protect(int id) =>
        new()
        {
            ProductId = id,
            ProductCode = "R" + id,
            ProductName = "Reposição " + id,
            ActionType = CommercialGoalActionType.ProtectAvailability,
            Priority = InventoryAttentionPriority.High,
            Confidence = InventoryAttentionConfidence.Limited,
            PurchaseGuidanceAction = InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            Limitations = CommercialGoalActionLimitation.LocationLimitation,
        };

    static CommercialGoalProductContributionRow HistRow(int productId, decimal gp = 10m) =>
        new()
        {
            ProductId = productId,
            ProductCode = "C" + productId,
            ProductName = "Histórico " + productId,
            Revenue = 100m,
            Cogs = 100m - gp,
            GrossProfit = gp,
            GrossMarginPercent = 10m,
            GrossProfitShare = 0.1m,
            CostQuality = CommercialGoalCostQuality.Exact,
        };

    static CommercialGoalSnapshot GoalSnap(decimal goal, decimal realized) =>
        CommercialGoalComposer.Compose(
            new CommercialGoalSettingResolution
            {
                Competence = Sep2026,
                Source = CommercialGoalSettingSource.MonthlyOverride,
                GoalAmount = goal,
                HasValidGoal = true,
                QueryCount = 1,
            },
            new CommercialGoalFinancialSnapshot
            {
                Competence = Sep2026,
                NetCommercialRevenue = realized + 10m,
                Cogs = 10m,
                GrossProfit = realized,
                CostQuality = CommercialGoalCostQuality.Exact,
                GrossProfitAvailable = true,
            },
            Sep10);

    static string ReadModelSource()
    {
        var relative = new[] { "src", "SGDB.App", "Models", "CentralDecision.cs" };
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
