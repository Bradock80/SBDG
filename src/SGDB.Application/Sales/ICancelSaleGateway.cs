namespace SGDB.Application.Sales;

/// <summary>
/// Contrato mínimo para cancelar venda (efeitos/persistência no App).
/// </summary>
public interface ICancelSaleGateway
{
    void Cancel(CancelSaleCommand command);
}
