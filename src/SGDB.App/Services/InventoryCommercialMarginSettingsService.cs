using System.Globalization;
using System.Text.RegularExpressions;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Persistência 70F-B3B da margem bruta mínima global sobre venda.
/// Uma chave em app_settings. Sem default, grupo, produto, UI ou B3.
/// Opera no SQLite do processo; não afirma autoridade Rede Loja (dívida B3D).
/// </summary>
public static class InventoryCommercialMarginSettingsService
{
    public const string SettingKey = "inventory_min_gross_margin_percent";
    public const int ExpectedLoadQueryCount = 1;
    public const int MaxDecimalPlaces = 4;

    static readonly Regex InvariantPercentPattern = new(
        @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static InventoryCommercialMarginSetting Load()
    {
        var raw = AppSettingsService.GetSetting(SettingKey);
        return ClassifyRaw(raw, rowExists: raw is not null, queryCount: ExpectedLoadQueryCount);
    }

    public static InventoryCommercialMarginSaveResult Save(decimal value) =>
        TryWrite(value);

    public static InventoryCommercialMarginSaveResult Save(double value)
    {
        if (!InventoryIntelligenceEngine.IsFinite(value))
            return Reject(InventoryCommercialMarginSettingReason.Invalid, raw: value.ToString(CultureInfo.InvariantCulture));
        try
        {
            return TryWrite(Convert.ToDecimal(value));
        }
        catch (OverflowException)
        {
            return Reject(InventoryCommercialMarginSettingReason.OutOfRange, raw: value.ToString(CultureInfo.InvariantCulture));
        }
    }

    public static InventoryCommercialMarginSetting Clear()
    {
        AppSettingsService.DeleteSetting(SettingKey);
        return Missing(queryCount: 1);
    }

    static InventoryCommercialMarginSaveResult TryWrite(decimal value)
    {
        if (!IsValidPercent(value))
            return Reject(InventoryCommercialMarginSettingReason.OutOfRange, value);

        var rounded = decimal.Round(value, MaxDecimalPlaces, MidpointRounding.AwayFromZero);
        if (!IsValidPercent(rounded))
            return Reject(InventoryCommercialMarginSettingReason.OutOfRange, value);

        var serialized = Serialize(rounded);
        AppSettingsService.SetSetting(SettingKey, serialized);
        return new InventoryCommercialMarginSaveResult
        {
            Written = true,
            Setting = Configured(rounded, serialized, queryCount: 1),
        };
    }

    public static bool IsValidPercent(decimal value) =>
        value >= 0m && value < 100m;

    static InventoryCommercialMarginSetting ClassifyRaw(string? raw, bool rowExists, int queryCount)
    {
        if (!rowExists)
            return Missing(queryCount);

        if (string.IsNullOrEmpty(raw))
            return Invalid(raw ?? "", InventoryCommercialMarginSettingReason.EmptyValue, queryCount);

        if (string.IsNullOrWhiteSpace(raw) || !InvariantPercentPattern.IsMatch(raw))
            return Invalid(raw, InventoryCommercialMarginSettingReason.NonInvariantFormat, queryCount);

        if (!decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            return Invalid(raw, InventoryCommercialMarginSettingReason.NonInvariantFormat, queryCount);

        if (!IsValidPercent(parsed))
            return Invalid(raw, InventoryCommercialMarginSettingReason.OutOfRange, queryCount, parsed);

        return Configured(parsed, raw, queryCount);
    }

    static string Serialize(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    static InventoryCommercialMarginSetting Missing(int queryCount) =>
        new()
        {
            Status = InventoryCommercialMarginSettingStatus.Missing,
            Reasons = [InventoryCommercialMarginSettingReason.Missing],
            QueryCount = queryCount,
        };

    static InventoryCommercialMarginSetting Configured(decimal value, string raw, int queryCount) =>
        new()
        {
            Status = InventoryCommercialMarginSettingStatus.Configured,
            MinimumGrossMarginPercent = value,
            RawValue = raw,
            QueryCount = queryCount,
        };

    static InventoryCommercialMarginSetting Invalid(
        string raw,
        InventoryCommercialMarginSettingReason reason,
        int queryCount,
        decimal? parsed = null) =>
        new()
        {
            Status = InventoryCommercialMarginSettingStatus.Invalid,
            MinimumGrossMarginPercent = parsed is decimal value && IsValidPercent(value) ? value : null,
            RawValue = raw,
            Reasons = [reason, InventoryCommercialMarginSettingReason.Invalid],
            QueryCount = queryCount,
        };

    static InventoryCommercialMarginSaveResult Reject(
        InventoryCommercialMarginSettingReason reason,
        decimal value)
    {
        var raw = Serialize(value);
        return Reject(reason, raw, IsValidPercent(value) ? value : null);
    }

    static InventoryCommercialMarginSaveResult Reject(
        InventoryCommercialMarginSettingReason reason,
        string? raw,
        decimal? parsed = null) =>
        new()
        {
            Written = false,
            Setting = Invalid(raw ?? "", reason, queryCount: 0, parsed),
        };
}
