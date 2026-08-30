using SGDB.Models;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;
using SGDB.Views;

namespace SGDB.Tests;

/// <summary>
/// QA-CULTURE-CI — parsing e apresentação brasileira não dependem do Windows/runner.
/// </summary>
public class CultureIndependenceTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void ParseBr_5virgula50_NaoVira550(string culture)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal(5.5, ProductPriceHelper.ParseBr("5,50"));
        Assert.Equal(5.5, ProductPriceHelper.ParseBr("R$ 5,50"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void ParseBr_9virgula00_NaoVira900(string culture)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal(9, ProductPriceHelper.ParseBr("9,00"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void ParseBr_PontoDecimal_Continua5_50(string culture)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal(5.5, ProductPriceHelper.ParseBr("5.50"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void FormatBr_E_MoneyBr_SemprePtBr(string culture)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal("40,00", ProductPriceHelper.FormatBr(40));
        Assert.Equal("8,50", ProductPriceHelper.FormatFixed2(8.5));
        Assert.Equal("R$ 40,00", ProductPriceHelper.MoneyBr(40));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void ProductLot_QtyDisplay_3_5_SempreVirgulaBrasileira(string culture)
    {
        using var _ = new CultureScope(culture);
        Assert.Equal("3,500", ProductLotListRow.FormatQty(3.5));
        Assert.Equal("12", ProductLotListRow.FormatQty(12));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void GradeCompra_9virgula00_NaoVira900(string culture)
    {
        using var _ = new CultureScope(culture);
        var draft = new PurchaseItemDraft { SalePrice = 8 };
        draft.SalePriceDisplay = "9,00";
        Assert.Equal(9, draft.SalePrice);
        Assert.True(draft.UpdateSalePrice);
        Assert.Equal("9,00", draft.SalePriceDisplay);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void GradeCompra_5virgula50_NaoVira550(string culture)
    {
        using var _ = new CultureScope(culture);
        var draft = new PurchaseItemDraft
        {
            Quantity = 1,
            UnitPrice = 5,
            PrevSale = 8,
            SalePrice = 8,
        };
        draft.UnitPriceDisplay = "5,50";
        Assert.Equal(5.50, draft.UnitPrice);
        Assert.Equal(8, draft.SalePrice);
        Assert.False(draft.UpdateSalePrice);
    }
}
