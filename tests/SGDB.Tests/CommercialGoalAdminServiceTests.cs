using System.IO;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 71B-B6 — configuração da meta via B3. Banco TEMP; nunca deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CommercialGoalAdminServiceTests
{
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);
    static readonly DateOnly Sep15 = new(2026, 9, 15);

    static TempDatabase Begin(string role = "admin")
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole(role);
        return db;
    }

    [Fact]
    public void Parse_moeda_ptbr()
    {
        Assert.True(CommercialGoalAdminService.TryParseMoney("12.000,00", out var a, out _));
        Assert.Equal(12_000m, a);
        Assert.True(CommercialGoalAdminService.TryParseMoney("R$ 7.500,50", out var b, out _));
        Assert.Equal(7_500.50m, b);
        Assert.True(CommercialGoalAdminService.TryParseMoney("180,5", out var c, out _));
        Assert.Equal(180.50m, c);
        Assert.False(CommercialGoalAdminService.TryParseMoney("0", out _, out _));
        Assert.False(CommercialGoalAdminService.TryParseMoney("-12", out _, out _));
        Assert.False(CommercialGoalAdminService.TryParseMoney("", out _, out _));
        Assert.False(CommercialGoalAdminService.TryParseMoney("abc", out _, out _));
    }

    [Fact]
    public void Set_e_clear_default()
    {
        using var _ = Begin();
        var saved = CommercialGoalAdminService.TrySaveDefault(Sep2026, "12.000,00");
        Assert.True(saved.Succeeded);
        Assert.True(saved.Snapshot.HasDefault);
        Assert.Equal("12.000,00", saved.Snapshot.DefaultEditorText);

        var presented = CommercialGoalLoader.Load(Sep2026, Sep15);
        Assert.Equal(CommercialGoalSettingSource.Default, presented.GoalSource);
        Assert.Equal("R$ 12.000,00", presented.Goal.ValueText);

        var cleared = CommercialGoalAdminService.TryClearDefault(Sep2026, confirmed: true);
        Assert.True(cleared.Succeeded);
        Assert.False(cleared.Snapshot.HasDefault);
        var after = CommercialGoalLoader.Load(Sep2026, Sep15);
        Assert.Equal(CommercialGoalSettingSource.None, after.GoalSource);
        Assert.Equal(CommercialGoalPresentation.GoalNotConfigured, after.Goal.ValueText);
    }

    [Fact]
    public void Set_e_clear_override()
    {
        using var _ = Begin();
        var saved = CommercialGoalAdminService.TrySaveOverride(Sep2026, "18000");
        Assert.True(saved.Succeeded);
        Assert.True(saved.Snapshot.HasMonthlyOverride);

        var presented = CommercialGoalLoader.Load(Sep2026, Sep15);
        Assert.Equal(CommercialGoalSettingSource.MonthlyOverride, presented.GoalSource);
        Assert.Equal("R$ 18.000,00", presented.Goal.ValueText);

        var cleared = CommercialGoalAdminService.TryClearOverride(Sep2026, confirmed: true);
        Assert.True(cleared.Succeeded);
        Assert.False(cleared.Snapshot.HasMonthlyOverride);
        Assert.Equal(CommercialGoalSettingSource.None, CommercialGoalLoader.Load(Sep2026, Sep15).GoalSource);
    }

    [Fact]
    public void Override_vence_default_e_reload()
    {
        using var _ = Begin();
        Assert.True(CommercialGoalAdminService.TrySaveDefault(Sep2026, "12000").Succeeded);
        Assert.True(CommercialGoalAdminService.TrySaveOverride(Sep2026, "15.000,00").Succeeded);
        var presented = CommercialGoalLoader.Load(Sep2026, Sep15);
        Assert.Equal(CommercialGoalSettingSource.MonthlyOverride, presented.GoalSource);
        Assert.Equal("R$ 15.000,00", presented.Goal.ValueText);
        Assert.Equal(CommercialGoalPresentation.OriginOverride, presented.GoalOriginText);
    }

    [Fact]
    public void Vendedor_nao_salva()
    {
        using var _ = Begin("vendedor");
        var result = CommercialGoalAdminService.TrySaveDefault(Sep2026, "12000");
        Assert.False(result.Succeeded);
        Assert.Contains("permissão", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(CommercialGoalSettingsService.GetDefault().GoalAmount);
    }

    [Fact]
    public void Cliente_rede_nao_salva()
    {
        using var _ = Begin();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        try
        {
            var result = CommercialGoalAdminService.TrySaveOverride(Sep2026, "12000");
            Assert.False(result.Succeeded);
            Assert.False(CommercialGoalAdminService.StationAllowsWrite());
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Nao_seed_12000()
    {
        using var _ = Begin();
        var editor = CommercialGoalAdminService.LoadEditor(Sep2026);
        Assert.Equal("", editor.DefaultEditorText);
        Assert.Equal("", editor.MonthlyEditorText);
        var presented = CommercialGoalLoader.Load(Sep2026, Sep15);
        Assert.NotEqual("R$ 12.000,00", presented.Goal.ValueText);
        Assert.Equal(CommercialGoalPresentation.GoalNotConfigured, presented.Goal.ValueText);
    }

    [Fact]
    public void Loader_nao_tem_sql()
    {
        var src = File.ReadAllText(Find("src", "SGDB.App", "Services", "CommercialGoalLoader.cs"));
        Assert.DoesNotContain("SELECT", src, StringComparison.Ordinal);
        Assert.Contains("CommercialGoalComposerService.Load", src, StringComparison.Ordinal);
        Assert.Contains("CommercialGoalPresentation.Apply", src, StringComparison.Ordinal);
    }

    static string Find(params string[] relative)
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
