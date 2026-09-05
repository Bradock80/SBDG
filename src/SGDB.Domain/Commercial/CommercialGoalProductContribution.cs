namespace SGDB.Domain.Commercial;

/// <summary>
/// Limitações V1 da contribuição por produto. Códigos, não textos de UI.
/// </summary>
[Flags]
public enum CommercialGoalProductContributionLimitation
{
    None = 0,
    ExchangesNotAdjusted = 1 << 0,
    HasUnattributedRevenue = 1 << 1,
    HistoricalBomUnavailable = 1 << 2,
}

/// <summary>
/// Contribuição histórica de um SKU na competência. Sem SQL.
/// GrossProfit null = não publicado (período Unavailable ou SKU Unavailable).
/// </summary>
public sealed class CommercialGoalProductContributionRow
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public decimal Revenue { get; init; }
    public decimal Cogs { get; init; }
    public decimal? GrossProfit { get; init; }
    public decimal? GrossMarginPercent { get; init; }
    public decimal? GrossProfitShare { get; init; }
    public CommercialGoalCostQuality CostQuality { get; init; }
    public CommercialGoalProductContributionLimitation Limitations { get; init; }
    public double UnitsSold { get; init; }
    public int SaleCount { get; init; }

    public bool HasLimitation(CommercialGoalProductContributionLimitation limitation) =>
        Limitations.HasFlag(limitation);
}

/// <summary>
/// Snapshot 71B-B8B. Receita/CMV/GP por SKU + parcela não atribuída. 0 SQL próprio.
/// </summary>
public sealed class CommercialGoalProductContributionSnapshot
{
    public const int ExpectedQueryCount = 1;
    public const int OwnQueryCount = 0;

    public CommercialCompetence Competence { get; init; }
    public decimal Revenue { get; init; }
    public decimal UnattributedRevenue { get; init; }
    public decimal Cogs { get; init; }
    public decimal UnattributedCogs { get; init; }
    public decimal? GrossProfit { get; init; }
    public decimal? UnattributedGrossProfit { get; init; }
    public CommercialGoalCostQuality CostQuality { get; init; }
    public bool GrossProfitAvailable { get; init; }
    public int SaleCount { get; init; }
    public int SaleItemCount { get; init; }
    public int QueryCount { get; init; }
    public CommercialGoalProductContributionLimitation Limitations { get; init; }
    public IReadOnlyList<CommercialGoalProductContributionRow> Rows { get; init; } = [];

    public bool HasLimitation(CommercialGoalProductContributionLimitation limitation) =>
        Limitations.HasFlag(limitation);
}
