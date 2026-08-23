using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class ProductNextExpiryTests
{
    private static readonly DateTime ExpiryNear = new(2026, 10, 10);
    private static readonly DateTime ExpiryFar = new(2026, 11, 20);

    [Fact]
    public void SemLote_RetornaNull()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2, "S1", "SEM LOTE");

        Assert.Null(ProductExpiryService.GetNextExpiry(productId));
        Assert.Equal(ProductExpiryService.UninformedDisplay, ProductExpiryService.FormatDisplay(null));
        Assert.Null(ProductService.GetById(productId)!.NextExpiry);
    }

    [Fact]
    public void UmLote_RetornaADataDele()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "S2", "UM LOTE");
        Receive(productId, 10, "A", ExpiryNear);

        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
        Assert.Equal("10/10/2026", ProductExpiryService.FormatDisplay(ExpiryNear));
        Assert.Equal(ExpiryNear, ProductService.GetById(productId)!.NextExpiry);
    }

    [Fact]
    public void DoisLotes_RetornaMenorData()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "S3", "DOIS LOTES");
        Receive(productId, 10, "A", ExpiryNear);
        Receive(productId, 24, "B", ExpiryFar);

        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
        Assert.Equal(2, CountLots(productId));
    }

    [Fact]
    public void LoteSemValidade_Ignorado()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "S4", "SEM EXP");
        Receive(productId, 8, "SEMDATA", expiry: null);
        Receive(productId, 5, "COM", ExpiryFar);

        Assert.Equal(ExpiryFar, ProductExpiryService.GetNextExpiry(productId));
    }

    [Fact]
    public void LoteQuantidadeZero_Ignorado()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "S5", "QTD ZERO");
        Receive(productId, 10, "A", ExpiryNear);
        Receive(productId, 24, "B", ExpiryFar);

        using (var conn = DatabaseService.OpenConnection())
        using (var tx = conn.BeginTransaction())
        {
            ProductLotService.DeductFefo(conn, tx, productId, 10);
            tx.Commit();
        }

        Assert.Equal(1, CountLots(productId));
        Assert.Equal(ExpiryFar, ProductExpiryService.GetNextExpiry(productId));
    }

    [Fact]
    public void ExtraJsonDataValidade_NaoVenceProductLots()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "S6", "LEGADO");
        SetExtraJson(productId, new ProductExtra
        {
            ControleValidade = true,
            DataValidade = "01/01/2020",
        });
        Receive(productId, 10, "A", ExpiryNear);
        Receive(productId, 24, "B", ExpiryFar);

        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
        var extra = ProductExtra.Parse(ProductService.GetById(productId)!.ExtraJson);
        Assert.Equal("01/01/2020", extra.DataValidade);
    }

    [Fact]
    public void DuasCompras_NaoSobrescrevemDatas_ProximaEMin()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "S7", "COMPRAS");

        CreateClosedPurchase(supplierId, productId, "COMPRAS", 10, "A", ExpiryNear, "NF-A");
        CreateClosedPurchase(supplierId, productId, "COMPRAS", 24, "B", ExpiryFar, "NF-B");

        Assert.Equal(34, TestDataHelper.GetProductStock(productId));
        Assert.Equal(2, CountLots(productId));
        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));

        CreateClosedPurchase(supplierId, productId, "COMPRAS", 5, "C", ExpiryNear.AddDays(-5), "NF-C");
        Assert.Equal(ExpiryNear.AddDays(-5), ProductExpiryService.GetNextExpiry(productId));
    }

    [Fact]
    public void CompraMaisNovaComValidadeMaisLonga_MantemMin()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "S8", "LONGA");

        CreateClosedPurchase(supplierId, productId, "LONGA", 10, "A", ExpiryNear, "NF-1");
        CreateClosedPurchase(supplierId, productId, "LONGA", 24, "B", ExpiryFar, "NF-2");

        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
        Assert.Equal(ExpiryNear, ProductService.GetById(productId)!.NextExpiry);
    }

    private static void Receive(int productId, double qty, string lot, DateTime? expiry) =>
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = qty,
            LotNumber = lot,
            ExpiryDate = expiry,
        });

    private static int CountLots(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM product_lots WHERE product_id = $id AND quantity > 0.0001;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetExtraJson(int productId, ProductExtra extra)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET extra_json = $j WHERE id = $id;";
        cmd.Parameters.AddWithValue("$j", extra.ToJson());
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
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
            VALUES ('fornecedor', 'juridica', 'FORN B1', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
