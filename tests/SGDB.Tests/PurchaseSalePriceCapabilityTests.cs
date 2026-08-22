using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Complemento 69D-B — preflight purchase_sale_price_atomic antes do POST.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseSalePriceCapabilityTests
{
    private static TempDatabase BeginDb()
    {
        StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void SemUpdateSalePrice_NaoConsultaStatusNemBloqueia()
    {
        using var _ = BeginDb();
        StoreNetworkClient.TestStatusFeatures = ["session"]; // host antigo
        var input = ClosedInput(Item(1, "X", 5.50, 8, update: false));

        StoreNetworkClient.EnsurePurchaseSalePriceCapability(input);

        Assert.Equal(0, StoreNetworkClient.TestStatusFetchCount);
        Assert.False(PurchaseSalePriceRules.NeedsAtomicSalePriceCapability(input));
    }

    [Fact]
    public void ComUpdateSalePrice_HostSemCapability_BloqueiaAntesDoPost()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "BLK", "BLOQUEIO");
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures = ["session", "pairing"];

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                PurchaseService.Create(ClosedInput(Item(productId, "BLOQUEIO", 5.50, 9, update: true), supplier),
                    closeOnSave: true));
            Assert.Equal(PurchaseSalePriceRules.HostNeedsUpgradeBeforeCloseMessage, ex.Message);
            Assert.Equal(0, StoreNetworkClient.TestPurchaseSendCount);
            Assert.Equal(1, StoreNetworkClient.TestStatusFetchCount);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.Equal(0, CountTable("purchases"));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountTable("payable_titles"));
        Assert.Equal(8, ProductService.GetById(productId)!.SalePrice);
    }

    [Fact]
    public void HostNovo_AnunciaCapability_ApiVersionContinua2()
    {
        Assert.Contains(PurchaseSalePriceRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);
        Assert.Contains("pairing", StoreNetworkHost.AdvertisedFeatures);
        Assert.Contains("session", StoreNetworkHost.AdvertisedFeatures);

        var host = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkHost.cs"));
        Assert.Contains("apiVersion = 2", host);
        Assert.DoesNotContain("apiVersion = 3", host);
        Assert.Contains("PurchaseSalePriceRules.AtomicFeature", host);
        Assert.Contains("AdvertisedFeatures", host);
    }

    [Fact]
    public void ClientNovo_HostNovo_CapabilityEmCache_NaoRefazStatus()
    {
        using var _ = BeginDb();
        StoreNetworkClient.SeedCachedFeatures(
        [
            "session",
            PurchaseSalePriceRules.AtomicFeature,
        ]);
        var input = ClosedInput(Item(1, "X", 5.50, 9, update: true));

        StoreNetworkClient.EnsurePurchaseSalePriceCapability(input);

        Assert.Equal(0, StoreNetworkClient.TestStatusFetchCount);
    }

    [Fact]
    public void RespostaSemConfirmacao_AindaERejeitada()
    {
        var input = ClosedInput(Item(1, "X", 5.50, 9, update: true));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PurchaseSalePriceRules.EnsureHostAppliedSalePrices(input, closeOnSave: true, salePriceUpdates: null));
        Assert.Equal(PurchaseSalePriceRules.HostNeedsUpdateMessage, ex.Message);
    }

    [Fact]
    public void HostNovo_CreateLocal_AindaFechaComPreco()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "HN", "HOST NOVO");

        PurchaseService.CreateLocal(ClosedInput(Item(productId, "HOST NOVO", 5.50, 9, update: true), supplier),
            closeOnSave: true);

        Assert.Equal(9, ProductService.GetById(productId)!.SalePrice);
        Assert.Equal(1, CountTable("purchases"));
    }

    [Fact]
    public void SemHttps_ContinuaSemFallbackHttp()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StoreNetworkClient.CreateHttpClient("http://127.0.0.1:9", TimeSpan.FromSeconds(1)));
        Assert.Equal(StoreNetworkClient.TlsRequiredMessage, ex.Message);

        var client = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkClient.cs"));
        Assert.Contains("TlsRequiredMessage", client);
        Assert.DoesNotContain("http://{GetClientHost()", client);
    }

    [Fact]
    public void StatusDto_DeserializaFeatures_SemMudarApiVersion()
    {
        const string json = """
            {"ok":true,"apiVersion":2,"features":["session","purchase_sale_price_atomic"]}
            """;
        var dto = System.Text.Json.JsonSerializer.Deserialize<StoreNetworkStatusDto>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.ApiVersion);
        Assert.Contains(PurchaseSalePriceRules.AtomicFeature, dto.Features!);
        Assert.True(PurchaseSalePriceRules.SupportsAtomicSalePrice(dto.Features));
    }

    [Fact]
    public void SegundaCompra_ReutilizaCacheDoHostAntigo_SemNovoStatus()
    {
        using var _ = BeginDb();
        StoreNetworkClient.TestStatusFeatures = ["session"];
        var input = ClosedInput(Item(1, "X", 5.50, 9, update: true));

        Assert.Throws<InvalidOperationException>(() =>
            StoreNetworkClient.EnsurePurchaseSalePriceCapability(input));
        Assert.Equal(1, StoreNetworkClient.TestStatusFetchCount);

        var again = Assert.Throws<InvalidOperationException>(() =>
            StoreNetworkClient.EnsurePurchaseSalePriceCapability(input));
        Assert.Equal(PurchaseSalePriceRules.HostNeedsUpgradeBeforeCloseMessage, again.Message);
        Assert.Equal(1, StoreNetworkClient.TestStatusFetchCount);
    }

    private static PurchaseInput ClosedInput(PurchaseItemInput item, int supplierId = 1) =>
        new()
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-CAP",
            GerarEstoque = true,
            Items = [item],
        };

    private static PurchaseItemInput Item(
        int productId, string name, double unit, double sale, bool update) =>
        new()
        {
            ProductId = productId,
            ProductName = name,
            Quantity = 1,
            UnitPrice = unit,
            SalePrice = sale,
            UpdateSalePrice = update,
        };

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN CAP', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountTable(string table)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string AppSourceRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SGDB.App"));
}
