namespace SGDB.Application.Sales;

/// <summary>
/// Resultado mínimo após Swap — o que a View usa (mensagem + refresh).
/// Não transporta PdvSaleDetail / Models do App.
/// </summary>
public sealed class SwapSaleItemResult
{
    public int SaleId { get; init; }
    public double NewTotal { get; init; }
    public double? RefundHint { get; init; }
    public string Message { get; init; } = "";
}
