using SGDB.Adapters;
using SGDB.Application.Sales;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// Paridade de mapeamento Command ↔ tipos App (sem SQLite).
/// </summary>
public class FinalizeSaleGatewayMappingTests
{
    [Fact]
    public void ToRequest_MapsAllFields_WithoutLoss()
    {
        var command = new FinalizeSaleCommand
        {
            Items =
            [
                new SaleLine
                {
                    ProductId = 11,
                    Quantity = 2.5,
                    UnitPrice = 3.49,
                    StockUnitsPerSale = 20,
                },
                new SaleLine
                {
                    ProductId = 22,
                    Quantity = 1,
                    UnitPrice = 10,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Misto",
            Payments =
            [
                new SalePayment { PaymentType = "Dinheiro", Amount = 5 },
                new SalePayment { PaymentType = "Pix", Amount = 8.725 },
                new SalePayment { PaymentType = "Fiado", Amount = 5 },
            ],
            Discount = 1.25,
            Surcharge = 0.5,
            CashReceived = 5,
            CustomerPersonId = 77,
            SellerId = 3,
            SessionDate = new DateTime(2026, 8, 11),
        };

        var request = FinalizeSaleGateway.ToRequest(command);

        Assert.Equal(2, request.Items.Count);
        Assert.Equal(11, request.Items[0].ProductId);
        Assert.Equal(2.5, request.Items[0].Quantity);
        Assert.Equal(3.49, request.Items[0].UnitPrice);
        Assert.Equal(20, request.Items[0].StockUnitsPerSale);
        Assert.Equal(50, request.Items[0].StockQuantity); // 2.5 × 20

        Assert.Equal(22, request.Items[1].ProductId);
        Assert.Equal(1, request.Items[1].Quantity);
        Assert.Equal(10, request.Items[1].UnitPrice);
        Assert.Equal(1, request.Items[1].StockUnitsPerSale);

        Assert.Equal("Misto", request.PaymentType);
        Assert.NotNull(request.Payments);
        Assert.Equal(3, request.Payments!.Count);
        Assert.Equal("Dinheiro", request.Payments[0].PaymentType);
        Assert.Equal(5, request.Payments[0].Amount);
        Assert.Equal("Pix", request.Payments[1].PaymentType);
        Assert.Equal(8.725, request.Payments[1].Amount);
        Assert.Equal("Fiado", request.Payments[2].PaymentType);
        Assert.Equal(5, request.Payments[2].Amount);

        Assert.Equal(1.25, request.Discount);
        Assert.Equal(0.5, request.Surcharge);
        Assert.Equal(5, request.CashReceived);
        Assert.Equal(77, request.CustomerPersonId);
        Assert.Equal(3, request.SellerId);
    }

    [Fact]
    public void ToRequest_NullPaymentsAndNullableIds_Preserved()
    {
        var request = FinalizeSaleGateway.ToRequest(new FinalizeSaleCommand
        {
            Items =
            [
                new SaleLine { ProductId = 1, Quantity = 1, UnitPrice = 1 },
            ],
            PaymentType = "Dinheiro",
            Payments = null,
            CustomerPersonId = null,
            SellerId = null,
        });

        Assert.Null(request.Payments);
        Assert.Null(request.CustomerPersonId);
        Assert.Null(request.SellerId);
    }

    [Fact]
    public void ToResult_MapsSaleFields()
    {
        var result = FinalizeSaleGateway.ToResult(new PdvFinalizeResult
        {
            SaleId = 55,
            Total = 100.1,
            ChangeAmount = 0.1,
            CashReceived = 100.2,
        });

        Assert.Equal(55, result.SaleId);
        Assert.Equal(100.1, result.Total);
        Assert.Equal(0.1, result.ChangeAmount);
        Assert.Equal(100.2, result.CashReceived);
    }
}
