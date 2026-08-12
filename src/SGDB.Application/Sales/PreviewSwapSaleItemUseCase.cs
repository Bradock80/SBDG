namespace SGDB.Application.Sales;

/// <summary>
/// Orquestra o preview de troca de item: valida IDs e delega ao gateway.
/// Sem SQL, permissão, UI ou efeitos — isso permanece em <c>PdvService.PreviewSwapSaleItem</c>.
/// </summary>
public sealed class PreviewSwapSaleItemUseCase
{
    private readonly IPreviewSwapSaleItemGateway _gateway;

    public PreviewSwapSaleItemUseCase(IPreviewSwapSaleItemGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public PreviewSwapSaleItemResult Execute(PreviewSwapSaleItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SaleId <= 0)
            throw new ArgumentException("SaleId inválido.", nameof(command));
        if (command.ItemId <= 0)
            throw new ArgumentException("ItemId inválido.", nameof(command));
        if (command.NewProductId <= 0)
            throw new ArgumentException("NewProductId inválido.", nameof(command));

        return _gateway.Preview(command);
    }
}
