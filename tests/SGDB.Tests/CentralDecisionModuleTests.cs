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
/// 71C-B5/B6 — módulo visual + integração menu/toolbar.
/// Sem instanciar UserControl WPF; sem banco da loja nos testes estruturais.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CentralDecisionModuleTests
{
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly CommercialCompetence Oct2026 = CommercialCompetence.Create(2026, 10);
    static readonly DateOnly Sep10 = new(2026, 9, 10);
    static readonly DateTime Sep10Dt = new(2026, 9, 10);

    [Fact]
    public void QueryCount_ui_e_identidade()
    {
        Assert.Equal(0, CentralDecisionUi.ExpectedQueryCount);
        Assert.Equal(0, CentralDecisionLoader.ExpectedQueryCount);
        Assert.Equal(0, CentralDecisionPresentation.ExpectedQueryCount);
        Assert.Equal("central_decisao", CentralDecisionUi.ModuleId);
        Assert.Equal(CentralDecisionPresentation.Title, CentralDecisionUi.ModuleTitle);
        Assert.Equal("Central", CentralDecisionUi.ToolbarTitle);
    }

    [Fact]
    public void View_carrega_CentralDecisionLoader_uma_vez()
    {
        var cs = ReadViewCs();
        Assert.Equal(1, CountToken(cs, "CentralDecisionLoader.Load("));
        Assert.DoesNotContain("CommercialGoalLoader", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialGoalActionPlanSourceLoader", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialGoalProductContributionService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialGoalComposerService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryProjectionService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("SqliteCommand", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandText", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy", cs, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxActNow", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxPreserve", cs, StringComparison.Ordinal);
        Assert.Contains("if (_loading)", MethodBody(cs, "private void Load()"), StringComparison.Ordinal);
        var ctor = MethodBody(cs, "public CentralDecisionModuleView()");
        Assert.DoesNotContain("CentralDecisionLoader", ctor, StringComparison.Ordinal);
        Assert.Contains("Loaded +=", ctor, StringComparison.Ordinal);
        Assert.Contains("Loaded -=", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void View_sem_promocao_sem_segundo_loader()
    {
        var cs = ReadViewCs();
        var xaml = ReadViewXaml();
        Assert.DoesNotContain("AccessControl", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("RelatoriosAcesso", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowModule", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("new MainWindow", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(MainWindow)", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("extra_json", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ValiditySuggestedAction", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ConsiderPromotion", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialGoalLoader", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button", Section(xaml, "ActNowSection", "PreserveSection"), StringComparison.Ordinal);
        Assert.DoesNotContain("<Button", Section(xaml, "PreserveSection", "LimitationsSection"), StringComparison.Ordinal);
    }

    [Fact]
    public void View_bloqueia_cliente_antes_do_load()
    {
        var cs = ReadViewCs();
        var xaml = ReadViewXaml();
        var clientIdx = cs.IndexOf("StoreNetworkMode.IsClient", StringComparison.Ordinal);
        var loadIdx = cs.IndexOf("CentralDecisionLoader.Load(", StringComparison.Ordinal);
        Assert.InRange(clientIdx, 0, loadIdx - 1);
        Assert.Contains("ShowClientBlocked();", cs, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClientBlockOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClientBlockText\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_e_toolbar_chamam_mesmo_modulo()
    {
        var xaml = ReadSource("src", "SGDB.App", "MainWindow.xaml");
        Assert.Contains("Tag=\"central_decisao\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Central de Decisão\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Label=\"Central\"", xaml, StringComparison.Ordinal);
        var metaMenu = xaml.IndexOf("Header=\"Meta Comercial\"", StringComparison.Ordinal);
        var centralMenu = xaml.IndexOf("Header=\"Central de Decisão\"", StringComparison.Ordinal);
        Assert.True(metaMenu >= 0 && centralMenu > metaMenu);
        var toolbarMeta = xaml.IndexOf("x:Name=\"BtnMeta\"", StringComparison.Ordinal);
        var toolbarCentral = xaml.IndexOf("x:Name=\"BtnCentral\"", StringComparison.Ordinal);
        var toolbarCompras = xaml.IndexOf("x:Name=\"BtnCompras\"", StringComparison.Ordinal);
        Assert.True(toolbarMeta >= 0 && toolbarCentral > toolbarMeta && toolbarCompras > toolbarCentral);
        Assert.Contains("Tag=\"inicio\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BtnMeuNegocio\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"home\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Cliente_RedeLoja_bloqueado_antes_da_view()
    {
        var mode = ReadSource("src", "SGDB.App", "Services", "StoreNetworkMode.cs");
        var main = ReadSource("src", "SGDB.App", "MainWindow.xaml.cs");
        Assert.Contains("or \"central_decisao\"", mode, StringComparison.Ordinal);
        var blockIdx = main.IndexOf("StoreNetworkMode.IsModuleBlockedOnClient(moduleId)", StringComparison.Ordinal);
        var viewIdx = main.IndexOf("new CentralDecisionModuleView()", StringComparison.Ordinal);
        Assert.InRange(blockIdx, 0, viewIdx - 1);
        Assert.Contains("CentralDecisionUi.ModuleId", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Highlight_Central_e_BtnMeta_corrigidos()
    {
        var main = ReadSource("src", "SGDB.App", "MainWindow.xaml.cs");
        Assert.Contains("SetToolbarActive(BtnMeta, CommercialGoalUi.ModuleId)", main, StringComparison.Ordinal);
        Assert.Contains("SetToolbarActive(BtnCentral, CentralDecisionUi.ModuleId)", main, StringComparison.Ordinal);
        Assert.Contains("SetToolbarPermission(BtnMeta, CommercialGoalUi.ModuleId)", main, StringComparison.Ordinal);
        Assert.Contains("SetToolbarPermission(BtnCentral, CentralDecisionUi.ModuleId)", main, StringComparison.Ordinal);
        Assert.Contains("if (btn == BtnMeta) return CommercialGoalUi.ModuleId;", main, StringComparison.Ordinal);
        Assert.Contains("if (btn == BtnCentral) return CentralDecisionUi.ModuleId;", main, StringComparison.Ordinal);
        var highlight = MethodBody(main, "private void UpdateToolbarHighlight()");
        Assert.Contains("BtnMeta", highlight, StringComparison.Ordinal);
        Assert.Contains("BtnCentral", highlight, StringComparison.Ordinal);
        Assert.Contains("BtnMeuNegocio", highlight, StringComparison.Ordinal);
    }

    [Fact]
    public void RelatoriosAcesso_e_perfis()
    {
        var access = ReadSource("src", "SGDB.App", "Services", "AccessControl.cs");
        Assert.Contains("or \"central_decisao\"", access, StringComparison.Ordinal);
        var centralIdx = access.IndexOf("or \"central_decisao\"", StringComparison.Ordinal);
        var relIdx = access.IndexOf("=> p.RelatoriosAcesso", centralIdx, StringComparison.Ordinal);
        Assert.True(relIdx > centralIdx);

        TestDataHelper.SetSessionRole("gestor");
        Assert.True(AccessControl.CanAccessModule(CentralDecisionUi.ModuleId));
        Assert.True(AccessControl.Can("RelatoriosAcesso"));

        TestDataHelper.SetSessionRole("admin");
        Assert.True(AccessControl.CanAccessModule(CentralDecisionUi.ModuleId));

        TestDataHelper.SetSessionRole("vendedor");
        Assert.False(AccessControl.CanAccessModule(CentralDecisionUi.ModuleId));

        TestDataHelper.SetSessionCustomPermissions("vendedor", p => p.RelatoriosAcesso = true);
        Assert.True(AccessControl.CanAccessModule(CentralDecisionUi.ModuleId));
    }

    [Fact]
    public void MainWindow_registra_Central_sem_substituir_Home()
    {
        var mainCs = ReadSource("src", "SGDB.App", "MainWindow.xaml.cs");
        var mainXaml = ReadSource("src", "SGDB.App", "MainWindow.xaml");
        Assert.Contains("CentralDecisionModuleView", mainCs, StringComparison.Ordinal);
        Assert.Contains("CentralDecisionUi.ModuleId", mainCs, StringComparison.Ordinal);
        Assert.Contains("central_decisao", mainXaml, StringComparison.Ordinal);
        Assert.Contains("ShowHome()", mainCs, StringComparison.Ordinal);
        Assert.Contains("Tag=\"inicio\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("Label=\"Meu Negócio\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CentralDecisionLoader.Load", mainCs, StringComparison.Ordinal);
    }

    [Fact]
    public void View_layout_5_mais_3_ItemsControl()
    {
        var xaml = ReadViewXaml();
        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActNowItems\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreserveItems\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WhatText", xaml, StringComparison.Ordinal);
        Assert.Contains("WhyText", xaml, StringComparison.Ordinal);
        Assert.Contains("CareText", xaml, StringComparison.Ordinal);
        Assert.Contains("PromotionText", xaml, StringComparison.Ordinal);
        Assert.Contains("ComboText", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"640\"", xaml, StringComparison.Ordinal);
        var actIdx = xaml.IndexOf("x:Name=\"ActNowSection\"", StringComparison.Ordinal);
        var preserveIdx = xaml.IndexOf("x:Name=\"PreserveSection\"", StringComparison.Ordinal);
        Assert.True(actIdx >= 0 && preserveIdx > actIdx);
    }

    [Fact]
    public void ApplyView_apenas_bind_sem_loader()
    {
        var cs = ReadViewCs();
        var apply = MethodBody(cs, "private void ApplyView()");
        Assert.DoesNotContain("CentralDecisionLoader", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Load(", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Compose(", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseService", apply, StringComparison.Ordinal);
        Assert.Contains("ApplyActNow()", apply, StringComparison.Ordinal);
        Assert.Contains("ApplyPreserve()", apply, StringComparison.Ordinal);

        var actNow = MethodBody(cs, "void ApplyActNow()");
        Assert.Contains("ActNowItems.ItemsSource = _presented.ActNow", actNow, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy", actNow, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(", actNow, StringComparison.Ordinal);

        var preserve = MethodBody(cs, "void ApplyPreserve()");
        Assert.Contains("PreserveItems.ItemsSource = _presented.Preserve", preserve, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy", preserve, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(", preserve, StringComparison.Ordinal);

        var load = MethodBody(cs, "private void Load()");
        Assert.Contains("CentralDecisionLoader.Load(", load, StringComparison.Ordinal);
        Assert.Equal(1, CountToken(load, "CentralDecisionLoader.Load("));
        Assert.Contains("ApplyView()", load, StringComparison.Ordinal);
        Assert.Contains("var referenceDate = Today();", load, StringComparison.Ordinal);
    }

    [Fact]
    public void Falha_fisica_nao_vira_Empty()
    {
        var keep = CentralDecisionUi.ResolveLoadFailure(hasValidSnapshot: true);
        Assert.True(keep.KeepPreviousSnapshot);
        Assert.Equal(CentralDecisionUi.RefreshKeepDataMessage, keep.OperatorMessage);

        var cold = CentralDecisionUi.ResolveLoadFailure(hasValidSnapshot: false);
        Assert.False(cold.KeepPreviousSnapshot);
        var unavailable = CentralDecisionUi.UnavailablePresentation(
            Sep2026, Sep10, cold.OperatorMessage);
        Assert.Equal(CentralDecisionState.Unavailable, unavailable.State);
        Assert.NotEqual(CentralDecisionState.Empty, unavailable.State);
        Assert.Equal(CentralDecisionPresentation.HeadlineUnavailable, unavailable.Headline);
        Assert.Equal(CentralDecisionPresentation.HeadlineUnavailable, unavailable.EmptyText);
        Assert.NotEqual(CentralDecisionPresentation.HeadlineEmpty, unavailable.Headline);
        Assert.Empty(unavailable.ActNow);
        Assert.Contains(unavailable.Limitations, l => l.IsProminent);

        var cs = ReadViewCs();
        Assert.Contains("CentralDecisionUi.ResolveLoadFailure", cs, StringComparison.Ordinal);
        Assert.Contains("UnavailablePresentation", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("HeadlineEmpty", MethodBody(cs, "private void Load()"), StringComparison.Ordinal);
    }

    [Fact]
    public void Presentation_caps_e_ordem_preservados_pelo_pipeline()
    {
        var result = CentralDecisionLoader.Assemble(
            BelowPace(),
            Sources(
                Intelligence(Turnover(1), Turnover(2), Turnover(3)),
                Attention(Review(1), Excess(2), Idle(3))),
            Contribution(Hist(1, 10m), Hist(2, 20m), Hist(3, 30m)));
        Assert.True(result.Presentation.ActNow.Count <= 5);
        Assert.Equal(
            result.Decision.ActNow.Select(x => x.ProductId).ToArray(),
            result.Presentation.ActNow.Select(x => x.ProductId).ToArray());
        Assert.Equal(CommercialGoalActionType.ReviewData, result.Presentation.ActNow[0].ActionType);
        Assert.Equal(CentralDecisionPresentation.WhatReviewData, result.Presentation.ActNow[0].WhatText);

        var preserve = CentralDecisionLoader.Assemble(
            Achieved(),
            Sources(Intelligence(Turnover(11), Turnover(13), Turnover(10))),
            Contribution(Hist(11, 90m), Hist(13, 80m), Hist(10, 30m)));
        Assert.True(preserve.Presentation.Preserve.Count <= 3);
        Assert.Equal(
            preserve.Decision.Preserve.Select(x => x.ProductId).ToArray(),
            preserve.Presentation.Preserve.Select(x => x.ProductId).ToArray());
        Assert.All(preserve.Presentation.Preserve, p => Assert.Equal("Cobertura normal", p.CoverageText));
    }

    [Fact]
    public void Seguranca_comercial_na_presentation_bindavel()
    {
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
        Assert.False(protectItem.HasPromotionSuggestion);

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

        var b8Missing = CentralDecisionLoader.Assemble(
            BelowPace(),
            Sources(Intelligence(Turnover(2)), Attention(Excess(2))),
            contribution: null);
        Assert.Equal(CommercialGoalActionType.PrioritizeExcess, b8Missing.Decision.ActNow[0].ActionType);
        Assert.NotEmpty(b8Missing.Presentation.ActNow);
        Assert.Equal(CommercialGoalPresentation.EmDash, b8Missing.Presentation.ActNow[0].GrossProfitText);
    }

    [Fact]
    public void Meta_InventoryOnly_e_FinancialUnavailable_na_faixa()
    {
        var noGoal = CentralDecisionLoader.Assemble(
            NoGoal(),
            Sources(Intelligence(Turnover(1)), Attention(Monitor(1))),
            Contribution(Hist(1, 15m)));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, noGoal.Decision.PlanMode);
        Assert.Equal(CommercialGoalPresentation.GoalNotConfigured, noGoal.Presentation.GoalStrip.Goal.ValueText);
        Assert.Contains(
            CentralDecisionPresentation.InventoryOnlyNote,
            noGoal.Presentation.GoalStrip.InventoryOnlyNote,
            StringComparison.Ordinal);

        var invalid = CentralDecisionLoader.Assemble(
            InvalidGoal(),
            Sources(Intelligence(Turnover(1)), Attention(Monitor(1))),
            Contribution(Hist(1, 15m)));
        Assert.Equal(CommercialGoalActionPlanMode.InventoryOnly, invalid.Decision.PlanMode);
        Assert.Equal(CommercialGoalPresentation.GoalInvalid, invalid.Presentation.GoalStrip.Goal.ValueText);

        var unavailable = CentralDecisionLoader.Assemble(
            FinancialUnavailable(),
            Sources(Intelligence(Turnover(2)), Attention(Excess(2))),
            Contribution(unavailable: true));
        Assert.Equal(CommercialGoalPresentation.EmDash, unavailable.Presentation.GoalStrip.Realized.ValueText);
        Assert.DoesNotContain("R$ 0,00", unavailable.Presentation.GoalStrip.Realized.ValueText, StringComparison.Ordinal);
        Assert.NotEqual(
            CommercialGoalPresentation.GoalNotConfigured,
            unavailable.Presentation.GoalStrip.Goal.ValueText);
    }

    [Fact]
    public void Query_budget_operacional_e_futuro()
    {
        using var db = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 12_000m);
        TestDataHelper.SeedSimpleProduct(20, 10, 6, "NT1", "Sem tese");

        var operational = CentralDecisionLoader.Load(Sep2026, Sep10);
        Assert.True(operational.QueryCount <= 15, $"operacional: {operational.QueryCount}");
        Assert.Equal(0, operational.Presentation.QueryCount);

        var future = CentralDecisionLoader.Load(Oct2026, Sep10);
        Assert.Equal(CentralDecisionState.Future, future.Decision.State);
        Assert.Empty(future.Presentation.ActNow);
        Assert.True(future.QueryCount <= 5, $"futuro: {future.QueryCount}");
        Assert.Equal(0, future.Decision.ActionPlan!.QueryCount);
    }

    [Fact]
    public void Sem_SQL_promocao_extra_json_no_helper()
    {
        var ui = ReadSource("src", "SGDB.App", "Models", "CentralDecisionUi.cs");
        Assert.DoesNotContain("Sqlite", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("extra_json", ui, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConsiderPromotion", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreNetworkMode", ui, StringComparison.Ordinal);
        Assert.Equal(CentralDecisionState.Unavailable,
            CentralDecisionUi.UnavailablePresentation(Sep2026, Sep10, "x").State);
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

    static InventoryAttentionResult Idle(int id) =>
        new()
        {
            ProductId = id,
            Action = InventoryOperatorAction.Monitor,
            PrimaryReason = InventoryAttentionReason.Idle,
            Family = InventoryAttentionFamily.Turnover,
            Priority = InventoryAttentionPriority.Medium,
            Confidence = InventoryAttentionConfidence.Reliable,
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

    static CommercialGoalProductContributionRow Hist(int id, decimal gp) =>
        new()
        {
            ProductId = id,
            ProductCode = "C" + id,
            ProductName = "Hist " + id,
            Revenue = 100m,
            Cogs = 100m - gp,
            GrossProfit = gp,
            GrossMarginPercent = 10m,
            CostQuality = CommercialGoalCostQuality.Exact,
        };

    static string Section(string xaml, string startName, string endName)
    {
        var start = xaml.IndexOf($"x:Name=\"{startName}\"", StringComparison.Ordinal);
        var end = xaml.IndexOf($"x:Name=\"{endName}\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return xaml[start..end];
    }

    static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var brace = source.IndexOf('{', start);
        Assert.True(brace > start);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[brace..(i + 1)];
            }
        }

        throw new InvalidOperationException(signature);
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

    static string ReadViewCs() =>
        ReadSource("src", "SGDB.App", "Views", "CentralDecisionModuleView.xaml.cs");

    static string ReadViewXaml() =>
        ReadSource("src", "SGDB.App", "Views", "CentralDecisionModuleView.xaml");

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
