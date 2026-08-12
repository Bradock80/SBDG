namespace SGDB.Application.Sales;

/// <summary>
/// Entrada da troca de item da venda do dia.
/// Espelha <c>PdvService.SwapSaleItem</c>.
/// </summary>
public sealed class SwapSaleItemCommand
{
    public int SaleId { get; init; }
    public int ItemId { get; init; }
    public int NewProductId { get; init; }
    public bool KeepLinePrice { get; init; }
    public double? NewQuantity { get; init; }

    /// <summary>
    /// Pagamentos confirmados pelo operador quando o total mudou (exceto fiado puro).
    /// Null/omitido = não informado (Service decide conforme política 24.5).
    /// </summary>
    public IReadOnlyList<SalePayment>? ConfirmedPayments { get; init; }

    public double CashReceived { get; init; }
    public int? CustomerPersonId { get; init; }

    /// <summary>Data da sessão de caixa; null = decisão do adapter (hoje).</summary>
    public DateTime? SessionDate { get; init; }
}
