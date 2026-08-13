using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 60B — Consolidação de inventário atômica (tudo ou nada).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryConsolidateAtomicityTests
{
    [Fact]
    public void Consolidate_TresProdutos_Sucesso_AjustaStocksMovementsESessao()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var (a, b, c) = SeedThree(100, 50, 20);
        var session = OpenAndCount(a, 90, b, 45, c, 18);

        var result = InventoryService.Consolidate(session.Id);

        Assert.Equal(3, result.AdjustedCount);
        Assert.Equal(90, TestDataHelper.GetProductStock(a));
        Assert.Equal(45, TestDataHelper.GetProductStock(b));
        Assert.Equal(18, TestDataHelper.GetProductStock(c));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));

        var movs = ListInventoryMovements(session.Id);
        Assert.Equal(3, movs.Count);
        Assert.Contains(movs, m => m.ProductId == a && m.Before == 100 && m.After == 90 && m.Qty == 10 && m.Type == "saida");
        Assert.Contains(movs, m => m.ProductId == b && m.Before == 50 && m.After == 45 && m.Qty == 5 && m.Type == "saida");
        Assert.Contains(movs, m => m.ProductId == c && m.Before == 20 && m.After == 18 && m.Qty == 2 && m.Type == "saida");
        Assert.All(movs, m =>
        {
            Assert.Equal("ajuste_manual", m.Operation);
            Assert.Contains($"Inventário #{session.Id}", m.Notes ?? "", StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(m.User));
        });
    }

    [Fact]
    public void Consolidate_FalhaPrimeiro_NadaPersiste_SessaoAberta()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var (a, b, c) = SeedThree(100, 50, 20);
        var session = OpenAndCount(a, 90, b, 45, c, 18);

        try
        {
            StockService.TestBeforeTransactionalAdjust = id =>
            {
                if (id == a)
                    throw new InvalidOperationException("falha controlada no primeiro");
            };

            var ex = Assert.Throws<InvalidOperationException>(() => InventoryService.Consolidate(session.Id));
            Assert.Contains("falha controlada", ex.Message);
        }
        finally
        {
            StockService.TestBeforeTransactionalAdjust = null;
        }

        AssertIntact(a, 100, b, 50, c, 20, session.Id);
    }

    [Fact]
    public void Consolidate_FalhaNoMeio_RollbackDoPrimeiro_SessaoAberta()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var (a, b, c) = SeedThree(100, 50, 20);
        var session = OpenAndCount(a, 90, b, 45, c, 18);

        try
        {
            StockService.TestBeforeTransactionalAdjust = id =>
            {
                if (id == b)
                    throw new InvalidOperationException("falha controlada no meio");
            };

            var ex = Assert.Throws<InvalidOperationException>(() => InventoryService.Consolidate(session.Id));
            Assert.Contains("falha controlada", ex.Message);
        }
        finally
        {
            StockService.TestBeforeTransactionalAdjust = null;
        }

        AssertIntact(a, 100, b, 50, c, 20, session.Id);
    }

    [Fact]
    public void Consolidate_FalhaUltimo_RollbackAnteriores_SessaoAberta()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var (a, b, c) = SeedThree(100, 50, 20);
        var session = OpenAndCount(a, 90, b, 45, c, 18);

        try
        {
            StockService.TestBeforeTransactionalAdjust = id =>
            {
                if (id == c)
                    throw new InvalidOperationException("falha controlada no ultimo");
            };

            var ex = Assert.Throws<InvalidOperationException>(() => InventoryService.Consolidate(session.Id));
            Assert.Contains("falha controlada", ex.Message);
        }
        finally
        {
            StockService.TestBeforeTransactionalAdjust = null;
        }

        AssertIntact(a, 100, b, 50, c, 20, session.Id);
    }

    [Fact]
    public void Consolidate_FalhaAntesDeMarcarSessao_NadaPersiste()
    {
        // Exceção no último Adjust ocorre antes do UPDATE status — mesma garantia de rollback.
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var (a, b, c) = SeedThree(100, 50, 20);
        var session = OpenAndCount(a, 90, b, 45, c, 18);

        try
        {
            StockService.TestBeforeTransactionalAdjust = id =>
            {
                if (id == c)
                    throw new InvalidOperationException("falha antes de marcar sessao");
            };

            Assert.Throws<InvalidOperationException>(() => InventoryService.Consolidate(session.Id));
        }
        finally
        {
            StockService.TestBeforeTransactionalAdjust = null;
        }

        AssertIntact(a, 100, b, 50, c, 20, session.Id);
    }

    [Fact]
    public void Consolidate_FalhaAoMarcarSessao_RollbackStocksEMovements()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var (a, b, c) = SeedThree(100, 50, 20);
        var session = OpenAndCount(a, 90, b, 45, c, 18);

        try
        {
            InventoryService.TestBeforeMarkSessionConsolidated = () =>
                throw new InvalidOperationException("falha controlada ao marcar sessao");

            var ex = Assert.Throws<InvalidOperationException>(() => InventoryService.Consolidate(session.Id));
            Assert.Contains("marcar sessao", ex.Message);
        }
        finally
        {
            InventoryService.TestBeforeMarkSessionConsolidated = null;
        }

        AssertIntact(a, 100, b, 50, c, 20, session.Id);
    }

    [Fact]
    public void Consolidate_Sucesso_DepoisSegundoConsolidate_BloqueadoSemExtra()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var (a, b, c) = SeedThree(100, 50, 20);
        var session = OpenAndCount(a, 90, b, 45, c, 18);

        InventoryService.Consolidate(session.Id);
        var movBefore = CountAllMovements();

        var ex = Assert.Throws<InvalidOperationException>(() => InventoryService.Consolidate(session.Id));
        Assert.Contains("encerrado", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(90, TestDataHelper.GetProductStock(a));
        Assert.Equal(45, TestDataHelper.GetProductStock(b));
        Assert.Equal(18, TestDataHelper.GetProductStock(c));
        Assert.Equal(movBefore, CountAllMovements());
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Consolidate_CountedFisicoCigarro_NaoReinterpretaMacos()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var cigId = SeedCigarette(stock: 100, fator: 20);
        var session = InventoryService.CreateSession();
        var item = InventoryService.ListItems(session.Id).Single(i => i.ProductId == cigId);
        // 3 maços + 7 avulsos já convertidos pela UI → 67 físico
        InventoryService.SetCounted(item.Id, 67);

        InventoryService.Consolidate(session.Id);

        Assert.Equal(67, TestDataHelper.GetProductStock(cigId));
        Assert.Equal(67, InventoryService.ListItems(session.Id).Single(i => i.ProductId == cigId).CountedQty);
    }

    [Fact]
    public void Consolidate_ProdutoComum_Preservado()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(stock: 40, salePrice: 5, costPrice: 2, code: "COM1", name: "COMUM");
        var session = InventoryService.CreateSession();
        var item = InventoryService.ListItems(session.Id).Single(i => i.ProductId == id);
        InventoryService.SetCounted(item.Id, 37);

        var result = InventoryService.Consolidate(session.Id);

        Assert.Equal(1, result.AdjustedCount);
        Assert.Equal(37, TestDataHelper.GetProductStock(id));
        Assert.Equal("consolidada", GetSessionStatus(session.Id));
    }

    [Fact]
    public void Adjust_ConnTx_DuasChamadas_RollbackExterno_NaoPersiste()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var a = TestDataHelper.SeedSimpleProduct(100, 5, 2, "TXA", "Tx A");
        var b = TestDataHelper.SeedSimpleProduct(50, 5, 2, "TXB", "Tx B");

        using (var conn = DatabaseService.OpenConnection())
        using (var tx = conn.BeginTransaction())
        {
            StockService.Adjust(conn, tx, a, StockAdjustMode.Saldo, newStock: 90);
            StockService.Adjust(conn, tx, b, StockAdjustMode.Saldo, newStock: 40);
            // sem Commit — dispose/rollback
        }

        Assert.Equal(100, TestDataHelper.GetProductStock(a));
        Assert.Equal(50, TestDataHelper.GetProductStock(b));
        Assert.Equal(0, CountAllMovements());
    }

    [Fact]
    public void Adjust_Publico_ContinuaCommitandoSozinho()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(10, 5, 2, "PUB", "Public Adjust");

        var result = StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 7);

        Assert.Equal(7, result.StockAfter);
        Assert.Equal(7, TestDataHelper.GetProductStock(id));
        Assert.Equal(1, CountAllMovements());
    }

    private static (int A, int B, int C) SeedThree(double stockA, double stockB, double stockC)
    {
        var a = TestDataHelper.SeedSimpleProduct(stockA, 5, 2, "A001", "Produto A");
        var b = TestDataHelper.SeedSimpleProduct(stockB, 5, 2, "B001", "Produto B");
        var c = TestDataHelper.SeedSimpleProduct(stockC, 5, 2, "C001", "Produto C");
        return (a, b, c);
    }

    private static InventorySession OpenAndCount(
        int a, double countedA,
        int b, double countedB,
        int c, double countedC)
    {
        var session = InventoryService.CreateSession();
        var items = InventoryService.ListItems(session.Id);
        InventoryService.SetCounted(items.Single(i => i.ProductId == a).Id, countedA);
        InventoryService.SetCounted(items.Single(i => i.ProductId == b).Id, countedB);
        InventoryService.SetCounted(items.Single(i => i.ProductId == c).Id, countedC);
        return session;
    }

    private static void AssertIntact(
        int a, double stockA,
        int b, double stockB,
        int c, double stockC,
        int sessionId)
    {
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a));
        Assert.Equal(stockB, TestDataHelper.GetProductStock(b));
        Assert.Equal(stockC, TestDataHelper.GetProductStock(c));
        Assert.Equal(0, CountAllMovements());
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

    private static List<MovRow> ListInventoryMovements(int sessionId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT product_id, movement_type, quantity, IFNULL(stock_before,0), IFNULL(stock_after,0),
                   IFNULL(operation,''), IFNULL(notes,''), IFNULL(user_name,'')
            FROM movements
            WHERE IFNULL(notes,'') LIKE $note
            ORDER BY product_id;
            """;
        cmd.Parameters.AddWithValue("$note", $"%Inventário #{sessionId}%");
        var list = new List<MovRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new MovRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }
        return list;
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
                'CIG60', 'CIGARRO TESTE', 'CIGARROS', 'UN', 28.5, $stock, 20, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private sealed record MovRow(
        int ProductId, string Type, double Qty, double Before, double After,
        string Operation, string Notes, string User);
}
