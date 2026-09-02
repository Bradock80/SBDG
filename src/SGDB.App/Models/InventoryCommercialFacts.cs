namespace SGDB.Models;

/// <summary>
/// Qualidade do custo médio de catálogo (<c>products.cost_price</c>).
/// Zero não é custo conhecido. Sem fallback para último custo ou lote.
/// </summary>
public enum InventoryCommercialCostQuality
{
    Known = 0,
    UnknownOrZero,
    Invalid,
    Unavailable,
}

/// <summary>
/// Qualidade do preço-base de catálogo (<c>products.sale_price</c>).
/// Não afirma preço final do PDV.
/// </summary>
public enum InventoryCommercialPriceQuality
{
    Usable = 0,
    Unusable,
    Invalid,
    Unavailable,
}

/// <summary>
/// Fato, limitação ou bloqueio comercial 70F-B2. Sem texto PT-BR.
/// WholesalePricingConfigured e UnitSalePricingConfigured são contexto, não bloqueio.
/// </summary>
public enum InventoryCommercialFactsReason
{
    None = 0,
    MissingProduct,
    InvalidCost,
    UnknownCost,
    InvalidSalePrice,
    UnusableSalePrice,
    SaleNotAllowed,
    CompositionProduct,
    AmbiguousSaleUnit,
    IncompleteWholesalePricing,
    WholesalePricingConfigured,
    UnitSalePricingConfigured,
}

/// <summary>
/// Entrada pura do classificador 70F-B2. Valores crus, sem arredondamento.
/// </summary>
public sealed class InventoryCommercialFactsInput
{
    public int ProductId { get; init; }
    public bool ProductFound { get; init; }
    public double CatalogSalePrice { get; init; }
    public double CurrentAverageCost { get; init; }
    public bool AllowsSale { get; init; } = true;
    public bool IsCompositionProduct { get; init; }
    public bool IsCigaretteProduct { get; init; }
    public double WholesalePrice { get; init; }
    public double WholesaleMinimumQuantity { get; init; }
    public double UnitSalePrice { get; init; }
}

/// <summary>
/// Fatos comerciais de um SKU. Não é decisão, desconto, promoção nem margem mínima.
/// CatalogSalePrice é referência de catálogo, não o total cobrado no PDV.
/// </summary>
public sealed class InventoryCommercialFacts
{
    public int ProductId { get; init; }
    public bool ProductFound { get; init; }
    public double? CatalogSalePrice { get; init; }
    public double? CurrentAverageCost { get; init; }
    public InventoryCommercialPriceQuality PriceQuality { get; init; } =
        InventoryCommercialPriceQuality.Unavailable;
    public InventoryCommercialCostQuality CostQuality { get; init; } =
        InventoryCommercialCostQuality.Unavailable;
    public bool CanEvaluateFinancialScenario { get; init; }
    public bool AllowsSale { get; init; }
    public bool IsCompositionProduct { get; init; }
    public bool IsCigaretteProduct { get; init; }
    public bool HasWholesalePricing { get; init; }
    public double? WholesalePrice { get; init; }
    public double? WholesaleMinimumQuantity { get; init; }
    public bool HasUnitSalePricing { get; init; }
    public double? UnitSalePrice { get; init; }
    public bool HasSpecialPricingContext { get; init; }
    public IReadOnlyList<InventoryCommercialFactsReason> LimitationReasons { get; init; } = [];
}

/// <summary>
/// Lote 70F-B2. QueryCount = 1 com IDs; 0 se a lista efetiva for vazia.
/// </summary>
public sealed class InventoryCommercialFactsSnapshot
{
    public int QueryCount { get; init; }
    public IReadOnlyList<int> RequestedProductIds { get; init; } = [];
    public IReadOnlyList<InventoryCommercialFacts> Rows { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryCommercialFacts> ByProductId { get; init; } =
        new Dictionary<int, InventoryCommercialFacts>();
}
