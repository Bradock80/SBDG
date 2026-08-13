using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 60D-B — Recontagem inteligente (counted_at + count_baseline_qty).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryRecountIntelligenceTests
{
    [Fact]
    public void Migration_CriaColunasCountedAtEBaseline()
    {
        using var db = TempDatabase.Create();
        var cols = GetInventoryItemColumns();
        Assert.Contains("counted_at", cols);
        Assert.Contains("count_baseline_qty", cols);
    }

    [Fact]
    public void Migration_Idempotente_ReabreBancoSemErro()
    {
        using var db = TempDatabase.Create();
        DatabaseService.Initialize(db.DatabasePath);
        DatabaseService.Initialize(db.DatabasePath);
        var cols = GetInventoryItemColumns();
        Assert.Contains("counted_at", cols);
        Assert.Contains("count_baseline_qty", cols);
    }

    [Fact]
    public void SetCounted_PrimeiraContagem_GravaBaselineTimestampEPreservaTheoretical()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "SC1", "Primeira");
        var session = InventoryService.CreateSession();
        var itemId = ItemId(session.Id, id);

        InventoryService.SetCounted(itemId, 95);

        var item = InventoryService.ListItems(session.Id).Single(i => i.ProductId == id);
        Assert.Equal(95, item.CountedQty);
        Assert.Equal(100, item.CountBaselineQty);
        Assert.False(string.IsNullOrWhiteSpace(item.CountedAt));
        Assert.Equal(100, item.TheoreticalQty);
    }

    [Fact]
    public void SetCounted_Recontagem_SubstituiBaselineETimestamp_TheoreticalIntacta()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "SC2", "Reconta");
        var session = InventoryService.CreateSession();
        var itemId = ItemId(session.Id, id);

        InventoryService.SetCounted(itemId, 95);
        var firstAt = InventoryService.ListItems(session.Id).Single(i => i.ProductId == id).CountedAt;
        WaitPastSqliteSecond();
        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 90);
        InventoryService.SetCounted(itemId, 88);

        var item = InventoryService.ListItems(session.Id).Single(i => i.ProductId == id);
        Assert.Equal(88, item.CountedQty);
        Assert.Equal(90, item.CountBaselineQty);
        Assert.NotEqual(firstAt, item.CountedAt);
        Assert.Equal(100, item.TheoreticalQty);
    }

    [Fact]
    public void Consolidate_MovementAntesDaContagem_NaoBloqueia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "60d-before");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "BEF", "Antes Conta");
        var session = InventoryService.CreateSession();
        TestDataHelper.FinalizeSimpleCashSale(id, qty: 10, unitPrice: 5, cashReceived: 50);
        Assert.Equal(90, TestDataHelper.GetProductStock(id));
        WaitPastSqliteSecond();
        InventoryService.SetCounted(ItemId(session.Id, id), 88);

        var result = InventoryService.Consolidate(session.Id);

        Assert.Equal(1, result.AdjustedCount);
        Assert.Equal(88, TestDataHelper.GetProductStock(id));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_MovementDepoisDaContagem_Bloqueia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "60d-after");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "AFT", "Depois Conta");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);
        WaitPastSqliteSecond();
        TestDataHelper.FinalizeSimpleCashSale(id, qty: 10, unitPrice: 5, cashReceived: 50);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains(ex.Conflicts, c => c.ProductId == id && c.HasMovementSinceCount);
        Assert.Equal("aberta", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_StockSemMovementDepois_BloqueiaPorBaseline()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "BASE", "Baseline");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);
        SetStockWithoutMovement(id, 110);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        var c = Assert.Single(ex.Conflicts, x => x.ProductId == id);
        Assert.True(c.StockDivergedFromBaseline);
        Assert.False(c.HasMovementSinceCount);
        Assert.Equal("aberta", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_VendaCancelDepois_Bloqueia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(openingAmount: 50, notes: "60d-vc");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "VC", "IdaVolta");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);
        WaitPastSqliteSecond();
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, qty: 10, unitPrice: 5, cashReceived: 50);
        PdvService.CancelSale(sale.SaleId);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains(ex.Conflicts, c => c.ProductId == id && c.HasMovementSinceCount);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
        Assert.Equal("aberta", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_RecontagemAposConflito_LiberaSemCancelarSessao()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "60d-recount");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "RC", "Recontar");
        var session = InventoryService.CreateSession();
        var itemId = ItemId(session.Id, id);
        InventoryService.SetCounted(itemId, 95);
        WaitPastSqliteSecond();
        TestDataHelper.FinalizeSimpleCashSale(id, qty: 10, unitPrice: 5, cashReceived: 50);

        Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.NotNull(InventoryService.GetOpenSession());

        WaitPastSqliteSecond();
        InventoryService.SetCounted(itemId, 88); // baseline = 90

        var result = InventoryService.Consolidate(session.Id);
        Assert.Equal(1, result.AdjustedCount);
        Assert.Equal(88, TestDataHelper.GetProductStock(id));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_NovoMovementAposRecontagem_BloqueiaNovamente()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 80, notes: "60d-again");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "AG", "DeNovo");
        var session = InventoryService.CreateSession();
        var itemId = ItemId(session.Id, id);
        InventoryService.SetCounted(itemId, 95);
        WaitPastSqliteSecond();
        TestDataHelper.FinalizeSimpleCashSale(id, qty: 10, unitPrice: 5, cashReceived: 50);
        Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));

        WaitPastSqliteSecond();
        InventoryService.SetCounted(itemId, 88);
        WaitPastSqliteSecond();
        TestDataHelper.FinalizeSimpleCashSale(id, qty: 5, unitPrice: 5, cashReceived: 25);

        Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Equal("aberta", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_ItemLegadoSemCountedAt_ExigeRecontagem()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "LEG", "Legado");
        var session = InventoryService.CreateSession();
        var itemId = ItemId(session.Id, id);
        InventoryService.SetCounted(itemId, 95);
        ClearRecountMetadata(itemId);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains(ex.Conflicts, c => c.ProductId == id && c.RequiresRecount);
        Assert.Contains("precisa ser recontado", ex.Message);

        InventoryService.SetCounted(itemId, 95);
        var result = InventoryService.Consolidate(session.Id);
        Assert.Equal(95, TestDataHelper.GetProductStock(id));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_ItemLegadoSemBaseline_ExigeRecontagem()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(40, 5, 2, "LEG2", "Legado Baseline");
        var session = InventoryService.CreateSession();
        var itemId = ItemId(session.Id, id);
        InventoryService.SetCounted(itemId, 37);
        ClearBaselineOnly(itemId);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains(ex.Conflicts, c => c.ProductId == id && c.RequiresRecount);
    }

    [Fact]
    public void Consolidate_SoConflitantePrecisaRecontar_OutrosIntactos()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "60d-partial");
        var ok = TestDataHelper.SeedSimpleProduct(50, 5, 2, "OK", "Sem Conflito");
        var bad = TestDataHelper.SeedSimpleProduct(100, 5, 2, "BAD", "Com Conflito");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, ok), 48);
        InventoryService.SetCounted(ItemId(session.Id, bad), 95);
        WaitPastSqliteSecond();
        TestDataHelper.FinalizeSimpleCashSale(bad, qty: 10, unitPrice: 5, cashReceived: 50);

        Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));

        WaitPastSqliteSecond();
        InventoryService.SetCounted(ItemId(session.Id, bad), 88);

        var result = InventoryService.Consolidate(session.Id);
        Assert.True(result.AdjustedCount >= 2);
        Assert.Equal(48, TestDataHelper.GetProductStock(ok));
        Assert.Equal(88, TestDataHelper.GetProductStock(bad));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_CigarroFisico_Preservado()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var cigId = SeedCigarette(100, 20);
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, cigId), 67);
        var item = InventoryService.ListItems(session.Id).Single(i => i.ProductId == cigId);
        Assert.Equal(100, item.CountBaselineQty);

        InventoryService.Consolidate(session.Id);
        Assert.Equal(67, TestDataHelper.GetProductStock(cigId));
    }

    private static int ItemId(int sessionId, int productId) =>
        InventoryService.ListItems(sessionId).Single(i => i.ProductId == productId).Id;

    private static void WaitPastSqliteSecond() => Thread.Sleep(1100);

    private static string GetSessionStatus(int sessionId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM inventory_sessions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", sessionId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static HashSet<string> GetInventoryItemColumns()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(inventory_items);";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(1));
        return set;
    }

    private static void SetStockWithoutMovement(int productId, double stock)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock = $stock WHERE id = $id;";
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static void ClearRecountMetadata(int itemId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE inventory_items
            SET counted_at = NULL, count_baseline_qty = NULL
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", itemId);
        cmd.ExecuteNonQuery();
    }

    private static void ClearBaselineOnly(int itemId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE inventory_items SET count_baseline_qty = NULL WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", itemId);
        cmd.ExecuteNonQuery();
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
                'CIG60D', 'CIGARRO 60D', 'CIGARROS', 'UN', 28.5, $stock, 20, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
