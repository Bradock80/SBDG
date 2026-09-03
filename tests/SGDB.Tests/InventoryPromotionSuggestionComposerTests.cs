using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B5C — composer puro. Sem SQL, UI, PDV, promoção ou recálculo B4/B5B.
/// </summary>
public class InventoryPromotionSuggestionComposerTests
{
    [Fact]
    public void QueryCount_do_composer_e_zero() =>
        Assert.Equal(0, InventoryPromotionSuggestionComposer.ExpectedQueryCount);

    [Fact]
    public void Pipeline_herdado_e_9()
    {
        Assert.Equal(9, InventoryPromotionSuggestionComposer.ExpectedPipelineQueryCount);
        Assert.Equal(
            InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount,
            InventoryPromotionSuggestionComposer.ExpectedPipelineQueryCount);
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryCommercialScenarioComposer.ExpectedQueryCount
            + InventoryPromotionSuggestionEngine.ExpectedQueryCount
            + InventoryPromotionSuggestionComposer.ExpectedQueryCount);
    }

    [Fact]
    public void Snapshot_QueryCount_e_9() =>
        Assert.Equal(9, Compose(Happy()).QueryCount);

    [Fact]
    public void Populacao_vem_de_70C()
    {
        var snapshot = Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1, 2),
            Scenarios = ScenarioSnap(
                ScenarioRow(1, Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light())),
                ScenarioRow(99, Available(99, InventoryCommercialScenarioThesis.ExpirySurplus, 2, Light()))),
        });
        Assert.Equal(new[] { 1, 2 }, snapshot.Rows.Select(r => r.ProductId));
        Assert.DoesNotContain(snapshot.Rows, r => r.ProductId == 99);
    }

    [Fact]
    public void Um_produto_70C_gera_uma_row()
    {
        var snapshot = Compose(Happy());
        Assert.Single(snapshot.Rows);
        Assert.Equal(1, snapshot.Rows[0].ProductId);
        Assert.Equal(1, snapshot.Rows[0].Suggestion.ProductId);
    }

    [Fact]
    public void Dez_produtos_70C_geram_dez_rows()
    {
        var ids = Enumerable.Range(1, 10).ToArray();
        var snapshot = Compose(Happy(ids));
        Assert.Equal(10, snapshot.Rows.Count);
        Assert.Equal(ids, snapshot.Rows.Select(r => r.ProductId));
        Assert.Equal(10, snapshot.ByProductId.Count);
    }

    [Fact]
    public void Ordem_70C_preservada()
    {
        var snapshot = Compose(Happy([10, 2, 7]));
        Assert.Equal(new[] { 10, 2, 7 }, snapshot.Rows.Select(r => r.ProductId).ToArray());
        Assert.NotEqual(
            snapshot.Rows.Select(r => r.ProductId).OrderBy(id => id),
            snapshot.Rows.Select(r => r.ProductId));
    }

    [Fact]
    public void Available_Excess_e_Suggested()
    {
        var light = Light();
        var moderate = Moderate();
        var snapshot = Compose(Happy(
            result: Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, light, moderate)));
        var suggestion = Assert.Single(snapshot.Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess, suggestion.PrimaryReason);
        Assert.Equal(2, suggestion.Scenarios.Count);
        Assert.Same(light, suggestion.Scenarios[0]);
        Assert.Same(moderate, suggestion.Scenarios[1]);
    }

    [Fact]
    public void Available_Expiry_e_Suggested()
    {
        var snapshot = Compose(Happy(
            result: Available(1, InventoryCommercialScenarioThesis.ExpirySurplus, 3.5, Light(), Moderate())));
        var suggestion = Assert.Single(snapshot.Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus, suggestion.PrimaryReason);
        Assert.Equal(3.5, suggestion.AttentionQuantity);
    }

    [Fact]
    public void Expired_preservado()
    {
        var snapshot = Compose(Happy(result: new InventoryCommercialScenarioResult
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.Expired,
            PrimaryReason = InventoryCommercialScenarioReason.Expired,
            Confidence = InventoryAttentionConfidence.Reliable,
            AttentionQuantity = 4,
            Scenarios = [Light()],
        }));
        var suggestion = Assert.Single(snapshot.Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.Expired, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.RemoveExpired, suggestion.Action);
        Assert.Equal(InventoryPromotionSuggestionReason.Expired, suggestion.PrimaryReason);
        Assert.Empty(suggestion.Scenarios);
        Assert.Equal(4, suggestion.AttentionQuantity);
    }

    [Fact]
    public void ExpiresToday_preservado()
    {
        var snapshot = Compose(Happy(result: Monitor(
            1, InventoryCommercialScenarioThesis.None, InventoryCommercialScenarioReason.ExpiresToday, Light())));
        var suggestion = Assert.Single(snapshot.Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.PrioritizeExposure, suggestion.Action);
        Assert.Equal(InventoryPromotionSuggestionReason.ExpiresToday, suggestion.PrimaryReason);
        Assert.Empty(suggestion.Scenarios);
    }

    [Fact]
    public void Idle_preservado()
    {
        var snapshot = Compose(Happy(result: Monitor(
            1, InventoryCommercialScenarioThesis.Idle, InventoryCommercialScenarioReason.Idle, Light())));
        var suggestion = Assert.Single(snapshot.Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionReason.IdleOnly, suggestion.PrimaryReason);
        Assert.Empty(suggestion.Scenarios);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
    }

    [Fact]
    public void HighCoverage_preservado()
    {
        var snapshot = Compose(Happy(result: Monitor(
            1,
            InventoryCommercialScenarioThesis.HighCoverage,
            InventoryCommercialScenarioReason.HighCoverageMonitoring)));
        var suggestion = Assert.Single(snapshot.Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionReason.HighCoverageOnly, suggestion.PrimaryReason);
        Assert.Empty(suggestion.Scenarios);
    }

    [Fact]
    public void Limited_nao_promove()
    {
        var b4 = Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light());
        b4 = Clone(b4, confidence: InventoryAttentionConfidence.Limited);
        var suggestion = Assert.Single(Compose(Happy(result: b4)).Rows).Suggestion;
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
        Assert.Empty(suggestion.Scenarios);
    }

    [Fact]
    public void ReviewData_nao_promove()
    {
        var suggestion = Assert.Single(Compose(Happy(result: new InventoryCommercialScenarioResult
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.ReviewData,
            PrimaryReason = InventoryCommercialScenarioReason.LocationLimitation,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        })).Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.ReviewData, suggestion.Status);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
        Assert.Empty(suggestion.Scenarios);
    }

    [Fact]
    public void PolicyMissing_preservado()
    {
        var suggestion = Assert.Single(Compose(Happy(result: new InventoryCommercialScenarioResult
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.PolicyMissing,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyMissing,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        })).Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.PolicyMissing, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionReason.PolicyMissing, suggestion.PrimaryReason);
        Assert.Empty(suggestion.Scenarios);
        Assert.Empty(suggestion.Warnings);
    }

    [Fact]
    public void PolicyInvalid_preservado()
    {
        var suggestion = Assert.Single(Compose(Happy(result: new InventoryCommercialScenarioResult
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.PolicyInvalid,
            PrimaryReason = InventoryCommercialScenarioReason.PolicyInvalid,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        })).Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.PolicyInvalid, suggestion.Status);
        Assert.Empty(suggestion.Scenarios);
    }

    [Fact]
    public void FinancialDataUnavailable_preservado()
    {
        var suggestion = Assert.Single(Compose(Happy(result: new InventoryCommercialScenarioResult
        {
            ProductId = 1,
            Status = InventoryCommercialScenarioStatus.FinancialDataUnavailable,
            PrimaryReason = InventoryCommercialScenarioReason.UnknownCost,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = [Light()],
        })).Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.FinancialDataUnavailable, suggestion.Status);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
        Assert.Empty(suggestion.Scenarios);
    }

    [Fact]
    public void Policy_0_warning_preservado()
    {
        var b4 = Clone(
            Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate()),
            minMargin: 0);
        var suggestion = Assert.Single(Compose(Happy(result: b4)).Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
        Assert.Contains(
            InventoryPromotionSuggestionWarning.MinimumMarginPolicyAllowsAtCost, suggestion.Warnings);
        Assert.Equal(2, suggestion.Scenarios.Count);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.PolicyMissing, suggestion.Status);
    }

    [Fact]
    public void Wholesale_warning_preservado()
    {
        var light = Light(9.40);
        var snapshot = Compose(Happy(
            result: Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, light, Moderate()),
            wholesale: true));
        var suggestion = Assert.Single(snapshot.Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
        Assert.Contains(
            InventoryPromotionSuggestionWarning.WholesalePricingMayDiffer, suggestion.Warnings);
        Assert.Same(light, suggestion.Scenarios[0]);
        Assert.Equal(9.40, suggestion.Scenarios[0].SimulatedCatalogPrice);
    }

    [Fact]
    public void Wholesale_via_LimitationReason_sem_recalcular_preco()
    {
        var row = ScenarioRow(
            1,
            Available(1, InventoryCommercialScenarioThesis.ExpirySurplus, 2, Light()),
            wholesale: false);
        row = new InventoryCommercialScenarioRow
        {
            ProductId = row.ProductId,
            Attention = row.Attention,
            Facts = new InventoryCommercialFacts
            {
                ProductId = 1,
                HasWholesalePricing = false,
                LimitationReasons = [InventoryCommercialFactsReason.WholesalePricingConfigured],
            },
            ScenarioResult = row.ScenarioResult,
        };
        var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(row),
        }).Rows).Suggestion;
        Assert.Contains(
            InventoryPromotionSuggestionWarning.WholesalePricingMayDiffer, suggestion.Warnings);
    }

    [Fact]
    public void Light_e_Moderate_preservados()
    {
        var light = Light(9.40);
        var moderate = Moderate(8.80);
        var suggestion = Assert.Single(Compose(Happy(
            result: Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, light, moderate))).Rows)
            .Suggestion;
        Assert.Equal(2, suggestion.Scenarios.Count);
        Assert.Equal(InventoryCommercialScenarioKind.Light, suggestion.Scenarios[0].Kind);
        Assert.Equal(InventoryCommercialScenarioKind.Moderate, suggestion.Scenarios[1].Kind);
        Assert.Equal(9.40, suggestion.Scenarios[0].SimulatedCatalogPrice);
        Assert.Equal(8.80, suggestion.Scenarios[1].SimulatedCatalogPrice);
        Assert.Same(light, suggestion.Scenarios[0]);
        Assert.Same(moderate, suggestion.Scenarios[1]);
    }

    [Fact]
    public void Um_cenario_preservado()
    {
        var light = Light();
        var suggestion = Assert.Single(Compose(Happy(
            result: Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, light))).Rows)
            .Suggestion;
        Assert.Same(light, Assert.Single(suggestion.Scenarios));
    }

    [Fact]
    public void AttentionQuantity_decimal_preservada()
    {
        var suggestion = Assert.Single(Compose(Happy(
            result: Available(1, InventoryCommercialScenarioThesis.ExpirySurplus, 3.75, Light()))).Rows)
            .Suggestion;
        Assert.Equal(3.75, suggestion.AttentionQuantity);
        Assert.Equal(InventoryCommercialAttentionQuantitySource.ExpirySurplus, suggestion.AttentionQuantitySource);
    }

    [Fact]
    public void Source_preservada()
    {
        var b4 = Available(
            1,
            InventoryCommercialScenarioThesis.ProjectedExcess30,
            8,
            InventoryCommercialAttentionQuantitySource.ProjectedExcess30,
            Light());
        var suggestion = Assert.Single(Compose(Happy(result: b4)).Rows).Suggestion;
        Assert.Equal(InventoryCommercialAttentionQuantitySource.ProjectedExcess30, suggestion.AttentionQuantitySource);
    }

    [Fact]
    public void Priority_preservada()
    {
        var suggestion = Assert.Single(Compose(Happy(priority: InventoryAttentionPriority.Critical)).Rows)
            .Suggestion;
        Assert.Equal(InventoryAttentionPriority.Critical, suggestion.AttentionPriority);
    }

    [Fact]
    public void Priority_ausente_nao_inventa_Normal()
    {
        var row = ScenarioRow(
            1,
            Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light()),
            priority: InventoryAttentionPriority.High);
        row = new InventoryCommercialScenarioRow
        {
            ProductId = 1,
            Attention = new InventoryAttentionResult { ProductId = 0, Priority = InventoryAttentionPriority.Normal },
            Facts = row.Facts,
            ScenarioResult = row.ScenarioResult,
        };
        var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(row),
        }).Rows).Suggestion;
        Assert.Null(suggestion.AttentionPriority);
        Assert.NotEqual(InventoryAttentionPriority.Normal, suggestion.AttentionPriority);
    }

    [Fact]
    public void B4_ausente_ReviewData_Unavailable()
    {
        var snapshot = Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(),
        });
        var suggestion = Assert.Single(snapshot.Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.ReviewData, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ReviewData, suggestion.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.ReviewInformation, suggestion.Objective);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, suggestion.Confidence);
        Assert.Equal(InventoryPromotionSuggestionReason.ScenarioMissing, suggestion.PrimaryReason);
        Assert.Null(suggestion.AttentionQuantity);
        Assert.Empty(suggestion.Scenarios);
        Assert.Empty(suggestion.Warnings);
    }

    [Fact]
    public void B4_ausente_nunca_Suggested()
    {
        var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = null,
        }).Rows).Suggestion;
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
        Assert.NotEqual(InventoryPromotionSuggestionAction.ConsiderPromotion, suggestion.Action);
        Assert.NotEqual(InventoryPromotionSuggestionReason.InvalidInput, suggestion.PrimaryReason);
        Assert.NotEqual(InventoryPromotionSuggestionReason.IdleOnly, suggestion.PrimaryReason);
        Assert.NotEqual(InventoryPromotionSuggestionReason.HighCoverageOnly, suggestion.PrimaryReason);
        Assert.NotEqual(InventoryPromotionSuggestionReason.PolicyMissing, suggestion.PrimaryReason);
        Assert.NotEqual(InventoryPromotionSuggestionReason.FinancialDataUnavailable, suggestion.PrimaryReason);
    }

    [Fact]
    public void B4_duplicado_ReviewData_Unavailable()
    {
        var available = ScenarioRow(
            1,
            Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate()),
            priority: InventoryAttentionPriority.Critical,
            wholesale: true);
        var idle = ScenarioRow(
            1,
            Monitor(1, InventoryCommercialScenarioThesis.Idle, InventoryCommercialScenarioReason.Idle));
        var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(available, idle),
        }).Rows).Suggestion;
        Assert.Equal(InventoryPromotionSuggestionStatus.ReviewData, suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionAction.ReviewData, suggestion.Action);
        Assert.Equal(InventoryPromotionSuggestionObjective.ReviewInformation, suggestion.Objective);
        Assert.Equal(InventoryAttentionConfidence.Unavailable, suggestion.Confidence);
        Assert.Equal(InventoryPromotionSuggestionReason.DuplicateScenario, suggestion.PrimaryReason);
        Assert.Null(suggestion.AttentionPriority);
        Assert.Null(suggestion.AttentionQuantity);
        Assert.Empty(suggestion.Warnings);
    }

    [Fact]
    public void Duplicado_nunca_escolhe_Available_arbitrariamente()
    {
        var first = ScenarioRow(
            1,
            Available(1, InventoryCommercialScenarioThesis.ExpirySurplus, 2, Light(), Moderate()));
        var second = ScenarioRow(
            1,
            Monitor(1, InventoryCommercialScenarioThesis.Idle, InventoryCommercialScenarioReason.Idle));
        foreach (var rows in new[]
                 {
                     new[] { first, second },
                     new[] { second, first },
                 })
        {
            var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
            {
                Intelligence = Intelligence(1),
                Scenarios = ScenarioSnap(rows),
            }).Rows).Suggestion;
            Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
            Assert.NotEqual(InventoryPromotionSuggestionStatus.MonitorOnly, suggestion.Status);
            Assert.Equal(InventoryPromotionSuggestionReason.DuplicateScenario, suggestion.PrimaryReason);
        }
    }

    [Fact]
    public void Duplicado_remove_cenarios_comerciais()
    {
        var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(
                ScenarioRow(1, Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate())),
                ScenarioRow(1, Available(1, InventoryCommercialScenarioThesis.ExpirySurplus, 2, Light()))),
        }).Rows).Suggestion;
        Assert.Empty(suggestion.Scenarios);
        Assert.Equal(InventoryCommercialScenarioThesis.None, suggestion.Thesis);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, suggestion.Status);
    }

    [Fact]
    public void Duplicado_detectado_mesmo_se_ByProductId_esconder()
    {
        var available = ScenarioRow(
            1, Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light()));
        var extra = ScenarioRow(
            1, Monitor(1, InventoryCommercialScenarioThesis.Idle, InventoryCommercialScenarioReason.Idle));
        var snapshot = Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = new InventoryCommercialScenarioSnapshot
            {
                Rows = [available, extra],
                ByProductId = new Dictionary<int, InventoryCommercialScenarioRow> { [1] = available },
            },
        });
        Assert.Equal(
            InventoryPromotionSuggestionReason.DuplicateScenario,
            Assert.Single(snapshot.Rows).Suggestion.PrimaryReason);
        Assert.NotEqual(InventoryPromotionSuggestionStatus.Suggested, snapshot.Rows[0].Suggestion.Status);
    }

    [Fact]
    public void Extra_B4_ignorado()
    {
        var snapshot = Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(
                ScenarioRow(1, Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light())),
                ScenarioRow(99, Available(99, InventoryCommercialScenarioThesis.ExpirySurplus, 2, Light()))),
        });
        Assert.Single(snapshot.Rows);
        Assert.Equal(1, snapshot.Rows[0].ProductId);
        Assert.False(snapshot.ByProductId.ContainsKey(99));
    }

    [Fact]
    public void ProductId_correto()
    {
        var b4 = Available(77, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light());
        var snapshot = Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(5),
            Scenarios = ScenarioSnap(ScenarioRow(5, b4)),
        });
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(5, row.ProductId);
        Assert.Equal(5, row.Suggestion.ProductId);
        Assert.Equal(5, snapshot.ByProductId[5].ProductId);
    }

    [Fact]
    public void ByProductId_O1_contem_todos_70C()
    {
        var snapshot = Compose(Happy([4, 1, 9]));
        Assert.Equal(3, snapshot.ByProductId.Count);
        Assert.True(snapshot.ByProductId.TryGetValue(4, out var row4));
        Assert.True(snapshot.ByProductId.TryGetValue(1, out var row1));
        Assert.True(snapshot.ByProductId.TryGetValue(9, out var row9));
        Assert.Same(snapshot.Rows[0], row4);
        Assert.Same(snapshot.Rows[1], row1);
        Assert.Same(snapshot.Rows[2], row9);
        Assert.IsAssignableFrom<IReadOnlyDictionary<int, InventoryPromotionSuggestionRow>>(snapshot.ByProductId);
    }

    [Fact]
    public void Ausencia_nao_vira_zero()
    {
        var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(),
        }).Rows).Suggestion;
        Assert.Null(suggestion.AttentionQuantity);
        Assert.NotEqual(0d, suggestion.AttentionQuantity);
        Assert.Empty(suggestion.Scenarios);
        Assert.Equal(InventoryCommercialAttentionQuantitySource.None, suggestion.AttentionQuantitySource);
    }

    [Fact]
    public void Ausencia_nao_vira_Reliable()
    {
        var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(),
        }).Rows).Suggestion;
        Assert.Equal(InventoryAttentionConfidence.Unavailable, suggestion.Confidence);
        Assert.NotEqual(InventoryAttentionConfidence.Reliable, suggestion.Confidence);
    }

    [Fact]
    public void Duplicate_nao_vira_Reliable()
    {
        var suggestion = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(
                ScenarioRow(1, Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light())),
                ScenarioRow(1, Available(1, InventoryCommercialScenarioThesis.ExpirySurplus, 2, Light()))),
        }).Rows).Suggestion;
        Assert.Equal(InventoryAttentionConfidence.Unavailable, suggestion.Confidence);
        Assert.NotEqual(InventoryAttentionConfidence.Reliable, suggestion.Confidence);
    }

    [Fact]
    public void Input_vazio()
    {
        var snapshot = InventoryPromotionSuggestionComposer.Compose((InventoryPromotionSuggestionComposeInput?)null);
        Assert.Empty(snapshot.Rows);
        Assert.Empty(snapshot.ByProductId);
        Assert.Equal(9, snapshot.QueryCount);
    }

    [Fact]
    public void SetentaC_vazio_com_extras_B4_fica_vazio()
    {
        var snapshot = Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(),
            Scenarios = ScenarioSnap(
                ScenarioRow(1, Available(1, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light()))),
        });
        Assert.Empty(snapshot.Rows);
        Assert.Empty(snapshot.ByProductId);
        Assert.Equal(9, snapshot.QueryCount);
    }

    [Fact]
    public void Determinismo()
    {
        var input = Happy([5, 1, 9]);
        var a = Compose(input);
        var b = Compose(input);
        Assert.Equal(a.Rows.Select(r => r.ProductId), b.Rows.Select(r => r.ProductId));
        Assert.Equal(a.Rows[0].Suggestion.Status, b.Rows[0].Suggestion.Status);
        Assert.Equal(a.Rows[0].Suggestion.PrimaryReason, b.Rows[0].Suggestion.PrimaryReason);
        Assert.Equal(
            a.Rows[0].Suggestion.Scenarios[0].SimulatedCatalogPrice,
            b.Rows[0].Suggestion.Scenarios[0].SimulatedCatalogPrice);
        Assert.Equal(a.QueryCount, b.QueryCount);
    }

    [Fact]
    public void Ordem_independente_de_dictionary()
    {
        var intelligence = Intelligence(10, 2, 7);
        var scenarios = ScenarioSnap(
            ScenarioRow(7, Available(7, InventoryCommercialScenarioThesis.ExpirySurplus, 1, Light())),
            ScenarioRow(10, Available(10, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate())),
            ScenarioRow(2, Monitor(2, InventoryCommercialScenarioThesis.Idle, InventoryCommercialScenarioReason.Idle)));
        var snapshot = Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = intelligence,
            Scenarios = scenarios,
        });
        Assert.Equal(new[] { 10, 2, 7 }, snapshot.Rows.Select(r => r.ProductId));
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, snapshot.Rows[0].Suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionStatus.MonitorOnly, snapshot.Rows[1].Suggestion.Status);
        Assert.Equal(InventoryPromotionSuggestionStatus.Suggested, snapshot.Rows[2].Suggestion.Status);
    }

    [Fact]
    public void Sem_FirstOrDefault_no_loop()
    {
        var source = ReadSource("src", "SGDB.App", "Services", "InventoryPromotionSuggestionComposer.cs");
        Assert.DoesNotContain("FirstOrDefault", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SingleOrDefault", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Indexa_B4_uma_vez_antes_do_loop()
    {
        var source = ReadSource("src", "SGDB.App", "Services", "InventoryPromotionSuggestionComposer.cs");
        var idx = source.IndexOf("foreach (var turnover in rows)", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var before = source[..idx];
        var composeEnd = source.IndexOf("static Dictionary<int, InventoryCommercialScenarioRow> IndexScenarios", StringComparison.Ordinal);
        Assert.True(composeEnd > idx);
        var loop = source[idx..composeEnd];
        Assert.Contains("IndexScenarios", before, StringComparison.Ordinal);
        Assert.DoesNotContain("IndexScenarios(", loop, StringComparison.Ordinal);
        Assert.Contains("TryGetValue", loop, StringComparison.Ordinal);
    }

    [Fact]
    public void Pureza_sem_sql_settings_write_pdv_promo_ui_combo_meta_compra()
    {
        var composer = ReadSource("src", "SGDB.App", "Services", "InventoryPromotionSuggestionComposer.cs");
        var model = ReadSource("src", "SGDB.App", "Models", "InventoryPromotionSuggestion.cs");
        foreach (var text in new[] { composer, model })
        {
            Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SELECT ", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE ", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INSERT ", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE ", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AppSettingsService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MarginSettingsService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryCommercialMarginSettingsService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Today", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AppSession", text, StringComparison.Ordinal);
            Assert.DoesNotContain("StoreNetwork", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AuditService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PdvService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PdvCartHelper", text, StringComparison.Ordinal);
            Assert.DoesNotContain("sale_price", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_promocional", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_inicio", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("promo_fim", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("desconto_percent", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ProductCompositionService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Fornecedor", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Pedido sugerido", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("meta mensal", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Random", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryCommercialScenarioEngine.Evaluate", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryCommercialPriceFloorEngine", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ProductPriceCalculator", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ValiditySuggestedAction", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Ação comercial", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Considerar promoção", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Cenário leve", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nao_integrado_na_Window()
    {
        var view = ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml.cs");
        var xaml = ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml");
        var detailCs = ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml.cs");
        var detailXaml = ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml");
        foreach (var text in new[] { view, xaml, detailCs, detailXaml })
        {
            Assert.DoesNotContain("InventoryPromotionSuggestionComposer", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryPromotionSuggestionSnapshot", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryPromotionSuggestionRow", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Sobrecarga_e_DTO_equivalentes()
    {
        var input = Happy();
        var viaDto = Compose(input);
        var viaArgs = InventoryPromotionSuggestionComposer.Compose(input.Intelligence, input.Scenarios);
        Assert.Equal(viaDto.Rows[0].Suggestion.Status, viaArgs.Rows[0].Suggestion.Status);
        Assert.Equal(viaDto.Rows[0].Suggestion.PrimaryReason, viaArgs.Rows[0].Suggestion.PrimaryReason);
        Assert.Same(viaDto.Rows[0].Suggestion.Scenarios[0], viaArgs.Rows[0].Suggestion.Scenarios[0]);
    }

    [Fact]
    public void B5B_e_autoridade_quando_cenario_unico()
    {
        var b4 = Available(1, InventoryCommercialScenarioThesis.ExpirySurplus, 2.25, Light(), Moderate());
        var row = ScenarioRow(1, b4, priority: InventoryAttentionPriority.High, wholesale: true);
        var composed = Assert.Single(Compose(new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(1),
            Scenarios = ScenarioSnap(row),
        }).Rows).Suggestion;
        var direct = InventoryPromotionSuggestionEngine.Evaluate(
            b4, InventoryAttentionPriority.High, hasWholesalePricing: true);
        Assert.Equal(direct.Status, composed.Status);
        Assert.Equal(direct.PrimaryReason, composed.PrimaryReason);
        Assert.Equal(direct.AttentionPriority, composed.AttentionPriority);
        Assert.Equal(direct.Warnings, composed.Warnings);
        Assert.Equal(direct.AttentionQuantity, composed.AttentionQuantity);
        Assert.Same(direct.Scenarios[0], composed.Scenarios[0]);
    }

    static InventoryPromotionSuggestionSnapshot Compose(InventoryPromotionSuggestionComposeInput input) =>
        InventoryPromotionSuggestionComposer.Compose(input);

    static InventoryPromotionSuggestionComposeInput Happy(
        int[]? ids = null,
        InventoryCommercialScenarioResult? result = null,
        InventoryAttentionPriority priority = InventoryAttentionPriority.High,
        bool wholesale = false)
    {
        ids ??= [1];
        var rows = ids.Select(id =>
        {
            var scenario = result is null
                ? Available(id, InventoryCommercialScenarioThesis.ProjectedExcess30, 8, Light(), Moderate())
                : Clone(result, productId: id);
            return ScenarioRow(id, scenario, priority, wholesale);
        }).ToArray();
        return new InventoryPromotionSuggestionComposeInput
        {
            Intelligence = Intelligence(ids),
            Scenarios = ScenarioSnap(rows),
        };
    }

    static InventoryIntelligenceSnapshot Intelligence(params int[] ids) =>
        new()
        {
            Rows = ids.Select(id => new ProductTurnoverRow { ProductId = id, Name = $"P{id}" }).ToArray(),
        };

    static InventoryCommercialScenarioSnapshot ScenarioSnap(
        params InventoryCommercialScenarioRow[] rows) =>
        new() { Rows = rows };

    static InventoryCommercialScenarioRow ScenarioRow(
        int id,
        InventoryCommercialScenarioResult result,
        InventoryAttentionPriority? priority = null,
        bool wholesale = false) =>
        new()
        {
            ProductId = id,
            Attention = priority is { } p
                ? new InventoryAttentionResult { ProductId = id, Priority = p }
                : new InventoryAttentionResult { ProductId = id, Priority = InventoryAttentionPriority.High },
            Facts = new InventoryCommercialFacts
            {
                ProductId = id,
                ProductFound = true,
                HasWholesalePricing = wholesale,
                LimitationReasons = wholesale
                    ? [InventoryCommercialFactsReason.WholesalePricingConfigured]
                    : [],
            },
            ScenarioResult = result.ProductId == id ? result : Clone(result, productId: id),
        };

    static InventoryCommercialScenarioResult Available(
        int productId,
        InventoryCommercialScenarioThesis thesis,
        double? quantity,
        params InventoryCommercialScenario[] scenarios) =>
        Available(productId, thesis, quantity, DefaultSource(thesis), scenarios);

    static InventoryCommercialScenarioResult Available(
        int productId,
        InventoryCommercialScenarioThesis thesis,
        double? quantity,
        InventoryCommercialAttentionQuantitySource source,
        params InventoryCommercialScenario[] scenarios) =>
        new()
        {
            ProductId = productId,
            Status = InventoryCommercialScenarioStatus.Available,
            PrimaryReason = thesis == InventoryCommercialScenarioThesis.ExpirySurplus
                ? InventoryCommercialScenarioReason.ExpirySurplus
                : InventoryCommercialScenarioReason.ProjectedExcess30,
            Thesis = thesis,
            Confidence = InventoryAttentionConfidence.Reliable,
            CurrentCatalogPrice = 10,
            CurrentGrossMarginPercent = 40,
            MinimumAllowedCatalogPrice = 8.20,
            MinimumGrossMarginPercent = 20,
            FinancialRoomAmount = 1.80,
            AttentionQuantity = quantity,
            AttentionQuantitySource = source,
            Scenarios = scenarios,
        };

    static InventoryCommercialAttentionQuantitySource DefaultSource(
        InventoryCommercialScenarioThesis thesis) =>
        thesis == InventoryCommercialScenarioThesis.ExpirySurplus
            ? InventoryCommercialAttentionQuantitySource.ExpirySurplus
            : InventoryCommercialAttentionQuantitySource.ProjectedExcess30;

    static InventoryCommercialScenarioResult Monitor(
        int productId,
        InventoryCommercialScenarioThesis thesis,
        InventoryCommercialScenarioReason primary,
        params InventoryCommercialScenario[] scenarios) =>
        new()
        {
            ProductId = productId,
            Status = InventoryCommercialScenarioStatus.MonitorOnly,
            PrimaryReason = primary,
            Thesis = thesis,
            Confidence = InventoryAttentionConfidence.Reliable,
            Scenarios = scenarios,
        };

    static InventoryCommercialScenarioResult Clone(
        InventoryCommercialScenarioResult source,
        InventoryAttentionConfidence? confidence = null,
        double? minMargin = null,
        int? productId = null) =>
        new()
        {
            ProductId = productId ?? source.ProductId,
            Status = source.Status,
            PrimaryReason = source.PrimaryReason,
            SecondaryReasons = source.SecondaryReasons,
            Thesis = source.Thesis,
            Confidence = confidence ?? source.Confidence,
            CurrentCatalogPrice = source.CurrentCatalogPrice,
            CurrentGrossMarginPercent = source.CurrentGrossMarginPercent,
            MinimumAllowedCatalogPrice = source.MinimumAllowedCatalogPrice,
            MinimumGrossMarginPercent = minMargin ?? source.MinimumGrossMarginPercent,
            FinancialRoomAmount = source.FinancialRoomAmount,
            CatalogPriceIsAboveMinimumAllowed = source.CatalogPriceIsAboveMinimumAllowed,
            AttentionQuantity = source.AttentionQuantity,
            AttentionQuantitySource = source.AttentionQuantitySource,
            Scenarios = source.Scenarios,
        };

    static InventoryCommercialScenario Light(double price = 9.40) =>
        new()
        {
            Kind = InventoryCommercialScenarioKind.Light,
            SimulatedCatalogPrice = price,
            ReductionAmount = 0.60,
            ReductionPercent = 6,
            GrossMarginPercent = 36.17,
        };

    static InventoryCommercialScenario Moderate(double price = 8.80) =>
        new()
        {
            Kind = InventoryCommercialScenarioKind.Moderate,
            SimulatedCatalogPrice = price,
            ReductionAmount = 1.20,
            ReductionPercent = 12,
            GrossMarginPercent = 31.82,
        };

    static string ReadSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}
