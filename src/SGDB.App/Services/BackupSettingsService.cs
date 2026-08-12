using System.Text.Json;
using SGDB.Models;

namespace SGDB.Services;

public static class BackupSettingsService
{
    private const string Key = "backup_settings_json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static BackupSettings Load()
    {
        var raw = AppSettingsService.GetSetting(Key);
        if (string.IsNullOrWhiteSpace(raw))
            return new BackupSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<BackupSettings>(raw, JsonOpts) ?? new BackupSettings();
            settings.ScheduleTimes = NormalizeTimes(settings.ScheduleTimes);
            settings.RetentionDays = Math.Clamp(settings.RetentionDays, 1, 365);
            return settings;
        }
        catch
        {
            return new BackupSettings();
        }
    }

    public static void Save(BackupSettings settings)
    {
        settings.ScheduleTimes = NormalizeTimes(settings.ScheduleTimes);
        settings.RetentionDays = Math.Clamp(settings.RetentionDays, 1, 365);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        AppSettingsService.SetSetting(Key, json);
    }

    public static void UpdateLastRun(Action<BackupSettings> mutate)
    {
        var settings = Load();
        mutate(settings);
        Save(settings);
    }

    public static List<string> NormalizeTimes(IEnumerable<string>? times)
    {
        var list = new List<string>();
        if (times is null)
            return list;

        foreach (var t in times)
        {
            if (TryNormalizeTime(t, out var norm) && !list.Contains(norm, StringComparer.Ordinal))
                list.Add(norm);
        }

        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public static bool TryNormalizeTime(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input))
            return false;

        input = input.Trim();
        if (TimeSpan.TryParse(input, out var ts))
        {
            normalized = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}";
            return true;
        }

        if (input.Contains(':') && TimeSpan.TryParse(input + ":00", out ts))
        {
            normalized = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}";
            return true;
        }

        return false;
    }
}
