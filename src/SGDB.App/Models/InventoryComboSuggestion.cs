namespace SGDB.Models;

/// <summary>
/// Limitação V1: o par pode ser recomendado, mas a confiança cai.
/// Blockers de B1 não entram aqui.
/// </summary>
public enum InventoryComboSuggestionLimitation
{
    WeakPairEvidence = 0,
    InsufficientPairHistory,
    TargetLimitedConfidence,
    AnchorLimitedConfidence,
    OtherDataLimitation,
}

/// <summary>
/// Candidato a par já avaliado por B1/B2/B3. B4 não recalcula autoridades.
/// TargetFacts/AnchorFacts = 70C para estoque, cobertura e VMV.
/// </summary>
public sealed class InventoryComboCandidate
{
    public InventoryComboTargetEligibility? TargetEligibility { get; init; }
    public InventoryComboAnchorEligibility? AnchorEligibility { get; init; }
    public InventoryComboPairCoOccurrenceFacts? PairEvidenceFacts { get; init; }
    public InventoryComboPairFinancialFacts? FinancialFacts { get; init; }
    public ProductTurnoverRow? TargetFacts { get; init; }
    public ProductTurnoverRow? AnchorFacts { get; init; }
}

/// <summary>
/// Sugestão estruturada V1. Sem nome, código, texto PT-BR ou formatação de UI.
/// </summary>
public sealed class InventoryComboSuggestion
{
    public int TargetProductId { get; init; }
    public int AnchorProductId { get; init; }
    public ComboTargetEligibilityReason TargetReason { get; init; }
    public ComboAnchorEligibilityReason AnchorReason { get; init; }
    public InventoryComboPairEvidence PairEvidence { get; init; }
    public double NormalPairPrice { get; init; }
    public double PairCost { get; init; }
    public double PairFloorPrice { get; init; }
    public IReadOnlyList<InventoryComboPairFinancialScenario> Scenarios { get; init; } = [];
    public double TargetStock { get; init; }
    public double AnchorStock { get; init; }
    public double? AnchorCoverageDays { get; init; }
    public int PairTransactions { get; init; }
    public int TargetTransactions { get; init; }
    public double? ConfidenceTargetToAnchor { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
    public IReadOnlyList<InventoryComboSuggestionLimitation> Limitations { get; init; } = [];
}

/// <summary>
/// Decisão B4 para um alvo. QueryCount = 0. Lista vazia é resultado válido.
/// </summary>
public sealed class InventoryComboSuggestionSnapshot
{
    public int QueryCount { get; init; }
    public int TargetProductId { get; init; }
    public IReadOnlyList<InventoryComboSuggestion> Rows { get; init; } = [];
}
