using SGDB.Domain.Purchases;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69S-B6 — totais NFe + sugestão de fator (histórico não converte sozinho).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class NfeTotalsAndPackSuggestionTests
{
    [Theory]
    [InlineData(100, 0, 100, 100, "fat.vLiq", true)]
    [InlineData(100, 15, 115, 115, "fat.vLiq", true)]
    public void Totais_VprodVnfFatLiq_Reconciliam(
        double vProd, double charges, double vNf, double fatLiq, string source, bool ok)
    {
        var landed = vProd + charges;
        var r = NfeCostReconciliation.Reconcile([landed], fatLiq, 0, 0, vNf);
        Assert.Equal(ok, r.IsReconciled);
        Assert.Equal(source, r.ExpectedSource);
        Assert.Equal(landed, r.CalculatedEffectiveCost, 2);
        Assert.Equal(fatLiq, r.ExpectedPayable, 2);
    }

    [Fact]
    public void Totais_Desconto_VnfEFatLiq100()
    {
        // vProd 120, desc 20 → vNF/fat 100; custo efetivo pago = 100
        var r = NfeCostReconciliation.Reconcile([100], fatLiq: 100, dupSum: 0, pagSum: 0, headerVNf: 100);
        Assert.True(r.IsReconciled);
        Assert.Equal("fat.vLiq", r.ExpectedSource);
    }

    [Fact]
    public void Totais_Frete_Vnf110()
    {
        var r = NfeCostReconciliation.Reconcile([110], fatLiq: 110, dupSum: 0, pagSum: 0, headerVNf: 110);
        Assert.True(r.IsReconciled);
        Assert.Equal(110, r.ExpectedPayable, 2);
    }

    [Fact]
    public void Totais_VnfDiferenteDeFatLiq_UsaFatLiq()
    {
        var r = NfeCostReconciliation.Reconcile([100], fatLiq: 100, dupSum: 0, pagSum: 0, headerVNf: 115);
        Assert.True(r.IsReconciled);
        Assert.Equal("fat.vLiq", r.ExpectedSource);
        Assert.Equal(100, r.ExpectedPayable, 2);
    }

    [Fact]
    public void Totais_DuplicatasSomamFatLiq_SemFat_UsaDup()
    {
        var r = NfeCostReconciliation.Reconcile([100], fatLiq: 0, dupSum: 100, pagSum: 0, headerVNf: 100);
        Assert.True(r.IsReconciled);
        Assert.Equal("duplicatas", r.ExpectedSource);
    }

    [Fact]
    public void Totais_BonificacaoExcluida_DoEsperadoViaVnf()
    {
        // Compra 100 + bonif 20 (excluída) → esperado vNF-20
        var r = NfeCostReconciliation.Reconcile(
            [100], fatLiq: 0, dupSum: 0, pagSum: 0, headerVNf: 120, excludedGross: 20);
        Assert.True(r.IsReconciled);
        Assert.Equal("vNF − itens não pagos", r.ExpectedSource);
        Assert.Equal(100, r.ExpectedPayable, 2);
    }

    [Fact]
    public void Xml_IndTot0_NaoEntraNoPayable()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = TwoLineXml(
            ("PROD PAGO", 5102, 1, 50, 50),
            ("PROD IND0", 5102, 0, 20, 20),
            vNf: 50);
        var preview = NfeXmlImportService.ParseXml(xml);
        Assert.Equal(2, preview.Items.Count);
        var paid = preview.Items.Where(i => i.IncludeInPayable).Sum(i => i.EffectiveLineCost);
        Assert.Equal(50, paid, 2);
        Assert.Contains(preview.Items, i => !i.IncludeInPayable);
    }

    [Fact]
    public void Xml_Remessa_StatusRemessa_NaoPago()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "b6rem",
            xProd: "AMOSTRA GRATIS",
            cfop: "5911",
            qCom: 1, uCom: "UN", vUnCom: 10, vProd: 10,
            vNf: 10);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(NfeEffectiveCostStatus.Remessa, item.CostStatus);
        Assert.False(item.IncludeInPayable);
        Assert.False(item.NeedsPackFactorReview);
    }

    [Fact]
    public void Cx9324_ComFator30_ConverteFisico()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedProduct("PIRAQUE RECHEADO MORANGO CX30", fator: 30, barcode: "7896024760364", pack: "17896024760361");

        var xml = LineXml(
            chave: "b6cx30",
            xProd: "PIRAQUE RECHEADO MORANGO",
            cEan: "17896024760361",
            cEanTrib: "7896024760364",
            qCom: 1, uCom: "CX", vUnCom: 93.24, vProd: 93.24,
            qTrib: 1, uTrib: "CX", vUnTrib: 93.24,
            vNf: 93.24);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(30, item.Quantity, 4);
        Assert.Equal(3.108, item.UnitPrice, 3);
        Assert.Equal(93.24, item.EffectiveLineCost, 2);
        Assert.False(item.NeedsPackFactorReview);
    }

    [Fact]
    public void Cx9324_SemFatorConfiavel_NaoConverte_MarcaRevisarComSugestao()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedProduct("PIRAQUE RECHEADO MORANGO", fator: 1, barcode: "7896024760364", pack: null);
        // Histórico 30 UN (já físico) — sugestão, não conversão automática.
        SeedPurchaseHistory(id, [30, 30, 30, 10, 30]);

        var xml = LineXml(
            chave: "b6rev",
            xProd: "PIRAQUE RECHEADO MORANGO",
            cEan: "7896024760364",
            cEanTrib: "7896024760364",
            qCom: 1, uCom: "UN", vUnCom: 93.24, vProd: 93.24,
            qTrib: 1, uTrib: "UN", vUnTrib: 93.24,
            vNf: 93.24);
        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(1, item.Quantity, 4);
        Assert.Equal(93.24, item.UnitPrice, 2);
        Assert.True(item.NeedsPackFactorReview);
        Assert.Equal(30, item.SuggestedPackFactor, 4);
        Assert.Equal(NfeEffectiveCostStatus.Revisar, item.CostStatus);
        Assert.Contains("Possível embalagem", item.PackNote ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SugestaoHistorico_Piraque_AltaConfianca30()
    {
        var r = PurchasePackFactorSuggestion.SuggestFromHistory(1, [30, 30, 30, 10, 30]);
        Assert.Equal(30, r.SuggestedFactor);
        Assert.Equal(PackFactorConfidence.High, r.Confidence);
        Assert.True(r.RecommendReview);
        Assert.Contains("30", r.Evidence);
    }

    [Fact]
    public void SugestaoHistorico_NaoConverteQuandoFatorJaCadastrado()
    {
        var r = PurchasePackFactorSuggestion.SuggestFromHistory(23, [23, 46, 23]);
        Assert.Equal(23, r.SuggestedFactor);
        Assert.False(r.RecommendReview);
    }

    [Fact]
    public void ShouldFlagPackReview_SoQuandoNaoConvertido()
    {
        var sug = PurchasePackFactorSuggestion.SuggestFromHistory(1, [30, 30, 30, 30]);
        Assert.True(PurchasePackFactorSuggestion.ShouldFlagPackReview(1, 1, sug));
        Assert.False(PurchasePackFactorSuggestion.ShouldFlagPackReview(1, 30, sug));
    }

    [Fact]
    public void Preview_HeaderFields_ExposeVprodVnfFat()
    {
        using var _ = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = LineXml(
            chave: "b6hdr",
            xProd: "AGUA 500ML",
            qCom: 10, uCom: "UN", vUnCom: 1, vProd: 10,
            vNf: 10, fatLiq: 10, vIpi: 0, vSt: 0);
        var preview = NfeXmlImportService.ParseXml(xml);
        Assert.Equal(10, preview.HeaderVProd, 2);
        Assert.Equal(10, preview.HeaderVNf, 2);
        Assert.Equal(10, preview.FatLiq, 2);
        Assert.Equal(10, preview.TotalValue, 2);
    }

    private static int SeedProduct(string name, double fator, string barcode, string? pack)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator > 1 ? fator : 0,
            PrecoCompra = 3,
            BarcodeEmbalagem = pack,
        };
        return ProductService.Create(new ProductInput
        {
            Code = "B6" + Guid.NewGuid().ToString("N")[..6],
            Barcode = barcode,
            Name = name,
            GroupName = "BISCOITO",
            Unit = "UN",
            CostPrice = 3,
            SalePrice = 4.5,
            Stock = 0,
            Extra = extra,
            Active = true,
        }).Id;
    }

    private static void SeedPurchaseHistory(int productId, IReadOnlyList<double> qtys)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();

        int supplierId;
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM people WHERE active = 1 LIMIT 1;";
            var existing = find.ExecuteScalar();
            if (existing is null)
            {
                using var p = conn.CreateCommand();
                p.Transaction = tx;
                p.CommandText = """
                    INSERT INTO people (person_type, name, cpf_cnpj, active, created_at, person_kind)
                    VALUES ('PJ', 'FORN B6', '00000000000191', 1, datetime('now'), 'supplier');
                    SELECT last_insert_rowid();
                    """;
                supplierId = Convert.ToInt32(p.ExecuteScalar());
            }
            else
            {
                supplierId = Convert.ToInt32(existing);
            }
        }

        int purchaseId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO purchases (
                    supplier_id, emission_date, entry_date, series, number, status, total, gerar_estoque, notes, created_at
                ) VALUES (
                    $s, date('now'), date('now'), '1', 'B6H', 'fechada', 0, 0, 'hist-b6', datetime('now','localtime')
                );
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$s", supplierId);
            purchaseId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        foreach (var q in qtys)
        {
            using var item = conn.CreateCommand();
            item.Transaction = tx;
            item.CommandText = """
                INSERT INTO purchase_items (purchase_id, product_id, product_name, quantity, unit_price, subtotal)
                VALUES ($p, $prod, 'HIST', $q, 3.0, $sub);
                """;
            item.Parameters.AddWithValue("$p", purchaseId);
            item.Parameters.AddWithValue("$prod", productId);
            item.Parameters.AddWithValue("$q", q);
            item.Parameters.AddWithValue("$sub", q * 3.0);
            item.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static string LineXml(
        string chave,
        string xProd,
        double qCom,
        string uCom,
        double vUnCom,
        double vProd,
        double vNf,
        string cfop = "5102",
        string? cEan = null,
        string? cEanTrib = null,
        double qTrib = 0,
        string? uTrib = null,
        double vUnTrib = 0,
        double fatLiq = 0,
        double vIpi = 0,
        double vSt = 0)
    {
        var accessKey = ("332605" + chave.PadLeft(38, '0'))[..44];
        qTrib = qTrib > 0 ? qTrib : qCom;
        uTrib ??= uCom;
        vUnTrib = vUnTrib > 0 ? vUnTrib : vUnCom;
        cEan ??= "SEM GTIN";
        cEanTrib ??= cEan;
        if (fatLiq <= 0) fatLiq = vNf;
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
              <NFe>
                <infNFe Id="NFe{accessKey}">
                  <ide><nNF>1</nNF><serie>1</serie><dhEmi>2026-05-01T10:00:00-03:00</dhEmi></ide>
                  <emit><CNPJ>56228356014272</CNPJ><xNome>CRBS S/A</xNome></emit>
                  <det nItem="1">
                    <prod>
                      <cProd>1</cProd><cEAN>{cEan}</cEAN><xProd>{xProd}</xProd>
                      <uCom>{uCom}</uCom><qCom>{D(qCom)}</qCom><vUnCom>{D(vUnCom)}</vUnCom>
                      <vProd>{D(vProd)}</vProd>
                      <cEANTrib>{cEanTrib}</cEANTrib>
                      <uTrib>{uTrib}</uTrib><qTrib>{D(qTrib)}</qTrib><vUnTrib>{D(vUnTrib)}</vUnTrib>
                      <CFOP>{cfop}</CFOP><indTot>1</indTot>
                    </prod>
                  </det>
                  <total><ICMSTot>
                    <vProd>{D(vProd)}</vProd><vNF>{D(vNf)}</vNF>
                    <vST>{D(vSt)}</vST><vDesc>0</vDesc><vFrete>0</vFrete><vOutro>0</vOutro><vIPI>{D(vIpi)}</vIPI>
                  </ICMSTot></total>
                  <cobr><fat><nFat>1</nFat><vOrig>{D(fatLiq)}</vOrig><vDesc>0</vDesc><vLiq>{D(fatLiq)}</vLiq></fat></cobr>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }

    private static string TwoLineXml(
        (string Name, int Cfop, int IndTot, double VProd, double VUn) a,
        (string Name, int Cfop, int IndTot, double VProd, double VUn) b,
        double vNf)
    {
        var accessKey = "33260556228356014272550240000000000000000001";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
              <NFe>
                <infNFe Id="NFe{accessKey}">
                  <ide><nNF>2</nNF><serie>1</serie><dhEmi>2026-05-01T10:00:00-03:00</dhEmi></ide>
                  <emit><CNPJ>56228356014272</CNPJ><xNome>CRBS S/A</xNome></emit>
                  <det nItem="1"><prod>
                    <cProd>1</cProd><cEAN>SEM GTIN</cEAN><xProd>{a.Name}</xProd>
                    <uCom>UN</uCom><qCom>1</qCom><vUnCom>{D(a.VUn)}</vUnCom><vProd>{D(a.VProd)}</vProd>
                    <cEANTrib>SEM GTIN</cEANTrib><uTrib>UN</uTrib><qTrib>1</qTrib><vUnTrib>{D(a.VUn)}</vUnTrib>
                    <CFOP>{a.Cfop}</CFOP><indTot>{a.IndTot}</indTot>
                  </prod></det>
                  <det nItem="2"><prod>
                    <cProd>2</cProd><cEAN>SEM GTIN</cEAN><xProd>{b.Name}</xProd>
                    <uCom>UN</uCom><qCom>1</qCom><vUnCom>{D(b.VUn)}</vUnCom><vProd>{D(b.VProd)}</vProd>
                    <cEANTrib>SEM GTIN</cEANTrib><uTrib>UN</uTrib><qTrib>1</qTrib><vUnTrib>{D(b.VUn)}</vUnTrib>
                    <CFOP>{b.Cfop}</CFOP><indTot>{b.IndTot}</indTot>
                  </prod></det>
                  <total><ICMSTot>
                    <vProd>{D(a.VProd + b.VProd)}</vProd><vNF>{D(vNf)}</vNF>
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
