using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>ETAPA 69E-B2 — helper de leitura de CMV (snapshot vs legado estimado).</summary>
public class HistoricalSaleCostRulesTests
{
    [Fact]
    public void Snapshot_NaoUsaCostPriceAtual()
    {
        var extra = new ProductExtra { FatorEmbalagem = 20 };
        var line = HistoricalSaleCostRules.ResolveLine(
            2, costAtSale: 5, catalogCost: 99, unitSalePrice: 8, "AGUA", "Bebidas", extra);
        Assert.True(line.IsHistorical);
        Assert.False(line.IsEstimated);
        Assert.Equal(5, line.UnitCost);
        Assert.Equal(10, line.TotalCost);
    }

    [Fact]
    public void Snapshot_NaoUsaUnitCostForSoldLine()
    {
        var extra = new ProductExtra { FatorEmbalagem = 20 };
        var converted = ProductPriceHelper.UnitCostForSoldLine(20, 1.50, extra, "Rothmans Blue", "Cigarros");
        Assert.NotEqual(0.50, converted);

        var line = HistoricalSaleCostRules.ResolveLine(
            5, 0.50, catalogCost: 20, unitSalePrice: 1.50, "Rothmans Blue", "Cigarros", extra);
        Assert.True(line.IsHistorical);
        Assert.Equal(0.50, line.UnitCost);
        Assert.Equal(2.50, ProductPriceHelper.RoundPrice(line.TotalCost));
        Assert.NotEqual(converted, line.UnitCost);
    }

    [Fact]
    public void Legado_UsaFallbackEstimado()
    {
        var extra = new ProductExtra { FatorEmbalagem = 20 };
        var expected = ProductPriceHelper.UnitCostForSoldLine(10, 8, extra, "AGUA", "Bebidas");
        var line = HistoricalSaleCostRules.ResolveLine(
            3, null, 10, 8, "AGUA", "Bebidas", extra);
        Assert.False(line.IsHistorical);
        Assert.True(line.IsEstimated);
        Assert.Equal(expected, line.UnitCost);
        Assert.Equal(3 * expected, line.TotalCost);
    }

    [Fact]
    public void Legado_MarcadoEstimado()
    {
        var line = HistoricalSaleCostRules.ResolveLine(1, null, 6, 8, "X", null, new ProductExtra());
        Assert.True(line.IsEstimated);
        Assert.False(line.IsHistorical);
    }

    [Fact]
    public void CustoZeroSnapshot_EHistoricoNaoLegado()
    {
        var line = HistoricalSaleCostRules.ResolveLine(2, 0, catalogCost: 9, 8, "BRINDE", null, new ProductExtra());
        Assert.True(line.IsHistorical);
        Assert.False(line.IsEstimated);
        Assert.Equal(0, line.UnitCost);
        Assert.Equal(0, line.TotalCost);
    }

    [Fact]
    public void Null_ELegado()
    {
        var line = HistoricalSaleCostRules.ResolveLine(1, null, 5, 8, "X", null, new ProductExtra());
        Assert.True(line.IsEstimated);
        Assert.Equal(5, line.UnitCost);
    }

    [Fact]
    public void Pack_SnapshotNaoReconverte()
    {
        var line = HistoricalSaleCostRules.ResolveLine(
            2, 120, catalogCost: 5, 120, "FARDO 24", "Bebidas", new ProductExtra());
        Assert.True(line.IsHistorical);
        Assert.Equal(240, line.TotalCost);
    }

    [Fact]
    public void Fracionado_PreservaDouble()
    {
        var line = HistoricalSaleCostRules.ResolveLine(
            2.5, 10, catalogCost: 99, 12, "AÇÚCAR KG", "Mercearia", new ProductExtra());
        Assert.Equal(25, line.TotalCost);
        Assert.True(line.IsHistorical);
    }

    [Fact]
    public void PeriodoMisto_HasEstimated()
    {
        var period = HistoricalSaleCostRules.Sum(
        [
            HistoricalSaleCostRules.ResolveLine(1, 5, 9, 8, "A", null, new ProductExtra()),
            HistoricalSaleCostRules.ResolveLine(1, null, 6, 8, "B", null, new ProductExtra()),
        ]);
        Assert.Equal(5, period.Historical);
        Assert.Equal(6, period.EstimatedLegacy);
        Assert.Equal(11, period.Total);
        Assert.True(period.HasEstimatedLegacyCost);
        Assert.True(period.HasHistoricalCost);
        Assert.True(period.ProfitIsEstimated);
        Assert.True(period.MarginIsEstimated);
        Assert.Equal(HistoricalSaleCostRules.EstimatedLegacyPeriodNote, period.ReliabilityNote);
    }

    [Fact]
    public void PeriodoSoSnapshot_SemAviso()
    {
        var period = HistoricalSaleCostRules.Sum(
        [
            HistoricalSaleCostRules.ResolveLine(1, 5, 9, 8, "A", null, new ProductExtra()),
        ]);
        Assert.False(period.HasEstimatedLegacyCost);
        Assert.Null(period.ReliabilityNote);
        Assert.Equal(5, period.Total);
    }
}
