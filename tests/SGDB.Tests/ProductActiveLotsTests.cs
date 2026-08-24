using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class ProductActiveLotsTests
{
    private static readonly DateTime ExpiryNear = new(2026, 10, 10);
    private static readonly DateTime ExpiryFar = new(2026, 11, 20);

    [Fact]
    public void DoisLotes_OrdenadosPelaValidade()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "L1", "ORDENA");
        Receive(productId, 24, "B", ExpiryFar, cost: 3.1, purchaseId: 20);
        Receive(productId, 10, "A", ExpiryNear, cost: 2.5, purchaseId: 10);

        var lots = ProductLotService.ListByProduct(productId);
        Assert.Equal(2, lots.Count);
        Assert.Equal("A", lots[0].LotNumber);
        Assert.Equal(ExpiryNear, lots[0].ExpiryDate);
        Assert.Equal("B", lots[1].LotNumber);
        Assert.Equal(ExpiryFar, lots[1].ExpiryDate);
        Assert.Equal(ExpiryNear, ProductExpiryService.NextFromLots(lots));
        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
    }

    [Fact]
    public void LoteSemValidade_FicaPorUltimo()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "L2", "SEM DATA");
        Receive(productId, 8, "SEM", expiry: null);
        Receive(productId, 5, "COM", ExpiryFar);

        var lots = ProductLotService.ListByProduct(productId);
        Assert.Equal(2, lots.Count);
        Assert.Equal("COM", lots[0].LotNumber);
        Assert.Equal("SEM", lots[1].LotNumber);
        Assert.Null(lots[1].ExpiryDate);
        Assert.Equal(ExpiryFar, ProductExpiryService.NextFromLots(lots));
    }

    [Fact]
    public void SomenteQuantityPositiva()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "L3", "QTD");
        Receive(productId, 10, "A", ExpiryNear);
        Receive(productId, 24, "B", ExpiryFar);

        using (var conn = DatabaseService.OpenConnection())
        using (var tx = conn.BeginTransaction())
        {
            ProductLotService.DeductFefo(conn, tx, productId, 10);
            tx.Commit();
        }

        var lots = ProductLotService.ListByProduct(productId);
        Assert.Single(lots);
        Assert.Equal("B", lots[0].LotNumber);
        Assert.Equal(ExpiryFar, ProductExpiryService.GetNextExpiry(productId));
    }

    [Fact]
    public void ProdutoSemLote_ListaVazia()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2, "L4", "VAZIO");

        Assert.Empty(ProductLotService.ListByProduct(productId));
        Assert.Null(ProductExpiryService.GetNextExpiry(productId));
        Assert.Empty(ProductLotListRow.FromLots([]));
    }

    [Fact]
    public void CustoEPurchaseId_Preservados()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "L5", "CUSTO");
        Receive(productId, 12, "A", ExpiryNear, cost: 4.35, purchaseId: 423, notes: "NF-423");

        var lot = Assert.Single(ProductLotService.ListByProduct(productId));
        Assert.Equal(4.35, lot.UnitCost, 2);
        Assert.Equal(423, lot.PurchaseId);
        Assert.Equal("NF-423", lot.Notes);

        var row = Assert.Single(ProductLotListRow.FromLots([lot]));
        Assert.Equal("Compra #423", row.OriginDisplay);
        Assert.Contains("4,35", row.CostDisplay.Replace(".", ","));
        Assert.Contains("NF-423", row.HistoryDisplay);
    }

    [Fact]
    public void CadastroContinuaMostrandoNextExpiry()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "L6", "NEXT");
        Receive(productId, 10, "A", ExpiryNear);
        Receive(productId, 24, "B", ExpiryFar);

        Assert.Equal(ExpiryNear, ProductService.GetById(productId)!.NextExpiry);
        Assert.Equal("10/10/2026", ProductExpiryService.FormatDisplay(ExpiryNear));
    }

    [Fact]
    public void DuasCompras_PreservamDuasDatas()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "L7", "DUAS");

        CreateClosedPurchase(supplierId, productId, "DUAS", 10, "A", ExpiryNear, "NF-A");
        CreateClosedPurchase(supplierId, productId, "DUAS", 24, "B", ExpiryFar, "NF-B");

        var lots = ProductLotService.ListByProduct(productId);
        Assert.Equal(2, lots.Count);
        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
        Assert.Equal(34, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void ExtraJson_NaoInterfere()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "L8", "EXTRA");
        SetExtraJson(productId, new ProductExtra
        {
            ControleValidade = true,
            DataValidade = "01/01/2020",
        });
        Receive(productId, 10, "A", ExpiryNear);

        Assert.Equal(ExpiryNear, ProductExpiryService.GetNextExpiry(productId));
        Assert.Equal("01/01/2020", ProductExtra.Parse(ProductService.GetById(productId)!.ExtraJson).DataValidade);
        Assert.DoesNotContain("01/01/2020", ProductLotListRow.FromLots(ProductLotService.ListByProduct(productId))[0].ExpiryDisplay);
    }

    private static void Receive(
        int productId, double qty, string lot, DateTime? expiry,
        double cost = 0, int? purchaseId = null, string? notes = null) =>
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = qty,
            LotNumber = lot,
            ExpiryDate = expiry,
            UnitCost = cost,
            PurchaseId = purchaseId,
            Notes = notes,
        });

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
            VALUES ('fornecedor', 'juridica', 'FORN B2', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
