using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// End-to-end: Use Case → Gateway real → PdvService.SwapSaleItem → SQLite.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class SwapSaleItemUseCaseIntegrationTests
{
    [Fact]
    public void Execute_TotalAumenta_ComConfirmedPayments_ViaApplication()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "teste");
        var a = TestDataHelper.SeedSimpleProduct(20, 90, 40, "A", "A");
        var b = TestDataHelper.SeedSimpleProduct(20, 110, 50, "B", "B");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = a,
                    Quantity = 1,
                    UnitPrice = 90,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            Payments = [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 90 }],
            CashReceived = 90,
        });
        var itemId = GetItemId(sale.SaleId);
        var stockA = TestDataHelper.GetProductStock(a);
        var stockB = TestDataHelper.GetProductStock(b);

        var preview = ApplicationServices.PreviewSwapSaleItem.Execute(new PreviewSwapSaleItemCommand
        {
            SaleId = sale.SaleId,
            ItemId = itemId,
            NewProductId = b,
            KeepLinePrice = false,
        });
        Assert.True(preview.RequiresPaymentConfirmation);
        Assert.Equal(20, preview.Difference);

        var result = ApplicationServices.SwapSaleItem.Execute(new SwapSaleItemCommand
        {
            SaleId = sale.SaleId,
            ItemId = itemId,
            NewProductId = b,
            KeepLinePrice = false,
            ConfirmedPayments =
            [
                new SalePayment { PaymentType = "Dinheiro", Amount = 110 },
            ],
            CashReceived = 110,
        });

        Assert.Equal(sale.SaleId, result.SaleId);
        Assert.Equal(110, result.NewTotal);
        Assert.Null(result.RefundHint);

        Assert.Equal(b, GetItemProductId(itemId));
        Assert.Equal(stockA + 1, TestDataHelper.GetProductStock(a)); // restaurado
        Assert.Equal(stockB - 1, TestDataHelper.GetProductStock(b));
        Assert.Equal(110, GetSaleTotal(sale.SaleId));

        var cash = GetCash(sale.SaleId);
        Assert.Single(cash);
        Assert.Equal("Dinheiro", cash[0].PaymentType);
        Assert.Equal(110, cash[0].AmountIn);
        Assert.DoesNotContain(cash, c => c.PaymentType == "Pix");

        Assert.Equal(1, CountAuditTrocarItem(sale.SaleId));
    }

    private static int GetItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetItemProductId(int itemId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM sale_items WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", itemId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double GetSaleTotal(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT total FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static List<(string PaymentType, double AmountIn)> GetCash(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(payment_type,''), IFNULL(amount_in,0)
            FROM cash_movements
            WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id
            ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<(string, double)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.GetDouble(1)));
        return list;
    }

    private static int CountAuditTrocarItem(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM audit_log
            WHERE action = 'trocar_item' AND entity = 'venda' AND entity_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
