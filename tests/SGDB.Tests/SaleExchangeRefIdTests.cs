using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 70C-B1F2 — movements.ref_id = sale_exchanges.id nas trocas novas.
/// Sem backfill. Sem alterar o motor 70C.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class SaleExchangeRefIdTests
{
    private const double Tol = 0.0001;

    private static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(100, "70c-b1f2");
        return db;
    }

    [Fact]
    public void Confirm_TrocaSimples_DevolucaoENovo_MesmoRefId()
    {
        using var _ = Begin();
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "A1", "Origem");
        var b = TestDataHelper.SeedSimpleProduct(20, 12, 5, "B1", "Novo");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = SaleItemId(sale.SaleId);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 1, UnitPrice = 12 }],
            PaymentType = "Dinheiro",
        });

        Assert.True(result.ExchangeId > 0);
        var returned = ExchangeMovements(a, "entrada", "devolucao_troca");
        var delivered = ExchangeMovements(b, "saida", "venda");
        Assert.Single(returned);
        Assert.Single(delivered);
        Assert.Equal("sale_exchange", returned[0].RefType);
        Assert.Equal("sale_exchange", delivered[0].RefType);
        Assert.Equal(result.ExchangeId, returned[0].RefId);
        Assert.Equal(result.ExchangeId, delivered[0].RefId);
        Assert.All(AllExchangeMovements(result.ExchangeId), m => Assert.Equal(result.ExchangeId, m.RefId));
    }

    [Fact]
    public void Confirm_SomenteDevolucao_EntradaComRefId()
    {
        using var _ = Begin();
        var id = TestDataHelper.SeedSimpleProduct(20, 10, 4, "D1", "So Devolve");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 2, 10, 20);
        var itemId = SaleItemId(sale.SaleId);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 2 }],
        });

        var rows = ExchangeMovements(id, "entrada", "devolucao_troca");
        Assert.Single(rows);
        Assert.Equal("sale_exchange", rows[0].RefType);
        Assert.Equal(result.ExchangeId, rows[0].RefId);
        Assert.Equal(0, Count("sale_exchange_new_items"));
    }

    [Fact]
    public void Confirm_SomenteItemNovo_NaoPermitido()
    {
        using var _ = Begin();
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "S1", "Origem");
        var b = TestDataHelper.SeedSimpleProduct(20, 8, 3, "S2", "Novo Isolado");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);

        var ex = Assert.Throws<SaleExchangeException>(() =>
            SaleExchangeService.Confirm(new SaleExchangeRequest
            {
                OriginalSaleId = sale.SaleId,
                Returns = [],
                NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 1, UnitPrice = 8 }],
                PaymentType = "Dinheiro",
            }));
        Assert.Contains("devolver", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Count("sale_exchanges"));
        Assert.Equal(0, CountExchangeStockMovements());
    }

    [Fact]
    public void Confirm_MultiplosProdutos_MesmoExchangeId()
    {
        using var _ = Begin();
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "M1", "Origem 1");
        var b = TestDataHelper.SeedSimpleProduct(20, 11, 4, "M2", "Origem 2");
        var c = TestDataHelper.SeedSimpleProduct(20, 12, 5, "M3", "Novo 1");
        var d = TestDataHelper.SeedSimpleProduct(20, 13, 5, "M4", "Novo 2");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                Line(a, 1, 10),
                Line(b, 1, 11),
            ],
            PaymentType = "Dinheiro",
            CashReceived = 21,
        });
        var items = SaleItemIds(sale.SaleId);
        Assert.Equal(2, items.Count);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns =
            [
                new SaleExchangeReturnLine { SaleItemId = items[0].Id, Qty = 1 },
                new SaleExchangeReturnLine { SaleItemId = items[1].Id, Qty = 1 },
            ],
            NewItems =
            [
                new SaleExchangeNewLine { ProductId = c, Qty = 1, UnitPrice = 12 },
                new SaleExchangeNewLine { ProductId = d, Qty = 1, UnitPrice = 13 },
            ],
            PaymentType = "Dinheiro",
        });

        var movements = AllExchangeMovements(result.ExchangeId);
        Assert.Equal(4, movements.Count);
        Assert.All(movements, m =>
        {
            Assert.Equal("sale_exchange", m.RefType);
            Assert.Equal(result.ExchangeId, m.RefId);
        });
        Assert.Equal(4, movements.Select(m => m.ProductId).Distinct().Count());
        Assert.Contains(movements, m => m.ProductId == a && m.Type == "entrada");
        Assert.Contains(movements, m => m.ProductId == b && m.Type == "entrada");
        Assert.Contains(movements, m => m.ProductId == c && m.Type == "saida");
        Assert.Contains(movements, m => m.ProductId == d && m.Type == "saida");
    }

    [Fact]
    public void Confirm_Bloqueio70I_AposInsert_RollbackTotal()
    {
        using var _ = Begin();
        var a = TestDataHelper.SeedSimpleProduct(10, 5, 2, "E70A", "Origem 70I");
        var b = TestDataHelper.SeedSimpleProduct(10, 5, 2, "E70B", "Vencido 70I");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = b,
            Quantity = 10,
            LotNumber = "V",
            ExpiryDate = DateTime.Today.AddDays(-1),
        });
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 5, 5);
        var itemId = SaleItemId(sale.SaleId);

        var stockA = TestDataHelper.GetProductStock(a);
        var stockB = TestDataHelper.GetProductStock(b);
        var lotsB = TestDataHelper.SumLots(b);
        var cashBefore = Count("cash_movements");
        var movementsBefore = Count("movements");

        Assert.Throws<ExpirySaleException>(() =>
            SaleExchangeService.Confirm(new SaleExchangeRequest
            {
                OriginalSaleId = sale.SaleId,
                Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
                NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 1, UnitPrice = 5 }],
                PaymentType = "Dinheiro",
            }));

        Assert.Equal(0, Count("sale_exchanges"));
        Assert.Equal(0, Count("sale_exchange_return_items"));
        Assert.Equal(0, Count("sale_exchange_new_items"));
        Assert.Equal(0, CountExchangeStockMovements());
        Assert.Equal(movementsBefore, Count("movements"));
        Assert.Equal(stockA, TestDataHelper.GetProductStock(a), Tol);
        Assert.Equal(stockB, TestDataHelper.GetProductStock(b), Tol);
        Assert.Equal(lotsB, TestDataHelper.SumLots(b), Tol);
        Assert.Equal(10, LotQty(b, "V"), Tol);
        Assert.Equal(cashBefore, Count("cash_movements"));
        Assert.Equal(0, CountCashExchange());
    }

    [Fact]
    public void Confirm_Diferenca_CaixaUsaMesmoExchangeId()
    {
        using var _ = Begin();
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 4, "C1", "Origem Caixa");
        var b = TestDataHelper.SeedSimpleProduct(20, 15, 6, "C2", "Novo Caixa");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        var itemId = SaleItemId(sale.SaleId);

        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 1, UnitPrice = 15 }],
            PaymentType = "Dinheiro",
        });

        var cashRef = CashExchangeRefIds();
        Assert.Single(cashRef);
        Assert.Equal(result.ExchangeId, cashRef[0]);
        Assert.All(AllExchangeMovements(result.ExchangeId), m => Assert.Equal(result.ExchangeId, m.RefId));
    }

    [Fact]
    public void Legado_MovementsRefIdNull_NaoSaoPreenchidos()
    {
        using var _ = Begin();
        var legado = TestDataHelper.SeedSimpleProduct(30, 10, 4, "LNULL", "Legado Null");
        var moderno = TestDataHelper.SeedSimpleProduct(30, 10, 4, "LNEW", "Troca Nova");
        InsertRawExchangeMovement(legado, "entrada", 1, "devolucao_troca", refId: null);
        Assert.Equal(1, CountNullExchangeMovements());

        var sale = TestDataHelper.FinalizeSimpleCashSale(moderno, 1, 10, 10);
        var result = SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 1 }],
        });

        Assert.Equal(1, CountNullExchangeMovements());
        var legadoRow = ExchangeMovements(legado, "entrada", "devolucao_troca");
        Assert.Single(legadoRow);
        Assert.Null(legadoRow[0].RefId);
        var novo = ExchangeMovements(moderno, "entrada", "devolucao_troca");
        Assert.Single(novo);
        Assert.Equal(result.ExchangeId, novo[0].RefId);
    }

    [Fact]
    public void Motor70C_TrocaNovaComRefId_NaoDuplicaEvento()
    {
        using var _ = Begin();
        var a = TestDataHelper.SeedSimpleProduct(50, 10, 4, "DUPA", "Origem Dup");
        var b = TestDataHelper.SeedSimpleProduct(50, 8, 3, "DUPB", "Novo Dup");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 1 }],
            NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 3, UnitPrice = 8 }],
            PaymentType = "Dinheiro",
        });

        var rowB = InventoryIntelligenceService.GetByProductId(b, DateTime.Today);
        Assert.NotNull(rowB);
        Assert.Equal(3, rowB!.Vmv7, Tol);
        Assert.Equal(0, InventoryIntelligenceService.GetByProductId(a, DateTime.Today)!.Vmv7, Tol);
    }

    [Fact]
    public void Motor70C_LegadoRefIdNull_ContinuaNoFallback()
    {
        using var _ = Begin();
        var legado = TestDataHelper.SeedSimpleProduct(80, 10, 4, "FBLEG", "Troca Legada");
        var saleId = InsertLegacySale(legado, 4, DateTime.Today, stockQty: 4);
        InsertLegacyExchange(saleId, legado, returnQty: 1, DateBrHelper.NowUtcIso());

        var row = InventoryIntelligenceService.GetByProductId(legado, DateTime.Today);
        Assert.NotNull(row);
        Assert.Equal(3, row!.Vmv7, Tol);
        Assert.Equal(0, CountNullExchangeMovements());
    }

    private static PdvCartLine Line(int productId, double qty, double price) => new()
    {
        ProductId = productId,
        Code = "T",
        Name = "Item",
        Unit = "UN",
        Quantity = qty,
        UnitPrice = price,
        StockUnitsPerSale = 1,
    };

    private static int SaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<(int Id, int ProductId)> SaleItemIds(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, product_id FROM sale_items WHERE sale_id = $id ORDER BY id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<(int, int)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.GetInt32(1)));
        return list;
    }

    private static List<MovementRef> ExchangeMovements(int productId, string type, string operation)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT product_id, IFNULL(movement_type,''), IFNULL(operation,''),
                   IFNULL(ref_type,''), ref_id
            FROM movements
            WHERE product_id = $pid
              AND IFNULL(ref_type,'') = 'sale_exchange'
              AND IFNULL(movement_type,'') = $type
              AND IFNULL(operation,'') = $op
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$op", operation);
        return ReadMovements(cmd);
    }

    private static List<MovementRef> AllExchangeMovements(int exchangeId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT product_id, IFNULL(movement_type,''), IFNULL(operation,''),
                   IFNULL(ref_type,''), ref_id
            FROM movements
            WHERE IFNULL(ref_type,'') = 'sale_exchange' AND ref_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", exchangeId);
        return ReadMovements(cmd);
    }

    private static List<MovementRef> ReadMovements(SqliteCommand cmd)
    {
        var list = new List<MovementRef>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new MovementRef(
                r.GetInt32(0),
                r.GetString(1).Trim().ToLowerInvariant(),
                r.GetString(2).Trim().ToLowerInvariant(),
                r.GetString(3),
                r.IsDBNull(4) ? null : Convert.ToInt32(r.GetValue(4))));
        }
        return list;
    }

    private static int Count(string table)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = table switch
        {
            "sale_exchanges" => "SELECT COUNT(*) FROM sale_exchanges;",
            "sale_exchange_return_items" => "SELECT COUNT(*) FROM sale_exchange_return_items;",
            "sale_exchange_new_items" => "SELECT COUNT(*) FROM sale_exchange_new_items;",
            "cash_movements" => "SELECT COUNT(*) FROM cash_movements;",
            "movements" => "SELECT COUNT(*) FROM movements;",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountExchangeStockMovements()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movements WHERE IFNULL(ref_type,'') = 'sale_exchange';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountNullExchangeMovements()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE IFNULL(ref_type,'') = 'sale_exchange' AND ref_id IS NULL;
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountCashExchange()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cash_movements WHERE IFNULL(ref_type,'') = 'sale_exchange';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<int> CashExchangeRefIds()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ref_id FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale_exchange' AND ref_id IS NOT NULL
            ORDER BY id;
            """;
        var list = new List<int>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(r.GetInt32(0));
        return list;
    }

    private static double LotQty(int productId, string lot)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(quantity),0) FROM product_lots
            WHERE product_id = $pid AND IFNULL(lot_number,'') = $lot;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$lot", lot);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static void InsertRawExchangeMovement(
        int productId, string type, double qty, string operation, int? refId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at,
              operation, ref_type, ref_id
            ) VALUES (
              $pid, $type, $qty, 0, 'legado null', datetime('now','localtime'),
              $op, 'sale_exchange', $refId
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$qty", qty);
        cmd.Parameters.AddWithValue("$op", operation);
        cmd.Parameters.AddWithValue("$refId", refId is int id ? id : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static int InsertLegacySale(int productId, double quantity, DateTime sessionDate, double stockQty)
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
                ) VALUES ($sale, $pid, 'LEG', 'Legado', 'UN', $qty, 10, $sub, $stock);
                """;
            item.Parameters.AddWithValue("$sale", saleId);
            item.Parameters.AddWithValue("$pid", productId);
            item.Parameters.AddWithValue("$qty", quantity);
            item.Parameters.AddWithValue("$sub", quantity * 10);
            item.Parameters.AddWithValue("$stock", stockQty);
            item.ExecuteNonQuery();
        }
        tx.Commit();
        return saleId;
    }

    private static void InsertLegacyExchange(int originalSaleId, int productId, double returnQty, string createdAtUtc)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        int exchangeId;
        using (var ex = conn.CreateCommand())
        {
            ex.Transaction = tx;
            ex.CommandText = """
                INSERT INTO sale_exchanges (
                  original_sale_id, created_at, return_total, new_total, difference
                ) VALUES ($sale, $created, $ret, 0, $diff);
                SELECT last_insert_rowid();
                """;
            ex.Parameters.AddWithValue("$sale", originalSaleId);
            ex.Parameters.AddWithValue("$created", createdAtUtc);
            ex.Parameters.AddWithValue("$ret", returnQty * 10);
            ex.Parameters.AddWithValue("$diff", -returnQty * 10);
            exchangeId = Convert.ToInt32(ex.ExecuteScalar());
        }
        using (var item = conn.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                INSERT INTO sale_exchange_return_items (
                  exchange_id, sale_item_id, product_id, product_code, product_name,
                  qty, unit_price, amount
                ) VALUES ($ex, 0, $pid, 'LEGX', 'Troca Legada', $qty, 10, $amt);
                """;
            item.Parameters.AddWithValue("$ex", exchangeId);
            item.Parameters.AddWithValue("$pid", productId);
            item.Parameters.AddWithValue("$qty", returnQty);
            item.Parameters.AddWithValue("$amt", returnQty * 10);
            item.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private sealed record MovementRef(int ProductId, string Type, string Operation, string RefType, int? RefId);
}
