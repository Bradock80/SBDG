using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Paridade dos métodos públicos de roteamento vs *Local em standalone
/// (ETAPA 31: sem fachadas no PdvService).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvQueryServiceParityTests
{
    private static void EnsureStandalone()
    {
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
    }

    [Fact]
    public void ListSales_Publico_Igual_ListSalesLocal_EmStandalone()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "parity");
        var productId = TestDataHelper.SeedSimpleProduct(30, 10, 4, "P1", "Parity Lista");
        TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);

        var viaPublic = PdvQueryService.ListSales(includeCancelled: true);
        var viaLocal = PdvQueryService.ListSalesLocal(includeCancelled: true);

        Assert.Equal(viaLocal.Count, viaPublic.Count);
        for (var i = 0; i < viaPublic.Count; i++)
        {
            Assert.Equal(viaLocal[i].Id, viaPublic[i].Id);
            Assert.Equal(viaLocal[i].Total, viaPublic[i].Total);
            Assert.Equal(viaLocal[i].PaymentType, viaPublic[i].PaymentType);
            Assert.Equal(viaLocal[i].Cancelled, viaPublic[i].Cancelled);
            Assert.Equal(viaLocal[i].PaymentLabel, viaPublic[i].PaymentLabel);
            Assert.Equal(viaLocal[i].ItemsCount, viaPublic[i].ItemsCount);
        }
    }

    [Fact]
    public void GetSaleDetail_Publico_Igual_GetSaleDetailLocal_EmStandalone()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "parity");
        var productId = TestDataHelper.SeedSimpleProduct(30, 10, 4, "P2", "Parity Detail");
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 2, 10, 20);

        var viaPublic = PdvQueryService.GetSaleDetail(sale.SaleId);
        var viaLocal = PdvQueryService.GetSaleDetailLocal(sale.SaleId);

        Assert.Equal(viaLocal.Id, viaPublic.Id);
        Assert.Equal(viaLocal.Total, viaPublic.Total);
        Assert.Equal(viaLocal.PaymentType, viaPublic.PaymentType);
        Assert.Equal(viaLocal.PaymentLabel, viaPublic.PaymentLabel);
        Assert.Equal(viaLocal.CashReceived, viaPublic.CashReceived);
        Assert.Equal(viaLocal.ChangeAmount, viaPublic.ChangeAmount);
        Assert.Equal(viaLocal.Items.Count, viaPublic.Items.Count);
        Assert.Equal(viaLocal.Payments.Count, viaPublic.Payments.Count);
    }

    [Fact]
    public void GetResumoDia_Publico_Igual_GetResumoDiaLocal_EmStandalone()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        CashService.OpenSession(50, "parity");
        var productId = TestDataHelper.SeedSimpleProduct(30, 10, 4, "P3", "Parity Resumo");
        TestDataHelper.FinalizeSimpleCashSale(productId, 3, 10, 30);

        var viaPublic = PdvQueryService.GetResumoDia();
        var viaLocal = PdvQueryService.GetResumoDiaLocal();

        Assert.Equal(viaLocal.Faturamento, viaPublic.Faturamento);
        Assert.Equal(viaLocal.QtdVendas, viaPublic.QtdVendas);
        Assert.Equal(viaLocal.TicketMedio, viaPublic.TicketMedio);
        Assert.Equal(viaLocal.LucroReal, viaPublic.LucroReal);
        Assert.Equal(viaLocal.FiadoTotal, viaPublic.FiadoTotal);
        Assert.Equal(viaLocal.Formas.Count, viaPublic.Formas.Count);
        Assert.Equal(viaLocal.TopProdutos.Count, viaPublic.TopProdutos.Count);
        Assert.Equal(viaLocal.SessionDate, viaPublic.SessionDate);
        Assert.Equal(viaLocal.CaixaOpen, viaPublic.CaixaOpen);
    }
}
