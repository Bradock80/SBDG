namespace SGDB.Models;

public enum LegacyMergeEvidenceKind
{
    Comprovado,
    Insuficiente,
    Conflitante,
}

public sealed class LegacyMergeAbsorbCandidate
{
    public int AbsorbId { get; init; }
    public string AbsorbName { get; init; } = "";
    public int KeepId { get; init; }
    public string KeepName { get; init; } = "";
    public string MergedAt { get; init; } = "";
    public string UserLogin { get; init; } = "";
    public string UserName { get; init; } = "";
    public double AbsorbStock { get; init; }
    public double AbsorbFridge { get; init; }
    public bool AbsorbActive { get; init; }
    public double KeepStock { get; init; }
    public double KeepFridge { get; init; }
    public double? AuditKeepStockBefore { get; init; }
    public double? AuditAbsorbStockBefore { get; init; }
    public double? AuditStockAfter { get; init; }
    public double? AuditAbsorbFridgeBefore { get; init; }
    public int MergeAuditId { get; init; }
    public bool HasUnificacaoMovement { get; init; }
    public LegacyMergeEvidenceKind Kind { get; init; }
    public string Reason { get; init; } = "";
    public bool HasResidual =>
        Math.Abs(AbsorbStock) > 1e-4 || Math.Abs(AbsorbFridge) > 1e-4;

    public string UiStatus => Kind == LegacyMergeEvidenceKind.Comprovado
        ? (HasResidual ? LegacyMergeCleanupUiStatus.Comprovado : LegacyMergeCleanupUiStatus.JaSaneado)
        : LegacyMergeCleanupUiStatus.Revisar;
}

public sealed class LegacyMergeSanitizeResult
{
    public bool AlreadyClean { get; init; }
    public int AbsorbId { get; init; }
    public int KeepId { get; init; }
    public double AbsorbStockBefore { get; init; }
    public double AbsorbFridgeBefore { get; init; }
}

public static class LegacyMergeCleanupUiStatus
{
    public const string Comprovado = "COMPROVADO";
    public const string JaSaneado = "JÁ SANEADO";
    public const string Revisar = "REVISAR";
}

public sealed class LegacyMergeCleanupBackupInfo
{
    public string BackupPath { get; init; } = "";
    public DateTime BackupDate { get; init; }
    public long BackupSize { get; init; }
    public bool IsValid { get; init; }
}

public sealed class LegacyMergeCleanupDetail
{
    public LegacyMergeAbsorbCandidate Candidate { get; init; } = null!;
    public Product? Absorb { get; init; }
    public Product? Keep { get; init; }
    public double KeepStockBefore { get; init; }
    public double KeepStockAfter { get; init; }
    public double AbsorbCost { get; init; }
    public double KeepCost { get; init; }
    public double KeepPrecoCompra { get; init; }
    public double KeepSalePrice { get; init; }
}

public sealed class LegacyMergeCleanupFailure
{
    public int AbsorbId { get; init; }
    public string AbsorbName { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class LegacyMergeCleanupBatchResult
{
    public int Candidates { get; init; }
    public int Sanitized { get; init; }
    public int AlreadyClean { get; init; }
    public int Blocked { get; init; }
    public int Failures { get; init; }
    public bool Executed { get; init; }
    public IReadOnlyList<LegacyMergeCleanupFailure> FailureItems { get; init; } = [];
    public string KeepUnchangedMessage { get; init; } =
        "Estoque dos produtos principais não foi alterado.";
}
