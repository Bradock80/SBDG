using System.IO;
using System.Text;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71B-B7 — presentation PT-BR do plano de atenção comercial.
/// </summary>
public class CommercialGoalActionPlanPresentationTests
{
    static readonly DateOnly Sep15 = new(2026, 9, 15);
    static readonly DateOnly Aug1 = new(2026, 8, 1);
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);

    static readonly string[] Forbidden =
    [
        "URGENTE",
        "VOCÊ ESTÁ PERDENDO DINHEIRO",
        "VENDA AGORA",
        "atingir a meta",
        "aumentará seu lucro",
        "lucro incremental",
        "vai gerar",
        "fará você atingir",
        "unidades deste produto para atingir",
    ];

    [Fact]
    public void QueryCount_e_zero()
    {
        Assert.Equal(0, CommercialGoalActionPlanPresentation.ExpectedQueryCount);
        Assert.Equal("Prioridades comerciais", CommercialGoalActionPlanPresentation.SectionTitle);
    }

    [Fact]
    public void Apply_rejeita_nulo()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CommercialGoalActionPlanPresentation.Apply(null!));
    }

    [Fact]
    public void Headline_por_estado()
    {
        Assert.Equal(
            CommercialGoalActionPlanPresentation.HeadlineBelowPace,
            Present(BelowPace(), ExcessItem(1)).Headline);
        Assert.Equal(
            CommercialGoalActionPlanPresentation.HeadlineOnPace,
            Present(OnPace(), ExcessItem(1)).Headline);
        Assert.Equal(
            CommercialGoalActionPlanPresentation.HeadlineAbovePace,
            Present(AbovePace(), ExcessItem(1)).Headline);
        Assert.Equal(
            CommercialGoalActionPlanPresentation.HeadlineAchieved,
            Present(Achieved(), ExcessItem(1)).Headline);
        Assert.Equal(
            CommercialGoalActionPlanPresentation.HeadlineNoGoal,
            Present(NoGoal(), ExcessItem(1)).Headline);
        Assert.Equal(
            CommercialGoalActionPlanPresentation.HeadlineInvalidGoal,
            Present(InvalidGoal(), ExcessItem(1)).Headline);
        Assert.Equal(
            CommercialGoalActionPlanPresentation.HeadlineUnavailable,
            Present(Unavailable(), ExcessItem(1)).Headline);
        Assert.Equal(
            CommercialGoalActionPlanPresentation.HeadlineFuture,
            Present(NotStarted(), ExcessItem(1)).Headline);
    }

    [Fact]
    public void Empty_state()
    {
        var presented = Present(OnPace());
        Assert.True(presented.IsEmpty);
        Assert.Empty(presented.Items);
        Assert.Equal(CommercialGoalActionPlanPresentation.EmptyMessage, presented.EmptyText);
        Assert.Contains(
            CommercialGoalActionPlanPresentation.EmptyMessage,
            presented.SupportingText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ate_cinco_itens_com_produto_motivo_confianca()
    {
        var items = Enumerable.Range(1, 5).Select(ExcessItem).ToArray();
        var presented = Present(OnPace(), items);
        Assert.Equal(5, presented.Items.Count);
        Assert.Contains("5 pontos merecem atenção", presented.SupportingText, StringComparison.Ordinal);
        var row = presented.Items[0];
        Assert.Equal("P1 — Produto 1", row.ProductTitle);
        Assert.Equal(CommercialGoalActionPlanPresentation.ReasonExcess, row.ReasonText);
        Assert.Equal(InventoryAttentionPresentation.ConfidenceReliable, row.ConfidenceText);
        Assert.Equal(InventoryAttentionPresentation.PriorityMedium, row.PriorityText);
    }

    [Fact]
    public void Complementos_promocao_combo_reposicao()
    {
        var presented = Present(BelowPace(), new CommercialGoalActionItem
        {
            ProductId = 7,
            ProductCode = "X7",
            ProductName = "Excesso",
            ActionType = CommercialGoalActionType.PrioritizeExcess,
            Priority = InventoryAttentionPriority.Medium,
            Confidence = InventoryAttentionConfidence.Limited,
            HasPromotionSuggestion = true,
            HasComboSuggestion = true,
            PurchaseGuidanceAction = InventoryPurchaseGuidanceAction.DoNotReplenishNow,
        });
        var row = Assert.Single(presented.Items);
        Assert.True(row.HasComplements);
        Assert.Contains(CommercialGoalActionPlanPresentation.ComplementPromotion, row.ComplementsText, StringComparison.Ordinal);
        Assert.Contains(CommercialGoalActionPlanPresentation.ComplementCombo, row.ComplementsText, StringComparison.Ordinal);
        Assert.Contains(CommercialGoalActionPlanPresentation.ComplementDoNotReplenish, row.ComplementsText, StringComparison.Ordinal);
        Assert.Equal(InventoryAttentionPresentation.ConfidenceLimited, row.ConfidenceText);
    }

    [Fact]
    public void Future_nao_lista_acoes_mesmo_com_candidatos_no_snapshot()
    {
        var presented = Present(NotStarted(), ExcessItem(1), ExcessItem(2));
        Assert.True(presented.IsFutureCompetence);
        Assert.Empty(presented.Items);
        Assert.Equal(CommercialGoalActionPlanPresentation.HeadlineFuture, presented.Headline);
        Assert.Equal(CommercialGoalActionPlanPresentation.SupportingFuture, presented.SupportingText);
    }

    [Fact]
    public void NoGoal_e_unavailable_nao_perseguem_meta()
    {
        var noGoal = Present(NoGoal(), ExcessItem(1));
        Assert.Contains(
            CommercialGoalActionPlanPresentation.SupportingNoGoal,
            noGoal.SupportingText,
            StringComparison.Ordinal);

        var unavailable = Present(Unavailable(), ExcessItem(1));
        Assert.Contains(
            CommercialGoalActionPlanPresentation.SupportingUnavailable,
            unavailable.SupportingText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("R$ 0,00", Visible(unavailable), StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_causalidade_nem_alarme()
    {
        var presented = Present(BelowPace(), new CommercialGoalActionItem
        {
            ProductId = 1,
            ProductCode = "A",
            ProductName = "Alfa",
            ActionType = CommercialGoalActionType.PrioritizeExcess,
            HasPromotionSuggestion = true,
            HasComboSuggestion = true,
            Priority = InventoryAttentionPriority.Medium,
            Confidence = InventoryAttentionConfidence.Reliable,
        });
        var visible = Visible(presented);
        foreach (var phrase in Forbidden)
            Assert.DoesNotContain(phrase, visible, StringComparison.OrdinalIgnoreCase);

        var src = ReadSource("src", "SGDB.App", "Models", "CommercialGoalActionPlanPresentation.cs");
        foreach (var phrase in Forbidden)
            Assert.DoesNotContain(phrase, src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ganhar R$", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("10%", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Presentation_e_pura()
    {
        var src = ReadSource("src", "SGDB.App", "Models", "CommercialGoalActionPlanPresentation.cs");
        Assert.DoesNotContain("SELECT", src, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenConnection", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", src, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", src, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService", src, StringComparison.Ordinal);
    }

    static CommercialGoalActionPlanPresentationSnapshot Present(
        CommercialGoalSnapshot goal,
        params CommercialGoalActionItem[] items)
    {
        var plan = CommercialGoalActionPlanComposer.Compose(
            goal,
            items.Length == 0
                ? new CommercialGoalActionPlanSources()
                : new CommercialGoalActionPlanSources
                {
                    Attention = AttentionFrom(items),
                    Promotion = PromotionFrom(items),
                    Combos = ComboFrom(items),
                    Guidance = GuidanceFrom(items),
                    Intelligence = IntelligenceFrom(items),
                });
        return CommercialGoalActionPlanPresentation.Apply(plan);
    }

    static InventoryAttentionSnapshot AttentionFrom(CommercialGoalActionItem[] items)
    {
        var results = new List<InventoryAttentionResult>(items.Length);
        foreach (var item in items)
        {
            results.Add(new InventoryAttentionResult
            {
                ProductId = item.ProductId,
                Action = item.ActionType switch
                {
                    CommercialGoalActionType.ReviewData => InventoryOperatorAction.ReviewData,
                    CommercialGoalActionType.RemoveExpired => InventoryOperatorAction.RemoveExpired,
                    CommercialGoalActionType.PrioritizeExpiryRisk => InventoryOperatorAction.PrioritizeSale,
                    CommercialGoalActionType.PrioritizeExcess => InventoryOperatorAction.EvaluateExcess,
                    CommercialGoalActionType.PrioritizeIdle => InventoryOperatorAction.Monitor,
                    CommercialGoalActionType.Monitor => InventoryOperatorAction.Monitor,
                    _ => InventoryOperatorAction.None,
                },
                PrimaryReason = item.ActionType switch
                {
                    CommercialGoalActionType.PrioritizeIdle => InventoryAttentionReason.Idle,
                    CommercialGoalActionType.PrioritizeExcess => InventoryAttentionReason.ProjectedExcess30,
                    CommercialGoalActionType.RemoveExpired => InventoryAttentionReason.Expired,
                    CommercialGoalActionType.PrioritizeExpiryRisk => InventoryAttentionReason.SurplusAtExpiry,
                    CommercialGoalActionType.ReviewData => InventoryAttentionReason.NegativeStock,
                    _ => InventoryAttentionReason.DatedWithoutSurplusInWindow,
                },
                Family = item.ActionType == CommercialGoalActionType.PrioritizeIdle
                    ? InventoryAttentionFamily.Turnover
                    : InventoryAttentionFamily.Excess,
                Priority = item.Priority,
                Confidence = item.Confidence,
            });
        }

        return new InventoryAttentionSnapshot
        {
            Results = results,
            ByProductId = results.ToDictionary(r => r.ProductId),
        };
    }

    static InventoryPromotionSuggestionSnapshot PromotionFrom(CommercialGoalActionItem[] items)
    {
        var rows = new List<InventoryPromotionSuggestionRow>();
        foreach (var item in items)
        {
            if (!item.HasPromotionSuggestion)
                continue;
            rows.Add(new InventoryPromotionSuggestionRow
            {
                ProductId = item.ProductId,
                Suggestion = new InventoryPromotionSuggestionResult
                {
                    ProductId = item.ProductId,
                    Status = InventoryPromotionSuggestionStatus.Suggested,
                    Action = InventoryPromotionSuggestionAction.ConsiderPromotion,
                    Confidence = item.Confidence,
                },
            });
        }

        return new InventoryPromotionSuggestionSnapshot
        {
            Rows = rows,
            ByProductId = rows.ToDictionary(r => r.ProductId),
        };
    }

    static InventoryComboIntelligenceSnapshot ComboFrom(CommercialGoalActionItem[] items)
    {
        var groups = new List<InventoryComboTargetSuggestionGroup>();
        foreach (var item in items)
        {
            if (!item.HasComboSuggestion)
                continue;
            groups.Add(new InventoryComboTargetSuggestionGroup
            {
                ProductId = item.ProductId,
                Eligibility = new InventoryComboTargetEligibility
                {
                    ProductId = item.ProductId,
                    Status = ComboEligibilityStatus.Eligible,
                    Reason = ComboTargetEligibilityReason.ProjectedExcess,
                },
                Suggestions =
                [
                    new InventoryComboSuggestion
                    {
                        TargetProductId = item.ProductId,
                        AnchorProductId = item.ProductId + 100,
                        Confidence = InventoryAttentionConfidence.Reliable,
                        PairEvidence = InventoryComboPairEvidence.Observed,
                    },
                ],
            });
        }

        return new InventoryComboIntelligenceSnapshot
        {
            Targets = groups,
            ByProductId = groups.ToDictionary(g => g.ProductId),
        };
    }

    static InventoryPurchaseGuidanceSnapshot GuidanceFrom(CommercialGoalActionItem[] items)
    {
        var results = new List<InventoryPurchaseGuidanceResult>();
        foreach (var item in items)
        {
            if (item.PurchaseGuidanceAction == InventoryPurchaseGuidanceAction.None)
                continue;
            results.Add(new InventoryPurchaseGuidanceResult
            {
                ProductId = item.ProductId,
                Action = item.PurchaseGuidanceAction,
                Status = item.PurchaseGuidanceAction == InventoryPurchaseGuidanceAction.ReviewData
                    ? InventoryPurchaseGuidanceStatus.ReviewData
                    : InventoryPurchaseGuidanceStatus.GuidanceAvailable,
                PrimaryReason = InventoryPurchaseGuidanceReason.ProjectedExcess30,
                Confidence = InventoryAttentionConfidence.Reliable,
            });
        }

        return new InventoryPurchaseGuidanceSnapshot
        {
            Results = results,
            ByProductId = results.ToDictionary(r => r.ProductId),
        };
    }

    static InventoryIntelligenceSnapshot IntelligenceFrom(CommercialGoalActionItem[] items) =>
        new()
        {
            Rows = items.Select(i => new ProductTurnoverRow
            {
                ProductId = i.ProductId,
                Code = i.ProductCode.Length > 0 ? i.ProductCode : "P" + i.ProductId,
                Name = i.ProductName.Length > 0 ? i.ProductName : "Produto " + i.ProductId,
            }).ToArray(),
        };

    static CommercialGoalActionItem ExcessItem(int id) =>
        new()
        {
            ProductId = id,
            ProductCode = "P" + id,
            ProductName = "Produto " + id,
            ActionType = CommercialGoalActionType.PrioritizeExcess,
            Priority = InventoryAttentionPriority.Medium,
            Confidence = InventoryAttentionConfidence.Reliable,
        };

    static string Visible(CommercialGoalActionPlanPresentationSnapshot presented)
    {
        var sb = new StringBuilder();
        sb.Append(presented.SectionTitle).Append('\n');
        sb.Append(presented.Headline).Append('\n');
        sb.Append(presented.SupportingText).Append('\n');
        sb.Append(presented.EmptyText).Append('\n');
        foreach (var item in presented.Items)
        {
            sb.Append(item.PriorityText).Append('\n');
            sb.Append(item.ProductTitle).Append('\n');
            sb.Append(item.ReasonText).Append('\n');
            sb.Append(item.ComplementsText).Append('\n');
            sb.Append(item.ConfidenceText).Append('\n');
        }

        return sb.ToString();
    }

    static CommercialGoalSnapshot BelowPace() => Goal(ValidOverride(12_000m), Gross(5_000m), Sep15);
    static CommercialGoalSnapshot OnPace() => Goal(ValidOverride(12_000m), Gross(6_000m), Sep15);
    static CommercialGoalSnapshot AbovePace() => Goal(ValidOverride(12_000m), Gross(8_000m), Sep15);
    static CommercialGoalSnapshot Achieved() => Goal(ValidOverride(12_000m), Gross(12_000m), Sep15);
    static CommercialGoalSnapshot NoGoal() => Goal(None(), Exact(90m, 20m), Sep15);
    static CommercialGoalSnapshot InvalidGoal() => Goal(InvalidDefault(), Exact(90m, 20m), Sep15);
    static CommercialGoalSnapshot NotStarted() => Goal(ValidOverride(12_000m), Exact(0m, 0m), Aug1);
    static CommercialGoalSnapshot Unavailable() =>
        Goal(
            ValidOverride(12_000m),
            new CommercialGoalFinancialSnapshot
            {
                Competence = Sep2026,
                NetCommercialRevenue = 10m,
                CostQuality = CommercialGoalCostQuality.Unavailable,
                GrossProfitAvailable = false,
            },
            Sep15);

    static CommercialGoalSnapshot Goal(
        CommercialGoalSettingResolution setting,
        CommercialGoalFinancialSnapshot financial,
        DateOnly reference) =>
        CommercialGoalComposer.Compose(setting, financial, reference);

    static CommercialGoalSettingResolution None() =>
        new() { Competence = Sep2026, Source = CommercialGoalSettingSource.None, QueryCount = 2 };

    static CommercialGoalSettingResolution ValidOverride(decimal amount) =>
        new()
        {
            Competence = Sep2026,
            Source = CommercialGoalSettingSource.MonthlyOverride,
            GoalAmount = amount,
            HasValidGoal = true,
            QueryCount = 1,
        };

    static CommercialGoalSettingResolution InvalidDefault() =>
        new()
        {
            Competence = Sep2026,
            Source = CommercialGoalSettingSource.InvalidDefault,
            QueryCount = 2,
        };

    static CommercialGoalFinancialSnapshot Exact(decimal revenue, decimal cogs) =>
        new()
        {
            Competence = Sep2026,
            NetCommercialRevenue = revenue,
            Cogs = cogs,
            GrossProfit = revenue - cogs,
            CostQuality = CommercialGoalCostQuality.Exact,
            GrossProfitAvailable = true,
        };

    static CommercialGoalFinancialSnapshot Gross(decimal gross) =>
        Exact(gross >= 0 ? gross + 40m : 10m, (gross >= 0 ? gross + 40m : 10m) - gross);

    static string ReadSource(params string[] relative)
    {
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
