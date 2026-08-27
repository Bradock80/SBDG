using System.Globalization;
using System.IO;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69T-F — orquestra a UI administrativa. A lógica de saneamento
/// permanece em <see cref="LegacyMergedProductStockCleanupService"/>.
/// </summary>
public static class LegacyMergeCleanupAdminService
{
    public const string BatchAuditAction = "sanear_merge_legado_lote";
    public const string BatchAuditEntity = "manutencao";

    public static string? BackupPath { get; private set; }
    public static DateTime? BackupDate { get; private set; }
    public static long? BackupSize { get; private set; }
    public static bool HasValidSessionBackup =>
        BackupPath is not null
        && BackupService.ValidateArchive(BackupPath).IsValid;

    public static void ResetSession()
    {
        BackupPath = null;
        BackupDate = null;
        BackupSize = null;
    }

    public static bool CanAccess() =>
        AccessControl.CanAccessModule(LegacyMergeCleanupRules.ModuleId);

    public static void EnsureAccess()
    {
        if (!CanAccess())
            throw new InvalidOperationException(LegacyMergeCleanupRules.AccessDeniedMessage);
    }

    public static bool CanExecuteOnThisMachine() => !StoreNetworkMode.IsClient;

    public static void EnsureExecuteAllowedHere()
    {
        StoreNetworkMode.EnsureLocalMutationAllowed("sanear merge legado");
        if (!CanExecuteOnThisMachine())
            throw new InvalidOperationException(LegacyMergeCleanupRules.ClientBlockedMessage);
    }

    public static IReadOnlyList<LegacyMergeAbsorbCandidate> ListCandidates()
    {
        EnsureAccess();
        return LegacyMergedProductStockCleanupService.ListCandidates();
    }

    public static IReadOnlyList<LegacyMergeAbsorbCandidate> ListAutomatic() =>
        ListCandidates()
            .Where(c => c.UiStatus != LegacyMergeCleanupUiStatus.Revisar)
            .ToList();

    public static IReadOnlyList<LegacyMergeAbsorbCandidate> ListManualReview() =>
        ListCandidates()
            .Where(c => c.UiStatus == LegacyMergeCleanupUiStatus.Revisar)
            .ToList();

    public static IReadOnlyList<LegacyMergeAbsorbCandidate> ListExecutable() =>
        ListCandidates()
            .Where(c => c.UiStatus == LegacyMergeCleanupUiStatus.Comprovado)
            .ToList();

    public static LegacyMergeCleanupDetail GetDetail(int absorbId)
    {
        EnsureAccess();
        var candidate = ListCandidates().FirstOrDefault(c => c.AbsorbId == absorbId)
            ?? throw new InvalidOperationException(LegacyMergedProductStockCleanupService.NotFoundMessage);
        var absorb = ProductService.GetById(candidate.AbsorbId);
        var keep = ProductService.GetById(candidate.KeepId);
        var keepStock = keep?.Stock ?? candidate.KeepStock;
        return new LegacyMergeCleanupDetail
        {
            Candidate = candidate,
            Absorb = absorb,
            Keep = keep,
            KeepStockBefore = keepStock,
            KeepStockAfter = keepStock,
            AbsorbCost = absorb?.CostPrice ?? 0,
            KeepCost = keep?.CostPrice ?? 0,
            KeepPrecoCompra = keep is null ? 0 : ProductExtra.Parse(keep.ExtraJson).PrecoCompra,
            KeepSalePrice = keep?.SalePrice ?? 0,
        };
    }

    public static LegacyMergeCleanupBackupInfo CreateRequiredBackup()
    {
        EnsureAccess();
        EnsureExecuteAllowedHere();
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(
                BackupService.DefaultBackupFolder,
                $"SGDB_backup_residuos_{stamp}.zip");
            var path = BackupService.CreateConsistentBackup(dest);
            var validation = BackupService.ValidateArchive(path);
            if (!validation.IsValid)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
                throw new InvalidOperationException(
                    "O backup foi criado, mas não passou na validação (ZIP / deposito.db).");
            }

            BackupPath = path;
            BackupDate = DateTime.Now;
            BackupSize = validation.Size;
            return new LegacyMergeCleanupBackupInfo
            {
                BackupPath = path,
                BackupDate = BackupDate.Value,
                BackupSize = validation.Size,
                IsValid = true,
            };
        }
        catch (Exception ex)
        {
            ResetSession();
            if (ex is InvalidOperationException ioe
                && (ioe.Message == LegacyMergeCleanupRules.AccessDeniedMessage
                    || ioe.Message == LegacyMergeCleanupRules.ClientBlockedMessage))
                throw;

            throw new InvalidOperationException(
                LegacyMergeCleanupRules.BackupConsistentFailedMessage + "\n\n" + ex.Message,
                ex);
        }
    }

    public static string BuildConfirmMessage()
    {
        EnsureAccess();
        var count = ListExecutable().Count;
        var backupName = BackupPath is null ? "(nenhum)" : Path.GetFileName(BackupPath);
        var backupWhen = BackupDate?.ToString("dd/MM/yyyy HH:mm:ss") ?? "—";
        var backupPath = BackupPath ?? "—";
        return
            $"Serão saneados {count} produtos antigos já comprovadamente unificados.\n\n" +
            "O estoque, custo, preço de compra e preço de venda dos produtos\n" +
            "principais NÃO serão alterados.\n\n" +
            "O saldo residual dos cadastros antigos será zerado.\n\n" +
            $"Backup:\n{backupName}\n{backupWhen}\n{backupPath}\n\n" +
            "Deseja continuar?";
    }

    public static bool HasPhysicalInventoryWarning() =>
        ListCandidates().Any(c =>
            LegacyMergeCleanupRules.MatchesPhysicalInventoryPriority(c.KeepName));

    /// <summary>
    /// <paramref name="confirmed"/> false = cancelar (não altera nada).
    /// </summary>
    public static LegacyMergeCleanupBatchResult ExecuteProven(bool confirmed)
    {
        EnsureAccess();
        EnsureExecuteAllowedHere();

        var all = LegacyMergedProductStockCleanupService.ListCandidates();
        var blocked = all.Count(c => c.UiStatus == LegacyMergeCleanupUiStatus.Revisar);
        var proven = all.Where(c => c.Kind == LegacyMergeEvidenceKind.Comprovado).ToList();

        if (!confirmed)
        {
            return new LegacyMergeCleanupBatchResult
            {
                Candidates = proven.Count,
                Blocked = blocked,
                Executed = false,
            };
        }

        if (!HasValidSessionBackup)
            throw new InvalidOperationException(LegacyMergeCleanupRules.BackupRequiredMessage);

        var sanitized = 0;
        var already = 0;
        var failures = new List<LegacyMergeCleanupFailure>();

        foreach (var candidate in proven)
        {
            try
            {
                var result = LegacyMergedProductStockCleanupService.Sanitize(candidate.AbsorbId);
                if (result.AlreadyClean)
                    already++;
                else
                    sanitized++;
            }
            catch (Exception ex)
            {
                failures.Add(new LegacyMergeCleanupFailure
                {
                    AbsorbId = candidate.AbsorbId,
                    AbsorbName = candidate.AbsorbName,
                    Message = ex.Message,
                });
            }
        }

        var batch = new LegacyMergeCleanupBatchResult
        {
            Candidates = proven.Count,
            Sanitized = sanitized,
            AlreadyClean = already,
            Blocked = blocked,
            Failures = failures.Count,
            Executed = true,
            FailureItems = failures,
        };

        AuditService.LogJson(
            BatchAuditAction,
            BatchAuditEntity,
            proven.Count.ToString(CultureInfo.InvariantCulture),
            new
            {
                user = AppSession.UserLogin,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                backup_path = BackupPath,
                backup_date = BackupDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                backup_size = BackupSize,
                candidates = batch.Candidates,
                sanitized = batch.Sanitized,
                already_clean = batch.AlreadyClean,
                blocked = batch.Blocked,
                failures = batch.Failures,
            },
            $"Lote saneamento merge legado · {batch.Sanitized} saneados · {batch.AlreadyClean} já saneados · {batch.Blocked} bloqueados · {batch.Failures} falhas");

        return batch;
    }

    public static bool ResultIsWarning(LegacyMergeCleanupBatchResult result) =>
        result.Failures > 0;

    public static string FormatResult(LegacyMergeCleanupBatchResult result)
    {
        var text =
            $"Candidatos:\n{result.Candidates}\n\n" +
            $"Saneados:\n{result.Sanitized}\n\n" +
            $"Já saneados:\n{result.AlreadyClean}\n\n" +
            $"Bloqueados:\n{result.Blocked}\n\n" +
            $"Falhas:\n{result.Failures}\n\n" +
            result.KeepUnchangedMessage;
        if (result.FailureItems.Count > 0)
        {
            text += "\n\nFalhas:\n" + string.Join("\n",
                result.FailureItems.Select(f => $"#{f.AbsorbId} {f.AbsorbName}: {f.Message}"));
        }
        return text;
    }
}
