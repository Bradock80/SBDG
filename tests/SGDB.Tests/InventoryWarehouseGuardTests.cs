using SGDB.Domain.Inventory;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 64B — Inventário continua sendo SOMENTE o depósito.
/// Geladeira é informativa; F9 não altera stock_fridge.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryWarehouseGuardTests
{
    [Fact]
    public void CreateSession_TheoreticalUsaSoStock_FridgeForaDoSnapshot()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedWarehouse(100, fridge: 20, fridgeMin: 12);

        var session = InventoryService.CreateSession();
        var item = Item(session.Id, id);

        Assert.Equal(100, item.TheoreticalQty);
        Assert.Equal(20, item.StockFridge);
        Assert.Equal(12, item.StockFridgeMin);
        Assert.Equal(100, item.CurrentStock);
        Assert.True(item.UsesFridge);
        Assert.Equal(120, item.StoreTotalCurrent);
        Assert.DoesNotContain("120", item.TheoreticalDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Consolidate_Count98_PreservaFridge_Total118()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedWarehouse(100, fridge: 20, fridgeMin: 12);
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 98);

        var result = InventoryService.Consolidate(session.Id);

        Assert.True(result.AdjustedCount >= 1);
        Assert.Equal(98, TestDataHelper.GetProductStock(id));
        Assert.Equal(20, TestDataHelper.GetProductFridge(id));
        Assert.Equal(118, TestDataHelper.GetProductStock(id) + TestDataHelper.GetProductFridge(id));
    }

    [Fact]
    public void Consolidate_Count120_MotorAplicaNoDeposito_FridgeIntocada()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedWarehouse(100, fridge: 20, fridgeMin: 12);
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 120);

        InventoryService.Consolidate(session.Id);

        Assert.Equal(120, TestDataHelper.GetProductStock(id));
        Assert.Equal(20, TestDataHelper.GetProductFridge(id));
        Assert.Equal(140, TestDataHelper.GetProductStock(id) + TestDataHelper.GetProductFridge(id));
    }

    [Fact]
    public void ListItems_ExpoeFridgeEHintDeDeposito()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var withFridge = SeedWarehouse(100, fridge: 20, fridgeMin: 12, code: "G1", name: "COM FRIDGE");
        var noFridge = SeedWarehouse(40, fridge: 0, fridgeMin: 0, code: "S1", name: "SEM FRIDGE");
        var session = InventoryService.CreateSession();

        var fridgeItem = Item(session.Id, withFridge);
        Assert.Equal(20, fridgeItem.StockFridge);
        Assert.NotEqual("—", fridgeItem.FridgeDisplay);
        Assert.Contains("Depósito teórico: 100", fridgeItem.WarehouseHint, StringComparison.Ordinal);
        Assert.Contains("Geladeira atual: 20", fridgeItem.WarehouseHint, StringComparison.Ordinal);
        Assert.Contains("Total atual: 120", fridgeItem.WarehouseHint, StringComparison.Ordinal);
        Assert.Null(fridgeItem.Difference);

        var plain = Item(session.Id, noFridge);
        Assert.False(plain.UsesFridge);
        Assert.Equal("—", plain.FridgeDisplay);
        Assert.Equal("Depósito teórico: 40", plain.WarehouseHint);
        Assert.DoesNotContain("Geladeira", plain.WarehouseHint, StringComparison.Ordinal);
    }

    [Fact]
    public void Difference_ContinuaCountedMenosDepositoTeorico()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedWarehouse(100, fridge: 20, fridgeMin: 12);
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 98);

        var item = Item(session.Id, id);
        Assert.Equal(-2, item.Difference);
        Assert.NotEqual(-22, item.Difference);
    }

    [Fact]
    public void TransferAfterCount_ContinuaBloqueandoF9()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedWarehouse(100, fridge: 0, fridgeMin: 12);
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);
        WaitPastSqliteSecond();
        StockService.TransferWarehouseToFridge(id, 10);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains(ex.Conflicts, c => c.ProductId == id && c.HasMovementSinceCount);
        Assert.Equal(90, TestDataHelper.GetProductStock(id));
        Assert.Equal(10, TestDataHelper.GetProductFridge(id));
    }

    [Fact]
    public void TransferBeforeCount_RecontagemConsolidaDeposito_PreservaFridge()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedWarehouse(100, fridge: 20, fridgeMin: 12);
        var session = InventoryService.CreateSession();
        StockService.TransferWarehouseToFridge(id, 10);
        Assert.Equal(90, TestDataHelper.GetProductStock(id));
        Assert.Equal(30, TestDataHelper.GetProductFridge(id));

        var itemId = ItemId(session.Id, id);
        InventoryService.SetCounted(itemId, 88);
        var listed = Item(session.Id, id);
        Assert.Equal(100, listed.TheoreticalQty);
        Assert.Equal(90, listed.CountBaselineQty);
        Assert.Equal(30, listed.StockFridge);
        Assert.Equal(120, listed.StoreTotalCurrent);

        var result = InventoryService.Consolidate(session.Id);
        Assert.True(result.AdjustedCount >= 1);
        Assert.Equal(88, TestDataHelper.GetProductStock(id));
        Assert.Equal(30, TestDataHelper.GetProductFridge(id));
    }

    [Fact]
    public void CigarroFisico_ContagemETeoricoSaoDeposito_FridgeNaoSoma()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var cigId = SeedCigarette(stock: 100, fator: 20, fridge: 20, fridgeMin: 12);
        var physical = InventoryPhysicalQuantityCalculator.Calculate(3, 7, 20);
        Assert.Equal(67, physical);

        var session = InventoryService.CreateSession();
        var item = Item(session.Id, cigId);
        Assert.Equal(100, item.TheoreticalQty);
        Assert.Equal(20, item.StockFridge);
        Assert.Equal(120, item.StoreTotalCurrent);
        Assert.Contains("Geladeira atual: 20", item.WarehouseHint, StringComparison.Ordinal);
        Assert.Equal(67, physical);

        InventoryService.SetCounted(item.Id, physical);
        InventoryService.Consolidate(session.Id);

        Assert.Equal(67, TestDataHelper.GetProductStock(cigId));
        Assert.Equal(20, TestDataHelper.GetProductFridge(cigId));
    }

    private static int SeedWarehouse(
        double stock, double fridge, int fridgeMin,
        string code = "W64", string name = "PROD 64B")
    {
        var id = TestDataHelper.SeedSimpleProduct(stock, 5, 2, code, name);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE products
            SET stock_fridge = $fridge, stock_fridge_min = $min
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$fridge", fridge);
        cmd.Parameters.AddWithValue("$min", fridgeMin);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static int SeedCigarette(double stock, double fator, double fridge, int fridgeMin)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            PrecoAvulso = 1.5,
            PrecoAtacado = 28.5,
            QtdAtacado = fator,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active,
                extra_json, stock_fridge, stock_fridge_min
            ) VALUES (
                'CIG64B', 'CIGARRO 64B', 'CIGARROS', 'UN', 28.5, $stock, 20, 1,
                $extra, $fridge, $min
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        cmd.Parameters.AddWithValue("$fridge", fridge);
        cmd.Parameters.AddWithValue("$min", fridgeMin);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static InventoryItem Item(int sessionId, int productId) =>
        InventoryService.ListItems(sessionId).Single(i => i.ProductId == productId);

    private static int ItemId(int sessionId, int productId) => Item(sessionId, productId).Id;

    private static void WaitPastSqliteSecond() => Thread.Sleep(1100);
}
