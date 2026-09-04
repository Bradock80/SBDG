namespace SGDB.Models;

/// <summary>
/// Entrada já carregada 71A-B5. Sem I/O próprio. 70C popula; 70E/70F/70G entram por ProductId.
/// Today, se omitido, reusa Intelligence.Today do pipeline.
/// </summary>
public sealed class InventoryComboIntelligenceComposeInput
{
    public DateTime? Today { get; init; }
    public InventoryIntelligenceSnapshot? Intelligence { get; init; }
    public InventoryAttentionSnapshot? Attention { get; init; }
    public InventoryCommercialFactsSnapshot? Facts { get; init; }
    public InventoryPurchaseGuidanceSnapshot? Guidance { get; init; }
    public InventoryCommercialMarginPolicyResolution? PolicyResolution { get; init; }
}

/// <summary>
/// Identidade 70C para Presentation. Sem formatação PT-BR.
/// </summary>
public sealed class InventoryComboProductTitle
{
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
}

/// <summary>
/// Alvo elegível B1 com até Top 3 sugestões B4. Lista vazia é resultado válido.
/// Code/Name vêm de 70C para Presentation futura; B5 não formata texto.
/// </summary>
public sealed class InventoryComboTargetSuggestionGroup
{
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public InventoryComboTargetEligibility Eligibility { get; init; } = new();
    public IReadOnlyList<InventoryComboSuggestion> Suggestions { get; init; } = [];
}

/// <summary>
/// Snapshot 71A-B5. QueryCount = 9 sem B2, 10 com B2. Sem Presentation, UI ou persistência.
/// Targets só contém alvos Eligible; 0 sugestões ≠ alvo não elegível.
/// </summary>
public sealed class InventoryComboIntelligenceSnapshot
{
    public int QueryCount { get; init; }
    public int CoOccurrenceQueryCount { get; init; }
    public int CoOccurrenceCalls { get; init; }
    public int TargetsEvaluated { get; init; }
    public int EligibleTargets { get; init; }
    public int EligibleAnchors { get; init; }
    public int PairCandidatesEvaluated { get; init; }
    public int PairFinancialEvaluations { get; init; }
    public IReadOnlyList<int> RequestedTargetIds { get; init; } = [];
    public IReadOnlyList<int> RequestedAnchorIds { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryComboProductTitle> ProductTitles { get; init; } =
        new Dictionary<int, InventoryComboProductTitle>();
    public IReadOnlyList<InventoryComboTargetSuggestionGroup> Targets { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryComboTargetSuggestionGroup> ByProductId { get; init; } =
        new Dictionary<int, InventoryComboTargetSuggestionGroup>();
}
