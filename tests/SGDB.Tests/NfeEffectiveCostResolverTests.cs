using SGDB.Domain.Purchases;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

public class NfeEffectiveCostResolverTests
{
    [Fact]
    public void Ambev_PrecoUnitarioFinal_Valida77_10PorDuzia()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 64.76,
            QCom = 1,
            UCom = "DZ",
            VUnCom = 64.76,
            QTrib = 12,
            UTrib = "UN",
            VUnTrib = 5.396667,
            InfAdProd = "Preco Unitario Final:77,1000",
            HeaderVProd = 64.76,
            HeaderVNf = 77.10,
        });
        Assert.Equal(NfeEffectiveCostSources.PrecoUnitarioFinal, d.Source);
        Assert.Equal(NfeEffectiveCostStatus.Conferido, d.Status);
        Assert.Equal(77.10, d.EffectiveCommercialUnitCost, 2);
        Assert.Equal(77.10, d.EffectiveLineCost, 2);
        Assert.False(d.NeedsManualReview);
    }

    [Fact]
    public void Ambev_SemPrecoFinal_UsaLanded()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 64.76,
            QCom = 1,
            VUnCom = 64.76,
            VIpi = 2,
            VIcmsSt = 8,
            VFcpSt = 2.34,
            HeaderVNf = 77.10,
            HeaderVProd = 64.76,
        });
        Assert.Equal(NfeEffectiveCostSources.Landed, d.Source);
        Assert.Equal(77.10, d.EffectiveLineCost, 2);
        Assert.Equal(NfeEffectiveCostStatus.Calculado, d.Status);
    }

    [Fact]
    public void Ambev_PrecoFinalNaoConcilia_Revisar()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 64.76,
            QCom = 1,
            VUnCom = 64.76,
            InfAdProd = "Preço Unitário Final:99,0000",
            HeaderVProd = 64.76,
            HeaderVNf = 64.76,
        });
        Assert.Equal(NfeEffectiveCostStatus.Revisar, d.Status);
        Assert.True(d.NeedsManualReview);
        Assert.Equal(64.76, d.EffectiveLineCost, 2);
        Assert.Equal(NfeEffectiveCostSources.Landed, d.Source);
    }

    [Fact]
    public void CocaCola_LandedComSt_Aproxima56_40()
    {
        var d = NfeEffectiveCostResolver.Resolve(CocaInput());
        Assert.Equal(NfeEffectiveCostSources.Landed, d.Source);
        Assert.Equal(56.40, d.EffectiveCommercialUnitCost, 2);
        Assert.Equal(56.40, d.EffectiveLineCost, 2);
    }

    [Fact]
    public void CocaCola_OutroItemComStFcp()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 40,
            QCom = 1,
            VUnCom = 40,
            VIpi = 1.2,
            VIcmsSt = 4.5,
            VFcpSt = 0.8,
        });
        Assert.Equal(46.50, d.EffectiveLineCost, 2);
        Assert.Equal(5.30, d.StCharges, 2);
    }

    [Fact]
    public void DescontoGrande_271_26Menos112_92_Eh158_34()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 271.26,
            QCom = 1,
            VUnCom = 271.26,
            VDesc = 112.92,
        });
        Assert.Equal(158.34, d.EffectiveLineCost, 2);
        Assert.NotEqual(271.26, d.EffectiveLineCost);
    }

    [Fact]
    public void ItemSemDesconto_MantemVProdMaisEncargos()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 10,
            QCom = 2,
            VUnCom = 5,
            VIpi = 1,
        });
        Assert.Equal(11, d.EffectiveLineCost, 2);
        Assert.Equal(5.5, d.EffectiveCommercialUnitCost, 2);
    }

    [Fact]
    public void CustoLiquidoZero_Revisar_NaoVoltaVProdBruto()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 100,
            QCom = 1,
            VDesc = 100,
        });
        Assert.Equal(NfeEffectiveCostStatus.Revisar, d.Status);
        Assert.Equal(0, d.EffectiveLineCost);
    }

    [Fact]
    public void Bonificacao_5910_CustoZero()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 50,
            QCom = 1,
            VUnCom = 50,
            Cfop = "5910",
        });
        Assert.Equal(NfeEffectiveCostStatus.Bonificacao, d.Status);
        Assert.Equal(0, d.EffectiveLineCost);
        Assert.False(d.IncludeInPayable);
    }

    [Fact]
    public void Remessa_5911_CustoZero()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 20,
            QCom = 1,
            Cfop = "5911",
        });
        Assert.Equal(NfeEffectiveCostStatus.Remessa, d.Status);
        Assert.Equal(0, d.EffectiveLineCost);
        Assert.False(d.IncludeInPayable);
    }

    [Fact]
    public void IndTotZero_NaoEntraComoPago()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 15,
            QCom = 1,
            IndTot = 0,
        });
        Assert.Equal(NfeEffectiveCostSources.IndTotZero, d.Source);
        Assert.False(d.IncludeInPayable);
        Assert.Equal(0, d.EffectiveLineCost);
    }

    [Fact]
    public void FreteNoItem_EntraNoLanded()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 10,
            QCom = 1,
            VFrete = 2.5,
        });
        Assert.Equal(12.5, d.EffectiveLineCost, 2);
        Assert.False(d.NeedsManualReview);
    }

    [Fact]
    public void FreteSoNoTotal_Revisar()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 10,
            QCom = 1,
            HeaderFrete = 8,
            HeaderFreightUnallocated = true,
        });
        Assert.Equal(NfeEffectiveCostStatus.Revisar, d.Status);
        Assert.True(d.NeedsManualReview);
        Assert.Equal(10, d.EffectiveLineCost, 2);
    }

    [Fact]
    public void VOutro_EntraNoLanded()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 10,
            QCom = 1,
            VOutro = 1.25,
        });
        Assert.Equal(11.25, d.EffectiveLineCost, 2);
    }

    [Fact]
    public void VItemPresente_ConfereMasNaoViraFonte()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 10,
            QCom = 1,
            VIpi = 2,
            VItem = 12,
        });
        Assert.Equal(NfeEffectiveCostSources.Landed, d.Source);
        Assert.Equal(12, d.EffectiveLineCost, 2);
        Assert.Contains("vItem", d.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StRetido_NaoSomaDeNovo()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 10,
            QCom = 1,
            VIcmsSt = 3,
            VIcmsStRet = 3,
        });
        Assert.Equal(13, d.EffectiveLineCost, 2);
        Assert.Contains("retido", d.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QComDiferenteQTrib_UnidadeComercialUsaQCom()
    {
        var d = NfeEffectiveCostResolver.Resolve(new NfeEffectiveCostInput
        {
            VProd = 24,
            QCom = 2,
            QTrib = 24,
            UCom = "CX",
            UTrib = "UN",
            VUnCom = 12,
        });
        Assert.Equal(12, d.EffectiveCommercialUnitCost, 2);
        Assert.Equal(24, d.EffectiveLineCost, 2);
    }

    [Fact]
    public void Conciliacao_TotalConfere()
    {
        var r = NfeCostReconciliation.Reconcile([56.40, 77.10], fatLiq: 133.50, 0, 0, 133.50);
        Assert.True(r.IsReconciled);
        Assert.Equal("NF-e conferida", r.FooterStatus);
    }

    [Fact]
    public void Conciliacao_TotalDivergente()
    {
        var r = NfeCostReconciliation.Reconcile([50], fatLiq: 0, dupSum: 0, pagSum: 0, headerVNf: 80);
        Assert.False(r.IsReconciled);
        Assert.Equal("Revisar antes de finalizar", r.FooterStatus);
    }

    [Fact]
    public void EdicaoManual_NaoESobrescritaPeloResolver()
    {
        var original = NfeEffectiveCostResolver.Resolve(CocaInput());
        var manual = original.AsManual(60, 1, 1);
        Assert.Equal(NfeEffectiveCostSources.Manual, manual.Source);
        Assert.Equal(NfeEffectiveCostStatus.ConferidoManual, manual.Status);
        Assert.Equal(60, manual.EffectiveLineCost, 2);
        Assert.Equal(56.40, original.EffectiveLineCost, 2);
    }

    [Fact]
    public void MediaPonderada_FormulaIntocada()
    {
        var avg = ProductPriceCalculator.WeightedAverageCost(
            stockBefore: 10, costBefore: 5, qtyIn: 10, costIn: 7);
        Assert.Equal(6, avg);
        var fromLines = PurchaseAverageCostRules.WeightedAverageFromLines(
            10, 0, 5, "AGUA", "Bebidas", 1, [(10, 7)]);
        Assert.Equal(6, fromLines);
    }

    [Fact]
    public void Cfop5102_NaoEBonificacaoNemRemessa()
    {
        Assert.Equal(NfeCfopCostKind.Normal, NfeCfopCostClassifier.Classify("5102"));
        Assert.Equal(NfeCfopCostKind.Normal, NfeCfopCostClassifier.Classify("5405"));
    }

    [Fact]
    public void Tolerancia_Centralizada()
    {
        Assert.Equal(0.05, NfeCostTolerance.AllowedDelta(1));
        Assert.Equal(0.50, NfeCostTolerance.AllowedDelta(100), 2);
        Assert.True(NfeCostTolerance.NearlyEqual(100, 100.40, 100));
        Assert.False(NfeCostTolerance.NearlyEqual(100, 101, 100));
    }

    [Fact]
    public void Parser_PrecoUnitarioFinal_AcentoEEspaco()
    {
        Assert.True(NfeInfAdProdFinalPriceParser.TryParse("x Preço Unitário Final: 77,1000 y", out var v));
        Assert.Equal(77.1, v, 4);
    }

    static NfeEffectiveCostInput CocaInput() => new()
    {
        VProd = 51.845,
        QCom = 1,
        UCom = "CX",
        VUnCom = 51.845,
        VIpi = 0.555,
        VIcmsSt = 3.5,
        VFcpSt = 0.5,
    };
}

[Collection(TempDatabaseCollection.Name)]
public class NfeEffectiveCostImportTests
{
    [Fact]
    public void ParseXml_Ambev_Antarctica1L_77_10PorDuzia()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "21",
            emit: "CRBS S/A",
            cnpj: "02581177000176",
            qCom: 1, uCom: "DZ", vUnCom: 64.76, vProd: 64.76,
            qTrib: 12, uTrib: "UN", vUnTrib: 5.396667,
            xProd: "ANTARCTICA PILSEN 1L",
            infAdProd: "Preco Unitario Final:77,1000",
            vNf: 77.10);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(77.10, item.EffectiveCommercialUnitCost, 2);
        Assert.Equal(NfeEffectiveCostSources.PrecoUnitarioFinal, item.CostSource);
        Assert.Equal(NfeEffectiveCostStatus.Conferido, item.CostStatus);
        Assert.Equal(12, item.Quantity);
        Assert.Equal(77.10 / 12, item.UnitPrice, 4);
    }

    [Fact]
    public void ParseXml_CocaCola_Caixa56_40()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "22",
            emit: "SPAL INDUSTRIA BRASILEIRA DE BEBIDAS SA",
            cnpj: "61186730000157",
            qCom: 1, uCom: "CX", vUnCom: 51.845, vProd: 51.845,
            vIpi: 0.555, vIcmsSt: 3.5, vFcpSt: 0.5,
            xProd: "COCA COLA 1L RETORNAVEL",
            vNf: 56.40);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(56.40, item.EffectiveCommercialUnitCost, 2);
        Assert.Equal(56.40, item.UnitPrice, 2);
        Assert.Equal(NfeEffectiveCostSources.Landed, item.CostSource);
    }

    [Fact]
    public void ParseXml_SouzaCruz_02000Mil_200Cigarros_CustoMacoHw25()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "23",
            emit: "SOUZA CRUZ LTDA",
            cnpj: "33009911025395",
            qCom: 0.2, uCom: "MIL", vUnCom: 554.60, vProd: 110.92,
            qTrib: 0.2, uTrib: "MIL", vUnTrib: 554.60,
            vIcmsSt: 3.86, vFcpSt: 0.22,
            xProd: "ROTHMANS Hand Selected Red  BOX HW25",
            vNf: 115);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(200, item.Quantity);
        Assert.Equal(25, item.PackFactor);
        Assert.Equal(115, item.EffectiveLineCost, 2);
        Assert.Equal(14.38, item.ResolveCatalogCost(), 2);
    }

    [Fact]
    public void ParseXml_DescontoGrande()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "24", qCom: 1, uCom: "UN", vUnCom: 271.26, vProd: 271.26,
            vDesc: 112.92, xProd: "PRODUTO COM DESC", vNf: 158.34);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(158.34, item.EffectiveLineCost, 2);
        Assert.Equal(158.34, item.UnitPrice, 2);
    }

    [Fact]
    public void ParseXml_Bonificacao_NaoEntraComoPago()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "25", qCom: 1, uCom: "UN", vUnCom: 40, vProd: 40,
            cfop: "5910", xProd: "BRINDE", vNf: 40);
        var preview = NfeXmlImportService.ParseXml(xml);
        var item = Assert.Single(preview.Items);
        Assert.Equal(NfeEffectiveCostStatus.Bonificacao, item.CostStatus);
        Assert.Equal(0, item.EffectiveLineCost);
        Assert.False(item.IncludeInPayable);
        Assert.Equal(0, preview.Reconciliation!.CalculatedEffectiveCost);
    }

    [Fact]
    public void ParseXml_Remessa_5911()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "26", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            cfop: "6911", xProd: "AMOSTRA", vNf: 10);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(NfeEffectiveCostStatus.Remessa, item.CostStatus);
        Assert.False(item.IncludeInPayable);
    }

    [Fact]
    public void ParseXml_IndTotZero()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "27", qCom: 1, uCom: "UN", vUnCom: 8, vProd: 8,
            indTot: 0, xProd: "FORA DO TOTAL", vNf: 0);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.False(item.IncludeInPayable);
        Assert.Equal(0, item.UnitPrice);
    }

    [Fact]
    public void ParseXml_FreteSoNoTotal_Revisar()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "28", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            xProd: "COM FRETE TOTAL", vNf: 18, vFreteTot: 8);
        var preview = NfeXmlImportService.ParseXml(xml);
        var item = Assert.Single(preview.Items);
        Assert.Equal(NfeEffectiveCostStatus.Revisar, item.CostStatus);
        Assert.Equal(10, item.EffectiveLineCost, 2);
    }

    [Fact]
    public void ParseXml_CaixaFardo_QComQTrib()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "29", qCom: 2, uCom: "CX", vUnCom: 24, vProd: 48,
            qTrib: 24, uTrib: "UN", vUnTrib: 2,
            xProd: "REFRIGERANTE CX 12", vNf: 48);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(24, item.Quantity);
        Assert.Equal(2, item.UnitPrice, 2);
        Assert.Equal(24, item.EffectiveCommercialUnitCost, 2);
    }

    [Fact]
    public void EdicaoManual_Prevalece()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "30", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            xProd: "AGUA MINERAL 500ML", vNf: 10);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        item.UnitPrice = 9.50;
        Assert.True(item.IsManualCost);
        Assert.Equal(NfeEffectiveCostSources.Manual, item.CostSource);
        Assert.Equal(NfeEffectiveCostStatus.ConferidoManual, item.CostStatus);
        Assert.Equal(9.50, item.UnitPrice, 2);
        item.ApplyStCostOverride(includeSt: false);
        Assert.True(item.IsManualCost);
        Assert.Equal(9.50, item.UnitPrice, 2);
    }

    [Fact]
    public void ParseXml_AmbevSemPrecoFinal_UsaLanded()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "33",
            qCom: 1, uCom: "DZ", vUnCom: 64.76, vProd: 64.76,
            qTrib: 12, uTrib: "UN", vUnTrib: 5.396667,
            vIpi: 2, vIcmsSt: 8, vFcpSt: 2.34,
            xProd: "ANTARCTICA PILSEN 1L",
            vNf: 77.10);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(NfeEffectiveCostSources.Landed, item.CostSource);
        Assert.Equal(77.10, item.EffectiveLineCost, 2);
        Assert.Equal(NfeEffectiveCostStatus.Calculado, item.CostStatus);
    }

    [Fact]
    public void ParseXml_FreteNoItem_EntraNoCusto()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "34", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            vFrete: 2.5, xProd: "COM FRETE ITEM", vNf: 12.5);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(12.50, item.EffectiveLineCost, 2);
        Assert.NotEqual(NfeEffectiveCostStatus.Revisar, item.CostStatus);
    }

    [Fact]
    public void ParseXml_VOutro_EntraNoCusto()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "35", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            vOutro: 1.25, xProd: "COM VOUTRO", vNf: 11.25);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(11.25, item.EffectiveLineCost, 2);
    }

    [Fact]
    public void ParseXml_ItemSemDesconto()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "36", qCom: 2, uCom: "UN", vUnCom: 5, vProd: 10,
            vIpi: 1, xProd: "SEM DESC", vNf: 11);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(11, item.EffectiveLineCost, 2);
        Assert.Equal(5.5, item.EffectiveCommercialUnitCost, 2);
    }

    [Fact]
    public void ParseXml_TotalConciliado()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "37", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            vIpi: 2, xProd: "CONCILIA", vNf: 12);
        var preview = NfeXmlImportService.ParseXml(xml);
        Assert.True(preview.Reconciliation!.IsReconciled);
        Assert.Equal("NF-e conferida", preview.Reconciliation.FooterStatus);
    }

    [Fact]
    public void ParseXml_TotalDivergente()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "38", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            xProd: "DIVERGE", vNf: 80);
        var preview = NfeXmlImportService.ParseXml(xml);
        Assert.False(preview.Reconciliation!.IsReconciled);
        Assert.Equal("Revisar antes de finalizar", preview.Reconciliation.FooterStatus);
    }

    [Fact]
    public void ParseXml_QComDiferenteQTrib()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "39", qCom: 2, uCom: "CX", vUnCom: 12, vProd: 24,
            qTrib: 24, uTrib: "UN", vUnTrib: 1,
            xProd: "FARDO 12 UN", vNf: 24);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(24, item.Quantity);
        Assert.Equal(12, item.EffectiveCommercialUnitCost, 2);
        Assert.Equal(1, item.UnitPrice, 2);
    }

    [Fact]
    public void CustoEfetivo_ChegaAoPurchaseItem_EMediaUsaNovoUnitPrice()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var extra = new ProductExtra { PrecoCompra = 50 }.ToJson();
        int id;
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO products (code, barcode, name, group_name, unit, sale_price, stock, cost_price, active, extra_json)
                VALUES ('COCA1', '7894900011517', 'COCA COLA 1L RETORNAVEL', 'Bebidas', 'UN', 8, 0, 50, 1, $e);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$e", extra);
            id = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var xml = BuildXml(
            chave: "31",
            emit: "SPAL",
            cnpj: "61186730000157",
            qCom: 1, uCom: "CX", vUnCom: 51.845, vProd: 51.845,
            vIpi: 0.555, vIcmsSt: 3.5, vFcpSt: 0.5,
            xProd: "COCA COLA 1L RETORNAVEL",
            cEan: "7894900011517",
            vNf: 56.40);
        var preview = NfeXmlImportService.ParseXml(xml);
        var applied = NfeXmlImportService.Apply(preview, createMissingProducts: false,
            updateStock: true, updateCost: true);
        var purchase = PurchaseService.GetById(applied.PurchaseId)!;
        var line = Assert.Single(purchase.Items);
        Assert.Equal(56.40, line.UnitPrice, 2);
        var product = ProductService.GetById(id)!;
        Assert.Equal(56.40, product.CostPrice, 2);
        Assert.Equal(56.40, ProductExtra.Parse(product.ExtraJson).PrecoCompra, 2);
    }

    [Fact]
    public void ParseXml_VItemPresente_NaoViraFontePrincipal()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            chave: "32", qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            vIpi: 2, vItem: 12, xProd: "COM VITEM", vNf: 12);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(NfeEffectiveCostSources.Landed, item.CostSource);
        Assert.Equal(12, item.EffectiveLineCost, 2);
    }

    static string BuildXml(
        string chave,
        double qCom,
        string uCom,
        double vUnCom,
        double vProd,
        string xProd,
        double vNf,
        string emit = "FORN TESTE",
        string cnpj = "12345678000199",
        double qTrib = 0,
        string? uTrib = null,
        double vUnTrib = 0,
        double vIpi = 0,
        double vIcmsSt = 0,
        double vFcpSt = 0,
        double vDesc = 0,
        double vFrete = 0,
        double vFreteTot = 0,
        double vOutro = 0,
        string? infAdProd = null,
        string cfop = "5102",
        int? indTot = null,
        double? vItem = null,
        string? cEan = null)
    {
        var accessKey = ("352508" + chave.PadLeft(38, '0'))[..44];
        qTrib = qTrib > 0 ? qTrib : qCom;
        uTrib ??= uCom;
        vUnTrib = vUnTrib > 0 ? vUnTrib : vUnCom;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string D(double v) => v.ToString("0.####", inv);
        var inf = string.IsNullOrWhiteSpace(infAdProd) ? "" : $"<infAdProd>{infAdProd}</infAdProd>";
        var ind = indTot is int i ? $"<indTot>{i}</indTot>" : "<indTot>1</indTot>";
        var vItemXml = vItem is double vi ? $"<vItem>{D(vi)}</vItem>" : "";
        var ean = cEan ?? "7891000000000";
        var ipi = vIpi > 0
            ? $"<IPI><IPITrib><vIPI>{D(vIpi)}</vIPI></IPITrib></IPI>"
            : "";
        var icms = (vIcmsSt > 0 || vFcpSt > 0)
            ? $"<ICMS><ICMS10><vICMSST>{D(vIcmsSt)}</vICMSST><vFCPST>{D(vFcpSt)}</vFCPST></ICMS10></ICMS>"
            : "";
        var vDescXml = vDesc > 0 ? $"<vDesc>{D(vDesc)}</vDesc>" : "";
        var vFreteXml = vFrete > 0 ? $"<vFrete>{D(vFrete)}</vFrete>" : "";
        var vOutroXml = vOutro > 0 ? $"<vOutro>{D(vOutro)}</vOutro>" : "";
        var totFrete = vFreteTot > 0 ? vFreteTot : vFrete;
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc>
              <NFe>
                <infNFe Id="NFe{accessKey}">
                  <ide><nNF>{chave}</nNF><serie>1</serie><dhEmi>2026-08-25T10:00:00-03:00</dhEmi></ide>
                  <emit><CNPJ>{cnpj}</CNPJ><xNome>{emit}</xNome><enderEmit><UF>SP</UF></enderEmit></emit>
                  <det nItem="1">
                    <prod>
                      <cProd>1</cProd><cEAN>{ean}</cEAN><xProd>{xProd}</xProd>
                      <CFOP>{cfop}</CFOP>
                      <uCom>{uCom}</uCom><qCom>{D(qCom)}</qCom><vUnCom>{D(vUnCom)}</vUnCom>
                      <vProd>{D(vProd)}</vProd>
                      <cEANTrib>{ean}</cEANTrib>
                      <uTrib>{uTrib}</uTrib><qTrib>{D(qTrib)}</qTrib><vUnTrib>{D(vUnTrib)}</vUnTrib>
                      {ind}{vItemXml}{vDescXml}{vFreteXml}{vOutroXml}
                    </prod>
                    <imposto>{icms}{ipi}</imposto>
                    {inf}
                  </det>
                  <total>
                    <ICMSTot>
                      <vProd>{D(vProd)}</vProd>
                      <vNF>{D(vNf)}</vNF>
                      <vST>{D(vIcmsSt)}</vST>
                      <vDesc>{D(vDesc)}</vDesc>
                      <vFrete>{D(totFrete)}</vFrete>
                      <vIPI>{D(vIpi)}</vIPI>
                    </ICMSTot>
                  </total>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }
}
