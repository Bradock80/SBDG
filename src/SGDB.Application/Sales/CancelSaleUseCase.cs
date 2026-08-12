namespace SGDB.Application.Sales;

/// <summary>
/// Orquestra o cancelamento de venda: valida pré-condições mínimas e delega ao gateway.
/// Não conhece SQLite, WPF, permissão, auditoria nem a transação —
/// isso permanece em <c>PdvService.CancelSale</c>.
/// </summary>
public sealed class CancelSaleUseCase
{
    private readonly ICancelSaleGateway _gateway;

    public CancelSaleUseCase(ICancelSaleGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public void Execute(CancelSaleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SaleId <= 0)
            throw new ArgumentException("SaleId inválido.", nameof(command));

        _gateway.Cancel(command);
    }
}
