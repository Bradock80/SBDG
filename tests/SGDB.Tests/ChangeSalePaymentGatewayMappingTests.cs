using SGDB.Adapters;
using SGDB.Application.Sales;

namespace SGDB.Tests;

/// <summary>
/// Paridade de mapeamento Command → PdvPaymentPart (sem SQLite).
/// </summary>
public class ChangeSalePaymentGatewayMappingTests
{
    [Fact]
    public void ToPayments_MapsAllFields_WithoutLoss()
    {
        var command = new ChangeSalePaymentCommand
        {
            SaleId = 42,
            Payments =
            [
                new SalePayment { PaymentType = "Dinheiro", Amount = 10.5 },
                new SalePayment { PaymentType = "Pix", Amount = 19.5 },
            ],
            CashReceived = 50,
            CustomerPersonId = 7,
            SessionDate = new DateTime(2026, 8, 11),
        };

        var parts = ChangeSalePaymentGateway.ToPayments(command);

        Assert.Equal(2, parts.Count);
        Assert.Equal("Dinheiro", parts[0].PaymentType);
        Assert.Equal(10.5, parts[0].Amount);
        Assert.Equal("Pix", parts[1].PaymentType);
        Assert.Equal(19.5, parts[1].Amount);
    }

    [Fact]
    public void ToPayments_EmptyList_Preserved()
    {
        var parts = ChangeSalePaymentGateway.ToPayments(new ChangeSalePaymentCommand
        {
            SaleId = 1,
            Payments = [],
        });
        Assert.Empty(parts);
    }
}
