using System.Globalization;
using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71B-B4 — compositor da Meta Comercial. Compose é puro; Load usa banco TEMP.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CommercialGoalComposerTests
{
    static readonly DateOnly Sep10 = new(2026, 9, 10);
    static readonly DateOnly Sep15 = new(2026, 9, 15);
    static readonly DateOnly Oct5 = new(2026, 10, 5);
    static readonly DateOnly Aug1 = new(2026, 8, 1);
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly CommercialCompetence Oct2026 = CommercialCompetence.Create(2026, 10);

    [Fact]
    public void QueryCount_proprio_e_zero()
    {
        Assert.Equal(0, CommercialGoalComposer.OwnQueryCount);
        Assert.Equal(0, CommercialGoalSnapshot.OwnQueryCount);
        Assert.Equal(0, CommercialGoalComposerService.OwnQueryCount);
        Assert.Equal(2, CommercialGoalComposer.InheritedFinancialQueryCount);
        Assert.Equal(2, CommercialGoalFinancialSnapshot.ExpectedQueryCount);
        Assert.Equal(0, CommercialGoalProgressEngine.ExpectedQueryCount);
    }

    [Fact]
    public void Competence_mismatch_e_erro_tecnico()
    {
        var goal = ValidOverride(Sep2026, 12_000m);
        var financial = Exact(Oct2026, 100m, 40m);
        var ex = Assert.Throws<ArgumentException>(() =>
            CommercialGoalComposer.Compose(goal, financial, Sep10));
        Assert.Contains("competência", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_rejeita_nulos()
    {
        var financial = Exact(Sep2026, 0, 0);
        Assert.Throws<ArgumentNullException>(() =>
            CommercialGoalComposer.Compose(null!, financial, Sep10));
        Assert.Throws<ArgumentNullException>(() =>
            CommercialGoalComposer.Compose(ValidOverride(Sep2026, 12_000m), null!, Sep10));
    }

    [Fact]
    public void Sem_meta()
    {
        var snap = CommercialGoalComposer.Compose(None(Sep2026), Exact(Sep2026, 80m, 30m), Sep10);
        Assert.Equal(CommercialGoalSettingSource.None, snap.GoalSource);
        Assert.False(snap.HasValidGoal);
        Assert.Null(snap.GoalAmount);
        Assert.Equal(50m, snap.GrossProfit);
        Assert.True(snap.ProgressAvailable);
        Assert.Equal(CommercialGoalStatus.NoGoal, snap.Status);
        Assert.Equal(50m, snap.RealizedGrossProfit);
        Assert.Equal(CommercialGoalProgressSkipReason.None, snap.ProgressSkipReason);
        Assert.Equal(4, snap.QueryCount);
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.LinearCalendarProjection));
    }

    [Fact]
    public void Default()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidDefault(Sep2026, 12_000m), Exact(Sep2026, 80m, 30m), Sep10);
        Assert.Equal(CommercialGoalSettingSource.Default, snap.GoalSource);
        Assert.Equal(12_000m, snap.GoalAmount);
        Assert.True(snap.HasValidGoal);
        Assert.True(snap.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));
        Assert.Equal(4, snap.QueryCount);
    }

    [Fact]
    public void Override()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 18_000m), Exact(Sep2026, 80m, 30m), Sep10);
        Assert.Equal(CommercialGoalSettingSource.MonthlyOverride, snap.GoalSource);
        Assert.Equal(18_000m, snap.GoalAmount);
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));
        Assert.Equal(3, snap.QueryCount);
    }

    [Fact]
    public void Override_vence_default_na_resolucao_composta()
    {
        var resolution = new CommercialGoalSettingResolution
        {
            Competence = Sep2026,
            Source = CommercialGoalSettingSource.MonthlyOverride,
            GoalAmount = 18_000m,
            HasValidGoal = true,
            MonthlyOverride = new CommercialGoalStoredSetting
            {
                Status = CommercialGoalStoredSettingStatus.Configured,
                GoalAmount = 18_000m,
                QueryCount = 1,
            },
            DefaultSetting = null,
            QueryCount = 1,
        };
        var snap = CommercialGoalComposer.Compose(resolution, Exact(Sep2026, 10m, 4m), Sep10);
        Assert.Equal(18_000m, snap.GoalAmount);
        Assert.Equal(CommercialGoalSettingSource.MonthlyOverride, snap.GoalSource);
        Assert.Null(snap.GoalResolution.DefaultSetting);
    }

    [Fact]
    public void Default_invalido()
    {
        var snap = CommercialGoalComposer.Compose(InvalidDefault(Sep2026), Exact(Sep2026, 80m, 30m), Sep10);
        Assert.Equal(CommercialGoalSettingSource.InvalidDefault, snap.GoalSource);
        Assert.False(snap.HasValidGoal);
        Assert.Null(snap.GoalAmount);
        Assert.False(snap.ProgressAvailable);
        Assert.Null(snap.Progress);
        Assert.Null(snap.Status);
        Assert.Null(snap.RealizedGrossProfit);
        Assert.Equal(CommercialGoalProgressSkipReason.InvalidGoalConfiguration, snap.ProgressSkipReason);
        Assert.Equal(50m, snap.GrossProfit);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.FinancialQuality);
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));
        Assert.NotEqual(CommercialGoalStatus.NoGoal, snap.Status);
    }

    [Fact]
    public void Override_invalido()
    {
        var snap = CommercialGoalComposer.Compose(
            InvalidOverride(Sep2026), Exact(Sep2026, 80m, 30m), Sep10);
        Assert.Equal(CommercialGoalSettingSource.InvalidMonthlyOverride, snap.GoalSource);
        Assert.False(snap.ProgressAvailable);
        Assert.Equal(CommercialGoalProgressSkipReason.InvalidGoalConfiguration, snap.ProgressSkipReason);
        Assert.Equal(50m, snap.GrossProfit);
        Assert.Equal(3, snap.QueryCount);
    }

    [Fact]
    public void Financeiro_Exact()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 100m, 40m), Sep10);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.FinancialQuality);
        Assert.Equal(60m, snap.GrossProfit);
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));
        Assert.Equal(60m, snap.Progress!.Realized);
        Assert.True(snap.ProgressAvailable);
    }

    [Fact]
    public void Financeiro_EstimatedLegacy()
    {
        var financial = Estimated(Sep2026, 16m, 18m, -2m);
        var snap = CommercialGoalComposer.Compose(ValidOverride(Sep2026, 12_000m), financial, Sep10);
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, snap.FinancialQuality);
        Assert.True(snap.ProgressAvailable);
        Assert.Equal(-2m, snap.Progress!.Realized);
        Assert.True(snap.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));
        Assert.NotEqual(CommercialGoalCostQuality.Unavailable, snap.FinancialQuality);
        Assert.Equal(CommercialGoalProgressSkipReason.None, snap.ProgressSkipReason);
    }

    [Fact]
    public void Financeiro_Unavailable()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Unavailable(Sep2026, 10m), Sep10);
        Assert.Equal(CommercialGoalCostQuality.Unavailable, snap.FinancialQuality);
        Assert.False(snap.GrossProfitAvailable);
        Assert.Null(snap.GrossProfit);
        Assert.False(snap.ProgressAvailable);
        Assert.Null(snap.RealizedGrossProfit);
        Assert.Equal(CommercialGoalProgressSkipReason.GrossProfitUnavailable, snap.ProgressSkipReason);
        Assert.Equal(10m, snap.Financial.NetCommercialRevenue);
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));
    }

    [Fact]
    public void Mes_valido_zerado()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 0m, 0m), Sep10);
        Assert.True(snap.GrossProfitAvailable);
        Assert.Equal(0m, snap.GrossProfit);
        Assert.True(snap.ProgressAvailable);
        Assert.Equal(0m, snap.RealizedGrossProfit);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.FinancialQuality);
    }

    [Fact]
    public void GrossProfit_negativo()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 10m, 40m), Sep10);
        Assert.Equal(-30m, snap.GrossProfit);
        Assert.Equal(-30m, snap.RealizedGrossProfit);
        Assert.Equal(-30m, snap.Progress!.Realized);
        Assert.Equal(12_030m, snap.Progress.RemainingAmount);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Meta_valida_Exact()
    {
        var expected = CommercialGoalProgressEngine.Evaluate(Sep2026, Sep15, 12_000m, 6_000m);
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 10_000m, 4_000m), Sep15);
        Assert.Equal(expected.Status, snap.Status);
        Assert.Equal(expected.Realized, snap.RealizedGrossProfit);
        Assert.Equal(expected.RemainingAmount, snap.Progress!.RemainingAmount);
        Assert.Equal(expected.AchievementRatio, snap.Progress.AchievementRatio);
        Assert.Equal(expected.RequiredGrossProfitPerRemainingDay, snap.Progress.RequiredGrossProfitPerRemainingDay);
        Assert.Equal(expected.ProjectedMonthEndGrossProfit, snap.Progress.ProjectedMonthEndGrossProfit);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.FinancialQuality);
    }

    [Fact]
    public void Meta_valida_EstimatedLegacy_propaga_limitacao()
    {
        var financial = Estimated(Sep2026, 100m, 40m, 60m);
        var snap = CommercialGoalComposer.Compose(ValidOverride(Sep2026, 12_000m), financial, Sep15);
        var expected = CommercialGoalProgressEngine.Evaluate(Sep2026, Sep15, 12_000m, 60m);
        Assert.Equal(expected.Status, snap.Status);
        Assert.Equal(60m, snap.Progress!.Realized);
        Assert.True(snap.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));
        Assert.True(snap.ProgressAvailable);
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, snap.FinancialQuality);
    }

    [Fact]
    public void Meta_valida_Unavailable_nao_vira_realizado_zero()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Unavailable(Sep2026, 200m), Sep15);
        Assert.Null(snap.Progress);
        Assert.Null(snap.RealizedGrossProfit);
        Assert.Null(snap.Status);
        Assert.DoesNotContain(CommercialGoalStatus.BelowPace, new[] { snap.Status ?? (CommercialGoalStatus)(-1) });
        Assert.Equal(200m, snap.Financial.NetCommercialRevenue);
        Assert.Equal(0m, snap.Financial.Cogs);
    }

    [Fact]
    public void Sem_meta_financeiro_Exact()
    {
        var snap = CommercialGoalComposer.Compose(None(Sep2026), Exact(Sep2026, 90m, 20m), Sep10);
        Assert.Equal(CommercialGoalStatus.NoGoal, snap.Status);
        Assert.Equal(70m, snap.GrossProfit);
        Assert.Equal(70m, snap.RealizedGrossProfit);
        Assert.Equal(CommercialGoalCostQuality.Exact, snap.FinancialQuality);
        Assert.True(snap.ProgressAvailable);
    }

    [Fact]
    public void Sem_meta_Estimated()
    {
        var snap = CommercialGoalComposer.Compose(
            None(Sep2026), Estimated(Sep2026, 16m, 18m, -2m), Sep10);
        Assert.Equal(CommercialGoalStatus.NoGoal, snap.Status);
        Assert.Equal(-2m, snap.GrossProfit);
        Assert.True(snap.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));
        Assert.True(snap.ProgressAvailable);
    }

    [Fact]
    public void Configuracao_invalida_financeiro_Exact()
    {
        var snap = CommercialGoalComposer.Compose(
            InvalidDefault(Sep2026), Exact(Sep2026, 90m, 20m), Sep10);
        Assert.Equal(70m, snap.GrossProfit);
        Assert.False(snap.ProgressAvailable);
        Assert.Equal(CommercialGoalProgressSkipReason.InvalidGoalConfiguration, snap.ProgressSkipReason);
        Assert.NotEqual(CommercialGoalSettingSource.None, snap.GoalSource);
    }

    [Fact]
    public void Current_BelowPace()
    {
        var snap = ComposeCurrent(5_999.99m);
        Assert.Equal(CommercialGoalPeriodState.Current, snap.Progress!.PeriodState);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Current_AbovePace()
    {
        Assert.Equal(CommercialGoalStatus.AbovePace, ComposeCurrent(6_000.01m).Status);
    }

    [Fact]
    public void Current_OnPace()
    {
        Assert.Equal(CommercialGoalStatus.OnPace, ComposeCurrent(6_000m).Status);
    }

    [Fact]
    public void Current_Achieved()
    {
        var snap = ComposeCurrent(12_000m);
        Assert.Equal(CommercialGoalStatus.Achieved, snap.Status);
        Assert.Equal(0m, snap.Progress!.RemainingAmount);
    }

    [Fact]
    public void Current_negativo()
    {
        var snap = ComposeCurrent(-300m);
        Assert.Equal(-300m, snap.RealizedGrossProfit);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Past_abaixo_da_meta()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 20_000m, 10_000m), Oct5);
        Assert.Equal(CommercialGoalPeriodState.Closed, snap.Progress!.PeriodState);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
        Assert.Null(snap.Progress.RequiredGrossProfitPerRemainingDay);
        Assert.False(snap.Progress.HasRequiredPace);
        Assert.Equal(10_000m, snap.Progress.ProjectedMonthEndGrossProfit);
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.CurrentDayTreatedAsWholeDay));
    }

    [Fact]
    public void Past_atingida()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 20_000m, 8_000m), Oct5);
        Assert.Equal(CommercialGoalStatus.Achieved, snap.Status);
        Assert.Equal(12_000m, snap.Progress!.ProjectedMonthEndGrossProfit);
    }

    [Fact]
    public void Past_EstimatedLegacy()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Estimated(Sep2026, 20_000m, 11_000m, 9_000m), Oct5);
        Assert.Equal(CommercialGoalPeriodState.Closed, snap.Progress!.PeriodState);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
        Assert.Equal(9_000m, snap.RealizedGrossProfit);
        Assert.True(snap.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));
        Assert.True(snap.HasLimitation(CommercialGoalLimitation.LinearCalendarProjection));
    }

    [Fact]
    public void Future_meta_valida_zero_NotStarted()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 0m, 0m), Aug1);
        Assert.Equal(CommercialGoalPeriodState.Future, snap.Progress!.PeriodState);
        Assert.Equal(CommercialGoalStatus.NotStarted, snap.Status);
        Assert.Equal(0m, snap.RealizedGrossProfit);
        Assert.True(snap.GrossProfitAvailable);
        Assert.Null(snap.Progress.ProjectedMonthEndGrossProfit);
        Assert.False(snap.Progress.HasLinearProjection);
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.LinearCalendarProjection));
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.CurrentDayTreatedAsWholeDay));
        Assert.NotEqual(CommercialGoalCostQuality.Unavailable, snap.FinancialQuality);
    }

    [Fact]
    public void Limitacao_legacy_somente_quando_aplicavel()
    {
        var exact = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 10m, 4m), Sep10);
        Assert.False(exact.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));

        var estimated = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Estimated(Sep2026, 10m, 4m, 6m), Sep10);
        Assert.True(estimated.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));

        var unavailable = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Unavailable(Sep2026, 10m), Sep10);
        Assert.False(unavailable.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));
    }

    [Fact]
    public void Limitacao_exchanges_V1_sempre()
    {
        foreach (var snap in new[]
                 {
                     CommercialGoalComposer.Compose(None(Sep2026), Exact(Sep2026, 0, 0), Sep10),
                     CommercialGoalComposer.Compose(ValidOverride(Sep2026, 12_000m), Unavailable(Sep2026, 1m), Sep10),
                     CommercialGoalComposer.Compose(InvalidDefault(Sep2026), Exact(Sep2026, 1m, 0m), Sep10),
                     CommercialGoalComposer.Compose(ValidDefault(Sep2026, 12_000m), Estimated(Sep2026, 1m, 0m, 1m), Oct5),
                 })
        {
            Assert.True(snap.HasLimitation(CommercialGoalLimitation.ExchangesNotAdjusted));
        }
    }

    [Fact]
    public void Limitacao_projecao_linear()
    {
        var current = ComposeCurrent(6_000m);
        Assert.True(current.HasLimitation(CommercialGoalLimitation.LinearCalendarProjection));

        var closed = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 10m, 4m), Oct5);
        Assert.True(closed.HasLimitation(CommercialGoalLimitation.LinearCalendarProjection));

        var future = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 0, 0), Aug1);
        Assert.False(future.HasLimitation(CommercialGoalLimitation.LinearCalendarProjection));

        var noGoal = CommercialGoalComposer.Compose(None(Sep2026), Exact(Sep2026, 10m, 4m), Sep10);
        Assert.False(noGoal.HasLimitation(CommercialGoalLimitation.LinearCalendarProjection));
    }

    [Fact]
    public void Limitacao_dia_civil_inteiro()
    {
        var current = ComposeCurrent(6_000m);
        Assert.True(current.HasLimitation(CommercialGoalLimitation.CurrentDayTreatedAsWholeDay));

        var closed = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 10m, 4m), Oct5);
        Assert.False(closed.HasLimitation(CommercialGoalLimitation.CurrentDayTreatedAsWholeDay));

        var future = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 0, 0), Aug1);
        Assert.False(future.HasLimitation(CommercialGoalLimitation.CurrentDayTreatedAsWholeDay));
    }

    [Fact]
    public void Limitacao_default_historico()
    {
        var fromDefault = CommercialGoalComposer.Compose(
            ValidDefault(Sep2026, 12_000m), Exact(Sep2026, 10m, 4m), Sep10);
        Assert.True(fromDefault.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));

        var fromOverride = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 10m, 4m), Sep10);
        Assert.False(fromOverride.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));

        var none = CommercialGoalComposer.Compose(None(Sep2026), Exact(Sep2026, 10m, 4m), Sep10);
        Assert.False(none.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));

        var invalid = CommercialGoalComposer.Compose(
            InvalidDefault(Sep2026), Exact(Sep2026, 10m, 4m), Sep10);
        Assert.False(invalid.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));
    }

    [Fact]
    public void Critico_Unavailable_nao_vira_Realized_zero()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Unavailable(Sep2026, 0m), Sep10);
        Assert.Null(snap.RealizedGrossProfit);
        Assert.Null(snap.Progress);
        Assert.NotEqual(0m, snap.RealizedGrossProfit);
    }

    [Fact]
    public void Critico_InvalidGoal_nao_vira_NoGoal()
    {
        var snap = CommercialGoalComposer.Compose(
            InvalidOverride(Sep2026), Exact(Sep2026, 10m, 4m), Sep10);
        Assert.NotEqual(CommercialGoalSettingSource.None, snap.GoalSource);
        Assert.NotEqual(CommercialGoalStatus.NoGoal, snap.Status);
        Assert.Equal(CommercialGoalSettingSource.InvalidMonthlyOverride, snap.GoalSource);
        Assert.False(snap.ProgressAvailable);
    }

    [Fact]
    public void Critico_Estimated_nao_vira_Unavailable()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Estimated(Sep2026, 10m, 4m, 6m), Sep10);
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, snap.FinancialQuality);
        Assert.True(snap.ProgressAvailable);
        Assert.Equal(6m, snap.RealizedGrossProfit);
        Assert.NotEqual(CommercialGoalProgressSkipReason.GrossProfitUnavailable, snap.ProgressSkipReason);
    }

    [Fact]
    public void Critico_lucro_negativo_nao_vira_zero()
    {
        var snap = CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, 5m, 40m), Sep10);
        Assert.Equal(-35m, snap.GrossProfit);
        Assert.Equal(-35m, snap.RealizedGrossProfit);
        Assert.NotEqual(0m, snap.RealizedGrossProfit);
    }

    [Fact]
    public void Invalido_e_Unavailable_preservam_as_duas_dimensoes()
    {
        var snap = CommercialGoalComposer.Compose(
            InvalidDefault(Sep2026), Unavailable(Sep2026, 15m), Sep10);
        Assert.Equal(
            CommercialGoalProgressSkipReason.InvalidGoalConfiguration
            | CommercialGoalProgressSkipReason.GrossProfitUnavailable,
            snap.ProgressSkipReason);
        Assert.Equal(CommercialGoalSettingSource.InvalidDefault, snap.GoalSource);
        Assert.Equal(CommercialGoalCostQuality.Unavailable, snap.FinancialQuality);
        Assert.False(snap.ProgressAvailable);
        Assert.Equal(15m, snap.Financial.NetCommercialRevenue);
    }

    [Fact]
    public void Sem_meta_e_Unavailable_nao_inventa_progresso()
    {
        var snap = CommercialGoalComposer.Compose(None(Sep2026), Unavailable(Sep2026, 15m), Sep10);
        Assert.Equal(CommercialGoalSettingSource.None, snap.GoalSource);
        Assert.Equal(CommercialGoalProgressSkipReason.GrossProfitUnavailable, snap.ProgressSkipReason);
        Assert.False(snap.ProgressAvailable);
        Assert.Equal(15m, snap.Financial.NetCommercialRevenue);
    }

    [Fact]
    public void Load_override_queryCount_3()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 12_000m);
        var snap = CommercialGoalComposerService.Load(Sep2026, Sep10);
        Assert.Equal(3, snap.QueryCount);
        Assert.Equal(CommercialGoalSettingSource.MonthlyOverride, snap.GoalSource);
        Assert.Equal(12_000m, snap.GoalAmount);
        Assert.Equal(0m, snap.GrossProfit);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Load_default_queryCount_4()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetDefault(12_000m);
        var snap = CommercialGoalComposerService.Load(Sep2026, Sep10);
        Assert.Equal(4, snap.QueryCount);
        Assert.Equal(CommercialGoalSettingSource.Default, snap.GoalSource);
        Assert.True(snap.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));
    }

    [Fact]
    public void Load_sem_meta_mostra_financeiro()
    {
        using var _ = Begin();
        var day = new DateTime(2026, 9, 10);
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B4S", "Simples");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), day);

        var snap = CommercialGoalComposerService.Load(Sep2026, Sep10);
        Assert.Equal(CommercialGoalSettingSource.None, snap.GoalSource);
        Assert.Equal(10m, snap.Financial.NetCommercialRevenue);
        Assert.Equal(6m, snap.Financial.Cogs);
        Assert.Equal(4m, snap.GrossProfit);
        Assert.Equal(CommercialGoalStatus.NoGoal, snap.Status);
        Assert.Equal(4m, snap.RealizedGrossProfit);
        Assert.Equal(4, snap.QueryCount);
    }

    [Fact]
    public void Load_override_vence_default_e_usa_B1()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetDefault(12_000m);
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 18_000m);
        var day = new DateTime(2026, 9, 15);
        var pid = TestDataHelper.SeedSimpleProduct(50, 10, 4, "B4O", "Override");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), day);

        var snap = CommercialGoalComposerService.Load(Sep2026, Sep15);
        Assert.Equal(CommercialGoalSettingSource.MonthlyOverride, snap.GoalSource);
        Assert.Equal(18_000m, snap.GoalAmount);
        Assert.Equal(6m, snap.RealizedGrossProfit);
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
        Assert.Equal(3, snap.QueryCount);
        Assert.False(snap.HasLimitation(CommercialGoalLimitation.HistoricalDefaultCanChange));
    }

    [Fact]
    public void Load_EstimatedLegacy_calcula_e_marca()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 12_000m);
        var day = new DateTime(2026, 9, 10);
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 5, "B4L", "Legado");
        InsertLegacySale(pid, 2, 8, day);
        SetCost(pid, 9);

        var snap = CommercialGoalComposerService.Load(Sep2026, Sep10);
        Assert.Equal(CommercialGoalCostQuality.EstimatedLegacy, snap.FinancialQuality);
        Assert.Equal(-2m, snap.GrossProfit);
        Assert.Equal(-2m, snap.RealizedGrossProfit);
        Assert.True(snap.ProgressAvailable);
        Assert.True(snap.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate));
        Assert.Equal(CommercialGoalStatus.BelowPace, snap.Status);
    }

    [Fact]
    public void Load_Unavailable_nao_produz_progresso_zero()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 12_000m);
        var day = new DateTime(2026, 9, 10);
        var pid = TestDataHelper.SeedSimpleProduct(10, 10, 4, "B4U", "Unavail");
        var saleId = InsertLegacySale(pid, 1, 10, day);
        SetItemQuantityUnavailable(saleId);

        var snap = CommercialGoalComposerService.Load(Sep2026, Sep10);
        Assert.Equal(CommercialGoalCostQuality.Unavailable, snap.FinancialQuality);
        Assert.Null(snap.GrossProfit);
        Assert.False(snap.ProgressAvailable);
        Assert.Null(snap.RealizedGrossProfit);
        Assert.Equal(CommercialGoalProgressSkipReason.GrossProfitUnavailable, snap.ProgressSkipReason);
        Assert.Equal(10m, snap.Financial.NetCommercialRevenue);
    }

    [Fact]
    public void Load_default_invalido_preserva_config_e_financeiro()
    {
        using var _ = Begin();
        AppSettingsService.SetSetting(CommercialGoalSettingKeys.Default, "abc");
        var day = new DateTime(2026, 9, 10);
        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B4I", "Inv");
        TestDataHelper.FinalizeSimpleCashSale(pid, 1, 10, 10);
        SetSessionDate(LastSaleId(), day);

        var snap = CommercialGoalComposerService.Load(Sep2026, Sep10);
        Assert.Equal(CommercialGoalSettingSource.InvalidDefault, snap.GoalSource);
        Assert.False(snap.ProgressAvailable);
        Assert.Equal(4m, snap.GrossProfit);
        Assert.NotEqual(CommercialGoalStatus.NoGoal, snap.Status);
    }

    [Fact]
    public void Load_DateTime_usa_somente_a_data_civil()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 12_000m);
        var snap = CommercialGoalComposerService.Load(Sep2026, new DateTime(2026, 9, 15, 23, 59, 59));
        Assert.Equal(Sep15, snap.ReferenceDate);
        Assert.Equal(CommercialGoalPeriodState.Current, snap.Progress!.PeriodState);
        Assert.Equal(15, snap.Progress.ElapsedCalendarDays);
    }

    [Fact]
    public void Load_competencia_futura_zero_e_NotStarted()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Oct2026, 12_000m);
        var snap = CommercialGoalComposerService.Load(Oct2026, Sep10);
        Assert.Equal(CommercialGoalStatus.NotStarted, snap.Status);
        Assert.Equal(0m, snap.GrossProfit);
        Assert.True(snap.GrossProfitAvailable);
        Assert.Null(snap.Progress!.ProjectedMonthEndGrossProfit);
    }

    [Fact]
    public void Composer_nao_tem_sql_relogio_nem_ui()
    {
        var domain = ReadSource("src", "SGDB.Domain", "Commercial", "CommercialGoalComposer.cs");
        Assert.DoesNotContain("DateTime.Now", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", domain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("WPF", domain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppSettings", domain, StringComparison.Ordinal);
        Assert.DoesNotContain("Forecast", domain, StringComparison.Ordinal);

        var snapshot = ReadSource("src", "SGDB.Domain", "Commercial", "CommercialGoalSnapshot.cs");
        Assert.DoesNotContain("R$", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("previsão", snapshot, StringComparison.OrdinalIgnoreCase);

        var loader = ReadSource("src", "SGDB.App", "Services", "CommercialGoalComposerService.cs");
        Assert.DoesNotContain("DateTime.Now", loader, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", loader, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSetting", loader, StringComparison.Ordinal);
        Assert.Contains("CommercialGoalSettingsService.Resolve", loader, StringComparison.Ordinal);
        Assert.Contains("CommercialGoalFinancialService.Load", loader, StringComparison.Ordinal);
        Assert.Contains("CommercialGoalComposer.Compose", loader, StringComparison.Ordinal);
    }

    static CommercialGoalSnapshot ComposeCurrent(decimal realized)
    {
        var revenue = realized >= 0 ? realized + 40m : 10m;
        var cogs = revenue - realized;
        return CommercialGoalComposer.Compose(
            ValidOverride(Sep2026, 12_000m), Exact(Sep2026, revenue, cogs), Sep15);
    }

    static CommercialGoalSettingResolution None(CommercialCompetence c) =>
        new()
        {
            Competence = c,
            Source = CommercialGoalSettingSource.None,
            HasValidGoal = false,
            QueryCount = 2,
        };

    static CommercialGoalSettingResolution ValidDefault(CommercialCompetence c, decimal amount) =>
        new()
        {
            Competence = c,
            Source = CommercialGoalSettingSource.Default,
            GoalAmount = amount,
            HasValidGoal = true,
            DefaultSetting = new CommercialGoalStoredSetting
            {
                Status = CommercialGoalStoredSettingStatus.Configured,
                GoalAmount = amount,
                QueryCount = 1,
            },
            QueryCount = 2,
        };

    static CommercialGoalSettingResolution ValidOverride(CommercialCompetence c, decimal amount) =>
        new()
        {
            Competence = c,
            Source = CommercialGoalSettingSource.MonthlyOverride,
            GoalAmount = amount,
            HasValidGoal = true,
            MonthlyOverride = new CommercialGoalStoredSetting
            {
                Status = CommercialGoalStoredSettingStatus.Configured,
                GoalAmount = amount,
                QueryCount = 1,
            },
            QueryCount = 1,
        };

    static CommercialGoalSettingResolution InvalidDefault(CommercialCompetence c) =>
        new()
        {
            Competence = c,
            Source = CommercialGoalSettingSource.InvalidDefault,
            HasValidGoal = false,
            DefaultSetting = new CommercialGoalStoredSetting
            {
                Status = CommercialGoalStoredSettingStatus.Invalid,
                RawValue = "abc",
                Reasons =
                [
                    CommercialGoalStoredSettingReason.NonInvariantFormat,
                    CommercialGoalStoredSettingReason.Invalid,
                ],
                QueryCount = 1,
            },
            QueryCount = 2,
        };

    static CommercialGoalSettingResolution InvalidOverride(CommercialCompetence c) =>
        new()
        {
            Competence = c,
            Source = CommercialGoalSettingSource.InvalidMonthlyOverride,
            HasValidGoal = false,
            MonthlyOverride = new CommercialGoalStoredSetting
            {
                Status = CommercialGoalStoredSettingStatus.Invalid,
                RawValue = "xyz",
                Reasons =
                [
                    CommercialGoalStoredSettingReason.NonInvariantFormat,
                    CommercialGoalStoredSettingReason.Invalid,
                ],
                QueryCount = 1,
            },
            QueryCount = 1,
        };

    static CommercialGoalFinancialSnapshot Exact(
        CommercialCompetence c, decimal revenue, decimal cogs) =>
        Financial(c, revenue, cogs, revenue - cogs, CommercialGoalCostQuality.Exact, available: true);

    static CommercialGoalFinancialSnapshot Estimated(
        CommercialCompetence c, decimal revenue, decimal cogs, decimal gross) =>
        Financial(c, revenue, cogs, gross, CommercialGoalCostQuality.EstimatedLegacy, available: true, estimated: true);

    static CommercialGoalFinancialSnapshot Unavailable(CommercialCompetence c, decimal revenue) =>
        Financial(c, revenue, 0m, null, CommercialGoalCostQuality.Unavailable, available: false);

    static CommercialGoalFinancialSnapshot Financial(
        CommercialCompetence c,
        decimal revenue,
        decimal cogs,
        decimal? gross,
        CommercialGoalCostQuality quality,
        bool available,
        bool estimated = false) =>
        new()
        {
            Competence = c,
            NetCommercialRevenue = revenue,
            Cogs = cogs,
            GrossProfit = gross,
            CostQuality = quality,
            ProfitIsEstimated = estimated,
            GrossProfitAvailable = available,
        };

    static TempDatabase Begin()
    {
        PdvService.TestBeforeInsertSaleItems = null;
        PdvService.TestAfterInsertSaleItems = null;
        PdvService.TestAfterSwapItemUpdate = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(100, "71b-b4");
        return db;
    }

    static void SetCost(int productId, double cost)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET cost_price = $c WHERE id = $id;";
        cmd.Parameters.AddWithValue("$c", cost);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    static int InsertLegacySale(int productId, double qty, double unitPrice, DateTime sessionDate)
    {
        var total = ProductPriceHelper.RoundPrice(qty * unitPrice);
        using var conn = DatabaseService.OpenConnection();
        using var ins = conn.CreateCommand();
        ins.CommandText = """
            INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
            VALUES ($d, $t, 'Dinheiro', 0, datetime('now','localtime'));
            SELECT last_insert_rowid();
            """;
        ins.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        ins.Parameters.AddWithValue("$t", total);
        var saleId = Convert.ToInt32(ins.ExecuteScalar());
        using var item = conn.CreateCommand();
        item.CommandText = """
            INSERT INTO sale_items (sale_id, product_id, product_name, quantity, unit_price, subtotal)
            VALUES ($s, $p, 'LEGADO 71B', $q, $u, $t);
            """;
        item.Parameters.AddWithValue("$s", saleId);
        item.Parameters.AddWithValue("$p", productId);
        item.Parameters.AddWithValue("$q", qty);
        item.Parameters.AddWithValue("$u", unitPrice);
        item.Parameters.AddWithValue("$t", total);
        item.ExecuteNonQuery();
        return saleId;
    }

    static void SetSessionDate(int saleId, DateTime day)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET session_date = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", saleId);
        cmd.ExecuteNonQuery();
    }

    static int LastSaleId()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(id) FROM sales;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void SetItemQuantityUnavailable(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sale_items SET quantity = 1e999 WHERE sale_id = $s;";
        cmd.Parameters.AddWithValue("$s", saleId);
        cmd.ExecuteNonQuery();
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
