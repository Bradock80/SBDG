using SGDB.Domain.Products;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// Garante que a fachada App e o Domain permanecem alinhados durante a migração.
/// </summary>
public class ProductPriceParityTests
{
    [Fact]
    public void Helper_And_Domain_WeightedAverageCost_ReturnSameResult()
    {
        Assert.Equal(
            ProductPriceHelper.WeightedAverageCost(100, 10, 20, 12),
            ProductPriceCalculator.WeightedAverageCost(100, 10, 20, 12));
        Assert.Equal(
            ProductPriceHelper.WeightedAverageCost(0, 99, 10, 7.5),
            ProductPriceCalculator.WeightedAverageCost(0, 99, 10, 7.5));
        Assert.Equal(
            ProductPriceHelper.WeightedAverageCost(10, 5, 5, -2),
            ProductPriceCalculator.WeightedAverageCost(10, 5, 5, -2));
    }

    [Fact]
    public void Helper_And_Domain_RemoveFromWeightedAverage_ReturnSameResult()
    {
        Assert.Equal(
            ProductPriceHelper.RemoveFromWeightedAverage(100, 10, 20, 12),
            ProductPriceCalculator.RemoveFromWeightedAverage(100, 10, 20, 12));
        Assert.Equal(
            ProductPriceHelper.RemoveFromWeightedAverage(20, 10, 20, 10),
            ProductPriceCalculator.RemoveFromWeightedAverage(20, 10, 20, 10));
    }

    [Fact]
    public void Helper_And_Domain_RoundPrice_ReturnSameResult()
    {
        Assert.Equal(ProductPriceHelper.RoundPrice(1.115), ProductPriceCalculator.RoundPrice(1.115));
        Assert.Equal(ProductPriceHelper.RoundPrice(1.125), ProductPriceCalculator.RoundPrice(1.125));
    }

    [Fact]
    public void Helper_And_Domain_MarginAndMarkup_ReturnSameResult()
    {
        Assert.Equal(
            ProductPriceHelper.MarginOnSale(7, 10),
            ProductPriceCalculator.MarginOnSale(7, 10));
        Assert.Equal(
            ProductPriceHelper.SaleFromCostAndMargin(10, 20),
            ProductPriceCalculator.SaleFromCostAndMargin(10, 20));
        Assert.Equal(
            ProductPriceHelper.CostFromPurchaseAndPercent(100, 10),
            ProductPriceCalculator.CostFromPurchaseAndPercent(100, 10));
        Assert.Equal(
            ProductPriceHelper.PackCostFromUnit(1, 20),
            ProductPriceCalculator.PackCostFromUnit(1, 20));
    }
}
