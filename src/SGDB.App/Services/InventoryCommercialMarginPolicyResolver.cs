using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Resolver puro 70F-B3C: setting persistido → política global efetiva.
/// Sem SQL, UI, default de negócio, grupo ou produto.
/// </summary>
public static class InventoryCommercialMarginPolicyResolver
{
    public const int ExpectedQueryCount = 0;

    public static InventoryCommercialMarginPolicyResolution Resolve(
        InventoryCommercialMarginSetting? setting)
    {
        if (setting is null || setting.Status == InventoryCommercialMarginSettingStatus.Missing)
            return Missing(setting?.Reasons);

        if (setting.Status == InventoryCommercialMarginSettingStatus.Invalid)
            return Invalid(setting.Reasons);

        if (setting.MinimumGrossMarginPercent is decimal value)
            return Available(value);

        return Invalid(setting.Reasons);
    }

    /// <summary>
    /// Única conversão decimal → double, na borda do B3.
    /// Só Available produz política; Missing e Invalid não fabricam valor.
    /// </summary>
    public static InventoryCommercialMarginPolicy? TryCreatePriceFloorPolicy(
        InventoryCommercialMarginPolicyResolution? resolution)
    {
        if (resolution is null
            || resolution.Status != InventoryCommercialMarginPolicyResolutionStatus.Available
            || resolution.Source != InventoryCommercialMarginPolicySource.Global
            || resolution.EffectiveMinimumGrossMarginPercent is not decimal value)
            return null;

        return new InventoryCommercialMarginPolicy
        {
            MinimumGrossMarginPercent = decimal.ToDouble(value),
        };
    }

    static InventoryCommercialMarginPolicyResolution Available(decimal value) =>
        new()
        {
            Status = InventoryCommercialMarginPolicyResolutionStatus.Available,
            Source = InventoryCommercialMarginPolicySource.Global,
            EffectiveMinimumGrossMarginPercent = value,
        };

    static InventoryCommercialMarginPolicyResolution Missing(
        IReadOnlyList<InventoryCommercialMarginSettingReason>? reasons) =>
        new()
        {
            Status = InventoryCommercialMarginPolicyResolutionStatus.Missing,
            Source = InventoryCommercialMarginPolicySource.None,
            Reasons = ReasonsOr(reasons, InventoryCommercialMarginSettingReason.Missing),
        };

    static InventoryCommercialMarginPolicyResolution Invalid(
        IReadOnlyList<InventoryCommercialMarginSettingReason>? reasons) =>
        new()
        {
            Status = InventoryCommercialMarginPolicyResolutionStatus.Invalid,
            Source = InventoryCommercialMarginPolicySource.None,
            Reasons = ReasonsOr(reasons, InventoryCommercialMarginSettingReason.Invalid),
        };

    static IReadOnlyList<InventoryCommercialMarginSettingReason> ReasonsOr(
        IReadOnlyList<InventoryCommercialMarginSettingReason>? reasons,
        InventoryCommercialMarginSettingReason fallback)
    {
        if (reasons is { Count: > 0 })
            return reasons;
        return [fallback];
    }
}
