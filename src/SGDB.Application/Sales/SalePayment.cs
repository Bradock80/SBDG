namespace SGDB.Application.Sales;

/// <summary>Parte de pagamento de venda (PDV e deck).</summary>
public sealed class SalePayment
{
    public string PaymentType { get; init; } = "";
    public double Amount { get; init; }
}
