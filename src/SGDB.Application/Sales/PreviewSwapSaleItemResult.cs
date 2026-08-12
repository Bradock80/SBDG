namespace SGDB.Application.Sales;

/// <summary>
/// Resultado Application do preview de Swap — só o que a View usa para decidir UI.
/// </summary>
public sealed class PreviewSwapSaleItemResult
{
    public int SaleId { get; init; }
    public double OldTotal { get; init; }
    public double NewTotal { get; init; }
    public double Difference { get; init; }
    public string PaymentType { get; init; } = "";
    public IReadOnlyList<SalePayment> CurrentPayments { get; init; } = [];
    public int? CustomerPersonId { get; init; }
    public bool IsPureFiado { get; init; }
    public bool RequiresPaymentConfirmation { get; init; }
    public double? RefundHint { get; init; }
}
