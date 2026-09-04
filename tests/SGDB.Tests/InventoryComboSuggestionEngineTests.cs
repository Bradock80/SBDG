using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71A-B4 — motor puro de sugestão/ranking. Sem SQL, UI, recálculo B1/B2/B3.
/// </summary>
public class InventoryComboSuggestionEngineTests
{
    const int TargetId = 11;

    [Fact]
    public void QueryCount_e_top3_constantes()
    {
        Assert.Equal(0, InventoryComboSuggestionEngine.ExpectedQueryCount);
        Assert.Equal(3, InventoryComboSuggestionEngine.MaxSuggestionsPerTarget);
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
    }

    [Fact]
    public void Target_bloqueado_nao_gera_sugestao()
    {
        var snap = Build(
            Target(status: ComboEligibilityStatus.Blocked, reason: ComboTargetEligibilityReason.TargetNoTurnoverNeed),
            Cand(22, InventoryComboPairEvidence.Observed));
        Assert.Empty(snap.Rows);
        Assert.Equal(0, snap.QueryCount);
    }

    [Fact]
    public void Target_unavailable_nao_gera_sugestao()
    {
        var snap = Build(
            Target(confidence: InventoryAttentionConfidence.Unavailable),
            Cand(22, InventoryComboPairEvidence.Observed));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Target_expired_inconsistente_e_descartado()
    {
        var snap = Build(
            Target(reason: ComboTargetEligibilityReason.TargetExpired),
            Cand(22, InventoryComboPairEvidence.Observed));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Anchor_bloqueada_descarta_candidato()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, anchorStatus: ComboEligibilityStatus.Blocked));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Observed_e_permitido()
    {
        var snap = Build(Target(), Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4, targetTx: 10));
        var row = Assert.Single(snap.Rows);
        Assert.Equal(22, row.AnchorProductId);
        Assert.Equal(InventoryComboPairEvidence.Observed, row.PairEvidence);
        Assert.Equal(InventoryAttentionConfidence.Reliable, row.Confidence);
        Assert.Empty(row.Limitations);
        Assert.Equal(ComboTargetEligibilityReason.ExpirySurplus, row.TargetReason);
        Assert.Equal(ComboAnchorEligibilityReason.HealthyNormalCoverage, row.AnchorReason);
    }

    [Fact]
    public void Weak_e_permitido_com_Limited()
    {
        var snap = Build(Target(), Cand(22, InventoryComboPairEvidence.Weak, pairTx: 2, targetTx: 10));
        var row = Assert.Single(snap.Rows);
        Assert.Equal(InventoryAttentionConfidence.Limited, row.Confidence);
        Assert.Equal(InventoryComboSuggestionLimitation.WeakPairEvidence, Assert.Single(row.Limitations));
    }

    [Fact]
    public void Insufficient_sozinho_preenche()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.InsufficientHistory, pairTx: 1, targetTx: 3, confidence: null));
        var row = Assert.Single(snap.Rows);
        Assert.Equal(InventoryAttentionConfidence.Limited, row.Confidence);
        Assert.Equal(
            InventoryComboSuggestionLimitation.InsufficientPairHistory,
            Assert.Single(row.Limitations));
        Assert.Null(row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void NoneObserved_descartado()
    {
        var snap = Build(Target(), Cand(22, InventoryComboPairEvidence.NoneObserved, pairTx: 0, targetTx: 10));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void InvalidCounts_descartado()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.InvalidCounts, pairTx: 9, targetTx: 5, confidence: null));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Observed_vence_Weak_mesmo_com_VMV_menor()
    {
        var snap = Build(
            Target(),
            Cand(30, InventoryComboPairEvidence.Weak, pairTx: 2, vmv: 50, coverage: 80),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 3, vmv: 1, coverage: 16));
        Assert.Equal(new[] { 22, 30 }, Ids(snap));
    }

    [Fact]
    public void Insufficient_nao_fica_acima_de_Weak_mesmo_com_VMV_maior()
    {
        var snap = Build(
            Target(),
            Cand(40, InventoryComboPairEvidence.InsufficientHistory, pairTx: 1, targetTx: 3, confidence: null, vmv: 99, coverage: 200),
            Cand(22, InventoryComboPairEvidence.Weak, pairTx: 1, vmv: 1, coverage: 16));
        Assert.Equal(new[] { 22, 40 }, Ids(snap));
    }

    [Fact]
    public void PairTransactions_maior_primeiro_na_classe()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 3),
            Cand(33, InventoryComboPairEvidence.Observed, pairTx: 8));
        Assert.Equal(new[] { 33, 22 }, Ids(snap));
    }

    [Fact]
    public void Confidence_maior_primeiro_no_empate_de_pares()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4, targetTx: 20, confidence: 0.2),
            Cand(33, InventoryComboPairEvidence.Observed, pairTx: 4, targetTx: 8, confidence: 0.5));
        Assert.Equal(new[] { 33, 22 }, Ids(snap));
    }

    [Fact]
    public void Confidence_null_fica_abaixo_de_numerico()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.InsufficientHistory, pairTx: 1, targetTx: 3, confidence: null, coverage: 20),
            Cand(33, InventoryComboPairEvidence.InsufficientHistory, pairTx: 1, targetTx: 3, confidence: 0.1, coverage: 20));
        Assert.Equal(new[] { 33, 22 }, Ids(snap));
    }

    [Fact]
    public void Coverage_maior_primeiro()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4, coverage: 16, vmv: 2),
            Cand(33, InventoryComboPairEvidence.Observed, pairTx: 4, coverage: 40, vmv: 2));
        Assert.Equal(new[] { 33, 22 }, Ids(snap));
    }

    [Fact]
    public void Vmv_maior_primeiro_depois_da_cobertura()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4, coverage: 20, vmv: 1),
            Cand(33, InventoryComboPairEvidence.Observed, pairTx: 4, coverage: 20, vmv: 5));
        Assert.Equal(new[] { 33, 22 }, Ids(snap));
    }

    [Fact]
    public void ProductId_menor_no_empate_completo()
    {
        var snap = Build(
            Target(),
            Cand(40, InventoryComboPairEvidence.Observed, pairTx: 4, coverage: 20, vmv: 2),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4, coverage: 20, vmv: 2));
        Assert.Equal(new[] { 22, 40 }, Ids(snap));
    }

    [Fact]
    public void Margem_nao_ordena()
    {
        var lowMargin = Cand(40, InventoryComboPairEvidence.Observed, pairTx: 4, coverage: 20, vmv: 2, pairPrice: 30);
        var highMargin = Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4, coverage: 20, vmv: 2, pairPrice: 90);
        var snap = Build(Target(), lowMargin, highMargin);
        Assert.Equal(new[] { 22, 40 }, Ids(snap));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    public void Top3_nunca_ultrapassa(int count)
    {
        var candidates = new InventoryComboCandidate[count];
        for (var i = 0; i < count; i++)
            candidates[i] = Cand(20 + i, InventoryComboPairEvidence.Observed, pairTx: 3 + i);
        var snap = Build(Target(), candidates);
        Assert.Equal(Math.Min(count, 3), snap.Rows.Count);
        Assert.True(snap.Rows.Count <= InventoryComboSuggestionEngine.MaxSuggestionsPerTarget);
    }

    [Fact]
    public void Preenchimento_Observed_Weak_melhor_Insufficient()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 5),
            Cand(33, InventoryComboPairEvidence.Weak, pairTx: 2),
            Cand(44, InventoryComboPairEvidence.InsufficientHistory, pairTx: 1, targetTx: 3, confidence: null, coverage: 10),
            Cand(55, InventoryComboPairEvidence.InsufficientHistory, pairTx: 2, targetTx: 3, confidence: null, coverage: 10));
        Assert.Equal(new[] { 22, 33, 55 }, Ids(snap));
    }

    [Fact]
    public void Tres_Observed_excluem_Insufficient()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 8),
            Cand(33, InventoryComboPairEvidence.Observed, pairTx: 5),
            Cand(44, InventoryComboPairEvidence.Observed, pairTx: 3),
            Cand(55, InventoryComboPairEvidence.InsufficientHistory, pairTx: 2, targetTx: 3, confidence: null, vmv: 99));
        Assert.Equal(new[] { 22, 33, 44 }, Ids(snap));
        Assert.DoesNotContain(snap.Rows, r => r.AnchorProductId == 55);
    }

    [Fact]
    public void Financeiro_unavailable_descarta()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, financialAvailable: false));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Financeiro_sem_CurrentPrices_descarta()
    {
        var financial = Financial(available: true, scenarios:
        [
            new InventoryComboPairFinancialScenario
            {
                Kind = InventoryComboPairFinancialScenarioKind.TargetReductionReference,
                PairPrice = 27.5,
                GrossProfit = 11.5,
                GrossMargin = 11.5 / 27.5,
                ReductionFromCurrent = 2.5,
            },
        ]);
        var snap = Build(Target(), Cand(22, InventoryComboPairEvidence.Observed, financial: financial));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Current_abaixo_do_piso_descarta()
    {
        var financial = Financial(normal: 18, cost: 16, floor: 20, pairPrice: 18);
        var snap = Build(Target(), Cand(22, InventoryComboPairEvidence.Observed, financial: financial));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Cenarios_B3_sao_preservados()
    {
        var scenarios = new InventoryComboPairFinancialScenario[]
        {
            new()
            {
                Kind = InventoryComboPairFinancialScenarioKind.CurrentPrices,
                PairPrice = 30,
                GrossProfit = 14,
                GrossMargin = 14d / 30d,
                ReductionFromCurrent = 0,
            },
            new()
            {
                Kind = InventoryComboPairFinancialScenarioKind.TargetReductionReference,
                PairPrice = 27.5,
                GrossProfit = 11.5,
                GrossMargin = 11.5 / 27.5,
                ReductionFromCurrent = 2.5,
            },
        };
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, financial: Financial(scenarios: scenarios)));
        var row = Assert.Single(snap.Rows);
        Assert.Same(scenarios, row.Scenarios);
        Assert.Equal(2, row.Scenarios.Count);
        Assert.Equal(30, row.NormalPairPrice);
        Assert.Equal(16, row.PairCost);
        Assert.Equal(20, row.PairFloorPrice);
    }

    [Fact]
    public void Target_Limited_mais_Observed_permanece_Limited()
    {
        var target = Target(confidence: InventoryAttentionConfidence.Limited);
        var snap = Build(target, Cand(22, InventoryComboPairEvidence.Observed, target: target));
        var row = Assert.Single(snap.Rows);
        Assert.Equal(InventoryAttentionConfidence.Limited, row.Confidence);
        Assert.Contains(InventoryComboSuggestionLimitation.TargetLimitedConfidence, row.Limitations);
    }

    [Fact]
    public void Lista_vazia_e_valida()
    {
        var snap = InventoryComboSuggestionEngine.BuildForTarget(Target(), []);
        Assert.Empty(snap.Rows);
        Assert.Equal(0, snap.QueryCount);
        Assert.Equal(TargetId, snap.TargetProductId);
    }

    [Fact]
    public void Target_igual_anchor_descarta()
    {
        var snap = Build(Target(), Cand(TargetId, InventoryComboPairEvidence.Observed));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Duplicata_identica_vira_uma()
    {
        var a = Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4);
        var snap = Build(Target(), a, a);
        Assert.Equal(22, Assert.Single(snap.Rows).AnchorProductId);
    }

    [Fact]
    public void Duplicata_conflitante_nao_escolhe_silenciosamente()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 8));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Duplicata_Observed_versus_NoneObserved_descarta_o_par()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4),
            Cand(22, InventoryComboPairEvidence.NoneObserved, pairTx: 0, targetTx: 10));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void PairTransactions_maior_que_target_descarta()
    {
        var snap = Build(
            Target(),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 9, targetTx: 5, confidence: 0.9));
        Assert.Empty(snap.Rows);
    }

    [Fact]
    public void Confidence_fora_de_0_1_descarta()
    {
        var high = Build(Target(), Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4, confidence: 1.2));
        var neg = Build(Target(), Cand(22, InventoryComboPairEvidence.Observed, pairTx: 4, confidence: -0.1));
        Assert.Empty(high.Rows);
        Assert.Empty(neg.Rows);
    }

    [Fact]
    public void Determinismo_mesma_entrada_mesma_ordem()
    {
        var candidates = new[]
        {
            Cand(40, InventoryComboPairEvidence.Weak, pairTx: 2),
            Cand(22, InventoryComboPairEvidence.Observed, pairTx: 5),
            Cand(33, InventoryComboPairEvidence.Observed, pairTx: 5, coverage: 30),
            Cand(55, InventoryComboPairEvidence.InsufficientHistory, pairTx: 1, targetTx: 3, confidence: null),
        };
        var first = Ids(Build(Target(), candidates));
        var second = Ids(Build(Target(), candidates));
        Assert.Equal(first, second);
        Assert.Equal(new[] { 33, 22, 40 }, first);
    }

    [Fact]
    public void Fonte_nao_consulta_nem_recalcula()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryComboSuggestionEngine.cs"));
        var model = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Models", "InventoryComboSuggestion.cs"));
        foreach (var text in new[] { source, model })
        {
            Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ProductService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PurchaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PdvService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PromotionSuggestionEngine", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryComboCoOccurrenceService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryCommercialPriceFloorEngine.Evaluate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryComboPairFinancialEngine", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryComboCoOccurrenceService", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("ComputeFloor", source, StringComparison.Ordinal);
        Assert.Contains("ToCents", source, StringComparison.Ordinal);
    }

    static InventoryComboSuggestionSnapshot Build(
        InventoryComboTargetEligibility target,
        params InventoryComboCandidate[] candidates) =>
        InventoryComboSuggestionEngine.BuildForTarget(target, candidates);

    static int[] Ids(InventoryComboSuggestionSnapshot snap) =>
        snap.Rows.Select(r => r.AnchorProductId).ToArray();

    static InventoryComboTargetEligibility Target(
        ComboEligibilityStatus status = ComboEligibilityStatus.Eligible,
        ComboTargetEligibilityReason reason = ComboTargetEligibilityReason.ExpirySurplus,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable) =>
        new()
        {
            ProductId = TargetId,
            Status = status,
            Reason = reason,
            Confidence = confidence,
        };

    static InventoryComboCandidate Cand(
        int anchorId,
        InventoryComboPairEvidence evidence,
        int pairTx = 4,
        int targetTx = 10,
        double? confidence = 0.4,
        ComboEligibilityStatus anchorStatus = ComboEligibilityStatus.Eligible,
        double vmv = 2,
        double? coverage = 20,
        bool financialAvailable = true,
        InventoryComboPairFinancialFacts? financial = null,
        double pairPrice = 30,
        InventoryComboTargetEligibility? target = null)
    {
        target ??= Target();
        if (evidence == InventoryComboPairEvidence.InsufficientHistory && confidence == 0.4)
            confidence = null;
        if (evidence is InventoryComboPairEvidence.NoneObserved or InventoryComboPairEvidence.InvalidCounts
            && confidence == 0.4)
            confidence = evidence == InventoryComboPairEvidence.NoneObserved ? 0 : null;

        return new InventoryComboCandidate
        {
            TargetEligibility = target,
            AnchorEligibility = new InventoryComboAnchorEligibility
            {
                ProductId = anchorId,
                Status = anchorStatus,
                Reason = anchorStatus == ComboEligibilityStatus.Eligible
                    ? ComboAnchorEligibilityReason.HealthyNormalCoverage
                    : ComboAnchorEligibilityReason.AnchorCoverageUnsafe,
                Confidence = InventoryAttentionConfidence.Reliable,
            },
            PairEvidenceFacts = new InventoryComboPairCoOccurrenceFacts
            {
                TargetProductId = TargetId,
                AnchorProductId = anchorId,
                PairTransactions = pairTx,
                TargetTransactions = targetTx,
                ConfidenceTargetToAnchor = confidence,
                Evidence = evidence,
            },
            FinancialFacts = financial ?? Financial(available: financialAvailable, pairPrice: pairPrice),
            TargetFacts = Turnover(target.ProductId, 80, 1, 80),
            AnchorFacts = Turnover(anchorId, coverage is double days ? days * vmv : 40, vmv, coverage),
        };
    }

    static ProductTurnoverRow Turnover(int id, double stock, double vmv, double? coverage) =>
        new()
        {
            ProductId = id,
            TotalStock = stock,
            Stock = stock,
            Vmv30 = vmv,
            CoverageDays = coverage,
            CoverageBand = InventoryCoverageBand.Normal,
            HasPhysicalAvailabilityEvidence = true,
            HistoryDays = 90,
        };

    static InventoryComboPairFinancialFacts Financial(
        bool available = true,
        double normal = 30,
        double cost = 16,
        double floor = 20,
        double pairPrice = 30,
        InventoryComboPairFinancialScenario[]? scenarios = null)
    {
        if (!available)
        {
            return new InventoryComboPairFinancialFacts
            {
                Status = InventoryComboPairFinancialStatus.Unavailable,
                Reason = InventoryComboPairFinancialReason.MarginPolicyUnavailable,
            };
        }

        scenarios ??=
        [
            new InventoryComboPairFinancialScenario
            {
                Kind = InventoryComboPairFinancialScenarioKind.CurrentPrices,
                PairPrice = pairPrice,
                GrossProfit = pairPrice - cost,
                GrossMargin = pairPrice > 0 ? (pairPrice - cost) / pairPrice : 0,
                ReductionFromCurrent = 0,
            },
        ];

        return new InventoryComboPairFinancialFacts
        {
            Status = InventoryComboPairFinancialStatus.Available,
            Reason = InventoryComboPairFinancialReason.None,
            NormalPairPrice = normal,
            PairCost = cost,
            PairFloorPrice = floor,
            Scenarios = scenarios,
        };
    }

    static string FindSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
