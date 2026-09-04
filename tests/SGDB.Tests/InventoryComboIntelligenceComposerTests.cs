using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71A-B5 — composer/orquestrador em memória. Sem UI, schema, PDV ou persistência.
/// B2 é injetado para medir chamadas; B3/B4 permanecem puros.
/// </summary>
public class InventoryComboIntelligenceComposerTests
{
    static readonly DateTime Today = new(2026, 9, 3);

    [Fact]
    public void Query_budget_constantes()
    {
        Assert.Equal(0, InventoryComboIntelligenceComposer.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboTargetEligibilityEngine.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboAnchorEligibilityEngine.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboPairFinancialEngine.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboSuggestionEngine.ExpectedQueryCount);
        Assert.Equal(1, InventoryComboCoOccurrenceService.ExpectedQueryCount);
        Assert.Equal(9, InventoryComboIntelligenceComposer.ExpectedBasePipelineQueryCount);
        Assert.Equal(10, InventoryComboIntelligenceComposer.ExpectedPipelineQueryCount);
        Assert.Equal(20, InventoryComboIntelligenceComposer.MaxPreselectedAnchorsPerTarget);
        Assert.Equal(3, InventoryComboSuggestionEngine.MaxSuggestionsPerTarget);
        Assert.Equal(
            InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount
            + InventoryComboCoOccurrenceService.ExpectedQueryCount,
            InventoryComboIntelligenceComposer.ExpectedPipelineQueryCount);
    }

    [Fact]
    public void Cenario_controlado_T1_T2_T3()
    {
        var t1 = ExpiryTarget(1);
        var t2 = ExcessTarget(2);
        var t3 = CriticalProduct(3);
        var a1 = HealthyAnchor(10, coverage: 40, vmv: 2);
        var a2 = HealthyAnchor(11, coverage: 30, vmv: 2);
        var a3 = ConsiderReplenishment(12);
        var a4 = AttentionCoverage(13);
        var a5 = CompositionProduct(14);
        var probe = new CoOccurrenceProbe
        {
            EvidenceOf = (t, a) => (t, a) switch
            {
                (1, 10) => InventoryComboPairEvidence.Observed,
                (1, 11) => InventoryComboPairEvidence.Weak,
                (2, 10) => InventoryComboPairEvidence.NoneObserved,
                (2, 11) => InventoryComboPairEvidence.Observed,
                _ => InventoryComboPairEvidence.NoneObserved,
            },
        };

        var snap = Compose(probe, t1, t2, t3, a1, a2, a3, a4, a5);

        Assert.Equal(10, snap.QueryCount);
        Assert.Equal(1, snap.CoOccurrenceQueryCount);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(2, snap.EligibleTargets);
        Assert.Equal(2, snap.EligibleAnchors);
        Assert.Equal(2, snap.Targets.Count);
        Assert.False(snap.ByProductId.ContainsKey(3));
        Assert.DoesNotContain(snap.RequestedAnchorIds, id => id is 12 or 13 or 14);

        var g1 = snap.ByProductId[1];
        Assert.Equal(ComboTargetEligibilityReason.ExpirySurplus, g1.Eligibility.Reason);
        Assert.Equal(new[] { 10, 11 }, g1.Suggestions.Select(s => s.AnchorProductId).ToArray());
        Assert.Equal(InventoryComboPairEvidence.Observed, g1.Suggestions[0].PairEvidence);
        Assert.Equal(InventoryComboPairEvidence.Weak, g1.Suggestions[1].PairEvidence);

        var g2 = snap.ByProductId[2];
        Assert.Equal(ComboTargetEligibilityReason.ProjectedExcess, g2.Eligibility.Reason);
        var only = Assert.Single(g2.Suggestions);
        Assert.Equal(11, only.AnchorProductId);
        Assert.Equal(InventoryComboPairEvidence.Observed, only.PairEvidence);
        Assert.Equal(3, snap.PairFinancialEvaluations);
        Assert.True(snap.PairFinancialEvaluations < snap.EligibleTargets * snap.EligibleAnchors);
    }

    [Fact]
    public void Top3_delegado_ao_B4()
    {
        var target = ExpiryTarget(1);
        var anchors = Enumerable.Range(20, 5).Select(id => HealthyAnchor(id, coverage: 20 + id, vmv: 2)).ToArray();
        var probe = new CoOccurrenceProbe
        {
            PairTransactionsOf = (_, a) => 3 + (a - 20),
            EvidenceOf = (_, _) => InventoryComboPairEvidence.Observed,
        };
        var snap = Compose(probe, new[] { target }.Concat(anchors).ToArray());
        var ids = Assert.Single(snap.Targets).Suggestions.Select(s => s.AnchorProductId).ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Equal(new[] { 24, 23, 22 }, ids);
    }

    [Fact]
    public void Preselecao_K20_nao_usa_par_nem_preco()
    {
        var target = IdleTarget(1);
        var anchors = new List<CatalogProduct>();
        for (var i = 0; i < 25; i++)
        {
            var id = 100 + i;
            anchors.Add(HealthyAnchor(id, coverage: 40 - i, vmv: 2));
        }

        var probe = new CoOccurrenceProbe();
        var snap = Compose(probe, new[] { target }.Concat(anchors).ToArray());
        Assert.Equal(1, probe.Calls);
        Assert.Equal(20, probe.LastAnchors!.Count);
        Assert.Equal(Enumerable.Range(100, 20).ToArray(), probe.LastAnchors.ToArray());
        Assert.DoesNotContain(124, probe.LastAnchors);
        Assert.Equal(20, snap.RequestedAnchorIds.Count);
        Assert.Equal(
            2 + snap.RequestedTargetIds.Count + snap.RequestedAnchorIds.Count,
            InventoryComboIntelligenceComposer.EstimateCoOccurrenceParameterCount(
                snap.RequestedTargetIds.Count, snap.RequestedAnchorIds.Count));
        Assert.True(
            InventoryComboIntelligenceComposer.EstimateCoOccurrenceParameterCount(
                snap.RequestedTargetIds.Count, snap.RequestedAnchorIds.Count)
            < InventoryComboIntelligenceComposer.SqliteMaxVariableNumber);
    }

    [Fact]
    public void Self_e_removido_antes_do_Take20()
    {
        var self = DualEligible(50, coverage: 90);
        var others = new List<CatalogProduct>();
        for (var i = 0; i < 21; i++)
            others.Add(HealthyAnchor(200 + i, coverage: 40 - i, vmv: 1));

        var probe = new CoOccurrenceProbe();
        var snap = Compose(probe, new[] { self }.Concat(others).ToArray());
        Assert.Equal(1, snap.EligibleTargets);
        Assert.Equal(22, snap.EligibleAnchors);
        Assert.Equal(20, probe.LastAnchors!.Count);
        Assert.DoesNotContain(50, probe.LastAnchors);
        Assert.Equal(20, probe.LastAnchors.Count);
        Assert.Equal(Enumerable.Range(200, 20).ToArray(), probe.LastAnchors.ToArray());
        Assert.DoesNotContain(220, probe.LastAnchors);
    }

    [Fact]
    public void Varios_targets_B2_uma_vez()
    {
        var products = new List<CatalogProduct>
        {
            IdleTarget(1, history: 90),
            IdleTarget(2, history: 120),
            IdleTarget(3, history: 91),
        };
        for (var i = 0; i < 4; i++)
            products.Add(HealthyAnchor(10 + i, coverage: 40 - i, vmv: 2));

        var probe = new CoOccurrenceProbe();
        var snap = Compose(probe, products.ToArray());
        Assert.Equal(1, probe.Calls);
        Assert.Equal(1, snap.CoOccurrenceCalls);
        Assert.Equal(3, snap.EligibleTargets);
        Assert.Equal(new[] { 1, 2, 3 }, probe.LastTargets!.ToArray());
        Assert.Equal(90, probe.LastHistory![1]);
        Assert.Equal(120, probe.LastHistory[2]);
        Assert.Equal(Today, probe.LastToday);
    }

    [Fact]
    public void Sem_target_nao_chama_B2_query_9()
    {
        var probe = new CoOccurrenceProbe();
        var snap = Compose(probe, HealthyAnchor(1), HealthyAnchor(2));
        Assert.Equal(0, probe.Calls);
        Assert.Equal(0, snap.CoOccurrenceCalls);
        Assert.Equal(0, snap.CoOccurrenceQueryCount);
        Assert.Equal(9, snap.QueryCount);
        Assert.Equal(0, snap.EligibleTargets);
        Assert.Empty(snap.Targets);
        Assert.Empty(snap.RequestedTargetIds);
    }

    [Fact]
    public void Sem_anchor_preserva_target_com_zero_sugestoes()
    {
        var probe = new CoOccurrenceProbe();
        var snap = Compose(probe, IdleTarget(1), ConsiderReplenishment(2), CompositionProduct(3));
        Assert.Equal(0, probe.Calls);
        Assert.Equal(9, snap.QueryCount);
        Assert.Equal(1, snap.EligibleTargets);
        Assert.Equal(0, snap.EligibleAnchors);
        var group = Assert.Single(snap.Targets);
        Assert.Equal(1, group.ProductId);
        Assert.Empty(group.Suggestions);
        Assert.Equal(ComboEligibilityStatus.Eligible, group.Eligibility.Status);
    }

    [Fact]
    public void Sem_par_depois_de_excluir_self_nao_chama_B2()
    {
        var probe = new CoOccurrenceProbe();
        var snap = Compose(probe, DualEligible(7, coverage: 40));
        Assert.Equal(1, snap.EligibleTargets);
        Assert.Equal(1, snap.EligibleAnchors);
        Assert.Equal(0, probe.Calls);
        Assert.Equal(9, snap.QueryCount);
        Assert.Empty(Assert.Single(snap.Targets).Suggestions);
    }

    [Fact]
    public void NoneObserved_pula_B3_e_nao_entra_no_B4()
    {
        var probe = new CoOccurrenceProbe
        {
            EvidenceOf = (_, _) => InventoryComboPairEvidence.NoneObserved,
        };
        var snap = Compose(probe, IdleTarget(1), HealthyAnchor(2));
        Assert.Equal(1, probe.Calls);
        Assert.Equal(10, snap.QueryCount);
        Assert.Equal(0, snap.PairFinancialEvaluations);
        Assert.Equal(0, snap.PairCandidatesEvaluated);
        Assert.Empty(Assert.Single(snap.Targets).Suggestions);
    }

    [Fact]
    public void InsufficientHistory_executa_B3_e_pode_preencher()
    {
        var probe = new CoOccurrenceProbe
        {
            EvidenceOf = (_, _) => InventoryComboPairEvidence.InsufficientHistory,
        };
        var snap = Compose(probe, IdleTarget(1, history: 20), HealthyAnchor(2));
        Assert.Equal(1, snap.PairFinancialEvaluations);
        var suggestion = Assert.Single(Assert.Single(snap.Targets).Suggestions);
        Assert.Equal(InventoryComboPairEvidence.InsufficientHistory, suggestion.PairEvidence);
        Assert.Equal(2, suggestion.AnchorProductId);
        Assert.True(suggestion.NormalPairPrice > 0);
    }

    [Fact]
    public void Weak_nao_pula_B3()
    {
        var probe = new CoOccurrenceProbe
        {
            EvidenceOf = (_, _) => InventoryComboPairEvidence.Weak,
        };
        var snap = Compose(probe, IdleTarget(1), HealthyAnchor(2));
        Assert.Equal(1, snap.PairFinancialEvaluations);
        Assert.Equal(InventoryComboPairEvidence.Weak, Assert.Single(Assert.Single(snap.Targets).Suggestions).PairEvidence);
    }

    [Fact]
    public void Financeiro_indisponivel_nao_gera_sugestao()
    {
        var probe = new CoOccurrenceProbe
        {
            EvidenceOf = (_, _) => InventoryComboPairEvidence.Observed,
        };
        var snap = Compose(
            probe,
            MissingPolicy(),
            IdleTarget(1),
            HealthyAnchor(2));
        Assert.Equal(1, snap.PairFinancialEvaluations);
        Assert.Empty(Assert.Single(snap.Targets).Suggestions);
    }

    [Fact]
    public void Uniao_nao_vaza_par_extra_para_o_B4()
    {
        var t1 = DualEligible(100, coverage: 90);
        var t2 = IdleTarget(1, history: 90);
        var others = new List<CatalogProduct>();
        for (var i = 0; i < 21; i++)
            others.Add(HealthyAnchor(200 + i, coverage: 40 - i, vmv: 1));

        var probe = new CoOccurrenceProbe
        {
            EvidenceOf = (_, _) => InventoryComboPairEvidence.Observed,
        };
        var snap = Compose(probe, new[] { t1, t2 }.Concat(others).ToArray());
        Assert.Equal(1, probe.Calls);
        Assert.True(probe.LastAnchors!.Count > 20, "união deve ser maior que K de um alvo.");
        Assert.Contains(219, probe.LastAnchors);
        Assert.DoesNotContain(220, probe.LastAnchors);

        var t2Ids = snap.ByProductId[1].Suggestions.Select(s => s.AnchorProductId).ToHashSet();
        Assert.DoesNotContain(219, t2Ids);
        var t1Ids = snap.ByProductId[100].Suggestions.Select(s => s.AnchorProductId).ToHashSet();
        Assert.DoesNotContain(100, t1Ids);
    }

    [Fact]
    public void Determinismo_nao_depende_da_ordem_de_entrada()
    {
        var products = new List<CatalogProduct>
        {
            ExcessTarget(5),
            ExpiryTarget(2),
            HealthyAnchor(30, coverage: 25, vmv: 3),
            HealthyAnchor(20, coverage: 25, vmv: 1),
            HealthyAnchor(40, coverage: 50, vmv: 1),
            CriticalProduct(9),
        };
        var probeA = new CoOccurrenceProbe();
        var a = Compose(probeA, products.ToArray());
        products.Reverse();
        var probeB = new CoOccurrenceProbe();
        var b = Compose(probeB, products.ToArray());

        Assert.Equal(Ids(a), Ids(b));
        Assert.Equal(a.QueryCount, b.QueryCount);
        Assert.Equal(a.EligibleTargets, b.EligibleTargets);
        Assert.Equal(probeA.LastAnchors!.ToArray(), probeB.LastAnchors!.ToArray());
    }

    [Fact]
    public void N_mais_1_5_targets_20_anchors_B2_uma_vez()
    {
        var products = new List<CatalogProduct>();
        for (var i = 0; i < 5; i++)
            products.Add(IdleTarget(10 + i));
        for (var i = 0; i < 20; i++)
            products.Add(HealthyAnchor(100 + i, coverage: 40 - i, vmv: 2));

        var probe = new CoOccurrenceProbe();
        var snap = Compose(probe, products.ToArray());
        Assert.Equal(1, probe.Calls);
        Assert.Equal(5, snap.EligibleTargets);
        Assert.Equal(20, snap.EligibleAnchors);
        Assert.Equal(20, probe.LastAnchors!.Count);
        Assert.Equal(5 * 20, snap.PairFinancialEvaluations);
        Assert.Equal(5 * 20, snap.PairCandidatesEvaluated);
        Assert.Equal(
            2 + 5 + 20,
            InventoryComboIntelligenceComposer.EstimateCoOccurrenceParameterCount(5, 20));
        Assert.True(2 + 5 + 20 < 999);
    }

    [Fact]
    public void Alvo_elegivel_sem_sugestao_permanece_no_snapshot()
    {
        var probe = new CoOccurrenceProbe
        {
            EvidenceOf = (_, _) => InventoryComboPairEvidence.NoneObserved,
        };
        var snap = Compose(probe, IdleTarget(8), HealthyAnchor(9));
        Assert.True(snap.ByProductId.ContainsKey(8));
        Assert.Empty(snap.ByProductId[8].Suggestions);
        Assert.Equal(ComboEligibilityStatus.Eligible, snap.ByProductId[8].Eligibility.Status);
        Assert.False(snap.ByProductId.ContainsKey(9));
    }

    [Fact]
    public void Code_e_Name_70C_ficam_disponiveis()
    {
        var target = IdleTarget(1);
        var snap = Compose(new CoOccurrenceProbe(), target, HealthyAnchor(2));
        var group = Assert.Single(snap.Targets);
        Assert.Equal("C1", group.Code);
        Assert.Equal("P1", group.Name);
    }

    [Fact]
    public void Composer_nao_abre_kit_nem_recalcula_autoridades()
    {
        var source = ReadSource("src", "SGDB.App", "Services", "InventoryComboIntelligenceComposer.cs");
        var model = ReadSource("src", "SGDB.App", "Models", "InventoryComboIntelligence.cs");
        foreach (var text in new[] { source, model })
        {
            Assert.DoesNotContain("ProductCompositionService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PurchaseGuidanceEngine", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryIntelligenceService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryProjectionService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryAttentionEngine", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryCommercialFactsService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MainWindow", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("maço", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("caixa", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SELECT ", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("InventoryComboCoOccurrenceService", source, StringComparison.Ordinal);
        Assert.Contains("InventoryComboSuggestionEngine.BuildForTarget", source, StringComparison.Ordinal);
        Assert.Contains("InventoryComboPairFinancialEngine.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("MaxPreselectedAnchorsPerTarget = 20", source, StringComparison.Ordinal);
        Assert.Contains("if (anchor.ProductId == targetProductId)", source, StringComparison.Ordinal);
        Assert.Contains("ids.Count >= MaxPreselectedAnchorsPerTarget", source, StringComparison.Ordinal);
    }

    static int[] Ids(InventoryComboIntelligenceSnapshot snap) =>
        snap.Targets
            .SelectMany(t => t.Suggestions.Select(s => (t.ProductId, s.AnchorProductId)))
            .Select(x => x.ProductId * 1000 + x.AnchorProductId)
            .ToArray();

    static InventoryComboIntelligenceSnapshot Compose(
        CoOccurrenceProbe probe,
        params CatalogProduct[] products) =>
        Compose(probe, Policy(), products);

    static InventoryComboIntelligenceSnapshot Compose(
        CoOccurrenceProbe probe,
        InventoryCommercialMarginPolicyResolution policy,
        params CatalogProduct[] products)
    {
        var intelligence = new InventoryIntelligenceSnapshot
        {
            Today = Today,
            QueryCount = 6,
            Rows = products.Select(p => p.Turnover).ToList(),
        };
        var attention = new InventoryAttentionSnapshot
        {
            Today = Today,
            Results = products.Select(p => p.Attention).ToList(),
            ByProductId = products.ToDictionary(p => p.ProductId, p => p.Attention),
        };
        var facts = new InventoryCommercialFactsSnapshot
        {
            QueryCount = 1,
            Rows = products.Select(p => p.Facts).ToList(),
            ByProductId = products.ToDictionary(p => p.ProductId, p => p.Facts),
        };
        var guidance = new InventoryPurchaseGuidanceSnapshot
        {
            QueryCount = 0,
            Results = products.Select(p => p.Guidance).ToList(),
            ByProductId = products.ToDictionary(p => p.ProductId, p => p.Guidance),
        };
        return InventoryComboIntelligenceComposer.Compose(
            new InventoryComboIntelligenceComposeInput
            {
                Today = Today,
                Intelligence = intelligence,
                Attention = attention,
                Facts = facts,
                Guidance = guidance,
                PolicyResolution = policy,
            },
            probe.Load);
    }

    static InventoryCommercialMarginPolicyResolution Policy() =>
        new()
        {
            Status = InventoryCommercialMarginPolicyResolutionStatus.Available,
            Source = InventoryCommercialMarginPolicySource.Global,
            EffectiveMinimumGrossMarginPercent = 20m,
        };

    static InventoryCommercialMarginPolicyResolution MissingPolicy() =>
        new()
        {
            Status = InventoryCommercialMarginPolicyResolutionStatus.Missing,
            Source = InventoryCommercialMarginPolicySource.None,
            Reasons = [InventoryCommercialMarginSettingReason.Missing],
        };

    static CatalogProduct IdleTarget(int id, int history = 90) =>
        new(
            id,
            Turnover(id, stock: 80, vmv: 0, history: history, idle: true, band: InventoryCoverageBand.NotCalculable, coverage: null),
            Attention(id, InventoryAttentionReason.Idle, InventoryAttentionFamily.Turnover, InventoryOperatorAction.Monitor),
            CommercialFacts(id),
            Guidance(id, InventoryPurchaseGuidanceAction.DoNotReplenishNow, InventoryPurchaseGuidanceReason.IdleStock, InventoryPurchaseGuidanceStatus.GuidanceAvailable));

    static CatalogProduct ExpiryTarget(int id) =>
        new(
            id,
            Turnover(id, stock: 80, vmv: 1, history: 90, band: InventoryCoverageBand.Normal, coverage: 80),
            Attention(
                id,
                InventoryAttentionReason.SurplusAtExpiry,
                InventoryAttentionFamily.Expiry,
                InventoryOperatorAction.PrioritizeSale,
                surplus: 4),
            CommercialFacts(id),
            Guidance(
                id,
                InventoryPurchaseGuidanceAction.DoNotReplenishNow,
                InventoryPurchaseGuidanceReason.ProjectedExpirySurplus,
                InventoryPurchaseGuidanceStatus.GuidanceAvailable));

    static CatalogProduct ExcessTarget(int id) =>
        new(
            id,
            Turnover(id, stock: 100, vmv: 2, history: 90, band: InventoryCoverageBand.Normal, coverage: 50),
            Attention(
                id,
                InventoryAttentionReason.ProjectedExcess30,
                InventoryAttentionFamily.Excess,
                InventoryOperatorAction.EvaluateExcess,
                excess: 40),
            CommercialFacts(id),
            Guidance(
                id,
                InventoryPurchaseGuidanceAction.DoNotReplenishNow,
                InventoryPurchaseGuidanceReason.ProjectedExcess30,
                InventoryPurchaseGuidanceStatus.GuidanceAvailable));

    static CatalogProduct HealthyAnchor(int id, double coverage = 20, double vmv = 2) =>
        new(
            id,
            Turnover(id, stock: coverage * vmv, vmv: vmv, history: 90, band: InventoryCoverageBand.Normal, coverage: coverage),
            Attention(id, InventoryAttentionReason.None, InventoryAttentionFamily.Normal, InventoryOperatorAction.Monitor),
            CommercialFacts(id),
            Guidance(id, InventoryPurchaseGuidanceAction.Monitor, InventoryPurchaseGuidanceReason.None, InventoryPurchaseGuidanceStatus.Monitor));

    static CatalogProduct DualEligible(int id, double coverage) =>
        new(
            id,
            Turnover(id, stock: coverage * 2, vmv: 2, history: 90, idle: true, band: InventoryCoverageBand.Normal, coverage: coverage),
            Attention(id, InventoryAttentionReason.Idle, InventoryAttentionFamily.Turnover, InventoryOperatorAction.Monitor),
            CommercialFacts(id),
            Guidance(id, InventoryPurchaseGuidanceAction.Monitor, InventoryPurchaseGuidanceReason.None, InventoryPurchaseGuidanceStatus.Monitor));

    static CatalogProduct CriticalProduct(int id) =>
        new(
            id,
            Turnover(id, stock: 2, vmv: 1, history: 90, band: InventoryCoverageBand.Critical, coverage: 2),
            Attention(id, InventoryAttentionReason.None, InventoryAttentionFamily.Normal, InventoryOperatorAction.Monitor),
            CommercialFacts(id),
            Guidance(
                id,
                InventoryPurchaseGuidanceAction.ConsiderReplenishment,
                InventoryPurchaseGuidanceReason.CriticalCoverage,
                InventoryPurchaseGuidanceStatus.GuidanceAvailable));

    static CatalogProduct ConsiderReplenishment(int id) =>
        new(
            id,
            Turnover(id, stock: 5, vmv: 1, history: 90, band: InventoryCoverageBand.Low, coverage: 5),
            Attention(id, InventoryAttentionReason.None, InventoryAttentionFamily.Turnover, InventoryOperatorAction.Monitor),
            CommercialFacts(id),
            Guidance(
                id,
                InventoryPurchaseGuidanceAction.ConsiderReplenishment,
                InventoryPurchaseGuidanceReason.LowCoverage,
                InventoryPurchaseGuidanceStatus.GuidanceAvailable));

    static CatalogProduct AttentionCoverage(int id) =>
        new(
            id,
            Turnover(id, stock: 20, vmv: 2, history: 90, band: InventoryCoverageBand.Attention, coverage: 10),
            Attention(id, InventoryAttentionReason.None, InventoryAttentionFamily.Normal, InventoryOperatorAction.Monitor),
            CommercialFacts(id),
            Guidance(id, InventoryPurchaseGuidanceAction.Monitor, InventoryPurchaseGuidanceReason.None, InventoryPurchaseGuidanceStatus.Monitor));

    static CatalogProduct CompositionProduct(int id) =>
        new(
            id,
            Turnover(id, stock: 40, vmv: 2, history: 90, composition: true, band: InventoryCoverageBand.Normal, coverage: 20),
            Attention(id, InventoryAttentionReason.CompositionProduct, InventoryAttentionFamily.DataQuality, InventoryOperatorAction.Monitor),
            InventoryCommercialFactsEngine.Classify(new InventoryCommercialFactsInput
            {
                ProductId = id,
                ProductFound = true,
                CatalogSalePrice = 10,
                CurrentAverageCost = 6,
                AllowsSale = true,
                IsCompositionProduct = true,
            }),
            Guidance(
                id,
                InventoryPurchaseGuidanceAction.None,
                InventoryPurchaseGuidanceReason.CompositionProduct,
                InventoryPurchaseGuidanceStatus.NotApplicable));

    static ProductTurnoverRow Turnover(
        int id,
        double stock,
        double vmv,
        int history,
        InventoryCoverageBand band,
        double? coverage,
        bool idle = false,
        bool composition = false) =>
        new()
        {
            ProductId = id,
            Code = "C" + id,
            Name = "P" + id,
            Stock = stock,
            TotalStock = stock,
            Vmv30 = vmv,
            HistoryDays = history,
            CoverageBand = band,
            CoverageDays = coverage,
            IsIdle = idle,
            IsCompositionProduct = composition,
            HasPhysicalAvailabilityEvidence = true,
            IsHistoryInsufficient30 = history < 30,
            IsHistoryInsufficient90 = history < 90,
        };

    static InventoryAttentionResult Attention(
        int id,
        InventoryAttentionReason primary,
        InventoryAttentionFamily family,
        InventoryOperatorAction action,
        double? surplus = null,
        double? excess = null) =>
        new()
        {
            ProductId = id,
            PrimaryReason = primary,
            Family = family,
            Action = action,
            Confidence = InventoryAttentionConfidence.Reliable,
            ProjectedExpirySurplusQuantity = surplus,
            ProjectedExcessQuantity = excess,
            SecondaryReasons = [],
        };

    static InventoryCommercialFacts CommercialFacts(int id) =>
        InventoryCommercialFactsEngine.Classify(new InventoryCommercialFactsInput
        {
            ProductId = id,
            ProductFound = true,
            CatalogSalePrice = 10,
            CurrentAverageCost = 6,
            AllowsSale = true,
        });

    static InventoryPurchaseGuidanceResult Guidance(
        int id,
        InventoryPurchaseGuidanceAction action,
        InventoryPurchaseGuidanceReason primary,
        InventoryPurchaseGuidanceStatus status) =>
        new()
        {
            ProductId = id,
            Action = action,
            PrimaryReason = primary,
            Status = status,
            Confidence = InventoryAttentionConfidence.Reliable,
            SecondaryReasons = [],
        };

    static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    sealed record CatalogProduct(
        int ProductId,
        ProductTurnoverRow Turnover,
        InventoryAttentionResult Attention,
        InventoryCommercialFacts Facts,
        InventoryPurchaseGuidanceResult Guidance);

    sealed class CoOccurrenceProbe
    {
        public int Calls { get; private set; }
        public IReadOnlyList<int>? LastTargets { get; private set; }
        public IReadOnlyList<int>? LastAnchors { get; private set; }
        public IReadOnlyDictionary<int, int>? LastHistory { get; private set; }
        public DateTime LastToday { get; private set; }
        public Func<int, int, InventoryComboPairEvidence>? EvidenceOf { get; init; }
        public Func<int, int, int>? PairTransactionsOf { get; init; }

        public InventoryComboCoOccurrenceSnapshot Load(
            IReadOnlyList<int> targetIds,
            IReadOnlyList<int> anchorIds,
            IReadOnlyDictionary<int, int> targetHistoryDays,
            DateTime today)
        {
            Calls++;
            LastTargets = targetIds.ToList();
            LastAnchors = anchorIds.ToList();
            LastHistory = new Dictionary<int, int>(targetHistoryDays);
            LastToday = today.Date;

            var rows = new List<InventoryComboPairCoOccurrenceFacts>();
            foreach (var targetId in targetIds)
            {
                targetHistoryDays.TryGetValue(targetId, out var history);
                var targetTx = 10;
                foreach (var anchorId in anchorIds)
                {
                    if (targetId == anchorId)
                        continue;
                    var evidence = EvidenceOf?.Invoke(targetId, anchorId)
                        ?? InventoryComboPairEvidence.Observed;
                    var pairTx = PairTransactionsOf?.Invoke(targetId, anchorId)
                        ?? PairTx(evidence);
                    rows.Add(InventoryComboPairEvidenceEngine.Classify(
                        targetId, anchorId, pairTx, targetTx, history));
                }
            }

            return new InventoryComboCoOccurrenceSnapshot
            {
                QueryCount = InventoryComboCoOccurrenceService.ExpectedQueryCount,
                RequestedTargetIds = targetIds.ToList(),
                RequestedAnchorIds = anchorIds.ToList(),
                Rows = rows,
            };
        }

        static int PairTx(InventoryComboPairEvidence evidence) =>
            evidence switch
            {
                InventoryComboPairEvidence.Observed => 5,
                InventoryComboPairEvidence.Weak => 2,
                InventoryComboPairEvidence.NoneObserved => 0,
                InventoryComboPairEvidence.InsufficientHistory => 1,
                _ => 0,
            };
    }
}
