using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69D-C2-B1 — capability purchase_cancel_cost_safe no cancel/reopen da Rede Loja.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseCancelCostCapabilityTests
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
    public void HostAntigo_BloqueiaCancelAntesDoPost()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "CC", "CAP CANCEL");
        var purchaseId = CreateClosed(supplier, productId, "CAP CANCEL", 10, 7);

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures =
        [
            "session", "pairing",
            PurchaseSalePriceRules.AtomicFeature,
            PurchaseAverageCostRules.AtomicFeature,
        ];

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
            Assert.Equal(PurchaseCancelCostRules.HostNeedsUpgradeBeforeCancelMessage, ex.Message);
            Assert.Equal(0, StoreNetworkClient.TestCancelSendCount);
            Assert.Equal(1, StoreNetworkClient.TestStatusFetchCount);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.Equal("fechada", GetStatus(purchaseId));
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal(6, ProductService.GetById(productId)!.CostPrice);
    }

    [Fact]
    public void HostAntigo_BloqueiaReopenAntesDoPost()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "CR", "CAP REOPEN");
        var purchaseId = CreateClosed(supplier, productId, "CAP REOPEN", 10, 7);

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures = ["session"];

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Reopen(purchaseId));
            Assert.Equal(PurchaseCancelCostRules.HostNeedsUpgradeBeforeCancelMessage, ex.Message);
            Assert.Equal(0, StoreNetworkClient.TestCancelSendCount);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.Equal("fechada", GetStatus(purchaseId));
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
            PurchaseCancelCostRules.AtomicFeature,
        ]);

        StoreNetworkClient.EnsurePurchaseCancelCostSafeCapability();

        Assert.Equal(0, StoreNetworkClient.TestStatusFetchCount);
    }

    [Fact]
    public void HostNovo_AnunciaCapability_ApiVersionContinua2()
    {
        Assert.Contains(PurchaseCancelCostRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);
        Assert.Contains(PurchaseAverageCostRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);
        Assert.Contains(PurchaseSalePriceRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);

        var host = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkHost.cs"));
        Assert.Contains("apiVersion = 2", host);
        Assert.DoesNotContain("apiVersion = 3", host);
        Assert.Contains("PurchaseCancelCostRules.AtomicFeature", host);
    }

    [Fact]
    public void Client_CancelReopen_SoPostNoHost_SemSegundoRpcDeCusto()
    {
        var client = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkClient.cs"));
        var cancel = Slice(client, "public static void CancelPurchase", "public static IReadOnlyList<Person> ListPeople");
        Assert.Contains("EnsurePurchaseCancelCostSafeCapability", cancel);
        Assert.Contains("/cancel", cancel);
        Assert.DoesNotContain("UpdateProduct", cancel);
        Assert.DoesNotContain("api/products", cancel);

        var reopen = Slice(client, "public static void ReopenPurchase", "public static IReadOnlyList<Person> ListPeople");
        Assert.Contains("EnsurePurchaseCancelCostSafeCapability", reopen);
        Assert.Contains("/reopen", reopen);
        Assert.DoesNotContain("UpdateProduct", reopen);
    }

    [Fact]
    public void StatusDto_DeserializaNovaCapability()
    {
        const string json = """
            {"ok":true,"apiVersion":2,"features":["session","purchase_cancel_cost_safe"]}
            """;
        var dto = System.Text.Json.JsonSerializer.Deserialize<StoreNetworkStatusDto>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.ApiVersion);
        Assert.True(PurchaseCancelCostRules.SupportsCancelCostSafe(dto.Features));
    }

    [Fact]
    public void TlsSession_Intacto()
    {
        var client = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkClient.cs"));
        Assert.Contains("TlsRequiredMessage", client);
        Assert.Contains("https://", client);
        Assert.Contains("SessionToken", client);
    }

    private static string Slice(string src, string start, string end)
    {
        var i = src.IndexOf(start, StringComparison.Ordinal);
        Assert.True(i >= 0, start);
        var j = src.IndexOf(end, i, StringComparison.Ordinal);
        Assert.True(j > i, end);
        return src[i..j];
    }

    private static int CreateClosed(int supplierId, int productId, string name, double qty, double unit)
    {
        return PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-CAP",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = productId,
                    ProductName = name,
                    Quantity = qty,
                    UnitPrice = unit,
                    SalePrice = 8,
                },
            ],
        }, closeOnSave: true);
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN CAPC2', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string GetStatus(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static string AppSourceRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SGDB.App"));
}
