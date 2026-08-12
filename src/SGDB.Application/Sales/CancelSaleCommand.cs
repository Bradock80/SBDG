namespace SGDB.Application.Sales;

/// <summary>
/// Entrada do cancelamento de venda.
/// Espelha <c>PdvService.CancelSale(saleId, sessionDate)</c> — sem motivo no legado.
/// </summary>
public sealed class CancelSaleCommand
{
    public int SaleId { get; init; }

    /// <summary>Data da sessão de caixa; null = decisão do adapter (hoje).</summary>
    public DateTime? SessionDate { get; init; }
}
