using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 71A-B2: classifica fatos de coocorrência.
/// Sem SQL, UI, 70F/70G, PurchaseService ou ranking.
/// Thresholds operacionais V1, não significância estatística.
/// QueryCount = 0.
/// </summary>
public static class InventoryComboPairEvidenceEngine
{
    public const int ExpectedQueryCount = 0;

    /// <summary>Alvo precisa de pelo menos estas transações na janela para julgar o par.</summary>
    public const int MinimumTargetTransactions = 5;

    /// <summary>Mínimo de vendas conjuntas para Observed. 1–2 = Weak.</summary>
    public const int ObservedPairTransactions = 3;

    public const int WindowDays = InventoryIntelligenceEngine.Window90;

    public static InventoryComboPairCoOccurrenceFacts Classify(
        int targetProductId,
        int anchorProductId,
        int pairTransactions,
        int targetTransactions,
        int historyDays)
    {
        if (targetProductId == anchorProductId
            || pairTransactions < 0
            || targetTransactions < 0
            || pairTransactions > targetTransactions)
        {
            return new InventoryComboPairCoOccurrenceFacts
            {
                TargetProductId = targetProductId,
                AnchorProductId = anchorProductId,
                PairTransactions = pairTransactions,
                TargetTransactions = targetTransactions,
                ConfidenceTargetToAnchor = null,
                Evidence = InventoryComboPairEvidence.InvalidCounts,
            };
        }

        if (historyDays < WindowDays || targetTransactions < MinimumTargetTransactions)
        {
            return new InventoryComboPairCoOccurrenceFacts
            {
                TargetProductId = targetProductId,
                AnchorProductId = anchorProductId,
                PairTransactions = pairTransactions,
                TargetTransactions = targetTransactions,
                ConfidenceTargetToAnchor = null,
                Evidence = InventoryComboPairEvidence.InsufficientHistory,
            };
        }

        var evidence = pairTransactions >= ObservedPairTransactions
            ? InventoryComboPairEvidence.Observed
            : pairTransactions > 0
                ? InventoryComboPairEvidence.Weak
                : InventoryComboPairEvidence.NoneObserved;

        return new InventoryComboPairCoOccurrenceFacts
        {
            TargetProductId = targetProductId,
            AnchorProductId = anchorProductId,
            PairTransactions = pairTransactions,
            TargetTransactions = targetTransactions,
            ConfidenceTargetToAnchor = (double)pairTransactions / targetTransactions,
            Evidence = evidence,
        };
    }
}
