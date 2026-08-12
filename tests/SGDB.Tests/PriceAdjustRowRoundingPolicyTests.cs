using SGDB.Domain.Products;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 12 — PriceAdjustRow setters/LoadPrices usam AwayFromZero.
/// </summary>
public class PriceAdjustRowRoundingPolicyTests
{
    [Fact]
    public void PurchasePrice_Midpoint_UsaAwayFromZero()
    {
        var row = new PriceAdjustRow
        {
            ProductId = 1,
            Code = "T",
            Name = "T",
            CostPercent = 0,
            MarginPercent = 0,
            SalePrice = 0,
        };
        row.LoadPrices(0, 0, 0, 0);
        row.PurchasePrice = 1.125;

        Assert.Equal(1.13, row.PurchasePrice);
        Assert.Equal(ProductPriceCalculator.RoundPrice(1.125), row.PurchasePrice);
        Assert.NotEqual(Math.Round(1.125, 2, MidpointRounding.ToEven), row.PurchasePrice);
    }

    [Fact]
    public void CostAndSale_Normal_NaoMidpoint_MantemValor()
    {
        var row = new PriceAdjustRow
        {
            ProductId = 1,
            Code = "T",
            Name = "T",
            CostPercent = 0,
            MarginPercent = 0,
            SalePrice = 10,
        };
        row.LoadPrices(0, 0, 0, 0);
        row.CostPrice = 7.5;
        row.NewSalePrice = 10.25;

        Assert.Equal(7.5, row.CostPrice);
        Assert.Equal(10.25, row.NewSalePrice);
    }

    [Fact]
    public void LoadPrices_Midpoint_UsaAwayFromZero()
    {
        var row = new PriceAdjustRow
        {
            ProductId = 1,
            Code = "T",
            Name = "T",
            CostPercent = 0,
            MarginPercent = 0,
            SalePrice = 1.125,
        };
        row.LoadPrices(1.125, 2.225, 10.125, 3.125);

        Assert.Equal(1.13, row.PurchasePrice);
        Assert.Equal(2.23, row.CostPrice);
        Assert.Equal(10.13, row.NewMarginPercent);
        Assert.Equal(3.13, row.NewSalePrice);
        Assert.Equal(1.13, row.OriginalSalePrice);
    }

    [Fact]
    public void PurchasePrice_Zero_Permite()
    {
        var row = new PriceAdjustRow
        {
            ProductId = 1,
            Code = "T",
            Name = "T",
            CostPercent = 0,
            MarginPercent = 0,
            SalePrice = 0,
        };
        row.LoadPrices(5, 5, 0, 0);
        row.PurchasePrice = 0;
        Assert.Equal(0, row.PurchasePrice);
    }
}
