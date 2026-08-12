using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Adapters;

/// <summary>
/// Adapter App → PdvService.PreviewSwapSaleItem (sem gravação).
/// </summary>
public sealed class PreviewSwapSaleItemGateway : IPreviewSwapSaleItemGateway
{
    public PreviewSwapSaleItemResult Preview(PreviewSwapSaleItemCommand command)
    {
        var preview = PdvService.PreviewSwapSaleItem(
            command.SaleId,
            command.ItemId,
            command.NewProductId,
            command.KeepLinePrice,
            command.NewQuantity,
            command.SessionDate,
            cigaretteMode: command.CigaretteMode);

        return ToResult(preview);
    }

    /// <summary>Mapeamento PdvSwapItemPreview → Application result (testável sem SQLite).</summary>
    public static PreviewSwapSaleItemResult ToResult(PdvSwapItemPreview preview) =>
        new()
        {
            SaleId = preview.SaleId,
            OldTotal = preview.OldTotal,
            NewTotal = preview.NewTotal,
            Difference = preview.Difference,
            PaymentType = preview.PaymentType,
            CurrentPayments = preview.CurrentPayments
                .Select(p => new SalePayment { PaymentType = p.PaymentType, Amount = p.Amount })
                .ToList(),
            CustomerPersonId = preview.CustomerPersonId,
            IsPureFiado = preview.IsPureFiado,
            RequiresPaymentConfirmation = preview.RequiresPaymentConfirmation,
            RefundHint = preview.RefundHint,
        };
}
