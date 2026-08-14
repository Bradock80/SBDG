using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 66E — backfills de manutenção não disparam HTTP da Rede Loja no notebook cliente.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class ProductStartupBackfillClientGuardTests
{
    private const string SanitizeKey = "product_name_sanitize_v6";
    private const string PackUnitKey = "product_unit_un_v1";

    [Fact]
    public void DoubleDividedCosts_Client_ReturnsZero_WithoutRpc()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.SeedSimpleProduct(10, 5, 2);

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            var n = ProductService.BackfillFixDoubleDividedUnitCosts();
            Assert.Equal(0, n);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void CigarettePrices_Client_ReturnsZero_WithoutRpc()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.SeedSimpleProduct(10, 5, 2);

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            var n = ProductService.BackfillFixCigarettePrices();
            Assert.Equal(0, n);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void DoubleDividedCosts_Standalone_StillRuns()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.SeedSimpleProduct(10, 5, 2);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

        var n = ProductService.BackfillFixDoubleDividedUnitCosts();
        Assert.True(n >= 0);
    }

    [Fact]
    public void CigarettePrices_Standalone_StillRuns()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.SeedSimpleProduct(10, 5, 2);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

        var n = ProductService.BackfillFixCigarettePrices();
        Assert.True(n >= 0);
    }

    [Fact]
    public void Client_DoesNotMutateLocalProduct()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(10, 8.5, 0.17, code: "CIG1", name: "CIGARRO TESTE");
        var before = ProductService.GetById(id)!;

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.Equal(0, ProductService.BackfillFixDoubleDividedUnitCosts());
            Assert.Equal(0, ProductService.BackfillFixCigarettePrices());
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        var after = ProductService.GetById(id)!;
        Assert.Equal(before.CostPrice, after.CostPrice);
        Assert.Equal(before.SalePrice, after.SalePrice);
        Assert.Equal(before.ExtraJson, after.ExtraJson);
    }

    [Fact]
    public void SanitizeNames_Client_WithoutOnceFlag_ReturnsZero_WithoutRpc()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.SeedSimpleProduct(10, 5, 2, name: "PRODUTO QTD. 15.00 UN");
        DeleteSetting(SanitizeKey);
        Assert.NotEqual("1", AppSettingsService.GetSetting(SanitizeKey) ?? "");

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.Equal(0, ProductService.SanitizeAllCatalogNamesOnce());
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.NotEqual("1", AppSettingsService.GetSetting(SanitizeKey) ?? "");
    }

    [Fact]
    public void NormalizePackUnits_Client_WithoutOnceFlag_ReturnsZero_WithoutRpc()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedPackUnitProduct();
        DeleteSetting(PackUnitKey);
        Assert.NotEqual("1", AppSettingsService.GetSetting(PackUnitKey) ?? "");

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.Equal(0, ProductService.NormalizePackUnitsToUnOnce());
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.NotEqual("1", AppSettingsService.GetSetting(PackUnitKey) ?? "");
    }

    [Fact]
    public void AllStartupCatalogMaintenance_Client_WithoutOnceFlags_NoRpc_NoLocalMutation()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var dirtyId = TestDataHelper.SeedSimpleProduct(10, 5, 2, code: "DIRT", name: "PRODUTO QTD. 15.00 UN");
        var packId = SeedPackUnitProduct();
        DeleteSetting(SanitizeKey);
        DeleteSetting(PackUnitKey);
        Assert.NotEqual("1", AppSettingsService.GetSetting(SanitizeKey) ?? "");
        Assert.NotEqual("1", AppSettingsService.GetSetting(PackUnitKey) ?? "");
        Assert.True(string.IsNullOrWhiteSpace(StoreNetworkMode.GetClientHost()));

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.Equal(0, ProductService.BackfillMissingClassifications());
            Assert.Equal(0, ProductService.BackfillFixDoubleDividedUnitCosts());
            Assert.Equal(0, ProductService.BackfillFixCigarettePrices());
            Assert.Equal(0, ProductService.SanitizeAllCatalogNamesOnce());
            Assert.Equal(0, ProductService.NormalizePackUnitsToUnOnce());
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        var dirty = ProductService.GetById(dirtyId)!;
        var pack = ProductService.GetById(packId)!;
        Assert.Equal("PRODUTO QTD. 15.00 UN", dirty.Name);
        Assert.Equal("CX", pack.Unit);
        Assert.NotEqual("1", AppSettingsService.GetSetting(SanitizeKey) ?? "");
        Assert.NotEqual("1", AppSettingsService.GetSetting(PackUnitKey) ?? "");
    }

    [Fact]
    public void AllStartupCatalogMaintenance_Client_WithOnceFlagsAlreadySet_ReturnsZero_WithoutRpc()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.SeedSimpleProduct(10, 5, 2, code: "FLG1", name: "PRODUTO QTD. 15.00 UN");
        SeedPackUnitProduct();
        AppSettingsService.SetSetting(SanitizeKey, "1");
        AppSettingsService.SetSetting(PackUnitKey, "1");
        Assert.True(string.IsNullOrWhiteSpace(StoreNetworkMode.GetClientHost()));

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.Equal(0, ProductService.BackfillMissingClassifications());
            Assert.Equal(0, ProductService.BackfillFixDoubleDividedUnitCosts());
            Assert.Equal(0, ProductService.BackfillFixCigarettePrices());
            Assert.Equal(0, ProductService.SanitizeAllCatalogNamesOnce());
            Assert.Equal(0, ProductService.NormalizePackUnitsToUnOnce());
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.Equal("1", AppSettingsService.GetSetting(SanitizeKey));
        Assert.Equal("1", AppSettingsService.GetSetting(PackUnitKey));
    }

    [Fact]
    public void SanitizeNames_Standalone_StillRuns()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(10, 5, 2, name: "PRODUTO QTD. 15.00 UN");
        DeleteSetting(SanitizeKey);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

        var n = ProductService.SanitizeAllCatalogNamesOnce();
        Assert.True(n >= 1);
        Assert.Equal("1", AppSettingsService.GetSetting(SanitizeKey));
        Assert.Equal("PRODUTO", ProductService.GetById(id)!.Name);
    }

    [Fact]
    public void NormalizePackUnits_Standalone_StillRuns()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedPackUnitProduct();
        DeleteSetting(PackUnitKey);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

        var n = ProductService.NormalizePackUnitsToUnOnce();
        Assert.True(n >= 1);
        Assert.Equal("1", AppSettingsService.GetSetting(PackUnitKey));
        Assert.Equal("UN", ProductService.GetById(id)!.Unit);
    }

    private static void DeleteSetting(string key)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM app_settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
    }

    private static int SeedPackUnitProduct()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CX01', 'FARDO TESTE', 'CX', 10, 5, 2, 1, '{"fator_embalagem":12}'
            );
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
