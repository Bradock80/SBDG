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
            var dir = ResolveBackupFolder();
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Pasta de backup do banco efetivo. Banco isolado (teste) não mistura
    /// com Documents\SGDB\Backups da loja.
    /// </summary>
    public static string ResolveBackupFolder()
    {
        var db = DatabaseService.DatabasePath;
        if (DatabaseService.IsIsolatedDatabasePath(db))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(db));
            if (!string.IsNullOrWhiteSpace(dir))
                return Path.Combine(dir, "Backups");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SGDB", "Backups");
    }

    public static BackupArchiveValidation ValidateArchive(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new BackupArchiveValidation();

        var size = new FileInfo(path).Length;
        if (size <= 0)
            return new BackupArchiveValidation { FileExists = true, Size = 0 };

        try
        {
            using var zip = ZipFile.OpenRead(path);
            var hasDb = zip.Entries.Any(e =>
                e.Name.Equals("deposito.db", StringComparison.OrdinalIgnoreCase));
            return new BackupArchiveValidation
            {
                FileExists = true,
                Size = size,
                ZipOpens = true,
                HasDepositoDb = hasDb,
            };
        }
        catch
        {
            return new BackupArchiveValidation
            {
                FileExists = true,
                Size = size,
                ZipOpens = false,
            };
        }
    }

    /// <summary>Somente testes: falha forçada do snapshot consistente (VACUUM INTO).</summary>
    public static Action? TestBeforeConsistentSnapshot { get; set; }

    /// <summary>Cria cópia .db (e zip opcional) do banco atual. Retorna caminho do arquivo.</summary>
    public static string CreateBackup(string? destinationPath = null, bool asZip = true)
    {
        var path = CreateBackupFile(destinationPath, asZip);
        AuditService.Log("backup", "database", null, $"Arquivo: {path}");
        return path;
    }

    /// <summary>
    /// Snapshot SQLite consistente via VACUUM INTO. Sem File.Copy do deposito.db.
    /// Destinado ao backup obrigatório do 69T-F: se o snapshot falhar, a operação falha.
    /// </summary>
    public static string CreateConsistentBackup(string destinationPath)
    {
        var src = DatabaseService.DatabasePath;
        if (!File.Exists(src))
            throw new InvalidOperationException("Banco de dados não encontrado.");
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new InvalidOperationException("Destino do backup obrigatório não informado.");

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        SqliteConnection.ClearAllPools();

        var snapshot = Path.Combine(Path.GetTempPath(), $"sgdb_bak_consistent_{Guid.NewGuid():N}.db");
        try
        {
            SnapshotDatabase(src, snapshot, allowDegradedCopy: false);
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            using var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(snapshot, "deposito.db", CompressionLevel.Optimal);
        }
        catch
        {
            try { if (File.Exists(destinationPath)) File.Delete(destinationPath); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            try { if (File.Exists(snapshot)) File.Delete(snapshot); } catch { /* ignore */ }
        }

        AuditService.Log("backup", "database", null, $"Consistente (VACUUM INTO): {destinationPath}");
        return destinationPath;
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

        SqliteConnection.ClearAllPools();
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

        var snapshot = Path.Combine(Path.GetTempPath(), $"sgdb_bak_{stamp}_{Guid.NewGuid():N}.db");
        try
        {
            SnapshotDatabase(src, snapshot, allowDegradedCopy: true);
            if (asZip || destinationPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                using var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(snapshot, "deposito.db", CompressionLevel.Optimal);
            }
            else
            {
                File.Copy(snapshot, destinationPath, overwrite: true);
            }
        }
        finally
        {
            try { if (File.Exists(snapshot)) File.Delete(snapshot); } catch { /* ignore */ }
        }

        return destinationPath;
    }

    private static void SnapshotDatabase(string src, string dest, bool allowDegradedCopy)
    {
        if (File.Exists(dest))
            File.Delete(dest);

        Exception? vacuumError = null;
        try
        {
            if (!allowDegradedCopy)
                TestBeforeConsistentSnapshot?.Invoke();

            using var conn = DatabaseService.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO $dest;";
            cmd.Parameters.AddWithValue("$dest", dest);
            cmd.ExecuteNonQuery();
            if (File.Exists(dest) && new FileInfo(dest).Length > 0)
                return;

            vacuumError = new InvalidOperationException("VACUUM INTO não gerou arquivo de snapshot.");
        }
        catch (Exception ex)
        {
            vacuumError = ex;
        }

        if (allowDegradedCopy)
        {
            File.Copy(src, dest, overwrite: true);
            return;
        }

        try { if (File.Exists(dest)) File.Delete(dest); } catch { /* ignore */ }
        throw new InvalidOperationException(
            "Falha ao gerar snapshot SQLite consistente (VACUUM INTO)." +
            (vacuumError is null ? "" : " " + vacuumError.Message),
            vacuumError);
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
