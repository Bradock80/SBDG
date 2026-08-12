using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

public static class BackupService
{
    public static string DefaultBackupFolder
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "SGDB", "Backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Cria cópia .db (e zip opcional) do banco atual. Retorna caminho do arquivo.</summary>
    public static string CreateBackup(string? destinationPath = null, bool asZip = true)
    {
        var path = CreateBackupFile(destinationPath, asZip);
        AuditService.Log("backup", "database", null, $"Arquivo: {path}");
        return path;
    }

    public static BackupRunResult RunAutomaticBackup(BackupTrigger trigger, BackupSettings? settings = null)
    {
        settings ??= BackupSettingsService.Load();
        try
        {
            var localPath = CreateBackupFile(
                Path.Combine(DefaultBackupFolder, BuildAutoFileName(trigger)),
                asZip: true);

            string? cloudPath = null;
            var cloudOk = false;
            if (settings.CloudEnabled
                && settings.CloudMode == "sync_folder"
                && !string.IsNullOrWhiteSpace(settings.CloudFolderPath))
            {
                try
                {
                    cloudPath = CopyToCloudFolder(localPath, settings.CloudFolderPath.Trim());
                    cloudOk = true;
                }
                catch (Exception cloudEx)
                {
                    cloudOk = false;
                    RecordLastRun(settings, trigger, localPath, null, cloudOk: false, cloudEx.Message);
                    return new BackupRunResult
                    {
                        Success = true,
                        LocalPath = localPath,
                        Error = $"Backup local OK, mas falha na nuvem: {cloudEx.Message}",
                    };
                }
            }

            if (settings.RetentionEnabled && settings.RetentionDays > 0)
            {
                CleanupOldBackups(DefaultBackupFolder, settings.RetentionDays);
                if (settings.CloudEnabled && !string.IsNullOrWhiteSpace(settings.CloudFolderPath))
                    CleanupOldBackups(settings.CloudFolderPath.Trim(), settings.RetentionDays);
            }

            RecordLastRun(settings, trigger, localPath, cloudPath, cloudOk, null);
            AuditService.Log("backup", "database", null,
                $"Automático ({trigger}) · {localPath}" +
                (cloudPath is not null ? $" · Nuvem: {cloudPath}" : ""));

            return new BackupRunResult
            {
                Success = true,
                LocalPath = localPath,
                CloudPath = cloudPath,
            };
        }
        catch (Exception ex)
        {
            RecordLastRun(settings, trigger, null, null, cloudOk: false, ex.Message);
            return new BackupRunResult { Success = false, Error = ex.Message };
        }
    }

    private static string CreateBackupFile(string? destinationPath, bool asZip)
    {
        var src = DatabaseService.DatabasePath;
        if (!File.Exists(src))
            throw new InvalidOperationException("Banco de dados não encontrado.");

        try
        {
            using var conn = DatabaseService.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch { /* continua mesmo assim */ }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            destinationPath = Path.Combine(
                DefaultBackupFolder,
                asZip ? $"SGDB_backup_{stamp}.zip" : $"SGDB_backup_{stamp}.db");
        }

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (asZip || destinationPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var tempDb = Path.Combine(Path.GetTempPath(), $"sgdb_bak_{stamp}.db");
            File.Copy(src, tempDb, overwrite: true);
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            using (var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(tempDb, "deposito.db", CompressionLevel.Optimal);
            try { File.Delete(tempDb); } catch { /* ignore */ }
        }
        else
        {
            File.Copy(src, destinationPath, overwrite: true);
        }

        return destinationPath;
    }

    private static string BuildAutoFileName(BackupTrigger trigger)
    {
        var tag = trigger switch
        {
            BackupTrigger.Scheduled => "auto",
            BackupTrigger.CashClose => "caixa",
            BackupTrigger.AppClose => "sistema",
            _ => "auto",
        };
        return $"SGDB_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
    }

    public static string CopyToCloudFolder(string localBackupPath, string cloudFolder)
    {
        if (!File.Exists(localBackupPath))
            throw new InvalidOperationException("Arquivo de backup local não encontrado.");
        if (string.IsNullOrWhiteSpace(cloudFolder))
            throw new InvalidOperationException("Informe a pasta sincronizada com a nuvem.");

        Directory.CreateDirectory(cloudFolder);
        var dest = Path.Combine(cloudFolder, Path.GetFileName(localBackupPath));
        File.Copy(localBackupPath, dest, overwrite: true);
        return dest;
    }

    public static int CleanupOldBackups(string folder, int retentionDays)
    {
        if (retentionDays <= 0 || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return 0;

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        var removed = 0;
        foreach (var pattern in new[] { "SGDB_*.zip", "SGDB_backup_*.zip" })
        {
            foreach (var file in Directory.EnumerateFiles(folder, pattern))
            {
                try
                {
                    if (File.GetLastWriteTime(file) >= cutoff)
                        continue;
                    File.Delete(file);
                    removed++;
                }
                catch { /* ignore */ }
            }
        }

        return removed;
    }

    private static void RecordLastRun(
        BackupSettings settings,
        BackupTrigger trigger,
        string? localPath,
        string? cloudPath,
        bool cloudOk,
        string? error)
    {
        BackupSettingsService.UpdateLastRun(s =>
        {
            s.LastBackupAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            s.LastBackupTrigger = trigger.ToString();
            s.LastBackupSuccess = localPath is not null;
            s.LastBackupPath = localPath;
            s.LastError = error;
            if (cloudPath is not null || s.CloudEnabled)
            {
                s.LastCloudAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                s.LastCloudPath = cloudPath;
                s.LastCloudSuccess = cloudOk && cloudPath is not null;
            }
        });
    }

    /// <summary>
    /// Restaura a partir de .db ou .zip contendo deposito.db.
    /// Fecha conexões ativas via reinício — o chamador deve reiniciar o app após sucesso.
    /// </summary>
    public static void RestoreBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new InvalidOperationException("Arquivo de backup não encontrado.");

        var dest = DatabaseService.DatabasePath;
        var destDir = Path.GetDirectoryName(dest)
            ?? throw new InvalidOperationException("Pasta do banco inválida.");
        Directory.CreateDirectory(destDir);

        var safety = Path.Combine(destDir, $"deposito_antes_restore_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        if (File.Exists(dest))
            File.Copy(dest, safety, overwrite: true);

        var tempRestore = Path.Combine(Path.GetTempPath(), $"sgdb_restore_{Guid.NewGuid():N}.db");
        try
        {
            if (backupPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(backupPath);
                var entry = zip.Entries.FirstOrDefault(e =>
                    e.Name.Equals("deposito.db", StringComparison.OrdinalIgnoreCase)
                    || e.FullName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("ZIP não contém um arquivo .db.");
                entry.ExtractToFile(tempRestore, overwrite: true);
            }
            else
            {
                File.Copy(backupPath, tempRestore, overwrite: true);
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = tempRestore,
                Mode = SqliteOpenMode.ReadOnly,
            };
            using (var test = new SqliteConnection(builder.ConnectionString))
            {
                test.Open();
                using (var cmd = test.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
                    _ = cmd.ExecuteScalar();
                }

                // Só abrir não basta: banco "malformed" pode passar no COUNT e quebrar depois.
                using (var cmd = test.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA integrity_check;";
                    var result = Convert.ToString(cmd.ExecuteScalar()) ?? "";
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Este backup está corrompido (integrity_check falhou).\n\n" +
                            "Não restaure este arquivo.\n" +
                            "Use outro backup .zip/.db ou a pasta Banco do pen drive (deposito.db).");
                    }
                }
            }

            SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            foreach (var side in new[] { dest + "-wal", dest + "-shm" })
            {
                try { if (File.Exists(side)) File.Delete(side); } catch { /* ignore */ }
            }

            File.Copy(tempRestore, dest, overwrite: true);
            // Garante que não fiquem restos WAL de sessão anterior apontando para o DB antigo.
            foreach (var side in new[] { dest + "-wal", dest + "-shm" })
            {
                try { if (File.Exists(side)) File.Delete(side); } catch { /* ignore */ }
            }

            AuditService.Log("restore", "database", null,
                $"Restaurado de: {backupPath}. Cópia de segurança: {safety}");
        }
        finally
        {
            try { if (File.Exists(tempRestore)) File.Delete(tempRestore); } catch { /* ignore */ }
        }
    }
}
