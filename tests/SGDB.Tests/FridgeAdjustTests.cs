using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 65B — Ajuste físico da geladeira (stock_fridge), auditável, sem mexer no depósito.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class FridgeAdjustTests
{
    private static readonly DateTime ExpiryFar = DateTime.Today.AddDays(200);

    [Fact]
    public void AdjustFridge_Saldo_20To18_WritesPhysicalExit()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        var result = StockService.AdjustFridge(productId, StockAdjustMode.Saldo, newStock: 18, notes: "Quebra");

        AssertLocations(productId, 100, 18);
        Assert.Equal(118, Total(productId));
        Assert.Equal(20, result.StockBefore);
        Assert.Equal(18, result.StockAfter);
        Assert.Equal("saida", result.MovementType);
        Assert.Equal(2, result.Quantity);

        var mov = GetLatestMovement(productId, "ajuste_geladeira");
        Assert.Equal("saida", mov.Type);
        Assert.Equal(2, mov.Qty);
        Assert.Equal(120, mov.Before);
        Assert.Equal(118, mov.After);
        Assert.Contains("20", mov.Notes);
        Assert.Contains("18", mov.Notes);
        Assert.Contains("Quebra", mov.Notes);
        Assert.False(string.IsNullOrWhiteSpace(mov.User));
        Assert.Contains(mov.User, mov.Notes);
        Assert.Equal(2, GetCost(productId));
    }

    [Fact]
    public void AdjustFridge_Saldo_20To22_WritesPhysicalEntry()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        var result = StockService.AdjustFridge(productId, StockAdjustMode.Saldo, newStock: 22, notes: "Erro de contagem");

        AssertLocations(productId, 100, 22);
        Assert.Equal(122, Total(productId));
        Assert.Equal("entrada", result.MovementType);
        Assert.Equal(2, result.Quantity);

        var mov = GetLatestMovement(productId, "ajuste_geladeira");
        Assert.Equal("entrada", mov.Type);
        Assert.Equal(2, mov.Qty);
        Assert.Equal(120, mov.Before);
        Assert.Equal(122, mov.After);
    }

    [Fact]
    public void AdjustFridge_Entrada2_IncreasesFridgeOnly()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();
        var costBefore = GetCost(productId);

        StockService.AdjustFridge(productId, StockAdjustMode.Entrada, quantity: 2, notes: "Erro de contagem");

        AssertLocations(productId, 100, 22);
        Assert.Equal(costBefore, GetCost(productId));
        var mov = GetLatestMovement(productId, "ajuste_geladeira");
        Assert.Equal("entrada", mov.Type);
        Assert.Equal(2, mov.Qty);
        Assert.Equal(120, mov.Before);
        Assert.Equal(122, mov.After);
    }

    [Fact]
    public void AdjustFridge_Saida2_DecreasesFridgeOnly()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        StockService.AdjustFridge(productId, StockAdjustMode.Saida, quantity: 2, notes: "Quebra");

        AssertLocations(productId, 100, 18);
        var mov = GetLatestMovement(productId, "ajuste_geladeira");
        Assert.Equal("saida", mov.Type);
        Assert.Equal(2, mov.Qty);
        Assert.Equal(120, mov.Before);
        Assert.Equal(118, mov.After);
    }

    [Fact]
    public void AdjustFridge_AllModes_LeaveWarehouseIntact()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var a = Seed100_20("A65");
        var b = Seed100_20("B65");
        var c = Seed100_20("C65");

        StockService.AdjustFridge(a, StockAdjustMode.Entrada, quantity: 3, notes: "Erro de contagem");
        StockService.AdjustFridge(b, StockAdjustMode.Saida, quantity: 4, notes: "Perda");
        StockService.AdjustFridge(c, StockAdjustMode.Saldo, newStock: 11, notes: "Avaria");

        Assert.Equal(100, TestDataHelper.GetProductStock(a));
        Assert.Equal(100, TestDataHelper.GetProductStock(b));
        Assert.Equal(100, TestDataHelper.GetProductStock(c));
        Assert.Equal(23, TestDataHelper.GetProductFridge(a));
        Assert.Equal(16, TestDataHelper.GetProductFridge(b));
        Assert.Equal(11, TestDataHelper.GetProductFridge(c));
    }

    [Fact]
    public void AdjustFridge_SaidaGreaterThanFridge_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = TestDataHelper.SeedSimpleProduct(50, 5, 2, "S11", "SAIDA MAIOR");
        TestDataHelper.SetProductFridge(productId, 10);
        var movBefore = TestDataHelper.CountMovements(productId);

        var ex = Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Saida, quantity: 11, notes: "Quebra"));
        Assert.Contains("geladeira", ex.Message, StringComparison.OrdinalIgnoreCase);

        AssertLocations(productId, 50, 10);
        Assert.Equal(movBefore, TestDataHelper.CountMovements(productId));
        Assert.Equal(0, CountOperations(productId, "ajuste_geladeira"));
    }

    [Fact]
    public void AdjustFridge_NegativeQty_BlocksWithoutAbs()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Saida, quantity: -2, notes: "Quebra"));
        Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Entrada, quantity: -2, notes: "Quebra"));

        AssertLocations(productId, 100, 20);
        Assert.Equal(0, CountOperations(productId, "ajuste_geladeira"));
    }

    [Fact]
    public void AdjustFridge_NaNAndInfinity_Block()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Saida, quantity: double.NaN, notes: "Quebra"));
        Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Entrada, quantity: double.PositiveInfinity, notes: "Quebra"));
        Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Saldo, newStock: double.NegativeInfinity, notes: "Quebra"));
        Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Saldo, newStock: double.NaN, notes: "Quebra"));

        AssertLocations(productId, 100, 20);
        Assert.Equal(0, CountOperations(productId, "ajuste_geladeira"));
    }

    [Fact]
    public void AdjustFridge_NegativeSaldo_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        var ex = Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Saldo, newStock: -1, notes: "Quebra"));
        Assert.Contains("negativo", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertLocations(productId, 100, 20);
    }

    [Fact]
    public void AdjustFridge_EmptyReason_Blocks_FilledAllows()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        var empty = Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(productId, StockAdjustMode.Saida, quantity: 1, notes: "  "));
        Assert.Contains("motivo", empty.Message, StringComparison.OrdinalIgnoreCase);
        AssertLocations(productId, 100, 20);

        StockService.AdjustFridge(productId, StockAdjustMode.Saida, quantity: 1, notes: "Quebra");
        AssertLocations(productId, 100, 19);
        Assert.Equal(1, CountOperations(productId, "ajuste_geladeira"));
    }

    [Fact]
    public void AdjustFridge_SaldoUnchanged_DoesNotCreateMovement()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        var result = StockService.AdjustFridge(productId, StockAdjustMode.Saldo, newStock: 20, notes: null);
        Assert.Equal(0, result.Quantity);
        AssertLocations(productId, 100, 20);
        Assert.Equal(0, CountOperations(productId, "ajuste_geladeira"));
    }

    [Fact]
    public void AdjustFridge_DoesNotChangeLots()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "LOT65", "LOTE FRIDGE");
        CreateClosedPurchase(supplierId, productId, "LOTE FRIDGE", 20, "L65", ExpiryFar);
        StockService.TransferWarehouseToFridge(productId, 8);
        Assert.Equal(20, GetLotQty(productId, "L65"));

        StockService.AdjustFridge(productId, StockAdjustMode.Saldo, newStock: 6, notes: "Vencimento");

        AssertLocations(productId, 12, 6);
        Assert.Equal(20, GetLotQty(productId, "L65"));
        Assert.Equal(20, TestDataHelper.SumLots(productId));
    }

    [Fact]
    public void Transfer_StillNeutral_IsNotFridgeAdjust()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = TestDataHelper.SeedSimpleProduct(20, 5, 2, "TR65", "TRANSF");
        StockService.TransferWarehouseToFridge(productId, 8);
        StockService.TransferFridgeToWarehouse(productId, 3);

        AssertLocations(productId, 15, 5);
        Assert.Equal(20, Total(productId));
        Assert.Equal(0, CountOperations(productId, "ajuste_geladeira"));
        Assert.Equal(1, CountOperations(productId, "transferencia_geladeira"));
        Assert.Equal(1, CountOperations(productId, "retorno_geladeira"));
    }

    [Fact]
    public void StockIo_FridgeAdjust_IsPhysicalIo_TransferRemainsTransferOnly()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var outId = Seed100_20("IOOUT");
        var inId = Seed100_20("IOIN");
        var trId = TestDataHelper.SeedSimpleProduct(20, 5, 2, "IOTR", "IO TRANSF");

        StockService.AdjustFridge(outId, StockAdjustMode.Saida, quantity: 2, notes: "Quebra");
        StockService.AdjustFridge(inId, StockAdjustMode.Entrada, quantity: 2, notes: "Erro de contagem");
        StockService.TransferWarehouseToFridge(trId, 5);

        var report = StockIoService.List(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));
        var saida = Assert.Single(report.Rows.Where(r =>
            r.ProductId == outId
            && r.Operation.Contains("Ajuste da geladeira", StringComparison.OrdinalIgnoreCase)));
        Assert.False(saida.IsEntry);
        Assert.Equal(2, saida.Quantity);
        Assert.Equal(120, saida.StockBefore);
        Assert.Equal(118, saida.StockAfter);

        var entrada = Assert.Single(report.Rows.Where(r =>
            r.ProductId == inId
            && r.Operation.Contains("Ajuste da geladeira", StringComparison.OrdinalIgnoreCase)));
        Assert.True(entrada.IsEntry);
        Assert.Equal(2, entrada.Quantity);

        var transf = Assert.Single(report.Rows.Where(r =>
            r.ProductId == trId
            && r.Operation.Contains("Transferência", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(5, transf.Quantity);

        Assert.Equal(2, report.TotalEntradas);
        Assert.Equal(2, report.TotalSaidas);
    }

    [Fact]
    public void Purchase_FridgeLossThenReturn_CancelBlocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "P65", "PERDA FRIDGE");
        var purchaseId = CreateClosedPurchase(supplierId, productId, "PERDA FRIDGE", 20, "P65L", ExpiryFar);
        AssertLocations(productId, 20, 0);

        StockService.TransferWarehouseToFridge(productId, 15);
        AssertLocations(productId, 5, 15);

        StockService.AdjustFridge(productId, StockAdjustMode.Saldo, newStock: 13, notes: "Quebra");
        AssertLocations(productId, 5, 13);
        Assert.Equal(18, Total(productId));
        Assert.Equal(20, GetLotQty(productId, "P65L"));

        StockService.TransferFridgeToWarehouse(productId, 13);
        AssertLocations(productId, 18, 0);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("estoque", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        AssertLocations(productId, 18, 0);
        Assert.Equal(20, GetLotQty(productId, "P65L"));
        Assert.Equal(0, CountPurchaseMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void AdjustFridge_FailureBeforeMovement_RollsBack()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();
        var movBefore = TestDataHelper.CountMovements(productId);

        try
        {
            StockService.TestBeforeFridgeAdjustMovement = _ =>
                throw new InvalidOperationException("falha controlada no ajuste geladeira");
            var ex = Assert.Throws<InvalidOperationException>(
                () => StockService.AdjustFridge(productId, StockAdjustMode.Saida, quantity: 2, notes: "Quebra"));
            Assert.Contains("falha controlada", ex.Message);
        }
        finally
        {
            StockService.TestBeforeFridgeAdjustMovement = null;
        }

        AssertLocations(productId, 100, 20);
        Assert.Equal(movBefore, TestDataHelper.CountMovements(productId));
        Assert.Equal(0, CountOperations(productId, "ajuste_geladeira"));
    }

    [Fact]
    public void Adjust_Warehouse_StillWorks_LeavesFridgeIntact()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        var result = StockService.Adjust(productId, StockAdjustMode.Saida, quantity: 5, notes: "Correção");

        AssertLocations(productId, 95, 20);
        Assert.Equal(100, result.StockBefore);
        Assert.Equal(95, result.StockAfter);
        Assert.Equal(0, CountOperations(productId, "ajuste_geladeira"));
        Assert.Equal(1, CountOperations(productId, "saida_manual"));
    }

    [Fact]
    public void AdjustFridgeLocal_Client_DoesNotMutateLocalDb()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();
        var movBefore = TestDataHelper.CountMovements(productId);

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.Throws<StoreNetworkClientBlockedException>(() =>
                StockService.AdjustFridgeLocal(productId, StockAdjustMode.Saida, quantity: 2, notes: "Quebra"));
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        AssertLocations(productId, 100, 20);
        Assert.Equal(movBefore, TestDataHelper.CountMovements(productId));
    }

    [Fact]
    public void AdjustLocal_Client_StillBlocked_WarehousePathUnchanged()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var productId = Seed100_20();

        try
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
            Assert.Throws<StoreNetworkClientBlockedException>(() =>
                StockService.AdjustLocal(productId, StockAdjustMode.Saida, quantity: 2, notes: "Correção"));
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }

        AssertLocations(productId, 100, 20);
    }

    [Fact]
    public void AdjustFridge_MissingProduct_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var ex = Assert.Throws<InvalidOperationException>(
            () => StockService.AdjustFridge(999_999, StockAdjustMode.Saida, quantity: 1, notes: "Quebra"));
        Assert.Contains("não encontrado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static int Seed100_20(string code = "FR65")
    {
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, code, "GEL 65B");
        TestDataHelper.SetProductFridge(id, 20);
        return id;
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN 65B', 1, '{"ativo":true,"fornecedores":true}');
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
            Number = "NF-65B",
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
        Assert.Equal(fridge, TestDataHelper.GetProductFridge(productId));
    }

    private static double Total(int productId) =>
        TestDataHelper.GetProductStock(productId) + TestDataHelper.GetProductFridge(productId);

    private static double GetCost(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(cost_price,0) FROM products WHERE id = $id;";
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

    private static int CountPurchaseMovements(int productId, string operation, int purchaseId)
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
