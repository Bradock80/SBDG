using SGDB.Domain.Common;
using SGDB.Domain.Finance;

namespace SGDB.Tests;

public class InstallmentCalculatorTests
{
    private static readonly DateTime FixedBase = new(2026, 8, 11);
    private static readonly DateTime FirstDue = new(2026, 9, 1);

    [Fact]
    public void Generate_OneInstallment_ReturnsTotal()
    {
        var plan = InstallmentCalculator.Generate(100, 0, 1, FirstDue, 30, false, FixedBase);

        Assert.Single(plan);
        Assert.Equal(100, plan[0].Amount);
        Assert.Equal(FirstDue, plan[0].DueDate);
        Assert.Equal("Boleto", plan[0].ChargeType);
    }

    [Fact]
    public void Generate_TwoInstallments_EqualSplit()
    {
        var plan = InstallmentCalculator.Generate(100, 0, 2, FirstDue, 30, false, FixedBase);

        Assert.Equal(2, plan.Count);
        Assert.Equal(50, plan[0].Amount);
        Assert.Equal(50, plan[1].Amount);
        Assert.Equal(100, plan.Sum(p => p.Amount));
    }

    [Fact]
    public void Generate_ThreeInstallments_PreservesTotal()
    {
        // 100/3 → 33.33, 33.33, 33.34
        var plan = InstallmentCalculator.Generate(100, 0, 3, FirstDue, 1, true, FixedBase);

        Assert.Equal(3, plan.Count);
        Assert.Equal(33.33, plan[0].Amount);
        Assert.Equal(33.33, plan[1].Amount);
        Assert.Equal(33.34, plan[2].Amount);
        Assert.Equal(100, plan.Sum(p => p.Amount));
    }

    [Theory]
    [InlineData(100, 3)]
    [InlineData(10, 6)]
    [InlineData(1, 3)]
    public void Generate_SumOfInstallments_EqualsTotal_WhenNoDownPayment(double total, int count)
    {
        var plan = InstallmentCalculator.Generate(total, 0, count, FirstDue, 30, false, FixedBase);
        Assert.Equal(MonetaryRounding.Round(total), MonetaryRounding.Round(plan.Sum(p => p.Amount)));
    }

    [Fact]
    public void Generate_WithDownPayment_UsesBaseDateAndDinheiro()
    {
        var plan = InstallmentCalculator.Generate(100, 20, 2, FirstDue, 30, false, FixedBase);

        Assert.Equal(3, plan.Count);
        Assert.Equal(FixedBase, plan[0].DueDate);
        Assert.Equal("Dinheiro", plan[0].ChargeType);
        Assert.Equal(20, plan[0].Amount);
        Assert.Equal("Boleto", plan[1].ChargeType);
        Assert.Equal(40, plan[1].Amount);
        Assert.Equal(40, plan[2].Amount);
        Assert.Equal(100, plan.Sum(p => p.Amount));
    }

    [Fact]
    public void Generate_ZeroTotal_ProducesZeroAmounts()
    {
        var plan = InstallmentCalculator.Generate(0, 0, 3, FirstDue, 30, false, FixedBase);
        Assert.All(plan, p => Assert.Equal(0, p.Amount));
        Assert.Equal(0, plan.Sum(p => p.Amount));
    }

    [Fact]
    public void Generate_CountZeroOrNegative_ClampsToOne()
    {
        var a = InstallmentCalculator.Generate(50, 0, 0, FirstDue, 30, false, FixedBase);
        var b = InstallmentCalculator.Generate(50, 0, -5, FirstDue, 30, false, FixedBase);
        Assert.Single(a);
        Assert.Single(b);
        Assert.Equal(50, a[0].Amount);
        Assert.Equal(50, b[0].Amount);
    }

    [Fact]
    public void Generate_IntervalClampedToAtLeastOne()
    {
        var plan = InstallmentCalculator.Generate(30, 0, 2, FirstDue, 0, false, FixedBase);
        Assert.Equal(FirstDue, plan[0].DueDate);
        Assert.Equal(FirstDue.AddDays(1), plan[1].DueDate);
    }

    [Fact]
    public void Generate_WithFixedBaseDate_IsDeterministic()
    {
        var a = InstallmentCalculator.Generate(99.99, 10, 3, FirstDue, 1, true, FixedBase);
        var b = InstallmentCalculator.Generate(99.99, 10, 3, FirstDue, 1, true, FixedBase);

        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].DueDate, b[i].DueDate);
            Assert.Equal(a[i].Amount, b[i].Amount);
            Assert.Equal(a[i].ChargeType, b[i].ChargeType);
        }
    }

    [Fact]
    public void Generate_DailyInterval_AddsDays()
    {
        var start = new DateTime(2026, 8, 11);
        var plan = InstallmentCalculator.Generate(90, 0, 3, start, 10, false, FixedBase);

        Assert.Equal(new DateTime(2026, 8, 11), plan[0].DueDate);
        Assert.Equal(new DateTime(2026, 8, 21), plan[1].DueDate);
        Assert.Equal(new DateTime(2026, 8, 31), plan[2].DueDate);
    }

    [Fact]
    public void Generate_Monthly_FromJanuary31_UsesDotNetDateRule()
    {
        var jan31 = new DateTime(2026, 1, 31);
        var plan = InstallmentCalculator.Generate(90, 0, 3, jan31, 1, true, FixedBase);

        Assert.Equal(new DateTime(2026, 1, 31), plan[0].DueDate);
        // .NET AddMonths: 31/01 + 1 mês → 28/02/2026 (não bissexto)
        Assert.Equal(new DateTime(2026, 2, 28), plan[1].DueDate);
        Assert.Equal(jan31.AddMonths(1), plan[1].DueDate);
        Assert.Equal(new DateTime(2026, 3, 31), plan[2].DueDate);
    }

    [Fact]
    public void Generate_Monthly_LeapYearFebruary()
    {
        var jan31 = new DateTime(2024, 1, 31);
        var plan = InstallmentCalculator.Generate(60, 0, 2, jan31, 1, true, FixedBase);

        Assert.Equal(new DateTime(2024, 1, 31), plan[0].DueDate);
        Assert.Equal(new DateTime(2024, 2, 29), plan[1].DueDate);
    }

    [Fact]
    public void Generate_TinyDownPayment_Ignored()
    {
        // entrada <= 0.001 não gera linha de entrada
        var plan = InstallmentCalculator.Generate(100, 0.001, 1, FirstDue, 30, false, FixedBase);
        Assert.Single(plan);
        Assert.Equal("Boleto", plan[0].ChargeType);
    }

    [Fact]
    public void Generate_DoesNotUseSystemClock()
    {
        var past = new DateTime(2000, 1, 15);
        var plan = InstallmentCalculator.Generate(50, 10, 1, new DateTime(2000, 2, 1), 1, true, past);
        Assert.Equal(past, plan[0].DueDate);
        Assert.NotEqual(DateTime.Today, plan[0].DueDate);
    }
}
