using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Adapters;

/// <summary>
/// Adapter App → PdvService.SwapSaleItem (transação SQLite permanece no service).
/// </summary>
public sealed class SwapSaleItemGateway : ISwapSaleItemGateway
{
    public SwapSaleItemResult Swap(SwapSaleItemCommand command)
    {
        var confirmed = ToConfirmedPayments(command);
        var result = PdvService.SwapSaleItem(
            command.SaleId,
            command.ItemId,
            command.NewProductId,
            command.KeepLinePrice,
            command.NewQuantity,
            command.SessionDate,
            confirmedPayments: confirmed,
            cashReceived: command.CashReceived,
            customerPersonId: command.CustomerPersonId,
            cigaretteMode: command.CigaretteMode);

        return ToResult(result);
    }

    /// <summary>Mapeamento ConfirmedPayments → PdvPaymentPart (testável sem SQLite).</summary>
    public static IReadOnlyList<PdvPaymentPart>? ToConfirmedPayments(SwapSaleItemCommand command)
    {
        if (command.ConfirmedPayments is null)
            return null;
        return command.ConfirmedPayments
            .Select(p => new PdvPaymentPart
            {
                PaymentType = p.PaymentType,
                Amount = p.Amount,
            })
            .ToList();
    }

    /// <summary>Mapeamento PdvSwapItemResult → Application result (testável sem SQLite).</summary>
    public static SwapSaleItemResult ToResult(PdvSwapItemResult result) =>
        new()
        {
            SaleId = result.Sale.Id,
            NewTotal = result.Sale.Total,
            RefundHint = result.RefundHint,
            Message = result.Message,
        };
}
