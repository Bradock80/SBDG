using SGDB.Domain.Products;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 27 — testes diretos + paridade das regras extraídas para o Domain.
/// </summary>
public class ProductSaleQuantityAndWholesaleTests
{
    // ——— StockQuantityForSale (Domain) ———

    [Fact]
    public void StockQuantity_Fator1_DevolveQuantidadeComercial()
    {
        Assert.Equal(3, ProductPriceCalculator.StockQuantityForSale(3, 1));
        Assert.Equal(2.5, ProductPriceCalculator.StockQuantityForSale(2.5, 1));
    }

    [Fact]
    public void StockQuantity_FatorMenorOuIgual1_0001_NaoMultiplica()
    {
        Assert.Equal(5, ProductPriceCalculator.StockQuantityForSale(5, 1.0001));
        Assert.Equal(5, ProductPriceCalculator.StockQuantityForSale(5, 0));
        Assert.Equal(5, ProductPriceCalculator.StockQuantityForSale(5, 0.5));
    }

    [Fact]
    public void StockQuantity_CigarroMaco_Fator20()
    {
        Assert.Equal(20, ProductPriceCalculator.StockQuantityForSale(1, 20));
        Assert.Equal(60, ProductPriceCalculator.StockQuantityForSale(3, 20));
    }

    [Fact]
    public void StockQuantity_ZeroDisplay_ZeroFisico()
    {
        Assert.Equal(0, ProductPriceCalculator.StockQuantityForSale(0, 20));
        Assert.Equal(0, ProductPriceCalculator.StockQuantityForSale(0, 1));
    }

    [Fact]
    public void StockQuantity_Negativo_PreservaComportamentoAtual()
    {
        // Sem clamp — mesmo sinal da implementação original
        Assert.Equal(-40, ProductPriceCalculator.StockQuantityForSale(-2, 20));
        Assert.Equal(-2, ProductPriceCalculator.StockQuantityForSale(-2, 1));
    }

    [Fact]
    public void StockQuantity_Fracionado_Arredonda4Casas()
    {
        // 1.2345 * 20 = 24.69
        Assert.Equal(24.69, ProductPriceCalculator.StockQuantityForSale(1.2345, 20));
        // 1/3 * 20 ≈ 6.666666… → 6.6667 (MidpointRounding default ToEven? Math.Round default)
        // Math.Round(value, 4) uses MidpointRounding.ToEven by default
        var raw = (1.0 / 3.0) * 20;
        Assert.Equal(Math.Round(raw, 4), ProductPriceCalculator.StockQuantityForSale(1.0 / 3.0, 20));
    }

    // ——— WholesaleUnitPrice (Domain) ———

    [Fact]
    public void Wholesale_SemPrecoAtacado_DevolveSalePrice()
    {
        Assert.Equal(10, ProductPriceCalculator.WholesaleUnitPrice(10, 0, 12));
        Assert.Equal(10, ProductPriceCalculator.WholesaleUnitPrice(10, -1, 12));
    }

    [Fact]
    public void Wholesale_AtacadoUnitario_MenorOuIgualVenda()
    {
        // 9 ≤ 10 → unitário
        Assert.Equal(9, ProductPriceCalculator.WholesaleUnitPrice(10, 9, 12));
        // exatamente igual
        Assert.Equal(10, ProductPriceCalculator.WholesaleUnitPrice(10, 10, 12));
        // tolerância 0.009: 10.009 ainda unitário
        Assert.Equal(10.01, ProductPriceCalculator.WholesaleUnitPrice(10, 10.009, 12));
    }

    [Fact]
    public void Wholesale_AtacadoComoTotalDoLote_Divide()
    {
        // 120 > 10 e lote 12 → 10 unitário
        Assert.Equal(10, ProductPriceCalculator.WholesaleUnitPrice(10, 120, 12));
        // 100 / 20 = 5
        Assert.Equal(5, ProductPriceCalculator.WholesaleUnitPrice(8, 100, 20));
    }

    [Fact]
    public void Wholesale_LoteMenorQue2_NaoDivideMesmoSeMaiorQueVenda()
    {
        // precoAtacado 50 > sale 10, mas qtdLote 1 → Round(50)
        Assert.Equal(50, ProductPriceCalculator.WholesaleUnitPrice(10, 50, 1));
        Assert.Equal(50, ProductPriceCalculator.WholesaleUnitPrice(10, 50, 0));
    }

    [Fact]
    public void Wholesale_SalePriceZero_AtacadoPositivo()
    {
        // 0 atacado path: precoAtacado 0 → return salePrice 0
        Assert.Equal(0, ProductPriceCalculator.WholesaleUnitPrice(0, 0, 10));
        // atacado 20 > 0 → divide se lote ≥ 2
        Assert.Equal(2, ProductPriceCalculator.WholesaleUnitPrice(0, 20, 10));
    }

    [Fact]
    public void Wholesale_MidpointAwayFromZero()
    {
        // 10.005 → Round AwayFromZero = 10.01 (MonetaryRounding)
        Assert.Equal(10.01, ProductPriceCalculator.WholesaleUnitPrice(20, 10.005, 1));
        // total lote: 33.335 / 5 = 6.667 → 6.67
        Assert.Equal(6.67, ProductPriceCalculator.WholesaleUnitPrice(1, 33.335, 5));
    }

    // ——— Paridade PdvService ↔ Domain ———

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 20)]
    [InlineData(0, 20)]
    [InlineData(1.5, 10)]
    [InlineData(-1, 20)]
    [InlineData(3, 0)]
    public void Paridade_StockQuantity_PdvServiceIgualDomain(double qty, double factor)
    {
        Assert.Equal(
            ProductPriceCalculator.StockQuantityForSale(qty, factor),
            PdvService.StockQuantityForSale(qty, factor));
    }

    [Theory]
    [InlineData(10, 0, 12)]
    [InlineData(10, 9, 12)]
    [InlineData(10, 10, 12)]
    [InlineData(10, 120, 12)]
    [InlineData(10, 50, 1)]
    [InlineData(8, 100, 20)]
    [InlineData(10, 10.009, 12)]
    public void Paridade_Wholesale_PdvServiceIgualDomain(double sale, double atac, double lote)
    {
        Assert.Equal(
            ProductPriceCalculator.WholesaleUnitPrice(sale, atac, lote),
            PdvService.WholesaleUnitPrice(sale, atac, lote));
    }
}
