using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 11.1 — Caracteriza MidpointRounding após consolidação.
/// Antes (ETAPA 11): Math.Round(x, 2) → ToEven (padrão).
/// Depois: ProductPriceCalculator.RoundPrice → AwayFromZero.
/// Não altera produção; apenas documenta diferenças reais no runtime .NET.
/// </summary>
public class RoundingMidpointAuditTests
{
    private static double RoundToEven(double value) =>
        Math.Round(value, 2, MidpointRounding.ToEven);

    private static double RoundAway(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    [Fact]
    public void RoundPrice_PoliticaOficial_E_AwayFromZero()
    {
        Assert.Equal(RoundAway(1.125), ProductPriceCalculator.RoundPrice(1.125));
        Assert.NotEqual(RoundToEven(1.125), ProductPriceCalculator.RoundPrice(1.125));
        Assert.Equal(1.12, RoundToEven(1.125));
        Assert.Equal(1.13, ProductPriceCalculator.RoundPrice(1.125));
    }

    [Fact]
    public void RoundPrice_MetodosDoDomain_DependemDeAwayFromZero()
    {
        // CostFromPurchaseAndPercent(10.125, 0) = RoundPrice(10.125)
        Assert.Equal(1.13, ProductPriceCalculator.CostFromPurchaseAndPercent(1.125, 0));
        Assert.Equal(1.12, RoundToEven(1.125));

        // MarginOnSale: (1.6 - 0.03)/1.6*100 = 98.125
        Assert.Equal(98.13, ProductPriceCalculator.MarginOnSale(0.03, 1.6));
        Assert.Equal(98.12, RoundToEven((1.6 - 0.03) / 1.6 * 100.0));

        // SaleFromCostAndMargin: 0.05 / (1 - 0.60) = 0.125
        Assert.Equal(0.13, ProductPriceCalculator.SaleFromCostAndMargin(0.05, 60));
        Assert.Equal(0.12, RoundToEven(0.05 / (1.0 - 0.60)));
    }

    [Theory]
    [InlineData(0.03, 1.6, 98.12, 98.13)]
    [InlineData(0.07, 0.32, 78.12, 78.13)]
    [InlineData(0.11, 0.32, 65.62, 65.63)]
    public void MarginOnSale_Midpoint_AntigoToEven_Vs_DomainAway(
        double cost, double sale, double antigoToEven, double novoAway)
    {
        var raw = (sale - cost) / sale * 100.0;
        Assert.Equal(antigoToEven, RoundToEven(raw));
        Assert.Equal(novoAway, ProductPriceCalculator.MarginOnSale(cost, sale));
        Assert.Equal(novoAway, PriceAdjustRow.MarginOnSale(cost, sale));
        Assert.Equal(novoAway, PriceAdjustService.MarginOnSale(cost, sale));
        Assert.NotEqual(antigoToEven, novoAway);
    }

    [Theory]
    [InlineData(0.10, 25, 0.12, 0.13)]
    [InlineData(1.125, 0, 1.12, 1.13)]
    [InlineData(0.25, 6, 0.26, 0.27)]
    public void CostFromPurchaseAndPercent_Midpoint_AntigoToEven_Vs_DomainAway(
        double purchase, double pct, double antigoToEven, double novoAway)
    {
        var raw = purchase * (1.0 + pct / 100.0);
        Assert.Equal(antigoToEven, RoundToEven(raw));
        Assert.Equal(novoAway, ProductPriceCalculator.CostFromPurchaseAndPercent(purchase, pct));
        Assert.NotEqual(antigoToEven, novoAway);
    }

    [Theory]
    [InlineData(0.05, 60, 0.12, 0.13)]
    [InlineData(0.03, 76, 0.12, 0.13)]
    [InlineData(0.01, 60, 0.02, 0.03)]
    public void SaleFromCostAndMargin_Midpoint_AntigoToEven_Vs_DomainAway(
        double cost, double margin, double antigoToEven, double novoAway)
    {
        var raw = cost / (1.0 - margin / 100.0);
        Assert.Equal(antigoToEven, RoundToEven(raw));
        Assert.Equal(novoAway, ProductPriceCalculator.SaleFromCostAndMargin(cost, margin));
        Assert.Equal(novoAway, PriceAdjustRow.SaleFromMargin(cost, margin));
        Assert.Equal(novoAway, PriceAdjustService.SaleFromMargin(cost, margin));
        Assert.NotEqual(antigoToEven, novoAway);
    }

    [Fact]
    public void PlainRound_1_125_DifferToEvenVsAway()
    {
        // PriceAdjustService / Recalc usam RoundPrice em valores já em 2 casas
        // quando o input cai exatamente em *.xx5 (ex.: 1.125).
        Assert.Equal(1.12, RoundToEven(1.125));
        Assert.Equal(1.13, ProductPriceCalculator.RoundPrice(1.125));
    }
}
