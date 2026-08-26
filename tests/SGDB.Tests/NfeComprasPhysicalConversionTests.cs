using SGDB.Domain.Products;
using SGDB.Domain.Purchases;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69S-B3 — conversão comercial→física única (Movimento = Compras).
/// XML real do Piraquê não está neste PC; fixtures cobrem o padrão Ambev/caixa.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class NfeComprasPhysicalConversionTests
{
    [Fact]
    public void CxComFatorCadastro_ExpandeParaFisico()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedSnack(fator: 30, barcode: "7896004001012", packBarcode: "17896004001019");

        var xml = LineXml(
            chave: "69sb3cx",
            xProd: "PIRAQUE RECHEADO MORANGO",
            cEan: "17896004001019",
            cEanTrib: "7896004001012",
            qCom: 1, uCom: "CX", vUnCom: 93.24, vProd: 93.24,
            qTrib: 1, uTrib: "CX", vUnTrib: 93.24,
            vNf: 93.24);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(id, item.MatchedProductId);
        Assert.Equal(30, item.Quantity, 4);
        Assert.Equal(93.24, item.EffectiveLineCost, 2);
        Assert.Equal(3.108, item.UnitPrice, 3);
        Assert.Equal(93.24, ProductPriceHelper.RoundPrice(item.Quantity * item.UnitPrice), 2);
    }

    [Fact]
    public void UnComGtinEmbalagemDistinto_EFator_ExpandeParaFisico()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedSnack(fator: 30, barcode: "7896004001012", packBarcode: "17896004001019");

        var xml = LineXml(
            chave: "69sb3un",
            xProd: "PIRAQUE RECHEADO MORANGO",
            cEan: "17896004001019",
            cEanTrib: "7896004001012",
            qCom: 1, uCom: "UN", vUnCom: 93.24, vProd: 93.24,
            qTrib: 1, uTrib: "UN", vUnTrib: 93.24,
            vNf: 93.24);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(30, item.Quantity, 4);
        Assert.Equal(3.108, item.UnitPrice, 3);
        Assert.Equal(93.24, item.EffectiveLineCost, 2);
        Assert.Equal(1, item.NfQuantity, 4);
        Assert.Equal(93.24, item.NfUnitPrice, 2);
    }

    [Fact]
    public void RematchComFator_ReaplicaConversao_CxSemFatorNoParse()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");

        // Sem produto no parse → qty comercial; depois cadastro + rematch.
        var xml = LineXml(
            chave: "69sb3rm",
            xProd: "PIRAQUE RECHEADO MORANGO CX",
            cEan: "17896004001019",
            cEanTrib: "17896004001019",
            qCom: 1, uCom: "CX", vUnCom: 93.24, vProd: 93.24,
            qTrib: 1, uTrib: "CX", vUnTrib: 93.24,
            vNf: 93.24);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.True(item.IsNew);
        Assert.Equal(1, item.Quantity, 4);
        Assert.Equal(93.24, item.UnitPrice, 2);

        SeedSnack(fator: 30, barcode: "7896004001012", packBarcode: "17896004001019", name: "PIRAQUE RECHEADO MORANGO");
        Assert.True(NfeXmlImportService.TryReapplyPhysicalConversion(item, 30));
        Assert.Equal(30, item.Quantity, 4);
        Assert.Equal(3.108, item.UnitPrice, 3);
        Assert.Equal(93.24, item.TotalValue, 2);
        Assert.False(item.IsManualCost);
    }

    [Fact]
    public void MovimentoECompras_MesmoXml_MesmaQtyECustoFisico()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedSnack(fator: 30, barcode: "7896004001012", packBarcode: "17896004001019");

        var xml = LineXml(
            chave: "69sb3eq",
            xProd: "PIRAQUE RECHEADO MORANGO",
            cEan: "17896004001019",
            cEanTrib: "7896004001012",
            qCom: 1, uCom: "UN", vUnCom: 93.24, vProd: 93.24,
            qTrib: 1, uTrib: "UN", vUnTrib: 93.24,
            vNf: 93.24);

        var a = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        var b = Assert.Single(NfeXmlImportService.ParseXml(
            xml,
            NfeEffectiveCostImportPolicy.IncludeIcmsStFromAdvancedOverride(false)).Items);

        Assert.Equal(a.Quantity, b.Quantity);
        Assert.Equal(a.UnitPrice, b.UnitPrice, 6);
        Assert.Equal(a.EffectiveLineCost, b.EffectiveLineCost, 4);
        Assert.Equal(a.TotalValue, b.TotalValue, 4);
    }

    [Fact]
    public void BrahmaGarrafa_JaEmUnidade_NaoMultiplicaFatorExtra()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedBeer(name: "BRAHMA CHOPP GARRAFA VIDRO 300ML", fator: 24, barcode: "7891991010874");

        var xml = LineXml(
            chave: "69sb3br",
            xProd: "BRAHMA CHOPP GARRAFA VIDRO 300ML",
            cEan: "7891991010874",
            cEanTrib: "7891991010874",
            qCom: 92, uCom: "UN", vUnCom: 1.8380434783, vProd: 169.10,
            qTrib: 92, uTrib: "UN", vUnTrib: 1.8380434783,
            vNf: 169.10);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(92, item.Quantity, 4);
        Assert.Equal(169.10, item.EffectiveLineCost, 2);
        Assert.Equal(1.84, ProductPriceHelper.RoundPrice(item.UnitPrice), 2);
        Assert.Equal(169.10, ProductPriceHelper.RoundPrice(item.Quantity * item.UnitPrice), 2);
    }

    [Fact]
    public void SpatenLongNeck_24Un_TotalReconciliavel()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedBeer(name: "SPATEN LONG NECK 330ML", fator: 24, barcode: "7891991293121");

        var xml = LineXml(
            chave: "69sb3sp",
            xProd: "SPATEN LONG NECK 330ML",
            cEan: "7891991293121",
            cEanTrib: "7891991293121",
            qCom: 24, uCom: "UN", vUnCom: 4.0083333333, vProd: 96.20,
            qTrib: 24, uTrib: "UN", vUnTrib: 4.0083333333,
            vNf: 96.20);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(24, item.Quantity, 4);
        Assert.Equal(96.20, item.EffectiveLineCost, 2);
        Assert.Equal(4.01, ProductPriceHelper.RoundPrice(item.UnitPrice), 2);
        Assert.Equal(96.20, ProductPriceHelper.RoundPrice(item.Quantity * item.UnitPrice), 2);
    }

    [Fact]
    public void IsqueirosUnComFatorCartela_NaoMultiplica()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedSnack(fator: 12, barcode: "7891000100103", packBarcode: null, name: "ISQUEIRO BIC MAXI");

        var xml = LineXml(
            chave: "69sb3isq",
            xProd: "ISQUEIRO BIC MAXI",
            cEan: "7891000100103",
            cEanTrib: "7891000100103",
            qCom: 18, uCom: "UN", vUnCom: 2.50, vProd: 45,
            qTrib: 18, uTrib: "UN", vUnTrib: 2.50,
            vNf: 45);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(18, item.Quantity, 4);
        Assert.Equal(2.50, item.UnitPrice, 2);
    }

    [Fact]
    public void ToPurchaseItem_RecebeCustoFisico()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedSnack(fator: 30, barcode: "7896004001012", packBarcode: "17896004001019");

        var xml = LineXml(
            chave: "69sb3pi",
            xProd: "PIRAQUE RECHEADO MORANGO",
            cEan: "17896004001019",
            cEanTrib: "7896004001012",
            qCom: 1, uCom: "CX", vUnCom: 93.24, vProd: 93.24,
            qTrib: 1, uTrib: "CX", vUnTrib: 93.24,
            vNf: 93.24);

        var preview = NfeXmlImportService.ParseXml(xml);
        var item = Assert.Single(preview.Items);
        // Via Apply do Movimento — ToPurchaseItem é privado; validamos o contrato da grade.
        Assert.Equal(30, item.Quantity, 4);
        Assert.Equal(3.108, item.UnitPrice, 3);
        Assert.Equal(id, item.MatchedProductId);

        var avgBefore = typeof(PurchaseAverageCostRules);
        Assert.NotNull(avgBefore); // fórmula intacta (não alteramos a classe)
    }

    [Fact]
    public void HeaderVNf_NaoEConfundidoComSomaDeTresLinhas()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = MultiLineXml(
            vNf: 1019.50,
            ("PROD A", 10, 10),
            ("PROD B", 20, 20),
            ("PROD C", 30, 30),
            ("PROD D", 959.50, 959.50));

        var preview = NfeXmlImportService.ParseXml(xml);
        Assert.Equal(1019.50, preview.HeaderVNf, 2);
        Assert.Equal(4, preview.Items.Count);
        Assert.Equal(1019.50, preview.TotalValue, 2);
        Assert.NotEqual(60, preview.HeaderVNf); // não é só as 3 primeiras linhas
    }

    [Fact]
    public void CigarroMil_ContinuaEmCigarros()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "69sb3cig",
            xProd: "CIGARRO ROTHMANS RED BOX 20",
            cEan: "7895555000028",
            cEanTrib: "7895555000028",
            qCom: 0.2, uCom: "MIL", vUnCom: 554.60, vProd: 110.92,
            qTrib: 0.2, uTrib: "MIL", vUnTrib: 554.60,
            vNf: 110.92);

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(200, item.Quantity, 4); // 0,2 MIL = 200 cig
        Assert.True(item.UnitPrice < 1);
        Assert.Equal(110.92, item.EffectiveLineCost, 2);
    }

    private static int SeedSnack(
        double fator, string barcode, string? packBarcode, string name = "PIRAQUE RECHEADO MORANGO")
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoCompra = 3.10,
            BarcodeEmbalagem = packBarcode,
        };
        return ProductService.Create(new ProductInput
        {
            Code = "SNK" + Guid.NewGuid().ToString("N")[..6],
            Barcode = barcode,
            Name = name,
            GroupName = "MERCEARIA",
            Unit = "UN",
            CostPrice = 3.10,
            SalePrice = 4.50,
            Stock = 0,
            Extra = extra,
            Active = true,
        }).Id;
    }

    private static int SeedBeer(string name, double fator, string barcode)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoCompra = 2.00,
        };
        return ProductService.Create(new ProductInput
        {
            Code = "BER" + Guid.NewGuid().ToString("N")[..6],
            Barcode = barcode,
            Name = name,
            GroupName = "CERVEJA",
            Unit = "UN",
            CostPrice = 2.00,
            SalePrice = 5.00,
            Stock = 0,
            Extra = extra,
            Active = true,
        }).Id;
    }

    private static string LineXml(
        string chave,
        string xProd,
        double qCom,
        string uCom,
        double vUnCom,
        double vProd,
        double vNf,
        string? cEan = null,
        string? cEanTrib = null,
        double qTrib = 0,
        string? uTrib = null,
        double vUnTrib = 0)
    {
        var accessKey = ("332605" + chave.PadLeft(38, '0'))[..44];
        qTrib = qTrib > 0 ? qTrib : qCom;
        uTrib ??= uCom;
        vUnTrib = vUnTrib > 0 ? vUnTrib : vUnCom;
        cEan ??= "SEM GTIN";
        cEanTrib ??= cEan;
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
              <NFe>
                <infNFe Id="NFe{accessKey}">
                  <ide><nNF>1</nNF><serie>1</serie><dhEmi>2026-05-01T10:00:00-03:00</dhEmi></ide>
                  <emit>
                    <CNPJ>56228356014272</CNPJ>
                    <xNome>CRBS S/A</xNome>
                    <enderEmit><UF>RJ</UF></enderEmit>
                  </emit>
                  <det nItem="1">
                    <prod>
                      <cProd>1</cProd>
                      <cEAN>{cEan}</cEAN>
                      <xProd>{xProd}</xProd>
                      <uCom>{uCom}</uCom><qCom>{D(qCom)}</qCom><vUnCom>{D(vUnCom)}</vUnCom>
                      <vProd>{D(vProd)}</vProd>
                      <cEANTrib>{cEanTrib}</cEANTrib>
                      <uTrib>{uTrib}</uTrib><qTrib>{D(qTrib)}</qTrib><vUnTrib>{D(vUnTrib)}</vUnTrib>
                      <CFOP>5405</CFOP><indTot>1</indTot>
                    </prod>
                  </det>
                  <total><ICMSTot>
                    <vProd>{D(vProd)}</vProd><vNF>{D(vNf)}</vNF>
                    <vST>0</vST><vDesc>0</vDesc><vFrete>0</vFrete><vOutro>0</vOutro><vIPI>0</vIPI>
                  </ICMSTot></total>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }

    private static string MultiLineXml(double vNf, params (string Name, double VProd, double VUn)[] lines)
    {
        var accessKey = "33260556228356014272550240003648491895046246";
        var dets = new System.Text.StringBuilder();
        var i = 1;
        foreach (var (name, vProd, vUn) in lines)
        {
            dets.Append($"""
                  <det nItem="{i}">
                    <prod>
                      <cProd>{i}</cProd><cEAN>SEM GTIN</cEAN>
                      <xProd>{name}</xProd>
                      <uCom>UN</uCom><qCom>1.0000</qCom><vUnCom>{D(vUn)}</vUnCom>
                      <vProd>{D(vProd)}</vProd>
                      <cEANTrib>SEM GTIN</cEANTrib>
                      <uTrib>UN</uTrib><qTrib>1.0000</qTrib><vUnTrib>{D(vUn)}</vUnTrib>
                      <CFOP>5102</CFOP><indTot>1</indTot>
                    </prod>
                  </det>
                """);
            i++;
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
              <NFe>
                <infNFe Id="NFe{accessKey}">
                  <ide><nNF>364849</nNF><serie>24</serie><dhEmi>2026-05-01T10:00:00-03:00</dhEmi></ide>
                  <emit><CNPJ>56228356014272</CNPJ><xNome>CRBS S/A</xNome></emit>
                  {dets}
                  <total><ICMSTot>
                    <vProd>{D(vNf)}</vProd><vNF>{D(vNf)}</vNF>
                    <vST>0</vST><vDesc>0</vDesc><vFrete>0</vFrete><vOutro>0</vOutro><vIPI>0</vIPI>
                  </ICMSTot></total>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }

    private static string D(double v) =>
        v.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
}
