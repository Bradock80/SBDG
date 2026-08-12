using SGDB.Application.OpenTabs;
using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Adapters;

/// <summary>
/// Adapter App → OpenTabSettlementService (transação SQLite permanece no service).
/// </summary>
public sealed class OpenTabSettlementGateway : IOpenTabSettlementGateway
{
    public SaleExecutionResult Settle(SettleOpenTabCommand command)
    {
        var request = new PdvFinalizeRequest
        {
            Items = command.Items.Select(ToCartLine).ToList(),
            PaymentType = command.PaymentType,
            Payments = command.Payments?.Select(p => new PdvPaymentPart
            {
                PaymentType = p.PaymentType,
                Amount = p.Amount,
            }).ToList(),
            Discount = command.Discount,
            Surcharge = command.Surcharge,
            CashReceived = command.CashReceived,
            CustomerPersonId = command.CustomerPersonId,
            SellerId = command.SellerId,
        };

        var result = OpenTabSettlementService.SettleOpenTab(
            command.TabId, request, command.SessionDate);

        return new SaleExecutionResult
        {
            SaleId = result.SaleId,
            Total = result.Total,
            ChangeAmount = result.ChangeAmount,
            CashReceived = result.CashReceived,
        };
    }

    private static PdvCartLine ToCartLine(SaleLine line) =>
        new()
        {
            ProductId = line.ProductId,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            StockUnitsPerSale = line.StockUnitsPerSale,
        };
}
