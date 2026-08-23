using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Views;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class ManualPurchaseExpiryTests
{
    private static readonly DateTime ExpiryNear = new(2026, 10, 10);
    private static readonly DateTime ExpiryFar = new(2026, 11, 20);

    [Fact]
    public void Manual_ControlaValidade_PedeValidade()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProduct(controleValidade: true, name: "SABAO EM PO");

        var items = new[]
        {
            new NfeImportItem
            {
                Name = "SABAO EM PO",
                Quantity = 10,
                MatchedProductId = productId,
            },
        };

        Assert.True(ProductExpiryService.RequiresExpiryControl(ProductService.GetById(productId)));
        Assert.True(ProductExpiryService.PurchaseShouldPromptExpiry(items));
        Assert.True(NfeLotValidityWindow.ResolveRequiresExpiry(items[0]));
        Assert.True(items[0].NeedsManualExpiry);
    }

    [Fact]
    public void Manual_NaoControla_NaoObriga()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProduct(controleValidade: false, name: "SABAO EM PO");

        var items = new[]
        {
            new NfeImportItem
            {
                Name = "SABAO EM PO",
                Quantity = 10,
                MatchedProductId = productId,
            },
        };

        Assert.False(ProductExpiryService.RequiresExpiryControl(ProductService.GetById(productId)));
        Assert.False(ProductExpiryService.PurchaseShouldPromptExpiry(items));
        Assert.False(NfeLotValidityWindow.ResolveRequiresExpiry(items[0]));
        Assert.True(NfeLotValidityWindow.ConfirmOrSkip(owner: null, items));
    }

    [Fact]
    public void Xml_ContinuaPedindoQuandoControla()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProduct(controleValidade: true, name: "CERVEJA TESTE");

        var items = new[]
        {
            new NfeImportItem
            {
                Name = "CERVEJA TESTE",
                Quantity = 12,
                MatchedProductId = productId,
                HasXmlRastro = true,
            },
        };

        Assert.True(ProductExpiryService.PurchaseShouldPromptExpiry(items));
        Assert.True(NfeLotValidityWindow.ResolveRequiresExpiry(items[0]));
    }

    [Fact]
    public void ValidadeInformada_CriaProductLotsEPurchaseItemLots()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = SeedProduct(controleValidade: true, name: "REFRI COLA");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "REFRI COLA", 10, lot: "", ExpiryNear, "NF-MAN-1");

        var origin = Assert.Single(PurchaseService.ListPurchaseItemLots(purchaseId));
        Assert.Equal(ExpiryNear, origin.ExpiryDate);
        Assert.Equal(10, origin.Quantity);
        Assert.True(origin.ProductLotId is > 0);

        var lots = ProductLotService.ListByProduct(productId);
        var lot = Assert.Single(lots);
        Assert.Equal(ExpiryNear, lot.ExpiryDate);
        Assert.Equal(10, lot.Quantity);
        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
    }

    [Fact]
    public void DuasCompras_PreservamDuasValidades()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = SeedProduct(controleValidade: true, name: "SUCO UVA");

        CreateClosedPurchase(supplierId, productId, "SUCO UVA", 10, "L1", ExpiryNear, "NF-1");
        CreateClosedPurchase(supplierId, productId, "SUCO UVA", 24, "L2", ExpiryFar, "NF-2");

        Assert.Equal(34, TestDataHelper.GetProductStock(productId));
        Assert.Equal(2, ProductLotService.ListByProduct(productId).Count);
        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
    }

    [Fact]
    public void FechamentoSemValidadeObrigatoria_NaoGravaDataFalsa()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = SeedProduct(controleValidade: false, name: "VASSOURA");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "VASSOURA", 6, lot: null, expiry: null, "NF-LIVRE");

        Assert.Equal(6, TestDataHelper.GetProductStock(productId));
        Assert.Empty(PurchaseService.ListPurchaseItemLots(purchaseId));
        Assert.Empty(ProductLotService.ListByProduct(productId));
        Assert.Null(ProductExpiryService.GetNextExpiry(productId));
        var extra = ProductExtra.Parse(ProductService.GetById(productId)!.ExtraJson);
        Assert.Null(extra.DataValidade);
    }

    private static int SeedProduct(bool controleValidade, string name)
    {
        var extra = new ProductExtra { ControleValidade = controleValidade }.ToJson();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (code, name, unit, sale_price, stock, cost_price, active, extra_json)
            VALUES ($code, $name, 'UN', 5, 0, 2, 1, $extra);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", $"B1{Guid.NewGuid():N}"[..8]);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CreateClosedPurchase(
        int supplierId, int productId, string name, double qty, string? lot, DateTime? expiry, string number) =>
        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = number,
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = productId,
                    ProductName = name,
                    Quantity = qty,
                    UnitPrice = 2,
                    LotNumber = lot,
                    ExpiryDate = expiry,
                },
            ],
        }, closeOnSave: true);

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN B1M', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
