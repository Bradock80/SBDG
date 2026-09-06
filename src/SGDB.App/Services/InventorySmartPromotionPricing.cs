using SGDB.Domain.Common;
using SGDB.Domain.Products;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Precificação da promoção individual autorizada (25% desejada, 20% mínima).
/// Não grava preço. Não ativa promoção. Consome custo/preço já conhecidos.
/// </summary>
public static class InventorySmartPromotionPricing
{
    public const int ExpectedQueryCount = 0;

    public readonly record struct Result(
        InventorySmartPrincipalAction Action,
        double? SuggestedPrice,
        double? DiscountAmount,
        double? DiscountPercent,
        double? ResultingMarginPercent,
        int? DurationDays,
        DateOnly? ReevaluationDate,
        string Message);

    public static Result Evaluate(
        InventoryCommercialFacts? facts,
        ProductTurnoverRow? turnover,
        InventoryAttentionResult? attention,
        InventoryPromotionSuggestionResult? promotion,
        DateTime? today = null)
    {
        var day = DateOnly.FromDateTime((today ?? DateTime.Today).Date);
        if (facts is null || !facts.ProductFound)
            return Review("Custo ou preço ausente.");
        if (facts.CostQuality != InventoryCommercialCostQuality.Known
            || facts.CurrentAverageCost is not double cost
            || !InventoryIntelligenceEngine.IsFinite(cost)
            || cost <= 0)
            return Review("Custo ausente.");
        if (facts.PriceQuality != InventoryCommercialPriceQuality.Usable
            || facts.CatalogSalePrice is not double price
            || !InventoryIntelligenceEngine.IsFinite(price)
            || price <= 0)
            return Review("Preço ausente.");
        if (price < cost)
            return Review("Preço abaixo do custo.");
        if (!facts.AllowsSale)
            return Review("Produto sem permissão de venda.");

        var currentMargin = ProductPriceCalculator.MarginOnSale(cost, price);
        var urgency = IsUrgent(attention, promotion);
        var daysUntil = attention?.NearestDatedDaysUntilExpiry;
        var vmv = turnover?.Vmv30 ?? 0;
        var stock = turnover?.TotalStock ?? 0;
        if (vmv > InventoryIntelligenceEngine.Epsilon
            && daysUntil is int days
            && days > 0
            && stock > InventoryIntelligenceEngine.Epsilon
            && vmv * days + InventoryIntelligenceEngine.Epsilon >= stock)
        {
            return new Result(
                InventorySmartPrincipalAction.KeepPrice,
                price,
                0,
                0,
                currentMargin,
                null,
                day.AddDays(7),
                "O giro atual é suficiente para vender antes da validade. Mantenha o preço.");
        }

        var targetPercent = urgency
            ? InventorySmartMargin.AbsoluteMinimumPercent
            : InventorySmartMargin.DesiredPercent;
        var floor = (double)InventoryCommercialPriceFloorEngine.ComputeFloor(
            ToDec(cost), ToDec(targetPercent));
        if (floor < cost)
            floor = cost;
        var suggested = MonetaryRounding.Round(floor);
        if (suggested < cost)
        {
            return new Result(
                InventorySmartPrincipalAction.UnsafeLiquidation,
                null,
                null,
                null,
                null,
                null,
                day.AddDays(1),
                "Nenhuma promoção segura dentro da margem mínima de 20%.");
        }

        var margin = ProductPriceCalculator.MarginOnSale(cost, suggested);
        if (!InventorySmartMargin.MeetsAbsoluteMinimum(margin) || suggested < cost)
        {
            return new Result(
                InventorySmartPrincipalAction.NoSafePromotion,
                null,
                null,
                null,
                currentMargin,
                null,
                day.AddDays(7),
                "Nenhuma promoção segura dentro da margem mínima de 20%.");
        }

        if (MonetaryRounding.Round(suggested) >= MonetaryRounding.Round(price) - 0.009)
        {
            return new Result(
                urgency
                    ? InventorySmartPrincipalAction.HighlightWithoutDiscount
                    : InventorySmartPrincipalAction.KeepPrice,
                price,
                0,
                0,
                currentMargin,
                InventorySmartMargin.HighlightDurationDays,
                day.AddDays(7),
                urgency
                    ? "Dê destaque sem desconto: não há espaço seguro de margem."
                    : "Mantenha o preço. Não há espaço seguro para desconto.");
        }

        var discount = MonetaryRounding.Round(price - suggested);
        var percent = price > 0 ? MonetaryRounding.Round(discount / price * 100.0) : 0;
        return new Result(
            InventorySmartPrincipalAction.PromoteIndividual,
            suggested,
            discount,
            percent,
            margin,
            InventorySmartMargin.DefaultPromoDurationDays,
            day.AddDays(InventorySmartMargin.DefaultPromoDurationDays),
            urgency
                ? "Promoção urgente respeitando a margem mínima de 20%."
                : "Promoção segura na margem desejada de 25%.");
    }

    static bool IsUrgent(
        InventoryAttentionResult? attention,
        InventoryPromotionSuggestionResult? promotion)
    {
        if (attention?.Family == InventoryAttentionFamily.Expiry)
            return true;
        if (attention?.Priority is InventoryAttentionPriority.Critical or InventoryAttentionPriority.High)
            return true;
        if (promotion?.Thesis == InventoryCommercialScenarioThesis.ExpirySurplus)
            return true;
        if (attention?.NearestDatedDaysUntilExpiry is int days && days <= 7)
            return true;
        return false;
    }

    static Result Review(string message) =>
        new(InventorySmartPrincipalAction.ReviewData, null, null, null, null, null, null, message);

    static decimal ToDec(double value) => Convert.ToDecimal(value);
}
