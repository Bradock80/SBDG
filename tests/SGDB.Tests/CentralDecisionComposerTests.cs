using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71C-B2 — composer puro. Inputs B7 prontos; não reimplementa ranking.
/// </summary>
public class CentralDecisionComposerTests
{
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly DateOnly Sep10 = new(2026, 9, 10);

    [Fact]
    public void QueryCount_proprio_e_zero()
    {
        Assert.Equal(0, CentralDecisionComposer.OwnQueryCount);
        Assert.Equal(0, CentralDecisionSnapshot.OwnQueryCount);
        Assert.Equal(5, CentralDecisionSnapshot.MaxActNowItems);
        Assert.Equal(3, CentralDecisionSnapshot.MaxPreserveItems);
    }

    [Fact]
    public void ActNow_preserva_ordem_e_acao_B7()
    {
        var plan = Plan(
            Item(1, CommercialGoalActionType.ReviewData),
            Item(2, CommercialGoalActionType.PrioritizeExcess),
            Item(3, CommercialGoalActionType.Monitor));
        var snap = Compose(BelowPace(), plan);
        Assert.Equal(3, snap.ActNow.Count);
        Assert.Equal([1, 2, 3], snap.ActNow.Select(x => x.ProductId).ToArray());
        Assert.Equal(CommercialGoalActionType.ReviewData, snap.ActNow[0].ActionType);
        Assert.Same(plan.Items[1], snap.ActNow[1].Action);
        Assert.Equal(CommercialGoalActionType.Monitor, snap.ActNow[2].ActionType);
    }

    [Fact]
    public void ActNow_maximo_5_sem_reordenar()
    {
        var items = Enumerable.Range(1, 6)
            .Select(i => Item(i, CommercialGoalActionType.PrioritizeExcess))
            .ToArray();
        var snap = Compose(BelowPace(), Plan(items));
        Assert.Equal(5, snap.ActNow.Count);
        Assert.Equal([1, 2, 3, 4, 5], snap.ActNow.Select(x => x.ProductId).ToArray());
        Assert.DoesNotContain(6, snap.ActNow.Select(x => x.ProductId));
    }

    [Fact]
    public void B8_join_por_ProductId_ou_null()
    {
        var row = Hist(2, 40m);
        var snap = Compose(
            BelowPace(),
            Plan(Item(1, CommercialGoalActionType.PrioritizeExcess), Item(2, CommercialGoalActionType.Monitor)),
            Contribution(row));
        Assert.Null(snap.ActNow[0].Contribution);
        Assert.Same(row, snap.ActNow[1].Contribution);
        Assert.Equal(40m, snap.ActNow[1].Contribution!.GrossProfit);
    }

    [Fact]
    public void B8_nao_altera_primaria()
    {
        var snap = Compose(
            BelowPace(),
            Plan(Item(1, CommercialGoalActionType.ReviewData)),
            Contribution(Hist(1, 9_999m)));
        Assert.Equal(CommercialGoalActionType.ReviewData, snap.ActNow[0].ActionType);
        Assert.Equal(9_999m, snap.ActNow[0].Contribution!.GrossProfit);
    }

    [Fact]
    public void ReviewData_e_ProtectAvailability_permanecem()
    {
        var review = Compose(
            BelowPace(),
            Plan(Item(1, CommercialGoalActionType.ReviewData, promotion: true)));
        Assert.Equal(CommercialGoalActionType.ReviewData, review.ActNow[0].ActionType);

        var protect = Item(2, CommercialGoalActionType.ProtectAvailability);
        protect = Clone(protect, promotion: false, combo: true);
        var snap = Compose(BelowPace(), Plan(protect), Contribution(Hist(2, 800m)));
        Assert.Equal(CommercialGoalActionType.ProtectAvailability, snap.ActNow[0].ActionType);
        Assert.False(snap.ActNow[0].Action.HasPromotionSuggestion);
        Assert.True(snap.ActNow[0].Action.HasComboSuggestion);
    }

    [Fact]
    public void Complementos_B7_nao_sao_criados_nem_removidos()
    {
        var item = Item(
            4,
            CommercialGoalActionType.PrioritizeExcess,
            promotion: true,
            combo: true,
            guidance: InventoryPurchaseGuidanceAction.DoNotReplenishNow);
        var snap = Compose(BelowPace(), Plan(item));
        var action = snap.ActNow[0].Action;
        Assert.True(action.HasPromotionSuggestion);
        Assert.True(action.HasComboSuggestion);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, action.PurchaseGuidanceAction);
    }

    [Fact]
    public void Preserve_separado_cap_3_somente_Normal()
    {
        var rows = new[] { Hist(10, 30m), Hist(11, 90m), Hist(12, 60m), Hist(13, 80m) };
        var intelligence = Intelligence(
            Turnover(10, InventoryCoverageBand.Normal),
            Turnover(11, InventoryCoverageBand.Normal),
            Turnover(12, InventoryCoverageBand.Low),
            Turnover(13, InventoryCoverageBand.Normal));
        var snap = Compose(
            Achieved(),
            Plan(),
            Contribution(rows),
            Sources(intelligence));
        Assert.Empty(snap.ActNow);
        Assert.Equal(3, snap.Preserve.Count);
        Assert.Equal([11, 13, 10], snap.Preserve.Select(x => x.ProductId).ToArray());
        Assert.All(snap.Preserve, p => Assert.Equal(InventoryCoverageBand.Normal, p.CoverageBand));
        Assert.DoesNotContain(12, snap.Preserve.Select(x => x.ProductId));
    }

    [Fact]
    public void Preserve_exclui_ActNow_e_teses_B7()
    {
        var act = Item(1, CommercialGoalActionType.PrioritizeExcess);
        var intelligence = Intelligence(
            Turnover(1, InventoryCoverageBand.Normal),
            Turnover(2, InventoryCoverageBand.Normal),
            Idle(3, InventoryCoverageBand.Normal));
        var snap = Compose(
            BelowPace(),
            Plan(act),
            Contribution(Hist(1, 100m), Hist(2, 50m), Hist(3, 70m)),
            Sources(intelligence, Attention(3, InventoryOperatorAction.Monitor, InventoryAttentionReason.Idle)));
        Assert.Equal(1, Assert.Single(snap.ActNow).ProductId);
        Assert.Equal(2, Assert.Single(snap.Preserve).ProductId);
    }

    [Fact]
    public void Preserve_ignora_GP_unavailable_e_negativo()
    {
        var intelligence = Intelligence(
            Turnover(1, InventoryCoverageBand.Normal),
            Turnover(2, InventoryCoverageBand.Normal),
            Turnover(3, InventoryCoverageBand.Normal));
        var negative = Hist(2, -15m);
        var unavailable = Hist(3, 40m);
        unavailable = new CommercialGoalProductContributionRow
        {
            ProductId = 3,
            ProductCode = unavailable.ProductCode,
            ProductName = unavailable.ProductName,
            Revenue = 40m,
            Cogs = 0m,
            GrossProfit = null,
            CostQuality = CommercialGoalCostQuality.Unavailable,
        };
        var snap = Compose(
            Achieved(),
            Plan(),
            Contribution(Hist(1, 20m), negative, unavailable),
            Sources(intelligence));
        Assert.Equal(1, Assert.Single(snap.Preserve).ProductId);
        Assert.Empty(snap.ActNow);
    }

    [Fact]
    public void Preserve_desempate_ProductId()
    {
        var intelligence = Intelligence(
            Turnover(8, InventoryCoverageBand.Normal),
            Turnover(3, InventoryCoverageBand.Normal));
        var snap = Compose(
            Achieved(),
            Plan(),
            Contribution(Hist(8, 50m), Hist(3, 50m)),
            Sources(intelligence));
        Assert.Equal([3, 8], snap.Preserve.Select(x => x.ProductId).ToArray());
    }

    [Fact]
    public void GP_negativo_nao_cria_acao()
    {
        var intelligence = Intelligence(Turnover(1, InventoryCoverageBand.Normal));
        var snap = Compose(
            BelowPace(),
            Plan(),
            Contribution(Hist(1, -80m)),
            Sources(intelligence));
        Assert.Empty(snap.ActNow);
        Assert.Empty(snap.Preserve);
    }

    [Fact]
    public void Achieved_Normal_GP_alto_vai_para_Preserve()
    {
        var intelligence = Intelligence(Turnover(9, InventoryCoverageBand.Normal));
        var snap = Compose(
            Achieved(),
            Plan(),
            Contribution(Hist(9, 500m)),
            Sources(intelligence));
        Assert.Equal(CommercialGoalStatus.Achieved, snap.GoalStatus);
        Assert.Empty(snap.ActNow);
        Assert.Equal(9, Assert.Single(snap.Preserve).ProductId);
        Assert.Equal(InventoryCoverageBand.Normal, snap.Preserve[0].CoverageBand);
    }

    [Fact]
    public void BelowPace_nao_vence_ProtectAvailability()
    {
        var item = Item(4, CommercialGoalActionType.ProtectAvailability, promotion: false);
        var snap = Compose(BelowPace(), Plan(item), Contribution(Hist(4, 700m)));
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.GoalStatus);
        Assert.Equal(CommercialGoalActionType.ProtectAvailability, snap.ActNow[0].ActionType);
        Assert.False(snap.ActNow[0].Action.HasPromotionSuggestion);
        Assert.Empty(snap.Preserve);
    }

    [Fact]
    public void NoGoal_e_InvalidGoal_funcionam()
    {
        var plan = Plan(Item(1, CommercialGoalActionType.PrioritizeExcess));
        plan = WithMode(plan, CommercialGoalActionPlanMode.InventoryOnly);
        var noGoal = Compose(NoGoal(), plan);
        Assert.Equal(CommercialGoalStatus.NoGoal, noGoal.GoalStatus);
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, noGoal.PlanMode);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, noGoal.ActNow[0].ActionType);

        var invalid = Compose(InvalidGoal(), plan);
        Assert.False(invalid.Goal!.HasValidGoal);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, invalid.ActNow[0].ActionType);
        Assert.Null(invalid.GoalAmount);
    }

    [Fact]
    public void Financial_unavailable_nao_vira_zero()
    {
        var plan = WithMode(
            Plan(Item(1, CommercialGoalActionType.Monitor)),
            CommercialGoalActionPlanMode.InventoryOnly);
        var snap = Compose(FinancialUnavailable(), plan);
        Assert.Null(snap.RealizedGrossProfit);
        Assert.Null(snap.Goal!.GrossProfit);
        Assert.True(snap.HasLimitation(CentralDecisionLimitation.ContributionUnavailable));
        Assert.Equal(CommercialGoalActionType.Monitor, snap.ActNow[0].ActionType);
    }

    [Fact]
    public void B8_ausente_Limited_ActNow_continua()
    {
        var snap = Compose(BelowPace(), Plan(Item(1, CommercialGoalActionType.PrioritizeExcess)));
        Assert.Equal(CentralDecisionState.Limited, snap.State);
        Assert.NotEqual(CentralDecisionState.Empty, snap.State);
        Assert.Single(snap.ActNow);
        Assert.Null(snap.Contribution);
        Assert.Empty(snap.Preserve);
        Assert.True(snap.HasLimitation(CentralDecisionLimitation.ContributionUnavailable));
    }

    [Fact]
    public void Empty_Operational_Limited_Future_Unavailable()
    {
        var empty = Compose(Achieved(), Plan(), Contribution());
        Assert.Equal(CentralDecisionState.Empty, empty.State);
        Assert.Empty(empty.ActNow);
        Assert.Empty(empty.Preserve);

        var operational = Compose(
            Achieved(),
            Plan(),
            Contribution(Hist(1, 20m)),
            Sources(Intelligence(Turnover(1, InventoryCoverageBand.Normal))));
        Assert.Equal(CentralDecisionState.Operational, operational.State);
        Assert.Single(operational.Preserve);

        var limited = Compose(BelowPace(), Plan(Item(1, CommercialGoalActionType.Monitor)));
        Assert.Equal(CentralDecisionState.Limited, limited.State);

        var promoLimited = CentralDecisionComposer.Compose(
            BelowPace(),
            Plan(Item(1, CommercialGoalActionType.PrioritizeExcess, promotion: true)),
            Contribution(Hist(1, 10m)),
            additionalLimitations: CentralDecisionLimitation.PromotionSourceUnavailable);
        Assert.Equal(CentralDecisionState.Limited, promoLimited.State);
        Assert.True(promoLimited.ActNow[0].Action.HasPromotionSuggestion);

        var future = Compose(
            BelowPace(),
            WithMode(Plan(Item(1, CommercialGoalActionType.PrioritizeExcess)), CommercialGoalActionPlanMode.FutureCompetence));
        Assert.Equal(CentralDecisionState.Future, future.State);
        Assert.Empty(future.ActNow);
        Assert.Empty(future.Preserve);

        var unavailable = Compose(BelowPace(), plan: null);
        Assert.Equal(CentralDecisionState.Unavailable, unavailable.State);
        Assert.Empty(unavailable.ActNow);
        Assert.True(unavailable.HasLimitation(CentralDecisionLimitation.IntelligenceUnavailable));
    }

    [Fact]
    public void Contrato_A_Achieved_estoque_baixo_protege()
    {
        var snap = Compose(
            Achieved(),
            Plan(Item(1, CommercialGoalActionType.ProtectAvailability)));
        Assert.Equal(CommercialGoalActionType.ProtectAvailability, snap.ActNow[0].ActionType);
        Assert.Empty(snap.Preserve);
    }

    [Fact]
    public void Contrato_C_Achieved_excesso()
    {
        var snap = Compose(
            Achieved(),
            Plan(Item(2, CommercialGoalActionType.PrioritizeExcess)));
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, snap.ActNow[0].ActionType);
    }

    [Fact]
    public void Contrato_D_E_F_G_H_I()
    {
        var d = Compose(
            BelowPace(),
            Plan(Item(1, CommercialGoalActionType.ProtectAvailability)));
        Assert.Equal(CommercialGoalActionType.ProtectAvailability, d.ActNow[0].ActionType);

        var e = Compose(
            BelowPace(),
            Plan(Item(2, CommercialGoalActionType.PrioritizeExcess, promotion: true)));
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, e.ActNow[0].ActionType);
        Assert.True(e.ActNow[0].Action.HasPromotionSuggestion);

        var f = Compose(
            BelowPace(),
            Plan(Item(3, CommercialGoalActionType.ProtectAvailability, promotion: false)));
        Assert.False(f.ActNow[0].Action.HasPromotionSuggestion);

        var g = Compose(
            BelowPace(),
            Plan(Item(4, CommercialGoalActionType.ReviewData, promotion: true)));
        Assert.Equal(CommercialGoalActionType.ReviewData, g.ActNow[0].ActionType);

        var h = Compose(
            BelowPace(),
            Plan(Item(5, CommercialGoalActionType.ProtectAvailability)),
            Contribution(Hist(5, 1_000m)));
        Assert.Equal(CommercialGoalActionType.ProtectAvailability, h.ActNow[0].ActionType);

        var i = Compose(
            BelowPace(),
            Plan(Item(6, CommercialGoalActionType.PrioritizeExcess)),
            Contribution(Hist(6, 1_000m)));
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, i.ActNow[0].ActionType);
    }

    [Fact]
    public void Sem_formula_de_meta_nem_threshold_20()
    {
        var goal = BelowPace();
        var snap = Compose(goal, Plan());
        Assert.Same(goal, snap.Goal);
        Assert.Equal(goal.GoalAmount, snap.GoalAmount);
        Assert.Equal(goal.RealizedGrossProfit, snap.RealizedGrossProfit);
        Assert.Equal(goal.Progress?.RemainingAmount, snap.RemainingAmount);
        Assert.Equal(goal.Status, snap.GoalStatus);

        var src = ReadComposerSource();
        Assert.DoesNotContain("coverage > 20", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coverage >= 20", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CoverageDays >=", src, StringComparison.Ordinal);
        Assert.DoesNotContain("20 dias", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExpectedProfit", src, StringComparison.Ordinal);
        Assert.DoesNotContain("PromotionUplift", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AccelerateTurnover", src, StringComparison.Ordinal);
        Assert.DoesNotContain("enum CentralDecisionAction", src, StringComparison.Ordinal);
    }

    [Fact]
    public void SQL_IO_WPF_ausentes()
    {
        var src = ReadComposerSource();
        Assert.DoesNotContain("SqliteConnection", src, StringComparison.Ordinal);
        Assert.DoesNotContain("SqliteCommand", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsService", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", src, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", src, StringComparison.Ordinal);
        Assert.DoesNotContain("UserControl", src, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow", src, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductId_nao_duplica_em_ActNow()
    {
        var snap = Compose(
            BelowPace(),
            Plan(
                Item(1, CommercialGoalActionType.ReviewData),
                Item(1, CommercialGoalActionType.PrioritizeExcess)));
        Assert.Equal(1, Assert.Single(snap.ActNow).ProductId);
        Assert.Equal(CommercialGoalActionType.ReviewData, snap.ActNow[0].ActionType);
    }

    static CentralDecisionSnapshot Compose(
        CommercialGoalSnapshot goal,
        CommercialGoalActionPlanSnapshot? plan,
        CommercialGoalProductContributionSnapshot? contribution = null,
        CommercialGoalActionPlanSources? sources = null) =>
        CentralDecisionComposer.Compose(goal, plan, contribution, sources);

    static CommercialGoalActionPlanSnapshot Plan(params CommercialGoalActionItem[] items) =>
        new()
        {
            Competence = Sep2026,
            ReferenceDate = Sep10,
            Mode = CommercialGoalActionPlanMode.Operational,
            Items = items,
            QueryCount = 10,
        };

    static CommercialGoalActionPlanSnapshot WithMode(
        CommercialGoalActionPlanSnapshot plan,
        CommercialGoalActionPlanMode mode) =>
        new()
        {
            Competence = plan.Competence,
            ReferenceDate = plan.ReferenceDate,
            Mode = mode,
            Items = plan.Items,
            QueryCount = plan.QueryCount,
            Limitations = plan.Limitations,
            GoalStatus = plan.GoalStatus,
        };

    static CommercialGoalActionItem Item(
        int id,
        CommercialGoalActionType type,
        bool promotion = false,
        bool combo = false,
        InventoryPurchaseGuidanceAction guidance = InventoryPurchaseGuidanceAction.None) =>
        new()
        {
            ProductId = id,
            ProductCode = "P" + id,
            ProductName = "Produto " + id,
            ActionType = type,
            HasPromotionSuggestion = promotion,
            HasComboSuggestion = combo,
            PurchaseGuidanceAction = guidance,
            Confidence = InventoryAttentionConfidence.Reliable,
        };

    static CommercialGoalActionItem Clone(
        CommercialGoalActionItem item,
        bool promotion,
        bool combo) =>
        new()
        {
            ProductId = item.ProductId,
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ActionType = item.ActionType,
            HasPromotionSuggestion = promotion,
            HasComboSuggestion = combo,
            PurchaseGuidanceAction = item.PurchaseGuidanceAction,
            Confidence = item.Confidence,
        };

    static CommercialGoalProductContributionSnapshot Contribution(
        params CommercialGoalProductContributionRow[] rows) =>
        new()
        {
            Competence = Sep2026,
            GrossProfitAvailable = true,
            CostQuality = CommercialGoalCostQuality.Exact,
            GrossProfit = rows.Sum(r => r.GrossProfit ?? 0m),
            Rows = rows,
            QueryCount = 1,
        };

    static CommercialGoalProductContributionRow Hist(int id, decimal gp) =>
        new()
        {
            ProductId = id,
            ProductCode = "C" + id,
            ProductName = "Hist " + id,
            Revenue = 100m,
            Cogs = 100m - gp,
            GrossProfit = gp,
            CostQuality = CommercialGoalCostQuality.Exact,
        };

    static CommercialGoalActionPlanSources Sources(
        InventoryIntelligenceSnapshot intelligence,
        InventoryAttentionSnapshot? attention = null,
        InventoryPurchaseGuidanceSnapshot? guidance = null) =>
        new()
        {
            Intelligence = intelligence,
            Attention = attention,
            Guidance = guidance,
        };

    static InventoryIntelligenceSnapshot Intelligence(params ProductTurnoverRow[] rows) =>
        new() { Rows = rows };

    static ProductTurnoverRow Turnover(int id, InventoryCoverageBand band) =>
        new()
        {
            ProductId = id,
            Code = "T" + id,
            Name = "Turnover " + id,
            CoverageBand = band,
            TotalStock = 40,
            HasPhysicalAvailabilityEvidence = true,
        };

    static ProductTurnoverRow Idle(int id, InventoryCoverageBand band)
    {
        var row = Turnover(id, band);
        return new ProductTurnoverRow
        {
            ProductId = row.ProductId,
            Code = row.Code,
            Name = row.Name,
            CoverageBand = band,
            TotalStock = 40,
            HasPhysicalAvailabilityEvidence = true,
            IsIdle = true,
            HistoryDays = 90,
            DaysWithoutSale = 90,
        };
    }

    static InventoryAttentionSnapshot Attention(
        int id,
        InventoryOperatorAction action,
        InventoryAttentionReason reason) =>
        new()
        {
            Results = [new InventoryAttentionResult
            {
                ProductId = id,
                Action = action,
                PrimaryReason = reason,
            }],
            ByProductId = new Dictionary<int, InventoryAttentionResult>
            {
                [id] = new InventoryAttentionResult
                {
                    ProductId = id,
                    Action = action,
                    PrimaryReason = reason,
                },
            },
        };

    static CommercialGoalSnapshot BelowPace() =>
        Goal(12_000m, 3_000m, valid: true);

    static CommercialGoalSnapshot Achieved() =>
        Goal(100m, 100m, valid: true);

    static CommercialGoalSnapshot NoGoal() =>
        CommercialGoalComposer.Compose(
            new CommercialGoalSettingResolution
            {
                Competence = Sep2026,
                Source = CommercialGoalSettingSource.None,
                HasValidGoal = false,
                QueryCount = 2,
            },
            ExactFinancial(50m),
            Sep10);

    static CommercialGoalSnapshot InvalidGoal() =>
        CommercialGoalComposer.Compose(
            new CommercialGoalSettingResolution
            {
                Competence = Sep2026,
                Source = CommercialGoalSettingSource.InvalidDefault,
                HasValidGoal = false,
                QueryCount = 2,
            },
            ExactFinancial(50m),
            Sep10);

    static CommercialGoalSnapshot FinancialUnavailable() =>
        CommercialGoalComposer.Compose(
            new CommercialGoalSettingResolution
            {
                Competence = Sep2026,
                Source = CommercialGoalSettingSource.MonthlyOverride,
                GoalAmount = 12_000m,
                HasValidGoal = true,
                QueryCount = 1,
            },
            new CommercialGoalFinancialSnapshot
            {
                Competence = Sep2026,
                NetCommercialRevenue = 80m,
                Cogs = 0m,
                GrossProfit = null,
                CostQuality = CommercialGoalCostQuality.Unavailable,
                GrossProfitAvailable = false,
            },
            Sep10);

    static CommercialGoalSnapshot Goal(decimal goal, decimal realized, bool valid) =>
        CommercialGoalComposer.Compose(
            new CommercialGoalSettingResolution
            {
                Competence = Sep2026,
                Source = valid
                    ? CommercialGoalSettingSource.MonthlyOverride
                    : CommercialGoalSettingSource.None,
                GoalAmount = valid ? goal : null,
                HasValidGoal = valid,
                QueryCount = 1,
            },
            ExactFinancial(realized),
            Sep10);

    static CommercialGoalFinancialSnapshot ExactFinancial(decimal realized) =>
        new()
        {
            Competence = Sep2026,
            NetCommercialRevenue = realized + 10m,
            Cogs = 10m,
            GrossProfit = realized,
            CostQuality = CommercialGoalCostQuality.Exact,
            GrossProfitAvailable = true,
        };

    static string ReadComposerSource()
    {
        var relative = new[] { "src", "SGDB.App", "Services", "CentralDecisionComposer.cs" };
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
