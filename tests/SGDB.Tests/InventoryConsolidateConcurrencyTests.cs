using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 60C — Bloqueio de consolidação se estoque mudou durante o inventário (A+B).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryConsolidateConcurrencyTests
{
    [Fact]
    public void Consolidate_SemMovimento_ConsolidaNormalmente()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "S001", "Sem Mov");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);

        var result = InventoryService.Consolidate(session.Id);

        Assert.Equal(1, result.AdjustedCount);
        Assert.Equal(95, TestDataHelper.GetProductStock(id));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_VendaAposAbertura_Bloqueia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "60c");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "V001", "Com Venda");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);
        Thread.Sleep(1100);
        TestDataHelper.FinalizeSimpleCashSale(id, qty: 10, unitPrice: 5, cashReceived: 50);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));

        Assert.Contains(ex.Conflicts, c => c.ProductId == id);
        Assert.True(ex.Conflicts.Single(c => c.ProductId == id).HasMovementSinceOpen);
        AssertConflictIntact(id, 90, session.Id);
    }

    [Fact]
    public void Consolidate_CompraAposAbertura_Bloqueia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 8, 4, "P001", "Com Compra");
        var supplierId = SeedSupplier();
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 100);
        Thread.Sleep(1100);

        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-60C",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = id,
                    ProductName = "Com Compra",
                    Quantity = 20,
                    UnitPrice = 4,
                },
            ],
        }, closeOnSave: true);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains(ex.Conflicts, c => c.ProductId == id && c.HasMovementSinceOpen);
        AssertConflictIntact(id, 120, session.Id);
    }

    [Fact]
    public void Consolidate_AjusteAposAbertura_Bloqueia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "A001", "Com Ajuste");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);
        Thread.Sleep(1100);
        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 80);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains(ex.Conflicts, c => c.ProductId == id && c.HasMovementSinceOpen);
        AssertConflictIntact(id, 80, session.Id);
    }

    [Fact]
    public void Consolidate_VendaMaisCancel_BloqueiaMesmoStockVoltando()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(openingAmount: 50, notes: "60c-cancel");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "C001", "Ida Volta");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);
        Thread.Sleep(1100);

        var sale = TestDataHelper.FinalizeSimpleCashSale(id, qty: 10, unitPrice: 5, cashReceived: 50);
        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains(ex.Conflicts, c => c.ProductId == id && c.HasMovementSinceOpen);
        AssertConflictIntact(id, 100, session.Id);
    }

    [Fact]
    public void Consolidate_AlteracaoSemMovement_BloqueiaPorStock()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "M001", "Sem Movement");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 95);
        SetStockWithoutMovement(id, 110);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));
        var conflict = Assert.Single(ex.Conflicts, c => c.ProductId == id);
        Assert.False(conflict.HasMovementSinceOpen);
        Assert.Equal(100, conflict.TheoreticalQty);
        Assert.Equal(110, conflict.CurrentStock);
        AssertConflictIntact(id, 110, session.Id);
        Assert.Equal(0, CountInventoryNotes(session.Id));
    }

    [Fact]
    public void Consolidate_MovementOutroProduto_NaoBloqueia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "60c-other");
        var a = TestDataHelper.SeedSimpleProduct(100, 5, 2, "OA", "Contado");
        var b = TestDataHelper.SeedSimpleProduct(50, 5, 2, "OB", "Nao Contado Mov");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, a), 95);
        // B tem movement mas não foi contado
        TestDataHelper.FinalizeSimpleCashSale(b, qty: 5, unitPrice: 5, cashReceived: 25);

        var result = InventoryService.Consolidate(session.Id);

        Assert.Equal(1, result.AdjustedCount);
        Assert.Equal(95, TestDataHelper.GetProductStock(a));
        Assert.Equal(45, TestDataHelper.GetProductStock(b));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_MovementAntesDaAbertura_NaoBloqueia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "OLD", "Mov Antigo");
        InsertBackdatedMovement(id, "2020-01-01 12:00:00");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 97);

        var result = InventoryService.Consolidate(session.Id);

        Assert.Equal(1, result.AdjustedCount);
        Assert.Equal(97, TestDataHelper.GetProductStock(id));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_ProdutoNaoContadoComMovement_NaoBloqueiaSozinho()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "60c-uncounted");
        var counted = TestDataHelper.SeedSimpleProduct(40, 5, 2, "CNT", "Contado Ok");
        var uncounted = TestDataHelper.SeedSimpleProduct(30, 5, 2, "UNC", "Nao Contado");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, counted), 38);
        // uncounted permanece counted_qty NULL e sofre venda
        TestDataHelper.FinalizeSimpleCashSale(uncounted, qty: 3, unitPrice: 5, cashReceived: 15);

        var result = InventoryService.Consolidate(session.Id);

        Assert.Equal(1, result.AdjustedCount);
        Assert.Equal(38, TestDataHelper.GetProductStock(counted));
        Assert.Equal(27, TestDataHelper.GetProductStock(uncounted));
        Assert.Null(InventoryService.ListItems(session.Id).Single(i => i.ProductId == uncounted).CountedQty);
    }

    [Fact]
    public void Consolidate_Conflito_NaoAlteraStockNemMovementNemSessao()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(100, 5, 2, "X001", "Conflito");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, id), 90);
        Thread.Sleep(1100);
        StockService.Adjust(id, StockAdjustMode.Entrada, quantity: 5);
        var movBefore = CountAllMovements();

        Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));

        Assert.Equal(105, TestDataHelper.GetProductStock(id));
        Assert.Equal(movBefore, CountAllMovements());
        Assert.Equal(0, CountInventoryNotes(session.Id));
        Assert.Equal("aberta", GetSessionStatus(session.Id));
        Assert.NotNull(InventoryService.GetOpenSession());
    }

    [Fact]
    public void Consolidate_MultiplosConflitos_Retornados()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var a = TestDataHelper.SeedSimpleProduct(100, 5, 2, "MA", "Multi A");
        var b = TestDataHelper.SeedSimpleProduct(50, 5, 2, "MB", "Multi B");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, a), 90);
        InventoryService.SetCounted(ItemId(session.Id, b), 40);
        Thread.Sleep(1100);
        StockService.Adjust(a, StockAdjustMode.Saldo, newStock: 88);
        SetStockWithoutMovement(b, 55);

        var ex = Assert.Throws<InventoryConcurrencyException>(() => InventoryService.Consolidate(session.Id));

        Assert.Equal(2, ex.Conflicts.Count);
        Assert.Contains(ex.Conflicts, c => c.ProductId == a && c.HasMovementSinceOpen);
        Assert.Contains(ex.Conflicts, c => c.ProductId == b && !c.HasMovementSinceOpen);
        Assert.Contains("Multi A", ex.Message);
        Assert.Contains("Multi B", ex.Message);
        Assert.Contains("Reconte os produtos", ex.Message);
        Assert.Equal("aberta", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_CountedFisicoCigarro_PreservadoQuandoSemConflito()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var cigId = SeedCigarette(stock: 100, fator: 20);
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, cigId), 67);

        InventoryService.Consolidate(session.Id);

        Assert.Equal(67, TestDataHelper.GetProductStock(cigId));
    }

    [Fact]
    public void Consolidate_SucessoContinuaAtomico_TresProdutos()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var a = TestDataHelper.SeedSimpleProduct(100, 5, 2, "TA", "Atom A");
        var b = TestDataHelper.SeedSimpleProduct(50, 5, 2, "TB", "Atom B");
        var c = TestDataHelper.SeedSimpleProduct(20, 5, 2, "TC", "Atom C");
        var session = InventoryService.CreateSession();
        InventoryService.SetCounted(ItemId(session.Id, a), 90);
        InventoryService.SetCounted(ItemId(session.Id, b), 45);
        InventoryService.SetCounted(ItemId(session.Id, c), 18);

        InventoryService.Consolidate(session.Id);

        Assert.Equal(90, TestDataHelper.GetProductStock(a));
        Assert.Equal(45, TestDataHelper.GetProductStock(b));
        Assert.Equal(18, TestDataHelper.GetProductStock(c));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    private static int ItemId(int sessionId, int productId) =>
        InventoryService.ListItems(sessionId).Single(i => i.ProductId == productId).Id;

    private static void AssertConflictIntact(int productId, double expectedStock, int sessionId)
    {
        Assert.Equal(expectedStock, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountInventoryNotes(sessionId));
        Assert.Equal("aberta", GetSessionStatus(sessionId));
        Assert.NotNull(InventoryService.GetOpenSession());
    }

    private static string GetSessionStatus(int sessionId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM inventory_sessions WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", sessionId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static int CountAllMovements()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movements;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountInventoryNotes(int sessionId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movements WHERE IFNULL(notes,'') LIKE $note;";
        cmd.Parameters.AddWithValue("$note", $"%Inventário #{sessionId}%");
        return Convert.ToInt32(cmd.ExecuteScalar());
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

    private static void InsertBackdatedMovement(int productId, string createdAt)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at,
              stock_before, stock_after, operation, user_name, unit
            ) VALUES (
              $pid, 'entrada', 1, 0, 'mov antigo teste', $at,
              99, 100, 'ajuste_manual', 'teste', 'UN'
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$at", createdAt);
        cmd.ExecuteNonQuery();
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active)
            VALUES ('fornecedor', 'juridica', 'FORN 60C', 1);
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
                'CIG60C', 'CIGARRO 60C', 'CIGARROS', 'UN', 28.5, $stock, 20, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
