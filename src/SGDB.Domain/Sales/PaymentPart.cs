namespace SGDB.Domain.Sales;

/// <summary>
/// Parte de pagamento de uma venda (puro). Labels já normalizados pelo App.
/// </summary>
public sealed class PaymentPart
{
    public string PaymentType { get; init; } = "";
    public double Amount { get; init; }
}
