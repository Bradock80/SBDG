using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class ProductLotsNetworkTests
{
    [Fact]
    public void Standalone_ListaLotesLocais()
    {
        using var db = TempDatabase.Create();
        Assert.False(StoreNetworkMode.IsClient);
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "N1", "STAND");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 7,
            LotNumber = "L",
            ExpiryDate = new DateTime(2026, 10, 10),
        });

        var lots = ProductLotService.ListByProduct(productId);
        Assert.Single(lots);
        Assert.Equal("L", lots[0].LotNumber);
        Assert.Equal(0, StoreNetworkClient.TestListProductLotsSendCount);
    }

    [Fact]
    public void Host_ListaLotesLocais()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "N2", "HOST");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 4,
            LotNumber = "H",
            ExpiryDate = new DateTime(2026, 11, 20),
        });

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleServer);
        try
        {
            var lots = ProductLotService.ListByProduct(productId);
            Assert.Single(lots);
            Assert.Equal("H", lots[0].LotNumber);
            Assert.Equal(0, StoreNetworkClient.TestListProductLotsSendCount);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Client_UsaHost_NaoLeBancoLocal()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "N3", "CLI");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 9,
            LotNumber = "LOCAL",
            ExpiryDate = new DateTime(2026, 10, 10),
        });

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures =
        [
            "session",
            ProductExpiryService.LotsReadFeature,
        ];
        StoreNetworkClient.TestListProductLots = id =>
        [
            new ProductLot
            {
                Id = 99,
                ProductId = id,
                LotNumber = "HOST",
                ExpiryDateIso = "2026-12-01",
                Quantity = 3,
                PurchaseId = 7,
                UnitCost = 1.2,
            },
        ];

        try
        {
            var lots = ProductLotService.ListByProduct(productId);
            Assert.Equal("HOST", Assert.Single(lots).LotNumber);
            Assert.Equal(1, StoreNetworkClient.TestListProductLotsSendCount);
            Assert.Equal("LOCAL", Assert.Single(ProductLotService.ListByProductLocal(productId)).LotNumber);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void HostAntigo_FalhaClaro_SemChamarEndpoint()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "N4", "OLD");

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures = ["session", "pairing"];

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ProductLotService.ListByProduct(productId));
            Assert.Equal(ProductExpiryService.HostNeedsUpgradeForLotsMessage, ex.Message);
            Assert.Equal(0, StoreNetworkClient.TestListProductLotsSendCount);
            Assert.Equal(1, StoreNetworkClient.TestStatusFetchCount);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void HostNovo_AnunciaCapability_ApiVersionContinua2()
    {
        Assert.Contains(ProductExpiryService.LotsReadFeature, StoreNetworkHost.AdvertisedFeatures);
        var host = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkHost.cs"));
        Assert.Contains("apiVersion = 2", host);
        Assert.DoesNotContain("apiVersion = 3", host);
        Assert.Contains("ListByProductLocal", host);
        Assert.Contains("Equals(\"lots\"", host);
        var client = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkClient.cs"));
        Assert.Contains("api/products/{productId}/lots", client);
    }

    [Fact]
    public void Permissoes_ConsultaNaoExigeEdicao()
    {
        var seller = UserPermissions.ForRole("vendedor");
        Assert.True(seller.ProdutosConsultar);
        Assert.False(seller.ProdutosEditar);

        var form = File.ReadAllText(Path.Combine(AppSourceRoot(), "Views", "ProductFormWindow.xaml.cs"));
        var lotsWin = File.ReadAllText(Path.Combine(AppSourceRoot(), "Views", "ProductLotsWindow.xaml.cs"));
        var lotSvc = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "ProductLotService.cs"));
        Assert.DoesNotContain("ProdutosEditar", form);
        Assert.DoesNotContain("ProdutosEditar", lotsWin);
        Assert.DoesNotContain("ProdutosEditar", lotSvc);
        Assert.Contains("ListProductLots", lotSvc);
        Assert.DoesNotContain("OpenConnection", Slice(lotSvc,
            "public static IReadOnlyList<ProductLot> ListByProduct(int productId",
            "public static IReadOnlyList<ProductLot> ListByProductLocal"));
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
