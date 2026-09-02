using System.Globalization;
using System.IO;
using System.Reflection;
using SGDB.Domain.Common;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B4B — motor puro de cenários de catálogo. Sem SQL, UI, PDV, promoção ou composer.
/// </summary>
public class InventoryCommercialScenarioEngineTests
{
    [Fact]
    public void QueryCount_e_zero() =>
        Assert.Equal(0, InventoryCommercialScenarioEngine.ExpectedQueryCount);

    [Fact]
    public void Guard_de_margem_e_finito() =>
        Assert.Equal(25, InventoryCommercialScenarioEngine.MarginGuardLimit);

    [Fact]
    public void Exemplo_S10_F820_gera_940_e_880()
    {
        var result = Eval(Happy());
        Assert.Equal(InventoryCommercialScenarioStatus.Available, result.Status);
        Assert.Equal(2, result.Scenarios.Count);
        Assert.Equal(InventoryCommercialScenarioKind.Light, result.Scenarios[0].Kind);
        Assert.Equal(9.40, result.Scenarios[0].SimulatedCatalogPrice);
        Assert.Equal(InventoryCommercialScenarioKind.Moderate, result.Scenarios[1].Kind);
        Assert.Equal(8.80, result.Scenarios[1].SimulatedCatalogPrice);
        Assert.DoesNotContain(result.Scenarios, s => s.SimulatedCatalogPrice == 8.20);
        Assert.All(result.Scenarios, s =>
        {
            Assert.True(s.SimulatedCatalogPrice > 8.20);
            Assert.True(s.SimulatedCatalogPrice < 10);
        });
    }

    [Fact]
    public void Maximo_dois_cenarios() =>
        Assert.InRange(Eval(Happy()).Scenarios.Count, 1, 2);

    [Fact]
    public void Piso_nao_e_cenario()
    {
        var result = Eval(Happy());
        Assert.Equal(8.20, result.MinimumAllowedCatalogPrice);
        Assert.DoesNotContain(result.Scenarios, s => Cents(s.SimulatedCatalogPrice) == Cents(8.20));
    }

    [Fact]
    public void Cenarios_abaixo_do_atual_e_acima_do_piso()
    {
        var result = Eval(Happy());
        Assert.All(result.Scenarios, s =>
        {
            Assert.True(Cents(s.SimulatedCatalogPrice) < Cents(10));
            Assert.True(Cents(s.SimulatedCatalogPrice) > Cents(8.20));
        });
    }

    [Fact]
    public void Ordenacao_leve_depois_moderado()
    {
        var result = Eval(Happy());
        Assert.Equal(InventoryCommercialScenarioKind.Light, result.Scenarios[0].Kind);
        Assert.Equal(InventoryCommercialScenarioKind.Moderate, result.Scenarios[1].Kind);
        Assert.True(result.Scenarios[0].SimulatedCatalogPrice > result.Scenarios[1].SimulatedCatalogPrice);
    }

    [Fact]
    public void Excess30_valido_Available()
    {
        var result = Eval(Happy(excess: 8, surplus: null));
        Assert.Equal(InventoryCommercialScenarioStatus.Available, result.Status);
        Assert.Equal(InventoryCommercialScenarioThesis.ProjectedExcess30, result.Thesis);
        Assert.Equal(8, result.AttentionQuantity);
        Assert.Equal(
            InventoryCommercialAttentionQuantitySource.ProjectedExcess30,
            result.AttentionQuantitySource);
        Assert.NotEmpty(result.Scenarios);
    }

    [Fact]
    public void ExpirySurplus_valido_Available()
    {
        var result = Eval(Happy(excess: null, surplus: 3.5, eligibilityReason:
            InventoryCommercialEligibilityReason.ProjectedExpirySurplus));
        Assert.Equal(InventoryCommercialScenarioStatus.Available, result.Status);
        Assert.Equal(InventoryCommercialScenarioThesis.ExpirySurplus, result.Thesis);
        Assert.Equal(3.5, result.AttentionQuantity);
        Assert.Equal(
            InventoryCommercialAttentionQuantitySource.ExpirySurplus,
            result.AttentionQuantitySource);
    }

    [Fact]
    public void Ambos_horizontes_ExpirySurplus_primaria_sem_soma()
    {
        var result = Eval(Happy(excess: 10, surplus: 3));
        Assert.Equal(InventoryCommercialScenarioThesis.ExpirySurplus, result.Thesis);
        Assert.Equal(3, result.AttentionQuantity);
        Assert.NotEqual(13, result.AttentionQuantity);
        Assert.Equal(
            InventoryCommercialAttentionQuantitySource.ExpirySurplus,
            result.AttentionQuantitySource);
    }

    [Fact]
    public void Idle_sozinho_MonitorOnly_sem_cenario()
    {
        var result = Eval(Happy(
            excess: null,
            surplus: null,
            eligibilityReason: InventoryCommercialEligibilityReason.Idle,
            secondary: [InventoryCommercialEligibilityReason.Idle]));
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, result.Status);
        Assert.Equal(InventoryCommercialScenarioThesis.Idle, result.Thesis);
        Assert.Empty(result.Scenarios);
        Assert.Null(result.AttentionQuantity);
        Assert.Equal(10, result.CurrentCatalogPrice);
        Assert.Equal(8.20, result.MinimumAllowedCatalogPrice);
        Assert.Contains(InventoryCommercialScenarioReason.Idle, Reasons(result));
    }

    [Fact]
    public void HighCoverage_MonitorOnly()
    {
        var result = Eval(Happy(
            excess: null,
            surplus: null,
            kind: InventoryCommercialEligibilityKind.MonitorOnly,
            eligibilityReason: InventoryCommercialEligibilityReason.HighCoverageWithoutExcess));
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, result.Status);
        Assert.Equal(InventoryCommercialScenarioThesis.HighCoverage, result.Thesis);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.HighCoverageMonitoring, Reasons(result));
    }

    [Fact]
    public void Expired_com_excesso_vence()
    {
        var result = Eval(Happy(
            excess: 12,
            kind: InventoryCommercialEligibilityKind.NoCommercialRecommendation,
            eligibilityReason: InventoryCommercialEligibilityReason.Expired,
            secondary: [InventoryCommercialEligibilityReason.ProjectedExcess]));
        Assert.Equal(InventoryCommercialScenarioStatus.Expired, result.Status);
        Assert.Equal(InventoryCommercialScenarioReason.Expired, result.PrimaryReason);
        Assert.Equal(InventoryCommercialScenarioThesis.None, result.Thesis);
        Assert.Empty(result.Scenarios);
        Assert.Null(result.AttentionQuantity);
    }

    [Fact]
    public void Expired_com_idle_vence()
    {
        var result = Eval(Happy(
            excess: null,
            kind: InventoryCommercialEligibilityKind.NoCommercialRecommendation,
            eligibilityReason: InventoryCommercialEligibilityReason.Expired,
            secondary: [InventoryCommercialEligibilityReason.Idle]));
        Assert.Equal(InventoryCommercialScenarioStatus.Expired, result.Status);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void ExpiresToday_MonitorOnly()
    {
        var result = Eval(Happy(
            kind: InventoryCommercialEligibilityKind.MonitorOnly,
            eligibilityReason: InventoryCommercialEligibilityReason.ExpiresToday,
            excess: 5));
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, result.Status);
        Assert.Equal(InventoryCommercialScenarioReason.ExpiresToday, result.PrimaryReason);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void ReviewData_nao_vira_cenario_mesmo_com_B2_B3_bons()
    {
        var result = Eval(Happy(
            kind: InventoryCommercialEligibilityKind.ReviewData,
            eligibilityReason: InventoryCommercialEligibilityReason.LocationLimitation));
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.LocationLimitation, Reasons(result));
    }

    [Fact]
    public void InsufficientHistory_ReviewData()
    {
        var result = Eval(Happy(
            kind: InventoryCommercialEligibilityKind.ReviewData,
            eligibilityReason: InventoryCommercialEligibilityReason.InsufficientHistory));
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, result.Status);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void LocationLimitation_ReviewData()
    {
        var result = Eval(Happy(
            kind: InventoryCommercialEligibilityKind.ReviewData,
            eligibilityReason: InventoryCommercialEligibilityReason.LocationLimitation,
            excess: 9));
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, result.Status);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void NoCommercialRecommendation_sem_expired()
    {
        var result = Eval(Happy(
            excess: null,
            kind: InventoryCommercialEligibilityKind.NoCommercialRecommendation,
            eligibilityReason: InventoryCommercialEligibilityReason.NoObservableDemand));
        Assert.Equal(InventoryCommercialScenarioStatus.NoRecommendation, result.Status);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Unavailable_sem_cenario()
    {
        var result = Eval(Happy(confidence: InventoryAttentionConfidence.Unavailable));
        Assert.Equal(InventoryCommercialScenarioStatus.FinancialDataUnavailable, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.UnavailableConfidence, Reasons(result));
    }

    [Fact]
    public void Limited_com_excesso_nao_reduz()
    {
        var result = Eval(Happy(confidence: InventoryAttentionConfidence.Limited, excess: 20));
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.LimitedConfidence, Reasons(result));
    }

    [Fact]
    public void Limited_com_sobra_validade_nao_reduz()
    {
        var result = Eval(Happy(
            confidence: InventoryAttentionConfidence.Limited,
            excess: null,
            surplus: 4,
            eligibilityReason: InventoryCommercialEligibilityReason.ProjectedExpirySurplus));
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, result.Status);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Policy_Missing_nao_vira_zero()
    {
        var result = Eval(Happy(policy: MissingPolicy()));
        Assert.Equal(InventoryCommercialScenarioStatus.PolicyMissing, result.Status);
        Assert.Null(result.MinimumGrossMarginPercent);
        Assert.NotEqual(0, result.MinimumGrossMarginPercent);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.PolicyMissing, Reasons(result));
    }

    [Fact]
    public void Policy_Invalid_nao_vira_Missing()
    {
        var result = Eval(Happy(policy: InvalidPolicy()));
        Assert.Equal(InventoryCommercialScenarioStatus.PolicyInvalid, result.Status);
        Assert.NotEqual(InventoryCommercialScenarioStatus.PolicyMissing, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.PolicyInvalid, Reasons(result));
    }

    [Fact]
    public void Policy_0_porcento_e_valida()
    {
        var facts = Facts(sale: 12, cost: 10);
        var policy = AvailablePolicy(0m);
        var floor = InventoryCommercialPriceFloorEngine.Evaluate(
            facts, InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(policy));
        var result = Eval(Happy(
            facts: facts,
            policy: policy,
            floor: floor,
            sale: 12));
        Assert.Equal(InventoryCommercialScenarioStatus.Available, result.Status);
        Assert.Equal(0, result.MinimumGrossMarginPercent);
        Assert.Equal(10, result.MinimumAllowedCatalogPrice);
        Assert.NotEmpty(result.Scenarios);
        Assert.All(result.Scenarios, s => Assert.True(s.GrossMarginPercent + 1e-9 >= 0));
    }

    [Fact]
    public void Policy_Available_valida()
    {
        var result = Eval(Happy());
        Assert.Equal(InventoryCommercialScenarioStatus.Available, result.Status);
        Assert.Equal(20, result.MinimumGrossMarginPercent);
    }

    [Fact]
    public void Nenhuma_margem_default_no_motor()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialScenarioEngine.cs"));
        Assert.DoesNotContain("= 15", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 18", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 22", source, StringComparison.Ordinal);
        Assert.DoesNotContain("= 30", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.004, InventoryCommercialScenarioReason.UnknownCost)]
    [InlineData(0, InventoryCommercialScenarioReason.UnknownCost)]
    [InlineData(-2, InventoryCommercialScenarioReason.InvalidCost)]
    public void Custo_inapto_sem_cenario(double cost, InventoryCommercialScenarioReason expected)
    {
        var facts = Facts(cost: cost);
        var result = Eval(Happy(facts: facts, floor: UnavailableFloor()));
        Assert.Equal(InventoryCommercialScenarioStatus.FinancialDataUnavailable, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Contains(expected, Reasons(result));
    }

    [Fact]
    public void Preco_inutilizavel()
    {
        var facts = Facts(sale: 0);
        var result = Eval(Happy(facts: facts, floor: UnavailableFloor()));
        Assert.Equal(InventoryCommercialScenarioStatus.FinancialDataUnavailable, result.Status);
        Assert.Contains(InventoryCommercialScenarioReason.UnusablePrice, Reasons(result));
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Preco_invalido()
    {
        var facts = Facts(sale: -3);
        var result = Eval(Happy(facts: facts, floor: UnavailableFloor()));
        Assert.Contains(InventoryCommercialScenarioReason.InvalidPrice, Reasons(result));
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Produto_ausente()
    {
        var facts = Facts(found: false);
        var result = Eval(Happy(facts: facts, floor: UnavailableFloor()));
        Assert.Contains(InventoryCommercialScenarioReason.MissingProduct, Reasons(result));
        Assert.Empty(result.Scenarios);
        Assert.Null(result.CurrentCatalogPrice);
    }

    [Fact]
    public void Nao_vendavel_mesmo_com_excesso()
    {
        var facts = Facts(allowsSale: false);
        var result = Eval(Happy(facts: facts, floor: UnavailableFloor(), excess: 15));
        Assert.Equal(InventoryCommercialScenarioStatus.FinancialDataUnavailable, result.Status);
        Assert.Contains(InventoryCommercialScenarioReason.NotSellable, Reasons(result));
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Kit_sem_cenario_e_sem_BOM()
    {
        var facts = Facts(composition: true);
        var result = Eval(Happy(facts: facts, floor: UnavailableFloor()));
        Assert.Contains(InventoryCommercialScenarioReason.CompositionProduct, Reasons(result));
        Assert.Empty(result.Scenarios);
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialScenarioEngine.cs"));
        Assert.DoesNotContain("ProductCompositionService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Cigarro_ambiguo_sem_dividir_custo()
    {
        var facts = Facts(cigarette: true, unitSale: 0.85);
        Assert.False(facts.CanEvaluateFinancialScenario);
        var result = Eval(Happy(facts: facts, floor: UnavailableFloor()));
        Assert.Contains(InventoryCommercialScenarioReason.AmbiguousSaleUnit, Reasons(result));
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Atacado_nao_substitui_catalogo()
    {
        var facts = Facts(wholesalePrice: 8.5, wholesaleQty: 6);
        Assert.True(facts.CanEvaluateFinancialScenario);
        Assert.True(facts.HasWholesalePricing);
        var result = Eval(Happy(facts: facts));
        Assert.Equal(InventoryCommercialScenarioStatus.Available, result.Status);
        Assert.Equal(10, result.CurrentCatalogPrice);
        Assert.All(result.Scenarios, s => Assert.NotEqual(8.5, s.SimulatedCatalogPrice));
    }

    [Fact]
    public void Preco_igual_ao_piso()
    {
        var floor = Floor(sale: 8.20, floor: 8.20, room: 0, above: false, meets: true);
        var result = Eval(Happy(floor: floor, sale: 8.20, facts: Facts(sale: 8.20, cost: 6)));
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.PriceAtFloor, Reasons(result));
        Assert.Equal(0, result.FinancialRoomAmount);
    }

    [Fact]
    public void Preco_abaixo_do_piso()
    {
        var floor = Floor(sale: 7, floor: 8.20, room: 0, above: false, meets: false);
        var result = Eval(Happy(floor: floor, facts: Facts(sale: 7, cost: 6)));
        Assert.Equal(InventoryCommercialScenarioStatus.MonitorOnly, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.PriceBelowFloor, Reasons(result));
    }

    [Fact]
    public void Espaco_zero_sem_cenario()
    {
        var floor = Floor(sale: 10, floor: 10, room: 0, above: false, meets: true);
        var result = Eval(Happy(floor: floor, facts: Facts(sale: 10, cost: 6)));
        Assert.Empty(result.Scenarios);
        Assert.Equal(0, result.FinancialRoomAmount);
    }

    [Fact]
    public void Espaco_positivo()
    {
        var result = Eval(Happy());
        Assert.Equal(1.80, result.FinancialRoomAmount);
        Assert.True(result.CatalogPriceIsAboveMinimumAllowed);
        Assert.NotEmpty(result.Scenarios);
    }

    [Fact]
    public void Floor_unavailable()
    {
        var result = Eval(Happy(floor: UnavailableFloor()));
        Assert.Equal(InventoryCommercialScenarioStatus.FinancialDataUnavailable, result.Status);
        Assert.Contains(InventoryCommercialScenarioReason.FloorUnavailable, Reasons(result));
        Assert.Empty(result.Scenarios);
        Assert.Null(result.FinancialRoomAmount);
    }

    [Fact]
    public void Espaco_1_centavo_colapsa()
    {
        var facts = Facts(sale: 10, cost: 6);
        var floor = Floor(sale: 10, floor: 9.99, room: 0.01, above: true, meets: true);
        var result = Eval(Happy(facts: facts, floor: floor));
        Assert.Empty(result.Scenarios);
        Assert.NotEqual(InventoryCommercialScenarioStatus.Available, result.Status);
        Assert.Contains(InventoryCommercialScenarioReason.ScenarioCollapsedByRounding, Reasons(result));
    }

    [Fact]
    public void Espaco_2_centavos_nao_duplica()
    {
        var facts = Facts(sale: 10, cost: 6);
        var floor = Floor(sale: 10, floor: 9.98, room: 0.02, above: true, meets: true);
        var result = Eval(Happy(facts: facts, floor: floor));
        Assert.Equal(InventoryCommercialScenarioStatus.Available, result.Status);
        Assert.Single(result.Scenarios);
        Assert.Equal(9.99, result.Scenarios[0].SimulatedCatalogPrice);
        Assert.True(result.Scenarios[0].SimulatedCatalogPrice > 9.98);
        Assert.True(result.Scenarios[0].SimulatedCatalogPrice < 10);
    }

    [Fact]
    public void Espaco_pequeno_nao_Available_vazio()
    {
        var facts = Facts(sale: 10, cost: 6);
        var floor = Floor(sale: 10, floor: 9.995, room: 0.005, above: true, meets: true);
        var result = Eval(Happy(facts: facts, floor: floor));
        if (result.Status == InventoryCommercialScenarioStatus.Available)
            Assert.NotEmpty(result.Scenarios);
        else
            Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Guarda_incrementa_centavo_ate_margem()
    {
        var facts = Facts(sale: 12, cost: 6);
        var policy = AvailablePolicy(40m);
        var floor = Floor(sale: 12, floor: 8, room: 4, above: true, meets: true, margin: 40);
        var result = Eval(Happy(facts: facts, policy: policy, floor: floor));
        Assert.NotEmpty(result.Scenarios);
        Assert.All(result.Scenarios, s =>
        {
            Assert.True(s.GrossMarginPercent + 1e-9 >= 40);
            Assert.True(s.SimulatedCatalogPrice < 12);
            Assert.True(s.SimulatedCatalogPrice > 8);
        });
    }

    [Fact]
    public void Reducao_unitaria()
    {
        var result = Eval(Happy());
        var light = result.Scenarios[0];
        Assert.Equal(MonetaryRounding.Round(10 - 9.40), light.ReductionAmount);
        Assert.Equal(0.60, light.ReductionAmount);
        Assert.Equal(MonetaryRounding.Round(0.60 / 10 * 100), light.ReductionPercent);
        Assert.Equal(6, light.ReductionPercent);
        Assert.Equal(ProductPriceCalculator.MarginOnSale(6, 9.40), light.GrossMarginPercent);
        Assert.True(light.GrossMarginPercent + 1e-9 >= 20);
    }

    [Fact]
    public void Margem_de_todos_respeita_policy()
    {
        var result = Eval(Happy());
        Assert.All(result.Scenarios, s =>
            Assert.True(s.GrossMarginPercent + 1e-9 >= 20));
    }

    [Fact]
    public void Sem_impacto_total()
    {
        var type = typeof(InventoryCommercialScenario);
        Assert.Null(type.GetProperty("TotalRevenue"));
        Assert.Null(type.GetProperty("TotalMargin"));
        Assert.Null(type.GetProperty("SellThrough"));
        Assert.Null(type.GetProperty("PromotionQuantity"));
        Assert.Null(type.GetProperty("QuantityToSell"));
        Assert.Null(typeof(InventoryCommercialScenarioResult).GetProperty("CampaignDays"));
    }

    [Fact]
    public void Zero_nao_e_indisponivel_na_policy()
    {
        var missing = Eval(Happy(policy: MissingPolicy()));
        var zero = Eval(Happy(
            facts: Facts(sale: 12, cost: 10),
            policy: AvailablePolicy(0m),
            floor: InventoryCommercialPriceFloorEngine.Evaluate(
                Facts(sale: 12, cost: 10),
                InventoryCommercialMarginPolicyResolver.TryCreatePriceFloorPolicy(AvailablePolicy(0m)))));
        Assert.Null(missing.MinimumGrossMarginPercent);
        Assert.Equal(0, zero.MinimumGrossMarginPercent);
        Assert.NotEqual(missing.Status, zero.Status);
    }

    [Fact]
    public void Determinismo()
    {
        var input = Happy();
        var a = InventoryCommercialScenarioEngine.Evaluate(input);
        var b = InventoryCommercialScenarioEngine.Evaluate(input);
        Assert.Equal(a.Status, b.Status);
        Assert.Equal(a.Scenarios.Count, b.Scenarios.Count);
        Assert.Equal(a.Scenarios[0].SimulatedCatalogPrice, b.Scenarios[0].SimulatedCatalogPrice);
        Assert.Equal(a.Scenarios[1].SimulatedCatalogPrice, b.Scenarios[1].SimulatedCatalogPrice);
        Assert.Equal(a.AttentionQuantity, b.AttentionQuantity);
    }

    [Fact]
    public void Sobrecarga_e_DTO_equivalentes()
    {
        var input = Happy();
        var viaDto = InventoryCommercialScenarioEngine.Evaluate(input);
        var viaArgs = InventoryCommercialScenarioEngine.Evaluate(
            input.Eligibility, input.Facts, input.PolicyResolution, input.Floor,
            input.Turnover, input.Projection, input.Attention);
        Assert.Equal(viaDto.Status, viaArgs.Status);
        Assert.Equal(viaDto.Scenarios[0].SimulatedCatalogPrice, viaArgs.Scenarios[0].SimulatedCatalogPrice);
    }

    [Fact]
    public void Budget_pipeline_permanece_9()
    {
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialEligibilityEngine.ExpectedQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialPriceFloorEngine.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryCommercialMarginPolicyResolver.ExpectedQueryCount
            + InventoryCommercialScenarioEngine.ExpectedQueryCount);
    }

    [Fact]
    public void Pureza_sem_IO_cultura_agora_nem_settings()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialScenarioEngine.cs"));
        var model = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Models", "InventoryCommercialScenario.cs"));
        foreach (var text in new[] { source, model })
        {
            Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AppSettingsService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MarginSettingsService.Load", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AppSession", text, StringComparison.Ordinal);
            Assert.DoesNotContain("StoreNetwork", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CurrentCulture", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Random", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AuditService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("preco_promocional", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SaleFromCostAndMargin", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Catalogo_nao_e_preco_final_de_PDV()
    {
        var result = Eval(Happy());
        Assert.Equal(10, result.CurrentCatalogPrice);
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialScenarioEngine.cs"));
        Assert.DoesNotContain("PdvService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Input_nulo_ReviewData()
    {
        var result = InventoryCommercialScenarioEngine.Evaluate((InventoryCommercialScenarioInput?)null);
        Assert.Equal(InventoryCommercialScenarioStatus.ReviewData, result.Status);
        Assert.Empty(result.Scenarios);
        Assert.Contains(InventoryCommercialScenarioReason.InvalidInput, Reasons(result));
    }

    [Fact]
    public void Quantidade_fisica_decimal_nao_arredonda_para_inteiro()
    {
        var result = Eval(Happy(excess: null, surplus: 3.25,
            eligibilityReason: InventoryCommercialEligibilityReason.ProjectedExpirySurplus));
        Assert.Equal(3.25, result.AttentionQuantity);
    }

    [Fact]
    public void PolicyMissing_nao_esconde_Expired()
    {
        var result = Eval(Happy(
            kind: InventoryCommercialEligibilityKind.NoCommercialRecommendation,
            eligibilityReason: InventoryCommercialEligibilityReason.Expired,
            policy: MissingPolicy(),
            excess: 8));
        Assert.Equal(InventoryCommercialScenarioStatus.Expired, result.Status);
        Assert.Equal(InventoryCommercialScenarioReason.Expired, result.PrimaryReason);
    }

    static InventoryCommercialScenarioResult Eval(InventoryCommercialScenarioInput input) =>
        InventoryCommercialScenarioEngine.Evaluate(input);

    static InventoryCommercialScenarioInput Happy(
        InventoryCommercialEligibilityKind kind = InventoryCommercialEligibilityKind.CommercialCandidate,
        InventoryCommercialEligibilityReason eligibilityReason =
            InventoryCommercialEligibilityReason.ProjectedExcess,
        IReadOnlyList<InventoryCommercialEligibilityReason>? secondary = null,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable,
        double? excess = 8,
        double? surplus = null,
        InventoryCommercialFacts? facts = null,
        InventoryCommercialMarginPolicyResolution? policy = null,
        InventoryCommercialPriceFloorResult? floor = null,
        double sale = 10) =>
        new()
        {
            Eligibility = new InventoryCommercialEligibilityResult
            {
                ProductId = 1,
                Kind = kind,
                PrimaryReason = eligibilityReason,
                SecondaryReasons = secondary ?? [],
                Confidence = confidence,
            },
            Facts = facts ?? Facts(sale: sale, cost: 6),
            PolicyResolution = policy ?? AvailablePolicy(20m),
            Floor = floor ?? Floor(sale: sale, floor: 8.20, room: 1.80, above: true, meets: true),
            Attention = new InventoryAttentionResult
            {
                ProductId = 1,
                Confidence = confidence,
                ProjectedExcessQuantity = excess,
                ProjectedExpirySurplusQuantity = surplus,
                PrimaryReason = eligibilityReason == InventoryCommercialEligibilityReason.ProjectedExpirySurplus
                    ? InventoryAttentionReason.SurplusAtExpiry
                    : InventoryAttentionReason.ProjectedExcess30,
            },
            Turnover = new ProductTurnoverRow { ProductId = 1, TotalStock = 30, Stock = 30 },
        };

    static InventoryCommercialFacts Facts(
        double sale = 10,
        double cost = 6,
        bool found = true,
        bool allowsSale = true,
        bool composition = false,
        bool cigarette = false,
        double wholesalePrice = 0,
        double wholesaleQty = 0,
        double unitSale = 0) =>
        InventoryCommercialFactsEngine.Classify(new InventoryCommercialFactsInput
        {
            ProductId = 1,
            ProductFound = found,
            CatalogSalePrice = sale,
            CurrentAverageCost = cost,
            AllowsSale = allowsSale,
            IsCompositionProduct = composition,
            IsCigaretteProduct = cigarette,
            WholesalePrice = wholesalePrice,
            WholesaleMinimumQuantity = wholesaleQty,
            UnitSalePrice = unitSale,
        });

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

    static InventoryCommercialPriceFloorResult Floor(
        double sale,
        double floor,
        double room,
        bool above,
        bool meets,
        double margin = 20) =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialPriceFloorStatus.Available,
            MinimumGrossMarginPercent = margin,
            CatalogSalePrice = sale,
            CurrentAverageCost = 6,
            CurrentGrossMarginPercent = ProductPriceCalculator.MarginOnSale(6, sale),
            MinimumAllowedCatalogPrice = floor,
            MeetsMinimumMargin = meets,
            CatalogPriceIsAboveMinimumAllowed = above,
            AmountAboveMinimumAllowedCatalogPrice = room,
        };

    static InventoryCommercialPriceFloorResult UnavailableFloor() =>
        new()
        {
            ProductId = 1,
            Status = InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable,
            Reasons = [InventoryCommercialPriceFloorReason.FinancialScenarioUnavailable],
        };

    static IEnumerable<InventoryCommercialScenarioReason> Reasons(InventoryCommercialScenarioResult result)
    {
        yield return result.PrimaryReason;
        foreach (var reason in result.SecondaryReasons)
            yield return reason;
    }

    static long Cents(double value) =>
        (long)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);

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
