namespace SGDB.Application.Sales;

/// <summary>
/// Orquestra a finalização de venda PDV: valida pré-condições mínimas e delega ao gateway.
/// Não conhece SQLite, WPF nem a transação — isso permanece em <c>PdvService.FinalizeSale</c>.
/// </summary>
public sealed class FinalizeSaleUseCase
{
    private readonly IFinalizeSaleGateway _gateway;

    public FinalizeSaleUseCase(IFinalizeSaleGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public SaleExecutionResult Execute(FinalizeSaleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Items is null)
            throw new ArgumentException("Items é obrigatório.", nameof(command));

        return _gateway.Finalize(command);
    }
}
