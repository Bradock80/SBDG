using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class OpenTabSettlementServiceTests
{
    [Fact]
    public void SettleOpenTab_Atomico_FinalizaVendaEFechaDeck()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 50, notes: "teste");

        const double stockBefore = 50;
        const double qty = 2;
        const double unitPrice = 15;
        var expectedTotal = 30.0;

        var productId = TestDataHelper.SeedSimpleProduct(stockBefore, unitPrice, costPrice: 5);
        var tabId = OpenTabService.Create("Mesa Atômica");
        OpenTabService.AddProduct(tabId, productId, qty, unitPrice);
        var lines = OpenTabService.ToCartLines(tabId).ToList();

        var result = OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Dinheiro",
            CashReceived = expectedTotal,
        });

        Assert.True(result.SaleId > 0);
        Assert.Equal(expectedTotal, result.Total);
        Assert.Equal(stockBefore - qty, TestDataHelper.GetProductStock(productId));

        using var conn = DatabaseService.OpenConnection();

        using (var items = conn.CreateCommand())
        {
            items.CommandText = "SELECT COUNT(*) FROM sale_items WHERE sale_id = $id;";
            items.Parameters.AddWithValue("$id", result.SaleId);
            Assert.Equal(1L, (long)(items.ExecuteScalar() ?? 0L));
        }

        using (var mov = conn.CreateCommand())
        {
            mov.CommandText = """
                SELECT COUNT(*) FROM movements
                WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $sale;
                """;
            mov.Parameters.AddWithValue("$sale", result.SaleId);
            Assert.True(Convert.ToInt32(mov.ExecuteScalar()) >= 1);
        }

        using (var cash = conn.CreateCommand())
        {
            cash.CommandText = """
                SELECT COUNT(*), IFNULL(SUM(amount_in),0)
                FROM cash_movements
                WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $sale;
                """;
            cash.Parameters.AddWithValue("$sale", result.SaleId);
            using var r = cash.ExecuteReader();
            Assert.True(r.Read());
            Assert.True(r.GetInt64(0) >= 1);
            Assert.Equal(expectedTotal, r.GetDouble(1));
        }

        using (var tab = conn.CreateCommand())
        {
            tab.CommandText = "SELECT status, sale_id FROM open_tabs WHERE id = $id;";
            tab.Parameters.AddWithValue("$id", tabId);
            using var r = tab.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("settled", r.GetString(0));
            Assert.Equal(result.SaleId, r.GetInt32(1));
        }
    }

    [Fact]
    public void SettleOpenTab_QuandoFalha_FazRollbackCompleto()
    {
        // Falha natural: deck cancelado após montar o pedido.
        // FinalizeSaleCore grava na TX; MarkSettledCore (RequireOpen) falha → ROLLBACK.
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 50, notes: "teste");

        const double stockBefore = 80;
        const double qty = 5;
        const double unitPrice = 7;
        var expectedTotal = 35.0;

        var productId = TestDataHelper.SeedSimpleProduct(stockBefore, unitPrice, costPrice: 2);
        var tabId = OpenTabService.Create("Deck Rollback");
        OpenTabService.AddProduct(tabId, productId, qty, unitPrice);
        var lines = OpenTabService.ToCartLines(tabId).ToList();

        OpenTabService.Cancel(tabId);

        var salesBefore = CountSales();
        var cashBefore = CountCashSaleMovements();
        var movBefore = CountStockSaleMovements();

        var ex = Assert.Throws<OpenTabException>(() =>
            OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
            {
                Items = lines,
                PaymentType = "Dinheiro",
                CashReceived = expectedTotal,
            }));

        Assert.Contains("fechado ou cancelado", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(salesBefore, CountSales());
        Assert.Equal(0, CountSaleItems());
        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(productId));
        Assert.Equal(cashBefore, CountCashSaleMovements());
        Assert.Equal(movBefore, CountStockSaleMovements());

        using var conn = DatabaseService.OpenConnection();
        using var tab = conn.CreateCommand();
        tab.CommandText = "SELECT status, sale_id FROM open_tabs WHERE id = $id;";
        tab.Parameters.AddWithValue("$id", tabId);
        using var r = tab.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("cancelled", r.GetString(0));
        Assert.True(r.IsDBNull(1));
    }

    [Fact]
    public void SettleOpenTab_DeckJaFechado_NaoGeraSegundaVenda()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 50, notes: "teste");

        const double stockBefore = 30;
        const double qty = 1;
        const double unitPrice = 12;
        var expectedTotal = 12.0;

        var productId = TestDataHelper.SeedSimpleProduct(stockBefore, unitPrice, costPrice: 4);
        var tabId = OpenTabService.Create("Deck Duplo");
        OpenTabService.AddProduct(tabId, productId, qty, unitPrice);
        var lines = OpenTabService.ToCartLines(tabId).ToList();

        var first = OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Dinheiro",
            CashReceived = expectedTotal,
        });

        Assert.Equal(1, CountSales());
        Assert.Equal(stockBefore - qty, TestDataHelper.GetProductStock(productId));

        var ex = Assert.Throws<OpenTabException>(() =>
            OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
            {
                Items = lines,
                PaymentType = "Dinheiro",
                CashReceived = expectedTotal,
            }));

        Assert.Contains("já foi fechado", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountSales());
        Assert.Equal(first.SaleId, GetTabSaleId(tabId));
        Assert.Equal(stockBefore - qty, TestDataHelper.GetProductStock(productId));
    }

    private static int CountSales()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sales;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountSaleItems()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sale_items;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountCashSaleMovements()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cash_movements WHERE IFNULL(ref_type,'') = 'sale';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountStockSaleMovements()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movements WHERE IFNULL(ref_type,'') = 'sale';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetTabSaleId(int tabId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sale_id FROM open_tabs WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", tabId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
