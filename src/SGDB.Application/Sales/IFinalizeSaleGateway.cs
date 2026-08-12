namespace SGDB.Application.Sales;

/// <summary>
/// Contrato mínimo para finalizar venda PDV (efeitos/persistência no App).
/// </summary>
public interface IFinalizeSaleGateway
{
    SaleExecutionResult Finalize(FinalizeSaleCommand command);
}
