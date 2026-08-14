using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 62B — Transferência geladeira → depósito (localização, total constante, sem lotes).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class FridgeTransferTests
{
    private static readonly DateTime ExpiryFar = DateTime.Today.AddDays(200);

    [Fact]
    public void TransferFridgeToWarehouse_Partial_PreservesTotal()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);
        StockService.TransferWarehouseToFridge(productId, 15);
        AssertLocations(productId, 5, 15);

        StockService.TransferFridgeToWarehouse(productId, 10);

        AssertLocations(productId, 15, 5);
        Assert.Equal(20, Total(productId));
    }

    [Fact]
    public void TransferFridgeToWarehouse_All_ClearsFridge()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);
        StockService.TransferWarehouseToFridge(productId, 15);

        StockService.TransferFridgeToWarehouse(productId, 15);

        AssertLocations(productId, 20, 0);
        Assert.Equal(20, Total(productId));
    }

    [Fact]
    public void TransferFridgeToWarehouse_MoreThanFridge_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);
        StockService.TransferWarehouseToFridge(productId, 15);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StockService.TransferFridgeToWarehouse(productId, 20));
        Assert.Contains("maior que a geladeira", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertLocations(productId, 5, 15);
        Assert.Equal(0, CountOperations(productId, "retorno_geladeira"));
    }

    [Fact]
    public void TransferFridgeToWarehouse_Zero_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(10);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StockService.TransferFridgeToWarehouse(productId, 0));
        Assert.Contains("quantidade", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertLocations(productId, 10, 0);
    }

    [Fact]
    public void TransferFridgeToWarehouse_Negative_BlocksWithoutAbs()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);
        StockService.TransferWarehouseToFridge(productId, 15);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StockService.TransferFridgeToWarehouse(productId, -5));
        Assert.Contains("quantidade", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertLocations(productId, 5, 15);
        Assert.Equal(0, CountOperations(productId, "retorno_geladeira"));
    }

    [Fact]
    public void TransferFridgeToWarehouse_NaNAndInfinity_Block()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);
        StockService.TransferWarehouseToFridge(productId, 15);

        Assert.Throws<InvalidOperationException>(
            () => StockService.TransferFridgeToWarehouse(productId, double.NaN));
        Assert.Throws<InvalidOperationException>(
            () => StockService.TransferFridgeToWarehouse(productId, double.PositiveInfinity));
        Assert.Throws<InvalidOperationException>(
            () => StockService.TransferFridgeToWarehouse(productId, double.NegativeInfinity));
        AssertLocations(productId, 5, 15);
    }

    [Fact]
    public void TransferFridgeToWarehouse_MissingProduct_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var ex = Assert.Throws<InvalidOperationException>(
            () => StockService.TransferFridgeToWarehouse(999_999, 1));
        Assert.Contains("não encontrado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TransferFridgeToWarehouse_WritesMovementWithEqualTotals()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);
        StockService.TransferWarehouseToFridge(productId, 15);

        var result = StockService.TransferFridgeToWarehouse(productId, 10);
        Assert.True(result.MovementId is > 0);

        var mov = GetLatestMovement(productId, "retorno_geladeira");
        Assert.Equal("entrada", mov.Type);
        Assert.Equal(10, mov.Qty);
        Assert.Equal(20, mov.Before);
        Assert.Equal(20, mov.After);
        Assert.Equal(mov.Before, mov.After);
        Assert.Contains("geladeira→depósito", mov.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(mov.User));
    }

    [Fact]
    public void Transfer_RoundTrip_PreservesTotal()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);

        StockService.TransferWarehouseToFridge(productId, 15);
        AssertLocations(productId, 5, 15);
        Assert.Equal(20, Total(productId));

        StockService.TransferFridgeToWarehouse(productId, 15);
        AssertLocations(productId, 20, 0);
        Assert.Equal(20, Total(productId));
        Assert.Equal(1, CountOperations(productId, "transferencia_geladeira"));
        Assert.Equal(1, CountOperations(productId, "retorno_geladeira"));
    }

    [Fact]
    public void Purchase_FridgeReturn_ThenCancel_Succeeds()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "PC", "COMPRA FRIDGE");
        var purchaseId = CreateClosedPurchase(supplierId, productId, "COMPRA FRIDGE", 20, "B", ExpiryFar);
        Assert.Equal(20, GetLotQty(productId, "B"));

        StockService.TransferWarehouseToFridge(productId, 15);
        Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));

        StockService.TransferFridgeToWarehouse(productId, 15);
        AssertLocations(productId, 20, 0);

        PurchaseService.Cancel(purchaseId);
        AssertLocations(productId, 0, 0);
        Assert.Equal(0, GetLotQty(productId, "B"));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
    }

    [Fact]
    public void Purchase_FridgeSale_ReturnRemaining_CancelBlocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "62b-sale");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "PS", "VENDA FRIDGE");
        var purchaseId = CreateClosedPurchase(supplierId, productId, "VENDA FRIDGE", 20, "B", ExpiryFar);

        StockService.TransferWarehouseToFridge(productId, 15);
        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 5, unitPrice: 5, cashReceived: 25);
        AssertLocations(productId, 5, 10);
        Assert.Equal(20, GetLotQty(productId, "B"));

        StockService.TransferFridgeToWarehouse(productId, 10);
        AssertLocations(productId, 15, 0);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("estoque", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        AssertLocations(productId, 15, 0);
        Assert.Equal(20, GetLotQty(productId, "B"));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void StockIo_ReturnFridge_IsTransferOnly_NotPhysicalEntry()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);
        StockService.TransferWarehouseToFridge(productId, 15);
        StockService.TransferFridgeToWarehouse(productId, 10);

        var report = StockIoService.List(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        var retorno = Assert.Single(report.Rows.Where(r =>
            r.ProductId == productId
            && r.Operation.Contains("Retorno geladeira", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(10, retorno.Quantity);
        Assert.True(retorno.IsEntry);
        Assert.Equal(20, retorno.StockBefore);
        Assert.Equal(20, retorno.StockAfter);

        Assert.Equal(0, report.TotalEntradas);
        Assert.Equal(0, report.TotalSaidas);
    }

    [Fact]
    public void TransferFridgeToWarehouse_FailureBeforeMovement_RollsBack()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = SeedProductWithStock(20);
        StockService.TransferWarehouseToFridge(productId, 15);

        try
        {
            StockService.TestBeforeFridgeReturnMovement = _ =>
                throw new InvalidOperationException("falha controlada no retorno");
            var ex = Assert.Throws<InvalidOperationException>(
                () => StockService.TransferFridgeToWarehouse(productId, 10));
            Assert.Contains("falha controlada", ex.Message);
        }
        finally
        {
            StockService.TestBeforeFridgeReturnMovement = null;
        }

        AssertLocations(productId, 5, 15);
        Assert.Equal(0, CountOperations(productId, "retorno_geladeira"));
    }

    [Fact]
    public void TransferWarehouseToFridge_StillWorks_DoesNotChangeLots()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "LT", "LOTE IDA");
        CreateClosedPurchase(supplierId, productId, "LOTE IDA", 20, "B", ExpiryFar);
        Assert.Equal(20, GetLotQty(productId, "B"));

        StockService.TransferWarehouseToFridge(productId, 8);
        AssertLocations(productId, 12, 8);
        Assert.Equal(20, GetLotQty(productId, "B"));

        StockService.TransferFridgeToWarehouse(productId, 8);
        AssertLocations(productId, 20, 0);
        Assert.Equal(20, GetLotQty(productId, "B"));
    }

    // --- helpers ---

    private static int SeedProductWithStock(double stock) =>
        TestDataHelper.SeedSimpleProduct(stock, 5, 2, "FR62", "FRIDGE 62B");

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN 62B', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CreateClosedPurchase(
        int supplierId, int productId, string name, double qty, string? lot, DateTime? expiry)
    {
        return PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-62B",
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

    private static void AssertLocations(int productId, double warehouse, double fridge)
    {
        Assert.Equal(warehouse, TestDataHelper.GetProductStock(productId));
        Assert.Equal(fridge, GetFridge(productId));
    }

    private static double Total(int productId) =>
        TestDataHelper.GetProductStock(productId) + GetFridge(productId);

    private static double GetFridge(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(stock_fridge,0) FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToDouble(cmd.ExecuteScalar());
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

    private static string GetPurchaseStatus(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static int CountOperations(int productId, string operation)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE product_id = $pid AND IFNULL(operation,'') = $op;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$op", operation);
        return Convert.ToInt32(cmd.ExecuteScalar());
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

    private static MovRow GetLatestMovement(int productId, string operation)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT movement_type, quantity, IFNULL(stock_before,0), IFNULL(stock_after,0),
                   IFNULL(notes,''), IFNULL(user_name,'')
            FROM movements
            WHERE product_id = $pid AND IFNULL(operation,'') = $op
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$op", operation);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        return new MovRow(
            reader.GetString(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    private sealed record MovRow(
        string Type, double Qty, double Before, double After, string Notes, string User);
}
