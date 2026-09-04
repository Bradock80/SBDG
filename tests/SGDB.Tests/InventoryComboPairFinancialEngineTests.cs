using System.IO;
using SGDB.Domain.Common;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71A-B3 — fatos financeiros do par. Sem SQL, UI, B1, B2, ranking ou kit.
/// </summary>
public class InventoryComboPairFinancialEngineTests
{
    [Fact]
    public void QueryCount_e_zero()
    {
        Assert.Equal(0, InventoryComboPairFinancialEngine.ExpectedQueryCount);
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
    }

    [Fact]
    public void Caso_basico_target_10_custo_6_anchor_20_custo_10_margem_20()
    {
        var target = Facts(sale: 10, cost: 6);
        var anchor = Facts(sale: 20, cost: 10, id: 2);
        var result = Eval(target, anchor, 20);
        var targetFloor = FloorOf(target, 20);
        var pairFloor = PairFloor(6, 10, 20);

        Assert.Equal(InventoryComboPairFinancialStatus.Available, result.Status);
        Assert.Equal(30, result.NormalPairPrice);
        Assert.Equal(16, result.PairCost);
        Assert.Equal(pairFloor, result.PairFloorPrice);
        Assert.Equal(targetFloor, result.TargetFloorPrice);
        Assert.Equal(7.5, result.TargetFloorPrice);
        Assert.Equal(20, result.PairFloorPrice);
        Assert.Equal(2, result.Scenarios.Count);

        var current = result.Scenarios[0];
        Assert.Equal(InventoryComboPairFinancialScenarioKind.CurrentPrices, current.Kind);
        Assert.Equal(30, current.PairPrice);
        Assert.Equal(14, current.GrossProfit);
        Assert.Equal(14d / 30d, current.GrossMargin);
        Assert.Equal(0, current.ReductionFromCurrent);

        var reduced = result.Scenarios[1];
        Assert.Equal(InventoryComboPairFinancialScenarioKind.TargetReductionReference, reduced.Kind);
        Assert.Equal(27.5, reduced.PairPrice);
        Assert.Equal(11.5, reduced.GrossProfit);
        Assert.Equal(11.5 / 27.5, reduced.GrossMargin);
        Assert.Equal(2.5, reduced.ReductionFromCurrent);
        AssertScenariosRespectFloors(result, anchor.CatalogSalePrice!.Value);
    }

    [Fact]
    public void Sem_reducao_quando_target_ja_no_piso()
    {
        var target = Facts(sale: 7.5, cost: 6);
        var anchor = Facts(sale: 20, cost: 10, id: 2);
        var result = Eval(target, anchor, 20);
        Assert.Equal(InventoryComboPairFinancialStatus.Available, result.Status);
        Assert.Equal(27.5, result.NormalPairPrice);
        var only = Assert.Single(result.Scenarios);
        Assert.Equal(InventoryComboPairFinancialScenarioKind.CurrentPrices, only.Kind);
        Assert.Equal(27.5, only.PairPrice);
        Assert.Equal(0, only.ReductionFromCurrent);
    }

    [Fact]
    public void Com_reducao_gera_dois_cenarios()
    {
        var result = Eval(Facts(sale: 10, cost: 6), Facts(sale: 20, cost: 10, id: 2), 20);
        Assert.Equal(2, result.Scenarios.Count);
        Assert.Equal(
            InventoryComboPairFinancialScenarioKind.TargetReductionReference,
            result.Scenarios[1].Kind);
        Assert.True(result.Scenarios[1].ReductionFromCurrent > 0);
    }

    [Fact]
    public void PairFloor_domina_quando_maior_que_anchor_mais_target_floor()
    {
        var target = Facts(sale: 10, cost: 6);
        var anchor = Facts(sale: 12, cost: 10, id: 2);
        var result = Eval(target, anchor, 20);
        var targetFloor = FloorOf(target, 20);
        var pairFloor = PairFloor(6, 10, 20);
        var candidate = MonetaryRounding.Round(12 + targetFloor);
        Assert.True(pairFloor > candidate);
        Assert.Equal(InventoryComboPairFinancialStatus.Available, result.Status);
        Assert.Equal(pairFloor, result.PairFloorPrice);
        Assert.Equal(2, result.Scenarios.Count);
        Assert.Equal(pairFloor, result.Scenarios[1].PairPrice);
        AssertScenariosRespectFloors(result, 12);
    }

    [Fact]
    public void Current_abaixo_do_piso_nao_mascara()
    {
        var target = Facts(sale: 8, cost: 6);
        var anchor = Facts(sale: 10, cost: 10, id: 2);
        var result = Eval(target, anchor, 20);
        Assert.Equal(InventoryComboPairFinancialStatus.Unavailable, result.Status);
        Assert.Equal(InventoryComboPairFinancialReason.PriceBelowFloor, result.Reason);
        Assert.Equal(18, result.NormalPairPrice);
        Assert.Equal(20, result.PairFloorPrice);
        Assert.True(result.NormalPairPrice < result.PairFloorPrice);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Ancora_nao_e_reduzida_usa_catalogo_nao_piso()
    {
        var target = Facts(sale: 10, cost: 6);
        var anchor = Facts(sale: 20, cost: 10, id: 2);
        var result = Eval(target, anchor, 20);
        var targetFloor = FloorOf(target, 20);
        var anchorFloor = FloorOf(anchor, 20);
        Assert.Equal(12.5, anchorFloor);
        var withCatalog = MonetaryRounding.Round(20 + targetFloor);
        var withAnchorFloor = MonetaryRounding.Round(anchorFloor + targetFloor);
        Assert.NotEqual(withCatalog, withAnchorFloor);
        Assert.Equal(withCatalog, result.Scenarios[1].PairPrice);
        Assert.NotEqual(withAnchorFloor, result.Scenarios[1].PairPrice);
    }

    [Fact]
    public void Target_reduction_nunca_abaixo_do_piso_individual()
    {
        var target = Facts(sale: 10, cost: 6);
        var anchor = Facts(sale: 20, cost: 10, id: 2);
        var result = Eval(target, anchor, 20);
        AssertScenariosRespectFloors(result, 20);
        var reduced = result.Scenarios[1];
        var impliedCents = InventoryCommercialPriceFloorEngine.ToCents(reduced.PairPrice)
            - InventoryCommercialPriceFloorEngine.ToCents(MonetaryRounding.Round(20));
        Assert.True(impliedCents >= InventoryCommercialPriceFloorEngine.ToCents(result.TargetFloorPrice!.Value));
    }

    [Fact]
    public void Lucro_e_margem_nao_dividem_por_zero()
    {
        var result = Eval(Facts(sale: 10, cost: 6), Facts(sale: 20, cost: 10, id: 2), 20);
        foreach (var scenario in result.Scenarios)
        {
            Assert.True(scenario.PairPrice > 0);
            Assert.Equal(
                MonetaryRounding.Round(scenario.PairPrice - result.PairCost!.Value),
                scenario.GrossProfit);
            Assert.Equal(scenario.GrossProfit / scenario.PairPrice, scenario.GrossMargin);
            Assert.InRange(scenario.GrossMargin, 0, 1);
        }
    }

    [Theory]
    [InlineData(10.001, 20.001)]
    [InlineData(10.005, 20.005)]
    [InlineData(10.009, 20.009)]
    public void Rounding_de_soma_e_piso_nao_fica_um_centavo_abaixo(double targetSale, double anchorSale)
    {
        var target = Facts(sale: targetSale, cost: 6.001);
        var anchor = Facts(sale: anchorSale, cost: 10.009, id: 2);
        var result = Eval(target, anchor, 20);
        if (result.Status != InventoryComboPairFinancialStatus.Available)
        {
            Assert.Equal(InventoryComboPairFinancialReason.PriceBelowFloor, result.Reason);
            Assert.Empty(result.Scenarios);
            return;
        }

        AssertScenariosRespectFloors(result, anchorSale);
        foreach (var scenario in result.Scenarios)
        {
            Assert.Equal(
                MonetaryRounding.Round(scenario.PairPrice),
                scenario.PairPrice);
            Assert.Equal(
                MonetaryRounding.Round(scenario.GrossProfit),
                scenario.GrossProfit);
        }
    }

    [Fact]
    public void Margem_zero_segue_70F()
    {
        var target = Facts(sale: 12, cost: 10);
        var anchor = Facts(sale: 15, cost: 10, id: 2);
        var result = Eval(target, anchor, 0);
        Assert.Equal(InventoryComboPairFinancialStatus.Available, result.Status);
        Assert.Equal(10, result.TargetFloorPrice);
        Assert.Equal(20, result.PairFloorPrice);
        Assert.Equal(27, result.NormalPairPrice);
        Assert.Equal(2, result.Scenarios.Count);
        Assert.Equal(25, result.Scenarios[1].PairPrice);
    }

    [Fact]
    public void Margem_alta_valida_99()
    {
        var target = Facts(sale: 2000, cost: 10);
        var anchor = Facts(sale: 2000, cost: 10, id: 2);
        var result = Eval(target, anchor, 99);
        Assert.Equal(InventoryComboPairFinancialStatus.Available, result.Status);
        Assert.Equal(FloorOf(target, 99), result.TargetFloorPrice);
        Assert.Equal(PairFloor(10, 10, 99), result.PairFloorPrice);
        Assert.NotEmpty(result.Scenarios);
        AssertScenariosRespectFloors(result, 2000);
    }

    [Fact]
    public void Margem_100_invalida()
    {
        var result = Eval(Facts(), Facts(id: 2), 100);
        AssertUnavailable(result, InventoryComboPairFinancialReason.MarginPolicyUnavailable);
    }

    [Fact]
    public void Margem_maior_que_100_invalida()
    {
        var result = Eval(Facts(), Facts(id: 2), 150);
        AssertUnavailable(result, InventoryComboPairFinancialReason.MarginPolicyUnavailable);
    }

    [Fact]
    public void Margem_negativa_invalida()
    {
        var result = Eval(Facts(), Facts(id: 2), -1);
        AssertUnavailable(result, InventoryComboPairFinancialReason.MarginPolicyUnavailable);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Margem_nao_finita_invalida(double percent)
    {
        var result = Eval(Facts(), Facts(id: 2), percent);
        AssertUnavailable(result, InventoryComboPairFinancialReason.MarginPolicyUnavailable);
    }

    [Fact]
    public void Politica_ausente()
    {
        var result = InventoryComboPairFinancialEngine.Evaluate(new InventoryComboPairFinancialInput
        {
            TargetFacts = Facts(),
            AnchorFacts = Facts(id: 2),
            MinGrossMarginPolicy = null,
        });
        AssertUnavailable(result, InventoryComboPairFinancialReason.MarginPolicyUnavailable);
    }

    [Fact]
    public void Input_nulo()
    {
        var result = InventoryComboPairFinancialEngine.Evaluate(null);
        AssertUnavailable(result, InventoryComboPairFinancialReason.MarginPolicyUnavailable);
    }

    [Fact]
    public void Custo_zero_segue_70F_indisponivel()
    {
        var result = Eval(Facts(cost: 0), Facts(id: 2), 20);
        AssertUnavailable(result, InventoryComboPairFinancialReason.TargetFinancialUnavailable);
        Assert.False(Facts(cost: 0).CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Preco_zero_nao_gera_cenario()
    {
        var zeroTarget = Eval(Facts(sale: 0), Facts(id: 2), 20);
        var zeroAnchor = Eval(Facts(), Facts(sale: 0, id: 2), 20);
        var bothZero = Eval(Facts(sale: 0, cost: 0), Facts(sale: 0, cost: 0, id: 2), 20);
        AssertUnavailable(zeroTarget, InventoryComboPairFinancialReason.TargetFinancialUnavailable);
        AssertUnavailable(zeroAnchor, InventoryComboPairFinancialReason.AnchorFinancialUnavailable);
        AssertUnavailable(bothZero, InventoryComboPairFinancialReason.TargetFinancialUnavailable);
    }

    [Fact]
    public void Preco_negativo_e_custo_negativo()
    {
        AssertUnavailable(
            Eval(Facts(sale: -3), Facts(id: 2), 20),
            InventoryComboPairFinancialReason.TargetFinancialUnavailable);
        AssertUnavailable(
            Eval(Facts(cost: -2), Facts(id: 2), 20),
            InventoryComboPairFinancialReason.TargetFinancialUnavailable);
        AssertUnavailable(
            Eval(Facts(), Facts(sale: -1, id: 2), 20),
            InventoryComboPairFinancialReason.AnchorFinancialUnavailable);
    }

    [Fact]
    public void CanEvaluate_false_no_target_e_na_ancora()
    {
        AssertUnavailable(
            Eval(Facts(composition: true), Facts(id: 2), 20),
            InventoryComboPairFinancialReason.TargetFinancialUnavailable);
        AssertUnavailable(
            Eval(Facts(), Facts(composition: true, id: 2), 20),
            InventoryComboPairFinancialReason.AnchorFinancialUnavailable);
        AssertUnavailable(
            Eval(Facts(found: false), Facts(id: 2), 20),
            InventoryComboPairFinancialReason.TargetFinancialUnavailable);
        AssertUnavailable(
            Eval(null, Facts(id: 2), 20),
            InventoryComboPairFinancialReason.TargetFinancialUnavailable);
        AssertUnavailable(
            Eval(Facts(), null, 20),
            InventoryComboPairFinancialReason.AnchorFinancialUnavailable);
    }

    [Fact]
    public void Piso_quebrado_reusa_teto_70F()
    {
        const double cost = 10.123;
        const double margin = 30;
        var target = Facts(sale: 20, cost: cost);
        var anchor = Facts(sale: 30, cost: cost, id: 2);
        var result = Eval(target, anchor, margin);
        Assert.Equal(FloorOf(target, margin), result.TargetFloorPrice);
        Assert.Equal(14.47, result.TargetFloorPrice);
        Assert.Equal(PairFloor(cost, cost, margin), result.PairFloorPrice);
        AssertScenariosRespectFloors(result, 30);
    }

    [Fact]
    public void Fonte_nao_acopla_B2_SQL_UI_desconto()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryComboPairFinancialEngine.cs"));
        var model = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Models", "InventoryComboPairFinancial.cs"));
        foreach (var text in new[] { source, model })
        {
            Assert.DoesNotContain("PairTransactions", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ConfidenceTargetToAnchor", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("desconto", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SaleFromCostAndMargin", text, StringComparison.Ordinal);
            Assert.DoesNotContain("esperado", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("adicional", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("ComputeFloor", source, StringComparison.Ordinal);
    }

    static InventoryComboPairFinancialFacts Eval(
        InventoryCommercialFacts? target,
        InventoryCommercialFacts? anchor,
        double? minPercent) =>
        InventoryComboPairFinancialEngine.Evaluate(new InventoryComboPairFinancialInput
        {
            TargetFacts = target,
            AnchorFacts = anchor,
            MinGrossMarginPolicy = new InventoryCommercialMarginPolicy
            {
                MinimumGrossMarginPercent = minPercent,
            },
        });

    static InventoryCommercialFacts Facts(
        double sale = 10,
        double cost = 6,
        bool found = true,
        bool composition = false,
        int id = 1) =>
        InventoryCommercialFactsEngine.Classify(new InventoryCommercialFactsInput
        {
            ProductId = id,
            ProductFound = found,
            CatalogSalePrice = sale,
            CurrentAverageCost = cost,
            IsCompositionProduct = composition,
        });

    static double FloorOf(InventoryCommercialFacts facts, double minPercent) =>
        InventoryCommercialPriceFloorEngine.Evaluate(
            facts,
            new InventoryCommercialMarginPolicy { MinimumGrossMarginPercent = minPercent })
            .MinimumAllowedCatalogPrice!.Value;

    static double PairFloor(double targetCost, double anchorCost, double minPercent)
    {
        Assert.True(InventoryCommercialPriceFloorEngine.TryToDecimal(targetCost, out var t));
        Assert.True(InventoryCommercialPriceFloorEngine.TryToDecimal(anchorCost, out var a));
        Assert.True(InventoryCommercialPriceFloorEngine.TryToDecimal(minPercent, out var m));
        return (double)InventoryCommercialPriceFloorEngine.ComputeFloor(t + a, m);
    }

    static void AssertUnavailable(
        InventoryComboPairFinancialFacts result,
        InventoryComboPairFinancialReason reason)
    {
        Assert.Equal(InventoryComboPairFinancialStatus.Unavailable, result.Status);
        Assert.Equal(reason, result.Reason);
        Assert.Empty(result.Scenarios);
    }

    static void AssertScenariosRespectFloors(
        InventoryComboPairFinancialFacts result,
        double anchorCatalog)
    {
        Assert.NotNull(result.PairFloorPrice);
        Assert.NotNull(result.TargetFloorPrice);
        var pairFloorCents = InventoryCommercialPriceFloorEngine.ToCents(result.PairFloorPrice.Value);
        var targetFloorCents = InventoryCommercialPriceFloorEngine.ToCents(result.TargetFloorPrice.Value);
        var anchorCents = InventoryCommercialPriceFloorEngine.ToCents(MonetaryRounding.Round(anchorCatalog));
        foreach (var scenario in result.Scenarios)
        {
            Assert.True(
                InventoryCommercialPriceFloorEngine.ToCents(scenario.PairPrice) >= pairFloorCents);
            var impliedTargetCents =
                InventoryCommercialPriceFloorEngine.ToCents(scenario.PairPrice) - anchorCents;
            Assert.True(impliedTargetCents >= targetFloorCents);
        }
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
