using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// End-to-end: Use Case → Gateway real → PdvService.ChangeSalePayment → SQLite.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class ChangeSalePaymentUseCaseIntegrationTests
{
    [Fact]
    public void Execute_DinheiroParaPix_ViaApplication_AtualizaCaixaSemMexerEstoque()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(100, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 3, 10, 30);
        var stockBefore = TestDataHelper.GetProductStock(productId);

        var result = ApplicationServices.ChangeSalePayment.Execute(new ChangeSalePaymentCommand
        {
            SaleId = sale.SaleId,
            Payments = [new SalePayment { PaymentType = "Pix", Amount = 30 }],
            CashReceived = 0,
        });

        Assert.Equal(sale.SaleId, result.SaleId);

        using var conn = DatabaseService.OpenConnection();
        using (var saleCmd = conn.CreateCommand())
        {
            saleCmd.CommandText = "SELECT payment_type, total FROM sales WHERE id = $id;";
            saleCmd.Parameters.AddWithValue("$id", sale.SaleId);
            using var r = saleCmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("Pix", r.GetString(0));
            Assert.Equal(30, r.GetDouble(1));
        }

        using (var cash = conn.CreateCommand())
        {
            cash.CommandText = """
                SELECT COUNT(*), IFNULL(SUM(amount_in),0), IFNULL(payment_type,'')
                FROM cash_movements
                WHERE IFNULL(ref_type,'') = 'sale' AND ref_id = $id;
                """;
            cash.Parameters.AddWithValue("$id", sale.SaleId);
            using var r = cash.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(1L, r.GetInt64(0));
            Assert.Equal(30, r.GetDouble(1));
            Assert.Equal("Pix", r.GetString(2));
        }

        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(productId));
    }
}
