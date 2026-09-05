using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71B-B7 — compositor qualitativo do plano de atenção comercial.
/// Puro: consome snapshots 70E/70F/70G/71A. Sem SQL, persistência ou causalidade financeira.
/// </summary>
public class CommercialGoalActionPlanComposerTests
{
    static readonly DateOnly Sep15 = new(2026, 9, 15);
    static readonly DateOnly Aug1 = new(2026, 8, 1);
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);

    [Fact]
    public void QueryCount_proprio_e_zero_e_maximo_cinco()
    {
        Assert.Equal(0, CommercialGoalActionPlanComposer.OwnQueryCount);
        Assert.Equal(0, CommercialGoalActionPlanSnapshot.OwnQueryCount);
        Assert.Equal(5, CommercialGoalActionPlanComposer.MaxActions);
        Assert.Equal(5, CommercialGoalActionPlanSnapshot.MaxActions);
        Assert.Equal(0, CommercialGoalActionPlanSourceLoader.ExpectedQueryCount);
        Assert.Equal(1, CommercialGoalActionPlanSourceLoader.ExpectedProjectionLoads);
        Assert.Equal(1, CommercialGoalActionPlanSourceLoader.ExpectedAttentionBuilds);
        Assert.Equal(1, CommercialGoalActionPlanSourceLoader.ExpectedFactsLoads);
        Assert.Equal(1, CommercialGoalActionPlanSourceLoader.ExpectedMarginLoads);
        Assert.Equal(1, CommercialGoalActionPlanSourceLoader.ExpectedGuidanceComposes);
        Assert.Equal(1, CommercialGoalActionPlanSourceLoader.ExpectedPromotionComposes);
        Assert.Equal(1, CommercialGoalActionPlanSourceLoader.ExpectedComboComposes);
        Assert.Equal(10, CommercialGoalActionPlanSourceLoader.InheritedPipelineQueryCount);
    }

    [Fact]
    public void Compose_rejeita_nulo()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CommercialGoalActionPlanComposer.Compose(null!));
    }

    [Fact]
    public void ReviewData_vence_oportunidade_comercial()
    {
        var plan = Compose(
            BelowPace(),
            Sources(
                Attention(Review(1), Excess(2)),
                Promotion(Suggested(1), Suggested(2)),
                combos: Combos(SafeCombo(1), SafeCombo(2))));

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(1, plan.Items[0].ProductId);
        Assert.Equal(CommercialGoalActionType.ReviewData, plan.Items[0].ActionType);
        Assert.False(plan.Items[0].HasPromotionSuggestion);
        Assert.False(plan.Items[0].HasComboSuggestion);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, plan.Items[1].ActionType);
        Assert.True(plan.Items[1].HasPromotionSuggestion);
    }

    [Fact]
    public void Vencido_vence_promocao_e_combo()
    {
        var plan = Compose(
            BelowPace(),
            Sources(
                Attention(Expired(3), Excess(4)),
                Promotion(Suggested(3), Suggested(4)),
                combos: Combos(SafeCombo(3))));

        Assert.Equal(CommercialGoalActionType.RemoveExpired, plan.Items[0].ActionType);
        Assert.Equal(3, plan.Items[0].ProductId);
        Assert.False(plan.Items[0].HasPromotionSuggestion);
        Assert.False(plan.Items[0].HasComboSuggestion);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, plan.Items[1].ActionType);
    }

    [Fact]
    public void Risco_de_validade_e_priorizado()
    {
        var plan = Compose(
            OnPace(),
            Sources(Attention(ExpiryRisk(10), Excess(11), Idle(12))));

        Assert.Equal(
            new[]
            {
                CommercialGoalActionType.PrioritizeExpiryRisk,
                CommercialGoalActionType.PrioritizeExcess,
                CommercialGoalActionType.PrioritizeIdle,
            },
            plan.Items.Select(i => i.ActionType).ToArray());
        Assert.Equal(new[] { 10, 11, 12 }, plan.Items.Select(i => i.ProductId).ToArray());
    }

    [Fact]
    public void Excesso_e_priorizado()
    {
        var plan = Compose(OnPace(), Sources(Attention(Excess(20), Idle(21), MonitorDated(22))));
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, plan.Items[0].ActionType);
        Assert.Equal(20, plan.Items[0].ProductId);
    }

    [Fact]
    public void Idle_e_priorizado()
    {
        var plan = Compose(OnPace(), Sources(Attention(Idle(30), MonitorDated(31))));
        Assert.Equal(CommercialGoalActionType.PrioritizeIdle, plan.Items[0].ActionType);
        Assert.Equal(30, plan.Items[0].ProductId);
    }

    [Fact]
    public void ConsiderReplenishment_nao_vira_promocao_principal()
    {
        var plan = Compose(
            BelowPace(),
            Sources(
                Attention(Clear(40)),
                Promotion(Suggested(40)),
                Guidance(Replenish(40)),
                Combos(SafeCombo(40))));

        var item = Assert.Single(plan.Items);
        Assert.Equal(40, item.ProductId);
        Assert.Equal(CommercialGoalActionType.ProtectAvailability, item.ActionType);
        Assert.Equal(CommercialGoalActionOrigin.PurchaseGuidance, item.Source);
        Assert.False(item.HasPromotionSuggestion);
        Assert.False(item.HasComboSuggestion);
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, item.PurchaseGuidanceAction);
    }

    [Fact]
    public void Combo_aparece_como_complemento_nao_duplicata()
    {
        var plan = Compose(
            BelowPace(),
            Sources(
                Attention(Excess(50)),
                combos: Combos(SafeCombo(50, 501, 502))));

        var item = Assert.Single(plan.Items);
        Assert.Equal(50, item.ProductId);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, item.ActionType);
        Assert.True(item.HasComboSuggestion);
        Assert.Equal(2, item.ComboSuggestionCount);
        Assert.True(item.Sources.HasFlag(CommercialGoalActionSource.SmartCombo));
    }

    [Fact]
    public void Promocao_aparece_como_complemento()
    {
        var plan = Compose(
            BelowPace(),
            Sources(Attention(Excess(60)), Promotion(Suggested(60))));

        var item = Assert.Single(plan.Items);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, item.ActionType);
        Assert.True(item.HasPromotionSuggestion);
        Assert.True(item.Sources.HasFlag(CommercialGoalActionSource.PromotionSuggestion));
        Assert.True(item.Sources.HasFlag(CommercialGoalActionSource.InventoryAttention));
    }

    [Fact]
    public void Mesmo_ProductId_consolidado_em_uma_acao()
    {
        var plan = Compose(
            BelowPace(),
            Sources(
                Attention(Excess(70)),
                Promotion(Suggested(70)),
                Guidance(DoNotReplenish(70)),
                Combos(SafeCombo(70, 701, 702, 703))));

        var item = Assert.Single(plan.Items);
        Assert.Equal(70, item.ProductId);
        Assert.True(item.HasPromotionSuggestion);
        Assert.True(item.HasComboSuggestion);
        Assert.Equal(3, item.ComboSuggestionCount);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, item.PurchaseGuidanceAction);
    }

    [Fact]
    public void Maximo_cinco_acoes()
    {
        var attention = Enumerable.Range(1, 8).Select(id => Excess(id, excess: 10 + id)).ToArray();
        var plan = Compose(OnPace(), Sources(Attention(attention)));
        Assert.Equal(8, plan.CandidateCount);
        Assert.Equal(5, plan.Items.Count);
        Assert.Equal(new[] { 8, 7, 6, 5, 4 }, plan.Items.Select(i => i.ProductId).ToArray());
    }

    [Fact]
    public void Ordenacao_e_desempate_deterministicos()
    {
        var first = Compose(OnPace(), Sources(Attention(Idle(2), Idle(5), Excess(3))));
        var second = Compose(OnPace(), Sources(Attention(Excess(3), Idle(5), Idle(2))));
        Assert.Equal(
            first.Items.Select(i => i.ProductId).ToArray(),
            second.Items.Select(i => i.ProductId).ToArray());
        Assert.Equal(new[] { 3, 2, 5 }, first.Items.Select(i => i.ProductId).ToArray());
    }

    [Fact]
    public void Confidence_nao_e_elevada()
    {
        var plan = Compose(
            BelowPace(),
            Sources(
                Attention(Excess(80, confidence: InventoryAttentionConfidence.Limited)),
                Promotion(Suggested(80, InventoryAttentionConfidence.Reliable)),
                combos: Combos(SafeCombo(80, 801))));

        var item = Assert.Single(plan.Items);
        Assert.Equal(InventoryAttentionConfidence.Limited, item.Confidence);
        Assert.True(item.HasPromotionSuggestion);
        Assert.True(item.HasComboSuggestion);
    }

    [Fact]
    public void Limitation_e_propagada()
    {
        var review = Review(90);
        review = CloneAttention(
            review,
            InventoryAttentionReason.NegativeStock,
            [InventoryAttentionReason.NoPhysicalEvidence, InventoryAttentionReason.InsufficientHistory],
            InventoryAttentionConfidence.Unavailable);

        var plan = Compose(
            Estimated(),
            Sources(
                attention: Attention(review),
                guidance: Guidance(new InventoryPurchaseGuidanceResult
                {
                    ProductId = 90,
                    Action = InventoryPurchaseGuidanceAction.ReviewData,
                    Status = InventoryPurchaseGuidanceStatus.ReviewData,
                    PrimaryReason = InventoryPurchaseGuidanceReason.LocationLimitation,
                    SecondaryReasons = [InventoryPurchaseGuidanceReason.StructuralDataIssue],
                    Confidence = InventoryAttentionConfidence.Unavailable,
                })));

        var item = Assert.Single(plan.Items);
        Assert.True(item.Limitations.HasFlag(CommercialGoalActionLimitation.StructuralDataIssue));
        Assert.True(item.Limitations.HasFlag(CommercialGoalActionLimitation.NoPhysicalEvidence));
        Assert.True(item.Limitations.HasFlag(CommercialGoalActionLimitation.InsufficientHistory));
        Assert.True(item.Limitations.HasFlag(CommercialGoalActionLimitation.LocationLimitation));
        Assert.True(item.Limitations.HasFlag(CommercialGoalActionLimitation.LegacyCostEstimate));
        Assert.True(plan.HasLimitation(CommercialGoalActionLimitation.LegacyCostEstimate));
    }

    [Fact]
    public void Nenhuma_causalidade_financeira_no_modelo()
    {
        var src = ReadSource("src", "SGDB.App", "Models", "CommercialGoalActionPlan.cs")
            + ReadSource("src", "SGDB.App", "Services", "CommercialGoalActionPlanComposer.cs");
        Assert.DoesNotContain("IncrementalProfit", src, StringComparison.Ordinal);
        Assert.DoesNotContain("GoalContribution", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Uplift", src, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedDemand", src, StringComparison.Ordinal);
        Assert.DoesNotContain("atingir a meta", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lucro incremental", src, StringComparison.OrdinalIgnoreCase);
        var item = typeof(CommercialGoalActionItem);
        Assert.Null(item.GetProperty("IncrementalProfit"));
        Assert.Null(item.GetProperty("GoalContribution"));
        Assert.Null(item.GetProperty("ExpectedUplift"));
        Assert.Null(item.GetProperty("UnitsToGoal"));
    }

    [Fact]
    public void Nao_altera_snapshots_de_entrada()
    {
        var excess = Excess(100);
        var results = new List<InventoryAttentionResult> { excess };
        var byId = new Dictionary<int, InventoryAttentionResult> { [100] = excess };
        var attention = new InventoryAttentionSnapshot { Results = results, ByProductId = byId };
        var promoRows = new List<InventoryPromotionSuggestionRow> { Suggested(100) };
        var promotion = new InventoryPromotionSuggestionSnapshot
        {
            Rows = promoRows,
            ByProductId = new Dictionary<int, InventoryPromotionSuggestionRow>
            {
                [100] = promoRows[0],
            },
        };
        var sources = new CommercialGoalActionPlanSources
        {
            Attention = attention,
            Promotion = promotion,
        };

        Compose(BelowPace(), sources);

        Assert.Same(results, attention.Results);
        Assert.Same(byId, attention.ByProductId);
        Assert.Same(promoRows, promotion.Rows);
        Assert.Single(attention.Results);
        Assert.Equal(InventoryOperatorAction.EvaluateExcess, attention.Results[0].Action);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, promoRows[0].Suggestion.Status);
    }

    [Fact]
    public void Lista_vazia()
    {
        var plan = Compose(OnPace(), new CommercialGoalActionPlanSources());
        Assert.Empty(plan.Items);
        Assert.Equal(0, plan.CandidateCount);
        Assert.Equal(CommercialGoalActionPlanMode.Operational, plan.Mode);
        Assert.Equal(CommercialGoalStatus.OnPace, plan.GoalStatus);
    }

    [Fact]
    public void Composicao_com_multiplas_fontes()
    {
        var plan = Compose(
            BelowPace(),
            Sources(
                Intelligence(Row(1, "A1", "Alfa"), Row(2, "B2", "Beta")),
                Attention(Excess(1), Idle(2)),
                Promotion(Suggested(1)),
                Guidance(DoNotReplenish(1), Replenish(2)),
                Combos(SafeCombo(1, 11))));

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(CommercialGoalActionType.ReviewData, plan.Items[0].ActionType);
        Assert.Equal(2, plan.Items[0].ProductId);
        Assert.Equal("B2", plan.Items[0].ProductCode);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, plan.Items[1].ActionType);
        Assert.Equal("A1", plan.Items[1].ProductCode);
        Assert.Equal("Alfa", plan.Items[1].ProductName);
        Assert.True(plan.Items[1].HasPromotionSuggestion);
        Assert.True(plan.Items[1].HasComboSuggestion);
        Assert.Equal(10, plan.Items[1].CurrentStock);
    }

    [Fact]
    public void BelowPace_destaca_complemento_comercial_no_mesmo_grau()
    {
        var plan = Compose(
            BelowPace(),
            Sources(
                Attention(Excess(1, excess: 8), Excess(2, excess: 8)),
                Promotion(Suggested(2))));
        Assert.Equal(new[] { 2, 1 }, plan.Items.Select(i => i.ProductId).ToArray());
        Assert.True(plan.Items[0].HasPromotionSuggestion);
    }

    [Fact]
    public void OnPace_nao_reordena_por_promocao()
    {
        var plan = Compose(
            OnPace(),
            Sources(
                Attention(Excess(1, excess: 8), Excess(2, excess: 8)),
                Promotion(Suggested(2))));
        Assert.Equal(new[] { 1, 2 }, plan.Items.Select(i => i.ProductId).ToArray());
    }

    [Fact]
    public void AbovePace_nao_incentiva_desconto()
    {
        var plan = Compose(
            AbovePace(),
            Sources(
                Attention(Excess(1, excess: 8), Excess(2, excess: 8)),
                Promotion(Suggested(2))));
        Assert.Equal(new[] { 1, 2 }, plan.Items.Select(i => i.ProductId).ToArray());
        Assert.Equal(CommercialGoalActionPlanMode.Operational, plan.Mode);
        Assert.Equal(CommercialGoalStatus.AbovePace, plan.GoalStatus);
        Assert.DoesNotContain(plan.Items, i => i.ActionType == CommercialGoalActionType.ProtectAvailability
            && i.HasPromotionSuggestion);
    }

    [Fact]
    public void Achieved_prioriza_estoque_sem_giro_agressivo()
    {
        var plan = Compose(
            Achieved(),
            Sources(
                Attention(ExpiryRisk(3), Excess(4), Idle(5)),
                Promotion(Suggested(4), Suggested(5))));
        Assert.Equal(CommercialGoalStatus.Achieved, plan.GoalStatus);
        Assert.Equal(CommercialGoalActionType.PrioritizeExpiryRisk, plan.Items[0].ActionType);
        Assert.Equal(new[] { 3, 4, 5 }, plan.Items.Select(i => i.ProductId).ToArray());
        Assert.All(plan.Items, item =>
            Assert.NotEqual(CommercialGoalActionType.ProtectAvailability, item.ActionType));
    }

    [Fact]
    public void NoGoal_nao_bloqueia_inteligencia_de_estoque()
    {
        var plan = Compose(NoGoal(), Sources(Attention(Excess(8))));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, plan.Mode);
        Assert.Equal(CommercialGoalStatus.NoGoal, plan.GoalStatus);
        var item = Assert.Single(plan.Items);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, item.ActionType);
        Assert.False(plan.HasValidGoal);
    }

    [Fact]
    public void InvalidGoal_nao_usa_valor_invalido()
    {
        var plan = Compose(InvalidGoal(), Sources(Attention(Idle(9))));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, plan.Mode);
        Assert.True(plan.ProgressSkipReason.HasFlag(
            CommercialGoalProgressSkipReason.InvalidGoalConfiguration));
        Assert.Null(plan.GoalStatus);
        Assert.False(plan.HasValidGoal);
        Assert.Single(plan.Items);
    }

    [Fact]
    public void NotStarted_nao_gera_plano_operacional()
    {
        var plan = Compose(
            NotStarted(),
            Sources(Attention(Expired(1), Excess(2), Idle(3))));
        Assert.Equal(CommercialGoalActionPlanMode.FutureCompetence, plan.Mode);
        Assert.Equal(CommercialGoalStatus.NotStarted, plan.GoalStatus);
        Assert.Empty(plan.Items);
        Assert.Equal(0, plan.CandidateCount);
        Assert.True(CommercialGoalActionPlanComposer.ShouldSkipIntelligence(NotStarted()));
    }

    [Fact]
    public void Financial_unavailable_nao_inventa_realizado_zero()
    {
        var goal = Unavailable();
        Assert.False(goal.GrossProfitAvailable);
        Assert.Null(goal.GrossProfit);
        Assert.Null(goal.Status);
        var plan = Compose(goal, Sources(Attention(Excess(11))));
        Assert.True(plan.HasLimitation(CommercialGoalActionLimitation.FinancialUnavailable));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, plan.Mode);
        Assert.Single(plan.Items);
        Assert.DoesNotContain(plan.Items, i => i.ActionType.ToString().Contains("Zero", StringComparison.Ordinal));
    }

    [Fact]
    public void EstimatedLegacy_preserva_limitacao()
    {
        var plan = Compose(Estimated(), Sources(Attention(Idle(12))));
        Assert.True(plan.HasLimitation(CommercialGoalActionLimitation.LegacyCostEstimate));
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, plan.FinancialQuality);
        Assert.Single(plan.Items);
        Assert.True(plan.Items[0].Limitations.HasFlag(CommercialGoalActionLimitation.LegacyCostEstimate));
    }

    [Fact]
    public void Lucro_negativo_nao_inventa_recuperacao()
    {
        var plan = Compose(NegativeProfit(), Sources(Attention(Excess(13))));
        Assert.Equal(CommercialGoalStatus.BelowPace, plan.GoalStatus);
        Assert.True(plan.GoalStatus != CommercialGoalStatus.Achieved);
        var item = Assert.Single(plan.Items);
        Assert.Null(item.GetType().GetProperty("RecoverableAmount"));
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, item.ActionType);
    }

    [Fact]
    public void Excesso_mais_ConsiderReplenishment_vira_revisao()
    {
        var plan = Compose(
            OnPace(),
            Sources(attention: Attention(Excess(14)), guidance: Guidance(Replenish(14))));
        var item = Assert.Single(plan.Items);
        Assert.Equal(CommercialGoalActionType.ReviewData, item.ActionType);
        Assert.False(item.HasPromotionSuggestion);
    }

    [Fact]
    public void Composer_nao_tem_sql()
    {
        var src = ReadSource("src", "SGDB.App", "Services", "CommercialGoalActionPlanComposer.cs");
        Assert.DoesNotContain("SELECT", src, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenConnection", src, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService.Load", src, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryIntelligenceService", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", src, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceLoader_nao_tem_sql_proprio()
    {
        var src = ReadSource("src", "SGDB.App", "Services", "CommercialGoalActionPlanSourceLoader.cs");
        Assert.DoesNotContain("SELECT", src, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenConnection", src, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(src, "InventoryProjectionService.Load"));
        Assert.Equal(1, CountOccurrences(src, "InventoryAttentionComposer.Build"));
        Assert.Equal(1, CountOccurrences(src, "InventoryCommercialFactsService.Load"));
        Assert.Equal(1, CountOccurrences(src, "InventoryCommercialMarginSettingsService.Load"));
        Assert.Equal(1, CountOccurrences(src, "InventoryPurchaseGuidanceComposer.Compose"));
        Assert.Equal(1, CountOccurrences(src, "InventoryPromotionSuggestionComposer.Compose"));
        Assert.Equal(1, CountOccurrences(src, "InventoryComboIntelligenceComposer.Compose"));
        Assert.DoesNotContain("for (", src, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", src, StringComparison.Ordinal);
    }

    static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(value, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }

        return count;
    }

    static CommercialGoalActionPlanSnapshot Compose(
        CommercialGoalSnapshot goal,
        CommercialGoalActionPlanSources sources) =>
        CommercialGoalActionPlanComposer.Compose(goal, sources);

    static CommercialGoalActionPlanSources Sources(
        InventoryAttentionSnapshot? attention = null,
        InventoryPromotionSuggestionSnapshot? promotion = null,
        InventoryPurchaseGuidanceSnapshot? guidance = null,
        InventoryComboIntelligenceSnapshot? combos = null,
        InventoryIntelligenceSnapshot? intelligence = null) =>
        new()
        {
            Intelligence = intelligence,
            Attention = attention,
            Promotion = promotion,
            Guidance = guidance,
            Combos = combos,
        };

    static CommercialGoalActionPlanSources Sources(
        InventoryIntelligenceSnapshot intelligence,
        InventoryAttentionSnapshot attention,
        InventoryPromotionSuggestionSnapshot promotion,
        InventoryPurchaseGuidanceSnapshot guidance,
        InventoryComboIntelligenceSnapshot combos) =>
        new()
        {
            Intelligence = intelligence,
            Attention = attention,
            Promotion = promotion,
            Guidance = guidance,
            Combos = combos,
        };

    static CommercialGoalSnapshot BelowPace() =>
        Goal(ValidOverride(12_000m), Gross(5_000m), Sep15);

    static CommercialGoalSnapshot OnPace() =>
        Goal(ValidOverride(12_000m), Gross(6_000m), Sep15);

    static CommercialGoalSnapshot AbovePace() =>
        Goal(ValidOverride(12_000m), Gross(8_000m), Sep15);

    static CommercialGoalSnapshot Achieved() =>
        Goal(ValidOverride(12_000m), Gross(12_000m), Sep15);

    static CommercialGoalSnapshot NoGoal() =>
        Goal(None(), Exact(90m, 20m), Sep15);

    static CommercialGoalSnapshot InvalidGoal() =>
        Goal(InvalidDefault(), Exact(90m, 20m), Sep15);

    static CommercialGoalSnapshot NotStarted() =>
        Goal(ValidOverride(12_000m), Exact(0m, 0m), Aug1);

    static CommercialGoalSnapshot Unavailable() =>
        Goal(ValidOverride(12_000m), Financial(10m, 0m, null, CommercialGoalCostQuality.Unavailable, false), Sep15);

    static CommercialGoalSnapshot Estimated() =>
        Goal(
            ValidOverride(12_000m),
            Financial(16m, 18m, -2m, CommercialGoalCostQuality.EstimatedLegacy, true, estimated: true),
            Sep15);

    static CommercialGoalSnapshot NegativeProfit() =>
        Goal(ValidOverride(12_000m), Exact(10m, 40m), Sep15);

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
            HasValidGoal = false,
            QueryCount = 2,
        };

    static CommercialGoalFinancialSnapshot Exact(decimal revenue, decimal cogs) =>
        Financial(revenue, cogs, revenue - cogs, CommercialGoalCostQuality.Exact, true);

    static CommercialGoalFinancialSnapshot Gross(decimal gross) =>
        Exact(gross >= 0 ? gross + 40m : 10m, (gross >= 0 ? gross + 40m : 10m) - gross);

    static CommercialGoalFinancialSnapshot Financial(
        decimal revenue,
        decimal cogs,
        decimal? gross,
        CommercialGoalCostQuality quality,
        bool available,
        bool estimated = false) =>
        new()
        {
            Competence = Sep2026,
            NetCommercialRevenue = revenue,
            Cogs = cogs,
            GrossProfit = gross,
            CostQuality = quality,
            ProfitIsEstimated = estimated,
            GrossProfitAvailable = available,
        };

    static InventoryAttentionSnapshot Attention(params InventoryAttentionResult[] results)
    {
        var map = new Dictionary<int, InventoryAttentionResult>(results.Length);
        foreach (var result in results)
            map.TryAdd(result.ProductId, result);
        return new InventoryAttentionSnapshot
        {
            Results = results,
            ByProductId = map,
        };
    }

    static InventoryAttentionResult Review(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.ReviewData,
            PrimaryReason = InventoryAttentionReason.NegativeStock,
            Priority = InventoryAttentionPriority.Critical,
            Family = InventoryAttentionFamily.DataQuality,
            Confidence = InventoryAttentionConfidence.Unavailable,
        };

    static InventoryAttentionResult Expired(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.RemoveExpired,
            PrimaryReason = InventoryAttentionReason.Expired,
            Priority = InventoryAttentionPriority.Critical,
            Family = InventoryAttentionFamily.Expiry,
            Confidence = InventoryAttentionConfidence.Reliable,
        };

    static InventoryAttentionResult ExpiryRisk(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.PrioritizeSale,
            PrimaryReason = InventoryAttentionReason.SurplusAtExpiry,
            Priority = InventoryAttentionPriority.High,
            Family = InventoryAttentionFamily.Expiry,
            Confidence = InventoryAttentionConfidence.Reliable,
            ProjectedExpirySurplusQuantity = 4,
            NearestDatedDaysUntilExpiry = 3,
        };

    static InventoryAttentionResult Excess(
        int id,
        double excess = 12,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.EvaluateExcess,
            PrimaryReason = InventoryAttentionReason.ProjectedExcess30,
            Priority = InventoryAttentionPriority.Medium,
            Family = InventoryAttentionFamily.Excess,
            Confidence = confidence,
            ProjectedExcessQuantity = excess,
        };

    static InventoryAttentionResult Idle(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.Monitor,
            PrimaryReason = InventoryAttentionReason.Idle,
            Priority = InventoryAttentionPriority.Medium,
            Family = InventoryAttentionFamily.Turnover,
            Confidence = InventoryAttentionConfidence.Reliable,
        };

    static InventoryAttentionResult MonitorDated(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.Monitor,
            PrimaryReason = InventoryAttentionReason.DatedWithoutSurplusInWindow,
            Priority = InventoryAttentionPriority.Low,
            Family = InventoryAttentionFamily.Expiry,
            Confidence = InventoryAttentionConfidence.Reliable,
            NearestDatedDaysUntilExpiry = 20,
        };

    static InventoryAttentionResult Clear(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.None,
            PrimaryReason = InventoryAttentionReason.None,
            Priority = InventoryAttentionPriority.Normal,
            Family = InventoryAttentionFamily.Normal,
            Confidence = InventoryAttentionConfidence.Reliable,
        };

    static InventoryAttentionResult CloneAttention(
        InventoryAttentionResult source,
        InventoryAttentionReason primary,
        IReadOnlyList<InventoryAttentionReason> secondary,
        InventoryAttentionConfidence confidence) =>
        new()
        {
            ProductId = source.ProductId,
            Action = source.Action,
            PrimaryReason = primary,
            SecondaryReasons = secondary,
            Priority = source.Priority,
            Family = source.Family,
            Confidence = confidence,
            ProjectedExcessQuantity = source.ProjectedExcessQuantity,
            ProjectedExpirySurplusQuantity = source.ProjectedExpirySurplusQuantity,
            NearestDatedDaysUntilExpiry = source.NearestDatedDaysUntilExpiry,
        };

    static InventoryPromotionSuggestionSnapshot Promotion(
        params InventoryPromotionSuggestionRow[] rows)
    {
        var map = new Dictionary<int, InventoryPromotionSuggestionRow>(rows.Length);
        foreach (var row in rows)
            map.TryAdd(row.ProductId, row);
        return new InventoryPromotionSuggestionSnapshot { Rows = rows, ByProductId = map };
    }

    static InventoryPromotionSuggestionRow Suggested(
        int id,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable) =>
        new()
        {
            ProductId = id,
            Suggestion = new InventoryPromotionSuggestionResult
            {
                ProductId = id,
                Status = InventoryPromotionSuggestionStatus.Suggested,
                Action = InventoryPromotionSuggestionAction.ConsiderPromotion,
                Confidence = confidence,
                PrimaryReason = InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess,
            },
        };

    static InventoryPurchaseGuidanceSnapshot Guidance(
        params InventoryPurchaseGuidanceResult[] results)
    {
        var map = new Dictionary<int, InventoryPurchaseGuidanceResult>(results.Length);
        foreach (var result in results)
            map.TryAdd(result.ProductId, result);
        return new InventoryPurchaseGuidanceSnapshot { Results = results, ByProductId = map };
    }

    static InventoryPurchaseGuidanceResult Replenish(int id) =>
        new()
        {
            ProductId = id,
            Status = InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            Action = InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            PrimaryReason = InventoryPurchaseGuidanceReason.LowCoverage,
            Confidence = InventoryAttentionConfidence.Limited,
        };

    static InventoryPurchaseGuidanceResult DoNotReplenish(int id) =>
        new()
        {
            ProductId = id,
            Status = InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            Action = InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            PrimaryReason = InventoryPurchaseGuidanceReason.ProjectedExcess30,
            Confidence = InventoryAttentionConfidence.Reliable,
        };

    static InventoryComboIntelligenceSnapshot Combos(
        params InventoryComboTargetSuggestionGroup[] groups)
    {
        var map = new Dictionary<int, InventoryComboTargetSuggestionGroup>(groups.Length);
        foreach (var group in groups)
            map.TryAdd(group.ProductId, group);
        return new InventoryComboIntelligenceSnapshot { Targets = groups, ByProductId = map };
    }

    static InventoryComboTargetSuggestionGroup SafeCombo(int productId, params int[] anchors)
    {
        if (anchors.Length == 0)
            anchors = [productId + 100];
        var suggestions = new List<InventoryComboSuggestion>(anchors.Length);
        foreach (var anchor in anchors)
        {
            suggestions.Add(new InventoryComboSuggestion
            {
                TargetProductId = productId,
                AnchorProductId = anchor,
                TargetReason = ComboTargetEligibilityReason.ProjectedExcess,
                PairEvidence = InventoryComboPairEvidence.Observed,
                Confidence = InventoryAttentionConfidence.Reliable,
            });
        }

        return new InventoryComboTargetSuggestionGroup
        {
            ProductId = productId,
            Code = "C" + productId,
            Name = "Combo " + productId,
            Eligibility = new InventoryComboTargetEligibility
            {
                ProductId = productId,
                Status = ComboEligibilityStatus.Eligible,
                Reason = ComboTargetEligibilityReason.ProjectedExcess,
                Confidence = InventoryAttentionConfidence.Reliable,
            },
            Suggestions = suggestions,
        };
    }

    static InventoryIntelligenceSnapshot Intelligence(params ProductTurnoverRow[] rows) =>
        new() { Rows = rows };

    static ProductTurnoverRow Row(int id, string code, string name) =>
        new()
        {
            ProductId = id,
            Code = code,
            Name = name,
            TotalStock = 10,
            CoverageDays = 40,
            DaysWithoutSale = 5,
        };

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
