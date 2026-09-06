namespace SGDB.Models;

/// <summary>
/// Margem comercial autorizada na ETAPA 72 para promoção individual e combo.
/// Não grava app_settings. Não altera o contrato 70F-B3 (sem default persistido).
/// Piso absoluto 20%; desejada 25%. Nunca recomenda abaixo do custo.
/// </summary>
public static class InventorySmartMargin
{
    public const double DesiredPercent = 25;
    public const double AbsoluteMinimumPercent = 20;
    public const int DefaultPromoDurationDays = 7;
    public const int HighlightDurationDays = 7;

    public static double EffectiveMinimumPercent(double? configuredPercent)
    {
        var configured = configuredPercent is double value
            && InventoryIntelligenceEngineIsFinite(value)
            && value >= 0
            && value < 100
            ? value
            : AbsoluteMinimumPercent;
        return Math.Max(AbsoluteMinimumPercent, configured);
    }

    public static double EffectiveMinimumPercent(InventoryCommercialMarginPolicy? policy) =>
        EffectiveMinimumPercent(policy?.MinimumGrossMarginPercent);

    public static double EffectiveMinimumPercent(InventoryCommercialMarginPolicyResolution? resolution)
    {
        if (resolution?.Status == InventoryCommercialMarginPolicyResolutionStatus.Available
            && resolution.EffectiveMinimumGrossMarginPercent is decimal value)
        {
            return EffectiveMinimumPercent((double)value);
        }

        return AbsoluteMinimumPercent;
    }

    public static InventoryCommercialMarginPolicy RecommendationPolicy(
        InventoryCommercialMarginPolicyResolution? resolution = null) =>
        new()
        {
            MinimumGrossMarginPercent = EffectiveMinimumPercent(resolution),
        };

    public static bool MeetsAbsoluteMinimum(double marginPercent) =>
        InventoryIntelligenceEngineIsFinite(marginPercent)
        && marginPercent + 0.0001 >= AbsoluteMinimumPercent;

    public static bool MeetsDesired(double marginPercent) =>
        InventoryIntelligenceEngineIsFinite(marginPercent)
        && marginPercent + 0.0001 >= DesiredPercent;

    static bool InventoryIntelligenceEngineIsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
