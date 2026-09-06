using System.Globalization;
using System.IO;
using System.Text;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71C-B4 — loader único. Orquestra autoridades existentes; não decide.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CentralDecisionLoaderTests
{
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly CommercialCompetence Oct2026 = CommercialCompetence.Create(2026, 10);
    static readonly DateOnly Sep10 = new(2026, 9, 10);
    static readonly DateTime Sep10Dt = new(2026, 9, 10);

    [Fact]
    public void QueryCount_proprio_e_teto()
    {
        Assert.Equal(0, CentralDecisionLoader.ExpectedQueryCount);
        Assert.Equal(15, CentralDecisionLoader.MaxInheritedQueryCount);
        Assert.Equal(2, CentralDecisionLoader.InheritedSettingsMaxQueryCount);
        Assert.Equal(2, CentralDecisionLoader.InheritedFinancialQueryCount);
        Assert.Equal(10, CentralDecisionLoader.InheritedIntelligenceQueryCount);
        Assert.Equal(1, CentralDecisionLoader.InheritedProductContributionQueryCount);
    }

    [Fact]
    public void Load_chama_cada_autoridade_uma_vez_sem_empilhar_loader_visual()
    {
        var src = ReadSource();
        Assert.Equal(1, CountToken(src, "CommercialGoalComposerService.Load("));
        Assert.Equal(1, CountToken(src, "CommercialGoalActionPlanSourceLoader.Load("));
        Assert.Equal(1, CountToken(src, "CommercialGoalProductContributionService.Load("));
        Assert.Equal(1, CountToken(src, "CommercialGoalActionPlanComposer.Compose("));
        Assert.Equal(1, CountToken(src, "CentralDecisionComposer.Compose("));
        Assert.Equal(1, CountToken(src, "CentralDecisionPresentation.Apply("));
        Assert.DoesNotContain("CommercialGoalLoader.Load", src, StringComparison.Ordinal);
        Assert.Equal(1, CountToken(src, "ShouldSkipIntelligence("));
    }

    [Fact]
    public void Sem_SQL_IO_WPF_N1_ranking_ou_clock_novo()
    {
        var src = ReadSource();
        Assert.DoesNotContain("SqliteCommand", src, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandText", src, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", src, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT", src, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsService", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", src, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", src, StringComparison.Ordinal);
        Assert.DoesNotContain("UserControl", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Parallel.", src, StringComparison.Ordinal);
        Assert.DoesNotContain("CoverageBand.Normal", src, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy", src, StringComparison.Ordinal);
        Assert.DoesNotContain("CompareItems", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Assemble_mesma_competencia_e_reference_date()
    {
        var goal = BelowPace();
        var result = CentralDecisionLoader.Assemble(
            goal,
            Sources(Intelligence(Turnover(1)), Attention(Monitor(1))),
            Contribution(Hist(1, 20m)));
        Assert.Equal(Sep2026, result.Decision.Competence);
        Assert.Equal(Sep10, result.Decision.ReferenceDate);
        Assert.Equal(Sep2026, result.Decision.Goal!.Competence);
        Assert.Equal(Sep10, result.Decision.Goal.ReferenceDate);
        Assert.Equal(Sep2026, result.Decision.ActionPlan!.Competence);
        Assert.Equal(Sep10, result.Decision.ActionPlan.ReferenceDate);
        Assert.Equal(Sep2026, result.Decision.Contribution!.Competence);
        Assert.Equal(Sep2026, result.Presentation.Competence);
        Assert.Equal(Sep10, result.Presentation.ReferenceDate);
        Assert.Equal(0, CentralDecisionLoader.ExpectedQueryCount);
        Assert.Equal(result.Decision.QueryCount, result.QueryCount);
        Assert.Equal(0, result.Presentation.QueryCount);
    }

    [Fact]
    public void Estados_Operational_Limited_Empty_Unavailable_Future()
    {
        var operational = CentralDecisionLoader.Assemble(
            BelowPace(),
            Sources(Intelligence(Turnover(1)), Attention(Monitor(1))),
            Contribution(Hist(1, 40m)));
        Assert.Equal(CentralDecisionState.Operational, operational.Decision.State);
        Assert.Equal(operational.Decision.State, operational.Presentation.State);

        var limited = CentralDecisionLoader.Assemble(
            BelowPace(),
            Sources(Intelligence(Turnover(2)), Attention(Monitor(2))),
            contribution: null);
        Assert.Equal(CentralDecisionState.Limited, limited.Decision.State);
        Assert.NotEmpty(limited.Decision.ActNow);

        var empty = CentralDecisionLoader.Assemble(
            Achieved(),
            Sources(Intelligence()),
            Contribution());
        Assert.Equal(CentralDecisionState.Empty, empty.Decision.State);
        Assert.Equal(CentralDecisionPresentation.HeadlineEmpty, empty.Presentation.EmptyText);

        var unavailable = CentralDecisionLoader.Assemble(
            BelowPace(),
            new CommercialGoalActionPlanSources { Intelligence = null },
            contribution: null);
        Assert.Equal(CentralDecisionState.Unavailable, unavailable.Decision.State);
        Assert.NotEqual(CentralDecisionState.Empty, unavailable.Decision.State);
        Assert.Equal(CentralDecisionPresentation.HeadlineUnavailable, unavailable.Presentation.EmptyText);

        var future = CentralDecisionLoader.Assemble(
            NotStarted(),
            Sources(Intelligence(Turnover(9)), Attention(Excess(9))),
            contribution: null);
        Assert.Equal(CentralDecisionState.Future, future.Decision.State);
        Assert.Empty(future.Decision.ActNow);
        Assert.Equal(CommercialGoalActionPlanMode.FutureCompetence, future.Decision.PlanMode);
    }

    [Fact]
    public void Meta_nao_muda_acao_e_InventoryOnly_preservado()
    {
        var review = Attention(Review(1));
        var promo = Promotion(Suggested(1));
        var noGoal = CentralDecisionLoader.Assemble(
            NoGoal(),
            Sources(Intelligence(Turnover(1)), review, promo),
            Contribution(Hist(1, 15m)));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, noGoal.Decision.PlanMode);
        Assert.Equal(CommercialGoalActionType.ReviewData, Assert.Single(noGoal.Decision.ActNow).ActionType);
        Assert.Contains(
            CentralDecisionPresentation.InventoryOnlyNote,
            noGoal.Presentation.GoalStrip.InventoryOnlyNote,
            StringComparison.Ordinal);

        var invalid = CentralDecisionLoader.Assemble(
            InvalidGoal(),
            Sources(Intelligence(Turnover(1)), review, promo),
            Contribution(Hist(1, 15m)));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, invalid.Decision.PlanMode);
        Assert.Equal(CommercialGoalActionType.ReviewData, Assert.Single(invalid.Decision.ActNow).ActionType);

        var unavailable = CentralDecisionLoader.Assemble(
            FinancialUnavailable(),
            Sources(Intelligence(Turnover(2)), Attention(Excess(2))),
            Contribution(unavailable: true));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, unavailable.Decision.PlanMode);
        Assert.Equal(CommercialGoalPresentation.EmDash, unavailable.Presentation.GoalStrip.Realized.ValueText);
        Assert.DoesNotContain("R$ 0,00", unavailable.Presentation.GoalStrip.Realized.ValueText, StringComparison.Ordinal);
        Assert.NotEqual(CommercialGoalPresentation.GoalNotConfigured, unavailable.Presentation.GoalStrip.Goal.ValueText);
    }

    [Fact]
    public void Seguranca_comercial_do_pipeline()
    {
        var achieved = CentralDecisionLoader.Assemble(
            Achieved(),
            Sources(Intelligence(Turnover(9))),
            Contribution(Hist(9, 500m)));
        Assert.Empty(achieved.Decision.ActNow);
        Assert.Equal(9, Assert.Single(achieved.Decision.Preserve).ProductId);
        var achievedText = Visible(achieved.Presentation);
        Assert.DoesNotContain(CentralDecisionPresentation.PromotionText, achievedText, StringComparison.Ordinal);
        Assert.DoesNotContain(CentralDecisionPresentation.ComboText, achievedText, StringComparison.Ordinal);

        var protect = CentralDecisionLoader.Assemble(
            BelowPace(),
            Sources(
                Intelligence(Turnover(4)),
                Attention(Clear(4)),
                Promotion(Suggested(4)),
                Guidance(Replenish(4)),
                Combos(SafeCombo(4))),
            Contribution(Hist(4, 80m)));
        var protectItem = Assert.Single(protect.Presentation.ActNow);
        Assert.Equal(CentralDecisionPresentation.WhatProtect, protectItem.WhatText);
        Assert.Equal("", protectItem.PromotionText);
        Assert.Equal("", protectItem.ComboText);

        var review = CentralDecisionLoader.Assemble(
            BelowPace(),
            Sources(
                Intelligence(Turnover(1)),
                Attention(Review(1)),
                Promotion(Suggested(1)),
                combos: Combos(SafeCombo(1))),
            Contribution(Hist(1, 90m)));
        var reviewItem = Assert.Single(review.Presentation.ActNow);
        Assert.Equal(CentralDecisionPresentation.WhatReviewData, reviewItem.WhatText);
        Assert.Equal("", reviewItem.PromotionText);
        Assert.Equal("", reviewItem.ComboText);
        Assert.Equal(CommercialGoalActionType.ReviewData, review.Decision.ActNow[0].ActionType);
    }

    [Fact]
    public void B8_nao_muda_acao_e_preserva_qualidade()
    {
        var sources = Sources(Intelligence(Turnover(3)), Attention(Excess(3)));
        var exact = CentralDecisionLoader.Assemble(BelowPace(), sources, Contribution(Hist(3, 40m)));
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, exact.Decision.ActNow[0].ActionType);
        Assert.Equal(CommercialGoalPresentation.FormatMoney(40m), exact.Presentation.ActNow[0].GrossProfitText);

        var missing = CentralDecisionLoader.Assemble(BelowPace(), sources, Contribution());
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, missing.Decision.ActNow[0].ActionType);
        Assert.Equal(CommercialGoalPresentation.EmDash, missing.Presentation.ActNow[0].GrossProfitText);

        var estimated = CentralDecisionLoader.Assemble(
            BelowPace(),
            sources,
            Contribution(Hist(3, 12m, CommercialGoalCostQuality.EstimatedLegacy)));
        Assert.Equal(
            CommercialGoalProductContributionPresentation.QualityEstimated,
            estimated.Presentation.ActNow[0].CostQualityText);

        var unavailable = CentralDecisionLoader.Assemble(
            BelowPace(),
            sources,
            Contribution(Hist(3, gp: null, CommercialGoalCostQuality.Unavailable)));
        Assert.Equal(CommercialGoalPresentation.EmDash, unavailable.Presentation.ActNow[0].GrossProfitText);

        var negative = CentralDecisionLoader.Assemble(
            BelowPace(),
            sources,
            Contribution(Hist(3, -25m)));
        Assert.Equal(CommercialGoalPresentation.FormatMoney(-25m), negative.Presentation.ActNow[0].GrossProfitText);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, negative.Decision.ActNow[0].ActionType);
    }

    [Fact]
    public void Assemble_nao_reordena_ActNow_nem_seleciona_Preserve()
    {
        var first = CentralDecisionLoader.Assemble(
            BelowPace(),
            Sources(Intelligence(Turnover(1), Turnover(2)), Attention(Review(1), Excess(2))),
            Contribution(Hist(1, 10m), Hist(2, 20m)));
        var second = CentralDecisionLoader.Assemble(
            BelowPace(),
            Sources(Intelligence(Turnover(2), Turnover(1)), Attention(Excess(2), Review(1))),
            Contribution(Hist(2, 20m), Hist(1, 10m)));
        Assert.Equal(
            first.Decision.ActNow.Select(x => x.ProductId).ToArray(),
            second.Decision.ActNow.Select(x => x.ProductId).ToArray());
        Assert.Equal(CommercialGoalActionType.ReviewData, first.Decision.ActNow[0].ActionType);
    }

    [Fact]
    public void Query_budget_operacional_sem_coocorrencia_e_futuro()
    {
        using var db = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 12_000m);
        TestDataHelper.SeedSimpleProduct(20, 10, 6, "NT1", "Sem tese");

        var operational = CentralDecisionLoader.Load(Sep2026, Sep10);
        Assert.True(operational.QueryCount <= 14, $"sem coocorrência: {operational.QueryCount}");
        Assert.True(operational.QueryCount <= CentralDecisionLoader.MaxInheritedQueryCount);
        Assert.Equal(Sep2026, operational.Decision.Competence);
        Assert.Equal(Sep10, operational.Decision.ReferenceDate);
        Assert.Equal(Sep2026, operational.Decision.Contribution!.Competence);
        Assert.NotEqual(CentralDecisionState.Future, operational.Decision.State);
        Assert.True(
            operational.Decision.ActionPlan!.QueryCount is 9 or 10,
            $"fontes: {operational.Decision.ActionPlan.QueryCount}");

        var future = CentralDecisionLoader.Load(Oct2026, Sep10);
        Assert.Equal(CentralDecisionState.Future, future.Decision.State);
        Assert.Empty(future.Decision.ActNow);
        Assert.Equal(CommercialGoalActionPlanMode.FutureCompetence, future.Decision.PlanMode);
        Assert.Equal(0, future.Decision.ActionPlan!.QueryCount);
        Assert.Equal(
            future.Decision.Goal!.QueryCount + future.Decision.Contribution!.QueryCount,
            future.QueryCount);
        Assert.True(future.QueryCount <= 5, $"futuro: {future.QueryCount}");
        Assert.True(future.QueryCount < operational.QueryCount);
    }

    [Fact]
    public void Query_budget_com_coocorrencia_no_teto()
    {
        using var db = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 12_000m);
        SeedIdle("TIDLE", "Alvo parado");
        SeedHealthy("AHIT", "Ancora saudavel");

        var result = CentralDecisionLoader.Load(Sep2026, Sep10);
        Assert.True(result.QueryCount <= 15, $"operacional: {result.QueryCount}");
        Assert.Equal(Sep2026, result.Decision.Competence);
        Assert.Equal(Sep10, result.Decision.ReferenceDate);
        var sourcesQuery = result.Decision.ActionPlan!.QueryCount;
        Assert.True(sourcesQuery is 9 or 10, $"fontes: {sourcesQuery}");
        Assert.Equal(
            result.Decision.Goal!.QueryCount + sourcesQuery + result.Decision.Contribution!.QueryCount,
            result.QueryCount);
        Assert.Equal(1, result.Decision.Contribution.QueryCount);
        Assert.True(result.Decision.Goal.QueryCount <= 4);
        if (sourcesQuery == 10)
            Assert.True(result.QueryCount is 14 or 15, $"com coocorrência: {result.QueryCount}");
    }

    [Fact]
    public void Load_NoGoal_Invalid_Unavailable_nao_inventam_zero()
    {
        using var db = Begin();
        var noGoal = CentralDecisionLoader.Load(Sep2026, Sep10);
        Assert.Equal(CommercialGoalStatus.NoGoal, noGoal.Decision.GoalStatus);
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, noGoal.Decision.PlanMode);
        Assert.NotEqual(CentralDecisionState.Unavailable, noGoal.Decision.State);
        Assert.Equal(CommercialGoalPresentation.GoalNotConfigured, noGoal.Presentation.GoalStrip.Goal.ValueText);

        AppSettingsService.SetSetting(CommercialGoalSettingKeys.Monthly(Sep2026), "abc");
        var invalid = CentralDecisionLoader.Load(Sep2026, Sep10);
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, invalid.Decision.PlanMode);
        Assert.Equal(CommercialGoalPresentation.GoalInvalid, invalid.Presentation.GoalStrip.Goal.ValueText);
        Assert.NotEqual(CentralDecisionState.Unavailable, invalid.Decision.State);
    }

    static TempDatabase Begin()
    {
        PdvService.TestBeforeInsertSaleItems = null;
        PdvService.TestAfterInsertSaleItems = null;
        PdvService.TestAfterSwapItemUpdate = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        InventoryCommercialMarginSettingsService.Save(20m);
        return db;
    }

    static int SeedIdle(string code, string name)
    {
        var id = TestDataHelper.SeedSimpleProduct(80, 10, 6, code, name);
        StampInbound(id, Sep10Dt.AddDays(-100));
        return id;
    }

    static int SeedHealthy(string code, string name)
    {
        var id = TestDataHelper.SeedSimpleProduct(40, 10, 6, code, name);
        StampInbound(id, Sep10Dt.AddDays(-90));
        InsertLot(id, 40, Sep10Dt.AddDays(180));
        for (var i = 0; i < 30; i++)
            InsertSale(Sep10Dt.AddDays(-i), (id, 2));
        return id;
    }

    static void StampInbound(int productId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at, operation
            ) VALUES (
              $pid, 'entrada', 1, 0, '71c inbound', $at, 'entrada_compra'
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$at", date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    static void InsertLot(int productId, double quantity, DateTime expiry)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO product_lots (product_id, lot_number, expiry_date, quantity, unit_cost)
            VALUES ($p, $l, $e, $q, $c);
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.Parameters.AddWithValue("$l", "L" + productId);
        cmd.Parameters.AddWithValue("$e", expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$q", quantity);
        cmd.Parameters.AddWithValue("$c", 6);
        cmd.ExecuteNonQuery();
    }

    static void InsertSale(DateTime sessionDate, params (int ProductId, double Qty)[] items)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        int saleId;
        using (var sale = conn.CreateCommand())
        {
            sale.Transaction = tx;
            sale.CommandText = """
                INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
                VALUES ($d, $total, 'Dinheiro', 0, $created);
                SELECT last_insert_rowid();
                """;
            sale.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sale.Parameters.AddWithValue("$total", items.Sum(i => i.Qty * 10));
            sale.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
            saleId = Convert.ToInt32(sale.ExecuteScalar());
        }

        foreach (var item in items)
        {
            using var line = conn.CreateCommand();
            line.Transaction = tx;
            line.CommandText = """
                INSERT INTO sale_items (
                  sale_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, stock_qty
                ) VALUES ($sale, $pid, 'SKU', 'Item', 'UN', $qty, 10, $sub, 0);
                """;
            line.Parameters.AddWithValue("$sale", saleId);
            line.Parameters.AddWithValue("$pid", item.ProductId);
            line.Parameters.AddWithValue("$qty", item.Qty);
            line.Parameters.AddWithValue("$sub", item.Qty * 10);
            line.ExecuteNonQuery();
        }

        tx.Commit();
    }

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

    static CommercialGoalSnapshot NotStarted() =>
        CommercialGoalComposer.Compose(
            new CommercialGoalSettingResolution
            {
                Competence = Oct2026,
                Source = CommercialGoalSettingSource.MonthlyOverride,
                GoalAmount = 12_000m,
                HasValidGoal = true,
                QueryCount = 1,
            },
            new CommercialGoalFinancialSnapshot
            {
                Competence = Oct2026,
                NetCommercialRevenue = 0m,
                Cogs = 0m,
                GrossProfit = 0m,
                CostQuality = CommercialGoalCostQuality.Exact,
                GrossProfitAvailable = true,
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

    static CommercialGoalActionPlanSources Sources(
        InventoryIntelligenceSnapshot? intelligence = null,
        InventoryAttentionSnapshot? attention = null,
        InventoryPromotionSuggestionSnapshot? promotion = null,
        InventoryPurchaseGuidanceSnapshot? guidance = null,
        InventoryComboIntelligenceSnapshot? combos = null) =>
        new()
        {
            Intelligence = intelligence,
            Attention = attention,
            Promotion = promotion,
            Guidance = guidance,
            Combos = combos,
        };

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

    static InventoryAttentionSnapshot Attention(params InventoryAttentionResult[] results)
    {
        var map = new Dictionary<int, InventoryAttentionResult>(results.Length);
        foreach (var result in results)
            map.TryAdd(result.ProductId, result);
        return new InventoryAttentionSnapshot { Results = results, ByProductId = map };
    }

    static InventoryAttentionResult Monitor(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.Monitor,
            PrimaryReason = InventoryAttentionReason.DatedWithoutSurplusInWindow,
            Priority = InventoryAttentionPriority.Low,
            Confidence = InventoryAttentionConfidence.Reliable,
        };

    static InventoryAttentionResult Review(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.ReviewData,
            PrimaryReason = InventoryAttentionReason.NegativeStock,
            Priority = InventoryAttentionPriority.Critical,
            Confidence = InventoryAttentionConfidence.Unavailable,
        };

    static InventoryAttentionResult Excess(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.EvaluateExcess,
            PrimaryReason = InventoryAttentionReason.ProjectedExcess30,
            Priority = InventoryAttentionPriority.Medium,
            Confidence = InventoryAttentionConfidence.Reliable,
            ProjectedExcessQuantity = 12,
        };

    static InventoryAttentionResult Clear(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.None,
            PrimaryReason = InventoryAttentionReason.None,
            Confidence = InventoryAttentionConfidence.Reliable,
        };

    static InventoryPromotionSuggestionSnapshot Promotion(
        params InventoryPromotionSuggestionRow[] rows)
    {
        var map = new Dictionary<int, InventoryPromotionSuggestionRow>(rows.Length);
        foreach (var row in rows)
            map.TryAdd(row.ProductId, row);
        return new InventoryPromotionSuggestionSnapshot { Rows = rows, ByProductId = map };
    }

    static InventoryPromotionSuggestionRow Suggested(int id) =>
        new()
        {
            ProductId = id,
            Suggestion = new InventoryPromotionSuggestionResult
            {
                ProductId = id,
                Status = InventoryPromotionSuggestionStatus.Suggested,
                Action = InventoryPromotionSuggestionAction.ConsiderPromotion,
                Confidence = InventoryAttentionConfidence.Reliable,
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

    static InventoryComboIntelligenceSnapshot Combos(
        params InventoryComboTargetSuggestionGroup[] groups)
    {
        var map = new Dictionary<int, InventoryComboTargetSuggestionGroup>(groups.Length);
        foreach (var group in groups)
            map.TryAdd(group.ProductId, group);
        return new InventoryComboIntelligenceSnapshot { Targets = groups, ByProductId = map };
    }

    static InventoryComboTargetSuggestionGroup SafeCombo(int productId) =>
        new()
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
            Suggestions =
            [
                new InventoryComboSuggestion
                {
                    TargetProductId = productId,
                    AnchorProductId = productId + 100,
                    TargetReason = ComboTargetEligibilityReason.ProjectedExcess,
                    PairEvidence = InventoryComboPairEvidence.Observed,
                    Confidence = InventoryAttentionConfidence.Reliable,
                },
            ],
        };

    static CommercialGoalProductContributionSnapshot Contribution(
        params CommercialGoalProductContributionRow[] rows) =>
        Contribution(false, rows);

    static CommercialGoalProductContributionSnapshot Contribution(
        bool unavailable,
        params CommercialGoalProductContributionRow[] rows) =>
        new()
        {
            Competence = Sep2026,
            GrossProfitAvailable = !unavailable,
            CostQuality = unavailable
                ? CommercialGoalCostQuality.Unavailable
                : CommercialGoalCostQuality.Exact,
            GrossProfit = unavailable ? null : rows.Sum(r => r.GrossProfit ?? 0m),
            Rows = rows,
            QueryCount = 1,
        };

    static CommercialGoalProductContributionRow Hist(
        int id,
        decimal? gp,
        CommercialGoalCostQuality quality = CommercialGoalCostQuality.Exact) =>
        new()
        {
            ProductId = id,
            ProductCode = "C" + id,
            ProductName = "Hist " + id,
            Revenue = 100m,
            Cogs = 100m - (gp ?? 0m),
            GrossProfit = gp,
            GrossMarginPercent = 10m,
            CostQuality = quality,
        };

    static string Visible(CentralDecisionPresentationSnapshot presented)
    {
        var sb = new StringBuilder();
        sb.Append(presented.Headline).Append(' ').Append(presented.EmptyText);
        foreach (var item in presented.ActNow)
        {
            sb.Append(' ').Append(item.WhatText)
                .Append(' ').Append(item.PromotionText)
                .Append(' ').Append(item.ComboText);
        }

        foreach (var item in presented.Preserve)
            sb.Append(' ').Append(item.ProductTitle);
        return sb.ToString();
    }

    static int CountToken(string src, string token)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var i = src.IndexOf(token, start, StringComparison.Ordinal);
            if (i < 0)
                return count;
            count++;
            start = i + token.Length;
        }
    }

    static string ReadSource()
    {
        var relative = new[] { "src", "SGDB.App", "Services", "CentralDecisionLoader.cs" };
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
