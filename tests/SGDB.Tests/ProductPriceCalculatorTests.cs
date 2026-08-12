using SGDB.Domain.Products;

namespace SGDB.Tests;

/// <summary>
/// Testes diretos do núcleo Domain (mesmos cenários caracterizados da fachada).
/// </summary>
public class ProductPriceCalculatorTests
{
    [Fact]
    public void WeightedAverageCost_CasoNormal_100_a_10_mais_20_a_12()
    {
        var result = ProductPriceCalculator.WeightedAverageCost(
            stockBefore: 100,
            costBefore: 10,
            qtyIn: 20,
            costIn: 12);

        Assert.Equal(10.33, result);
    }

    [Fact]
    public void WeightedAverageCost_EstoqueZerado_UsaCustoDaEntrada()
    {
        var result = ProductPriceCalculator.WeightedAverageCost(
            stockBefore: 0,
            costBefore: 99,
            qtyIn: 10,
            costIn: 7.5);

        Assert.Equal(7.5, result);
    }

    [Fact]
    public void WeightedAverageCost_QtyInZero_MantemCustoAnterior()
    {
        var result = ProductPriceCalculator.WeightedAverageCost(
            stockBefore: 50,
            costBefore: 9.99,
            qtyIn: 0,
            costIn: 1);

        Assert.Equal(9.99, result);
    }

    [Fact]
    public void WeightedAverageCost_CustoEntradaNegativo_ClampParaZero()
    {
        var result = ProductPriceCalculator.WeightedAverageCost(
            stockBefore: 10,
            costBefore: 5,
            qtyIn: 5,
            costIn: -2);

        Assert.Equal(3.33, result);
    }

    [Fact]
    public void RemoveFromWeightedAverage_RemocaoParcial()
    {
        var result = ProductPriceCalculator.RemoveFromWeightedAverage(
            stockNow: 100,
            costNow: 10,
            qtyOut: 20,
            costOut: 12);

        Assert.Equal(9.5, result);
    }

    [Fact]
    public void RemoveFromWeightedAverage_RemoveTudo_ZeraCusto()
    {
        var result = ProductPriceCalculator.RemoveFromWeightedAverage(
            stockNow: 20,
            costNow: 10,
            qtyOut: 20,
            costOut: 10);

        Assert.Equal(0, result);
    }

    [Fact]
    public void RoundPrice_AwayFromZero()
    {
        Assert.Equal(1.12, ProductPriceCalculator.RoundPrice(1.115), 2);
        Assert.Equal(1.13, ProductPriceCalculator.RoundPrice(1.125), 2);
    }

    [Fact]
    public void MarginOnSale_CasoNormal()
    {
        // (10 - 7) / 10 × 100 = 30
        Assert.Equal(30, ProductPriceCalculator.MarginOnSale(7, 10));
    }

    [Fact]
    public void MarginOnSale_SaleZero_RetornaZero()
    {
        Assert.Equal(0, ProductPriceCalculator.MarginOnSale(5, 0));
    }

    [Fact]
    public void SaleFromCostAndMargin_CasoNormal()
    {
        // 10 / (1 - 0.2) = 12.5
        Assert.Equal(12.5, ProductPriceCalculator.SaleFromCostAndMargin(10, 20));
    }

    [Fact]
    public void SaleFromCostAndMargin_Margin100_RetornaZero()
    {
        Assert.Equal(0, ProductPriceCalculator.SaleFromCostAndMargin(10, 100));
    }

    [Fact]
    public void CostFromPurchaseAndPercent_CasoNormal()
    {
        // 100 × 1.10 = 110
        Assert.Equal(110, ProductPriceCalculator.CostFromPurchaseAndPercent(100, 10));
    }

    [Fact]
    public void PackCostFromUnit_ComFator()
    {
        Assert.Equal(20, ProductPriceCalculator.PackCostFromUnit(1, 20));
    }

    [Fact]
    public void PackCostFromUnit_SemFator_MantemUnitario()
    {
        Assert.Equal(1.25, ProductPriceCalculator.PackCostFromUnit(1.25, 1));
    }
}
