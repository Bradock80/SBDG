using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Caracterização do comportamento atual de PdvService.FinalizeSale.
/// Não altera produção — documenta efeitos colaterais observados no DB.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvFinalizeSaleTests
{
    [Fact]
    public void FinalizeSale_Dinheiro_CriaVendaItensEstoqueECaixa()
    {
        using var db = TempDatabase.Create();

        // Pré-condição atual: caixa operacional.
        CashService.OpenSession(openingAmount: 50, notes: "teste");

        var productId = TestDataHelper.SeedSimpleProduct(stock: 100, salePrice: 10, costPrice: 4);
        const double qty = 3;
        const double unitPrice = 10;
        var expectedTotal = 30.0;
        var expectedStockAfter = 97.0;

        var result = TestDataHelper.FinalizeSimpleCashSale(productId, qty, unitPrice, expectedTotal);

        Assert.True(result.SaleId > 0);
        Assert.Equal(expectedTotal, result.Total);

        using var conn = DatabaseService.OpenConnection();

        // 1) Venda
        using (var sale = conn.CreateCommand())
        {
            sale.CommandText = """
                SELECT total, payment_type, cancelled, IFNULL(change_amount, 0)
                FROM sales WHERE id = $id;
                """;
            sale.Parameters.AddWithValue("$id", result.SaleId);
            using var r = sale.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(expectedTotal, r.GetDouble(0));
            Assert.Equal("Dinheiro", r.GetString(1));
            Assert.Equal(0, r.GetInt32(2));
        }

        // 2) Itens
        using (var items = conn.CreateCommand())
        {
            items.CommandText = """
                SELECT COUNT(*), SUM(quantity), SUM(subtotal)
                FROM sale_items WHERE sale_id = $id;
                """;
            items.Parameters.AddWithValue("$id", result.SaleId);
            using var r = items.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(1L, r.GetInt64(0));
            Assert.Equal(qty, r.GetDouble(1));
            Assert.Equal(expectedTotal, r.GetDouble(2));
        }

        // 3) Estoque
        Assert.Equal(expectedStockAfter, TestDataHelper.GetProductStock(productId));

        // 4) Movimento de estoque
        using (var mov = conn.CreateCommand())
        {
            mov.CommandText = """
                SELECT COUNT(*), IFNULL(SUM(quantity), 0)
                FROM movements
                WHERE product_id = $pid
                  AND IFNULL(ref_type, '') = 'sale'
                  AND ref_id = $sale;
                """;
            mov.Parameters.AddWithValue("$pid", productId);
            mov.Parameters.AddWithValue("$sale", result.SaleId);
            using var r = mov.ExecuteReader();
            Assert.True(r.Read());
            Assert.True(r.GetInt64(0) >= 1);
            Assert.Equal(qty, r.GetDouble(1));
        }

        // 5) Movimento financeiro (caixa)
        using (var cash = conn.CreateCommand())
        {
            cash.CommandText = """
                SELECT COUNT(*), IFNULL(SUM(amount_in), 0), IFNULL(MAX(kind), '')
                FROM cash_movements
                WHERE IFNULL(ref_type, '') = 'sale'
                  AND ref_id = $sale;
                """;
            cash.Parameters.AddWithValue("$sale", result.SaleId);
            using var r = cash.ExecuteReader();
            Assert.True(r.Read());
            Assert.True(r.GetInt64(0) >= 1);
            Assert.Equal(expectedTotal, r.GetDouble(1));
            Assert.Equal("venda", r.GetString(2));
        }
    }
}
