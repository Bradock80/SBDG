using System.IO;
using System.Text;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71C-B3 — apresentação pura. Traduz B2; não decide.
/// </summary>
public class CentralDecisionPresentationTests
{
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly DateOnly Sep10 = new(2026, 9, 10);

    static readonly string[] Forbidden =
    [
        "vai gerar", "irá gerar", "vai aumentar", "garante", "garantir",
        "bater a meta", "fechar a meta", "venda mais", "faça promoção",
        "dê desconto", "maximize seu lucro", "garanta sua meta",
        "lucro esperado", "lucro potencial", "contribuição futura",
        "aproveite que bateu a meta", "produto intocável", "não vender",
        "segurar vendas", "promova agora",
    ];

    [Fact]
    public void QueryCount_proprio_e_zero()
    {
        Assert.Equal(0, CentralDecisionPresentation.ExpectedQueryCount);
        var presented = Present(OperationalSnap());
        Assert.Equal(0, presented.QueryCount);
    }

    [Fact]
    public void Operational_Limited_Empty_Unavailable_Future()
    {
        Assert.Equal(CentralDecisionState.Operational, Present(OperationalSnap()).State);
        Assert.Equal(
            CentralDecisionPresentation.HeadlineOperational,
            Present(OperationalSnap()).Headline);

        var limited = Present(Composer(BelowPace(), Plan(Item(1, CommercialGoalActionType.Monitor))));
        Assert.Equal(CentralDecisionState.Limited, limited.State);
        Assert.Equal(CentralDecisionPresentation.HeadlineLimited, limited.Headline);
        Assert.Contains(
            limited.Limitations,
            l => l.Title == CentralDecisionPresentation.LimitationContributionTitle);

        var empty = Present(Composer(Achieved(), Plan(), Contribution()));
        Assert.Equal(CentralDecisionState.Empty, empty.State);
        Assert.Equal(CentralDecisionPresentation.HeadlineEmpty, empty.EmptyText);

        var unavailable = Present(Composer(BelowPace(), plan: null));
        Assert.Equal(CentralDecisionState.Unavailable, unavailable.State);
        Assert.Equal(CentralDecisionPresentation.HeadlineUnavailable, unavailable.EmptyText);
        Assert.NotEqual(CentralDecisionPresentation.HeadlineEmpty, unavailable.Headline);

        var future = Present(Composer(
            BelowPace(),
            WithMode(Plan(Item(1, CommercialGoalActionType.PrioritizeExcess)), CommercialGoalActionPlanMode.FutureCompetence)));
        Assert.Equal(CentralDecisionState.Future, future.State);
        Assert.Equal(CentralDecisionPresentation.HeadlineFuture, future.Headline);
        Assert.Empty(future.ActNow);
    }

    [Fact]
    public void Faixa_meta_valida_NoGoal_Invalid_Estimated_Unavailable()
    {
        var valid = Present(OperationalSnap()).GoalStrip;
        Assert.Equal(CommercialGoalPresentation.FormatMoney(12_000m), valid.Goal.ValueText);
        Assert.Equal(CommercialGoalPresentation.FormatMoney(3_000m), valid.Realized.ValueText);
        Assert.Equal(CommercialGoalPresentation.FormatMoney(9_000m), valid.Remaining.ValueText);
        Assert.Equal(CommercialGoalPresentation.StatusBelowPace, valid.Status.ValueText);
        Assert.False(valid.ShowEstimatedBadge);

        var noGoal = Present(Composer(NoGoal(), WithMode(Plan(), CommercialGoalActionPlanMode.InventoryOnly)));
        Assert.Equal(CommercialGoalPresentation.GoalNotConfigured, noGoal.GoalStrip.Goal.ValueText);
        Assert.Equal(CommercialGoalPresentation.StatusNoGoal, noGoal.GoalStrip.Status.ValueText);
        Assert.Contains(CentralDecisionPresentation.InventoryOnlyNote, noGoal.GoalStrip.InventoryOnlyNote, StringComparison.Ordinal);

        var invalid = Present(Composer(InvalidGoal(), WithMode(Plan(), CommercialGoalActionPlanMode.InventoryOnly)));
        Assert.Equal(CommercialGoalPresentation.GoalInvalid, invalid.GoalStrip.Goal.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, invalid.GoalStrip.Remaining.ValueText);

        var estimatedGoal = EstimatedGoal();
        var estimated = Present(Composer(
            estimatedGoal,
            WithMode(Plan(Item(1, CommercialGoalActionType.Monitor)), CommercialGoalActionPlanMode.InventoryOnly)));
        Assert.True(estimated.GoalStrip.ShowEstimatedBadge);
        Assert.Equal(CommercialGoalPresentation.EstimatedBadge, estimated.GoalStrip.EstimatedBadge);

        var unavailable = Present(Composer(
            FinancialUnavailable(),
            WithMode(Plan(Item(1, CommercialGoalActionType.Monitor)), CommercialGoalActionPlanMode.InventoryOnly)));
        Assert.Equal(CommercialGoalPresentation.FormatMoney(12_000m), unavailable.GoalStrip.Goal.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, unavailable.GoalStrip.Realized.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, unavailable.GoalStrip.Remaining.ValueText);
        Assert.DoesNotContain("R$ 0,00", unavailable.GoalStrip.Realized.ValueText, StringComparison.Ordinal);
        Assert.NotEqual(CommercialGoalPresentation.GoalNotConfigured, unavailable.GoalStrip.Goal.ValueText);
        Assert.Contains(CentralDecisionPresentation.InventoryOnlyNote, unavailable.GoalStrip.InventoryOnlyNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Acoes_primarias_O_Que_Por_Que()
    {
        Assert.Equal(CentralDecisionPresentation.WhatReviewData, What(CommercialGoalActionType.ReviewData));
        Assert.Equal(CentralDecisionPresentation.WhyReviewData, Why(CommercialGoalActionType.ReviewData));
        Assert.Contains(CentralDecisionPresentation.CareReviewData, Care(CommercialGoalActionType.ReviewData), StringComparison.Ordinal);

        Assert.Equal(CentralDecisionPresentation.WhatRemoveExpired, What(CommercialGoalActionType.RemoveExpired));
        Assert.Equal(CentralDecisionPresentation.WhatExpiry, What(CommercialGoalActionType.PrioritizeExpiryRisk));
        Assert.Equal(CentralDecisionPresentation.WhatExcess, What(CommercialGoalActionType.PrioritizeExcess));
        Assert.Equal(CentralDecisionPresentation.WhatIdle, What(CommercialGoalActionType.PrioritizeIdle));
        Assert.Equal(CentralDecisionPresentation.WhatProtect, What(CommercialGoalActionType.ProtectAvailability));
        Assert.Equal(CentralDecisionPresentation.WhatMonitor, What(CommercialGoalActionType.Monitor));
        Assert.Equal(CentralDecisionPresentation.WhyExcess, Why(CommercialGoalActionType.PrioritizeExcess));
        Assert.Equal(CentralDecisionPresentation.WhyIdle, Why(CommercialGoalActionType.PrioritizeIdle));
        Assert.Equal(CentralDecisionPresentation.WhyProtect, Why(CommercialGoalActionType.ProtectAvailability));
    }

    [Fact]
    public void Complementos_so_quando_B7_autoriza()
    {
        var with = PresentAct(Item(1, CommercialGoalActionType.PrioritizeExcess, promotion: true, combo: true));
        Assert.Equal(CentralDecisionPresentation.PromotionText, with.PromotionText);
        Assert.Equal(CentralDecisionPresentation.ComboText, with.ComboText);

        var without = PresentAct(Item(2, CommercialGoalActionType.PrioritizeExcess));
        Assert.Equal("", without.PromotionText);
        Assert.Equal("", without.ComboText);
        Assert.False(without.HasPromotionSuggestion);
        Assert.False(without.HasComboSuggestion);

        var review = PresentAct(Item(3, CommercialGoalActionType.ReviewData, promotion: true, combo: true));
        Assert.Equal("", review.PromotionText);
        Assert.Equal("", review.ComboText);

        var expired = PresentAct(Item(5, CommercialGoalActionType.RemoveExpired, promotion: true, combo: true));
        Assert.Equal(CentralDecisionPresentation.WhatRemoveExpired, expired.WhatText);
        Assert.Equal("", expired.PromotionText);
        Assert.Equal("", expired.ComboText);

        var protect = PresentAct(Item(
            4,
            CommercialGoalActionType.ProtectAvailability,
            promotion: true,
            combo: true,
            guidance: InventoryPurchaseGuidanceAction.ConsiderReplenishment));
        Assert.Equal("", protect.PromotionText);
        Assert.Equal("", protect.ComboText);
        Assert.Equal(CentralDecisionPresentation.CareConsiderReplenish, protect.ReplenishmentText);
        Assert.Contains(CentralDecisionPresentation.CareProtect, protect.CareText, StringComparison.Ordinal);
    }

    [Fact]
    public void B8_Exact_Estimated_Unavailable_e_GP_negativo()
    {
        var exact = PresentAct(Item(1, CommercialGoalActionType.Monitor), Hist(1, 40m, CommercialGoalCostQuality.Exact));
        Assert.Equal(CommercialGoalPresentation.FormatMoney(40m), exact.GrossProfitText);
        Assert.Equal(CommercialGoalProductContributionPresentation.QualityExact, exact.CostQualityText);

        var estimated = PresentAct(
            Item(2, CommercialGoalActionType.Monitor),
            Hist(2, 12m, CommercialGoalCostQuality.EstimatedLegacy));
        Assert.Equal(CommercialGoalProductContributionPresentation.QualityEstimated, estimated.CostQualityText);

        var missing = Hist(3, 0m, CommercialGoalCostQuality.Unavailable);
        missing = new CommercialGoalProductContributionRow
        {
            ProductId = 3,
            GrossProfit = null,
            CostQuality = CommercialGoalCostQuality.Unavailable,
        };
        var unavailable = PresentAct(Item(3, CommercialGoalActionType.Monitor), missing);
        Assert.Equal(CommercialGoalPresentation.EmDash, unavailable.GrossProfitText);
        Assert.Equal(CommercialGoalProductContributionPresentation.QualityUnavailable, unavailable.CostQualityText);

        var negative = PresentAct(
            Item(4, CommercialGoalActionType.Monitor),
            Hist(4, -25m, CommercialGoalCostQuality.Exact));
        Assert.Contains("-", negative.GrossProfitText, StringComparison.Ordinal);
        Assert.Equal(CommercialGoalPresentation.FormatMoney(-25m), negative.GrossProfitText);
    }

    [Fact]
    public void Preserve_e_Achieved_Normal_sem_aceleracao()
    {
        var snap = Composer(
            Achieved(),
            Plan(),
            Contribution(Hist(9, 500m)),
            Sources(Intelligence(Turnover(9))));
        var presented = Present(snap);
        Assert.Empty(presented.ActNow);
        var keep = Assert.Single(presented.Preserve);
        Assert.Equal(9, keep.ProductId);
        Assert.Equal("Cobertura normal", keep.CoverageText);
        Assert.Equal(CentralDecisionPresentation.PreserveTitle, presented.PreserveTitle);
        var visible = Visible(presented);
        Assert.DoesNotContain(CentralDecisionPresentation.PromotionText, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(CentralDecisionPresentation.ComboText, visible, StringComparison.Ordinal);
        Assert.DoesNotContain("aproveite", visible, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BelowPace_ProtectAvailability_sem_promocao()
    {
        var presented = Present(Composer(
            BelowPace(),
            Plan(Item(4, CommercialGoalActionType.ProtectAvailability, promotion: true, combo: true))));
        var row = Assert.Single(presented.ActNow);
        Assert.Equal(CentralDecisionPresentation.WhatProtect, row.WhatText);
        Assert.Equal(CentralDecisionPresentation.CareConsiderReplenish, row.ReplenishmentText);
        Assert.Equal("", row.PromotionText);
        Assert.Equal("", row.ComboText);
    }

    [Fact]
    public void Ordem_ActNow_e_Preserve_preservada()
    {
        var act = Present(Composer(
            BelowPace(),
            Plan(
                Item(1, CommercialGoalActionType.ReviewData),
                Item(2, CommercialGoalActionType.PrioritizeExcess),
                Item(3, CommercialGoalActionType.Monitor))));
        Assert.Equal([1, 2, 3], act.ActNow.Select(x => x.ProductId).ToArray());

        var preserve = Present(Composer(
            Achieved(),
            Plan(),
            Contribution(Hist(11, 90m), Hist(13, 80m), Hist(10, 30m)),
            Sources(Intelligence(Turnover(10), Turnover(11), Turnover(13)))));
        Assert.Equal([11, 13, 10], preserve.Preserve.Select(x => x.ProductId).ToArray());
    }

    [Fact]
    public void B3_nao_aplica_novo_top_nem_reordena()
    {
        var actions = Enumerable.Range(1, 6)
            .Select(i => Item(i, CommercialGoalActionType.Monitor))
            .ToArray();
        var actNow = actions
            .Select(action => new CentralDecisionActNowItem { Action = action })
            .ToArray();
        var presented = Present(new CentralDecisionSnapshot
        {
            State = CentralDecisionState.Operational,
            Competence = Sep2026,
            ReferenceDate = Sep10,
            Goal = BelowPace(),
            ActNow = actNow,
        });
        Assert.Equal(6, presented.ActNow.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], presented.ActNow.Select(x => x.ProductId).ToArray());
    }

    [Fact]
    public void Limitacoes_PTBR_sem_nomes_de_enum()
    {
        var action = new CommercialGoalActionItem
        {
            ProductId = 8,
            ProductCode = "P8",
            ProductName = "Produto 8",
            ActionType = CommercialGoalActionType.PrioritizeIdle,
            PurchaseGuidanceAction = InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            Limitations = CommercialGoalActionLimitation.LocationLimitation
                | CommercialGoalActionLimitation.InsufficientHistory
                | CommercialGoalActionLimitation.NoPhysicalEvidence
                | CommercialGoalActionLimitation.StructuralDataIssue
                | CommercialGoalActionLimitation.LegacyCostEstimate,
            Confidence = InventoryAttentionConfidence.Unavailable,
        };
        var row = PresentAct(action);
        Assert.Contains(CentralDecisionPresentation.CareLocation, row.CareText, StringComparison.Ordinal);
        Assert.Contains(CentralDecisionPresentation.CareInsufficient, row.CareText, StringComparison.Ordinal);
        Assert.Contains(CentralDecisionPresentation.CareNoEvidence, row.CareText, StringComparison.Ordinal);
        Assert.Contains(CentralDecisionPresentation.CareStructural, row.CareText, StringComparison.Ordinal);
        Assert.Contains(CentralDecisionPresentation.CareEstimated, row.CareText, StringComparison.Ordinal);
        Assert.Contains(CentralDecisionPresentation.CareDoNotReplenish, row.CareText, StringComparison.Ordinal);
        Assert.DoesNotContain("LocationLimitation", row.CareText, StringComparison.Ordinal);
        Assert.DoesNotContain("InsufficientHistory", row.CareText, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyCostEstimate", row.CareText, StringComparison.Ordinal);

        var envelope = Present(Composer(
            EstimatedGoal(),
            WithLimitations(
                WithMode(Plan(Item(1, CommercialGoalActionType.Monitor)), CommercialGoalActionPlanMode.InventoryOnly),
                CommercialGoalActionLimitation.FinancialUnavailable)));
        var visible = Visible(envelope);
        Assert.Contains(CommercialGoalPresentation.EstimatedBadge, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(CommercialGoalActionLimitation.FinancialUnavailable), visible, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(CentralDecisionLimitation.ContributionUnavailable), visible, StringComparison.Ordinal);
        Assert.Contains(CentralDecisionPresentation.InventoryOnlyNote, envelope.GoalStrip.InventoryOnlyNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_causalidade_SQL_IO_WPF()
    {
        var presented = Present(Composer(
            BelowPace(),
            Plan(Item(1, CommercialGoalActionType.PrioritizeExcess, promotion: true, combo: true)),
            Contribution(Hist(1, 40m))));
        var visible = Visible(presented);
        foreach (var phrase in Forbidden)
            Assert.DoesNotContain(phrase, visible, StringComparison.OrdinalIgnoreCase);

        var src = ReadSource();
        Assert.DoesNotContain("SqliteConnection", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsService", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", src, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", src, StringComparison.Ordinal);
        Assert.DoesNotContain("UserControl", src, StringComparison.Ordinal);
        foreach (var phrase in Forbidden)
            Assert.DoesNotContain(phrase, src, StringComparison.OrdinalIgnoreCase);
    }

    static string What(CommercialGoalActionType type) =>
        PresentAct(Item(1, type)).WhatText;

    static string Why(CommercialGoalActionType type) =>
        PresentAct(Item(1, type)).WhyText;

    static string Care(CommercialGoalActionType type) =>
        PresentAct(Item(1, type)).CareText;

    static CentralDecisionActNowPresentation PresentAct(
        CommercialGoalActionItem action,
        CommercialGoalProductContributionRow? row = null) =>
        Present(Composer(
            BelowPace(),
            Plan(action),
            row is null ? null : Contribution(row))).ActNow[0];

    static CentralDecisionPresentationSnapshot Present(CentralDecisionSnapshot snapshot) =>
        CentralDecisionPresentation.Apply(snapshot);

    static CentralDecisionSnapshot OperationalSnap() =>
        Composer(
            BelowPace(),
            Plan(),
            Contribution(Hist(1, 20m)),
            Sources(Intelligence(Turnover(1))));

    static CentralDecisionSnapshot Composer(
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
            Limitations = plan.Limitations,
        };

    static CommercialGoalActionPlanSnapshot WithLimitations(
        CommercialGoalActionPlanSnapshot plan,
        CommercialGoalActionLimitation limitations) =>
        new()
        {
            Competence = plan.Competence,
            ReferenceDate = plan.ReferenceDate,
            Mode = plan.Mode,
            Items = plan.Items,
            Limitations = limitations,
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

    static CommercialGoalProductContributionSnapshot Contribution(
        params CommercialGoalProductContributionRow[] rows) =>
        new()
        {
            Competence = Sep2026,
            GrossProfitAvailable = true,
            CostQuality = CommercialGoalCostQuality.Exact,
            Rows = rows,
            QueryCount = 1,
        };

    static CommercialGoalProductContributionRow Hist(
        int id,
        decimal gp,
        CommercialGoalCostQuality quality = CommercialGoalCostQuality.Exact) =>
        new()
        {
            ProductId = id,
            ProductCode = "C" + id,
            ProductName = "Hist " + id,
            Revenue = 100m,
            Cogs = 100m - gp,
            GrossProfit = gp,
            GrossMarginPercent = 10m,
            CostQuality = quality,
        };

    static CommercialGoalActionPlanSources Sources(InventoryIntelligenceSnapshot intelligence) =>
        new() { Intelligence = intelligence };

    static InventoryIntelligenceSnapshot Intelligence(params ProductTurnoverRow[] rows) =>
        new() { Rows = rows };

    static ProductTurnoverRow Turnover(int id) =>
        new()
        {
            ProductId = id,
            Code = "T" + id,
            Name = "Turnover " + id,
            CoverageBand = InventoryCoverageBand.Normal,
            TotalStock = 40,
            HasPhysicalAvailabilityEvidence = true,
        };

    static CommercialGoalSnapshot BelowPace() => Goal(12_000m, 3_000m, valid: true);

    static CommercialGoalSnapshot Achieved() => Goal(100m, 100m, valid: true);

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

    static CommercialGoalSnapshot EstimatedGoal() =>
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
                Cogs = 30m,
                GrossProfit = 50m,
                CostQuality = CommercialGoalCostQuality.EstimatedLegacy,
                GrossProfitAvailable = true,
                ProfitIsEstimated = true,
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

    static string Visible(CentralDecisionPresentationSnapshot presented)
    {
        var sb = new StringBuilder();
        sb.Append(presented.Title).Append(' ')
            .Append(presented.Subtitle).Append(' ')
            .Append(presented.Headline).Append(' ')
            .Append(presented.SupportingText).Append(' ')
            .Append(presented.EmptyText).Append(' ')
            .Append(presented.PreserveTitle).Append(' ')
            .Append(presented.PreserveSubtitle).Append(' ')
            .Append(presented.GoalStrip.Goal.ValueText).Append(' ')
            .Append(presented.GoalStrip.Realized.ValueText).Append(' ')
            .Append(presented.GoalStrip.Remaining.ValueText).Append(' ')
            .Append(presented.GoalStrip.Status.ValueText).Append(' ')
            .Append(presented.GoalStrip.InventoryOnlyNote).Append(' ')
            .Append(presented.GoalStrip.EstimatedBadge);
        foreach (var item in presented.ActNow)
        {
            sb.Append(' ').Append(item.WhatText)
                .Append(' ').Append(item.WhyText)
                .Append(' ').Append(item.CareText)
                .Append(' ').Append(item.PromotionText)
                .Append(' ').Append(item.ComboText)
                .Append(' ').Append(item.ReplenishmentText)
                .Append(' ').Append(item.GrossProfitText);
        }

        foreach (var item in presented.Preserve)
        {
            sb.Append(' ').Append(item.ProductTitle)
                .Append(' ').Append(item.GrossProfitText)
                .Append(' ').Append(item.CoverageText);
        }

        foreach (var limitation in presented.Limitations)
            sb.Append(' ').Append(limitation.Title).Append(' ').Append(limitation.Body);
        return sb.ToString();
    }

    static string ReadSource()
    {
        var relative = new[] { "src", "SGDB.App", "Models", "CentralDecisionPresentation.cs" };
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
