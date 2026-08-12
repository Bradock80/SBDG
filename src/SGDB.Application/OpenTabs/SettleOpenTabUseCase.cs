using SGDB.Application.Sales;

namespace SGDB.Application.OpenTabs;

/// <summary>
/// Orquestra o fechamento de deck: valida pré-condições mínimas e delega ao gateway.
/// Não conhece SQLite, WPF nem a transação — isso permanece no App.
/// </summary>
public sealed class SettleOpenTabUseCase
{
    private readonly IOpenTabSettlementGateway _gateway;

    public SettleOpenTabUseCase(IOpenTabSettlementGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public SaleExecutionResult Execute(SettleOpenTabCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.TabId <= 0)
            throw new ArgumentException("TabId inválido.", nameof(command));

        if (command.Items is null)
            throw new ArgumentException("Items é obrigatório.", nameof(command));

        return _gateway.Settle(command);
    }
}
