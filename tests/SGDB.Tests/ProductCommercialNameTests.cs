using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// Nome comercial na importação XML: tira logística (CX/UN/C/23),
/// mantém marca, sabor, tipo e volume da unidade vendida.
/// </summary>
public class ProductCommercialNameTests
{
    [Theory]
    [InlineData(
        "CERVEJA ANTARCTICA PILSEN 300ML CX C/23 UN",
        "ANTARCTICA PILSEN 300 ML")]
    [InlineData(
        "COCA COLA ORIGINAL 2L PET CX C/6 UN",
        "COCA-COLA ORIGINAL 2 L")]
    [InlineData(
        "REFRIGERANTE GUARANA ANTARCTICA 350ML LATA FD 12 UN",
        "GUARANÁ ANTARCTICA 350 ML LATA")]
    [InlineData(
        "HEINEKEN LONG NECK 330ML CX 24UN",
        "HEINEKEN LONG NECK 330 ML")]
    [InlineData(
        "ANTARCTICA 300ML CX C/23 UN",
        "ANTARCTICA 300 ML")]
    [InlineData(
        "CERVEJA ANTARCTICA PILSEN GARRAFA RETORNAVEL 300ML CX C/23 UNIDADES",
        "ANTARCTICA PILSEN GARRAFA RETORNAVEL 300 ML")]
    [InlineData(
        "SKOL PILSEN 350ML LATA CX C/12 UN",
        "SKOL PILSEN 350 ML LATA")]
    [InlineData(
        "COCA COLA ZERO 600ML PET FARDO C/12",
        "COCA-COLA ZERO 600 ML")]
    [InlineData(
        "AGUA CRYSTAL SEM GAS 500ML CX 12 UN",
        "AGUA CRYSTAL SEM GAS 500 ML")]
    [InlineData(
        "BRAHMA CHOPP 1L PET CX C/6 UN",
        "BRAHMA CHOPP 1 L")]
    [InlineData(
        "PRODUTO XYZ 500G PCT C/12 UN",
        "PRODUTO XYZ 500 G")]
    [InlineData(
        "PRODUTO XYZ 500G PACOTE C/10 UN",
        "PRODUTO XYZ 500 G")]
    [InlineData(
        "PRODUTO XYZ 500G PACOTE 10 UN",
        "PRODUTO XYZ 500 G")]
    [InlineData(
        "PRODUTO XYZ 500G PCT 20UN",
        "PRODUTO XYZ 500 G")]
    [InlineData(
        "PRODUTO XYZ 500G PCT C/20 UN",
        "PRODUTO XYZ 500 G")]
    [InlineData(
        "PRODUTO XYZ 1KG PCTE C/10",
        "PRODUTO XYZ 1 KG")]
    [InlineData(
        "CHOCOLATE LACTA 20G PCT. C/12 UN",
        "CHOCOLATE LACTA 20 G")]
    [InlineData(
        "BISCOITO MARILAN 40G PAC 10 UN",
        "BISCOITO MARILAN 40 G")]
    public void NormalizeCommercialName_TiraLogistica_MantemMarcaVolume(string xProd, string expected)
    {
        var actual = ProductClassificationHelper.NormalizeCommercialName(xProd);

        Assert.Equal(expected, actual.ToUpperInvariant());
        Assert.DoesNotMatch(@"\bCX\b", actual);
        Assert.DoesNotMatch(@"C/\s*\d+", actual);
        Assert.DoesNotMatch(@"\b(?:UN|UND|UNID|UNIDADE|UNIDADES|FD|FARDO|PACOTE|PCTE|PCT|PAC)\b", actual);
    }

    [Fact]
    public void NormalizeCommercialName_NaoRemovePctPacDentroDePalavra()
    {
        var impacto = ProductClassificationHelper.NormalizeCommercialName(
            "BISCOITO IMPACTO CHOCOLATE 40G");
        Assert.Contains("IMPACTO", impacto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("40 G", impacto, StringComparison.OrdinalIgnoreCase);

        var pacoca = ProductClassificationHelper.NormalizeCommercialName("PACOCA ROLHA 20G");
        Assert.Contains("PACOCA", pacoca, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 G", pacoca, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeCommercialName_NaoRemoveSaborNemTipoDaUnidade()
    {
        var actual = ProductClassificationHelper.NormalizeCommercialName(
            "CERVEJA HEINEKEN LAGER 330ML LONG NECK CX C/24 UN");

        Assert.Contains("HEINEKEN", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LAGER", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LONG NECK", actual, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("330 ML", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CX", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InferPackFactor_ContinuaLendoQuantidadeDaEmbalagemNoXProd()
    {
        Assert.Equal(23, NfeXmlImportService.InferPackFactorFromProductName(
            "CERVEJA ANTARCTICA PILSEN 300ML CX C/23 UN"));
        Assert.Equal(6, NfeXmlImportService.InferPackFactorFromProductName(
            "COCA COLA ORIGINAL 2L PET CX C/6 UN"));
        Assert.Equal(12, NfeXmlImportService.InferPackFactorFromProductName(
            "REFRIGERANTE GUARANA ANTARCTICA 350ML LATA FD 12 UN"));
        Assert.Equal(12, NfeXmlImportService.InferPackFactorFromProductName(
            "PRODUTO XYZ 500G PCT C/12 UN"));
        Assert.Equal(10, NfeXmlImportService.InferPackFactorFromProductName(
            "PRODUTO XYZ 500G PACOTE C/10 UN"));
        Assert.Equal(20, NfeXmlImportService.InferPackFactorFromProductName(
            "PRODUTO XYZ 500G PCT 20UN"));
    }
}

[Collection(TempDatabaseCollection.Name)]
public class ProductCommercialNamePersistenceTests
{
    [Fact]
    public void Create_ProdutoNovoDoXml_NaoGravaCxUnNoCadastro()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");

        var created = ProductService.Create(new ProductInput
        {
            Code = "XMLANT",
            Name = "CERVEJA ANTARCTICA PILSEN 300ML CX C/23 UN",
            Unit = "UN",
            CostPrice = 5.50,
            SalePrice = 9,
        });

        Assert.Equal("ANTARCTICA PILSEN 300 ML", created.Name);
        Assert.DoesNotContain("CX", created.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C/23", created.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("300 ML", created.Name, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("ANTARCTICA 300ML CX C/23 UN", created.Name);
    }

    [Fact]
    public void EnsureCleanCatalogName_Xml_NaoSobrescreveNomeComercialExistente()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(
            10, 8, 5, code: "ANT1", name: "ANTARCTICA 300 ML");
        var product = ProductService.GetById(id)!;

        var after = ProductService.EnsureCleanCatalogName(
            product,
            "CERVEJA ANTARCTICA PILSEN GARRAFA RETORNAVEL 300ML CX C/23 UNIDADES");

        Assert.Equal("ANTARCTICA 300 ML", after.Name);
        Assert.Equal("ANTARCTICA 300 ML", ProductService.GetById(id)!.Name);
    }

    [Fact]
    public void Create_ProdutoNovoDoXml_TiraPacotePctDoNomeComercial()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");

        var created = ProductService.Create(new ProductInput
        {
            Code = "XMLPCT",
            Name = "PRODUTO XYZ 500G PCT C/12 UN",
            Unit = "UN",
            CostPrice = 2,
            SalePrice = 4,
        });

        Assert.Equal("PRODUTO XYZ 500 G", created.Name);
        Assert.DoesNotContain("PCT", created.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C/12", created.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("500 G", created.Name, StringComparison.OrdinalIgnoreCase);
    }
}
