using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Preferência Visão simples | Visão detalhada.
/// Reusa app_settings existente (sem schema). Cache em memória para ApplyView = 0 query.
/// </summary>
public static class InventorySmartViewPreference
{
    public const string SettingKey = "inventory_smart_view_mode";
    public const string SimpleValue = "simple";
    public const string DetailedValue = "detailed";

    static InventorySmartViewMode _current = InventorySmartViewMode.Simple;
    static bool _loaded;

    public static InventorySmartViewMode Current => _current;

    public static InventorySmartViewMode LoadIfNeeded()
    {
        if (_loaded)
            return _current;
        try
        {
            var raw = AppSettingsService.GetSetting(SettingKey);
            _current = string.Equals(raw, DetailedValue, StringComparison.OrdinalIgnoreCase)
                ? InventorySmartViewMode.Detailed
                : InventorySmartViewMode.Simple;
        }
        catch
        {
            _current = InventorySmartViewMode.Simple;
        }

        _loaded = true;
        return _current;
    }

    public static void Set(InventorySmartViewMode mode)
    {
        _current = mode;
        _loaded = true;
        try
        {
            AppSettingsService.SetSetting(
                SettingKey,
                mode == InventorySmartViewMode.Detailed ? DetailedValue : SimpleValue);
        }
        catch
        {
            // Preferência em memória permanece mesmo se a persistência falhar.
        }
    }

    public static void ResetForTests(InventorySmartViewMode mode = InventorySmartViewMode.Simple)
    {
        _current = mode;
        _loaded = true;
    }
}
