using System.Globalization;
using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 70D-B2 — integração do motor de projeção com product_lots.
/// Bancos isolados. Sem UI. Sem deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryProjectionServiceTests
{
    private const double Tol = 0.0001;

    private static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    private static void SetProductCreated(int productId, DateTime date)
    {
        var utc = date.Date - DateBrHelper.BrazilOffset;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET created_at = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static void StampInbound(int productId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at, operation
            ) VALUES (
              $pid, 'entrada', 1, 0, '70d inbound', $at, 'entrada_compra'
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$at", date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static int InsertLegacySale(int productId, double quantity, DateTime sessionDate)
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
            sale.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd"));
            sale.Parameters.AddWithValue("$total", quantity * 10);
            sale.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
            saleId = Convert.ToInt32(sale.ExecuteScalar());
        }
        using (var item = conn.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                INSERT INTO sale_items (
                  sale_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, stock_qty
                ) VALUES ($sale, $pid, 'LEG', 'Legado', 'UN', $qty, 10, $sub, 0);
                """;
            item.Parameters.AddWithValue("$sale", saleId);
            item.Parameters.AddWithValue("$pid", productId);
            item.Parameters.AddWithValue("$qty", quantity);
            item.Parameters.AddWithValue("$sub", quantity * 10);
            item.ExecuteNonQuery();
        }
        tx.Commit();
        return saleId;
    }

    private static int SeedEligible(
        DateTime today,
        string code,
        double stock,
        double costPrice,
        double fridge = 0)
    {
        var id = TestDataHelper.SeedSimpleProduct(stock, 10, costPrice, code, code);
        if (fridge != 0)
            TestDataHelper.SetProductFridge(id, fridge);
        SetProductCreated(id, today.AddDays(-40));
        StampInbound(id, today.AddDays(-40));
        InsertLegacySale(id, 3, today);
        return id;
    }

    private static int InsertLot(
        int productId,
        double quantity,
        object? expiry,
        double unitCost = 0,
        string lotNumber = "L1")
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO product_lots (product_id, lot_number, expiry_date, quantity, unit_cost)
            VALUES ($p, $l, $e, $q, $c);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.Parameters.AddWithValue("$l", lotNumber);
        cmd.Parameters.AddWithValue("$e", expiry ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$q", quantity);
        cmd.Parameters.AddWithValue("$c", unitCost);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static InventoryProjectedProduct Require(InventoryProjectionSnapshot snap, int productId)
    {
        Assert.True(snap.ByProductId.ContainsKey(productId));
        return snap.ByProductId[productId];
    }

    [Fact]
    public void Intelligence_isolated_query_count_is_six()
    {
        using var db = Begin();
        var today = DateTime.Today;
        SeedEligible(today, "Q6", 50, 4);
        var snap = InventoryIntelligenceService.Load(today);
        Assert.Equal(6, snap.QueryCount);
        Assert.Equal(InventoryIntelligenceService.ExpectedQueryCount, snap.QueryCount);
    }

    [Fact]
    public void Composed_query_count_is_seven_and_independent_of_product_count()
    {
        using var db = Begin();
        var today = DateTime.Today;
        SeedEligible(today, "A", 50, 4);
        var one = InventoryProjectionService.Load(today);
        Assert.Equal(7, one.QueryCount);
        Assert.Equal(InventoryProjectionService.ExpectedQueryCount, one.QueryCount);
        Assert.Equal(6, one.Intelligence.QueryCount);

        SeedEligible(today, "B", 50, 4);
        SeedEligible(today, "C", 50, 4);
        var many = InventoryProjectionService.Load(today);
        Assert.Equal(7, many.QueryCount);
        Assert.Equal(6, many.Intelligence.QueryCount);
        Assert.True(many.Intelligence.Rows.Count >= 3);
        Assert.Equal(many.Intelligence.Rows.Count, many.ByProductId.Count);
    }

    [Fact]
    public void Today_is_the_snapshot_today_not_per_product_now()
    {
        using var db = Begin();
        var day = new DateTime(2024, 6, 15);
        SeedEligible(day, "DAY", 80, 4);
        var snap = InventoryProjectionService.Load(day);
        Assert.Equal(day, snap.Today);
        Assert.Equal(day, snap.Intelligence.Today);
        foreach (var row in snap.Intelligence.Rows)
            Assert.Equal(day, snap.Intelligence.Today);
    }

    [Fact]
    public void Product_without_lots_projects_empty_lots_and_untracked_warehouse()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "NONE", 40, 4);
        var snap = InventoryProjectionService.Load(today);
        var row = snap.Intelligence.Rows.Single(r => r.ProductId == id);
        var item = Require(snap, id);
        Assert.Empty(item.Projection.Lots);
        Assert.Empty(item.LotCosts);
        Assert.Equal(0, item.Projection.TrackedLotQuantity, Tol);
        Assert.Equal(row.Stock, item.Projection.UntrackedWarehouseQuantity, Tol);
        Assert.False(item.Projection.HasLotLocationLimitation);
    }

    [Fact]
    public void Dated_iso_lot_is_projected()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "ISO", 40, 4);
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 5);
        var item = Require(InventoryProjectionService.Load(today), id);
        Assert.True(item.Projection.CanProjectExpiry);
        var lot = Assert.Single(item.Projection.Lots);
        Assert.Equal(InventoryProjectionLotKind.Dated, lot.Kind);
        Assert.Equal(today.AddDays(10).Date, lot.ExpiryDate);
        Assert.Equal(5, Assert.Single(item.LotCosts).UsedCost);
        Assert.Equal(LotCostSource.LotRecorded, Assert.Single(item.LotCosts).CostSource);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_empty_or_spaces_expiry_is_undated(string? expiry)
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "UND", 40, 4);
        InsertLot(id, 40, expiry);
        var item = Require(InventoryProjectionService.Load(today), id);
        Assert.True(item.Projection.CanProjectExpiry);
        Assert.Equal(InventoryProjectionLotKind.Undated, Assert.Single(item.Projection.Lots).Kind);
        Assert.Null(Assert.Single(item.Projection.Lots).ExpiryDate);
        Assert.Null(Assert.Single(item.Projection.Lots).ProjectedSurplusAtExpiry);
    }

    [Theory]
    [InlineData("30/08/2026")]
    [InlineData("08/30/2026")]
    [InlineData("2026/08/30")]
    [InlineData("abc")]
    [InlineData("2026-99-99")]
    public void Invalid_expiry_blocks_only_that_product_expiry(string expiry)
    {
        using var db = Begin();
        var today = DateTime.Today;
        var good = SeedEligible(today, "OK", 40, 4);
        var bad = SeedEligible(today, "BAD", 40, 4);
        InsertLot(good, 40, today.AddDays(20).ToString("yyyy-MM-dd"), 2, "G");
        InsertLot(bad, 40, expiry, 2, "B");

        var snap = InventoryProjectionService.Load(today);
        var goodItem = Require(snap, good);
        var badItem = Require(snap, bad);

        Assert.True(goodItem.Projection.CanProjectExpiry);
        Assert.Equal(InventoryProjectionLotKind.Dated, Assert.Single(goodItem.Projection.Lots).Kind);

        Assert.True(badItem.Projection.CanProjectSku);
        Assert.False(badItem.Projection.CanProjectExpiry);
        Assert.Equal(
            InventoryExpiryProjectionBlockedReason.InvalidExpiryDate,
            badItem.Projection.ExpiryBlockedReason);
        Assert.Empty(badItem.Projection.Lots);
        Assert.NotEmpty(badItem.LotCosts);
    }

    [Fact]
    public void Multiple_lots_are_loaded_without_extra_queries()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "MULTI", 50, 4);
        InsertLot(id, 20, today.AddDays(5).ToString("yyyy-MM-dd"), 1, "A");
        InsertLot(id, 30, today.AddDays(20).ToString("yyyy-MM-dd"), 1, "B");
        var snap = InventoryProjectionService.Load(today);
        Assert.Equal(7, snap.QueryCount);
        var item = Require(snap, id);
        Assert.Equal(2, item.Projection.Lots.Count);
        Assert.Equal(2, item.LotCosts.Count);
    }

    [Fact]
    public void Lots_exceeding_warehouse_are_not_capped()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "OVER", 10, 4);
        InsertLot(id, 50, today.AddDays(10).ToString("yyyy-MM-dd"), 1);
        var item = Require(InventoryProjectionService.Load(today), id);
        Assert.True(item.Projection.CanProjectSku);
        Assert.Equal(
            InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse,
            item.Projection.ExpiryBlockedReason);
        Assert.Equal(50, item.Projection.TrackedLotQuantity, Tol);
    }

    [Fact]
    public void Lots_below_warehouse_keep_untracked_gap()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "GAP", 100, 4);
        InsertLot(id, 10, today.AddDays(10).ToString("yyyy-MM-dd"), 1);
        var row = InventoryIntelligenceService.Load(today).Rows.Single(r => r.ProductId == id);
        var item = Require(InventoryProjectionService.Load(today), id);
        Assert.True(item.Projection.CanProjectExpiry);
        Assert.Equal(10, item.Projection.TrackedLotQuantity, Tol);
        Assert.Equal(row.Stock - 10, item.Projection.UntrackedWarehouseQuantity, Tol);
        Assert.Single(item.Projection.Lots);
    }

    [Fact]
    public void Fridge_stock_sets_location_limitation_without_lot_inference()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "FRG", 40, 4, fridge: 7);
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 1);
        var snap = InventoryProjectionService.Load(today);
        var row = snap.Intelligence.Rows.Single(r => r.ProductId == id);
        var item = Require(snap, id);
        Assert.Equal(7, row.StockFridge, Tol);
        Assert.Equal(row.Stock + row.StockFridge, row.TotalStock, Tol);
        Assert.True(item.Projection.HasLotLocationLimitation);
        Assert.Single(item.Projection.Lots);
    }

    [Fact]
    public void Lot_cost_recorded_beats_product_cost()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "LC", 40, 8);
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 5);
        var cost = Assert.Single(Require(InventoryProjectionService.Load(today), id).LotCosts);
        Assert.Equal(5, cost.UsedCost);
        Assert.Equal(LotCostSource.LotRecorded, cost.CostSource);
    }

    [Fact]
    public void Zero_lot_cost_falls_back_to_product_as_estimate()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "FB", 40, 8);
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 0);
        var cost = Assert.Single(Require(InventoryProjectionService.Load(today), id).LotCosts);
        Assert.Equal(8, cost.UsedCost);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, cost.CostSource);
    }

    [Fact]
    public void Missing_cost_is_unavailable()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "NC", 40, 0);
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 0);
        var item = Require(InventoryProjectionService.Load(today), id);
        var cost = Assert.Single(item.LotCosts);
        Assert.Null(cost.UsedCost);
        Assert.Equal(LotCostSource.Unavailable, cost.CostSource);
        Assert.Null(Assert.Single(item.Projection.Lots).ProjectedSurplusValue);
    }

    [Fact]
    public void Cost_threshold_0_009_uses_product_estimate()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "TH0", 40, 8);
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 0.009);
        var cost = Assert.Single(Require(InventoryProjectionService.Load(today), id).LotCosts);
        Assert.Equal(8, cost.UsedCost);
        Assert.Equal(LotCostSource.CurrentAverageEstimate, cost.CostSource);
    }

    [Fact]
    public void Cost_threshold_just_above_0_009_uses_lot()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "TH1", 40, 8);
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 0.0090001);
        var cost = Assert.Single(Require(InventoryProjectionService.Load(today), id).LotCosts);
        Assert.Equal(0.0090001, cost.UsedCost);
        Assert.Equal(LotCostSource.LotRecorded, cost.CostSource);
    }

    [Fact]
    public void Negative_lot_quantity_is_not_hidden()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "NEG", 40, 4);
        InsertLot(id, -5, today.AddDays(10).ToString("yyyy-MM-dd"), 1);
        var item = Require(InventoryProjectionService.Load(today), id);
        Assert.True(item.Projection.CanProjectSku);
        Assert.Equal(
            InventoryExpiryProjectionBlockedReason.InvalidLotQuantity,
            item.Projection.ExpiryBlockedReason);
    }

    [Fact]
    public void Kit_copies_composition_flag_and_blocks()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var component = SeedEligible(today, "CMP", 10, 1);
        var extra = new ProductExtra
        {
            Composicao = true,
            ComposicaoItens =
            [
                new ProductCompositionItem
                {
                    ProductId = component,
                    Quantity = 1,
                    Code = "CMP",
                    Name = "Componente",
                    Unit = "UN",
                },
            ],
        }.ToJson();

        int kitId;
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO products (
                    code, name, unit, sale_price, stock, cost_price, active, extra_json
                ) VALUES ('KIT', 'Kit', 'UN', 20, 5, 0, 1, $extra);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$extra", extra);
            kitId = Convert.ToInt32(cmd.ExecuteScalar());
        }
        SetProductCreated(kitId, today.AddDays(-40));
        StampInbound(kitId, today.AddDays(-40));
        InsertLegacySale(kitId, 1, today);

        var snap = InventoryProjectionService.Load(today);
        var row = snap.Intelligence.Rows.Single(r => r.ProductId == kitId);
        Assert.True(row.IsCompositionProduct);
        var item = Require(snap, kitId);
        Assert.Equal(InventorySkuProjectionBlockedReason.CompositionProduct, item.Projection.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.CompositionProduct, item.Projection.ExpiryBlockedReason);
    }

    [Fact]
    public void History_days_29_blocks_from_row_not_recalculated()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = TestDataHelper.SeedSimpleProduct(40, 10, 4, "H29", "H29");
        SetProductCreated(id, today.AddDays(-28));
        StampInbound(id, today.AddDays(-28));
        InsertLegacySale(id, 3, today);
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 1);

        var snap = InventoryProjectionService.Load(today);
        var row = snap.Intelligence.Rows.Single(r => r.ProductId == id);
        Assert.Equal(29, row.HistoryDays);
        Assert.True(row.IsHistoryInsufficient30);
        var item = Require(snap, id);
        Assert.Equal(InventorySkuProjectionBlockedReason.InsufficientHistory, item.Projection.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.InsufficientHistory, item.Projection.ExpiryBlockedReason);
    }

    [Fact]
    public void No_physical_evidence_is_copied_from_row()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = TestDataHelper.SeedSimpleProduct(40, 10, 4, "NEV", "Sem evidência");
        SetProductCreated(id, today.AddDays(-40));
        InsertLot(id, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 1);
        var snap = InventoryProjectionService.Load(today);
        var row = snap.Intelligence.Rows.Single(r => r.ProductId == id);
        Assert.False(row.HasPhysicalAvailabilityEvidence);
        var item = Require(snap, id);
        Assert.Equal(InventorySkuProjectionBlockedReason.NoPhysicalEvidence, item.Projection.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.NoPhysicalEvidence, item.Projection.ExpiryBlockedReason);
    }

    [Fact]
    public void Inactive_product_lots_are_not_projected()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var active = SeedEligible(today, "ACT", 40, 4);
        var inactive = TestDataHelper.SeedSimpleProduct(40, 10, 4, "INA", "Inativo");
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE products SET active = 0 WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", inactive);
            cmd.ExecuteNonQuery();
        }
        InsertLot(active, 40, today.AddDays(10).ToString("yyyy-MM-dd"), 1, "A");
        InsertLot(inactive, 99, today.AddDays(10).ToString("yyyy-MM-dd"), 1, "I");

        var snap = InventoryProjectionService.Load(today);
        Assert.DoesNotContain(snap.Intelligence.Rows, r => r.ProductId == inactive);
        Assert.False(snap.ByProductId.ContainsKey(inactive));
        Assert.True(snap.ByProductId.ContainsKey(active));
    }

    [Fact]
    public void Mapping_copies_70c_fields_without_recomputing_vmv()
    {
        using var db = Begin();
        var today = DateTime.Today;
        var id = SeedEligible(today, "MAP", 55, 4, fridge: 2);
        var intelligence = InventoryIntelligenceService.Load(today);
        var row = intelligence.Rows.Single(r => r.ProductId == id);
        var composed = InventoryProjectionService.Load(today);
        Assert.Equal(row.Vmv30, composed.Intelligence.Rows.Single(r => r.ProductId == id).Vmv30);
        Assert.Equal(row.HistoryDays, composed.Intelligence.Rows.Single(r => r.ProductId == id).HistoryDays);
        Assert.Equal(row.Stock, composed.Intelligence.Rows.Single(r => r.ProductId == id).Stock);
        Assert.Equal(row.StockFridge, composed.Intelligence.Rows.Single(r => r.ProductId == id).StockFridge);
        Assert.Equal(row.TotalStock, composed.Intelligence.Rows.Single(r => r.ProductId == id).TotalStock);
        Assert.Equal(intelligence.QueryCount, composed.Intelligence.QueryCount);
        Assert.Equal(7, composed.QueryCount);
    }

    [Fact]
    public void Service_source_does_not_call_get_by_product_id()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "SGDB.App", "Services", "InventoryProjectionService.cs");
        if (!File.Exists(path))
        {
            path = FindServiceSource();
        }
        Assert.True(File.Exists(path), path);
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("GetByProductId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Today", source, StringComparison.Ordinal);
    }

    static string FindServiceSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SGDB.App", "Services", "InventoryProjectionService.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return "";
    }
}
