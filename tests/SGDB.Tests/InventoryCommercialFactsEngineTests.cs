using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70F-B2 — classificador puro de fatos comerciais. Sem SQLite, UI, promoção ou PDV.
/// </summary>
public class InventoryCommercialFactsEngineTests
{
    static InventoryCommercialFactsInput Simple(
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
        new()
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
        };

    [Fact]
    public void Preco_e_custo_positivos_permitem_cenario_financeiro()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple());
        Assert.True(facts.ProductFound);
        Assert.Equal(10, facts.CatalogSalePrice);
        Assert.Equal(6, facts.CurrentAverageCost);
        Assert.Equal(InventoryCommercialPriceQuality.Usable, facts.PriceQuality);
        Assert.Equal(InventoryCommercialCostQuality.Known, facts.CostQuality);
        Assert.True(facts.CanEvaluateFinancialScenario);
        Assert.True(facts.AllowsSale);
        Assert.Empty(facts.LimitationReasons);
    }

    [Fact]
    public void Custo_zero_nao_e_conhecido()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(cost: 0));
        Assert.Equal(0, facts.CurrentAverageCost);
        Assert.Equal(InventoryCommercialCostQuality.UnknownOrZero, facts.CostQuality);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.UnknownCost, facts.LimitationReasons);
    }

    [Fact]
    public void Custo_poeira_monetaria_tambem_e_desconhecido()
    {
        var facts = InventoryCommercialFactsEngine.Classify(
            Simple(cost: InventoryCommercialFactsEngine.MoneyEpsilon));
        Assert.Equal(InventoryCommercialCostQuality.UnknownOrZero, facts.CostQuality);
        Assert.False(facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Custo_negativo_e_invalido()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(cost: -1.5));
        Assert.Equal(-1.5, facts.CurrentAverageCost);
        Assert.Equal(InventoryCommercialCostQuality.Invalid, facts.CostQuality);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.InvalidCost, facts.LimitationReasons);
    }

    [Fact]
    public void Preco_zero_nao_e_utilizavel()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(sale: 0));
        Assert.Equal(0, facts.CatalogSalePrice);
        Assert.Equal(InventoryCommercialPriceQuality.Unusable, facts.PriceQuality);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.UnusableSalePrice, facts.LimitationReasons);
    }

    [Fact]
    public void Preco_negativo_e_invalido_e_nao_vira_zero()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(sale: -4));
        Assert.Equal(-4, facts.CatalogSalePrice);
        Assert.Equal(InventoryCommercialPriceQuality.Invalid, facts.PriceQuality);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.InvalidSalePrice, facts.LimitationReasons);
    }

    [Fact]
    public void Produto_inexistente_e_Unavailable()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(found: false, sale: 10, cost: 6));
        Assert.False(facts.ProductFound);
        Assert.Null(facts.CatalogSalePrice);
        Assert.Null(facts.CurrentAverageCost);
        Assert.Equal(InventoryCommercialPriceQuality.Unavailable, facts.PriceQuality);
        Assert.Equal(InventoryCommercialCostQuality.Unavailable, facts.CostQuality);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Equal(InventoryCommercialFactsReason.MissingProduct, Assert.Single(facts.LimitationReasons));
    }

    [Fact]
    public void AllowsSale_false_bloqueia_cenario_financeiro()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(allowsSale: false));
        Assert.False(facts.AllowsSale);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.SaleNotAllowed, facts.LimitationReasons);
    }

    [Fact]
    public void Atacado_completo_e_contexto_nao_substitui_preco_base()
    {
        var facts = InventoryCommercialFactsEngine.Classify(
            Simple(wholesaleQty: 10, wholesalePrice: 9));
        Assert.True(facts.HasWholesalePricing);
        Assert.Equal(10, facts.WholesaleMinimumQuantity);
        Assert.Equal(9, facts.WholesalePrice);
        Assert.Equal(10, facts.CatalogSalePrice);
        Assert.True(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.WholesalePricingConfigured, facts.LimitationReasons);
        Assert.DoesNotContain(InventoryCommercialFactsReason.IncompleteWholesalePricing, facts.LimitationReasons);
    }

    [Fact]
    public void Atacado_incompleto_nao_bloqueia_por_si()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(wholesaleQty: 10, wholesalePrice: 0));
        Assert.False(facts.HasWholesalePricing);
        Assert.Contains(InventoryCommercialFactsReason.IncompleteWholesalePricing, facts.LimitationReasons);
        Assert.True(facts.CanEvaluateFinancialScenario);
        Assert.Equal(10, facts.CatalogSalePrice);
    }

    [Fact]
    public void Cigarro_avulso_marca_unidade_ambigua_e_nao_substitui_catalogo()
    {
        var facts = InventoryCommercialFactsEngine.Classify(
            Simple(cigarette: true, unitSale: 1.2, sale: 24, cost: 18));
        Assert.True(facts.IsCigaretteProduct);
        Assert.True(facts.HasUnitSalePricing);
        Assert.Equal(1.2, facts.UnitSalePrice);
        Assert.Equal(24, facts.CatalogSalePrice);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.AmbiguousSaleUnit, facts.LimitationReasons);
        Assert.Contains(InventoryCommercialFactsReason.UnitSalePricingConfigured, facts.LimitationReasons);
    }

    [Fact]
    public void Cigarro_sem_avulso_catalogo_de_maco_permanece_avaliavel()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(cigarette: true, sale: 24, cost: 18));
        Assert.True(facts.IsCigaretteProduct);
        Assert.False(facts.HasUnitSalePricing);
        Assert.True(facts.CanEvaluateFinancialScenario);
        Assert.DoesNotContain(InventoryCommercialFactsReason.AmbiguousSaleUnit, facts.LimitationReasons);
        Assert.True(facts.HasSpecialPricingContext);
    }

    [Fact]
    public void Composto_nao_avalia_financeiro_nem_inventa_bom()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(composition: true));
        Assert.True(facts.IsCompositionProduct);
        Assert.False(facts.CanEvaluateFinancialScenario);
        Assert.Contains(InventoryCommercialFactsReason.CompositionProduct, facts.LimitationReasons);
        Assert.Equal(6, facts.CurrentAverageCost);
    }

    [Fact]
    public void Produto_normal_sem_contexto_especial()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple());
        Assert.False(facts.HasSpecialPricingContext);
        Assert.False(facts.HasWholesalePricing);
        Assert.False(facts.HasUnitSalePricing);
        Assert.False(facts.IsCigaretteProduct);
        Assert.False(facts.IsCompositionProduct);
    }

    [Fact]
    public void Custo_nao_finito_e_invalido()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(cost: double.NaN));
        Assert.Equal(InventoryCommercialCostQuality.Invalid, facts.CostQuality);
        Assert.False(facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Preco_nao_finito_e_invalido()
    {
        var facts = InventoryCommercialFactsEngine.Classify(Simple(sale: double.PositiveInfinity));
        Assert.Equal(InventoryCommercialPriceQuality.Invalid, facts.PriceQuality);
        Assert.False(facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Arredondamento_nao_altera_fatos_crus()
    {
        var raw = 10.123;
        var facts = InventoryCommercialFactsEngine.Classify(Simple(sale: raw, cost: 6.789));
        Assert.Equal(raw, facts.CatalogSalePrice);
        Assert.Equal(6.789, facts.CurrentAverageCost);
        Assert.NotEqual(ProductPriceCalculator.RoundPrice(raw), facts.CatalogSalePrice);
    }

    [Fact]
    public void Deterministico()
    {
        var input = Simple(wholesaleQty: 6, wholesalePrice: 8.5);
        var a = InventoryCommercialFactsEngine.Classify(input);
        var b = InventoryCommercialFactsEngine.Classify(input);
        Assert.Equal(a.CanEvaluateFinancialScenario, b.CanEvaluateFinancialScenario);
        Assert.Equal(a.CostQuality, b.CostQuality);
        Assert.Equal(a.PriceQuality, b.PriceQuality);
        Assert.Equal(a.LimitationReasons, b.LimitationReasons);
    }

    [Fact]
    public void Candidate_B1_nao_e_o_mesmo_eixo_que_cenario_financeiro()
    {
        Assert.NotEqual(
            nameof(InventoryCommercialEligibilityKind.CommercialCandidate),
            nameof(InventoryCommercialFacts.CanEvaluateFinancialScenario));
        var facts = InventoryCommercialFactsEngine.Classify(Simple(cost: 0));
        Assert.False(facts.CanEvaluateFinancialScenario);
    }

    [Fact]
    public void Enums_nao_tem_promocao_nem_execucao()
    {
        var names = Enum.GetNames<InventoryCommercialCostQuality>()
            .Concat(Enum.GetNames<InventoryCommercialPriceQuality>())
            .Concat(Enum.GetNames<InventoryCommercialFactsReason>())
            .ToArray();
        foreach (var name in names)
        {
            Assert.DoesNotContain("Promot", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Discount", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Recommend", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Floor", name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
