using SGDB.Domain.Commercial;

namespace SGDB.Tests;

/// <summary>71B-B8B — Hamilton em centavos, puro, sem SQL.</summary>
public class HamiltonCentsAllocatorTests
{
    [Fact]
    public void QueryCount_allocator_e_zero()
    {
        Assert.Equal(0, CommercialGoalHeaderAdjustmentAllocator.OwnQueryCount);
        Assert.Equal(0, CommercialGoalProductContributionSnapshot.OwnQueryCount);
        Assert.Equal(1, CommercialGoalProductContributionSnapshot.ExpectedQueryCount);
    }

    [Fact]
    public void Desconto_60_40()
    {
        var lines = Lines((1, 10, 60m), (2, 20, 40m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(90m, lines);
        Assert.Equal([5400, 3600], got);
        Assert.Equal(9000, got.Sum());
    }

    [Fact]
    public void Acrescimo_60_40()
    {
        var lines = Lines((1, 10, 60m), (2, 20, 40m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(110m, lines);
        Assert.Equal([6600, 4400], got);
        Assert.Equal(11000, got.Sum());
    }

    [Fact]
    public void Residuo_negativo_vai_ao_maior_peso()
    {
        var lines = Lines((1, 10, 10m), (2, 20, 20m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(29.99m, lines);
        Assert.Equal([1000, 1999], got);
        Assert.Equal(2999, got.Sum());
    }

    [Fact]
    public void Residuo_positivo_vai_ao_maior_peso()
    {
        var lines = Lines((1, 10, 10m), (2, 20, 20m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(30.01m, lines);
        Assert.Equal([1000, 2001], got);
        Assert.Equal(3001, got.Sum());
    }

    [Fact]
    public void Desempate_sale_item_id_menor_leva_centavo()
    {
        var lines = Lines((8, 50, 10m), (3, 40, 10m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(19.99m, lines);
        Assert.Equal(1999, got.Sum());
        Assert.Equal(1000, got[0]);
        Assert.Equal(999, got[1]);
    }

    [Fact]
    public void Desempate_product_id_quando_sale_item_id_igual()
    {
        var shares = new HamiltonShare[]
        {
            new(100, 7, 20),
            new(100, 7, 5),
        };
        var got = HamiltonCentsAllocator.Allocate(-1, shares);
        Assert.Equal([0, -1], got);
    }

    [Fact]
    public void Subtotal_zero_nao_participa_quando_ha_base_positiva()
    {
        var lines = Lines((1, 1, 10m), (2, 2, 0m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(9m, lines);
        Assert.Equal([900, 0], got);
    }

    [Fact]
    public void Todas_bases_zero_total_zero()
    {
        var lines = Lines((5, 1, 0m), (2, 2, 0m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(0m, lines);
        Assert.Equal([0, 0], got);
    }

    [Fact]
    public void Todas_bases_zero_total_nao_zero_menor_sale_item_id()
    {
        var lines = Lines((9, 1, 0m), (4, 2, 0m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(5m, lines);
        Assert.Equal([0, 500], got);
        Assert.Equal(500, got.Sum());
    }

    [Fact]
    public void Desconto_100_por_cento()
    {
        var lines = Lines((1, 1, 60m), (2, 2, 40m));
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(0m, lines);
        Assert.Equal([0, 0], got);
    }

    [Fact]
    public void Um_item_sem_ajuste()
    {
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(
            10m, Lines((1, 1, 10m)));
        Assert.Equal([1000], got);
    }

    [Fact]
    public void Dois_itens_sem_ajuste()
    {
        var got = CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(
            100m, Lines((1, 1, 60m), (2, 2, 40m)));
        Assert.Equal([6000, 4000], got);
    }

    [Fact]
    public void Nulo_rejeitado()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CommercialGoalHeaderAdjustmentAllocator.AllocateAttributedCents(1m, null!));
        Assert.Throws<ArgumentNullException>(() =>
            HamiltonCentsAllocator.Allocate(1, null!));
    }

    static CommercialGoalHeaderAdjustmentLine[] Lines(
        params (int SaleItemId, int ProductId, decimal Subtotal)[] rows)
    {
        var list = new CommercialGoalHeaderAdjustmentLine[rows.Length];
        for (var i = 0; i < rows.Length; i++)
            list[i] = new CommercialGoalHeaderAdjustmentLine(rows[i].SaleItemId, rows[i].ProductId, rows[i].Subtotal);
        return list;
    }
}
