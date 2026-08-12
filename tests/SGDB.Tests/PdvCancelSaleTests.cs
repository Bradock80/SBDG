using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Caracterização do comportamento atual de PdvService.CancelSale.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvCancelSaleTests
{
    [Fact]
    public void CancelSale_RestauraEstoqueRemoveCaixaEMarcaCancelled()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.GrantPdvCancelPermission();

        CashService.OpenSession(openingAmount: 50, notes: "teste");

        const double stockBefore = 100;
        const double qty = 4;
        const double unitPrice = 10;
        var expectedTotal = 40.0;
        var expectedStockAfterSale = 96.0;

        var productId = TestDataHelper.SeedSimpleProduct(stockBefore, unitPrice, costPrice: 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, qty, unitPrice, expectedTotal);

        Assert.Equal(expectedStockAfterSale, TestDataHelper.GetProductStock(productId));
        Assert.True(CountCashMovementsForSale(sale.SaleId) >= 1);
        Assert.True(CountStockMovements(productId, "sale", sale.SaleId) >= 1);

        PdvService.CancelSale(sale.SaleId);

        using var conn = DatabaseService.OpenConnection();

        // Venda marcada como cancelada; linha permanece
        using (var saleCmd = conn.CreateCommand())
        {
            saleCmd.CommandText = "SELECT cancelled, total FROM sales WHERE id = $id;";
            saleCmd.Parameters.AddWithValue("$id", sale.SaleId);
            using var r = saleCmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(1, r.GetInt32(0));
            Assert.Equal(expectedTotal, r.GetDouble(1));
        }

        // Itens permanecem (comportamento atual: não apaga sale_items)
        using (var items = conn.CreateCommand())
        {
            items.CommandText = "SELECT COUNT(*), SUM(quantity) FROM sale_items WHERE sale_id = $id;";
            items.Parameters.AddWithValue("$id", sale.SaleId);
            using var r = items.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(1L, r.GetInt64(0));
            Assert.Equal(qty, r.GetDouble(1));
        }

        // Estoque restaurado
        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(productId));

        // Movimento de restauração (ref_type sale_cancel)
        using (var restore = conn.CreateCommand())
        {
            restore.CommandText = """
                SELECT COUNT(*), IFNULL(SUM(quantity), 0), IFNULL(MAX(movement_type), '')
                FROM movements
                WHERE product_id = $pid
                  AND IFNULL(ref_type, '') = 'sale_cancel'
                  AND ref_id = $sale;
                """;
            restore.Parameters.AddWithValue("$pid", productId);
            restore.Parameters.AddWithValue("$sale", sale.SaleId);
            using var r = restore.ExecuteReader();
            Assert.True(r.Read());
            Assert.True(r.GetInt64(0) >= 1);
            Assert.Equal(qty, r.GetDouble(1));
            Assert.Equal("entrada", r.GetString(2));
        }

        // Movimentos de caixa da venda removidos (DeleteSaleMovements)
        Assert.Equal(0, CountCashMovementsForSale(sale.SaleId));
    }

    private static int CountCashMovementsForSale(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM cash_movements
            WHERE IFNULL(ref_type, '') = 'sale' AND ref_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static long CountStockMovements(int productId, string refType, int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE product_id = $pid
              AND IFNULL(ref_type, '') = $ref
              AND ref_id = $sale;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$ref", refType);
        cmd.Parameters.AddWithValue("$sale", saleId);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }
}
