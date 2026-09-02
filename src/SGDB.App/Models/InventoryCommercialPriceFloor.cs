namespace SGDB.Models;

/// <summary>
/// Política explícita de margem bruta sobre venda. Sem default de negócio.
/// Ausência de percentual = sem piso calculável.
/// </summary>
public sealed class InventoryCommercialMarginPolicy
{
    public double? MinimumGrossMarginPercent { get; init; }
}

public enum InventoryCommercialPriceFloorStatus
{
    Available = 0,
    PolicyMissing,
    PolicyInvalid,
    CommercialFactsUnavailable,
    FinancialScenarioUnavailable,
}

/// <summary>
/// Motivo atômico do limite. Sem promoção, desconto recomendado ou execução.
/// </summary>
public enum InventoryCommercialPriceFloorReason
{
    None = 0,
    PolicyMissing,
    PolicyInvalid,
    CommercialFactsUnavailable,
    FinancialScenarioUnavailable,
    CurrentPriceBelowMinimumMargin,
}

/// <summary>
/// Limite financeiro 70F-B3. Não é recomendação comercial.
/// AmountAboveMinimumAllowedCatalogPrice é espaço até o piso, não desconto.
/// </summary>
public sealed class InventoryCommercialPriceFloorResult
{
    public int ProductId { get; init; }
    public InventoryCommercialPriceFloorStatus Status { get; init; } =
        InventoryCommercialPriceFloorStatus.CommercialFactsUnavailable;
    public double? MinimumGrossMarginPercent { get; init; }
    public double? CatalogSalePrice { get; init; }
    public double? CurrentAverageCost { get; init; }
    public double? CurrentGrossMarginPercent { get; init; }
    public double? MinimumAllowedCatalogPrice { get; init; }
    public bool MeetsMinimumMargin { get; init; }
    public bool CatalogPriceIsAboveMinimumAllowed { get; init; }
    public double AmountAboveMinimumAllowedCatalogPrice { get; init; }
    public IReadOnlyList<InventoryCommercialPriceFloorReason> Reasons { get; init; } = [];
}
