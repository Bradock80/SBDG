using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69D-C1 — preflight purchase_average_cost_atomic antes do POST.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseAverageCostCapabilityTests
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
    public void SemFechar_NaoExigeCapability()
    {
        using var _ = BeginDb();
        StoreNetworkClient.TestStatusFeatures = ["session"];
        var input = OpenInput(Item(1, "X", 5.50));

        StoreNetworkClient.EnsurePurchaseAverageCostCapability(input, closeOnSave: false);

        Assert.Equal(0, StoreNetworkClient.TestStatusFetchCount);
    }

    [Fact]
    public void SemUpdateAverageCost_NaoExigeCapability()
    {
        using var _ = BeginDb();
        StoreNetworkClient.TestStatusFeatures = ["session"];
        var input = ClosedInput(Item(1, "X", 5.50));
        input.UpdateAverageCost = false;

        StoreNetworkClient.EnsurePurchaseAverageCostCapability(input, closeOnSave: true);

        Assert.Equal(0, StoreNetworkClient.TestStatusFetchCount);
        Assert.False(PurchaseAverageCostRules.NeedsAtomicAverageCostCapability(input));
    }

    [Fact]
    public void HostAntigo_BloqueiaAntesDoPost()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "BLK", "BLOQUEIO CUSTO");
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures = ["session", "pairing", PurchaseSalePriceRules.AtomicFeature];

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                PurchaseService.Create(ClosedInput(Item(productId, "BLOQUEIO CUSTO", 7), supplier),
                    closeOnSave: true));
            Assert.Equal(PurchaseAverageCostRules.HostNeedsUpgradeBeforeCloseMessage, ex.Message);
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
        Assert.Equal(5, ProductService.GetById(productId)!.CostPrice);
    }

    [Fact]
    public void HostNovo_AnunciaCapability_ApiVersionContinua2()
    {
        Assert.Contains(PurchaseAverageCostRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);
        Assert.Contains(PurchaseSalePriceRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);

        var host = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkHost.cs"));
        Assert.Contains("apiVersion = 2", host);
        Assert.DoesNotContain("apiVersion = 3", host);
        Assert.Contains("PurchaseAverageCostRules.AtomicFeature", host);
    }

    [Fact]
    public void CacheComCapability_NaoRefazStatus()
    {
        using var _ = BeginDb();
        StoreNetworkClient.SeedCachedFeatures(
        [
            "session",
            PurchaseSalePriceRules.AtomicFeature,
            PurchaseAverageCostRules.AtomicFeature,
        ]);
        var input = ClosedInput(Item(1, "X", 7));

        StoreNetworkClient.EnsurePurchaseAverageCostCapability(input, closeOnSave: true);

        Assert.Equal(0, StoreNetworkClient.TestStatusFetchCount);
    }

    [Fact]
    public void RespostaSemConfirmacao_Rejeitada()
    {
        var input = ClosedInput(Item(1, "X", 7));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PurchaseAverageCostRules.EnsureHostAppliedAverageCosts(input, closeOnSave: true, averageCostUpdates: null));
        Assert.Equal(PurchaseAverageCostRules.HostNeedsUpdateMessage, ex.Message);
    }

    [Fact]
    public void ContagemDivergente_Rejeitada()
    {
        var input = ClosedInput(Item(1, "X", 7));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PurchaseAverageCostRules.EnsureHostAppliedAverageCosts(input, closeOnSave: true, averageCostUpdates: 0));
        Assert.Equal(PurchaseAverageCostRules.HostDidNotApplyMessage, ex.Message);
    }

    [Fact]
    public void StatusDto_DeserializaNovaCapability()
    {
        const string json = """
            {"ok":true,"apiVersion":2,"features":["session","purchase_sale_price_atomic","purchase_average_cost_atomic"]}
            """;
        var dto = System.Text.Json.JsonSerializer.Deserialize<StoreNetworkStatusDto>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.ApiVersion);
        Assert.True(PurchaseAverageCostRules.SupportsAtomicAverageCost(dto.Features));
        Assert.True(PurchaseSalePriceRules.SupportsAtomicSalePrice(dto.Features));
    }

    private static PurchaseInput OpenInput(PurchaseItemInput item, int supplierId = 1) =>
        new()
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-OP",
            GerarEstoque = true,
            Items = [item],
        };

    private static PurchaseInput ClosedInput(PurchaseItemInput item, int supplierId = 1) =>
        new()
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-CAPC",
            GerarEstoque = true,
            Items = [item],
        };

    private static PurchaseItemInput Item(int productId, string name, double unit) =>
        new()
        {
            ProductId = productId,
            ProductName = name,
            Quantity = 1,
            UnitPrice = unit,
            SalePrice = 8,
            UpdateSalePrice = false,
        };

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN CAPC', 1, '{"ativo":true,"fornecedores":true}');
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
