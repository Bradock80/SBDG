using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 71B-B6 — módulo visual Meta Comercial. Inspeção estrutural + pipeline B4→B5.
/// Sem instanciar UserControl WPF, sem banco da loja.
/// </summary>
public class CommercialGoalModuleTests
{
    static readonly DateOnly Sep15 = new(2026, 9, 15);
    static readonly DateOnly Oct5 = new(2026, 10, 5);
    static readonly DateOnly Aug1 = new(2026, 8, 1);
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);

    [Fact]
    public void QueryCount_ui_e_loader_sao_zero()
    {
        Assert.Equal(0, CommercialGoalUi.ExpectedQueryCount);
        Assert.Equal(0, CommercialGoalLoader.ExpectedQueryCount);
        Assert.Equal(0, CommercialGoalPresentation.ExpectedQueryCount);
        Assert.Equal("meta_comercial", CommercialGoalUi.ModuleId);
        Assert.Equal("Meta Comercial", CommercialGoalUi.ModuleTitle);
        Assert.Equal("Meta", CommercialGoalUi.ToolbarTitle);
    }

    [Fact]
    public void Hierarquia_realizado_e_heroi()
    {
        var presented = Present(ValidOverride(12_000m), Gross(6_000m), Sep15);
        var layout = CommercialGoalKpiLayout.From(presented);
        Assert.Equal(presented.Realized, layout.Hero);
        Assert.Equal(
            new[] { presented.Goal, presented.Remaining, presented.RequiredPace },
            layout.Decision);
        Assert.Equal(
            new[] { presented.Achievement, presented.LinearProjection, presented.Status },
            layout.Context);
        Assert.Equal(CommercialGoalPresentation.CardRealized, layout.Hero.Title);
        Assert.Equal("R$ 6.000,00", layout.Hero.ValueText);
    }

    [Fact]
    public void Cards_decisao_meta_falta_ritmo()
    {
        var presented = Present(ValidOverride(12_000m), Gross(6_000m), Sep15);
        var layout = CommercialGoalKpiLayout.From(presented);
        Assert.Equal(CommercialGoalPresentation.CardGoal, layout.Decision[0].Title);
        Assert.Equal("R$ 12.000,00", layout.Decision[0].ValueText);
        Assert.Equal(CommercialGoalPresentation.CardRemaining, layout.Decision[1].Title);
        Assert.Equal("R$ 6.000,00", layout.Decision[1].ValueText);
        Assert.Equal(CommercialGoalPresentation.CardPace, layout.Decision[2].Title);
        Assert.EndsWith("/dia", layout.Decision[2].ValueText, StringComparison.Ordinal);
    }

    [Fact]
    public void Cards_contexto_atingimento_projecao_status()
    {
        var presented = Present(ValidOverride(12_000m), Gross(6_000m), Sep15);
        var layout = CommercialGoalKpiLayout.From(presented);
        Assert.Equal(CommercialGoalPresentation.CardAchievement, layout.Context[0].Title);
        Assert.Equal("50,00%", layout.Context[0].ValueText);
        Assert.Equal(CommercialGoalPresentation.CardProjection, layout.Context[1].Title);
        Assert.Equal(CommercialGoalPresentation.ProjectionSupporting, layout.Context[1].SupportingText);
        Assert.Equal(CommercialGoalPresentation.StatusOnPace, layout.Context[2].ValueText);
        Assert.DoesNotContain("Previsão", layout.Context[1].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Exact_nao_mostra_estimado()
    {
        var presented = Present(ValidOverride(12_000m), Exact(100m, 40m), Sep15);
        Assert.False(CommercialGoalUi.ShowEstimatedBanner(presented));
        Assert.False(presented.ShowEstimatedBadge);
        Assert.Equal("R$ 60,00", presented.Realized.ValueText);
    }

    [Fact]
    public void Estimated_visivel()
    {
        var presented = Present(ValidOverride(12_000m), Estimated(16m, 18m, -2m), Sep15);
        Assert.True(CommercialGoalUi.ShowEstimatedBanner(presented));
        Assert.Equal(CommercialGoalPresentation.EstimatedBadge, presented.EstimatedBadge);
        Assert.Contains(presented.Limitations, l => l.Key == "legacy" && l.IsProminent);
    }

    [Fact]
    public void Unavailable_nao_vira_zero()
    {
        var presented = Present(ValidOverride(12_000m), Unavailable(10m), Sep15);
        var layout = CommercialGoalKpiLayout.From(presented);
        Assert.Equal(CommercialGoalPresentation.EmDash, layout.Hero.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Remaining.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Achievement.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.RequiredPace.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.LinearProjection.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Status.ValueText);
        Assert.Equal(CommercialGoalPresentation.HeadlineUnavailable, presented.Headline);
        Assert.True(CommercialGoalUi.ShowCallout(presented));
        Assert.NotEqual("R$ 0,00", layout.Hero.ValueText);
        Assert.NotEqual("0%", presented.Achievement.ValueText);
        Assert.NotEqual(CommercialGoalPresentation.StatusBelowPace, presented.StatusText);
    }

    [Fact]
    public void NoGoal_financeiro_visivel()
    {
        var presented = Present(None(), Exact(90m, 20m), Sep15);
        Assert.Equal("R$ 70,00", presented.Realized.ValueText);
        Assert.Equal(CommercialGoalPresentation.HeadlineNoGoal, presented.Headline);
        Assert.True(CommercialGoalUi.ShowCallout(presented));
        Assert.Equal(CommercialGoalPresentation.GoalNotConfigured, presented.Goal.ValueText);
    }

    [Fact]
    public void InvalidGoal_nao_mascara_sem_meta()
    {
        var presented = Present(InvalidDefault(), Exact(90m, 20m), Sep15);
        Assert.Equal(CommercialGoalPresentation.OriginInvalidDefault, presented.Headline);
        Assert.NotEqual(CommercialGoalPresentation.OriginNone, presented.GoalOriginText);
        Assert.NotEqual(CommercialGoalPresentation.StatusNoGoal, presented.StatusText);
        Assert.Equal("R$ 70,00", presented.Realized.ValueText);
        Assert.True(CommercialGoalUi.ShowCallout(presented));
    }

    [Fact]
    public void Achieved_e_BelowPace()
    {
        var achieved = Present(ValidOverride(12_000m), Gross(12_000m), Sep15);
        Assert.Equal(CommercialGoalPresentation.StatusAchieved, achieved.StatusText);
        Assert.Equal("R$ 0,00", achieved.Remaining.ValueText);

        var below = Present(ValidOverride(12_000m), Gross(5_000m), Sep15);
        Assert.Equal(CommercialGoalPresentation.StatusBelowPace, below.StatusText);
        Assert.Equal(CommercialGoalPresentationTone.Attention, below.StatusTone);
        Assert.False(CommercialGoalUi.ShowCallout(below));
    }

    [Fact]
    public void Future_e_Closed()
    {
        var future = Present(ValidOverride(12_000m), Exact(0m, 0m), Aug1);
        Assert.Equal(CommercialGoalPresentation.StatusNotStarted, future.StatusText);
        Assert.Equal(CommercialGoalPresentation.EmDash, future.LinearProjection.ValueText);

        var closed = Present(ValidOverride(12_000m), Gross(10_000m), Oct5);
        Assert.Equal(CommercialGoalPresentation.StatusBelowPace, closed.StatusText);
        Assert.Equal(CommercialGoalPresentation.EmDash, closed.RequiredPace.ValueText);
    }

    [Fact]
    public void RelatoriosAcesso_permite()
    {
        TestDataHelper.SetSessionRole("gestor");
        Assert.True(AccessControl.CanAccessModule(CommercialGoalUi.ModuleId));
        Assert.True(AccessControl.CanAccessCommercialPolicy());
    }

    [Fact]
    public void Admin_permite()
    {
        TestDataHelper.SetSessionRole("admin");
        Assert.True(AccessControl.CanAccessModule(CommercialGoalUi.ModuleId));
        Assert.True(CommercialGoalAdminService.CanMutate());
    }

    [Fact]
    public void Vendedor_nao_abre()
    {
        TestDataHelper.SetSessionRole("vendedor");
        Assert.False(AccessControl.CanAccessModule(CommercialGoalUi.ModuleId));
        Assert.False(CommercialGoalAdminService.CanMutate());
    }

    [Fact]
    public void Vendedor_com_relatorio_nao_edita()
    {
        TestDataHelper.SetSessionCustomPermissions("vendedor", p =>
        {
            p.RelatoriosAcesso = true;
        });
        Assert.True(AccessControl.CanAccessModule(CommercialGoalUi.ModuleId));
        Assert.False(AccessControl.CanAccessCommercialPolicy());
        Assert.False(CommercialGoalAdminService.CanMutate());
    }

    [Fact]
    public void Cliente_RedeLoja_bloqueado_na_regra()
    {
        var mode = ReadSource("src", "SGDB.App", "Services", "StoreNetworkMode.cs");
        var main = ReadSource("src", "SGDB.App", "MainWindow.xaml.cs");
        Assert.Contains("or \"meta_comercial\"", mode, StringComparison.Ordinal);
        var blockIdx = main.IndexOf("StoreNetworkMode.IsModuleBlockedOnClient(moduleId)", StringComparison.Ordinal);
        var viewIdx = main.IndexOf("new CommercialGoalModuleView()", StringComparison.Ordinal);
        Assert.InRange(blockIdx, 0, viewIdx - 1);
        Assert.DoesNotContain("StoreNetworkRpc", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_e_toolbar_chamam_mesmo_modulo()
    {
        var xaml = ReadSource("src", "SGDB.App", "MainWindow.xaml");
        Assert.Contains("Tag=\"meta_comercial\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Meta Comercial\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Label=\"Meta\"", xaml, StringComparison.Ordinal);
        var estoqueIdx = xaml.IndexOf("Header=\"Estoque Inteligente\"", StringComparison.Ordinal);
        var repoIdx = xaml.IndexOf("Header=\"Reposição Inteligente\"", StringComparison.Ordinal);
        var comboIdx = xaml.IndexOf("Header=\"Combos Inteligentes\"", StringComparison.Ordinal);
        var metaIdx = xaml.IndexOf("Header=\"Meta Comercial\"", StringComparison.Ordinal);
        Assert.True(estoqueIdx >= 0 && repoIdx > estoqueIdx && comboIdx > repoIdx && metaIdx > comboIdx);
        var toolbarCombo = xaml.IndexOf("x:Name=\"BtnCombos\"", StringComparison.Ordinal);
        var toolbarMeta = xaml.IndexOf("x:Name=\"BtnMeta\"", StringComparison.Ordinal);
        var toolbarCompras = xaml.IndexOf("x:Name=\"BtnCompras\"", StringComparison.Ordinal);
        Assert.True(toolbarCombo >= 0 && toolbarMeta > toolbarCombo && toolbarCompras > toolbarMeta);
    }

    [Fact]
    public void View_carrega_B4_B5_uma_vez()
    {
        var cs = ReadViewCs();
        Assert.Contains("CommercialGoalLoader.Load(", cs, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(cs, "CommercialGoalLoader.Load("));
        Assert.DoesNotContain("CommercialGoalProgressEngine.Evaluate", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("CommercialGoalFinancialService.Load", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("DreService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseService", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsService", cs, StringComparison.Ordinal);
        Assert.Contains("if (_loading)", MethodBody(cs, "private void Load()"));
        var ctor = MethodBody(cs, "public CommercialGoalModuleView()");
        Assert.DoesNotContain("CommercialGoalLoader", ctor);
        Assert.Contains("Loaded +=", ctor);
    }

    [Fact]
    public void View_bloqueia_cliente_antes_do_load()
    {
        var cs = ReadViewCs();
        var clientIdx = cs.IndexOf("StoreNetworkMode.IsClient", StringComparison.Ordinal);
        var loadIdx = cs.IndexOf("CommercialGoalLoader.Load(", StringComparison.Ordinal);
        Assert.InRange(clientIdx, 0, loadIdx - 1);
        Assert.Contains("ShowClientBlocked();", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void View_layout_hierarquico()
    {
        var xaml = ReadViewXaml();
        Assert.Contains("x:Name=\"HeroCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeroValue\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"28\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Decision0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Decision1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Decision2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Context1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CommercialGoalPresentation.ProjectionTooltip", xaml, StringComparison.Ordinal);
        Assert.Contains("CommercialGoalUi.AboutNumbersTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"640\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewMouseWheel", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("#FEE2E2", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Previsão", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ranking", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plano de ação", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void View_atalhos_sem_busca()
    {
        var cs = ReadViewCs();
        Assert.Contains("Key.F5", cs, StringComparison.Ordinal);
        Assert.Contains("Key.R", cs, StringComparison.Ordinal);
        Assert.Contains("Key.Escape", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.F &&", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchBox", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessControl_mapeia_RelatoriosAcesso()
    {
        var source = ReadSource("src", "SGDB.App", "Services", "AccessControl.cs");
        Assert.Contains("or \"meta_comercial\"", source, StringComparison.Ordinal);
        var start = source.IndexOf("or \"meta_comercial\"", StringComparison.Ordinal);
        Assert.Contains("RelatoriosAcesso", source[start..(start + 80)], StringComparison.Ordinal);
    }

    [Fact]
    public void Competence_titulo_amigavel()
    {
        Assert.Equal("Setembro de 2026", CommercialGoalUi.FormatCompetenceTitle(Sep2026));
        Assert.Equal(
            "Meta específica — Setembro de 2026",
            CommercialGoalUi.MonthlyCaption(Sep2026));
    }

    [Fact]
    public void Tone_nao_usa_vermelho_agressivo()
    {
        var below = CommercialGoalUi.ToneColors(CommercialGoalPresentationTone.Attention);
        Assert.DoesNotContain("#FEE2E2", below.Bg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#991B1B", below.Fg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#DC2626", below.Accent, StringComparison.OrdinalIgnoreCase);
    }

    static CommercialGoalPresentationSnapshot Present(
        CommercialGoalSettingResolution goal,
        CommercialGoalFinancialSnapshot financial,
        DateOnly reference) =>
        CommercialGoalPresentation.Apply(
            CommercialGoalComposer.Compose(goal, financial, reference));

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

    static CommercialGoalFinancialSnapshot Estimated(decimal revenue, decimal cogs, decimal gross) =>
        new()
        {
            Competence = Sep2026,
            NetCommercialRevenue = revenue,
            Cogs = cogs,
            GrossProfit = gross,
            CostQuality = CommercialGoalCostQuality.EstimatedLegacy,
            ProfitIsEstimated = true,
            GrossProfitAvailable = true,
        };

    static CommercialGoalFinancialSnapshot Unavailable(decimal revenue) =>
        new()
        {
            Competence = Sep2026,
            NetCommercialRevenue = revenue,
            CostQuality = CommercialGoalCostQuality.Unavailable,
            GrossProfitAvailable = false,
        };

    static string ReadViewCs() =>
        ReadSource("src", "SGDB.App", "Views", "CommercialGoalModuleView.xaml.cs");

    static string ReadViewXaml() =>
        ReadSource("src", "SGDB.App", "Views", "CommercialGoalModuleView.xaml");

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

    static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var brace = source.IndexOf('{', start);
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

        return source[brace..];
    }

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
