namespace SGDB.Application.Sales;

/// <summary>
/// Orquestra a alteração de pagamento: valida pré-condições mínimas e delega ao gateway.
/// Não conhece SQLite, WPF, fiado, troco nem a transação —
/// isso permanece em <c>PdvService.ChangeSalePayment</c>.
/// </summary>
public sealed class ChangeSalePaymentUseCase
{
    private readonly IChangeSalePaymentGateway _gateway;

    public ChangeSalePaymentUseCase(IChangeSalePaymentGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public ChangeSalePaymentResult Execute(ChangeSalePaymentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SaleId <= 0)
            throw new ArgumentException("SaleId inválido.", nameof(command));

        if (command.Payments is null)
            throw new ArgumentException("Payments é obrigatório.", nameof(command));

        return _gateway.Change(command);
    }
}
