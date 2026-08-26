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
}

public sealed class LegacyMergeSanitizeResult
{
    public bool AlreadyClean { get; init; }
    public int AbsorbId { get; init; }
    public int KeepId { get; init; }
    public double AbsorbStockBefore { get; init; }
    public double AbsorbFridgeBefore { get; init; }
}
