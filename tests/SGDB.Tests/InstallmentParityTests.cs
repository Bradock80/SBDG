using System.Globalization;
using SGDB.Domain.Finance;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// Paridade: PurchaseFinanceHelper.GenerateParcelas ≡ Domain + formatação BR + TodayBrDate.
/// </summary>
public class InstallmentParityTests
{
    [Theory]
    [InlineData(100, 0, 1, "01/09/2026", 30, false)]
    [InlineData(100, 0, 2, "01/09/2026", 30, false)]
    [InlineData(100, 0, 3, "01/09/2026", 1, true)]
    [InlineData(100, 20, 2, "15/09/2026", 30, false)]
    [InlineData(10, 0, 6, "11/08/2026", 15, false)]
    [InlineData(1, 0, 3, "31/01/2026", 1, true)]
    [InlineData(99.99, 10.50, 4, "28/02/2026", 1, true)]
    [InlineData(0, 0, 3, "01/10/2026", 30, false)]
    public void Helper_Matches_Domain_WithTodayAsBaseDate(
        double total,
        double entrada,
        int qtd,
        string primeiroVencBr,
        int intervalo,
        bool emMeses)
    {
        var helper = PurchaseFinanceHelper.GenerateParcelas(
            total, entrada, qtd, primeiroVencBr, intervalo, emMeses);

        Assert.True(DateBrHelper.TryParseBr(primeiroVencBr, out var firstDue));
        var domain = InstallmentCalculator.Generate(
            total, entrada, qtd, firstDue, intervalo, emMeses, DateBrHelper.TodayBrDate());

        Assert.Equal(domain.Count, helper.Count);
        for (var i = 0; i < domain.Count; i++)
        {
            Assert.Equal(
                domain[i].DueDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR")),
                helper[i].Vencimento);
            Assert.Equal(domain[i].ChargeType, helper[i].Tipo);
            Assert.Equal(domain[i].Amount, helper[i].Valor);
        }
    }

    [Fact]
    public void Helper_InvalidFirstDue_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PurchaseFinanceHelper.GenerateParcelas(100, 0, 1, "", 30, false));
        Assert.Contains("1º vencimento", ex.Message);
    }

    [Fact]
    public void Helper_ThreeInstallments_Centavos_MatchesLegacyExpectation()
    {
        var helper = PurchaseFinanceHelper.GenerateParcelas(100, 0, 3, "01/09/2026", 1, true);
        Assert.Equal(3, helper.Count);
        Assert.Equal(33.33, helper[0].Valor);
        Assert.Equal(33.33, helper[1].Valor);
        Assert.Equal(33.34, helper[2].Valor);
        Assert.Equal(100, helper.Sum(p => p.Valor));
    }

    [Fact]
    public void Helper_WithEntrada_FirstRowIsTodayBr()
    {
        var helper = PurchaseFinanceHelper.GenerateParcelas(100, 25, 1, "01/09/2026", 30, false);
        Assert.Equal(2, helper.Count);
        Assert.Equal(DateBrHelper.TodayBr(), helper[0].Vencimento);
        Assert.Equal("Dinheiro", helper[0].Tipo);
        Assert.Equal(25, helper[0].Valor);
    }
}
