using SGDB.Domain.Finance;

namespace SGDB.Tests;

/// <summary>ETAPA 69H — classificação pura de movimentos de caixa (KPI vs saldo vs detalhe).</summary>
public class CashMovementReportingRulesTests
{
    private static readonly HashSet<int> Empty = [];

    [Fact]
    public void Par25338_NeutralizaIntegral()
    {
        const double huge = 59_224_415_254_560d;
        Assert.True(CashMovementReportingRules.TryProveIntegralNeutralization(
            saleCancelled: true,
            saleCashIn: huge,
            exchangeCashIn: 0,
            exchangeCashOut: huge,
            exchangeNewTotal: 0));
        Assert.True(CashMovementReportingRules.AmountsNeutralize(huge, huge));
    }

    [Fact]
    public void TrocaParcial_NaoProvaNeutralizacao()
    {
        Assert.False(CashMovementReportingRules.TryProveIntegralNeutralization(
            saleCancelled: true,
            saleCashIn: 100,
            exchangeCashIn: 0,
            exchangeCashOut: 40,
            exchangeNewTotal: 0));
    }

    [Fact]
    public void TrocaComItemNovo_FailSafeNaoOmite()
    {
        Assert.False(CashMovementReportingRules.TryProveIntegralNeutralization(
            saleCancelled: true,
            saleCashIn: 100,
            exchangeCashIn: 0,
            exchangeCashOut: 100,
            exchangeNewTotal: 50));
    }

    [Fact]
    public void VendaAtiva_NaoNeutraliza()
    {
        Assert.False(CashMovementReportingRules.TryProveIntegralNeutralization(
            saleCancelled: false,
            saleCashIn: 100,
            exchangeCashIn: 0,
            exchangeCashOut: 100,
            exchangeNewTotal: 0));
    }

    [Fact]
    public void Classify_VendaCancelada_ForaDoKpiPdv_PermaneceOperacionalSeNaoNeutralizada()
    {
        var flags = CashMovementReportingRules.Classify(
            "venda", "sale", 10, affectsBalance: true,
            cancelledSaleIds: new HashSet<int> { 10 },
            neutralizedSaleIds: Empty,
            neutralizedExchangeIds: Empty);

        Assert.False(flags.IncludeInPdvSalesKpi);
        Assert.True(flags.IncludeInOperationalInflows);
        Assert.True(flags.IncludeInBalance);
        Assert.True(flags.IncludeInDetail);
        Assert.Equal(CashMovementReportingRules.BadgeCancelledSale, flags.DetailBadge);
    }

    [Fact]
    public void Classify_ParNeutralizado_OmiteKpiOperacional_MantemSaldoEDetalhe()
    {
        var sale = CashMovementReportingRules.Classify(
            "venda", "sale", 10, true,
            new HashSet<int> { 10 }, new HashSet<int> { 10 }, Empty);
        var troca = CashMovementReportingRules.Classify(
            "troca", "sale_exchange", 3, true,
            new HashSet<int> { 10 }, new HashSet<int> { 10 }, new HashSet<int> { 3 });

        Assert.False(sale.IncludeInPdvSalesKpi);
        Assert.False(sale.IncludeInOperationalInflows);
        Assert.True(sale.IncludeInBalance);
        Assert.True(sale.IncludeInDetail);
        Assert.False(troca.IncludeInOperationalOutflows);
        Assert.True(troca.IncludeInBalance);
        Assert.Equal(CashMovementReportingRules.BadgeLinkedExchange, troca.DetailBadge);
    }

    [Fact]
    public void Classify_SangriaEVendaAtiva_NaoSaoOmitidas()
    {
        var venda = CashMovementReportingRules.Classify(
            "venda", "sale", 1, true, Empty, Empty, Empty);
        var sangria = CashMovementReportingRules.Classify(
            "sangria", null, 0, true, Empty, Empty, Empty);

        Assert.True(venda.IncludeInPdvSalesKpi);
        Assert.True(venda.IncludeInOperationalInflows);
        Assert.True(sangria.IncludeInOperationalOutflows);
        Assert.Null(sangria.DetailBadge);
    }
}
