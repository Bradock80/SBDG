namespace SGDB.Application.Sales;

/// <summary>
/// Entrada da alteração de forma de pagamento.
/// Espelha <c>PdvService.ChangeSalePayment</c>.
/// </summary>
public sealed class ChangeSalePaymentCommand
{
    public int SaleId { get; init; }

    public IReadOnlyList<SalePayment> Payments { get; init; } = [];

    public double CashReceived { get; init; }

    public int? CustomerPersonId { get; init; }

    /// <summary>Data da sessão de caixa; null = decisão do adapter (hoje).</summary>
    public DateTime? SessionDate { get; init; }
}
