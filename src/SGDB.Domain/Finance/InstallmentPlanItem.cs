namespace SGDB.Domain.Finance;

/// <summary>
/// Parcela calculada (puro). Sem formatação BR, IDs ou UI.
/// </summary>
public sealed class InstallmentPlanItem
{
    /// <summary>Ordem 1-based no plano gerado.</summary>
    public int Number { get; init; }

    public DateTime DueDate { get; init; }

    public double Amount { get; init; }

    /// <summary>
    /// Tipo de cobrança legado: "Dinheiro" (entrada) ou "Boleto" (parcelas).
    /// </summary>
    public string ChargeType { get; init; } = "Boleto";
}
