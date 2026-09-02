using System.Globalization;
using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70F-B3D — configuração operacional da margem mínima global.
/// Bancos isolados. Sem deposito.db. Sem RPC.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryCommercialMarginAdminServiceTests
{
    static TempDatabase Begin(string role = "admin", string network = StoreNetworkMode.RoleStandalone)
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(network);
        TestDataHelper.SetSessionRole(role);
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

    static string? ReadRaw() =>
        AppSettingsService.GetSetting(InventoryCommercialMarginSettingsService.SettingKey);

    static int CountAudit()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_log WHERE entity = $e;";
        cmd.Parameters.AddWithValue("$e", InventoryCommercialMarginAdminService.AuditEntity);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static string? LastAuditDetails()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT details FROM audit_log
            WHERE entity = $e
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$e", InventoryCommercialMarginAdminService.AuditEntity);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    [Fact]
    public void Admin_autorizado()
    {
        using var db = Begin("admin");
        Assert.True(AccessControl.CanAccessCommercialPolicy());
        Assert.True(AccessControl.CanAccessModule(InventoryCommercialMarginAdminService.ModuleId));
        Assert.True(InventoryCommercialMarginAdminService.CanMutate());
    }

    [Fact]
    public void Gestor_autorizado()
    {
        using var db = Begin("gestor");
        Assert.True(AccessControl.CanAccessCommercialPolicy());
        Assert.True(AccessControl.CanAccessModule(InventoryCommercialMarginAdminService.ModuleId));
        Assert.True(InventoryCommercialMarginAdminService.CanMutate());
    }

    [Fact]
    public void Vendedor_bloqueado()
    {
        using var db = Begin("vendedor");
        Assert.False(AccessControl.CanAccessCommercialPolicy());
        Assert.False(AccessControl.CanAccessModule(InventoryCommercialMarginAdminService.ModuleId));
        Assert.False(InventoryCommercialMarginAdminService.CanMutate());
        var result = InventoryCommercialMarginAdminService.TrySave("15");
        Assert.False(result.Succeeded);
        Assert.False(result.Audited);
        Assert.Equal(0, CountPolicyRows());
        Assert.Equal(0, CountAudit());
    }

    [Fact]
    public void Vendedor_com_RelatoriosAcesso_continua_bloqueado()
    {
        using var db = Begin("vendedor");
        TestDataHelper.SetSessionCustomPermissions("vendedor", p => p.RelatoriosAcesso = true);
        Assert.False(AccessControl.CanAccessCommercialPolicy());
        Assert.False(InventoryCommercialMarginAdminService.TrySave("15").Succeeded);
        Assert.Equal(0, CountPolicyRows());
    }

    [Fact]
    public void Cliente_RedeLoja_bloqueado_Save_e_Clear()
    {
        using var db = Begin("admin", StoreNetworkMode.RoleClient);
        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient(InventoryCommercialMarginAdminService.ModuleId));
        Assert.Equal(
            InventoryCommercialMarginAdminService.ClientBlockedMessage,
            StoreNetworkMode.BlockedModuleMessage(InventoryCommercialMarginAdminService.ModuleId));
        Assert.False(InventoryCommercialMarginAdminService.StationAllowsWrite());

        var save = InventoryCommercialMarginAdminService.TrySave("15,5");
        Assert.False(save.Succeeded);
        Assert.False(save.Audited);
        Assert.Equal(0, CountPolicyRows());

        InventoryCommercialMarginSettingsService.Save(10m);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        var clear = InventoryCommercialMarginAdminService.TryClear(true);
        Assert.False(clear.Succeeded);
        Assert.Equal("10", ReadRaw());
        Assert.Equal(0, CountAudit());
    }

    [Fact]
    public void Standalone_e_servidor_permitidos()
    {
        using var db = Begin("admin", StoreNetworkMode.RoleStandalone);
        Assert.True(InventoryCommercialMarginAdminService.TrySave("12,75").Succeeded);
        Assert.Equal("12.75", ReadRaw());

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleServer);
        Assert.True(InventoryCommercialMarginAdminService.StationAllowsWrite());
        Assert.True(InventoryCommercialMarginAdminService.TrySave("20").Succeeded);
        Assert.Equal("20", ReadRaw());
    }

    [Fact]
    public void Missing_Configured_Invalid_e_zero()
    {
        using var db = Begin();
        var missing = InventoryCommercialMarginAdminService.LoadSnapshot();
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Missing, missing.Status);
        Assert.Equal("Margem mínima não configurada.", missing.StatusText);
        Assert.Equal("", missing.EditorText);

        Assert.True(InventoryCommercialMarginAdminService.TrySave("0").Succeeded);
        var zero = InventoryCommercialMarginAdminService.LoadSnapshot();
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Available, zero.Status);
        Assert.Equal(0m, zero.EffectivePercent);
        Assert.Contains("0%", zero.StatusText, StringComparison.Ordinal);
        Assert.NotEqual(InventoryCommercialMarginPolicyResolutionStatus.Missing, zero.Status);

        Assert.True(InventoryCommercialMarginAdminService.TrySave("15,5").Succeeded);
        var configured = InventoryCommercialMarginAdminService.LoadSnapshot();
        Assert.Equal("Política vigente: 15,5%", configured.StatusText);
        Assert.Equal("15,5", configured.EditorText);

        AppSettingsService.SetSetting(InventoryCommercialMarginSettingsService.SettingKey, "15,5");
        var invalid = InventoryCommercialMarginAdminService.LoadSnapshot();
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Invalid, invalid.Status);
        Assert.Equal("A configuração armazenada é inválida.", invalid.StatusText);
        Assert.Equal("15,5", invalid.EditorText);
        Assert.Null(invalid.EffectivePercent);
    }

    [Theory]
    [InlineData("15", "15")]
    [InlineData("15,5", "15.5")]
    [InlineData("12,75", "12.75")]
    [InlineData("99,99", "99.99")]
    public void Input_ptBR_grava_invariante(string input, string expectedRaw)
    {
        using var db = Begin();
        var result = InventoryCommercialMarginAdminService.TrySave(input);
        Assert.True(result.Succeeded);
        Assert.Equal(expectedRaw, ReadRaw());
        Assert.Equal(InventoryCommercialMarginSettingStatus.Configured,
            InventoryCommercialMarginSettingsService.Load().Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-1")]
    [InlineData("100")]
    [InlineData("150")]
    [InlineData("abc")]
    public void Input_invalido_nao_grava_nem_audita(string? input)
    {
        using var db = Begin();
        var result = InventoryCommercialMarginAdminService.TrySave(input);
        Assert.False(result.Succeeded);
        Assert.False(result.Audited);
        Assert.Equal(0, CountPolicyRows());
        Assert.Equal(0, CountAudit());
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Missing, result.Snapshot.Status);
    }

    [Fact]
    public void Vazio_nao_vira_zero()
    {
        using var db = Begin();
        Assert.False(InventoryCommercialMarginAdminService.TryParsePercent("", out _, out _));
        Assert.False(InventoryCommercialMarginAdminService.TryParsePercent("   ", out _, out _));
        Assert.True(InventoryCommercialMarginAdminService.TryParsePercent("0", out var zero, out _));
        Assert.Equal(0m, zero);
    }

    [Fact]
    public void Clear_usa_B3B_e_nao_grava_vazio()
    {
        using var db = Begin();
        Assert.True(InventoryCommercialMarginAdminService.TrySave("15").Succeeded);
        var cleared = InventoryCommercialMarginAdminService.TryClear(true);
        Assert.True(cleared.Succeeded);
        Assert.Equal(0, CountPolicyRows());
        Assert.Null(ReadRaw());
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Missing, cleared.Snapshot.Status);
    }

    [Fact]
    public void Cancelamento_Clear_nao_altera()
    {
        using var db = Begin();
        Assert.True(InventoryCommercialMarginAdminService.TrySave("18").Succeeded);
        var audits = CountAudit();
        var cancelled = InventoryCommercialMarginAdminService.TryClear(false);
        Assert.False(cancelled.Succeeded);
        Assert.False(cancelled.Audited);
        Assert.Equal("18", ReadRaw());
        Assert.Equal(audits, CountAudit());
    }

    [Fact]
    public void Save_e_Clear_registram_audit_de_para()
    {
        using var db = Begin();
        Assert.True(InventoryCommercialMarginAdminService.TrySave("15,5").Succeeded);
        Assert.Equal(1, CountAudit());
        var first = LastAuditDetails();
        Assert.Contains("\"operation\":\"salvar\"", first, StringComparison.Ordinal);
        Assert.Contains("\"previous_status\":\"Missing\"", first, StringComparison.Ordinal);
        Assert.DoesNotContain("\"previous_percent\":0", first, StringComparison.Ordinal);
        Assert.Contains("\"new_percent\":15.5", first, StringComparison.Ordinal);
        Assert.Contains("\"origem\":\"sistema.politica_comercial\"", first, StringComparison.Ordinal);

        Assert.True(InventoryCommercialMarginAdminService.TrySave("20").Succeeded);
        var second = LastAuditDetails();
        Assert.Contains("\"previous_percent\":15.5", second, StringComparison.Ordinal);
        Assert.Contains("\"new_percent\":20", second, StringComparison.Ordinal);

        Assert.True(InventoryCommercialMarginAdminService.TryClear(true).Succeeded);
        var removed = LastAuditDetails();
        Assert.Contains("\"operation\":\"remover\"", removed, StringComparison.Ordinal);
        Assert.Contains("\"previous_percent\":20", removed, StringComparison.Ordinal);
        Assert.DoesNotContain("\"new_percent\":", removed, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_anterior_nao_vira_zero_no_audit()
    {
        using var db = Begin();
        AppSettingsService.SetSetting(InventoryCommercialMarginSettingsService.SettingKey, "15,5");
        Assert.True(InventoryCommercialMarginAdminService.TrySave("10").Succeeded);
        var details = LastAuditDetails();
        Assert.Contains("\"previous_status\":\"Invalid\"", details, StringComparison.Ordinal);
        Assert.Contains("\"previous_raw\":\"15,5\"", details, StringComparison.Ordinal);
        Assert.DoesNotContain("\"previous_percent\":0", details, StringComparison.Ordinal);
        Assert.Contains("\"new_percent\":10", details, StringComparison.Ordinal);
    }

    [Fact]
    public void Cultura_nao_muda_persistencia()
    {
        using var db = Begin();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.True(InventoryCommercialMarginAdminService.TrySave("15,5").Succeeded);
            Assert.Equal("15.5", ReadRaw());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Fonte_UI_nao_escreve_SQL_nem_inventa_default()
    {
        var view = File.ReadAllText(FindSource("src", "SGDB.App", "Views", "CommercialPolicyModuleView.xaml.cs"));
        var xaml = File.ReadAllText(FindSource("src", "SGDB.App", "Views", "CommercialPolicyModuleView.xaml"));
        var admin = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "InventoryCommercialMarginAdminService.cs"));
        foreach (var text in new[] { view, xaml, admin })
        {
            Assert.DoesNotContain("INSERT INTO app_settings", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lucro_percent", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_promocional", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("desconto recomendado", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promoção automática", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Mínimo mercado", text, StringComparison.Ordinal);
        }

        Assert.Contains("InventoryCommercialMarginAdminService.TrySave", view, StringComparison.Ordinal);
        Assert.Contains("InventoryCommercialMarginAdminService.TryClear", view, StringComparison.Ordinal);
        Assert.Contains("InventoryCommercialMarginSettingsService.Save", admin, StringComparison.Ordinal);
        Assert.Contains("InventoryCommercialMarginSettingsService.Clear", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("= 15", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("= 30", admin, StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_tem_titulo_explicacao_e_acoes()
    {
        var xaml = File.ReadAllText(FindSource("src", "SGDB.App", "Views", "CommercialPolicyModuleView.xaml"));
        Assert.Contains("Política comercial", xaml, StringComparison.Ordinal);
        Assert.Contains("Margem mínima é o limite inferior usado pelas análises", xaml, StringComparison.Ordinal);
        Assert.Contains("não altera preços automaticamente", xaml, StringComparison.Ordinal);
        Assert.Contains("Salvar (F9)", xaml, StringComparison.Ordinal);
        Assert.Contains("Remover configuração", xaml, StringComparison.Ordinal);
        Assert.Contains("PercentBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Placeholder=", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Menu_e_protocolo_RedeLoja_intactos()
    {
        var menu = File.ReadAllText(FindSource("src", "SGDB.App", "MainWindow.xaml"));
        var main = File.ReadAllText(FindSource("src", "SGDB.App", "MainWindow.xaml.cs"));
        var host = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "StoreNetworkHost.cs"));
        Assert.Contains("Tag=\"politica_comercial\"", menu, StringComparison.Ordinal);
        Assert.Contains("CommercialPolicyModuleView", main, StringComparison.Ordinal);
        Assert.DoesNotContain("politica_comercial", host, StringComparison.Ordinal);
        Assert.DoesNotContain("inventory_min_gross_margin_percent", host, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/commercial", host, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void B3B_B3C_B3_permanecem_separados()
    {
        var b3 = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "InventoryCommercialPriceFloorEngine.cs"));
        var b3c = File.ReadAllText(FindSource("src", "SGDB.App", "Services", "InventoryCommercialMarginPolicyResolver.cs"));
        Assert.DoesNotContain("AdminService", b3, StringComparison.Ordinal);
        Assert.DoesNotContain("AdminService", b3c, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsService", b3, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsService", b3c, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", b3c, StringComparison.Ordinal);
    }

    [Fact]
    public void Budget_estoque_inteligente_continua_9()
    {
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialEligibilityEngine.ExpectedQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialPriceFloorEngine.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryCommercialMarginPolicyResolver.ExpectedQueryCount);
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
