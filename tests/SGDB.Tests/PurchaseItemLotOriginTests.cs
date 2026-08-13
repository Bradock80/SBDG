using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 61C — Persistência da origem exata dos lotes da compra.
/// Não altera o cancelamento (FEFO permanece).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseItemLotOriginTests
{
    private static readonly DateTime ExpiryX = DateTime.Today.AddDays(120);
    private static readonly DateTime ExpiryNear = DateTime.Today.AddDays(30);

    [Fact]
    public void Schema_CriaTabelaPurchaseItemLots_ComColunasEIndices()
    {
        using var db = TempDatabase.Create();
        using var conn = DatabaseService.OpenConnection();

        Assert.True(TableExists(conn, "purchase_item_lots"));
        var cols = GetColumns(conn, "purchase_item_lots");
        Assert.Contains("id", cols);
        Assert.Contains("purchase_item_id", cols);
        Assert.Contains("purchase_id", cols);
        Assert.Contains("product_id", cols);
        Assert.Contains("lot_number", cols);
        Assert.Contains("expiry_date", cols);
        Assert.Contains("quantity", cols);
        Assert.Contains("product_lot_id", cols);
        Assert.Contains("created_at", cols);

        var indexes = ListIndexNames(conn, "purchase_item_lots");
        Assert.Contains("idx_purchase_item_lots_purchase", indexes);
        Assert.Contains("idx_purchase_item_lots_item", indexes);
        Assert.Contains("idx_purchase_item_lots_product", indexes);
    }

    [Fact]
    public void Schema_InitializeDuasVezes_Idempotente()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SGDB.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.db");
        try
        {
            DatabaseService.Initialize(path);
            using (var conn = DatabaseService.OpenConnection())
                Assert.True(TableExists(conn, "purchase_item_lots"));

            DatabaseService.Initialize(path);
            using (var conn = DatabaseService.OpenConnection())
            {
                Assert.True(TableExists(conn, "purchase_item_lots"));
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM purchase_item_lots;";
                Assert.Equal(0L, (long)(cmd.ExecuteScalar() ?? -1));
            }
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void Schema_BancoAntigoSemTabela_AbreECriaPurchaseItemLots()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SGDB.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "old.db");
        try
        {
            DatabaseService.Initialize(path);
            using (var conn = DatabaseService.OpenConnection())
            using (var drop = conn.CreateCommand())
            {
                drop.CommandText = "DROP TABLE IF EXISTS purchase_item_lots;";
                drop.ExecuteNonQuery();
                Assert.False(TableExists(conn, "purchase_item_lots"));
            }

            DatabaseService.Initialize(path);
            using var opened = DatabaseService.OpenConnection();
            Assert.True(TableExists(opened, "purchase_item_lots"));
            using var select = opened.CreateCommand();
            select.CommandText = "SELECT COUNT(*) FROM purchase_item_lots;";
            Assert.Equal(0L, (long)(select.ExecuteScalar() ?? -1));
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void PurchaseWithLot_PersistsExactOrigin_AndProductLot()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "P1", "COM LOTE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "COM LOTE", 20, "B", ExpiryX);

        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal(20, GetLotQty(productId, "B"));
        Assert.Equal(1, CountLotRows(productId));

        var origins = PurchaseService.ListPurchaseItemLots(purchaseId);
        var row = Assert.Single(origins);
        var itemId = GetSinglePurchaseItemId(purchaseId);
        Assert.Equal(itemId, row.PurchaseItemId);
        Assert.Equal(purchaseId, row.PurchaseId);
        Assert.Equal(productId, row.ProductId);
        Assert.Equal(20, row.Quantity);
        Assert.Equal("B", row.LotNumber);
        Assert.Equal(ExpiryX.Date, row.ExpiryDate);
        Assert.True(row.ProductLotId is > 0);
        Assert.Equal(GetLotId(productId, "B"), row.ProductLotId);
    }

    [Fact]
    public void TwoPurchasesSameLot_MergeInProductLots_KeepIndependentOrigins()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MRG", "MERGE");

        var p1 = CreateClosedPurchase(supplierId, productId, "MERGE", 10, "ABC", ExpiryX, number: "NF-1");
        var p2 = CreateClosedPurchase(supplierId, productId, "MERGE", 20, "ABC", ExpiryX, number: "NF-2");

        Assert.Equal(30, TestDataHelper.GetProductStock(productId));
        Assert.Equal(1, CountLotRows(productId));
        Assert.Equal(30, GetLotQty(productId, "ABC"));
        var mergedLotId = GetLotId(productId, "ABC");

        var o1 = Assert.Single(PurchaseService.ListPurchaseItemLots(p1));
        var o2 = Assert.Single(PurchaseService.ListPurchaseItemLots(p2));
        Assert.Equal(10, o1.Quantity);
        Assert.Equal(20, o2.Quantity);
        Assert.Equal(p1, o1.PurchaseId);
        Assert.Equal(p2, o2.PurchaseId);
        Assert.NotEqual(o1.PurchaseItemId, o2.PurchaseItemId);
        Assert.Equal("ABC", o1.LotNumber);
        Assert.Equal("ABC", o2.LotNumber);
        Assert.Equal(mergedLotId, o1.ProductLotId);
        Assert.Equal(mergedLotId, o2.ProductLotId);
    }

    [Fact]
    public void PurchaseWithoutLot_DoesNotCreateOriginRow()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(100, 5, 2, "NL", "SEM LOTE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SEM LOTE", 20, lot: null, expiry: null);

        Assert.Equal(120, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountLotRows(productId));
        Assert.Empty(PurchaseService.ListPurchaseItemLots(purchaseId));
    }

    [Fact]
    public void Purchase_ExpiryWithoutLotNumber_PersistsOrigin()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "EXP", "SO VALIDADE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SO VALIDADE", 15, lot: "", expiry: ExpiryX);

        var row = Assert.Single(PurchaseService.ListPurchaseItemLots(purchaseId));
        Assert.Equal("", row.LotNumber);
        Assert.Equal(ExpiryX.Date, row.ExpiryDate);
        Assert.Equal(15, row.Quantity);
        Assert.Equal(productId, row.ProductId);
        Assert.True(row.ProductLotId is > 0);
    }

    [Fact]
    public void Purchase_LotWithoutExpiry_PersistsOrigin()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "NV", "SEM VAL");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SEM VAL", 10, "SEMVAL", expiry: null);

        var row = Assert.Single(PurchaseService.ListPurchaseItemLots(purchaseId));
        Assert.Equal("SEMVAL", row.LotNumber);
        Assert.Null(row.ExpiryDate);
        Assert.Equal(10, row.Quantity);
        Assert.True(row.ProductLotId is > 0);
    }

    [Fact]
    public void Purchase_CigarettePhysicalQty_OriginUsesFortyNotPacks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var cigId = SeedCigarette(stock: 0, fator: 20);

        var purchaseId = CreateClosedPurchase(
            supplierId, cigId, "CIGARRO 61C", qty: 40, lot: "C1", expiry: ExpiryX, number: "NF-CIG");

        Assert.Equal(40, TestDataHelper.GetProductStock(cigId));
        var row = Assert.Single(PurchaseService.ListPurchaseItemLots(purchaseId));
        Assert.Equal(40, row.Quantity);
        Assert.Equal("C1", row.LotNumber);
    }

    [Fact]
    public void Purchase_NfeKeyWithLot_UsesSamePurchaseServicePath()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "NFE", "NFE LOTE");

        var purchaseId = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "123",
            NfeKey = "35200112345678901234567890123456789012345678",
            GerarEstoque = true,
            Notes = "Importado via XML NF-e",
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = productId,
                    ProductName = "NFE LOTE",
                    Quantity = 8,
                    UnitPrice = 2,
                    LotNumber = "XML1",
                    ExpiryDate = ExpiryX,
                },
            ],
        }, closeOnSave: true);

        var row = Assert.Single(PurchaseService.ListPurchaseItemLots(purchaseId));
        Assert.Equal(8, row.Quantity);
        Assert.Equal("XML1", row.LotNumber);
        Assert.Equal(1, CountMovements(productId, "entrada_nfe", purchaseId));
    }

    [Fact]
    public void SamePurchaseItem_TableAllowsMultipleOriginRows()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MLT", "MULTI LOTE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "MULTI LOTE", 5, "A", ExpiryX);
        var itemId = GetSinglePurchaseItemId(purchaseId);
        var lotId = GetLotId(productId, "A");

        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO purchase_item_lots (
                    purchase_item_id, purchase_id, product_id, lot_number, expiry_date,
                    quantity, product_lot_id, created_at
                ) VALUES ($item, $purchase, $product, 'B', $exp, 3, $lotid, datetime('now','localtime'));
                """;
            cmd.Parameters.AddWithValue("$item", itemId);
            cmd.Parameters.AddWithValue("$purchase", purchaseId);
            cmd.Parameters.AddWithValue("$product", productId);
            cmd.Parameters.AddWithValue("$exp", ExpiryX.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$lotid", lotId);
            cmd.ExecuteNonQuery();
        }

        var byItem = PurchaseService.ListPurchaseItemLotsByItem(itemId);
        Assert.Equal(2, byItem.Count);
        Assert.Contains(byItem, r => r.LotNumber == "A" && r.Quantity == 5);
        Assert.Contains(byItem, r => r.LotNumber == "B" && r.Quantity == 3);
    }

    [Fact]
    public void InsertOriginFailure_RollsBackPurchaseStockMovementAndLot()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(50, 5, 2, "RB", "ROLLBACK");

        try
        {
            PurchaseService.TestBeforeInsertPurchaseItemLot = () =>
                throw new InvalidOperationException("falha controlada na rastreabilidade");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                CreateClosedPurchase(supplierId, productId, "ROLLBACK", 12, "R1", ExpiryX, number: "NF-RB"));
            Assert.Contains("falha controlada", ex.Message);
        }
        finally
        {
            PurchaseService.TestBeforeInsertPurchaseItemLot = null;
        }

        Assert.Equal(50, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountLotRows(productId));
        Assert.Equal(0, CountPurchases());
        Assert.Equal(0, CountPurchaseItems());
        Assert.Equal(0, CountPurchaseItemLots());
        Assert.Equal(0, CountMovements(productId, "entrada_compra", purchaseId: 0));
    }

    [Fact]
    public void CancelPurchase_StillUsesDeductFefo_OriginRowsRemain()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2, "FEFO", "FEFO ESTORNO");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 10,
            LotNumber = "A",
            ExpiryDate = ExpiryNear,
        });

        var purchaseB = CreateClosedPurchase(supplierId, productId, "FEFO ESTORNO", 20, "B", ExpiryX, number: "NF-B");
        Assert.Single(PurchaseService.ListPurchaseItemLots(purchaseB));

        PurchaseService.Cancel(purchaseB);

        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetLotQty(productId, "A"));
        Assert.Equal(10, GetLotQty(productId, "B"));
        // Origem da compra B permanece (61C não muda estorno).
        var origin = Assert.Single(PurchaseService.ListPurchaseItemLots(purchaseB));
        Assert.Equal(20, origin.Quantity);
        Assert.Equal("B", origin.LotNumber);
    }

    [Fact]
    public void OldPurchase_BeforeOriginRows_ListIsEmpty()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "OLD", "ANTIGA");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "ANTIGA", 9, "L", ExpiryX);
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM purchase_item_lots WHERE purchase_id = $id;";
            cmd.Parameters.AddWithValue("$id", purchaseId);
            cmd.ExecuteNonQuery();
        }

        Assert.Empty(PurchaseService.ListPurchaseItemLots(purchaseId));
        Assert.Equal(9, GetLotQty(productId, "L"));
    }

    // --- helpers ---

    private static int CreateClosedPurchase(
        int supplierId,
        int productId,
        string name,
        double qty,
        string? lot,
        DateTime? expiry,
        string number = "NF-61C",
        double unitPrice = 2)
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
                    UnitPrice = unitPrice,
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
            VALUES ('fornecedor', 'juridica', 'FORN 61C', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCigarette(double stock, double fator)
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
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIG61C', 'CIGARRO 61C', 'CIGARROS', 'UN', 28.5, $stock, 20, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetSinglePurchaseItemId(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM purchase_items WHERE purchase_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountLotRows(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM product_lots WHERE product_id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double GetLotQty(int productId, string lot)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(quantity),0) FROM product_lots
            WHERE product_id = $id AND IFNULL(lot_number,'') = $lot;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.Parameters.AddWithValue("$lot", lot);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static int GetLotId(int productId, string lot)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id FROM product_lots
            WHERE product_id = $id AND IFNULL(lot_number,'') = $lot
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.Parameters.AddWithValue("$lot", lot);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountMovements(int productId, string operation, int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE product_id = $pid AND IFNULL(operation,'') = $op
              AND ($rid = 0 OR (IFNULL(ref_type,'') = 'purchase' AND ref_id = $rid));
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$op", operation);
        cmd.Parameters.AddWithValue("$rid", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountPurchases()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM purchases;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountPurchaseItems()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM purchase_items;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountPurchaseItemLots()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM purchase_item_lots;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM sqlite_master
            WHERE type = 'table' AND name = $name
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$name", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static HashSet<string> GetColumns(SqliteConnection conn, string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(1));
        return set;
    }

    private static HashSet<string> ListIndexNames(SqliteConnection conn, string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                set.Add(reader.GetString(0));
        }
        return set;
    }

    private static void CleanupDir(string dir)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            /* ignore cleanup races on Windows */
        }
    }
}
