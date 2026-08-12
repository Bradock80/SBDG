using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Adapters;

/// <summary>
/// Adapter App → PdvService.ChangeSalePayment (transação SQLite permanece no service).
/// </summary>
public sealed class ChangeSalePaymentGateway : IChangeSalePaymentGateway
{
    public ChangeSalePaymentResult Change(ChangeSalePaymentCommand command)
    {
        var payments = ToPayments(command);
        var detail = PdvService.ChangeSalePayment(
            command.SaleId,
            payments,
            command.CashReceived,
            command.CustomerPersonId,
            command.SessionDate);

        return new ChangeSalePaymentResult { SaleId = detail.Id };
    }

    /// <summary>Mapeamento Command → lista PdvPaymentPart (testável sem SQLite).</summary>
    public static IReadOnlyList<PdvPaymentPart> ToPayments(ChangeSalePaymentCommand command) =>
        command.Payments.Select(p => new PdvPaymentPart
        {
            PaymentType = p.PaymentType,
            Amount = p.Amount,
        }).ToList();
}
