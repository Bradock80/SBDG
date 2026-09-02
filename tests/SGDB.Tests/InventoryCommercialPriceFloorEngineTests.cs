using System.IO;
using SGDB.Domain.Common;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B3 — piso de catálogo para margem bruta sobre venda.
/// Sem SQL, UI, promoção, desconto ou margem default.
/// </summary>
public class InventoryCommercialPriceFloorEngineTests
{
    static InventoryCommercialFacts Facts(
        double sale = 10,
        double cost = 6,
        bool found = true,
        bool allowsSale = true,
        bool composition = false,
        bool cigarette = false,
        double wholesalePrice = 0,
        double wholesaleQty = 0,
        double unitSale = 0,
        int id = 1) =>
        InventoryCommercialFactsEngine.Classify(new InventoryCommercialFactsInput
        {
            ProductId = id,
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

    static InventoryCommercialMarginPolicy Policy(double? percent) =>
        new() { MinimumGrossMarginPercent = percent };

    static InventoryCommercialPriceFloorResult Eval(
        InventoryCommercialFacts? facts,
        double? minPercent) =>
        InventoryCommercialPriceFloorEngine.Evaluate(facts, Policy(minPercent));

    [Fact]
    public void QueryCount_e_zero() =>
        Assert.Equal(0, InventoryCommercialPriceFloorEngine.ExpectedQueryCount);

    [Fact]
    public void Politica_ausente_nao_assume_default()
    {
        var result = InventoryCommercialPriceFloorEngine.Evaluate(Facts(), null);
        Assert.Equal(InventoryCommercialPriceFloorStatus.PolicyMissing, result.Status);
        Assert.Null(result.MinimumGrossMarginPercent);
        Assert.Null(result.MinimumAllowedCatalogPrice);
        Assert.False(result.MeetsMinimumMargin);
        Assert.Equal(InventoryCommercialPriceFloorReason.PolicyMissing, Assert.Single(result.Reasons));
    }

    [Fact]
    public void Politica_com_percentual_nulo_e_ausente()
    {
        var result = InventoryCommercialPriceFloorEngine.Evaluate(Facts(), Policy(null));
        Assert.Equal(InventoryCommercialPriceFloorStatus.PolicyMissing, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Margem_negativa_invalida()
    {
        var result = Eval(Facts(), -1);
        Assert.Equal(InventoryCommercialPriceFloorStatus.PolicyInvalid, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Margem_zero_e_valida()
    {
        var facts = Facts(sale: 12, cost: 10);
        var result = Eval(facts, 0);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, result.Status);
        Assert.Equal(0, result.MinimumGrossMarginPercent);
        Assert.Equal(10, result.MinimumAllowedCatalogPrice);
        Assert.True(result.MeetsMinimumMargin);
        Assert.True(result.CatalogPriceIsAboveMinimumAllowed);
        Assert.Equal(2, result.AmountAboveMinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Margem_positiva_valida()
    {
        var result = Eval(Facts(sale: 20, cost: 10), 40);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, result.Status);
        Assert.Equal(40, result.MinimumGrossMarginPercent);
        Assert.Equal(16.67, result.MinimumAllowedCatalogPrice);
        Assert.True(result.MeetsMinimumMargin);
    }

    [Fact]
    public void Margem_99_99_valida()
    {
        var result = Eval(Facts(sale: 2000, cost: 10), 99.99);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, result.Status);
        Assert.True(result.MinimumAllowedCatalogPrice is double floor && floor >= 10);
        Assert.True(Satisfies(10, result.MinimumAllowedCatalogPrice!.Value, 99.99));
    }

    [Fact]
    public void Margem_100_invalida()
    {
        var result = Eval(Facts(), 100);
        Assert.Equal(InventoryCommercialPriceFloorStatus.PolicyInvalid, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Margem_maior_que_100_invalida()
    {
        var result = Eval(Facts(), 150);
        Assert.Equal(InventoryCommercialPriceFloorStatus.PolicyInvalid, result.Status);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Margem_nao_finita_invalida(double percent)
    {
        var result = Eval(Facts(), percent);
        Assert.Equal(InventoryCommercialPriceFloorStatus.PolicyInvalid, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Custo_desconhecido_nao_calcula_piso()
    {
        var result = Eval(Facts(cost: 0.004), 30);
        Assert.Equal(InventoryCommercialCostQuality.UnknownOrZero, Facts(cost: 0.004).CostQuality);
        Assert.Equal(InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
        Assert.Null(result.CurrentGrossMarginPercent);
    }

    [Fact]
    public void Custo_zero_B2_nao_vira_piso()
    {
        var result = Eval(Facts(cost: 0), 0);
        Assert.Equal(InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Custo_negativo_B2_indisponivel()
    {
        var result = Eval(Facts(cost: -2), 20);
        Assert.Equal(InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable, result.Status);
    }

    [Fact]
    public void Preco_invalido_B2_indisponivel()
    {
        var result = Eval(Facts(sale: -3), 20);
        Assert.Equal(InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable, result.Status);
        Assert.Null(result.CurrentGrossMarginPercent);
    }

    [Fact]
    public void Produto_inexistente()
    {
        var result = Eval(Facts(found: false), 20);
        Assert.Equal(InventoryCommercialPriceFloorStatus.CommercialFactsUnavailable, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Fatos_nulos()
    {
        var result = Eval(null, 20);
        Assert.Equal(InventoryCommercialPriceFloorStatus.CommercialFactsUnavailable, result.Status);
    }

    [Fact]
    public void AllowsSale_false_indisponivel()
    {
        var result = Eval(Facts(allowsSale: false), 20);
        Assert.Equal(InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable, result.Status);
        Assert.Equal(40, result.CurrentGrossMarginPercent);
    }

    [Fact]
    public void Kit_nao_calcula_piso()
    {
        var result = Eval(Facts(composition: true), 20);
        Assert.Equal(InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
        Assert.Equal(40, result.CurrentGrossMarginPercent);
    }

    [Fact]
    public void Cigarro_unidade_ambigua_continua_bloqueado()
    {
        var facts = Facts(cigarette: true, unitSale: 1.2, sale: 24, cost: 18);
        Assert.False(facts.CanEvaluateFinancialScenario);
        var result = Eval(facts, 20);
        Assert.Equal(InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Atacado_completo_nao_substitui_catalogo()
    {
        var facts = Facts(sale: 12, cost: 6, wholesaleQty: 10, wholesalePrice: 9);
        var result = Eval(facts, 40);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, result.Status);
        Assert.Equal(12, result.CatalogSalePrice);
        Assert.NotEqual(9, result.CatalogSalePrice);
        Assert.Equal(10, result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Margem_atual_positiva_usa_autoridade()
    {
        var result = Eval(Facts(sale: 10, cost: 6), 20);
        Assert.Equal(ProductPriceCalculator.MarginOnSale(6, 10), result.CurrentGrossMarginPercent);
        Assert.Equal(40, result.CurrentGrossMarginPercent);
    }

    [Fact]
    public void Margem_atual_zero()
    {
        var result = Eval(Facts(sale: 10, cost: 10), 0);
        Assert.Equal(0, result.CurrentGrossMarginPercent);
        Assert.True(result.MeetsMinimumMargin);
        Assert.False(result.CatalogPriceIsAboveMinimumAllowed);
        Assert.Equal(0, result.AmountAboveMinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Margem_atual_negativa_preco_abaixo_do_custo()
    {
        var result = Eval(Facts(sale: 8, cost: 10), 0);
        Assert.Equal(ProductPriceCalculator.MarginOnSale(10, 8), result.CurrentGrossMarginPercent);
        Assert.True(result.CurrentGrossMarginPercent < 0);
        Assert.False(result.MeetsMinimumMargin);
        Assert.False(result.CatalogPriceIsAboveMinimumAllowed);
        Assert.Equal(0, result.AmountAboveMinimumAllowedCatalogPrice);
        Assert.Equal(
            InventoryCommercialPriceFloorReason.CurrentPriceBelowMinimumMargin,
            Assert.Single(result.Reasons));
        Assert.Equal(10, result.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Preco_atual_acima_do_piso()
    {
        var result = Eval(Facts(sale: 20, cost: 10), 40);
        Assert.True(result.MeetsMinimumMargin);
        Assert.True(result.CatalogPriceIsAboveMinimumAllowed);
        Assert.Equal(3.33, result.AmountAboveMinimumAllowedCatalogPrice);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Preco_atual_igual_ao_piso()
    {
        var result = Eval(Facts(sale: 10, cost: 10), 0);
        Assert.True(result.MeetsMinimumMargin);
        Assert.False(result.CatalogPriceIsAboveMinimumAllowed);
        Assert.Equal(0, result.AmountAboveMinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Preco_atual_abaixo_do_piso_espaco_zero()
    {
        var result = Eval(Facts(sale: 11, cost: 10), 40);
        Assert.False(result.MeetsMinimumMargin);
        Assert.False(result.CatalogPriceIsAboveMinimumAllowed);
        Assert.Equal(0, result.AmountAboveMinimumAllowedCatalogPrice);
        Assert.True(result.AmountAboveMinimumAllowedCatalogPrice >= 0);
    }

    [Fact]
    public void Espaco_nunca_negativo()
    {
        var result = Eval(Facts(sale: 1, cost: 50), 50);
        Assert.Equal(0, result.AmountAboveMinimumAllowedCatalogPrice);
        Assert.False(result.CatalogPriceIsAboveMinimumAllowed);
    }

    [Fact]
    public void AwayFromZero_pode_violar_politica_teto_corrige()
    {
        const double cost = 10.123;
        const double margin = 30;
        var domain = ProductPriceCalculator.SaleFromCostAndMargin(cost, margin);
        Assert.Equal(14.46, domain);
        Assert.False(Satisfies(cost, domain, margin));

        var result = Eval(Facts(sale: 20, cost: cost), margin);
        Assert.Equal(14.47, result.MinimumAllowedCatalogPrice);
        Assert.True(Satisfies(cost, result.MinimumAllowedCatalogPrice!.Value, margin));
        Assert.True(result.MinimumAllowedCatalogPrice > domain);
    }

    [Fact]
    public void Piso_arredondado_ainda_satisfaz_politica()
    {
        var result = Eval(Facts(sale: 50, cost: 7.77), 33.33);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, result.Status);
        Assert.True(Satisfies(7.77, result.MinimumAllowedCatalogPrice!.Value, 33.33));
        Assert.Equal(
            MonetaryRounding.CeilingToCents(
                7.77m / (1m - 33.33m / 100m)),
            (decimal)result.MinimumAllowedCatalogPrice.Value);
    }

    [Fact]
    public void CurrentMargin_usa_ProductPriceCalculator()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialPriceFloorEngine.cs"));
        Assert.Contains("ProductPriceCalculator.MarginOnSale", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaleFromCostAndMargin", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Custo_nao_e_substituido_e_promo_nao_aparece()
    {
        var source = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialPriceFloorEngine.cs"));
        var model = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Models", "InventoryCommercialPriceFloor.cs"));
        foreach (var text in new[] { source, model })
        {
            Assert.DoesNotContain("preco_compra", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("preco_promocional", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RecommendedDiscount", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PromotionPrice", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SuggestedPromotion", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Enums_sem_semantica_de_promocao()
    {
        var names = Enum.GetNames<InventoryCommercialPriceFloorStatus>()
            .Concat(Enum.GetNames<InventoryCommercialPriceFloorReason>())
            .ToArray();
        foreach (var name in names)
        {
            Assert.DoesNotContain("Promot", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Discount", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Recommend", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Apply", name, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(
            "HasPriceReductionRoom",
            typeof(InventoryCommercialPriceFloorResult).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(
            "ReductionRoomAmount",
            typeof(InventoryCommercialPriceFloorResult).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void Deterministico_e_sem_mutacao()
    {
        var facts = Facts(sale: 18, cost: 10, wholesaleQty: 6, wholesalePrice: 15);
        var original = facts.LimitationReasons.Count;
        var a = Eval(facts, 25);
        var b = Eval(facts, 25);
        Assert.Equal(a.Status, b.Status);
        Assert.Equal(a.MinimumAllowedCatalogPrice, b.MinimumAllowedCatalogPrice);
        Assert.Equal(a.CurrentGrossMarginPercent, b.CurrentGrossMarginPercent);
        Assert.Equal(a.AmountAboveMinimumAllowedCatalogPrice, b.AmountAboveMinimumAllowedCatalogPrice);
        Assert.Equal(original, facts.LimitationReasons.Count);
        Assert.True(facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Politica_diferente_produz_piso_diferente()
    {
        var facts = Facts(sale: 20, cost: 10);
        var low = Eval(facts, 10);
        var high = Eval(facts, 40);
        Assert.True(high.MinimumAllowedCatalogPrice > low.MinimumAllowedCatalogPrice);
        Assert.Equal(11.12, low.MinimumAllowedCatalogPrice);
        Assert.Equal(16.67, high.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Margem_30_somente_quando_fornecida()
    {
        var facts = Facts(sale: 20, cost: 10);
        var missing = InventoryCommercialPriceFloorEngine.Evaluate(facts, null);
        var thirty = Eval(facts, 30);
        Assert.Null(missing.MinimumAllowedCatalogPrice);
        Assert.Equal(14.29, thirty.MinimumAllowedCatalogPrice);
        Assert.NotEqual(thirty.MinimumAllowedCatalogPrice, missing.MinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Centavos_e_custos_pequenos()
    {
        var result = Eval(Facts(sale: 0.05, cost: 0.01), 50);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, result.Status);
        Assert.Equal(0.02, result.MinimumAllowedCatalogPrice);
        Assert.True(Satisfies(0.01, 0.02, 50));
        Assert.True(result.MeetsMinimumMargin);
    }

    [Fact]
    public void Valores_monetarios_grandes()
    {
        var result = Eval(Facts(sale: 1_000_000, cost: 400_000), 20);
        Assert.Equal(InventoryCommercialPriceFloorStatus.Available, result.Status);
        Assert.Equal(500_000, result.MinimumAllowedCatalogPrice);
        Assert.True(result.MeetsMinimumMargin);
        Assert.Equal(500_000, result.AmountAboveMinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Custo_igual_ao_preco_com_politica_positiva_fica_abaixo()
    {
        var result = Eval(Facts(sale: 10, cost: 10), 10);
        Assert.False(result.MeetsMinimumMargin);
        Assert.Equal(11.12, result.MinimumAllowedCatalogPrice);
        Assert.Equal(0, result.AmountAboveMinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Tolerancia_decimal_preco_ja_em_centavos()
    {
        var result = Eval(Facts(sale: 16.67, cost: 10), 40);
        Assert.True(result.MeetsMinimumMargin);
        Assert.False(result.CatalogPriceIsAboveMinimumAllowed);
        Assert.Equal(0, result.AmountAboveMinimumAllowedCatalogPrice);
    }

    [Fact]
    public void Politica_valida_com_CanEvaluate_false_indisponivel()
    {
        var result = Eval(Facts(composition: true, sale: 20, cost: 10), 30);
        Assert.False(Facts(composition: true).CanEvaluateFinancialScenario);
        Assert.Equal(InventoryCommercialPriceFloorStatus.FinancialScenarioUnavailable, result.Status);
        Assert.Null(result.MinimumAllowedCatalogPrice);
        Assert.Equal(30, result.MinimumGrossMarginPercent);
        Assert.Equal(50, result.CurrentGrossMarginPercent);
    }

    [Fact]
    public void CeilingToCents_nao_altera_Round()
    {
        Assert.Equal(14.46, MonetaryRounding.Round(14.461428571));
        Assert.Equal(14.47m, MonetaryRounding.CeilingToCents(14.461428571m));
        Assert.Equal(10.00m, MonetaryRounding.CeilingToCents(10.000m));
        Assert.Equal(0m, MonetaryRounding.CeilingToCents(0m));
    }

    [Fact]
    public void B1_e_B2_nao_referenciam_piso()
    {
        var b1 = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialEligibilityEngine.cs"));
        var b2 = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Services", "InventoryCommercialFactsEngine.cs"));
        Assert.DoesNotContain("PriceFloor", b1, StringComparison.Ordinal);
        Assert.DoesNotContain("PriceFloor", b2, StringComparison.Ordinal);
        Assert.DoesNotContain("MinimumGrossMargin", b1, StringComparison.Ordinal);
        Assert.DoesNotContain("MinimumGrossMargin", b2, StringComparison.Ordinal);
    }

    [Fact]
    public void Budget_futuro_continua_8()
    {
        Assert.Equal(
            8,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialEligibilityEngine.ExpectedQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialPriceFloorEngine.ExpectedQueryCount);
    }

    static bool Satisfies(double cost, double sale, double minPercent) =>
        sale > 0 && (sale - cost) / sale * 100 + 1e-9 >= minPercent;

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
