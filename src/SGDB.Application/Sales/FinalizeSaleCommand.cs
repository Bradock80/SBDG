namespace SGDB.Application.Sales;

/// <summary>
/// Entrada do caso de uso de finalização de venda PDV.
/// Espelha <c>PdvFinalizeRequest</c> (+ sessão opcional).
/// </summary>
public sealed class FinalizeSaleCommand
{
    public IReadOnlyList<SaleLine> Items { get; init; } = [];

    public string PaymentType { get; init; } = "Dinheiro";

    public IReadOnlyList<SalePayment>? Payments { get; init; }

    public double Discount { get; init; }

    public double Surcharge { get; init; }

    public double CashReceived { get; init; }

    public int? CustomerPersonId { get; init; }

    public int? SellerId { get; init; }

    /// <summary>Data da sessão de caixa; null = decisão do adapter (hoje).</summary>
    public DateTime? SessionDate { get; init; }
}
