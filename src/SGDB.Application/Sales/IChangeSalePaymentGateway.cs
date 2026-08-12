namespace SGDB.Application.Sales;

/// <summary>
/// Contrato mínimo para alterar pagamento de venda (efeitos/persistência no App).
/// </summary>
public interface IChangeSalePaymentGateway
{
    ChangeSalePaymentResult Change(ChangeSalePaymentCommand command);
}
