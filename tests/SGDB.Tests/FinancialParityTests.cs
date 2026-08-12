using SGDB.Domain.Finance;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Paridade Service legado ↔ Domain após extração financeira.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class FinancialParityTests
{
    [Theory]
    [InlineData(100, 2, 0)]
    [InlineData(100, 1.5, 0.5)]
    [InlineData(0, 10, 0.99)]
    [InlineData(10, 1.25, 0)]
    [InlineData(-10, -1, -0.5)]
    public void Helper_And_Domain_FeeAmount_ReturnSameResult(
        double gross, double pct, double fixedFee)
    {
        Assert.Equal(
            FinancialCalculator.CalculateFeeAmount(gross, pct, fixedFee),
            PaymentMethodsService.CalcFeeAmount(gross, pct, fixedFee));
    }

    [Fact]
    public void CalcUnitSurcharge_NullTable_ReturnsZero()
    {
        Assert.Equal(0, PriceTablesService.CalcUnitSurcharge(100, null, "credito"));
    }

    [Fact]
    public void CalcUnitSurcharge_MethodNotTriggered_ReturnsZero()
    {
        using var db = TempDatabase.Create();
        var table = new PriceTable
        {
            Id = 1,
            Description = "T",
            SurchargePercent = 5,
            SurchargeFixed = 1,
            ApplyPaymentMethods = ["credito"],
            Active = true,
        };
        Assert.Equal(0, PriceTablesService.CalcUnitSurcharge(100, table, "dinheiro"));
    }

    [Fact]
    public void CalcUnitSurcharge_Triggered_MatchesDomain()
    {
        using var db = TempDatabase.Create();
        var table = new PriceTable
        {
            Id = 1,
            Description = "T",
            SurchargePercent = 3,
            SurchargeFixed = 1,
            ApplyPaymentMethods = ["pix", "credito"],
            Active = true,
        };
        var expected = FinancialCalculator.CalculateUnitSurcharge(50, 3, 1);
        Assert.Equal(expected, PriceTablesService.CalcUnitSurcharge(50, table, "pix"));
        Assert.Equal(2.5, expected);
    }

    [Fact]
    public void CalcUnitSurcharge_Midpoint_MatchesDomain()
    {
        using var db = TempDatabase.Create();
        var table = new PriceTable
        {
            Id = 1,
            Description = "T",
            SurchargePercent = 1.25,
            SurchargeFixed = 0,
            ApplyPaymentMethods = ["debito"],
            Active = true,
        };
        Assert.Equal(
            FinancialCalculator.CalculateUnitSurcharge(10, 1.25, 0),
            PriceTablesService.CalcUnitSurcharge(10, table, "debito"));
    }
}
