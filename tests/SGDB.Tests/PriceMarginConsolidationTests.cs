using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// Consolida margem/preço: consumidores devem bater com o Domain (AwayFromZero).
/// Edges de SaleFromMargin (margem≤0 / ≥100%) são locais e documentados.
/// </summary>
public class PriceMarginConsolidationTests
{
    [Theory]
    [InlineData(7, 10, 30)]
    [InlineData(0, 10, 100)]
    [InlineData(5, 0, 0)]
    [InlineData(1, 3, 66.67)]
    public void PriceAdjustRow_Margin_UsaMesmoResultadoDoDomain(double cost, double sale, double expected)
    {
        Assert.Equal(expected, PriceAdjustRow.MarginOnSale(cost, sale));
        Assert.Equal(ProductPriceCalculator.MarginOnSale(cost, sale), PriceAdjustRow.MarginOnSale(cost, sale));
    }

    [Theory]
    [InlineData(10, 20, 12.5)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 100, 10)]
    [InlineData(0, 20, 0)]
    public void PriceAdjustRow_SaleFromMargin_PreservaEdgesLocais(double cost, double margin, double expected)
    {
        Assert.Equal(expected, PriceAdjustRow.SaleFromMargin(cost, margin));
    }

    [Fact]
    public void PriceAdjustRow_SaleFromMargin_FaixaNormal_DelegaAoDomain()
    {
        Assert.Equal(
            ProductPriceCalculator.SaleFromCostAndMargin(10, 20),
            PriceAdjustRow.SaleFromMargin(10, 20));
    }

    [Fact]
    public void PriceAdjustRow_RecalcFromPurchase_UsaCustoDoDomain()
    {
        var row = new PriceAdjustRow
        {
            ProductId = 1,
            Code = "X",
            Name = "TESTE",
            CostPercent = 10,
            MarginPercent = 20,
            SalePrice = 12.5,
        };
        // PurchasePrice setter dispara RecalcFromPurchase (LoadPrices não).
        row.LoadPrices(purchase: 0, cost: 0, newMargin: 20, newSale: 12.5);
        row.PurchasePrice = 100;

        Assert.Equal(
            ProductPriceCalculator.CostFromPurchaseAndPercent(100, 10),
            row.CostPrice);
        Assert.Equal(
            ProductPriceCalculator.SaleFromCostAndMargin(row.CostPrice, 20),
            row.NewSalePrice);
    }

    [Theory]
    [InlineData(7, 10)]
    [InlineData(1, 3)]
    [InlineData(5, 0)]
    public void PriceAdjustService_Margin_UsaMesmoResultadoDoDomain(double cost, double sale)
    {
        Assert.Equal(
            ProductPriceCalculator.MarginOnSale(cost, sale),
            PriceAdjustService.MarginOnSale(cost, sale));
    }

    [Fact]
    public void PriceAdjustService_SaleFromMargin_Margem100_MantemCusto_NaoZeroDoDomain()
    {
        // Domain devolve 0; Service/Model locais devolvem o custo.
        Assert.Equal(0, ProductPriceCalculator.SaleFromCostAndMargin(10, 100));
        Assert.Equal(10, PriceAdjustService.SaleFromMargin(10, 100));
        Assert.Equal(10, PriceAdjustRow.SaleFromMargin(10, 100));
    }

    [Fact]
    public void PriceAdjustService_SaleFromMargin_FaixaNormal_DelegaAoDomain()
    {
        Assert.Equal(
            ProductPriceCalculator.SaleFromCostAndMargin(8, 25),
            PriceAdjustService.SaleFromMargin(8, 25));
    }

    [Fact]
    public void ProductService_MargemViaDomain_ParidadeComCalculator()
    {
        // Normalize grava LucroPercent via ProductPriceCalculator.MarginOnSale.
        Assert.Equal(30, ProductPriceCalculator.MarginOnSale(7, 10));
        Assert.Equal(66.67, ProductPriceCalculator.MarginOnSale(1, 3));
    }
}
