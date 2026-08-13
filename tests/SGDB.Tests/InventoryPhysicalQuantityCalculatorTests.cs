using SGDB.Domain.Inventory;

namespace SGDB.Tests;

public class InventoryPhysicalQuantityCalculatorTests
{
    [Fact]
    public void Calculate_3_maços_7_avulsos_fator20_retorna_67()
    {
        Assert.Equal(67, InventoryPhysicalQuantityCalculator.Calculate(3, 7, 20));
    }

    [Fact]
    public void Calculate_0_maços_7_avulsos_retorna_7()
    {
        Assert.Equal(7, InventoryPhysicalQuantityCalculator.Calculate(0, 7, 20));
    }

    [Fact]
    public void Calculate_3_maços_0_avulsos_retorna_60()
    {
        Assert.Equal(60, InventoryPhysicalQuantityCalculator.Calculate(3, 0, 20));
    }

    [Fact]
    public void Calculate_fator25_2_maços_5_avulsos_retorna_55()
    {
        Assert.Equal(55, InventoryPhysicalQuantityCalculator.Calculate(2, 5, 25));
    }

    [Fact]
    public void Calculate_0_mais_0_retorna_0()
    {
        Assert.Equal(0, InventoryPhysicalQuantityCalculator.Calculate(0, 0, 20));
    }

    [Fact]
    public void Split_2182_fator20_retorna_109_maços_2_avulsos()
    {
        var split = InventoryPhysicalQuantityCalculator.SplitPhysicalQuantity(2182, 20);
        Assert.Equal(109, split.Packs);
        Assert.Equal(2, split.Loose);
    }

    [Fact]
    public void Split_67_fator20_retorna_3_maços_7_avulsos()
    {
        var split = InventoryPhysicalQuantityCalculator.SplitPhysicalQuantity(67, 20);
        Assert.Equal(3, split.Packs);
        Assert.Equal(7, split.Loose);
    }

    [Fact]
    public void Normalize_1_maço_25_avulsos_fator20_vira_2_e_5()
    {
        var n = InventoryPhysicalQuantityCalculator.Normalize(1, 25, 20);
        Assert.Equal(2, n.Packs);
        Assert.Equal(5, n.Loose);
        Assert.Equal(45, InventoryPhysicalQuantityCalculator.Calculate(1, 25, 20));
    }

    [Fact]
    public void Normalize_0_maços_40_avulsos_fator20_vira_2_e_0()
    {
        var n = InventoryPhysicalQuantityCalculator.Normalize(0, 40, 20);
        Assert.Equal(2, n.Packs);
        Assert.Equal(0, n.Loose);
        Assert.Equal(40, InventoryPhysicalQuantityCalculator.Calculate(0, 40, 20));
    }

    [Fact]
    public void Calculate_maços_negativo_lanca()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryPhysicalQuantityCalculator.Calculate(-1, 0, 20));
    }

    [Fact]
    public void Calculate_avulsos_negativo_lanca()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryPhysicalQuantityCalculator.Calculate(0, -1, 20));
    }

    [Fact]
    public void Calculate_fator_menor_que_2_lanca()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryPhysicalQuantityCalculator.Calculate(1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryPhysicalQuantityCalculator.Calculate(1, 0, 0));
    }

    [Fact]
    public void Calculate_overflow_lanca()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryPhysicalQuantityCalculator.Calculate(
                InventoryPhysicalQuantityCalculator.MaxPhysicalQuantity, 0, 20));
    }

    [Fact]
    public void TryResolveFactor_menor_que_2_retorna_false()
    {
        Assert.False(InventoryPhysicalQuantityCalculator.TryResolveFactor(1, out _));
        Assert.False(InventoryPhysicalQuantityCalculator.TryResolveFactor(0, out _));
        Assert.False(InventoryPhysicalQuantityCalculator.TryResolveFactor(1.5, out _));
    }

    [Fact]
    public void TryResolveFactor_20_retorna_true()
    {
        Assert.True(InventoryPhysicalQuantityCalculator.TryResolveFactor(20, out var f));
        Assert.Equal(20, f);
    }

    [Fact]
    public void Split_decimal_lanca()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryPhysicalQuantityCalculator.SplitPhysicalQuantity(67.5, 20));
    }

    [Fact]
    public void IsWholeNumber_detecta_inteiro_e_decimal()
    {
        Assert.True(InventoryPhysicalQuantityCalculator.IsWholeNumber(67));
        Assert.True(InventoryPhysicalQuantityCalculator.IsWholeNumber(2182.0));
        Assert.False(InventoryPhysicalQuantityCalculator.IsWholeNumber(67.5));
    }
}
