using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 70F-B2: qualidade de custo/preço de catálogo.
/// Sem I/O, UI, desconto, promoção ou preço recomendado.
/// Não usa último custo, lote nem CMV da venda como média.
/// Não arredonda fatos crus. Não duplica FinalizeSaleCore.
/// </summary>
public static class InventoryCommercialFactsEngine
{
    /// <summary>Poeira monetária alinhada a lote/PDV. Quantidade física continua no epsilon 70C.</summary>
    public const double MoneyEpsilon = ValidityControlEngine.CostAvailableThreshold;

    public static readonly InventoryCommercialFactsReason[] ReasonPrecedence =
    [
        InventoryCommercialFactsReason.MissingProduct,
        InventoryCommercialFactsReason.InvalidCost,
        InventoryCommercialFactsReason.UnknownCost,
        InventoryCommercialFactsReason.InvalidSalePrice,
        InventoryCommercialFactsReason.UnusableSalePrice,
        InventoryCommercialFactsReason.SaleNotAllowed,
        InventoryCommercialFactsReason.CompositionProduct,
        InventoryCommercialFactsReason.AmbiguousSaleUnit,
        InventoryCommercialFactsReason.IncompleteWholesalePricing,
        InventoryCommercialFactsReason.WholesalePricingConfigured,
        InventoryCommercialFactsReason.UnitSalePricingConfigured,
    ];

    public static InventoryCommercialFacts Classify(InventoryCommercialFactsInput? input)
    {
        input ??= new InventoryCommercialFactsInput();
        if (!input.ProductFound)
        {
            return new InventoryCommercialFacts
            {
                ProductId = input.ProductId,
                ProductFound = false,
                PriceQuality = InventoryCommercialPriceQuality.Unavailable,
                CostQuality = InventoryCommercialCostQuality.Unavailable,
                LimitationReasons = [InventoryCommercialFactsReason.MissingProduct],
            };
        }

        var costQuality = ClassifyCost(input.CurrentAverageCost);
        var priceQuality = ClassifyPrice(input.CatalogSalePrice);
        var wholesaleComplete = IsCompleteWholesale(
            input.WholesaleMinimumQuantity, input.WholesalePrice);
        var wholesaleIncomplete = IsIncompleteWholesale(
            input.WholesaleMinimumQuantity, input.WholesalePrice);
        var hasUnitSale = input.IsCigaretteProduct
            && IsPositiveMoney(input.UnitSalePrice);
        var ambiguousUnit = hasUnitSale;
        var reasons = CollectReasons(
            costQuality,
            priceQuality,
            input.AllowsSale,
            input.IsCompositionProduct,
            ambiguousUnit,
            wholesaleComplete,
            wholesaleIncomplete,
            hasUnitSale);

        var canEvaluate = costQuality == InventoryCommercialCostQuality.Known
            && priceQuality == InventoryCommercialPriceQuality.Usable
            && input.AllowsSale
            && !input.IsCompositionProduct
            && !ambiguousUnit;

        return new InventoryCommercialFacts
        {
            ProductId = input.ProductId,
            ProductFound = true,
            CatalogSalePrice = input.CatalogSalePrice,
            CurrentAverageCost = input.CurrentAverageCost,
            PriceQuality = priceQuality,
            CostQuality = costQuality,
            CanEvaluateFinancialScenario = canEvaluate,
            AllowsSale = input.AllowsSale,
            IsCompositionProduct = input.IsCompositionProduct,
            IsCigaretteProduct = input.IsCigaretteProduct,
            HasWholesalePricing = wholesaleComplete,
            WholesalePrice = wholesaleComplete || wholesaleIncomplete
                ? input.WholesalePrice
                : null,
            WholesaleMinimumQuantity = wholesaleComplete || wholesaleIncomplete
                ? input.WholesaleMinimumQuantity
                : null,
            HasUnitSalePricing = hasUnitSale,
            UnitSalePrice = hasUnitSale ? input.UnitSalePrice : null,
            HasSpecialPricingContext = wholesaleComplete
                || wholesaleIncomplete
                || hasUnitSale
                || input.IsCigaretteProduct
                || input.IsCompositionProduct,
            LimitationReasons = reasons,
        };
    }

    public static InventoryCommercialCostQuality ClassifyCost(double cost)
    {
        if (!InventoryIntelligenceEngine.IsFinite(cost)
            || cost < -InventoryIntelligenceEngine.Epsilon)
            return InventoryCommercialCostQuality.Invalid;
        if (cost <= MoneyEpsilon)
            return InventoryCommercialCostQuality.UnknownOrZero;
        return InventoryCommercialCostQuality.Known;
    }

    public static InventoryCommercialPriceQuality ClassifyPrice(double sale)
    {
        if (!InventoryIntelligenceEngine.IsFinite(sale)
            || sale < -InventoryIntelligenceEngine.Epsilon)
            return InventoryCommercialPriceQuality.Invalid;
        if (sale <= MoneyEpsilon)
            return InventoryCommercialPriceQuality.Unusable;
        return InventoryCommercialPriceQuality.Usable;
    }

    static List<InventoryCommercialFactsReason> CollectReasons(
        InventoryCommercialCostQuality costQuality,
        InventoryCommercialPriceQuality priceQuality,
        bool allowsSale,
        bool composition,
        bool ambiguousUnit,
        bool wholesaleComplete,
        bool wholesaleIncomplete,
        bool hasUnitSale)
    {
        var reasons = new List<InventoryCommercialFactsReason>(6);
        if (costQuality == InventoryCommercialCostQuality.Invalid)
            reasons.Add(InventoryCommercialFactsReason.InvalidCost);
        if (costQuality == InventoryCommercialCostQuality.UnknownOrZero)
            reasons.Add(InventoryCommercialFactsReason.UnknownCost);
        if (priceQuality == InventoryCommercialPriceQuality.Invalid)
            reasons.Add(InventoryCommercialFactsReason.InvalidSalePrice);
        if (priceQuality == InventoryCommercialPriceQuality.Unusable)
            reasons.Add(InventoryCommercialFactsReason.UnusableSalePrice);
        if (!allowsSale)
            reasons.Add(InventoryCommercialFactsReason.SaleNotAllowed);
        if (composition)
            reasons.Add(InventoryCommercialFactsReason.CompositionProduct);
        if (ambiguousUnit)
            reasons.Add(InventoryCommercialFactsReason.AmbiguousSaleUnit);
        if (wholesaleIncomplete)
            reasons.Add(InventoryCommercialFactsReason.IncompleteWholesalePricing);
        if (wholesaleComplete)
            reasons.Add(InventoryCommercialFactsReason.WholesalePricingConfigured);
        if (hasUnitSale)
            reasons.Add(InventoryCommercialFactsReason.UnitSalePricingConfigured);
        return reasons;
    }

    static bool IsCompleteWholesale(double qty, double price) =>
        InventoryIntelligenceEngine.IsFinite(qty)
        && qty >= 2
        && IsPositiveMoney(price);

    static bool IsIncompleteWholesale(double qty, double price)
    {
        var qtySet = InventoryIntelligenceEngine.IsFinite(qty) && qty >= 2;
        var priceSet = IsPositiveMoney(price);
        var priceInvalid = InventoryIntelligenceEngine.IsFinite(price)
            && price < -InventoryIntelligenceEngine.Epsilon;
        if (priceInvalid && qtySet)
            return true;
        return qtySet != priceSet;
    }

    static bool IsPositiveMoney(double value) =>
        InventoryIntelligenceEngine.IsFinite(value) && value > MoneyEpsilon;
}
