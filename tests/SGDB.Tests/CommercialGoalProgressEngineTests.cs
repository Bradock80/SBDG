using System.IO;
using SGDB.Domain.Commercial;

namespace SGDB.Tests;

/// <summary>
/// 71B-B1 — motor puro de meta comercial. Sem SQL, settings, vendas, CMV ou UI.
/// Data de referência explícita; nenhum teste usa o relógio da máquina.
/// </summary>
public class CommercialGoalProgressEngineTests
{
    static readonly DateOnly Sep10 = new(2026, 9, 10);
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);

    static CommercialGoalProgressSnapshot Eval(
        DateOnly reference,
        decimal? goal,
        decimal realized,
        CommercialCompetence? competence = null) =>
        CommercialGoalProgressEngine.Evaluate(competence ?? Sep2026, reference, goal, realized);

    [Fact]
    public void QueryCount_e_zero_e_semantica_linear()
    {
        Assert.Equal(0, CommercialGoalProgressEngine.ExpectedQueryCount);
        Assert.Equal(0, CommercialGoalProgressSnapshot.ExpectedQueryCount);
        Assert.Contains("não é previsão", CommercialGoalProgressEngine.LinearProjectionSemantics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Forecast", CommercialGoalProgressEngine.LinearProjectionSemantics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Prediction", CommercialGoalProgressEngine.LinearProjectionSemantics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dia civil inteiro", CommercialGoalProgressEngine.PartialDayLimitation, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundMoney_segue_AwayFromZero_de_MonetaryRounding()
    {
        Assert.Equal(1.01m, CommercialGoalProgressEngine.RoundMoney(1.005m));
        Assert.Equal(1.02m, CommercialGoalProgressEngine.RoundMoney(1.015m));
        Assert.Equal(-1.01m, CommercialGoalProgressEngine.RoundMoney(-1.005m));
        Assert.Contains("AwayFromZero", ReadEngineSource(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 1, 31)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 2, 28)]
    [InlineData(2024, 2, 29)]
    public void DaysInMonth_civil(int year, int month, int days)
    {
        var c = CommercialCompetence.Create(year, month);
        Assert.Equal(year, c.Year);
        Assert.Equal(month, c.Month);
        Assert.Equal(days, c.DaysInMonth);
        Assert.Equal(new DateOnly(year, month, 1), c.StartDate);
        Assert.Equal(new DateOnly(year, month, days), c.EndDate);
        Assert.Equal($"{year:0000}-{month:00}", c.ToString());
    }

    [Fact]
    public void Fevereiro_nao_bissexto_nao_tem_dia_29()
    {
        Assert.Equal(28, CommercialCompetence.Create(2026, 2).DaysInMonth);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DateOnly(2026, 2, 29));
    }

    [Fact]
    public void Create_rejeita_mes_invalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialCompetence.Create(2026, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialCompetence.Create(2026, 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommercialCompetence.Create(0, 1));
    }

    [Fact]
    public void Current_dia_10_de_setembro_30()
    {
        var snap = Eval(Sep10, 12_000m, 0m);
        Assert.Equal(CommercialGoalPeriodState.Current, snap.PeriodState);
        Assert.Equal(30, snap.DaysInMonth);
        Assert.Equal(10, snap.ElapsedCalendarDays);
        Assert.Equal(21, snap.RemainingCalendarDaysIncludingToday);
    }

    [Fact]
    public void Primeiro_dia()
    {
        var snap = Eval(new DateOnly(2026, 9, 1), 12_000m, 0m);
        Assert.Equal(CommercialGoalPeriodState.Current, snap.PeriodState);
        Assert.Equal(1, snap.ElapsedCalendarDays);
        Assert.Equal(30, snap.RemainingCalendarDaysIncludingToday);
    }

    [Fact]
    public void Meio_do_mes()
    {
        var snap = Eval(new DateOnly(2026, 9, 15), 12_000m, 6_000m);
        Assert.Equal(15, snap.ElapsedCalendarDays);
        Assert.Equal(16, snap.RemainingCalendarDaysIncludingToday);
        Assert.Equal(6_000m, snap.ExpectedLinearProgressAmount);
    }

    [Fact]
    public void Ultimo_dia()
    {
        var snap = Eval(new DateOnly(2026, 9, 30), 12_000m, 10_000m);
        Assert.Equal(30, snap.ElapsedCalendarDays);
        Assert.Equal(1, snap.RemainingCalendarDaysIncludingToday);
        Assert.Equal(2_000m, snap.RequiredGrossProfitPerRemainingDay);
        Assert.True(snap.HasRequiredPace);
    }

    [Fact]
    public void Janeiro_31_ultimo_dia()
    {
        var jan = CommercialCompetence.Create(2026, 1);
        var snap = Eval(new DateOnly(2026, 1, 31), 3_100m, 0m, jan);
        Assert.Equal(31, snap.ElapsedCalendarDays);
        Assert.Equal(1, snap.RemainingCalendarDaysIncludingToday);
        Assert.Equal(3_100m, snap.RequiredGrossProfitPerRemainingDay);
    }

    [Fact]
    public void Competencia_passada()
    {
        var snap = Eval(new DateOnly(2026, 10, 1), 12_000m, 10_000m);
        Assert.Equal(CommercialGoalPeriodState.Closed, snap.PeriodState);
        Assert.Equal(30, snap.ElapsedCalendarDays);
        Assert.Equal(0, snap.RemainingCalendarDaysIncludingToday);
        Assert.Equal(12_000m, snap.ExpectedLinearProgressAmount);
        Assert.Equal(10_000m, snap.ProjectedMonthEndGrossProfit);
        Assert.False(snap.HasRequiredPace);
        Assert.Null(snap.RequiredGrossProfitPerRemainingDay);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Competencia_futura()
    {
        var snap = Eval(new DateOnly(2026, 8, 31), 12_000m, 9_999m);
        Assert.Equal(CommercialGoalPeriodState.Future, snap.PeriodState);
        Assert.Equal(0, snap.ElapsedCalendarDays);
        Assert.Equal(30, snap.RemainingCalendarDaysIncludingToday);
        Assert.Equal(0m, snap.ExpectedLinearProgressAmount);
        Assert.Equal(12_000m, snap.RemainingAmount);
        Assert.Null(snap.AchievementRatio);
        Assert.Null(snap.RequiredGrossProfitPerRemainingDay);
        Assert.Null(snap.ProjectedMonthEndGrossProfit);
        Assert.False(snap.HasLinearProjection);
        Assert.False(snap.HasRequiredPace);
        Assert.Equal(CommercialGoalStatus.NotStarted, snap.Status);
        Assert.Equal(9_999m, snap.Realized);
    }

    [Fact]
    public void Meta_null_e_NoGoal()
    {
        var snap = Eval(Sep10, null, 100m);
        Assert.Equal(CommercialGoalStatus.NoGoal, snap.Status);
        Assert.False(snap.HasValidGoal);
        Assert.Null(snap.Goal);
        Assert.Null(snap.RemainingAmount);
        Assert.Null(snap.AchievementRatio);
        Assert.Null(snap.ExpectedLinearProgressAmount);
        Assert.Null(snap.ProjectedMonthEndGrossProfit);
        Assert.Equal(100m, snap.Realized);
    }

    [Fact]
    public void Meta_zero_e_NoGoal()
    {
        var snap = Eval(Sep10, 0m, 50m);
        Assert.Equal(CommercialGoalStatus.NoGoal, snap.Status);
        Assert.Equal(0m, snap.Goal);
        Assert.Null(snap.RemainingAmount);
        Assert.Null(snap.AchievementRatio);
    }

    [Fact]
    public void Meta_negativa_e_InvalidGoal_nao_vira_zero()
    {
        var snap = Eval(Sep10, -12_000m, 100m);
        Assert.Equal(CommercialGoalStatus.InvalidGoal, snap.Status);
        Assert.Equal(-12_000m, snap.Goal);
        Assert.Null(snap.RemainingAmount);
        Assert.Null(snap.AchievementRatio);
        Assert.False(snap.HasValidGoal);
    }

    [Fact]
    public void Realizado_zero_nao_e_NA()
    {
        var snap = Eval(Sep10, 12_000m, 0m);
        Assert.Equal(0m, snap.Realized);
        Assert.Equal(0m, snap.ProjectedMonthEndGrossProfit);
        Assert.True(snap.HasLinearProjection);
        Assert.Equal(0m, snap.AchievementRatio);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Realizado_negativo_nao_e_clampado()
    {
        var snap = Eval(Sep10, 12_000m, -300m);
        Assert.Equal(-300m, snap.Realized);
        Assert.Equal(12_300m, snap.RemainingAmount);
        Assert.Equal(-300m / 12_000m, snap.AchievementRatio);
        Assert.Equal(-300m / 10m * 30m, snap.ProjectedMonthEndGrossProfit);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Realizado_exatamente_a_meta_e_Achieved_ritmo_zero()
    {
        var snap = Eval(Sep10, 12_000m, 12_000m);
        Assert.Equal(CommercialGoalStatus.Achieved, snap.Status);
        Assert.Equal(0m, snap.RemainingAmount);
        Assert.Equal(0m, snap.RequiredGrossProfitPerRemainingDay);
        Assert.True(snap.HasRequiredPace);
        Assert.Equal(1m, snap.AchievementRatio);
    }

    [Fact]
    public void Realizado_acima_da_meta()
    {
        var snap = Eval(Sep10, 12_000m, 15_000m);
        Assert.Equal(CommercialGoalStatus.Achieved, snap.Status);
        Assert.Equal(0m, snap.RemainingAmount);
        Assert.Equal(0m, snap.RequiredGrossProfitPerRemainingDay);
        Assert.Equal(1.25m, snap.AchievementRatio);
    }

    [Fact]
    public void Ritmo_inicio_dia_1()
    {
        var snap = Eval(new DateOnly(2026, 9, 1), 12_000m, 0m);
        Assert.Equal(12_000m / 30m, snap.RequiredGrossProfitPerRemainingDay);
        Assert.Equal(0m, snap.ProjectedMonthEndGrossProfit);
    }

    [Fact]
    public void Ritmo_meio_e_projeção()
    {
        var snap = Eval(new DateOnly(2026, 9, 15), 12_000m, 4_500m);
        Assert.Equal(7_500m, snap.RemainingAmount);
        Assert.Equal(7_500m / 16m, snap.RequiredGrossProfitPerRemainingDay);
        Assert.Equal(4_500m / 15m * 30m, snap.ProjectedMonthEndGrossProfit);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Encerrada_nao_tem_ritmo_e_BelowPace()
    {
        var snap = Eval(new DateOnly(2026, 10, 5), 12_000m, 10_000m);
        Assert.Null(snap.RequiredGrossProfitPerRemainingDay);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
        Assert.Equal(10_000m, snap.ProjectedMonthEndGrossProfit);
    }

    [Fact]
    public void Encerrada_atingida()
    {
        var snap = Eval(new DateOnly(2026, 10, 5), 12_000m, 12_000m);
        Assert.Equal(CommercialGoalStatus.Achieved, snap.Status);
        Assert.Equal(12_000m, snap.ProjectedMonthEndGrossProfit);
    }

    [Fact]
    public void Encerrada_um_centavo_abaixo_e_BelowPace()
    {
        var snap = Eval(new DateOnly(2026, 10, 5), 12_000m, 11_999.99m);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Futuro_nao_expõe_ritmo_no_mesmo_campo()
    {
        var snap = Eval(new DateOnly(2026, 8, 1), 12_000m, 0m);
        Assert.Equal(CommercialGoalStatus.NotStarted, snap.Status);
        Assert.Null(snap.RequiredGrossProfitPerRemainingDay);
        Assert.False(snap.HasRequiredPace);
    }

    [Fact]
    public void OnPace_no_dia_15()
    {
        var snap = Eval(new DateOnly(2026, 9, 15), 12_000m, 6_000m);
        Assert.Equal(CommercialGoalStatus.OnPace, snap.Status);
        Assert.Equal(6_000m, snap.ExpectedLinearProgressAmount);
    }

    [Fact]
    public void AbovePace()
    {
        var snap = Eval(new DateOnly(2026, 9, 15), 12_000m, 6_000.01m);
        Assert.Equal(CommercialGoalStatus.AbovePace, snap.Status);
    }

    [Fact]
    public void BelowPace()
    {
        var snap = Eval(new DateOnly(2026, 9, 15), 12_000m, 5_999.99m);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Comparacao_por_centavos_AwayFromZero()
    {
        var jan = CommercialCompetence.Create(2026, 1);
        var expected = 12_000m * 1m / 31m;
        var rounded = CommercialGoalProgressEngine.RoundMoney(expected);
        Assert.Equal(387.10m, rounded);

        var onPace = Eval(new DateOnly(2026, 1, 1), 12_000m, 387.10m, jan);
        Assert.Equal(CommercialGoalStatus.OnPace, onPace.Status);

        var below = Eval(new DateOnly(2026, 1, 1), 12_000m, 387.09m, jan);
        Assert.Equal(CommercialGoalStatus.BelowPace, below.Status);

        var above = Eval(new DateOnly(2026, 1, 1), 12_000m, 387.11m, jan);
        Assert.Equal(CommercialGoalStatus.AbovePace, above.Status);
    }

    [Fact]
    public void Achieved_por_centavos()
    {
        var snap = Eval(Sep10, 12_000m, 11_999.996m);
        Assert.Equal(12_000.00m, CommercialGoalProgressEngine.RoundMoney(11_999.996m));
        Assert.Equal(CommercialGoalStatus.Achieved, snap.Status);
    }

    [Fact]
    public void Projecao_dia_1_com_realizado()
    {
        var snap = Eval(new DateOnly(2026, 9, 1), 12_000m, 100m);
        Assert.Equal(100m * 30m, snap.ProjectedMonthEndGrossProfit);
    }

    [Fact]
    public void Projecao_ultimo_dia_igual_realizado()
    {
        var snap = Eval(new DateOnly(2026, 9, 30), 12_000m, 8_800m);
        Assert.Equal(8_800m, snap.ProjectedMonthEndGrossProfit);
    }

    [Fact]
    public void Metricas_nao_arredondam_durante_o_calculo()
    {
        var snap = Eval(new DateOnly(2026, 1, 1), 10_000m, 0m, CommercialCompetence.Create(2026, 1));
        Assert.Equal(10_000m / 31m, snap.ExpectedLinearProgressAmount);
        Assert.NotEqual(CommercialGoalProgressEngine.RoundMoney(10_000m / 31m), snap.ExpectedLinearProgressAmount);
    }

    [Fact]
    public void Bissexto_29_fevereiro_e_ultimo_dia()
    {
        var feb = CommercialCompetence.Create(2024, 2);
        var snap = Eval(new DateOnly(2024, 2, 29), 2_900m, 2_900m, feb);
        Assert.Equal(29, snap.DaysInMonth);
        Assert.Equal(29, snap.ElapsedCalendarDays);
        Assert.Equal(1, snap.RemainingCalendarDaysIncludingToday);
        Assert.Equal(CommercialGoalStatus.Achieved, snap.Status);
        Assert.Equal(0m, snap.RequiredGrossProfitPerRemainingDay);
    }

    [Fact]
    public void FromDate_usa_ano_mes_da_referencia()
    {
        var c = CommercialCompetence.FromDate(new DateOnly(2024, 2, 29));
        Assert.Equal(2024, c.Year);
        Assert.Equal(2, c.Month);
        Assert.Equal(29, c.DaysInMonth);
    }

    [Fact]
    public void Engine_nao_depende_de_relogio_nem_io()
    {
        var src = ReadEngineSource();
        Assert.DoesNotContain("DateTime.Now", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppSettings", src, StringComparison.Ordinal);
        Assert.DoesNotContain("WPF", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sales", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cost_at_sale", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Forecast", src, StringComparison.Ordinal);
    }

    static string ReadEngineSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "SGDB.Domain", "Commercial", "CommercialGoalProgressEngine.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("CommercialGoalProgressEngine.cs");
    }
}
