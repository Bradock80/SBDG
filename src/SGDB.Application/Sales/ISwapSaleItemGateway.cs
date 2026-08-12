namespace SGDB.Application.Sales;

/// <summary>
/// Contrato mínimo para executar troca de item (efeitos/persistência no App).
/// </summary>
public interface ISwapSaleItemGateway
{
    SwapSaleItemResult Swap(SwapSaleItemCommand command);
}
