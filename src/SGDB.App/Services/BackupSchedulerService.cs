using System.Windows.Threading;
using SGDB.Models;

namespace SGDB.Services;

public static class BackupSchedulerService
{
    private static DispatcherTimer? _timer;
    private static readonly HashSet<string> _executedSlots = new(StringComparer.Ordinal);
    private static DateTime _slotDay = DateTime.Today;

    public static void Start()
    {
        Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public static void Restart() => Start();

    public static void TryBackupOnCashClose()
    {
        var settings = BackupSettingsService.Load();
        if (!settings.AutoEnabled || !settings.BackupOnCashClose)
            return;
        if (ShouldSkipDueToRecentBackup(settings))
            return;
        BackupService.RunAutomaticBackup(BackupTrigger.CashClose, settings);
    }

    public static void TryBackupOnAppClose()
    {
        var settings = BackupSettingsService.Load();
        if (!settings.AutoEnabled || !settings.BackupOnAppClose)
            return;
        if (ShouldSkipDueToRecentBackup(settings))
            return;
        BackupService.RunAutomaticBackup(BackupTrigger.AppClose, settings);
    }

    private static void OnTick()
    {
        var settings = BackupSettingsService.Load();
        if (!settings.AutoEnabled || settings.ScheduleTimes.Count == 0)
            return;

        ResetSlotsIfNewDay();

        var now = DateTime.Now;
        var hm = now.ToString("HH:mm");
        foreach (var slot in settings.ScheduleTimes)
        {
            if (!string.Equals(slot, hm, StringComparison.Ordinal))
                continue;

            var key = $"{now:yyyy-MM-dd} {hm}";
            if (!_executedSlots.Add(key))
                continue;

            if (ShouldSkipDueToRecentBackup(settings))
                continue;

            BackupService.RunAutomaticBackup(BackupTrigger.Scheduled, settings);
        }
    }

    private static void ResetSlotsIfNewDay()
    {
        if (DateTime.Today == _slotDay)
            return;
        _executedSlots.Clear();
        _slotDay = DateTime.Today;
    }

    internal static bool ShouldSkipDueToRecentBackup(BackupSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.LastBackupAt))
            return false;
        if (!DateTime.TryParse(settings.LastBackupAt, out var last))
            return false;
        return (DateTime.Now - last).TotalMinutes < 5;
    }
}
