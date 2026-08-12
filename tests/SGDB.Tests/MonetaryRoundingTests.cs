using SGDB.Domain.Common;
using SGDB.Domain.Finance;
using SGDB.Domain.Products;

namespace SGDB.Tests;

public class MonetaryRoundingTests
{
    [Fact]
    public void Round_ValorComum()
    {
        Assert.Equal(10.25, MonetaryRounding.Round(10.25));
    }

    [Fact]
    public void Round_Midpoint_UsesAwayFromZero()
    {
        Assert.Equal(1.13, MonetaryRounding.Round(1.125));
        Assert.NotEqual(
            Math.Round(1.125, 2, MidpointRounding.ToEven),
            MonetaryRounding.Round(1.125));
    }

    [Fact]
    public void Round_Zero()
    {
        Assert.Equal(0, MonetaryRounding.Round(0));
    }

    [Fact]
    public void Round_Negativo_AwayFromZero()
    {
        // AwayFromZero em midpoint negativo: -1.125 → -1.13
        Assert.Equal(-1.13, MonetaryRounding.Round(-1.125));
    }

    [Fact]
    public void Round_JaComDuasCasas()
    {
        Assert.Equal(9.99, MonetaryRounding.Round(9.99));
    }

    [Theory]
    [InlineData(1.125)]
    [InlineData(0)]
    [InlineData(10.25)]
    [InlineData(-1.125)]
    [InlineData(0.125)]
    public void ProductPriceCalculator_RoundPrice_Equals_MonetaryRounding(double value)
    {
        Assert.Equal(MonetaryRounding.Round(value), ProductPriceCalculator.RoundPrice(value));
    }

    [Fact]
    public void FinancialCalculator_Fee_ContinuaUsandoPoliticaComum()
    {
        // 10 × 1.25% = 0.125 → AwayFromZero → 0.13
        Assert.Equal(0.13, FinancialCalculator.CalculateFeeAmount(10, 1.25));
        Assert.Equal(MonetaryRounding.Round(0.125), FinancialCalculator.CalculateFeeAmount(10, 1.25));
    }
}
