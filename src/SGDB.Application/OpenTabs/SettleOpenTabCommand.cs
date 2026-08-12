using SGDB.Application.Sales;

namespace SGDB.Application.OpenTabs;

/// <summary>
/// Entrada do caso de uso de fechamento de deck.
/// Espelha os dados de <c>PdvFinalizeRequest</c> + tabId (+ sessão opcional).
/// </summary>
public sealed class SettleOpenTabCommand
{
    public int TabId { get; init; }

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
