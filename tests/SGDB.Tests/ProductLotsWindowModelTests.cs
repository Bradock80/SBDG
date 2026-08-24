using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

[Collection(TempDatabaseCollection.Name)]
public class ProductLotsWindowModelTests
{
    [Fact]
    public void BotaoDesabilitadoSemProductId()
    {
        Assert.False(ProductExpiryService.CanOpenLotsWindow(null));
        Assert.False(ProductExpiryService.CanOpenLotsWindow(0));
        Assert.False(ProductExpiryService.CanOpenLotsWindow(-1));
    }

    [Fact]
    public void BotaoHabilitadoComProdutoSalvo()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(1, 5, 2, "W1", "SALVO");
        Assert.True(ProductExpiryService.CanOpenLotsWindow(productId));
    }

    [Fact]
    public void LinhaFormataValidadeStatusEOrigem()
    {
        var today = new DateTime(2026, 8, 23);
        var lot = new ProductLot
        {
            LotNumber = "ABC",
            Quantity = 12,
            ExpiryDateIso = "2026-08-30",
            UnitCost = 2.5,
            PurchaseId = 18,
            CreatedAt = "2026-08-01 10:00:00",
            Notes = "entrada NF",
        };

        var row = Assert.Single(ProductLotListRow.FromLots([lot], today));
        Assert.Equal("ABC", row.LotDisplay);
        Assert.Equal("12", row.QtyDisplay);
        Assert.Equal("30/08/2026", row.ExpiryDisplay);
        Assert.Equal("7", row.DaysDisplay);
        Assert.Equal("ATÉ 7 DIAS", row.StatusDisplay);
        Assert.Equal("Compra #18", row.OriginDisplay);
        Assert.Contains("entrada NF", row.HistoryDisplay);
        Assert.Contains("R$", row.CostDisplay);
    }

    [Fact]
    public void LinhaSemValidade_MostraNaoInformada()
    {
        var lot = new ProductLot { LotNumber = "", Quantity = 3.5, ExpiryDateIso = null };
        var row = Assert.Single(ProductLotListRow.FromLots([lot], new DateTime(2026, 8, 23)));
        Assert.Equal("—", row.LotDisplay);
        Assert.Equal("3,500", row.QtyDisplay);
        Assert.Equal(ProductExpiryService.UninformedDisplay, row.ExpiryDisplay);
        Assert.Equal("—", row.DaysDisplay);
        Assert.Equal("SEM VALIDADE", row.StatusDisplay);
        Assert.Equal("—", row.OriginDisplay);
    }
}
