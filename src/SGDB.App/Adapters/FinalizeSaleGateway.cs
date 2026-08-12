using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Adapters;

/// <summary>
/// Adapter App → PdvService.FinalizeSale (transação SQLite permanece no service).
/// </summary>
public sealed class FinalizeSaleGateway : IFinalizeSaleGateway
{
    public SaleExecutionResult Finalize(FinalizeSaleCommand command)
    {
        var request = ToRequest(command);
        var result = PdvService.FinalizeSale(request, command.SessionDate);
        return ToResult(result);
    }

    /// <summary>Mapeamento Command → PdvFinalizeRequest (testável sem SQLite).</summary>
    public static PdvFinalizeRequest ToRequest(FinalizeSaleCommand command) =>
        new()
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

    public static SaleExecutionResult ToResult(PdvFinalizeResult result) =>
        new()
        {
            SaleId = result.SaleId,
            Total = result.Total,
            ChangeAmount = result.ChangeAmount,
            CashReceived = result.CashReceived,
        };

    private static PdvCartLine ToCartLine(SaleLine line) =>
        new()
        {
            ProductId = line.ProductId,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            StockUnitsPerSale = line.StockUnitsPerSale,
        };
}
