using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Models;

/// <summary>Textos da visão simples. Sem I/O. Sem códigos técnicos soltos.</summary>
public static class InventorySmartPresentation
{
    public const int ExpectedQueryCount = 0;
    public const string SimpleLabel = "Visão simples";
    public const string DetailedLabel = "Visão detalhada";
    public const string DetailsButton = "Ver detalhes";
    public const string TodayTitle = "O que fazer hoje";
    public const string AreaUrgent = "Urgente";
    public const string AreaBuy = "Comprar";
    public const string AreaSell = "Vender mais rápido";
    public const string AreaFix = "Corrigir dados";
    public const string AreaPreserve = "Preservar";
    public const string NoSafePromotion = "Nenhuma promoção segura dentro da margem mínima de 20%.";
    public const string DesiredMarginCaption = "Margem desejada 25%";
    public const string MinimumMarginCaption = "Margem mínima 20%";

    public static string ActionLabel(
        InventorySmartPrincipalAction action,
        InventoryPurchaseGuidanceResult? guidance = null,
        InventorySmartPromotionPricing.Result? pricing = null) =>
        action switch
        {
            InventorySmartPrincipalAction.ReviewData => "Corrigir dados",
            InventorySmartPrincipalAction.RemoveExpired => "Retirar produto vencido",
            InventorySmartPrincipalAction.BuyNow => BuyNowText(guidance),
            InventorySmartPrincipalAction.BuySoon => BuySoonText(guidance),
            InventorySmartPrincipalAction.DoNotBuy => "Não comprar",
            InventorySmartPrincipalAction.SuspendPurchase => "Suspender compra",
            InventorySmartPrincipalAction.PromoteIndividual => PromoText(pricing),
            InventorySmartPrincipalAction.HighlightWithoutDiscount => "Dar destaque",
            InventorySmartPrincipalAction.KeepPrice => "Manter preço",
            InventorySmartPrincipalAction.EvaluateCombo => "Avaliar combo",
            InventorySmartPrincipalAction.UnsafeLiquidation => "Liquidação não segura — decisão humana",
            InventorySmartPrincipalAction.Monitor => "Acompanhar",
            InventorySmartPrincipalAction.Preserve => "Manter como está",
            InventorySmartPrincipalAction.NoSafePromotion => "Sem promoção segura",
            _ => "Acompanhar",
        };

    public static string UrgencyLabel(InventorySmartUrgency urgency) =>
        urgency switch
        {
            InventorySmartUrgency.Critical => "Urgente",
            InventorySmartUrgency.High => "Atenção",
            InventorySmartUrgency.Attention => "Atenção",
            _ => "Sem risco",
        };

    public static string AreaLabel(InventorySmartArea area) =>
        area switch
        {
            InventorySmartArea.Urgent => AreaUrgent,
            InventorySmartArea.Buy => AreaBuy,
            InventorySmartArea.SellFaster => AreaSell,
            InventorySmartArea.FixData => AreaFix,
            _ => AreaPreserve,
        };

    public static string PackagingText(double quantity, int? packs, double? packFactor)
    {
        var qty = InventoryIntelligencePresentation.FormatQty(quantity);
        if (packs is int n && n > 0 && packFactor is double factor && factor >= 2)
        {
            var packLabel = factor >= 10 ? "caixas" : "fardos";
            var unit = n == 1 ? packLabel.TrimEnd('s') : packLabel;
            return $"Comprar {qty} unidades · {n} {unit} de {InventoryIntelligencePresentation.FormatQty(factor)}";
        }

        return $"Comprar {qty} unidades";
    }

    public static string RejectionLabel(InventoryComboRejectionReason reason) =>
        reason switch
        {
            InventoryComboRejectionReason.InsufficientStock => "Estoque insuficiente",
            InventoryComboRejectionReason.MarginBelowMinimum => "Margem abaixo de 20%",
            InventoryComboRejectionReason.MissingCost => "Custo ausente",
            InventoryComboRejectionReason.MissingPrice => "Preço ausente",
            InventoryComboRejectionReason.IncompatibleValidity => "Validade incompatível",
            InventoryComboRejectionReason.CompanionShortageRisk => "Acompanhante com risco de falta",
            InventoryComboRejectionReason.NoCommercialRelation => "Sem relação comercial adequada",
            InventoryComboRejectionReason.IndividualPromotionSafer => "Promoção individual mais segura",
            InventoryComboRejectionReason.InsufficientData => "Dados insuficientes",
            InventoryComboRejectionReason.BelowCost => "Preço abaixo do custo",
            InventoryComboRejectionReason.NotSellable
                or InventoryComboRejectionReason.InactiveOrUnsellable => "Produto sem permissão de venda",
            InventoryComboRejectionReason.CommerciallyIncompatible => "Combinação comercialmente incompatível",
            InventoryComboRejectionReason.ReturnableUnsupported =>
                InventoryComboLifecycleRules.ReturnableBlockedMessage,
            _ => "Dados insuficientes",
        };

    public static InventorySmartCard ToCard(InventorySmartRecommendation rec) =>
        new()
        {
            ProductId = rec.ProductId,
            ProductCode = rec.ProductCode,
            ProductName = rec.ProductName,
            ProductTitle = string.IsNullOrWhiteSpace(rec.ProductCode)
                ? rec.ProductName
                : $"{rec.ProductCode} — {rec.ProductName}",
            PrincipalAction = rec.PrincipalAction,
            ActionText = rec.ActionText,
            QuantityOrDeadlineText = rec.QuantityOrDeadlineText,
            ReasonText = rec.ReasonText,
            Urgency = rec.Urgency,
            UrgencyText = UrgencyLabel(rec.Urgency),
            Area = rec.Area,
            Tone = ToneOf(rec.PrincipalAction),
            FixLocationText = rec.FixLocationText,
            Alternatives = rec.Alternatives,
            RejectionReasons = rec.ComboRejections.Select(RejectionLabel).ToArray(),
        };

    public static string ToneOf(InventorySmartPrincipalAction action) =>
        action switch
        {
            InventorySmartPrincipalAction.ReviewData
                or InventorySmartPrincipalAction.RemoveExpired
                or InventorySmartPrincipalAction.UnsafeLiquidation => "alert",
            InventorySmartPrincipalAction.BuyNow
                or InventorySmartPrincipalAction.PromoteIndividual => "attention",
            InventorySmartPrincipalAction.BuySoon
                or InventorySmartPrincipalAction.SuspendPurchase
                or InventorySmartPrincipalAction.EvaluateCombo => "notice",
            InventorySmartPrincipalAction.Preserve
                or InventorySmartPrincipalAction.KeepPrice => "positive",
            _ => "info",
        };

    static string BuyNowText(InventoryPurchaseGuidanceResult? guidance) =>
        guidance?.RecommendedQuantity is double qty
            ? PackagingText(qty, guidance.PackCount, guidance.PackFactor)
            : "Comprar agora";

    static string BuySoonText(InventoryPurchaseGuidanceResult? guidance) =>
        guidance?.RecommendedQuantity is double qty
            ? $"Comprar em breve · {PackagingText(qty, guidance.PackCount, guidance.PackFactor)}"
            : "Comprar em breve";

    static string PromoText(InventorySmartPromotionPricing.Result? pricing)
    {
        if (pricing?.DurationDays is int days)
            return $"Fazer promoção por {days} dias";
        return "Fazer promoção";
    }
}
