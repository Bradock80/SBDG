using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69D-C2-B2 — capability product_merge_safe_v2 no POST de unificação.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class ProductMergeCapabilityTests
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
    public void HostAntigo_BloqueiaMergeAntesDoPost()
    {
        using var _ = BeginDb();
        var keep = TestDataHelper.SeedSimpleProduct(10, 8, 5, "MK", "KEEP NET");
        var absorb = TestDataHelper.SeedSimpleProduct(5, 8, 7, "MA", "ABS NET");

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures =
        [
            "session", "pairing",
            PurchaseSalePriceRules.AtomicFeature,
            PurchaseAverageCostRules.AtomicFeature,
            PurchaseCancelCostRules.AtomicFeature,
        ];

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keep, absorb));
            Assert.Equal(ProductMergeRules.HostNeedsUpgradeBeforeMergeMessage, ex.Message);
            Assert.Equal(0, StoreNetworkClient.TestMergeSendCount);
            Assert.Equal(1, StoreNetworkClient.TestStatusFetchCount);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        Assert.True(ProductService.GetById(absorb)!.Active);
        Assert.Equal(10, TestDataHelper.GetProductStock(keep));
        Assert.Equal(5, ProductService.GetById(keep)!.CostPrice);
    }

    [Fact]
    public void CacheComCapability_NaoRefazStatus()
    {
        using var _ = BeginDb();
        StoreNetworkClient.SeedCachedFeatures(
        [
            "session",
            ProductMergeRules.AtomicFeature,
        ]);
        StoreNetworkClient.EnsureProductMergeSafeCapability();
        Assert.Equal(0, StoreNetworkClient.TestStatusFetchCount);
    }

    [Fact]
    public void HostNovo_AnunciaCapability_ApiVersionContinua2()
    {
        Assert.Contains(ProductMergeRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);
        var host = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkHost.cs"));
        Assert.Contains("apiVersion = 2", host);
        Assert.DoesNotContain("apiVersion = 3", host);
        Assert.Contains("ProductMergeRules.AtomicFeature", host);
        var merge = Slice(host, "path.Equals(\"/api/products/merge\"", "path.StartsWith(\"/api/products/\"");
        Assert.Contains("MergeProductsLocal", merge);
    }

    [Fact]
    public void Client_Merge_SoPostNoHost_SemSegundoRpc()
    {
        var client = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkClient.cs"));
        var merge = Slice(client, "public static Product MergeProducts", "public static StockAdjustResult AdjustStock");
        Assert.Contains("EnsureProductMergeSafeCapability", merge);
        Assert.Contains("api/products/merge", merge);
        Assert.DoesNotContain("UpdateProduct", merge);
    }

    [Fact]
    public void StatusDto_DeserializaNovaCapability()
    {
        const string json = """
            {"ok":true,"apiVersion":2,"features":["session","product_merge_safe_v2"]}
            """;
        var dto = System.Text.Json.JsonSerializer.Deserialize<StoreNetworkStatusDto>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.ApiVersion);
        Assert.True(ProductMergeRules.SupportsSafeMerge(dto.Features));
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

    private static string AppSourceRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SGDB.App"));
}
