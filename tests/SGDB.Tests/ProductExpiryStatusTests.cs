using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class ProductExpiryStatusTests
{
    private static readonly DateTime Today = new(2026, 8, 23);

    [Theory]
    [InlineData(-3, ProductExpiryStatusKind.Expired, "VENCIDO")]
    [InlineData(0, ProductExpiryStatusKind.Today, "VENCE HOJE")]
    [InlineData(7, ProductExpiryStatusKind.Within7, "ATÉ 7 DIAS")]
    [InlineData(15, ProductExpiryStatusKind.Within15, "ATÉ 15 DIAS")]
    [InlineData(30, ProductExpiryStatusKind.Within30, "ATÉ 30 DIAS")]
    [InlineData(60, ProductExpiryStatusKind.Within60, "ATÉ 60 DIAS")]
    [InlineData(90, ProductExpiryStatusKind.Within90, "ATÉ 90 DIAS")]
    [InlineData(91, ProductExpiryStatusKind.Ok, "OK")]
    public void Classify_FaixasDeDias(int offset, ProductExpiryStatusKind kind, string label)
    {
        var status = ProductExpiryService.Classify(Today.AddDays(offset), Today);
        Assert.Equal(kind, status.Kind);
        Assert.Equal(offset, status.Days);
        Assert.Equal(label, status.Label);
        Assert.Equal(offset, ProductExpiryService.DaysRemaining(Today.AddDays(offset), Today));
        Assert.Equal(offset.ToString(), ProductExpiryService.FormatDays(status.Days));
    }

    [Fact]
    public void Classify_SemValidade()
    {
        var status = ProductExpiryService.Classify(null, Today);
        Assert.Equal(ProductExpiryStatusKind.Uninformed, status.Kind);
        Assert.Null(status.Days);
        Assert.Equal("SEM VALIDADE", status.Label);
        Assert.Null(ProductExpiryService.DaysRemaining(null, Today));
        Assert.Equal("—", ProductExpiryService.FormatDays(null));
    }

    [Fact]
    public void DaysRemaining_IgnoraHorario()
    {
        var expiry = new DateTime(2026, 8, 23, 23, 59, 59);
        var morning = new DateTime(2026, 8, 23, 0, 1, 0);
        Assert.Equal(0, ProductExpiryService.DaysRemaining(expiry, morning));
    }
}
