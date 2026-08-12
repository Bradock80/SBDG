namespace SGDB.Models;

public enum BackupTrigger
{
    Manual,
    Scheduled,
    CashClose,
    AppClose,
}

public sealed class BackupSettings
{
    public bool AutoEnabled { get; set; }
    public List<string> ScheduleTimes { get; set; } = ["12:00", "22:00"];
    public bool BackupOnCashClose { get; set; } = true;
    public bool BackupOnAppClose { get; set; } = true;
    public bool RetentionEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;

    public bool CloudEnabled { get; set; }
    /// <summary>sync_folder | google_api (futuro)</summary>
    public string CloudMode { get; set; } = "sync_folder";
    public string CloudFolderPath { get; set; } = "";

    public string? LastBackupAt { get; set; }
    public string? LastBackupTrigger { get; set; }
    public bool LastBackupSuccess { get; set; }
    public string? LastBackupPath { get; set; }
    public bool LastCloudSuccess { get; set; }
    public string? LastCloudAt { get; set; }
    public string? LastCloudPath { get; set; }
    public string? LastError { get; set; }

    public string LastBackupDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LastBackupAt))
                return "Nenhum backup automático registrado ainda.";
            if (!DateTime.TryParse(LastBackupAt, out var dt))
                return LastBackupAt;

            var when = dt.Date == DateTime.Today ? "Hoje" : dt.ToString("dd/MM/yyyy");
            var time = dt.ToString("HH:mm");
            var trigger = FormatTrigger(LastBackupTrigger);
            var status = LastBackupSuccess ? "Sucesso" : "Falhou";
            var icon = LastBackupSuccess ? "🟢" : "🔴";
            return $"{icon} Último backup automático: {when} às {time} ({status}) · {trigger}";
        }
    }

    public string CloudStatusDisplay
    {
        get
        {
            if (!CloudEnabled || string.IsNullOrWhiteSpace(CloudFolderPath))
                return "☁️ Nuvem: desativada ou pasta não configurada.";
            if (!LastCloudSuccess && string.IsNullOrWhiteSpace(LastCloudAt))
                return "☁️ Nuvem: aguardando primeiro envio.";
            if (!DateTime.TryParse(LastCloudAt, out var dt))
                return LastCloudSuccess ? "☁️ Nuvem: sincronizado." : "☁️ Nuvem: falha na última sincronização.";

            var when = dt.Date == DateTime.Today ? "Hoje" : dt.ToString("dd/MM/yyyy");
            var time = dt.ToString("HH:mm");
            return LastCloudSuccess
                ? $"☁️ Status na Nuvem: Sincronizado às {time} ({when})"
                : $"☁️ Status na Nuvem: Falha às {time} ({when})";
        }
    }

    public string FooterStatusDisplay
    {
        get
        {
            if (!LastBackupSuccess && string.IsNullOrWhiteSpace(LastBackupAt))
                return "⚪ Configure o backup automático para proteger seus dados.";
            var main = LastBackupDisplay;
            if (!CloudEnabled)
                return main;
            return $"{main}\n{CloudStatusDisplay}";
        }
    }

    private static string FormatTrigger(string? trigger) => (trigger ?? "").ToLowerInvariant() switch
    {
        "scheduled" => "Agendado",
        "cashclose" => "Fechamento de caixa",
        "appclose" => "Encerramento do sistema",
        "manual" => "Manual",
        _ => "Automático",
    };
}

public sealed class BackupRunResult
{
    public bool Success { get; init; }
    public string? LocalPath { get; init; }
    public string? CloudPath { get; init; }
    public string? Error { get; init; }
}
