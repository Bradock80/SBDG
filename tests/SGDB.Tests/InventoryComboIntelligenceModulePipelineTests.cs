using System.Globalization;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71A-B7 — loader TEMP. Confirma query 9/10 e presentation sem query extra.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryComboIntelligenceModulePipelineTests
{
    static readonly DateTime Today = new(2026, 9, 3);

    [Fact]
    public void Loader_sem_target_query_9()
    {
        using var db = Begin();
        TestDataHelper.SeedSimpleProduct(20, 10, 6, "NT1", "Sem tese");
        var snap = InventoryComboIntelligenceLoader.LoadSnapshot(Today);
        var presented = InventoryComboPresentation.Apply(snap);
        Assert.Equal(9, snap.QueryCount);
        Assert.Equal(9, presented.QueryCount);
        Assert.Equal(0, InventoryComboIntelligenceUi.ExpectedQueryCount);
        Assert.Empty(presented.Targets);
        Assert.Equal(InventoryComboPresentation.EmptySnapshotMessage, presented.EmptySnapshotMessage);
        var counts = InventoryComboIntelligenceUi.CountCards(presented.Targets);
        Assert.Equal(0, counts.NeedTurnover);
        Assert.Equal(0, counts.Combinations);
    }

    [Fact]
    public void Loader_com_target_query_10_e_ui_preserva_ordem()
    {
        using var db = Begin();
        var t1 = SeedExcessTarget("TX", "Alvo excesso com nome bem longo para tooltip");
        var a1 = SeedHealthy("A1", "Ancora Observed", stock: 44);
        var a2 = SeedHealthy("A2", "Ancora Weak", stock: 36);
        SeedControlledHistory(t1, a1, a2);
        var snap = InventoryComboIntelligenceLoader.LoadSnapshot(Today);
        var presented = InventoryComboPresentation.Apply(snap);
        Assert.Equal(10, snap.QueryCount);
        Assert.Equal(10, presented.QueryCount);
        Assert.True(presented.ByProductId.ContainsKey(t1));
        var rows = InventoryComboIntelligenceUi.Apply(presented, InventoryComboUiFilter.Cleared());
        var target = Assert.Single(rows, r => r.ProductId == t1);
        Assert.Equal(
            presented.ByProductId[t1].Suggestions.Select(s => s.AnchorProductId).ToArray(),
            target.Suggestions.Select(s => s.AnchorProductId).ToArray());
        Assert.Contains(InventoryComboPresentation.DisclaimerText, presented.DisclaimerText);
    }

    static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        InventoryCommercialMarginSettingsService.Save(20m);
        return db;
    }

    static int SeedExcessTarget(string code, string name)
    {
        var id = TestDataHelper.SeedSimpleProduct(120, 10, 6, code, name);
        StampInbound(id, Today.AddDays(-90));
        return id;
    }

    static int SeedHealthy(string code, string name, double stock = 40)
    {
        var id = TestDataHelper.SeedSimpleProduct(stock, 10, 6, code, name);
        StampInbound(id, Today.AddDays(-90));
        InsertLot(id, stock, Today.AddDays(180));
        for (var i = 0; i < 30; i++)
            InsertSale(Today.AddDays(-i), (id, 2));
        return id;
    }

    static void SeedControlledHistory(int target, int a1, int a2)
    {
        for (var i = 0; i < 4; i++)
            InsertSale(Today.AddDays(-i), (target, 2), (a1, 2));
        for (var i = 4; i < 6; i++)
            InsertSale(Today.AddDays(-i), (target, 2), (a2, 2));
        for (var i = 6; i < 30; i++)
            InsertSale(Today.AddDays(-i), (target, 2));
    }

    static int InsertLot(int productId, double quantity, DateTime expiry)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO product_lots (product_id, lot_number, expiry_date, quantity, unit_cost)
            VALUES ($p, $l, $e, $q, $c);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.Parameters.AddWithValue("$l", "L" + productId);
        cmd.Parameters.AddWithValue("$e", expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$q", quantity);
        cmd.Parameters.AddWithValue("$c", 6);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void StampInbound(int productId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at, operation
            ) VALUES (
              $pid, 'entrada', 1, 0, '71a inbound', $at, 'entrada_compra'
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$at", date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    static int InsertSale(DateTime sessionDate, params (int ProductId, double Qty)[] items)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        int saleId;
        using (var sale = conn.CreateCommand())
        {
            sale.Transaction = tx;
            sale.CommandText = """
                INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
                VALUES ($d, $total, 'Dinheiro', 0, $created);
                SELECT last_insert_rowid();
                """;
            sale.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sale.Parameters.AddWithValue("$total", items.Sum(i => i.Qty * 10));
            sale.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
            saleId = Convert.ToInt32(sale.ExecuteScalar());
        }

        foreach (var item in items)
        {
            using var line = conn.CreateCommand();
            line.Transaction = tx;
            line.CommandText = """
                INSERT INTO sale_items (
                  sale_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, stock_qty
                ) VALUES ($sale, $pid, 'SKU', 'Item', 'UN', $qty, 10, $sub, 0);
                """;
            line.Parameters.AddWithValue("$sale", saleId);
            line.Parameters.AddWithValue("$pid", item.ProductId);
            line.Parameters.AddWithValue("$qty", item.Qty);
            line.Parameters.AddWithValue("$sub", item.Qty * 10);
            line.ExecuteNonQuery();
        }

        tx.Commit();
        return saleId;
    }
}
