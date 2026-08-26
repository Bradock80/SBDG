using SGDB.Domain.Purchases;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 69S-B2 — números extraídos de NFs reais da loja (Downloads),
/// sem gravar XML completo/destinatário no repositório.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class NfeEffectiveCostRealXmlTests
{
    [Fact]
    public void AmbevReal_Antarctica1L_77_10PorDuzia()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "41",
            emit: "CRBS S/A   CDD Volta Redonda",
            cnpj: "56228356014272",
            xProd: "ANTARCTICA PILSEN GFA VD 1L COM TTC",
            cfop: "5403",
            qCom: 6, uCom: "DZ", vUnCom: 64.7588179218, vProd: 388.55,
            qTrib: 72, uTrib: "UN", vUnTrib: 5.3965277778,
            vIpi: 11.35, vIcmsSt: 56.10, vFcpSt: 6.60,
            infAdProd: "FECOP PROPRIO BC:388,55 ALQ:2,00 VL:7,77/ FECOP ST BC 718,56 ALQ:2,00 VL :6,60 Preco Unitario Final:77,1000",
            vNf: 462.60);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);

        Assert.Equal(388.55, item.NfUnitPrice * item.NfQuantity, 2);
        Assert.Equal(6, item.NfQuantity);
        Assert.Equal("DZ", item.NfUnit);
        Assert.Equal(77.10, item.EffectiveCommercialUnitCost, 2);
        Assert.Equal(462.60, item.EffectiveLineCost, 2);
        Assert.Equal(72, item.Quantity);
        Assert.Equal(462.60 / 72, item.UnitPrice, 4);
        Assert.Equal(NfeEffectiveCostSources.PrecoUnitarioFinal, item.CostSource);
        Assert.Equal(NfeEffectiveCostStatus.Conferido, item.CostStatus);
        Assert.Equal("NOVO", item.StatusBadge);
    }

    [Fact]
    public void CocaReal_1LRetornavel_Aproxima56_40PorCaixa()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "42",
            emit: "SPAL INDUSTRIA BRASILEIRA DE BEBIDAS S/A",
            cnpj: "61186888010318",
            xProd: "COCA 1L VDR RET LS UNIV ROT 12UN JD T",
            cfop: "5401",
            qCom: 2, uCom: "CX", vUnCom: 51.845, vProd: 103.69,
            qTrib: 24, uTrib: "GR", vUnTrib: 4.3204166667,
            vIpi: 2.03, vIcmsSt: 6.30, vFcpSt: 0.79,
            infAdProd: "vBCFCP: 104.20  pFCP: 2.00  vFCP: 2.08  vBCFCPST: 143.56  pFCPST: 2.00  vFCPST: 0.79",
            vNf: 112.81);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(NfeEffectiveCostSources.Landed, item.CostSource);
        Assert.Equal(112.81, item.EffectiveLineCost, 2);
        Assert.Equal(56.405, item.EffectiveCommercialUnitCost, 3);
        Assert.InRange(item.EffectiveCommercialUnitCost, 56.39, 56.41);
    }

    [Fact]
    public void SouzaCruzReal_RothmansHw25_CustoMacoPlausivel()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "43",
            emit: "SOUZA CRUZ LTDA.",
            cnpj: "33009911025395",
            xProd: "ROTHMANS Hand Selected Red  BOX HW25",
            cfop: "5403",
            qCom: 0.2, uCom: "MIL", vUnCom: 554.60, vProd: 110.92,
            qTrib: 0.2, uTrib: "MIL", vUnTrib: 554.60,
            vIcmsSt: 3.86, vFcpSt: 0.22,
            infAdProd: "ALIQ. FCP = 2% / BC FCP 86.94 - FCP 1.74 / BC FCP ST 97.98 - FCP ST 0.22",
            vNf: 115);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(200, item.Quantity);
        Assert.Equal(25, item.PackFactor);
        Assert.Equal(8, item.Quantity / item.PackFactor, 4);
        Assert.Equal(115, item.EffectiveLineCost, 2);
        Assert.Equal(0.575, item.UnitPrice, 3);
        Assert.Equal(14.38, item.ResolveCatalogCost(), 2);
        Assert.NotEqual(125, item.ResolveCatalogCost());
    }

    [Fact]
    public void MovimentoECompras_MesmoXml_MesmoCusto()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "44",
            emit: "SPAL INDUSTRIA BRASILEIRA DE BEBIDAS S/A",
            cnpj: "61186888010318",
            xProd: "COCA 1L VDR RET LS UNIV ROT 12UN JD T",
            cfop: "5401",
            qCom: 2, uCom: "CX", vUnCom: 51.845, vProd: 103.69,
            qTrib: 24, uTrib: "GR", vUnTrib: 4.3204166667,
            vIpi: 2.03, vIcmsSt: 6.30, vFcpSt: 0.79,
            vNf: 112.81);

        var movimento = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        var compras = Assert.Single(NfeXmlImportService.ParseXml(
            xml,
            NfeEffectiveCostImportPolicy.IncludeIcmsStFromAdvancedOverride(danfeWithoutStChecked: false)).Items);

        Assert.Equal(movimento.UnitPrice, compras.UnitPrice, 6);
        Assert.Equal(movimento.EffectiveLineCost, compras.EffectiveLineCost, 4);
        Assert.Equal(movimento.EffectiveCommercialUnitCost, compras.EffectiveCommercialUnitCost, 6);
        Assert.Equal(movimento.Quantity, compras.Quantity);
        Assert.True(NfeEffectiveCostImportPolicy.DefaultIncludeIcmsStInCost);
        Assert.True(NfeEffectiveCostImportPolicy.IncludeIcmsStFromAdvancedOverride(false));
        Assert.False(NfeEffectiveCostImportPolicy.IncludeIcmsStFromAdvancedOverride(true));
    }

    [Fact]
    public void OverrideAvancado_NaoEPadrao_ENaoSobrescreveManual()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "45",
            xProd: "COCA 1L VDR RET LS UNIV ROT 12UN JD T",
            qCom: 2, uCom: "CX", vUnCom: 51.845, vProd: 103.69,
            qTrib: 24, uTrib: "GR", vUnTrib: 4.3204166667,
            vIpi: 2.03, vIcmsSt: 6.30, vFcpSt: 0.79,
            vNf: 112.81);

        var withSt = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        var withoutSt = Assert.Single(NfeXmlImportService.ParseXml(xml, includeIcmsStInCost: false).Items);
        Assert.True(withSt.UnitPrice > withoutSt.UnitPrice);

        var original = withSt.UnitPrice;
        withSt.UnitPrice = 9.99;
        Assert.True(withSt.IsManualCost);
        withSt.ApplyStCostOverride(includeSt: false);
        Assert.Equal(9.99, withSt.UnitPrice, 2);
        Assert.Equal(NfeEffectiveCostSources.Manual, withSt.CostSource);
        Assert.NotEqual(original, withSt.UnitPrice);
    }

    [Fact]
    public void StatusCompacto_NovoTemPrioridadeVisual()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "46", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            xProd: "AGUA MINERAL 500ML", vNf: 10);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.True(item.IsNew);
        Assert.Equal("NOVO", item.StatusBadge);
        Assert.False(item.NeedsCostReview);
    }

    static string LineXml(
        string chave,
        string xProd,
        double qCom,
        string uCom,
        double vUnCom,
        double vProd,
        double vNf,
        string emit = "FORN TESTE",
        string cnpj = "12345678000199",
        string cfop = "5102",
        double qTrib = 0,
        string? uTrib = null,
        double vUnTrib = 0,
        double vIpi = 0,
        double vIcmsSt = 0,
        double vFcpSt = 0,
        string? infAdProd = null)
    {
        var accessKey = ("352508" + chave.PadLeft(38, '0'))[..44];
        qTrib = qTrib > 0 ? qTrib : qCom;
        uTrib ??= uCom;
        vUnTrib = vUnTrib > 0 ? vUnTrib : vUnCom;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string D(double v) => v.ToString("0.##########", inv);
        var inf = string.IsNullOrWhiteSpace(infAdProd) ? "" : $"<infAdProd>{infAdProd}</infAdProd>";
        var ipi = vIpi > 0 ? $"<IPI><IPITrib><vIPI>{D(vIpi)}</vIPI></IPITrib></IPI>" : "";
        var icms = (vIcmsSt > 0 || vFcpSt > 0)
            ? $"<ICMS><ICMS10><vICMSST>{D(vIcmsSt)}</vICMSST><vFCPST>{D(vFcpSt)}</vFCPST></ICMS10></ICMS>"
            : "";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc>
              <NFe>
                <infNFe Id="NFe{accessKey}">
                  <ide><nNF>{chave}</nNF><serie>1</serie><dhEmi>2026-08-24T20:11:42-03:00</dhEmi></ide>
                  <emit><CNPJ>{cnpj}</CNPJ><xNome>{emit}</xNome><enderEmit><UF>RJ</UF></enderEmit></emit>
                  <det nItem="1">
                    <prod>
                      <cProd>1</cProd><cEAN>7891991009737</cEAN><xProd>{xProd}</xProd>
                      <CFOP>{cfop}</CFOP>
                      <uCom>{uCom}</uCom><qCom>{D(qCom)}</qCom><vUnCom>{D(vUnCom)}</vUnCom>
                      <vProd>{D(vProd)}</vProd>
                      <cEANTrib>7891991009737</cEANTrib>
                      <uTrib>{uTrib}</uTrib><qTrib>{D(qTrib)}</qTrib><vUnTrib>{D(vUnTrib)}</vUnTrib>
                      <indTot>1</indTot>
                    </prod>
                    <imposto>{icms}{ipi}</imposto>
                    {inf}
                  </det>
                  <total>
                    <ICMSTot>
                      <vProd>{D(vProd)}</vProd>
                      <vNF>{D(vNf)}</vNF>
                      <vST>{D(vIcmsSt)}</vST>
                      <vIPI>{D(vIpi)}</vIPI>
                    </ICMSTot>
                  </total>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }
}
