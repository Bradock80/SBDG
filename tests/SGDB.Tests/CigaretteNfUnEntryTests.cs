using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69L-B2 — caracterização: NF cigarro uCom=UN (maço) → estoque físico em cigarros,
/// cost_price/preco_compra em maço. Sem dupla conversão.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class CigaretteNfUnEntryTests
{
    private const string ProductName = "ROTHMANS HAND SELECTED RED";
    private const string Barcode = "789000069LB201";
    private const double MacoCost = 12.50;
    private const double Factor = 20;

    [Fact]
    public void ParseXml_10Un_Fator20_Vira200Fisicos_Nao10()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedMatchedCigarette(stock: 0, cost: 11.50);

        var item = ParseLine(BuildXml(qCom: 10, uCom: "UN", vUnCom: MacoCost, vProd: 125, chave: "1"));

        Assert.Equal(200, item.Quantity);
        Assert.Equal(10, item.NfQuantity);
        Assert.Equal("UN", item.NfUnit);
        Assert.Equal(Factor, item.PackFactor);
        Assert.Contains("maços", item.PackNote ?? "", StringComparison.OrdinalIgnoreCase);
        // Preço na grade após conversão = por cigarro (não 12,50).
        Assert.True(item.UnitPrice < 4.0);
        Assert.Equal(0.625, item.UnitPrice, 3);
    }

    [Fact]
    public void ParseXml_10Un_CustoCatalogoContinuaMaco()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedMatchedCigarette(stock: 0, cost: 11.50);

        var item = ParseLine(BuildXml(qCom: 10, uCom: "UN", vUnCom: MacoCost, vProd: 125, chave: "2"));

        Assert.Equal(MacoCost, item.ResolveCatalogCost());
        Assert.Equal(MacoCost, ProductPriceHelper.RoundPrice(item.UnitPrice * item.PackFactor));
        Assert.NotEqual(MacoCost, item.UnitPrice); // não gravar 12,50 como “por cigarro” no cadastro
    }

    [Fact]
    public void Apply_EstoqueZero_10Macos_Stock200_CustoMaco1250()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var cigId = SeedMatchedCigarette(stock: 0, cost: 11.50);

        var preview = NfeXmlImportService.ParseXml(
            BuildXml(qCom: 10, uCom: "UN", vUnCom: MacoCost, vProd: 125, chave: "3"));
        var result = NfeXmlImportService.Apply(preview, createMissingProducts: false,
            updateStock: true, updateCost: true);

        var p = ProductService.GetById(cigId)!;
        Assert.Equal(200, p.Stock);
        Assert.Equal(MacoCost, p.CostPrice);
        Assert.Equal(MacoCost, PrecoCompra(p));
        Assert.Equal(Factor, ProductExtra.Parse(p.ExtraJson).FatorEmbalagem);
        Assert.True(result.StockUpdated);
        Assert.Contains("10 CX", p.StockDisplay, StringComparison.Ordinal); // 200 UN (10 CX) ≈ 10 maços
    }

    [Fact]
    public void Apply_EstoqueExistente_MediaMaco_1150e1250_Vira1200_Stock400()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var cigId = SeedMatchedCigarette(stock: 200, cost: 11.50); // 10 maços @ 11,50

        var preview = NfeXmlImportService.ParseXml(
            BuildXml(qCom: 10, uCom: "UN", vUnCom: MacoCost, vProd: 125, chave: "4"));
        NfeXmlImportService.Apply(preview, createMissingProducts: false,
            updateStock: true, updateCost: true);

        var p = ProductService.GetById(cigId)!;
        Assert.Equal(400, p.Stock);
        Assert.Equal(12.00, p.CostPrice);
        Assert.Equal(MacoCost, PrecoCompra(p)); // último custo da NF (maço)
    }

    [Fact]
    public void ParseXml_NaoDuplicaConversao_200Nao4000()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedMatchedCigarette(stock: 0, cost: 11.50);

        var item = ParseLine(BuildXml(qCom: 10, uCom: "UN", vUnCom: MacoCost, vProd: 125, chave: "5"));
        Assert.Equal(200, item.Quantity);

        // ToPurchaseItem só reconverte se UnitPrice ainda parecer maço (>= 4).
        // Após ParseXml o preço já é por cigarro → Apply deve manter 200.
        var preview = NfeXmlImportService.ParseXml(
            BuildXml(qCom: 10, uCom: "UN", vUnCom: MacoCost, vProd: 125, chave: "5b"));
        NfeXmlImportService.Apply(preview, createMissingProducts: false,
            updateStock: true, updateCost: true);

        Assert.Equal(200, TestDataHelper.GetProductStock(preview.Items[0].MatchedProductId!.Value));
        Assert.NotEqual(4000, TestDataHelper.GetProductStock(preview.Items[0].MatchedProductId!.Value));
    }

    [Fact]
    public void ParseXml_1Pct_ComBox200s_Vira200Fisicos()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        // Sem match prévio: conversão só pelo nome BOX 200s + uCom PCT.
        var xml = BuildXml(
            qCom: 1, uCom: "PCT", vUnCom: 125, vProd: 125, chave: "6",
            xProd: "ROTHMANS HAND SELECTED RED BOX 200S",
            cEAN: "789000069LB206");

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(200, item.Quantity);
        Assert.Equal(Factor, item.PackFactor);
        // Custo de cadastro = maço (total 125 ÷ 10 maços), não o valor do pacote inteiro.
        Assert.Equal(12.50, item.ResolveCatalogCost());
    }

    [Fact]
    public void ParseXml_ProdutoNormal_10Un_Continua10()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var xml = BuildXml(
            qCom: 10, uCom: "UN", vUnCom: 2.50, vProd: 25, chave: "7",
            xProd: "AGUA MINERAL 500ML",
            cEAN: "789000069LB207");

        var item = Assert.Single(NfeXmlImportService.ParseXml(xml).Items);
        Assert.Equal(10, item.Quantity);
        Assert.Equal(2.50, item.UnitPrice);
    }

    [Fact]
    public void ParseXml_PrecoBaixo_TrataComoCigarrosJaFisicos_NaoMultiplicaPor20()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedMatchedCigarette(stock: 0, cost: 11.50);

        // Heurística: vUnCom < 4 → NF já em cigarros.
        var item = ParseLine(BuildXml(qCom: 10, uCom: "UN", vUnCom: 0.60, vProd: 6, chave: "8"));

        Assert.Equal(10, item.Quantity);
        Assert.StartsWith("10 cigarros", item.PackNote ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_Cancelar_Reverte200Fisicos_PreservaCustoMacoAnterior()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var cigId = SeedMatchedCigarette(stock: 0, cost: 11.50);

        var preview = NfeXmlImportService.ParseXml(
            BuildXml(qCom: 10, uCom: "UN", vUnCom: MacoCost, vProd: 125, chave: "9"));
        var applied = NfeXmlImportService.Apply(preview, createMissingProducts: false,
            updateStock: true, updateCost: true);
        Assert.Equal(200, TestDataHelper.GetProductStock(cigId));
        Assert.Equal(MacoCost, ProductService.GetById(cigId)!.CostPrice);

        PurchaseService.Cancel(applied.PurchaseId);

        var p = ProductService.GetById(cigId)!;
        Assert.Equal(0, p.Stock);
        Assert.Equal(11.50, p.CostPrice);
    }

    [Fact]
    public void Heuristica_Documentada_UnComUn_ComPrecoMaco_ExpandePorFator()
    {
        // Documenta a regra de ConvertCigaretteLineToStockUnits (via ParseXml):
        // uCom=UN + vUnCom >= 4 + produto cigarro → qCom × cigsPerPack.
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        SeedMatchedCigarette(stock: 0, cost: 11.50);

        var maco = ParseLine(BuildXml(qCom: 10, uCom: "UN", vUnCom: 4.00, vProd: 40, chave: "10a"));
        var fisico = ParseLine(BuildXml(qCom: 10, uCom: "UN", vUnCom: 3.99, vProd: 39.9, chave: "10b"));

        Assert.Equal(200, maco.Quantity);
        Assert.Equal(10, fisico.Quantity);
    }

    private static NfeImportItem ParseLine(string xml) =>
        Assert.Single(NfeXmlImportService.ParseXml(xml).Items);

    private static int SeedMatchedCigarette(double stock, double cost)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = Factor,
            QtdAtacado = Factor,
            PrecoAvulso = 1.50,
            PrecoAtacado = cost + 1,
            PrecoCompra = cost,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, barcode, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIG69LB2', $bc, $name, 'Cigarros', 'UN', $sale, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$bc", Barcode);
        cmd.Parameters.AddWithValue("$name", ProductName);
        cmd.Parameters.AddWithValue("$sale", cost + 1);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double PrecoCompra(Product p) =>
        ProductExtra.Parse(p.ExtraJson).PrecoCompra;

    private static string BuildXml(
        double qCom,
        string uCom,
        double vUnCom,
        double vProd,
        string chave,
        string? xProd = null,
        string? cEAN = null)
    {
        var name = xProd ?? ProductName;
        var ean = cEAN ?? Barcode;
        // Chave 44 dígitos única por cenário.
        var accessKey = ("352508" + chave.PadLeft(38, '0'))[..44];
        var q = qCom.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        var vu = vUnCom.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        var vp = vProd.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc>
              <NFe>
                <infNFe Id="NFe{accessKey}">
                  <ide>
                    <nNF>{chave}</nNF>
                    <serie>1</serie>
                    <dhEmi>2026-08-25T10:00:00-03:00</dhEmi>
                  </ide>
                  <emit>
                    <CNPJ>12345678000199</CNPJ>
                    <xNome>FORN CIG 69LB2</xNome>
                    <enderEmit><UF>SP</UF></enderEmit>
                  </emit>
                  <det nItem="1">
                    <prod>
                      <cProd>1</cProd>
                      <cEAN>{ean}</cEAN>
                      <xProd>{name}</xProd>
                      <uCom>{uCom}</uCom>
                      <qCom>{q}</qCom>
                      <vUnCom>{vu}</vUnCom>
                      <uTrib>{uCom}</uTrib>
                      <qTrib>{q}</qTrib>
                      <vUnTrib>{vu}</vUnTrib>
                      <vProd>{vp}</vProd>
                      <cEANTrib>{ean}</cEANTrib>
                    </prod>
                  </det>
                  <total>
                    <ICMSTot>
                      <vProd>{vp}</vProd>
                      <vNF>{vp}</vNF>
                      <vST>0</vST>
                      <vDesc>0</vDesc>
                    </ICMSTot>
                  </total>
                </infNFe>
              </NFe>
            </nfeProc>
            """;
    }
}
