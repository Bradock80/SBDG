using System.Globalization;
using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70F-B3B — persistência da margem bruta mínima global. Sem UI, grupo, produto ou default.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryCommercialMarginSettingsServiceTests
{
    static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        return db;
    }

    static int CountPolicyRows()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM app_settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", InventoryCommercialMarginSettingsService.SettingKey);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static string? ReadRaw()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = $key LIMIT 1;";
        cmd.Parameters.AddWithValue("$key", InventoryCommercialMarginSettingsService.SettingKey);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    static void PersistRaw(string value)
    {
        AppSettingsService.SetSetting(InventoryCommercialMarginSettingsService.SettingKey, value);
    }

    [Fact]
    public void QueryCount_de_load_e_um() =>
        Assert.Equal(1, InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount);

    [Fact]
    public void Chave_centralizada() =>
        Assert.Equal("inventory_min_gross_margin_percent", InventoryCommercialMarginSettingsService.SettingKey);

    [Fact]
    public void Chave_ausente_e_Missing()
    {
        using var db = Begin();
        var setting = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(InventoryCommercialMarginSettingStatus.Missing, setting.Status);
        Assert.Null(setting.MinimumGrossMarginPercent);
        Assert.Null(setting.RawValue);
        Assert.Equal(InventoryCommercialMarginSettingReason.Missing, Assert.Single(setting.Reasons));
        Assert.Equal(1, setting.QueryCount);
    }

    [Fact]
    public void Chave_ausente_nao_grava_banco()
    {
        using var db = Begin();
        Assert.Equal(0, CountPolicyRows());
        var before = CountAllSettings();
        _ = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(0, CountPolicyRows());
        Assert.Equal(before, CountAllSettings());
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("15", 15)]
    [InlineData("15.5", 15.5)]
    [InlineData("12.75", 12.75)]
    [InlineData("99.99", 99.99)]
    public void Valor_invariante_configurado(string raw, double expected)
    {
        using var db = Begin();
        PersistRaw(raw);
        var setting = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(InventoryCommercialMarginSettingStatus.Configured, setting.Status);
        Assert.Equal((decimal)expected, setting.MinimumGrossMarginPercent);
        Assert.Equal(raw, setting.RawValue);
        Assert.Empty(setting.Reasons);
    }

    [Theory]
    [InlineData("100")]
    [InlineData("-1")]
    [InlineData("150")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("15,5")]
    [InlineData("15.5.5")]
    [InlineData("1e1")]
    [InlineData("+15")]
    [InlineData(" 15.5")]
    [InlineData("15.5 ")]
    public void Valor_persistido_invalido(string raw)
    {
        using var db = Begin();
        PersistRaw(raw);
        var setting = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(InventoryCommercialMarginSettingStatus.Invalid, setting.Status);
        Assert.Null(setting.MinimumGrossMarginPercent);
        Assert.Equal(raw, setting.RawValue);
        Assert.Contains(InventoryCommercialMarginSettingReason.Invalid, setting.Reasons);
        Assert.Equal(raw, ReadRaw());
    }

    [Fact]
    public void Save_zero_valido_nao_e_ausencia()
    {
        using var db = Begin();
        var saved = InventoryCommercialMarginSettingsService.Save(0m);
        Assert.True(saved.Written);
        Assert.Equal(0m, saved.Setting.MinimumGrossMarginPercent);
        var loaded = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(InventoryCommercialMarginSettingStatus.Configured, loaded.Status);
        Assert.Equal(0m, loaded.MinimumGrossMarginPercent);
        Assert.Equal("0", loaded.RawValue);
        Assert.NotEqual(InventoryCommercialMarginSettingStatus.Missing, loaded.Status);
    }

    [Fact]
    public void Save_15_5_e_99_99()
    {
        using var db = Begin();
        Assert.True(InventoryCommercialMarginSettingsService.Save(15.5m).Written);
        Assert.Equal("15.5", ReadRaw());
        Assert.True(InventoryCommercialMarginSettingsService.Save(99.99m).Written);
        Assert.Equal("99.99", ReadRaw());
        Assert.Equal(99.99m, InventoryCommercialMarginSettingsService.Load().MinimumGrossMarginPercent);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(100.01)]
    [InlineData(150)]
    public void Save_fora_do_intervalo_recusado(double value)
    {
        using var db = Begin();
        var result = InventoryCommercialMarginSettingsService.Save((decimal)value);
        Assert.False(result.Written);
        Assert.Equal(InventoryCommercialMarginSettingStatus.Invalid, result.Setting.Status);
        Assert.Equal(0, CountPolicyRows());
        Assert.Equal(InventoryCommercialMarginSettingStatus.Missing, InventoryCommercialMarginSettingsService.Load().Status);
    }

    [Fact]
    public void Save_nao_escreve_sobre_valor_ja_valido()
    {
        using var db = Begin();
        Assert.True(InventoryCommercialMarginSettingsService.Save(20m).Written);
        var failed = InventoryCommercialMarginSettingsService.Save(100m);
        Assert.False(failed.Written);
        Assert.Equal("20", ReadRaw());
        Assert.Equal(20m, InventoryCommercialMarginSettingsService.Load().MinimumGrossMarginPercent);
    }

    [Fact]
    public void Save_NaN_e_Infinity_recusados()
    {
        using var db = Begin();
        Assert.False(InventoryCommercialMarginSettingsService.Save(double.NaN).Written);
        Assert.False(InventoryCommercialMarginSettingsService.Save(double.PositiveInfinity).Written);
        Assert.False(InventoryCommercialMarginSettingsService.Save(double.NegativeInfinity).Written);
        Assert.Equal(0, CountPolicyRows());
    }

    [Fact]
    public void Save_usa_invariant_mesmo_em_ptBR_e_enUS()
    {
        using var db = Begin();
        AssertSaveInvariant("pt-BR");
        Assert.Equal("15.5", ReadRaw());
        AssertSaveInvariant("en-US");
        Assert.Equal("15.5", ReadRaw());
        AssertSaveInvariant("pt-BR");
        var loaded = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(15.5m, loaded.MinimumGrossMarginPercent);
        Assert.Equal("15.5", loaded.RawValue);
        Assert.NotNull(loaded.RawValue);
        Assert.DoesNotContain(',', loaded.RawValue);
    }

    [Fact]
    public void Clear_remove_e_Load_volta_Missing()
    {
        using var db = Begin();
        Assert.True(InventoryCommercialMarginSettingsService.Save(12.75m).Written);
        Assert.Equal(1, CountPolicyRows());
        var cleared = InventoryCommercialMarginSettingsService.Clear();
        Assert.Equal(InventoryCommercialMarginSettingStatus.Missing, cleared.Status);
        Assert.Equal(0, CountPolicyRows());
        var loaded = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(InventoryCommercialMarginSettingStatus.Missing, loaded.Status);
        Assert.Null(loaded.MinimumGrossMarginPercent);
    }

    [Fact]
    public void Clear_inexistente_e_idempotente()
    {
        using var db = Begin();
        InventoryCommercialMarginSettingsService.Clear();
        InventoryCommercialMarginSettingsService.Clear();
        Assert.Equal(0, CountPolicyRows());
        Assert.Equal(InventoryCommercialMarginSettingStatus.Missing, InventoryCommercialMarginSettingsService.Load().Status);
    }

    [Fact]
    public void Save_sobrescreve_mesma_chave_sem_duplicar()
    {
        using var db = Begin();
        InventoryCommercialMarginSettingsService.Save(10m);
        InventoryCommercialMarginSettingsService.Save(12.75m);
        Assert.Equal(1, CountPolicyRows());
        Assert.Equal("12.75", ReadRaw());
    }

    [Fact]
    public void Precisao_de_quatro_casas()
    {
        using var db = Begin();
        Assert.True(InventoryCommercialMarginSettingsService.Save(12.34567m).Written);
        var loaded = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(12.3457m, loaded.MinimumGrossMarginPercent);
        Assert.Equal("12.3457", loaded.RawValue);
    }

    [Fact]
    public void Leitura_repetida_e_deterministica()
    {
        using var db = Begin();
        InventoryCommercialMarginSettingsService.Save(8.25m);
        var a = InventoryCommercialMarginSettingsService.Load();
        var b = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(a.Status, b.Status);
        Assert.Equal(a.MinimumGrossMarginPercent, b.MinimumGrossMarginPercent);
        Assert.Equal(a.RawValue, b.RawValue);
        Assert.Equal(1, a.QueryCount);
        Assert.Equal(1, b.QueryCount);
    }

    [Fact]
    public void Budget_futuro_e_9()
    {
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialEligibilityEngine.ExpectedQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialPriceFloorEngine.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount);
    }

    [Fact]
    public void Fonte_nao_le_lucro_nem_benchmark_nem_B3()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialMarginSettingsService.cs"));
        var model = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Models", "InventoryCommercialMarginSetting.cs"));
        foreach (var text in new[] { source, model })
        {
            Assert.DoesNotContain("lucro_percent", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_compra", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_promocional", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Mínimo mercado", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Meta ideal", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PriceFloorEngine", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PriceFloorEngine.Evaluate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("const decimal Default", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 15", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 18", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 22", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 30", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group_name", source, StringComparison.Ordinal);
        Assert.DoesNotContain("product_id", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void B1_B2_B3_nao_referenciam_settings()
    {
        var b1 = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialEligibilityEngine.cs"));
        var b2 = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialFactsEngine.cs"));
        var b2s = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialFactsService.cs"));
        var b3 = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialPriceFloorEngine.cs"));
        foreach (var text in new[] { b1, b2, b2s, b3 })
        {
            Assert.DoesNotContain("MarginSettings", text, StringComparison.Ordinal);
            Assert.DoesNotContain(InventoryCommercialMarginSettingsService.SettingKey, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("app_settings", b3, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetSetting", b3, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseService", b3, StringComparison.Ordinal);
    }

    [Fact]
    public void Apenas_uma_chave_de_politica()
    {
        using var db = Begin();
        InventoryCommercialMarginSettingsService.Save(11.11m);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM app_settings
            WHERE key LIKE '%margin%' OR key LIKE '%margem%' OR key LIKE '%lucro%';
            """;
        Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        Assert.Equal(InventoryCommercialMarginSettingsService.SettingKey, Assert.Single(ListPolicyKeys()));
    }

    static int CountAllSettings()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM app_settings;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static IReadOnlyList<string> ListPolicyKeys()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key FROM app_settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", InventoryCommercialMarginSettingsService.SettingKey);
        var keys = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            keys.Add(reader.GetString(0));
        return keys;
    }

    static void AssertSaveInvariant(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var result = InventoryCommercialMarginSettingsService.Save(15.5m);
            Assert.True(result.Written);
            Assert.Equal("15.5", result.Setting.RawValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    static string FindSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
