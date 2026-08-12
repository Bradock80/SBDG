using SGDB.Application.Sales;
using SGDB.Services;

namespace SGDB.Adapters;

/// <summary>
/// Adapter App → PdvService.CancelSale (transação SQLite permanece no service).
/// </summary>
public sealed class CancelSaleGateway : ICancelSaleGateway
{
    public void Cancel(CancelSaleCommand command) =>
        PdvService.CancelSale(command.SaleId, command.SessionDate);
}
