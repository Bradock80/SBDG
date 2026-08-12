namespace SGDB.Application.Sales;

/// <summary>
/// Contrato mínimo para preview de troca de item (leitura no App).
/// </summary>
public interface IPreviewSwapSaleItemGateway
{
    PreviewSwapSaleItemResult Preview(PreviewSwapSaleItemCommand command);
}
