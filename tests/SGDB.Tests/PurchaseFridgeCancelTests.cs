using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 61E-B — Estorno de compra exige quantidade no depósito (products.stock).
/// stock_fridge não autoriza a baixa e não é alterado pelo cancelamento.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseFridgeCancelTests
{
    private static readonly DateTime ExpiryFar = DateTime.Today.AddDays(200);

    [Fact]
    public void CancelPurchase_WarehouseSufficient_FridgeZero_Succeeds()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "WH0", "SO DEPOSITO");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SO DEPOSITO", 20, lot: null, expiry: null);
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetFridge(productId));

        PurchaseService.Cancel(purchaseId);

        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetFridge(productId));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
        Assert.Equal(1, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void CancelPurchase_Warehouse5_Fridge15_Need20_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "P15", "PARTE FRIDGE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "PARTE FRIDGE", 20, lot: null, expiry: null);
        SetLocations(productId, warehouse: 5, fridge: 15);
        var titlesBefore = PayableService.ListTitlesLocal(purchaseId: purchaseId).Count;

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("geladeira", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Retorne a quantidade", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(5, TestDataHelper.GetProductStock(productId));
        Assert.Equal(15, GetFridge(productId));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
        Assert.Equal(titlesBefore, PayableService.ListTitlesLocal(purchaseId: purchaseId).Count);
    }

    [Fact]
    public void CancelPurchase_Warehouse0_Fridge20_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "ALLF", "TUDO FRIDGE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "TUDO FRIDGE", 20, lot: null, expiry: null);
        SetLocations(productId, warehouse: 0, fridge: 20);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("geladeira", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(20, GetFridge(productId));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void CancelPurchase_Warehouse3_Fridge4_Need20_BlocksTotalInsufficient()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "TOT", "TOTAL INSUF");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "TOTAL INSUF", 20, lot: null, expiry: null);
        SetLocations(productId, warehouse: 3, fridge: 4);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("estoque atual", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("estoque negativo", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("geladeira", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(3, TestDataHelper.GetProductStock(productId));
        Assert.Equal(4, GetFridge(productId));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void CancelPurchase_Warehouse25_Fridge10_Need20_LeavesFridgeUntouched()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "EXF", "FRIDGE EXTRA");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "FRIDGE EXTRA", 20, lot: null, expiry: null);
        SetLocations(productId, warehouse: 25, fridge: 10);

        PurchaseService.Cancel(purchaseId);

        Assert.Equal(5, TestDataHelper.GetProductStock(productId));
        Assert.Equal(10, GetFridge(productId));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
        Assert.Equal(1, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void CancelPurchase_TrackedLot_PartInFridge_BlocksWithoutDeductExact()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "LOT", "LOTE FRIDGE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "LOTE FRIDGE", 20, "B", ExpiryFar);
        Assert.Equal(20, GetLotQty(productId, "B"));
        SetLocations(productId, warehouse: 5, fridge: 15);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("geladeira", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(5, TestDataHelper.GetProductStock(productId));
        Assert.Equal(15, GetFridge(productId));
        Assert.Equal(20, GetLotQty(productId, "B"));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
        Assert.Single(PurchaseService.ListPurchaseItemLots(purchaseId));
    }

    [Fact]
    public void CancelPurchase_Mixed_OneProductInFridge_BlocksAll()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var a = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MXA", "A OK");
        var b = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MXB", "B FRIDGE");

        var purchaseId = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-MIX-FR",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = a,
                    ProductName = "A OK",
                    Quantity = 10,
                    UnitPrice = 2,
                    LotNumber = "LA",
                    ExpiryDate = ExpiryFar,
                },
                new PurchaseItemInput
                {
                    ProductId = b,
                    ProductName = "B FRIDGE",
                    Quantity = 10,
                    UnitPrice = 2,
                },
            ],
        }, closeOnSave: true);

        SetLocations(b, warehouse: 5, fridge: 15);
        var titlesBefore = PayableService.ListTitlesLocal(purchaseId: purchaseId).Count;

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("geladeira", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(10, TestDataHelper.GetProductStock(a));
        Assert.Equal(5, TestDataHelper.GetProductStock(b));
        Assert.Equal(15, GetFridge(b));
        Assert.Equal(10, GetLotQty(a, "LA"));
        Assert.Equal(0, CountMovements(a, "estorno_compra", purchaseId));
        Assert.Equal(0, CountMovements(b, "estorno_compra", purchaseId));
        Assert.Equal(titlesBefore, PayableService.ListTitlesLocal(purchaseId: purchaseId).Count);
    }

    [Fact]
    public void CancelPurchase_AfterManualReturnToWarehouse_Succeeds()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "RET", "RETORNO");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "RETORNO", 20, "R1", ExpiryFar);
        SetLocations(productId, warehouse: 5, fridge: 15);

        Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(20, GetLotQty(productId, "R1"));

        SetLocations(productId, warehouse: 20, fridge: 0);
        PurchaseService.Cancel(purchaseId);

        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetFridge(productId));
        Assert.Equal(0, GetLotQty(productId, "R1"));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
        Assert.Equal(1, CountMovements(productId, "estorno_compra", purchaseId));
    }

    // --- helpers ---

    private static int CreateClosedPurchase(
        int supplierId,
        int productId,
        string name,
        double qty,
        string? lot,
        DateTime? expiry,
        string number = "NF-61EB")
    {
        return PurchaseService.Create(new PurchaseInput
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
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN 61EB', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetLocations(int productId, double warehouse, double fridge)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock = $stock, stock_fridge = $fridge WHERE id = $id;";
        cmd.Parameters.AddWithValue("$stock", warehouse);
        cmd.Parameters.AddWithValue("$fridge", fridge);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static double GetFridge(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(stock_fridge,0) FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static string GetPurchaseStatus(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static double GetLotQty(int productId, string lotNumber)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(quantity),0) FROM product_lots
            WHERE product_id = $id AND IFNULL(lot_number,'') = $lot;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.Parameters.AddWithValue("$lot", lotNumber);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static int CountMovements(int productId, string operation, int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE product_id = $pid
              AND IFNULL(operation,'') = $op
              AND IFNULL(ref_type,'') = 'purchase'
              AND IFNULL(ref_id,0) = $rid;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$op", operation);
        cmd.Parameters.AddWithValue("$rid", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
