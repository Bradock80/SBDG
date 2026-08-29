namespace SGDB.Models;

/// <summary>
/// Situação objetiva do giro físico (70C-B1). Não inclui Loss/AtRisk/BuyNow/Stuck.
/// Silêncio 30/60/90 é filtro derivado (HistoryDays + DaysWithoutSale), não valor deste enum.
/// </summary>
public enum InventoryTurnoverSituation
{
    Normal = 0,
    LowCoverage,
    NoTurnover,
    ZeroStock,
    NegativeStock,
    InsufficientHistory,
    NeverSold,
}

/// <summary>
/// Estado da cobertura comercial. CoverageDays só é preenchido quando Calculable.
/// </summary>
public enum InventoryCoverageState
{
    Calculable = 0,
    ZeroStock,
    NegativeStock,
    NoTurnover,
}

/// <summary>
/// Linha analítica de giro físico por SKU atual (já remapado / KEEP).
/// VMV operacional = MAX(0, vendas brutas − devoluções) / denominador civil.
/// </summary>
public sealed class ProductTurnoverRow
{
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";

    public double Stock { get; init; }
    public double StockFridge { get; init; }
    public double TotalStock { get; init; }

    public double Vmv7 { get; init; }
    public double Vmv30 { get; init; }
    public double Vmv90 { get; init; }

    /// <summary>Null quando não for matematicamente calculável (nunca Infinity/NaN).</summary>
    public double? CoverageDays { get; init; }
    public InventoryCoverageState CoverageState { get; init; }

    /// <summary>
    /// Última data civil com saída de venda válida não cancelada.
    /// Devolução no mesmo dia ou posterior não apaga esse fato.
    /// </summary>
    public DateTime? LastValidSaleDate { get; init; }

    /// <summary>Null quando nunca houve saída válida (NuncaVendido).</summary>
    public int? DaysWithoutSale { get; init; }

    public int HistoryDays { get; init; }
    public bool IsHistoryInsufficient7 { get; init; }
    public bool IsHistoryInsufficient30 { get; init; }
    public bool IsHistoryInsufficient90 { get; init; }

    /// <summary>
    /// True se houve compra fechada com estoque, entrada física confiável ou venda válida.
    /// Cadastro isolado não conta.
    /// </summary>
    public bool HasPhysicalAvailabilityEvidence { get; init; }

    public InventoryTurnoverSituation Situation { get; init; }

    /// <summary>
    /// Filtro futuro 30/60/90 de silêncio: exige evidência física e idade observável.
    /// Produto só cadastrado, sem entrada/venda, não entra no filtro.
    /// </summary>
    public bool QualifiesForDaysWithoutSaleFilter(int minDays)
    {
        if (minDays <= 0)
            return false;
        if (!HasPhysicalAvailabilityEvidence)
            return false;
        if (HistoryDays < minDays)
            return false;
        if (LastValidSaleDate is null)
            return true;
        return (DaysWithoutSale ?? 0) >= minDays;
    }
}

/// <summary>Resultado em lote do motor 70C-B1. QueryCount é constante (sem N+1).</summary>
public sealed class InventoryIntelligenceSnapshot
{
    public DateTime Today { get; init; }
    public int QueryCount { get; init; }
    public IReadOnlyList<ProductTurnoverRow> Rows { get; init; } = [];
}
