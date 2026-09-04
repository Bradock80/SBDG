using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71A-B2 — classificador puro. Sem SQL, UI, ranking, preço ou 70F/70G.
/// </summary>
public class InventoryComboPairEvidenceEngineTests
{
    const int Target = 11;
    const int Anchor = 22;

    [Fact]
    public void QueryCount_e_zero()
    {
        Assert.Equal(0, InventoryComboPairEvidenceEngine.ExpectedQueryCount);
        Assert.Equal(1, InventoryComboCoOccurrenceService.ExpectedQueryCount);
        Assert.Equal(InventoryIntelligenceEngine.Window90, InventoryComboPairEvidenceEngine.WindowDays);
        Assert.Equal(5, InventoryComboPairEvidenceEngine.MinimumTargetTransactions);
        Assert.Equal(3, InventoryComboPairEvidenceEngine.ObservedPairTransactions);
    }

    [Fact]
    public void HistoryDays_89_e_InsufficientHistory()
    {
        var row = Classify(pair: 10, target: 10, history: 89);
        AssertInsufficient(row, pair: 10, target: 10);
    }

    [Fact]
    public void HistoryDays_90_com_TargetTx_4_e_InsufficientHistory()
    {
        var row = Classify(pair: 0, target: 4, history: 90);
        AssertInsufficient(row, pair: 0, target: 4);
    }

    [Fact]
    public void HistoryDays_90_TargetTx_5_Pair_0_e_NoneObserved()
    {
        var row = Classify(pair: 0, target: 5, history: 90);
        Assert.Equal(InventoryComboPairEvidence.NoneObserved, row.Evidence);
        Assert.Equal(0, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Pair_1_e_Weak()
    {
        var row = Classify(pair: 1, target: 5, history: 90);
        Assert.Equal(InventoryComboPairEvidence.Weak, row.Evidence);
        Assert.Equal(0.2, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Pair_2_e_Weak()
    {
        var row = Classify(pair: 2, target: 5, history: 90);
        Assert.Equal(InventoryComboPairEvidence.Weak, row.Evidence);
        Assert.Equal(0.4, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Pair_3_e_Observed()
    {
        var row = Classify(pair: 3, target: 5, history: 90);
        Assert.Equal(InventoryComboPairEvidence.Observed, row.Evidence);
        Assert.Equal(0.6, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Pair_maior_que_3_e_Observed()
    {
        var row = Classify(pair: 4, target: 10, history: 90);
        Assert.Equal(InventoryComboPairEvidence.Observed, row.Evidence);
        Assert.Equal(0.4, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Confidence_3_sobre_10_e_zero_ponto_tres()
    {
        var row = Classify(pair: 3, target: 10, history: 90);
        Assert.Equal(0.3, row.ConfidenceTargetToAnchor);
        Assert.InRange(row.ConfidenceTargetToAnchor!.Value, 0, 1);
    }

    [Fact]
    public void Confidence_5_sobre_5_e_um()
    {
        var row = Classify(pair: 5, target: 5, history: 90);
        Assert.Equal(1, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Confidence_0_sobre_5_e_zero_em_NoneObserved()
    {
        var row = Classify(pair: 0, target: 5, history: 90);
        Assert.Equal(InventoryComboPairEvidence.NoneObserved, row.Evidence);
        Assert.Equal(0, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void InsufficientHistory_zera_confidence()
    {
        var byDays = Classify(pair: 4, target: 10, history: 89);
        var byTx = Classify(pair: 4, target: 4, history: 90);
        Assert.Null(byDays.ConfidenceTargetToAnchor);
        Assert.Null(byTx.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Percentual_nao_promove_Weak_a_Observed()
    {
        var row = Classify(pair: 2, target: 5, history: 90);
        Assert.Equal(InventoryComboPairEvidence.Weak, row.Evidence);
        Assert.Equal(0.4, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Pair_maior_que_Target_nao_clamp_e_InvalidCounts()
    {
        var row = Classify(pair: 6, target: 5, history: 90);
        Assert.Equal(InventoryComboPairEvidence.InvalidCounts, row.Evidence);
        Assert.Null(row.ConfidenceTargetToAnchor);
        Assert.Equal(6, row.PairTransactions);
        Assert.Equal(5, row.TargetTransactions);
    }

    [Fact]
    public void Contagens_negativas_sao_InvalidCounts()
    {
        var pairNeg = Classify(pair: -1, target: 5, history: 90);
        var targetNeg = Classify(pair: 0, target: -1, history: 90);
        Assert.Equal(InventoryComboPairEvidence.InvalidCounts, pairNeg.Evidence);
        Assert.Equal(InventoryComboPairEvidence.InvalidCounts, targetNeg.Evidence);
        Assert.Null(pairNeg.ConfidenceTargetToAnchor);
        Assert.Null(targetNeg.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Target_igual_anchor_e_InvalidCounts()
    {
        var row = InventoryComboPairEvidenceEngine.Classify(Target, Target, 1, 5, 90);
        Assert.Equal(InventoryComboPairEvidence.InvalidCounts, row.Evidence);
        Assert.Null(row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Confidence_nunca_ultrapassa_1()
    {
        var ok = Classify(pair: 5, target: 5, history: 90);
        var invalid = Classify(pair: 9, target: 5, history: 90);
        Assert.True(ok.ConfidenceTargetToAnchor <= 1);
        Assert.Null(invalid.ConfidenceTargetToAnchor);
    }

    static InventoryComboPairCoOccurrenceFacts Classify(int pair, int target, int history) =>
        InventoryComboPairEvidenceEngine.Classify(Target, Anchor, pair, target, history);

    static void AssertInsufficient(InventoryComboPairCoOccurrenceFacts row, int pair, int target)
    {
        Assert.Equal(InventoryComboPairEvidence.InsufficientHistory, row.Evidence);
        Assert.Null(row.ConfidenceTargetToAnchor);
        Assert.Equal(pair, row.PairTransactions);
        Assert.Equal(target, row.TargetTransactions);
    }
}
