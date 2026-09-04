namespace SGDB.Domain.Commercial;

/// <summary>
/// Qualidade do CMV agregado do período da Meta Comercial.
/// Exact = só snapshots; EstimatedLegacy = há fallback; Unavailable = custo inutilizável.
/// </summary>
public enum CommercialGoalCostQuality
{
    Exact = 0,
    EstimatedLegacy,
    Unavailable,
}

/// <summary>
/// Snapshot financeiro mensal 71B-B2. Sem meta, ritmo, UI ou formatação PT-BR.
/// GrossProfit null significa indisponível (nunca 0 como N/A).
/// </summary>
public sealed class CommercialGoalFinancialSnapshot
{
    public const int ExpectedQueryCount = 2;

    public CommercialCompetence Competence { get; init; }

    public decimal NetCommercialRevenue { get; init; }
    public decimal Cogs { get; init; }
    public decimal? GrossProfit { get; init; }

    public CommercialGoalCostQuality CostQuality { get; init; }

    public int SaleCount { get; init; }
    public int SaleItemCount { get; init; }

    public int HistoricalCostItemCount { get; init; }
    public int EstimatedLegacyCostItemCount { get; init; }
    public int UnavailableCostItemCount { get; init; }

    public decimal HistoricalCogs { get; init; }
    public decimal EstimatedLegacyCogs { get; init; }

    public bool ProfitIsEstimated { get; init; }
    public bool GrossProfitAvailable { get; init; }

    public string? CostReliabilityNote { get; init; }
    public string ExchangePnlLimitation { get; init; } = "";
}
