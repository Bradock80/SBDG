using System.Globalization;
using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Domain.Common;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 71B-B3 — persistência default + override da meta. Banco TEMP; nunca deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CommercialGoalSettingsServiceTests
{
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly CommercialCompetence Oct2026 = CommercialCompetence.Create(2026, 10);
    static readonly CommercialCompetence Jan2026 = CommercialCompetence.Create(2026, 1);
    static readonly CommercialCompetence Dec2025 = CommercialCompetence.Create(2025, 12);

    static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        return db;
    }

    [Fact]
    public void Chaves_centralizadas_na_competencia()
    {
        Assert.Equal("negocio_meta_lucro_bruto_default", CommercialGoalSettingKeys.Default);
        Assert.Equal("negocio_meta_lucro_bruto_2026-09", CommercialGoalSettingKeys.Monthly(Sep2026));
        Assert.Equal("negocio_meta_lucro_bruto_2026-10", CommercialGoalSettingsService.MonthlyKey(Oct2026));
        Assert.Equal("negocio_meta_lucro_bruto_2026-01", CommercialGoalSettingsService.MonthlyKey(Jan2026));
        Assert.Equal("negocio_meta_lucro_bruto_2025-12", CommercialGoalSettingsService.MonthlyKey(Dec2025));
        Assert.Equal(1, CommercialGoalSettingsService.ExpectedSingleKeyQueryCount);
        Assert.Equal(2, CommercialGoalSettingsService.ExpectedResolveMaxQueryCount);
        Assert.Contains("override", CommercialGoalSettingsSemantics.HistoricalDefaultLimitation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_ausente()
    {
        using var _ = Begin();
        var setting = CommercialGoalSettingsService.GetDefault();
        Assert.Equal(CommercialGoalStoredSettingStatus.Missing, setting.Status);
        Assert.Null(setting.GoalAmount);
        Assert.Equal(1, setting.QueryCount);
    }

    [Fact]
    public void Default_valido()
    {
        using var _ = Begin();
        Assert.True(CommercialGoalSettingsService.SetDefault(12_000m).Written);
        var setting = CommercialGoalSettingsService.GetDefault();
        Assert.Equal(CommercialGoalStoredSettingStatus.Configured, setting.Status);
        Assert.Equal(12_000.00m, setting.GoalAmount);
        Assert.Equal("12000.00", setting.RawValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-12_000)]
    public void Default_escrita_rejeita_nao_positiva(decimal value)
    {
        using var _ = Begin();
        Assert.False(CommercialGoalSettingsService.SetDefault(value).Written);
        Assert.Equal(CommercialGoalStoredSettingStatus.Missing, CommercialGoalSettingsService.GetDefault().Status);
        Assert.Null(ReadRaw(CommercialGoalSettingKeys.Default));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-12")]
    [InlineData("abc")]
    [InlineData("12.000,50")]
    [InlineData("12000.005")]
    [InlineData("")]
    public void Default_leitura_invalida(string raw)
    {
        using var _ = Begin();
        AppSettingsService.SetSetting(CommercialGoalSettingKeys.Default, raw);
        var setting = CommercialGoalSettingsService.GetDefault();
        Assert.Equal(CommercialGoalStoredSettingStatus.Invalid, setting.Status);
        Assert.Null(setting.GoalAmount);
        Assert.Equal(raw, setting.RawValue);
    }

    [Fact]
    public void Override_ausente_e_valido()
    {
        using var _ = Begin();
        Assert.Equal(CommercialGoalStoredSettingStatus.Missing,
            CommercialGoalSettingsService.GetMonthlyOverride(Sep2026).Status);
        Assert.True(CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 15_000.5m).Written);
        var setting = CommercialGoalSettingsService.GetMonthlyOverride(Sep2026);
        Assert.Equal(CommercialGoalStoredSettingStatus.Configured, setting.Status);
        Assert.Equal(15_000.50m, setting.GoalAmount);
        Assert.Equal("15000.50", setting.RawValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Override_escrita_rejeita_nao_positiva(decimal value)
    {
        using var _ = Begin();
        Assert.False(CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, value).Written);
        Assert.Null(ReadRaw(CommercialGoalSettingKeys.Monthly(Sep2026)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("xyz")]
    [InlineData("15,5")]
    public void Override_leitura_invalida(string raw)
    {
        using var _ = Begin();
        AppSettingsService.SetSetting(CommercialGoalSettingKeys.Monthly(Sep2026), raw);
        var setting = CommercialGoalSettingsService.GetMonthlyOverride(Sep2026);
        Assert.Equal(CommercialGoalStoredSettingStatus.Invalid, setting.Status);
        Assert.Null(setting.GoalAmount);
    }

    [Fact]
    public void Resolve_override_vence_default()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetDefault(12_000m);
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 18_000m);
        var resolution = CommercialGoalSettingsService.Resolve(Sep2026);
        Assert.Equal(CommercialGoalSettingSource.MonthlyOverride, resolution.Source);
        Assert.Equal(18_000m, resolution.GoalAmount);
        Assert.True(resolution.HasValidGoal);
        Assert.Equal(1, resolution.QueryCount);
        Assert.Null(resolution.DefaultSetting);
    }

    [Fact]
    public void Resolve_sem_override_usa_default()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetDefault(12_000m);
        var resolution = CommercialGoalSettingsService.Resolve(Sep2026);
        Assert.Equal(CommercialGoalSettingSource.Default, resolution.Source);
        Assert.Equal(12_000m, resolution.GoalAmount);
        Assert.True(resolution.HasValidGoal);
        Assert.Equal(2, resolution.QueryCount);
        Assert.Equal(CommercialGoalStoredSettingStatus.Missing, resolution.MonthlyOverride.Status);
    }

    [Fact]
    public void Resolve_ambos_ausentes_SemMeta()
    {
        using var _ = Begin();
        var resolution = CommercialGoalSettingsService.Resolve(Sep2026);
        Assert.Equal(CommercialGoalSettingSource.None, resolution.Source);
        Assert.Null(resolution.GoalAmount);
        Assert.False(resolution.HasValidGoal);
        Assert.Equal(2, resolution.QueryCount);
    }

    [Fact]
    public void Resolve_override_invalido_nao_cai_para_default()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetDefault(12_000m);
        AppSettingsService.SetSetting(CommercialGoalSettingKeys.Monthly(Sep2026), "abc");
        var resolution = CommercialGoalSettingsService.Resolve(Sep2026);
        Assert.Equal(CommercialGoalSettingSource.InvalidMonthlyOverride, resolution.Source);
        Assert.Null(resolution.GoalAmount);
        Assert.False(resolution.HasValidGoal);
        Assert.Equal(1, resolution.QueryCount);
        Assert.Null(resolution.DefaultSetting);
    }

    [Fact]
    public void Resolve_default_invalido()
    {
        using var _ = Begin();
        AppSettingsService.SetSetting(CommercialGoalSettingKeys.Default, "12,000.00");
        var resolution = CommercialGoalSettingsService.Resolve(Sep2026);
        Assert.Equal(CommercialGoalSettingSource.InvalidDefault, resolution.Source);
        Assert.Null(resolution.GoalAmount);
        Assert.False(resolution.HasValidGoal);
        Assert.Equal(2, resolution.QueryCount);
    }

    [Fact]
    public void ClearDefault_e_ClearMonthly()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetDefault(9_000m);
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 10_000m);
        var clearedDefault = CommercialGoalSettingsService.ClearDefault();
        Assert.Equal(CommercialGoalStoredSettingStatus.Missing, clearedDefault.Status);
        Assert.Null(ReadRaw(CommercialGoalSettingKeys.Default));

        var clearedMonthly = CommercialGoalSettingsService.ClearMonthlyOverride(Sep2026);
        Assert.Equal(CommercialGoalStoredSettingStatus.Missing, clearedMonthly.Status);
        Assert.Null(ReadRaw(CommercialGoalSettingKeys.Monthly(Sep2026)));
    }

    [Fact]
    public void Arredondamento_AwayFromZero_duas_casas()
    {
        using var _ = Begin();
        Assert.Equal(1.01m, MonetaryRounding.RoundDecimal(1.005m));
        Assert.True(CommercialGoalSettingsService.SetDefault(12_000.005m).Written);
        Assert.Equal(12_000.01m, CommercialGoalSettingsService.GetDefault().GoalAmount);
        Assert.Equal("12000.01", ReadRaw(CommercialGoalSettingKeys.Default));
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Persistencia_culture_invariant(string cultureName)
    {
        using var _ = Begin();
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Assert.True(CommercialGoalSettingsService.SetDefault(12_000.5m).Written);
            Assert.Equal("12000.50", ReadRaw(CommercialGoalSettingKeys.Default));
            Assert.Equal(12_000.50m, CommercialGoalSettingsService.GetDefault().GoalAmount);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void Historico_override_nao_muda_com_default()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetDefault(12_000m);
        CommercialGoalSettingsService.SetMonthlyOverride(Sep2026, 18_000m);
        CommercialGoalSettingsService.SetDefault(20_000m);

        var sep = CommercialGoalSettingsService.Resolve(Sep2026);
        Assert.Equal(18_000m, sep.GoalAmount);
        Assert.Equal(CommercialGoalSettingSource.MonthlyOverride, sep.Source);

        var oct = CommercialGoalSettingsService.Resolve(Oct2026);
        Assert.Equal(20_000m, oct.GoalAmount);
        Assert.Equal(CommercialGoalSettingSource.Default, oct.Source);
        Assert.Contains("default vigente", oct.HistoricalDefaultLimitation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Competencias_isoladas_por_chave()
    {
        using var _ = Begin();
        CommercialGoalSettingsService.SetMonthlyOverride(Jan2026, 1_000m);
        CommercialGoalSettingsService.SetMonthlyOverride(Dec2025, 2_000m);
        Assert.Equal(1_000m, CommercialGoalSettingsService.Resolve(Jan2026).GoalAmount);
        Assert.Equal(2_000m, CommercialGoalSettingsService.Resolve(Dec2025).GoalAmount);
        Assert.Equal(CommercialGoalSettingSource.None, CommercialGoalSettingsService.Resolve(Sep2026).Source);
    }

    [Fact]
    public void Servico_nao_abre_schema_nem_rpc()
    {
        var src = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "CommercialGoalSettingsService.cs"));
        Assert.DoesNotContain("CREATE TABLE", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("migration", src, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StoreNetworkClient", src, StringComparison.Ordinal);
        Assert.DoesNotContain("Rpc", src, StringComparison.Ordinal);
        Assert.Contains("AppSettingsService", src, StringComparison.Ordinal);
    }

    [Fact]
    public void AppSettings_DeleteSetting_continua_funcionando()
    {
        using var _ = Begin();
        AppSettingsService.SetSetting("tmp_71b_b3", "x");
        Assert.Equal("x", AppSettingsService.GetSetting("tmp_71b_b3"));
        AppSettingsService.DeleteSetting("tmp_71b_b3");
        Assert.Null(AppSettingsService.GetSetting("tmp_71b_b3"));
    }

    static string? ReadRaw(string key) => AppSettingsService.GetSetting(key);

    static string FindSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}
