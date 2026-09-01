namespace SGDB.Models;

/// <summary>
/// Prioridade de atenção 70E. Sem score 0–100. Normal = sem ação imediata (70E-A None).
/// </summary>
public enum InventoryAttentionPriority
{
    Critical = 0,
    High,
    Medium,
    Low,
    Normal,
}

/// <summary>
/// Família principal da atenção. Expiry ≠ Excess. Idle usa Turnover, não Excess.
/// DataQuality 70E-A = DataIssue. Expiry 70E-A = ValidityUrgency.
/// </summary>
public enum InventoryAttentionFamily
{
    DataQuality = 0,
    Expiry,
    Excess,
    Turnover,
    Normal,
}

/// <summary>
/// Ação sugerida ao operador. Não executa. Não é promoção.
/// Nomes alinhados a <see cref="ValiditySuggestedAction"/> onde há equivalência.
/// EvaluateExcess é novo e não reutiliza a ação comercial reservada do 70B2.
/// </summary>
public enum InventoryOperatorAction
{
    None = 0,
    ReviewData,
    RemoveExpired,
    PrioritizeSale,
    Monitor,
    EvaluateExcess,
}

/// <summary>
/// Confiança da classificação, não probabilidade de venda.
/// </summary>
public enum InventoryAttentionConfidence
{
    Reliable = 0,
    Limited,
    Unavailable,
}

/// <summary>
/// Motivo atômico e testável. Um produto pode ter vários; um é o principal.
/// </summary>
public enum InventoryAttentionReason
{
    None = 0,
    InvalidInput,
    NegativeStock,
    NegativeLocationStock,
    NegativeWarehouseStock,
    InconsistentStockTotals,
    TrackedQuantityExceedsWarehouse,
    DuplicateLotId,
    InvalidLotQuantity,
    InvalidExpiryDate,
    Expired,
    ExpiresToday,
    SurplusAtExpiry,
    NearExpiryWithoutSurplus,
    DatedWithoutSurplusInWindow,
    ProjectedExcess30,
    Idle,
    Undated,
    NoLot,
    InsufficientHistory,
    NoPhysicalEvidence,
    CompositionProduct,
    NoObservableDemand,
    /// <summary>Join 70C sem InventoryProjectedProduct. Não é ausência de lote nem histórico curto.</summary>
    ProjectionMissing,
    /// <summary>Duas projeções 70D para o mesmo ProductId. Não escolhe last-wins.</summary>
    DuplicateProjection,
}

/// <summary>
/// Resultado puro 70E-B1. Sem texto de UI. Sem recópia do snapshot 70D.
/// </summary>
public sealed class InventoryAttentionResult
{
    public int ProductId { get; init; }
    public InventoryAttentionPriority Priority { get; init; } = InventoryAttentionPriority.Normal;
    public InventoryAttentionFamily Family { get; init; } = InventoryAttentionFamily.Normal;
    public InventoryAttentionReason PrimaryReason { get; init; }
    public IReadOnlyList<InventoryAttentionReason> SecondaryReasons { get; init; } = [];
    public InventoryOperatorAction Action { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;

    /// <summary>Sobra 30d já calculada na 70D; null se indisponível ou não finita.</summary>
    public double? ProjectedExcessQuantity { get; init; }

    /// <summary>Soma das sobras até validade já calculadas; null se nenhuma.</summary>
    public double? ProjectedExpirySurplusQuantity { get; init; }

    /// <summary>Menor dias até validade entre lotes Dated que geraram atenção de prazo.</summary>
    public int? NearestDatedDaysUntilExpiry { get; init; }

    /// <summary>Qualidade do valor 70D da sobra até validade. Não é prejuízo.</summary>
    public InventoryProjectionSurplusValueQuality SurplusValueQuality { get; init; } =
        InventoryProjectionSurplusValueQuality.Unavailable;
}

/// <summary>
/// Classificação 70E em lote sobre o snapshot 70D. Sem recópia de giro/projeção.
/// QueryCount é herdado (70D = 7). Lista na ordem de Intelligence.Rows.
/// </summary>
public sealed class InventoryAttentionSnapshot
{
    public DateTime Today { get; init; }
    public int QueryCount { get; init; }
    public IReadOnlyList<InventoryAttentionResult> Results { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryAttentionResult> ByProductId { get; init; } =
        new Dictionary<int, InventoryAttentionResult>();
}
