using System.Globalization;
using System.IO;
using System.Text;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71B-B5 — apresentação PT-BR da Meta Comercial. Sem XAML, SQL ou recálculo.
/// </summary>
public class CommercialGoalPresentationTests
{
    static readonly DateOnly Sep10 = new(2026, 9, 10);
    static readonly DateOnly Sep15 = new(2026, 9, 15);
    static readonly DateOnly Oct5 = new(2026, 10, 5);
    static readonly DateOnly Aug1 = new(2026, 8, 1);
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);

    static readonly string[] TechnicalLeak =
    [
        "EstimatedLegacy",
        "CommercialGoalCostQuality",
        "GrossProfitUnavailable",
        "LinearProjection",
        "InvalidMonthlyOverride",
        "InvalidDefault",
        "ProgressSkipReason",
        "HasLinearProjection",
        "BelowPace",
        "AbovePace",
        "OnPace",
        "NoGoal",
        "NotStarted",
        "CostQuality",
        "CommercialGoalStatus",
        "CommercialGoalLimitation",
    ];

    [Fact]
    public void QueryCount_e_zero()
    {
        Assert.Equal(0, CommercialGoalPresentation.ExpectedQueryCount);
        var presented = CommercialGoalPresentation.Apply(
            Compose(ValidOverride(12_000m), Exact(80m, 30m), Sep10));
        Assert.Equal(3, presented.QueryCount);
        Assert.Equal(7, presented.Cards.Count);
        Assert.Equal(CommercialGoalPresentation.ModuleTitle, presented.ModuleTitle);
    }

    [Fact]
    public void Apply_rejeita_nulo()
    {
        Assert.Throws<ArgumentNullException>(() => CommercialGoalPresentation.Apply(null!));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void Moeda_positiva_zero_negativa(string culture)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal("R$ 12.000,00", CommercialGoalPresentation.FormatMoney(12_000m));
        Assert.Equal("R$ 7.500,00", CommercialGoalPresentation.FormatMoney(7_500m));
        Assert.Equal("R$ 0,00", CommercialGoalPresentation.FormatMoney(0m));
        Assert.Equal(ProductPriceHelper.MoneyBr(-500), CommercialGoalPresentation.FormatMoney(-500m));
        Assert.Equal(CommercialGoalPresentation.EmDash, CommercialGoalPresentation.FormatMoney(null));
        Assert.DoesNotContain("NaN", CommercialGoalPresentation.FormatMoney(-500m), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Percentual_duas_casas_sem_clamp()
    {
        Assert.Equal("62,50%", CommercialGoalPresentation.FormatPercent(0.625m));
        Assert.Equal("100,00%", CommercialGoalPresentation.FormatPercent(1m));
        Assert.Equal("125,00%", CommercialGoalPresentation.FormatPercent(1.25m));
        Assert.Equal("0,00%", CommercialGoalPresentation.FormatPercent(0m));
        Assert.Equal(CommercialGoalPresentation.EmDash, CommercialGoalPresentation.FormatPercent(null));
    }

    [Fact]
    public void Dias_singulares_e_plurais()
    {
        Assert.Equal("1 dia", CommercialGoalPresentation.FormatDays(1));
        Assert.Equal("2 dias", CommercialGoalPresentation.FormatDays(2));
        Assert.Equal("21 dias", CommercialGoalPresentation.FormatDays(21));
    }

    [Fact]
    public void NA_e_em_dash()
    {
        Assert.Equal("—", CommercialGoalPresentation.EmDash);
        Assert.Equal(InventoryProjectionPresentation.EmDash, CommercialGoalPresentation.EmDash);
    }

    [Fact]
    public void Meta_override()
    {
        var presented = Present(ValidOverride(12_000m), Exact(80m, 30m), Sep10);
        Assert.Equal(CommercialGoalPresentation.OriginOverride, presented.GoalOriginText);
        Assert.Equal("R$ 12.000,00", presented.Goal.ValueText);
        Assert.True(presented.Goal.IsAvailable);
        Assert.DoesNotContain("Não configurada", presented.Goal.ValueText, StringComparison.Ordinal);
    }

    [Fact]
    public void Meta_default()
    {
        var presented = Present(ValidDefault(12_000m), Exact(80m, 30m), Sep10);
        Assert.Equal(CommercialGoalPresentation.OriginDefault, presented.GoalOriginText);
        Assert.Equal("R$ 12.000,00", presented.Goal.ValueText);
    }

    [Fact]
    public void Meta_nao_configurada()
    {
        var presented = Present(None(), Exact(80m, 30m), Sep10);
        Assert.Equal(CommercialGoalPresentation.OriginNone, presented.GoalOriginText);
        Assert.Equal(CommercialGoalPresentation.GoalNotConfigured, presented.Goal.ValueText);
        Assert.False(presented.Goal.IsAvailable);
        Assert.DoesNotContain("R$ 0,00", presented.Goal.ValueText, StringComparison.Ordinal);
        Assert.Equal(CommercialGoalPresentation.HeadlineNoGoal, presented.Headline);
        Assert.Contains("continuam disponíveis", presented.SupportingText, StringComparison.Ordinal);
        Assert.Equal(CommercialGoalPresentation.StatusNoGoal, presented.StatusText);
    }

    [Fact]
    public void Meta_default_invalida()
    {
        var presented = Present(InvalidDefault(), Exact(80m, 30m), Sep10);
        Assert.Equal(CommercialGoalPresentation.OriginInvalidDefault, presented.GoalOriginText);
        Assert.Equal(CommercialGoalPresentation.GoalInvalid, presented.Goal.ValueText);
        Assert.Equal(CommercialGoalPresentation.OriginInvalidDefault, presented.Headline);
        Assert.Equal(CommercialGoalPresentation.SupportingInvalidDefault, presented.SupportingText);
        Assert.Equal(CommercialGoalPresentation.StatusInvalidGoal, presented.StatusText);
        Assert.NotEqual(CommercialGoalPresentation.StatusNoGoal, presented.StatusText);
        Assert.NotEqual(CommercialGoalPresentation.OriginNone, presented.GoalOriginText);
        Assert.DoesNotContain("R$ 0,00", presented.Goal.ValueText, StringComparison.Ordinal);
        Assert.Equal(CommercialGoalPresentationTone.Warning, presented.StatusTone);
    }

    [Fact]
    public void Meta_override_invalida()
    {
        var presented = Present(InvalidOverride(), Exact(80m, 30m), Sep10);
        Assert.Equal(CommercialGoalPresentation.OriginInvalidOverride, presented.GoalOriginText);
        Assert.Equal(CommercialGoalPresentation.GoalInvalid, presented.Goal.ValueText);
        Assert.Equal(CommercialGoalPresentation.OriginInvalidOverride, presented.Headline);
        Assert.Equal(CommercialGoalPresentation.SupportingInvalidOverride, presented.SupportingText);
        Assert.NotEqual(CommercialGoalPresentation.StatusNoGoal, presented.StatusText);
    }

    [Fact]
    public void Realizado_exact()
    {
        var presented = Present(ValidOverride(12_000m), Exact(100m, 40m), Sep10);
        Assert.Equal("R$ 60,00", presented.Realized.ValueText);
        Assert.True(presented.Realized.IsAvailable);
        Assert.Equal("", presented.EstimatedBadge);
        Assert.False(presented.ShowEstimatedBadge);
    }

    [Fact]
    public void Realizado_estimado_visivel()
    {
        var presented = Present(ValidOverride(12_000m), Estimated(16m, 18m, -2m), Sep10);
        Assert.Equal(ProductPriceHelper.MoneyBr(-2), presented.Realized.ValueText);
        Assert.Equal(CommercialGoalPresentation.RealizedEstimatedMark, presented.Realized.SupportingText);
        Assert.Equal(CommercialGoalPresentation.EstimatedBadge, presented.EstimatedBadge);
        Assert.True(presented.ShowEstimatedBadge);
        Assert.Equal(CommercialGoalPresentation.EstimatedExplanation, presented.EstimatedExplanation);
        Assert.Contains("estimativa", presented.EstimatedExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exato", presented.EstimatedExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.True(Limitation(presented, "legacy").IsProminent);
    }

    [Fact]
    public void Realizado_unavailable_nao_e_zero()
    {
        var presented = Present(ValidOverride(12_000m), Unavailable(10m), Sep10);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Realized.ValueText);
        Assert.False(presented.Realized.IsAvailable);
        Assert.DoesNotContain("R$ 0,00", presented.Realized.ValueText, StringComparison.Ordinal);
        Assert.Equal(CommercialGoalPresentation.HeadlineUnavailable, presented.Headline);
        Assert.Contains("custo de todas as vendas", presented.SupportingText, StringComparison.Ordinal);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Remaining.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Achievement.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.RequiredPace.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.LinearProjection.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Status.ValueText);
        Assert.DoesNotContain("Abaixo do ritmo", Visible(presented), StringComparison.Ordinal);
        Assert.DoesNotContain("Faltam", Visible(presented), StringComparison.Ordinal);
    }

    [Fact]
    public void Realizado_negativo()
    {
        var presented = Present(ValidOverride(12_000m), Exact(10m, 40m), Sep10);
        Assert.Equal(ProductPriceHelper.MoneyBr(-30), presented.Realized.ValueText);
        Assert.True(presented.Realized.IsAvailable);
        Assert.NotEqual("R$ 0,00", presented.Realized.ValueText);
    }

    [Fact]
    public void Progresso_BelowPace()
    {
        var presented = Present(ValidOverride(12_000m), Gross(5_999.99m), Sep15);
        Assert.Equal(CommercialGoalPresentation.StatusBelowPace, presented.StatusText);
        Assert.Equal(CommercialGoalPresentationTone.Attention, presented.StatusTone);
        Assert.True(presented.Remaining.IsAvailable);
        Assert.True(presented.Achievement.IsAvailable);
    }

    [Fact]
    public void Progresso_AbovePace()
    {
        var presented = Present(ValidOverride(12_000m), Gross(6_000.01m), Sep15);
        Assert.Equal(CommercialGoalPresentation.StatusAbovePace, presented.StatusText);
        Assert.Equal(CommercialGoalPresentationTone.Positive, presented.StatusTone);
    }

    [Fact]
    public void Progresso_OnPace()
    {
        var presented = Present(ValidOverride(12_000m), Gross(6_000m), Sep15);
        Assert.Equal(CommercialGoalPresentation.StatusOnPace, presented.StatusText);
        Assert.Equal("50,00%", presented.Achievement.ValueText);
        Assert.Equal("R$ 6.000,00", presented.Remaining.ValueText);
        Assert.Equal(CommercialGoalPresentationTone.Neutral, presented.StatusTone);
    }

    [Fact]
    public void Progresso_Achieved_falta_zero_percentual_acima_de_100()
    {
        var presented = Present(ValidOverride(12_000m), Gross(15_000m), Sep15);
        Assert.Equal(CommercialGoalPresentation.StatusAchieved, presented.StatusText);
        Assert.Equal("R$ 0,00", presented.Remaining.ValueText);
        Assert.Equal("125,00%", presented.Achievement.ValueText);
        Assert.Equal("R$ 0,00/dia", presented.RequiredPace.ValueText);
        Assert.Equal(CommercialGoalPresentationTone.Positive, presented.StatusTone);
    }

    [Fact]
    public void Progresso_NoGoal_financeiro_visivel()
    {
        var presented = Present(None(), Exact(90m, 20m), Sep10);
        Assert.Equal(CommercialGoalPresentation.StatusNoGoal, presented.StatusText);
        Assert.Equal("R$ 70,00", presented.Realized.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Remaining.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Achievement.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.RequiredPace.ValueText);
        Assert.Equal(CommercialGoalPresentation.HeadlineNoGoal, presented.Headline);
    }

    [Fact]
    public void Progresso_NotStarted()
    {
        var presented = Present(ValidOverride(12_000m), Exact(0m, 0m), Aug1);
        Assert.Equal(CommercialGoalPresentation.StatusNotStarted, presented.StatusText);
        Assert.Equal("R$ 0,00", presented.Realized.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.LinearProjection.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.RequiredPace.ValueText);
        Assert.False(presented.LinearProjection.IsAvailable);
    }

    [Fact]
    public void Ritmo_current_e_NA_em_outros_casos()
    {
        var current = Present(ValidOverride(12_000m), Gross(4_500m), Sep15);
        Assert.Equal(CommercialGoalPresentation.FormatPace(7_500m / 16m), current.RequiredPace.ValueText);
        Assert.EndsWith("/dia", current.RequiredPace.ValueText, StringComparison.Ordinal);

        var closed = Present(ValidOverride(12_000m), Gross(10_000m), Oct5);
        Assert.Equal(CommercialGoalPresentation.EmDash, closed.RequiredPace.ValueText);

        var future = Present(ValidOverride(12_000m), Exact(0m, 0m), Aug1);
        Assert.Equal(CommercialGoalPresentation.EmDash, future.RequiredPace.ValueText);
    }

    [Fact]
    public void Projecao_linear_nao_e_previsao()
    {
        var presented = Present(ValidOverride(12_000m), Gross(4_500m), Sep15);
        Assert.Equal(CommercialGoalPresentation.CardProjection, presented.LinearProjection.Title);
        Assert.Equal(CommercialGoalPresentation.ProjectionSupporting, presented.LinearProjection.SupportingText);
        Assert.Equal(CommercialGoalPresentation.ProjectionTooltip, presented.LinearProjection.Tooltip);
        Assert.Contains("linear", presented.LinearProjection.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("linear", presented.LinearProjection.SupportingText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Previsão", presented.LinearProjection.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Previsão", presented.LinearProjection.SupportingText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não é previsão", presented.LinearProjection.Tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Você vai fechar", Visible(presented), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("linear", presented.LinearProjection.ValueText == CommercialGoalPresentation.EmDash
            ? presented.LinearProjection.Title
            : presented.LinearProjection.Title, StringComparison.OrdinalIgnoreCase);
        Assert.True(presented.LinearProjection.IsAvailable);
        Assert.Equal(CommercialGoalPresentation.FormatMoney(4_500m / 15m * 30m), presented.LinearProjection.ValueText);
    }

    [Fact]
    public void Invalid_progresso_indisponivel_financeiro_visivel()
    {
        var presented = Present(InvalidDefault(), Exact(90m, 20m), Sep10);
        Assert.Equal("R$ 70,00", presented.Realized.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Remaining.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Achievement.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.RequiredPace.ValueText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.LinearProjection.ValueText);
        Assert.False(presented.Remaining.IsAvailable);
        Assert.NotEqual(CommercialGoalPresentation.StatusNoGoal, presented.StatusText);
    }

    [Fact]
    public void Limitacao_legacy()
    {
        var estimated = Present(ValidOverride(12_000m), Estimated(10m, 4m, 6m), Sep10);
        Assert.Contains(estimated.Limitations, l => l.Key == "legacy");
        Assert.Equal(CommercialGoalPresentation.LimitationLegacyTitle, Limitation(estimated, "legacy").Title);

        var exact = Present(ValidOverride(12_000m), Exact(10m, 4m), Sep10);
        Assert.DoesNotContain(exact.Limitations, l => l.Key == "legacy");
    }

    [Fact]
    public void Limitacao_exchanges()
    {
        var presented = Present(None(), Exact(0m, 0m), Sep10);
        var item = Limitation(presented, "exchanges");
        Assert.Equal(CommercialGoalPresentation.LimitationExchangesBody, item.Body);
        Assert.Contains("não estornam automaticamente", item.Body, StringComparison.Ordinal);
        Assert.False(item.IsProminent);
    }

    [Fact]
    public void Limitacao_projecao_linear()
    {
        var current = Present(ValidOverride(12_000m), Gross(6_000m), Sep15);
        Assert.Equal("Ritmo calculado por dias civis.", Limitation(current, "linear").Body);

        var future = Present(ValidOverride(12_000m), Exact(0m, 0m), Aug1);
        Assert.DoesNotContain(future.Limitations, l => l.Key == "linear");
    }

    [Fact]
    public void Limitacao_dia_civil_inteiro()
    {
        var current = Present(ValidOverride(12_000m), Gross(6_000m), Sep15);
        Assert.Contains("integralmente", Limitation(current, "current-day").Body, StringComparison.Ordinal);

        var closed = Present(ValidOverride(12_000m), Gross(10_000m), Oct5);
        Assert.DoesNotContain(closed.Limitations, l => l.Key == "current-day");
    }

    [Fact]
    public void Limitacao_default_historico()
    {
        var fromDefault = Present(ValidDefault(12_000m), Exact(10m, 4m), Sep10);
        var item = Limitation(fromDefault, "historical-default");
        Assert.False(item.IsProminent);
        Assert.Contains("meta padrão", item.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meta específica", item.Body, StringComparison.OrdinalIgnoreCase);

        var fromOverride = Present(ValidOverride(12_000m), Exact(10m, 4m), Sep10);
        Assert.DoesNotContain(fromOverride.Limitations, l => l.Key == "historical-default");
    }

    [Fact]
    public void Textos_nao_vazam_enum_tecnico()
    {
        var samples = new[]
        {
            Present(ValidOverride(12_000m), Gross(4_500m), Sep15),
            Present(ValidDefault(12_000m), Estimated(16m, 18m, -2m), Sep10),
            Present(None(), Exact(90m, 20m), Sep10),
            Present(InvalidOverride(), Exact(90m, 20m), Sep10),
            Present(ValidOverride(12_000m), Unavailable(10m), Sep10),
            Present(ValidOverride(12_000m), Exact(0m, 0m), Aug1),
        };

        foreach (var presented in samples)
        {
            var visible = Visible(presented);
            foreach (var leak in TechnicalLeak)
                Assert.DoesNotContain(leak, visible, StringComparison.Ordinal);
            Assert.DoesNotContain("NaN", visible, StringComparison.Ordinal);
            Assert.DoesNotContain("Infinity", visible, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Estimated_nao_afirma_exatidao()
    {
        var presented = Present(ValidOverride(12_000m), Estimated(16m, 18m, -2m), Sep10);
        var visible = Visible(presented);
        Assert.Contains("Lucro estimado", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("lucro é exato", visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("custo histórico completo", visible, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cards_na_ordem_esperada()
    {
        var presented = Present(ValidOverride(12_000m), Gross(6_000m), Sep15);
        Assert.Equal(
            new[]
            {
                CommercialGoalPresentation.CardGoal,
                CommercialGoalPresentation.CardRealized,
                CommercialGoalPresentation.CardRemaining,
                CommercialGoalPresentation.CardAchievement,
                CommercialGoalPresentation.CardPace,
                CommercialGoalPresentation.CardProjection,
                CommercialGoalPresentation.CardStatus,
            },
            presented.Cards.Select(c => c.Title).ToArray());
    }

    [Fact]
    public void Presentation_e_pura()
    {
        var src = ReadSource("src", "SGDB.App", "Models", "CommercialGoalPresentation.cs");
        Assert.DoesNotContain("DateTime.Now", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", src, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettings", src, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSetting", src, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenConnection", src, StringComparison.Ordinal);
        Assert.DoesNotContain("SolidColorBrush", src, StringComparison.Ordinal);
        Assert.DoesNotContain("#FF", src, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", src, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow", src, StringComparison.Ordinal);
        Assert.Contains("CommercialGoalComposer.Compose", ReadSource(
            "tests", "SGDB.Tests", "CommercialGoalPresentationTests.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Evaluate(", src, StringComparison.Ordinal);
        Assert.DoesNotContain("FinancialService.Load", src, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsService.Resolve", src, StringComparison.Ordinal);
    }

    static CommercialGoalPresentationSnapshot Present(
        CommercialGoalSettingResolution goal,
        CommercialGoalFinancialSnapshot financial,
        DateOnly reference) =>
        CommercialGoalPresentation.Apply(Compose(goal, financial, reference));

    static CommercialGoalSnapshot Compose(
        CommercialGoalSettingResolution goal,
        CommercialGoalFinancialSnapshot financial,
        DateOnly reference) =>
        CommercialGoalComposer.Compose(goal, financial, reference);

    static CommercialGoalLimitationPresentation Limitation(
        CommercialGoalPresentationSnapshot presented, string key) =>
        Assert.Single(presented.Limitations, l => l.Key == key);

    static string Visible(CommercialGoalPresentationSnapshot presented)
    {
        var sb = new StringBuilder();
        sb.Append(presented.ModuleTitle).Append('\n');
        sb.Append(presented.CompetenceText).Append('\n');
        sb.Append(presented.GoalOriginText).Append('\n');
        sb.Append(presented.Headline).Append('\n');
        sb.Append(presented.SupportingText).Append('\n');
        sb.Append(presented.StatusText).Append('\n');
        sb.Append(presented.EstimatedBadge).Append('\n');
        sb.Append(presented.EstimatedExplanation).Append('\n');
        foreach (var card in presented.Cards)
        {
            sb.Append(card.Title).Append('\n');
            sb.Append(card.ValueText).Append('\n');
            sb.Append(card.SupportingText).Append('\n');
            sb.Append(card.Tooltip).Append('\n');
        }

        foreach (var item in presented.Limitations)
        {
            sb.Append(item.Title).Append('\n');
            sb.Append(item.Body).Append('\n');
        }

        return sb.ToString();
    }

    static CommercialGoalSettingResolution None() =>
        new()
        {
            Competence = Sep2026,
            Source = CommercialGoalSettingSource.None,
            HasValidGoal = false,
            QueryCount = 2,
        };

    static CommercialGoalSettingResolution ValidDefault(decimal amount) =>
        new()
        {
            Competence = Sep2026,
            Source = CommercialGoalSettingSource.Default,
            GoalAmount = amount,
            HasValidGoal = true,
            QueryCount = 2,
        };

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

    static CommercialGoalSettingResolution InvalidOverride() =>
        new()
        {
            Competence = Sep2026,
            Source = CommercialGoalSettingSource.InvalidMonthlyOverride,
            HasValidGoal = false,
            QueryCount = 1,
        };

    static CommercialGoalFinancialSnapshot Exact(decimal revenue, decimal cogs) =>
        Financial(revenue, cogs, revenue - cogs, CommercialGoalCostQuality.Exact, true);

    static CommercialGoalFinancialSnapshot Gross(decimal gross) =>
        Exact(gross >= 0 ? gross + 40m : 10m, (gross >= 0 ? gross + 40m : 10m) - gross);

    static CommercialGoalFinancialSnapshot Estimated(
        decimal revenue, decimal cogs, decimal gross) =>
        Financial(revenue, cogs, gross, CommercialGoalCostQuality.EstimatedLegacy, true, estimated: true);

    static CommercialGoalFinancialSnapshot Unavailable(decimal revenue) =>
        Financial(revenue, 0m, null, CommercialGoalCostQuality.Unavailable, false);

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
