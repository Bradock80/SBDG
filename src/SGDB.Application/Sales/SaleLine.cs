namespace SGDB.Application.Sales;

/// <summary>Linha de venda compartilhada (PDV e deck). Sem display/UI.</summary>
public sealed class SaleLine
{
    public int ProductId { get; init; }
    public double Quantity { get; init; }
    public double UnitPrice { get; init; }
    /// <summary>Multiplicador de estoque (1 maço = N unidades).</summary>
    public double StockUnitsPerSale { get; init; } = 1;
}
