using System.Globalization;
using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B3C — resolver puro da margem mínima global. Sem SQL, UI, grupo ou produto.
/// </summary>
public class InventoryCommercialMarginPolicyResolverTests
{
    static InventoryCommercialMarginSetting Configured(decimal percent, string? raw = null) =>
        new()
        {
            Status = InventoryCommercialMarginSettingStatus.Configured,
            MinimumGrossMarginPercent = percent,
            RawValue = raw ?? percent.ToString(CultureInfo.InvariantCulture),
        };

    static InventoryCommercialMarginSetting MissingSetting() =>
        new()
        {
            Status = InventoryCommercialMarginSettingStatus.Missing,
            Reasons = [InventoryCommercialMarginSettingReason.Missing],
        };

    static InventoryCommercialMarginSetting InvalidSetting(
        InventoryCommercialMarginSettingReason reason = InventoryCommercialMarginSettingReason.Invalid,
        string raw = "100") =>
        new()
        {
            Status = InventoryCommercialMarginSettingStatus.Invalid,
            RawValue = raw,
            Reasons = [reason, InventoryCommercialMarginSettingReason.Invalid],
        };

    static InventoryCommercialFacts Facts(double sale = 20, double cost = 10, int id = 1) =>
        InventoryCommercialFactsEngine.Classify(new InventoryCommercialFactsInput
        {
            ProductId = id,
            ProductFound = true,
            CatalogSalePrice = sale,
            CurrentAverageCost = cost,
            AllowsSale = true,
        });

    [Fact]
    public void QueryCount_e_zero() =>
        Assert.Equal(0, InventoryCommercialMarginPolicyResolver.ExpectedQueryCount);

    [Theory]
    [InlineData("0")]
    [InlineData("15")]
    [InlineData("12.75")]
    [InlineData("15.5")]
    [InlineData("99.99")]
    public void Configured_vira_Available_Global(string raw)
    {
        var percent = decimal.Parse(raw, CultureInfo.InvariantCulture);
        var setting = Configured(percent, raw);
        var resolution = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Available, resolution.Status);
        Assert.Equal(InventoryCommercialMarginPolicySource.Global, resolution.Source);
        Assert.Equal(percent, resolution.EffectiveMinimumGrossMarginPercent);
        Assert.Empty(resolution.Reasons);
    }

    [Fact]
    public void Missing_permanece_Missing_sem_numero()
    {
        var resolution = InventoryCommercialMarginPolicyResolver.Resolve(MissingSetting());
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Missing, resolution.Status);
        Assert.Equal(InventoryCommercialMarginPolicySource.None, resolution.Source);
        Assert.Null(resolution.EffectiveMinimumGrossMarginPercent);
        Assert.NotEqual(0m, resolution.EffectiveMinimumGrossMarginPercent);
        Assert.Contains(InventoryCommercialMarginSettingReason.Missing, resolution.Reasons);
        Assert.Null(InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(resolution));
    }

    [Fact]
    public void Setting_nulo_e_Missing()
    {
        var resolution = InventoryCommercialMarginPolicyResolver.Resolve(null);
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Missing, resolution.Status);
        Assert.Equal(InventoryCommercialMarginPolicySource.None, resolution.Source);
        Assert.Null(resolution.EffectiveMinimumGrossMarginPercent);
    }

    [Fact]
    public void Invalid_permanece_Invalid_sem_Source_Global()
    {
        var resolution = InventoryCommercialMarginPolicyResolver.Resolve(
            InvalidSetting(InventoryCommercialMarginSettingReason.OutOfRange));
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Invalid, resolution.Status);
        Assert.Equal(InventoryCommercialMarginPolicySource.None, resolution.Source);
        Assert.NotEqual(InventoryCommercialMarginPolicySource.Global, resolution.Source);
        Assert.Null(resolution.EffectiveMinimumGrossMarginPercent);
        Assert.Contains(InventoryCommercialMarginSettingReason.Invalid, resolution.Reasons);
        Assert.Contains(InventoryCommercialMarginSettingReason.OutOfRange, resolution.Reasons);
        Assert.Null(InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(resolution));
    }

    [Fact]
    public void Configured_sem_valor_e_Invalid()
    {
        var setting = new InventoryCommercialMarginSetting
        {
            Status = InventoryCommercialMarginSettingStatus.Configured,
        };
        var resolution = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Invalid, resolution.Status);
        Assert.Equal(InventoryCommercialMarginPolicySource.None, resolution.Source);
        Assert.Null(resolution.EffectiveMinimumGrossMarginPercent);
    }

    [Fact]
    public void Enums_nao_expoem_Group_nem_Product()
    {
        var names = Enum.GetNames<InventoryCommercialMarginPolicySource>()
            .Concat(Enum.GetNames<InventoryCommercialMarginPolicyResolutionStatus>());
        foreach (var name in names)
        {
            Assert.DoesNotContain("Group", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Product", name, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            "ProductId",
            typeof(InventoryCommercialMarginPolicyResolution).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void Conversao_decimal_para_double_preserva_casos_suportados()
    {
        foreach (var value in new[] { 0m, 15m, 12.75m, 15.5m, 99.99m })
        {
            var policy = InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(
                InventoryCommercialMarginPolicyResolver.Resolve(Configured(value)));
            Assert.NotNull(policy);
            Assert.Equal(decimal.ToDouble(value), policy.MinimumGrossMarginPercent);
        }
    }

    [Fact]
    public void Cultura_nao_afeta_resolver()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
            var pt = InventoryCommercialMarginPolicyResolver.Resolve(Configured(15.5m));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var en = InventoryCommercialMarginPolicyResolver.Resolve(Configured(15.5m));
            Assert.Equal(pt.EffectiveMinimumGrossMarginPercent, en.EffectiveMinimumGrossMarginPercent);
            Assert.Equal(15.5m, pt.EffectiveMinimumGrossMarginPercent);
            Assert.Equal(15.5, InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(pt)!.MinimumGrossMarginPercent);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void Deterministico_e_sem_mutacao()
    {
        var reasons = new List<InventoryCommercialMarginSettingReason>
        {
            InventoryCommercialMarginSettingReason.NonInvariantFormat,
            InventoryCommercialMarginSettingReason.Invalid,
        };
        var setting = new InventoryCommercialMarginSetting
        {
            Status = InventoryCommercialMarginSettingStatus.Invalid,
            RawValue = "15,5",
            Reasons = reasons,
        };
        var a = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        var b = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        Assert.Equal(a.Status, b.Status);
        Assert.Equal(a.Source, b.Source);
        Assert.Equal(a.Reasons, b.Reasons);
        Assert.Equal(2, reasons.Count);
        Assert.Equal("15,5", setting.RawValue);
    }

    [Fact]
    public void Adapter_Available_cria_policy_B3()
    {
        var resolution = InventoryCommercialMarginPolicyResolver.Resolve(Configured(12.75m));
        var policy = InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(resolution);
        Assert.NotNull(policy);
        Assert.Equal(12.75, policy.MinimumGrossMarginPercent);
    }

    [Fact]
    public void Adapter_Missing_e_Invalid_nao_fabricam_policy()
    {
        Assert.Null(InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(null));
        Assert.Null(InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(
            InventoryCommercialMarginPolicyResolver.Resolve(MissingSetting())));
        Assert.Null(InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(
            InventoryCommercialMarginPolicyResolver.Resolve(InvalidSetting())));
    }

    [Fact]
    public void Zero_explicito_chega_ao_B3_diferente_de_Missing()
    {
        var zero = InventoryCommercialMarginPolicyResolver.Resolve(Configured(0m));
        var missing = InventoryCommercialMarginPolicyResolver.Resolve(MissingSetting());
        var zeroPolicy = InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(zero);
        var missingPolicy = InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(missing);

        Assert.Equal(0m, zero.EffectiveMinimumGrossMarginPercent);
        Assert.Equal(0.0, zeroPolicy!.MinimumGrossMarginPercent);
        Assert.Null(missing.EffectiveMinimumGrossMarginPercent);
        Assert.Null(missingPolicy);

        var facts = Facts(sale: 12, cost: 10);
        var withZero = InventoryCommercialPriceFloorEngine.Evaluate(facts, zeroPolicy);
        var withMissing = InventoryCommercialPriceFloorEngine.Evaluate(facts, missingPolicy);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, withZero.Status);
        Assert.Equal(10, withZero.MinimumAllowedCatalogPrice);
        Assert.Equal(InventoryCommercialPriceFloorStatus.PolicyMissing, withMissing.Status);
        Assert.Null(withMissing.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void B3_com_Available_calcula_piso()
    {
        var policy = InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(
            InventoryCommercialMarginPolicyResolver.Resolve(Configured(40m)));
        var result = InventoryCommercialPriceFloorEngine.Evaluate(Facts(sale: 20, cost: 10), policy);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, result.Status);
        Assert.Equal(16.67, result.MinimumAllowedCatalogPrice);
        Assert.Equal(40, result.MinimumGrossMarginPercent);
    }

    [Fact]
    public void B3_com_Invalid_permanece_sem_piso()
    {
        var policy = InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(
            InventoryCommercialMarginPolicyResolver.Resolve(InvalidSetting()));
        var result = InventoryCommercialPriceFloorEngine.Evaluate(Facts(), policy);
        Assert.Equal(InventoryCommercialPriceFloorStatus.PolicyMissing, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Budget_futuro_continua_9()
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

    [Fact]
    public void Fonte_pura_sem_banco_nem_default()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialMarginPolicyResolver.cs"));
        var model = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Models", "InventoryCommercialMarginPolicyResolution.cs"));
        foreach (var text in new[] { source, model })
        {
            Assert.DoesNotContain("AppSettingsService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AppSession", text, StringComparison.Ordinal);
            Assert.DoesNotContain("StoreNetwork", text, StringComparison.Ordinal);
            Assert.DoesNotContain("lucro_percent", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_compra", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Load()", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("= 15", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 18", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 22", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 30", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Group", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Product", source, StringComparison.Ordinal);
    }

    [Fact]
    public void B3B_B3_B1_B2_nao_conhecem_resolver()
    {
        var files = new[]
        {
            FindSource("src", "SGDB.App", "Services", "InventoryCommercialMarginSettingsService.cs"),
            FindSource("src", "SGDB.App", "Services", "InventoryCommercialPriceFloorEngine.cs"),
            FindSource("src", "SGDB.App", "Services", "InventoryCommercialEligibilityEngine.cs"),
            FindSource("src", "SGDB.App", "Services", "InventoryCommercialFactsEngine.cs"),
            FindSource("src", "SGDB.App", "Services", "InventoryCommercialFactsService.cs"),
        };
        foreach (var path in files)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("MarginPolicyResolver", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PolicyResolution", text, StringComparison.Ordinal);
        }

        var b3 = File.ReadAllText(files[1]);
        Assert.DoesNotContain("app_settings", b3, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetSetting", b3, StringComparison.Ordinal);
        Assert.DoesNotContain("MarginSettingsService", b3, StringComparison.Ordinal);
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
