using System.Globalization;
using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B4C — composer puro. Sem SQL, UI, PDV, promoção ou recálculo 70C–B4B.
/// </summary>
public class InventoryCommercialScenarioComposerTests
{
    [Fact]
    public void QueryCount_do_composer_e_zero() =>
        Assert.Equal(0, InventoryCommercialScenarioComposer.ExpectedQueryCount);

    [Fact]
    public void Pipeline_herdado_e_9()
    {
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialEligibilityEngine.ExpectedQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialPriceFloorEngine.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryCommercialMarginPolicyResolver.ExpectedQueryCount
            + InventoryCommercialScenarioEngine.ExpectedQueryCount
            + InventoryCommercialScenarioComposer.ExpectedQueryCount);
    }

    [Fact]
    public void Snapshot_QueryCount_e_9() =>
        Assert.Equal(9, Compose(Happy()).QueryCount);

    [Fact]
    public void Um_produto_completo_Available()
    {
        var snapshot = Compose(Happy());
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(1, row.ProductId);
        Assert.Equal(InventoryCommercialScenarioStatus.Available, row.ScenarioResult.Status);
        Assert.NotEmpty(row.ScenarioResult.Scenarios);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, row.PriceFloor.Status);
        Assert.True(row.Facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Multiplos_produtos()
    {
        var snapshot = Compose(Happy(ids: [3, 1, 2]));
        Assert.Equal(3, snapshot.Rows.Count);
        Assert.Equal(new[] { 3, 1, 2 }, snapshot.Rows.Select(r => r.ProductId));
    }

    [Fact]
    public void Ordem_70C_preservada()
    {
        var snapshot = Compose(Happy(ids: [10, 2, 7]));
        Assert.Equal(new[] { 10, 2, 7 }, snapshot.Rows.Select(r => r.ProductId).ToArray());
        Assert.NotEqual(
            snapshot.Rows.Select(r => r.ProductId).OrderBy(id => id),
            snapshot.Rows.Select(r => r.ProductId));
    }

    [Fact]
    public void Expired_permanece_Expired()
    {
        var snapshot = Compose(Happy(
            ids: [1],
            kind: InventoryCommercialEligibilityKind.NoCommercialRecommendation,
            reason: InventoryCommercialEligibilityReason.Expired,
            excess: 12));
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialScenarioStatus.Expired, row.ScenarioResult.Status);
        Assert.Empty(row.ScenarioResult.Scenarios);
        Assert.Equal(InventoryCommercialEligibilityReason.Expired, row.Eligibility.PrimaryReason);
    }

    [Fact]
    public void Idle_permanece_MonitorOnly()
    {
        var snapshot = Compose(Happy(
            reason: InventoryCommercialEligibilityReason.Idle,
            excess: null));
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, row.ScenarioResult.Status);
        Assert.Empty(row.ScenarioResult.Scenarios);
        Assert.Equal(InventoryCommercialScenarioThesis.Idle, row.ScenarioResult.Thesis);
    }

    [Fact]
    public void ExpiresToday_permanece_MonitorOnly()
    {
        var snapshot = Compose(Happy(
            kind: InventoryCommercialEligibilityKind.MonitorOnly,
            reason: InventoryCommercialEligibilityReason.ExpiresToday));
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, row.ScenarioResult.Status);
        Assert.Equal(InventoryCommercialScenarioReason.ExpiresToday, row.ScenarioResult.PrimaryReason);
        Assert.Empty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Limited_sem_cenario()
    {
        var snapshot = Compose(Happy(confidence: InventoryAttentionConfidence.Limited, excess: 20));
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, row.ScenarioResult.Status);
        Assert.Empty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Policy_Missing_global_nao_esvazia_snapshot()
    {
        var snapshot = Compose(Happy(policy: MissingPolicy()));
        Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Missing, snapshot.PolicyResolution.Status);
        Assert.Equal(InventoryCommercialScenarioStatus.PolicyMissing, snapshot.Rows[0].ScenarioResult.Status);
        Assert.Empty(snapshot.Rows[0].ScenarioResult.Scenarios);
        Assert.Null(snapshot.Rows[0].ScenarioResult.MinimumGrossMarginPercent);
    }

    [Fact]
    public void Policy_Invalid_global()
    {
        var snapshot = Compose(Happy(policy: InvalidPolicy()));
        Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Invalid, snapshot.PolicyResolution.Status);
        Assert.Equal(InventoryCommercialScenarioStatus.PolicyInvalid, snapshot.Rows[0].ScenarioResult.Status);
        Assert.Empty(snapshot.Rows[0].ScenarioResult.Scenarios);
    }

    [Fact]
    public void Policy_0_porcento_e_valida()
    {
        var snapshot = Compose(Happy(
            sale: 12,
            cost: 10,
            policy: AvailablePolicy(0m)));
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialScenarioStatus.Available, row.ScenarioResult.Status);
        Assert.Equal(0, row.ScenarioResult.MinimumGrossMarginPercent);
        Assert.Equal(10, row.PriceFloor.MinimumAllowedCatalogPrice);
        Assert.NotEmpty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Floor_calculado_via_B3()
    {
        var snapshot = Compose(Happy(sale: 10, cost: 6, policy: AvailablePolicy(20m)));
        var row = Assert.Single(snapshot.Rows);
        var expected = InventoryCommercialPriceFloorEngine.Evaluate(
            row.Facts,
            InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(AvailablePolicy(20m)));
        Assert.Equal(expected.MinimumAllowedCatalogPrice, row.PriceFloor.MinimumAllowedCatalogPrice);
        Assert.Equal(expected.AmountAboveMinimumAllowedCatalogPrice, row.PriceFloor.AmountAboveMinimumAllowedCatalogPrice);
        Assert.DoesNotContain(
            row.ScenarioResult.Scenarios,
            s => s.SimulatedCatalogPrice == row.PriceFloor.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void B4B_e_autoridade_do_status()
    {
        var snapshot = Compose(Happy());
        var row = Assert.Single(snapshot.Rows);
        var direct = InventoryCommercialScenarioEngine.Evaluate(new InventoryCommercialScenarioInput
        {
            Eligibility = row.Eligibility,
            Facts = row.Facts,
            PolicyResolution = snapshot.PolicyResolution,
            Floor = row.PriceFloor,
            Turnover = row.Turnover,
            Projection = row.Projection,
            Attention = row.Attention,
        });
        Assert.Equal(direct.Status, row.ScenarioResult.Status);
        Assert.Equal(direct.Scenarios.Count, row.ScenarioResult.Scenarios.Count);
        Assert.Equal(
            direct.Scenarios[0].SimulatedCatalogPrice,
            row.ScenarioResult.Scenarios[0].SimulatedCatalogPrice);
    }

    [Fact]
    public void Missing_projection_ReviewData()
    {
        var row = Assert.Single(Compose(WithoutProjection(Happy())).Rows);
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, row.ScenarioResult.Status);
        Assert.Equal(InventoryCommercialEligibilityReason.ProjectionMissing, row.Eligibility.PrimaryReason);
        Assert.Null(row.Projection);
        Assert.Empty(row.ScenarioResult.Scenarios);
        Assert.NotEqual(0d, row.ScenarioResult.AttentionQuantity);
    }

    [Fact]
    public void Duplicate_projection_nao_escolhe_silenciosamente()
    {
        var baseInput = Happy();
        var proj = Project(1);
        var snapshot = Compose(baseInput with
        {
            Projection = null,
            ProjectionRows = [proj, new InventoryProjectedProduct { ProductId = 1 }],
        });
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, row.ScenarioResult.Status);
        Assert.Equal(InventoryCommercialEligibilityReason.DuplicateProjection, row.Eligibility.PrimaryReason);
        Assert.Null(row.Projection);
        Assert.Empty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Missing_attention_nao_vira_Reliable()
    {
        var snapshot = Compose(Happy() with { Attention = new InventoryAttentionSnapshot() });
        var row = Assert.Single(snapshot.Rows);
        Assert.NotEqual(InventoryAttentionConfidence.Reliable, row.Attention.Confidence);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, row.Attention.Confidence);
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, row.ScenarioResult.Status);
        Assert.Empty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Duplicate_attention_nao_escolhe()
    {
        var a = Attention(1, InventoryAttentionConfidence.Reliable, 8);
        var b = Attention(1, InventoryAttentionConfidence.Reliable, 99);
        var snapshot = Compose(Happy() with
        {
            Attention = new InventoryAttentionSnapshot { Results = [a, b] },
        });
        var row = Assert.Single(snapshot.Rows);
        Assert.NotEqual(99, row.Attention.ProjectedExcessQuantity);
        Assert.NotEqual(8, row.Attention.ProjectedExcessQuantity);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, row.Attention.Confidence);
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, row.ScenarioResult.Status);
    }

    [Fact]
    public void Missing_eligibility_ReviewData()
    {
        var snapshot = Compose(Happy() with { Eligibility = [] });
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, row.Eligibility.Kind);
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, row.ScenarioResult.Status);
        Assert.NotEqual(InventoryCommercialEligibilityKind.CommercialCandidate, row.Eligibility.Kind);
        Assert.Empty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Duplicate_eligibility_nao_escolhe_first_last()
    {
        var first = Eligibility(1, InventoryCommercialEligibilityKind.CommercialCandidate,
            InventoryCommercialEligibilityReason.ProjectedExcess);
        var last = Eligibility(1, InventoryCommercialEligibilityKind.CommercialCandidate,
            InventoryCommercialEligibilityReason.Idle);
        var snapshot = Compose(Happy() with { Eligibility = [first, last] });
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialEligibilityKind.ReviewData, row.Eligibility.Kind);
        Assert.NotEqual(InventoryCommercialEligibilityReason.Idle, row.Eligibility.PrimaryReason);
        Assert.NotEqual(InventoryCommercialEligibilityReason.ProjectedExcess, row.Eligibility.PrimaryReason);
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, row.ScenarioResult.Status);
    }

    [Fact]
    public void Missing_facts_nao_inventa_preco_zero()
    {
        var snapshot = Compose(Happy() with { Facts = new InventoryCommercialFactsSnapshot() });
        var row = Assert.Single(snapshot.Rows);
        Assert.False(row.Facts.ProductFound);
        Assert.Null(row.Facts.CatalogSalePrice);
        Assert.Null(row.Facts.CurrentAverageCost);
        Assert.Contains(InventoryCommercialFactsReason.MissingProduct, row.Facts.LimitationReasons);
        Assert.Equal(InventoryCommercialScenarioStatus.FinancialDataUnavailable, row.ScenarioResult.Status);
        Assert.Empty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Duplicate_facts_nao_escolhe()
    {
        var a = Facts(1, 10, 6);
        var b = Facts(1, 99, 1);
        var snapshot = Compose(Happy() with
        {
            Facts = new InventoryCommercialFactsSnapshot { Rows = [a, b] },
        });
        var row = Assert.Single(snapshot.Rows);
        Assert.False(row.Facts.ProductFound);
        Assert.NotEqual(99, row.Facts.CatalogSalePrice);
        Assert.NotEqual(10, row.Facts.CatalogSalePrice);
        Assert.Null(row.Facts.CatalogSalePrice);
        Assert.Empty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Extra_projection_ignorada()
    {
        var snapshot = Compose(WithExtra(Happy(), extraProjection: 99));
        Assert.Single(snapshot.Rows);
        Assert.Equal(1, snapshot.Rows[0].ProductId);
        Assert.DoesNotContain(snapshot.Rows, r => r.ProductId == 99);
    }

    [Fact]
    public void Extra_facts_ignorados()
    {
        var snapshot = Compose(WithExtra(Happy(), extraFacts: 88));
        Assert.Single(snapshot.Rows);
        Assert.DoesNotContain(snapshot.Rows, r => r.ProductId == 88);
    }

    [Fact]
    public void Extra_attention_ignorada()
    {
        var snapshot = Compose(WithExtra(Happy(), extraAttention: 77));
        Assert.Single(snapshot.Rows);
        Assert.DoesNotContain(snapshot.Rows, r => r.ProductId == 77);
    }

    [Fact]
    public void Extra_eligibility_ignorada()
    {
        var snapshot = Compose(WithExtra(Happy(), extraEligibility: 66));
        Assert.Single(snapshot.Rows);
        Assert.DoesNotContain(snapshot.Rows, r => r.ProductId == 66);
    }

    [Fact]
    public void Produto_70C_nunca_desaparece()
    {
        var input = Happy(ids: [1, 2]);
        input = input with
        {
            Facts = FactsSnap(Facts(1, 10, 6)),
            Attention = AttentionSnap(Attention(1)),
            Eligibility = [Eligibility(1)],
            Projection = ProjectionSnap(Project(1)),
        };
        var snapshot = Compose(input);
        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal(new[] { 1, 2 }, snapshot.Rows.Select(r => r.ProductId));
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, snapshot.Rows[1].ScenarioResult.Status);
    }

    [Fact]
    public void ProductId_final_e_70C()
    {
        var facts = Facts(1, 10, 6);
        facts = new InventoryCommercialFacts
        {
            ProductId = 50,
            ProductFound = facts.ProductFound,
            CatalogSalePrice = facts.CatalogSalePrice,
            CurrentAverageCost = facts.CurrentAverageCost,
            PriceQuality = facts.PriceQuality,
            CostQuality = facts.CostQuality,
            CanEvaluateFinancialScenario = facts.CanEvaluateFinancialScenario,
            AllowsSale = facts.AllowsSale,
            LimitationReasons = facts.LimitationReasons,
        };
        var snapshot = Compose(Happy() with
        {
            Facts = FactsSnap(facts),
        });
        Assert.Equal(1, snapshot.Rows[0].ProductId);
        Assert.Equal(1, snapshot.Rows[0].Turnover.ProductId);
        Assert.NotEqual(50, snapshot.Rows[0].ProductId);
    }

    [Fact]
    public void Nao_inventa_zero_nem_Reliable_em_faltantes()
    {
        var snapshot = Compose(new InventoryCommercialScenarioComposeInput
        {
            Intelligence = new InventoryIntelligenceSnapshot { Rows = [Turnover(4)] },
            PolicyResolution = AvailablePolicy(20m),
        });
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(4, row.ProductId);
        Assert.Null(row.Facts.CatalogSalePrice);
        Assert.NotEqual(0, row.Facts.CatalogSalePrice);
        Assert.Null(row.Projection);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, row.Attention.Confidence);
        Assert.NotEqual(InventoryAttentionConfidence.Reliable, row.ScenarioResult.Confidence);
        Assert.Empty(row.ScenarioResult.Scenarios);
    }

    [Fact]
    public void Nao_soma_horizontes()
    {
        var snapshot = Compose(Happy(excess: 10, surplus: 3));
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(InventoryCommercialScenarioThesis.ExpirySurplus, row.ScenarioResult.Thesis);
        Assert.Equal(3, row.ScenarioResult.AttentionQuantity);
        Assert.NotEqual(13, row.ScenarioResult.AttentionQuantity);
    }

    [Fact]
    public void Politica_convertida_uma_vez()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialScenarioComposer.cs"));
        var idx = source.IndexOf("foreach (var turnover in rows)", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var before = source[..idx];
        var loop = source[idx..];
        Assert.Contains("TryCreatePriceFloorPolicy", before, StringComparison.Ordinal);
        Assert.DoesNotContain("TryCreatePriceFloorPolicy", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("MarginSettingsService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_FirstOrDefault_no_loop()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialScenarioComposer.cs"));
        Assert.DoesNotContain("FirstOrDefault", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SingleOrDefault", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Determinismo()
    {
        var input = Happy(ids: [5, 1, 9]);
        var a = Compose(input);
        var b = Compose(input);
        Assert.Equal(a.Rows.Select(r => r.ProductId), b.Rows.Select(r => r.ProductId));
        Assert.Equal(a.Rows[0].ScenarioResult.Status, b.Rows[0].ScenarioResult.Status);
        Assert.Equal(
            a.Rows[0].ScenarioResult.Scenarios[0].SimulatedCatalogPrice,
            b.Rows[0].ScenarioResult.Scenarios[0].SimulatedCatalogPrice);
        Assert.Equal(a.QueryCount, b.QueryCount);
    }

    [Fact]
    public void Pureza_sem_IO()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialScenarioComposer.cs"));
        Assert.DoesNotContain("DatabaseService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppSettingsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialMarginSettingsService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreNetwork", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentCulture", source, StringComparison.Ordinal);
        Assert.DoesNotContain("preco_promocional", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SaleFromCostAndMargin", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductCompositionService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Lista_readonly()
    {
        var snapshot = Compose(Happy(ids: [1, 2]));
        Assert.IsAssignableFrom<IReadOnlyList<InventoryCommercialScenarioRow>>(snapshot.Rows);
        Assert.IsAssignableFrom<IReadOnlyDictionary<int, InventoryCommercialScenarioRow>>(snapshot.ByProductId);
        Assert.Equal(2, snapshot.Rows.Count);
    }

    [Fact]
    public void Policy_resolution_preservada()
    {
        var policy = AvailablePolicy(18m);
        var snapshot = Compose(Happy(policy: policy));
        Assert.Same(policy, snapshot.PolicyResolution);
        Assert.Equal(18m, snapshot.PolicyResolution.EffectiveMinimumGrossMarginPercent);
        Assert.Equal(InventoryCommercialMarginPolicySource.Global, snapshot.PolicyResolution.Source);
    }

    [Fact]
    public void Facts_preservados()
    {
        var facts = Facts(1, 10, 6);
        var snapshot = Compose(Happy() with { Facts = FactsSnap(facts) });
        Assert.Same(facts, snapshot.Rows[0].Facts);
        Assert.Equal(10, snapshot.Rows[0].Facts.CatalogSalePrice);
    }

    [Fact]
    public void Price_floor_preservado_na_row()
    {
        var snapshot = Compose(Happy());
        var row = snapshot.Rows[0];
        Assert.NotNull(row.PriceFloor);
        Assert.Equal(row.PriceFloor.MinimumAllowedCatalogPrice, row.ScenarioResult.MinimumAllowedCatalogPrice);
        Assert.Equal(row.Facts.CatalogSalePrice, row.PriceFloor.CatalogSalePrice);
    }

    [Fact]
    public void Sobrecarga_e_DTO_equivalentes()
    {
        var input = Happy();
        var viaDto = Compose(input);
        var viaArgs = InventoryCommercialScenarioComposer.Compose(
            input.Intelligence, input.Projection, input.Attention,
            input.Eligibility, input.Facts, input.PolicyResolution);
        Assert.Equal(viaDto.Rows[0].ScenarioResult.Status, viaArgs.Rows[0].ScenarioResult.Status);
        Assert.Equal(
            viaDto.Rows[0].PriceFloor.MinimumAllowedCatalogPrice,
            viaArgs.Rows[0].PriceFloor.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Input_nulo_nao_crasha()
    {
        var snapshot = InventoryCommercialScenarioComposer.Compose((InventoryCommercialScenarioComposeInput?)null);
        Assert.Empty(snapshot.Rows);
        Assert.Equal(9, snapshot.QueryCount);
        Assert.Equal(InventoryCommercialMarginPolicyResolutionStatus.Missing, snapshot.PolicyResolution.Status);
    }

    [Fact]
    public void Expired_vence_projection_ausente()
    {
        var input = WithoutProjection(Happy(
            kind: InventoryCommercialEligibilityKind.NoCommercialRecommendation,
            reason: InventoryCommercialEligibilityReason.Expired));
        var row = Assert.Single(Compose(input).Rows);
        Assert.Equal(InventoryCommercialScenarioStatus.Expired, row.ScenarioResult.Status);
        Assert.Equal(InventoryCommercialEligibilityReason.Expired, row.Eligibility.PrimaryReason);
    }

    static InventoryCommercialScenarioSnapshot Compose(InventoryCommercialScenarioComposeInput input) =>
        InventoryCommercialScenarioComposer.Compose(input);

    static InventoryCommercialScenarioComposeInput Happy(
        int[]? ids = null,
        InventoryCommercialEligibilityKind kind = InventoryCommercialEligibilityKind.CommercialCandidate,
        InventoryCommercialEligibilityReason reason = InventoryCommercialEligibilityReason.ProjectedExcess,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable,
        double? excess = 8,
        double? surplus = null,
        double sale = 10,
        double cost = 6,
        InventoryCommercialMarginPolicyResolution? policy = null)
    {
        ids ??= [1];
        var turnovers = ids.Select(Turnover).ToList();
        var projections = ids.Select(Project).ToList();
        var attentions = ids.Select(id => Attention(id, confidence, excess, surplus, reason)).ToList();
        var eligibilities = ids.Select(id => Eligibility(id, kind, reason, confidence)).ToList();
        var facts = ids.Select(id => Facts(id, sale, cost)).ToList();
        var intelligence = new InventoryIntelligenceSnapshot { QueryCount = 6, Rows = turnovers };
        return new InventoryCommercialScenarioComposeInput
        {
            Intelligence = intelligence,
            Projection = new InventoryProjectionSnapshot
            {
                QueryCount = 7,
                Intelligence = intelligence,
                ByProductId = projections.ToDictionary(p => p.ProductId),
            },
            Attention = new InventoryAttentionSnapshot
            {
                QueryCount = 7,
                Results = attentions,
                ByProductId = attentions.ToDictionary(a => a.ProductId),
            },
            Eligibility = eligibilities,
            Facts = new InventoryCommercialFactsSnapshot
            {
                QueryCount = 1,
                RequestedProductIds = ids,
                Rows = facts,
                ByProductId = facts.ToDictionary(f => f.ProductId),
            },
            PolicyResolution = policy ?? AvailablePolicy(20m),
        };
    }

    static InventoryCommercialScenarioComposeInput WithoutProjection(
        InventoryCommercialScenarioComposeInput input) =>
        input with
        {
            Projection = new InventoryProjectionSnapshot
            {
                Intelligence = input.Intelligence ?? new(),
                ByProductId = new Dictionary<int, InventoryProjectedProduct>(),
            },
            ProjectionRows = null,
        };

    static InventoryCommercialScenarioComposeInput WithExtra(
        InventoryCommercialScenarioComposeInput input,
        int extraProjection = 0,
        int extraFacts = 0,
        int extraAttention = 0,
        int extraEligibility = 0)
    {
        var projection = input.Projection ?? new InventoryProjectionSnapshot();
        var byProduct = new Dictionary<int, InventoryProjectedProduct>(
            projection.ByProductId ?? new Dictionary<int, InventoryProjectedProduct>());
        if (extraProjection > 0)
            byProduct[extraProjection] = Project(extraProjection);

        var factsRows = (input.Facts?.Rows ?? []).ToList();
        if (extraFacts > 0)
            factsRows.Add(Facts(extraFacts, 10, 6));

        var attentionRows = (input.Attention?.Results ?? []).ToList();
        if (extraAttention > 0)
            attentionRows.Add(Attention(extraAttention));

        var eligibility = (input.Eligibility ?? []).ToList();
        if (extraEligibility > 0)
            eligibility.Add(Eligibility(extraEligibility));

        return input with
        {
            Projection = new InventoryProjectionSnapshot
            {
                Intelligence = projection.Intelligence,
                ByProductId = byProduct,
            },
            Facts = new InventoryCommercialFactsSnapshot
            {
                Rows = factsRows,
                ByProductId = factsRows.GroupBy(f => f.ProductId).ToDictionary(g => g.Key, g => g.First()),
            },
            Attention = new InventoryAttentionSnapshot { Results = attentionRows },
            Eligibility = eligibility,
        };
    }

    static ProductTurnoverRow Turnover(int id) =>
        new()
        {
            ProductId = id,
            Name = "P" + id,
            TotalStock = 30,
            Stock = 30,
        };

    static InventoryProjectedProduct Project(int id) =>
        new() { ProductId = id };

    static InventoryAttentionResult Attention(
        int id,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable,
        double? excess = 8,
        double? surplus = null,
        InventoryCommercialEligibilityReason reason = InventoryCommercialEligibilityReason.ProjectedExcess) =>
        new()
        {
            ProductId = id,
            Confidence = confidence,
            ProjectedExcessQuantity = excess,
            ProjectedExpirySurplusQuantity = surplus,
            PrimaryReason = reason == InventoryCommercialEligibilityReason.ProjectedExpirySurplus
                ? InventoryAttentionReason.SurplusAtExpiry
                : InventoryAttentionReason.ProjectedExcess30,
        };

    static InventoryCommercialEligibilityResult Eligibility(
        int id,
        InventoryCommercialEligibilityKind kind = InventoryCommercialEligibilityKind.CommercialCandidate,
        InventoryCommercialEligibilityReason reason = InventoryCommercialEligibilityReason.ProjectedExcess,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable) =>
        new()
        {
            ProductId = id,
            Kind = kind,
            PrimaryReason = reason,
            SecondaryReasons = [],
            Confidence = confidence,
        };

    static InventoryCommercialFacts Facts(int id, double sale, double cost) =>
        InventoryCommercialFactsEngine.Classify(new InventoryCommercialFactsInput
        {
            ProductId = id,
            ProductFound = true,
            CatalogSalePrice = sale,
            CurrentAverageCost = cost,
            AllowsSale = true,
        });

    static InventoryAttentionSnapshot AttentionSnap(params InventoryAttentionResult[] rows) =>
        new() { Results = rows, ByProductId = rows.ToDictionary(r => r.ProductId) };

    static InventoryCommercialFactsSnapshot FactsSnap(params InventoryCommercialFacts[] rows) =>
        new()
        {
            Rows = rows,
            ByProductId = rows.ToDictionary(r => r.ProductId),
        };

    static InventoryProjectionSnapshot ProjectionSnap(params InventoryProjectedProduct[] rows) =>
        new() { ByProductId = rows.ToDictionary(r => r.ProductId) };

    static InventoryCommercialMarginPolicyResolution AvailablePolicy(decimal percent) =>
        InventoryCommercialMarginPolicyResolver.Resolve(new InventoryCommercialMarginSetting
        {
            Status = InventoryCommercialMarginSettingStatus.Configured,
            MinimumGrossMarginPercent = percent,
            RawValue = percent.ToString(CultureInfo.InvariantCulture),
        });

    static InventoryCommercialMarginPolicyResolution MissingPolicy() =>
        InventoryCommercialMarginPolicyResolver.Resolve(new InventoryCommercialMarginSetting
        {
            Status = InventoryCommercialMarginSettingStatus.Missing,
            Reasons = [InventoryCommercialMarginSettingReason.Missing],
        });

    static InventoryCommercialMarginPolicyResolution InvalidPolicy() =>
        InventoryCommercialMarginPolicyResolver.Resolve(new InventoryCommercialMarginSetting
        {
            Status = InventoryCommercialMarginSettingStatus.Invalid,
            RawValue = "100",
            Reasons = [InventoryCommercialMarginSettingReason.Invalid],
        });

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
