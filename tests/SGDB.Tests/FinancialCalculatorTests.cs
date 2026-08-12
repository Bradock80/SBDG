using SGDB.Domain.Finance;
using SGDB.Domain.Products;

namespace SGDB.Tests;

public class FinancialCalculatorTests
{
    [Fact]
    public void CalculateFee_PercentOnly()
    {
        // 100 × 2% = 2
        Assert.Equal(2, FinancialCalculator.CalculateFeeAmount(100, 2));
    }

    [Fact]
    public void CalculateFee_FixedOnly()
    {
        Assert.Equal(1.5, FinancialCalculator.CalculateFeeAmount(100, 0, 1.5));
    }

    [Fact]
    public void CalculateFee_PercentAndFixed()
    {
        // 100 × 1.5% + 0.50 = 2.00
        Assert.Equal(2, FinancialCalculator.CalculateFeeAmount(100, 1.5, 0.5));
    }

    [Fact]
    public void CalculateFee_ZeroGross_ReturnsFixedOnly()
    {
        Assert.Equal(0.99, FinancialCalculator.CalculateFeeAmount(0, 10, 0.99));
    }

    [Fact]
    public void CalculateFee_NegativeInputs_ClampToZero()
    {
        Assert.Equal(0, FinancialCalculator.CalculateFeeAmount(-50, -2, -1));
    }

    [Fact]
    public void CalculateFee_Midpoint_UsesAwayFromZero()
    {
        // 1.25% de 10 = 0.125 → AwayFromZero → 0.13
        Assert.Equal(0.13, FinancialCalculator.CalculateFeeAmount(10, 1.25));
        Assert.Equal(0.13, ProductPriceCalculator.RoundPrice(0.125));
        Assert.NotEqual(0.12, FinancialCalculator.CalculateFeeAmount(10, 1.25));
    }

    [Fact]
    public void CalculateUnitSurcharge_PercentOnly()
    {
        Assert.Equal(2, FinancialCalculator.CalculateUnitSurcharge(100, 2, 0));
    }

    [Fact]
    public void CalculateUnitSurcharge_FixedOnly()
    {
        Assert.Equal(0.5, FinancialCalculator.CalculateUnitSurcharge(100, 0, 0.5));
    }

    [Fact]
    public void CalculateUnitSurcharge_PercentAndFixed()
    {
        // 50 × 3% + 1 = 2.5
        Assert.Equal(2.5, FinancialCalculator.CalculateUnitSurcharge(50, 3, 1));
    }

    [Fact]
    public void CalculateUnitSurcharge_BaseZero_ReturnsFixed()
    {
        Assert.Equal(1.25, FinancialCalculator.CalculateUnitSurcharge(0, 10, 1.25));
    }

    [Fact]
    public void CalculateUnitSurcharge_Midpoint_UsesAwayFromZero()
    {
        // 10 × 1.25% = 0.125 → 0.13
        Assert.Equal(0.13, FinancialCalculator.CalculateUnitSurcharge(10, 1.25, 0));
    }
}
