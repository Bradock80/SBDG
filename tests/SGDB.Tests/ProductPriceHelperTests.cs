using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// Caracterização do comportamento atual de ProductPriceHelper.
/// Não redefine regras — documenta a implementação existente.
/// </summary>
public class ProductPriceHelperTests
{
    [Fact]
    public void WeightedAverageCost_CasoNormal_100_a_10_mais_20_a_12()
    {
        // (100×10 + 20×12) / 120 = 1240/120 = 10.333... → RoundPrice AwayFromZero → 10.33
        var result = ProductPriceHelper.WeightedAverageCost(
            stockBefore: 100,
            costBefore: 10,
            qtyIn: 20,
            costIn: 12);

        Assert.Equal(10.33, result);
    }

    [Fact]
    public void WeightedAverageCost_EstoqueZerado_UsaCustoDaEntrada()
    {
        var result = ProductPriceHelper.WeightedAverageCost(
            stockBefore: 0,
            costBefore: 99,
            qtyIn: 10,
            costIn: 7.5);

        Assert.Equal(7.5, result);
    }

    [Fact]
    public void WeightedAverageCost_EstoqueNegativo_TrataComoZero_UsaCustoEntrada()
    {
        var result = ProductPriceHelper.WeightedAverageCost(
            stockBefore: -5,
            costBefore: 50,
            qtyIn: 8,
            costIn: 4);

        Assert.Equal(4, result);
    }

    [Fact]
    public void WeightedAverageCost_QtyInZero_MantemCustoAnterior()
    {
        var result = ProductPriceHelper.WeightedAverageCost(
            stockBefore: 50,
            costBefore: 9.99,
            qtyIn: 0,
            costIn: 1);

        Assert.Equal(9.99, result);
    }

    [Fact]
    public void WeightedAverageCost_CustoEntradaNegativo_ClampParaZero()
    {
        // incoming = Max(0, costIn) = 0; (10×5 + 5×0)/15 = 50/15 ≈ 3.333 → 3.33
        var result = ProductPriceHelper.WeightedAverageCost(
            stockBefore: 10,
            costBefore: 5,
            qtyIn: 5,
            costIn: -2);

        Assert.Equal(3.33, result);
    }

    [Fact]
    public void RemoveFromWeightedAverage_RemocaoParcial()
    {
        // stock 100 @ 10, remove 20 @ 12 → keptValue = 1000 - 240 = 760; remaining 80 → 9.5
        var result = ProductPriceHelper.RemoveFromWeightedAverage(
            stockNow: 100,
            costNow: 10,
            qtyOut: 20,
            costOut: 12);

        Assert.Equal(9.5, result);
    }

    [Fact]
    public void RemoveFromWeightedAverage_RemoveTudo_ZeraCusto()
    {
        var result = ProductPriceHelper.RemoveFromWeightedAverage(
            stockNow: 20,
            costNow: 10,
            qtyOut: 20,
            costOut: 10);

        Assert.Equal(0, result);
    }

    [Fact]
    public void RemoveFromWeightedAverage_QtyOutZero_MantemCusto()
    {
        var result = ProductPriceHelper.RemoveFromWeightedAverage(
            stockNow: 30,
            costNow: 8.25,
            qtyOut: 0,
            costOut: 99);

        Assert.Equal(8.25, result);
    }

    [Fact]
    public void RemoveFromWeightedAverage_KeptValueNegativo_ClampParaZero()
    {
        // stock 10 @ 5 = 50; remove 5 @ 20 = 100 → keptValue clamp 0 → 0
        var result = ProductPriceHelper.RemoveFromWeightedAverage(
            stockNow: 10,
            costNow: 5,
            qtyOut: 5,
            costOut: 20);

        Assert.Equal(0, result);
    }

    [Fact]
    public void RoundPrice_AwayFromZero()
    {
        // Math.Round(..., 2, AwayFromZero) sobre double: documenta resultado real.
        Assert.Equal(1.12, ProductPriceHelper.RoundPrice(1.115), 2);
        Assert.Equal(1.13, ProductPriceHelper.RoundPrice(1.125), 2);
    }
}
