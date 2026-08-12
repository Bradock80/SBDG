namespace SGDB.Application.Sales;

/// <summary>
/// Orquestra a troca de item: valida pré-condições mínimas e delega ao gateway.
/// Não conhece SQLite, WPF, estoque, pagamento, permissão, auditoria nem a transação —
/// isso permanece em <c>PdvService.SwapSaleItem</c>.
/// </summary>
public sealed class SwapSaleItemUseCase
{
    private readonly ISwapSaleItemGateway _gateway;

    public SwapSaleItemUseCase(ISwapSaleItemGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public SwapSaleItemResult Execute(SwapSaleItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SaleId <= 0)
            throw new ArgumentException("SaleId inválido.", nameof(command));
        if (command.ItemId <= 0)
            throw new ArgumentException("ItemId inválido.", nameof(command));
        if (command.NewProductId <= 0)
            throw new ArgumentException("NewProductId inválido.", nameof(command));

        return _gateway.Swap(command);
    }
}
