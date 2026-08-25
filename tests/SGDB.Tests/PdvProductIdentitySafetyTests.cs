using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 69R-B — identidade do produto acima da velocidade.
/// Caso real: 2* + EAN Lucky nunca pode incluir Heineken.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvProductIdentitySafetyTests
{
    private const string LuckyEan = "7896058250123";
    private const string HeinekenEan = "7891991011110";
    private const string TrapEanInHeinekenName = "7890000999888";
    private const string UnknownEan = "7890000000099";
    private const string PackUnitEan = "7891000333333";
    private const string PackBoxEan = "7891000444444";
    private const string CigaretteEan = "7891136099999";
    private const string IncidentEan13 = "7896588700608";
    private const string SampleGtin14 = "12345678901234";

    [Fact]
    public void CasoLoja_2Estrela_EanLucky_IncluiLuckyQty2_NuncaHeineken()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var luckyId = SeedLuckyAndHeinekenTrap();

        var parsed = PdvScanMultiplierParser.Parse("2*");
        Assert.Equal(PdvScanMultiplierKind.Armed, parsed.Kind);

        var scan = PdvService.ResolveExactBarcode(LuckyEan);
        Assert.NotNull(scan);
        Assert.Equal(luckyId, scan.Product.Id);
        Assert.Contains("LUCKY", scan.Product.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HEINEKEN", scan.Product.Name, StringComparison.OrdinalIgnoreCase);

        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(parsed.Quantity);
        var (cart, counter) = NewCart();
        IncludeResolved(session, cart, scan, ref counter);

        Assert.Single(cart);
        Assert.Equal(luckyId, cart[0].ProductId);
        Assert.Equal(2, cart[0].Quantity);
        Assert.DoesNotContain(cart, l => l.Name.Contains("HEINEKEN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CasoLoja_2Estrela_EanInexistente_NaoIncluiNada()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        SeedLuckyAndHeinekenTrap();

        Assert.Null(PdvService.ResolveExactBarcode(UnknownEan));
        Assert.Null(PdvService.ResolveScan(UnknownEan));

        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(2);
        var (cart, counter) = NewCart();
        Assert.Empty(cart);
        Assert.Equal(0, counter);
        Assert.True(session.IsArmed);
    }

    [Fact]
    public void LookupAberto_Ean_BarcodeGanhaDoLookup()
    {
        Assert.True(PdvProductIdentityPolicy.BarcodeBeatsLookup(LuckyEan));
        Assert.False(PdvProductIdentityPolicy.AllowLookupEnter(LuckyEan, multiplierArmed: false));
        Assert.False(PdvProductIdentityPolicy.AllowLookupEnter(LuckyEan, multiplierArmed: true));
        Assert.True(PdvProductIdentityPolicy.AllowLookupEnter("ANTAR", multiplierArmed: false));
        Assert.False(PdvProductIdentityPolicy.AllowLookupEnter("ANTAR", multiplierArmed: true));
    }

    [Fact]
    public void SearchFirst_NaoRodaParaBarcodeExato_MesmoSeNomeDeOutroProdutoContemOEan()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        SeedLuckyAndHeinekenTrap();

        var likeHits = PdvService.SearchProducts(TrapEanInHeinekenName, 80);
        Assert.Contains(likeHits, p => p.Name.Contains("HEINEKEN", StringComparison.OrdinalIgnoreCase));

        Assert.Null(PdvService.ResolveScan(TrapEanInHeinekenName));
        Assert.Null(PdvService.ResolveExactBarcode(TrapEanInHeinekenName));
        Assert.Null(PdvService.FindProduct(TrapEanInHeinekenName));
    }

    [Fact]
    public void PrefixoAntar_F6SemConfirmacao_NaoVendeEscondido()
    {
        Assert.False(PdvProductIdentityPolicy.PreviewCanBeIncluded);
        Assert.Equal(
            PdvF6Route.QuantityFirstDiscardPreview,
            PdvProductIdentityPolicy.RouteF6(pendingConfirmed: false));
    }

    [Fact]
    public void Antar_Enter_ConfirmaProduto_IdentidadeCompleta()
    {
        Assert.Equal(
            "ANTARCTICA PILSEN 300ML",
            PdvProductIdentityPolicy.SearchLabel("ANTARCTICA PILSEN 300ML", null, 1));
        Assert.NotEqual("ANTAR", PdvProductIdentityPolicy.SearchLabel("ANTARCTICA PILSEN 300ML", null, 1));
        Assert.True(PdvScanFocusPolicy.ShouldFocusQtyBox(fromBarcodeScan: false));
        Assert.False(PdvScanFocusPolicy.ShouldAutoInclude(fromBarcodeScan: false));
    }

    [Fact]
    public void ProdutoConfirmado_F6_MantemIdentidade()
    {
        Assert.Equal(
            PdvF6Route.FocusConfirmedQtyBox,
            PdvProductIdentityPolicy.RouteF6(pendingConfirmed: true));
        var label = PdvProductIdentityPolicy.SearchLabel("ANTARCTICA PILSEN 300ML", null, 1);
        Assert.Equal("ANTARCTICA PILSEN 300ML", label);
    }

    [Fact]
    public void F6_Mais10_Enter_IncluiProdutoConfirmadoQty10()
    {
        var session = new PdvIncludeQuantitySession();
        Assert.Equal(1, session.OnProductPending(1));
        Assert.False(session.IsArmed);
        session.MarkQtyBoxEdited();
        var (cart, counter) = NewCart();
        var product = LuckyProduct();
        IncludeFromQtyBox(session, cart, product, 10, ref counter);
        Assert.Single(cart);
        Assert.Equal(product.Id, cart[0].ProductId);
        Assert.Equal(10, cart[0].Quantity);
    }

    [Fact]
    public void DepoisInclui_LimpaPendenteEMultiplicador()
    {
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(2);
        var qty = session.OnProductPending(1);
        Assert.Equal(2, qty);
        Assert.False(session.IsArmed);
        var (cart, counter) = NewCart();
        IncludeFromQtyBox(session, cart, LuckyProduct(), qty, ref counter);
        Assert.False(session.IsArmed);
        Assert.Equal(1, session.OnProductPending(1));
    }

    [Theory]
    [InlineData("10x")]
    [InlineData("10*")]
    [InlineData("10X")]
    public void Multiplicador_MaisEanExato_Qty10(string raw)
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var luckyId = SeedLuckyAndHeinekenTrap();

        var parsed = PdvScanMultiplierParser.Parse(raw);
        Assert.Equal(PdvScanMultiplierKind.Armed, parsed.Kind);
        Assert.Equal(10, parsed.Quantity);
        Assert.True(string.IsNullOrEmpty(parsed.Remainder));

        var scan = PdvService.ResolveExactBarcode(LuckyEan);
        Assert.NotNull(scan);
        Assert.Equal(luckyId, scan.Product.Id);

        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(parsed.Quantity);
        var (cart, counter) = NewCart();
        IncludeResolved(session, cart, scan, ref counter);
        Assert.Equal(10, cart[0].Quantity);
        Assert.Equal(luckyId, cart[0].ProductId);
        Assert.False(session.IsArmed);
    }

    [Fact]
    public void ProximoScan_VoltaQty1()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var luckyId = SeedLuckyAndHeinekenTrap();
        var scan = PdvService.ResolveExactBarcode(LuckyEan)!;
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(2);
        var (cart, counter) = NewCart();
        IncludeResolved(session, cart, scan, ref counter);
        IncludeResolved(session, cart, scan, ref counter);
        Assert.Single(cart);
        Assert.Equal(luckyId, cart[0].ProductId);
        Assert.Equal(3, cart[0].Quantity);
    }

    [Fact]
    public void BarcodeEmbalagemExato_ResolveOMesmoSku()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedPack();
        var pack = PdvService.ResolveExactBarcode(PackBoxEan);
        Assert.NotNull(pack);
        Assert.Equal(id, pack.Product.Id);
        Assert.Equal(12, pack.Quantity);
        Assert.True(pack.IsPackSale);
        Assert.Null(PdvService.ResolveExactBarcode(UnknownEan));
    }

    [Fact]
    public void Fardo_VezesMultiplicador_Fisicos24()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedPack();
        var pack = PdvService.ResolveExactBarcode(PackBoxEan)!;
        Assert.Equal(id, pack.Product.Id);
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(2);
        var (cart, counter) = NewCart();
        IncludeResolved(session, cart, pack, ref counter);
        Assert.Equal(24, cart[0].Quantity);
        Assert.Equal(24, cart[0].StockQuantity);
        Assert.Equal(id, cart[0].ProductId);
    }

    [Fact]
    public void CigarroMaco_VezesMultiplicador_DoisMacosComerciais()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var id = SeedCigarette();
        var scan = PdvService.ResolveExactBarcode(CigaretteEan)!;
        Assert.Equal(id, scan.Product.Id);
        Assert.Equal(1, scan.Quantity);
        Assert.Equal(20, scan.StockUnitsPerSale);
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(2);
        var (cart, counter) = NewCart();
        IncludeResolved(session, cart, scan, ref counter);
        Assert.Equal(2, cart[0].Quantity);
        Assert.Equal(40, cart[0].StockQuantity);
        Assert.Equal(id, cart[0].ProductId);
    }

    [Fact]
    public void EanNaoViraQuantidade()
    {
        var session = new PdvIncludeQuantitySession();
        session.F6.Enter();
        var check = session.ConfirmF6(IncidentEan13);
        Assert.False(check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, check.Reason);
        Assert.Equal(PdvScanMultiplierKind.None, PdvScanMultiplierParser.Parse(IncidentEan13).Kind);
    }

    [Fact]
    public void GtinNaoViraQuantidade()
    {
        var session = new PdvIncludeQuantitySession();
        session.F6.Enter();
        Assert.False(session.ConfirmF6(SampleGtin14).Allowed);
        var parsed = PdvScanMultiplierParser.Parse(SampleGtin14 + "*");
        Assert.False(parsed.Check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, parsed.Check.Reason);
    }

    [Fact]
    public void LookupNaoInterceptaScanner()
    {
        Assert.True(PdvProductIdentityPolicy.RequireExactBarcode(multiplierArmed: true));
        Assert.True(PdvProductIdentityPolicy.BarcodeBeatsLookup(LuckyEan));
        Assert.False(PdvProductIdentityPolicy.AllowLookupEnter(LuckyEan, multiplierArmed: true));
        Assert.False(PdvProductIdentityPolicy.AllowAutoInclude(
            exactBarcodeResolved: true, multiplierWasArmed: true));
        Assert.True(PdvProductIdentityPolicy.AllowAutoInclude(
            exactBarcodeResolved: true, multiplierWasArmed: false));
    }

    [Fact]
    public void EstadoResidualInferior_LimpoSemPendente()
    {
        Assert.Equal("1,000", PdvProductIdentityPolicy.ResidualQtyText);
        Assert.Equal("0,00", PdvProductIdentityPolicy.ResidualMoneyText);
        Assert.Equal(
            "Próxima quantidade: 2 — aguardando leitura",
            PdvProductIdentityPolicy.ArmedHint("2"));
        Assert.Equal(
            "LUCKY STRIKE ORIGINAL BOX — QTD 2",
            PdvProductIdentityPolicy.SearchLabel("LUCKY STRIKE ORIGINAL BOX", null, 2));
    }

    [Fact]
    public void QtyFirst_2Estrela_SearchBoxFicaSemResto()
    {
        var parsed = PdvScanMultiplierParser.Parse("2*");
        Assert.Equal(PdvScanMultiplierKind.Armed, parsed.Kind);
        Assert.True(string.IsNullOrEmpty(parsed.Remainder));
        Assert.Equal(
            "Próxima quantidade: 2 — aguardando leitura",
            PdvProductIdentityPolicy.ArmedHint("2"));
    }

    [Fact]
    public void ResolveScan_BarcodeDoLucky_NuncaDevolveHeineken()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var luckyId = SeedLuckyAndHeinekenTrap();
        var scan = PdvService.ResolveScan(LuckyEan);
        Assert.NotNull(scan);
        Assert.Equal(luckyId, scan.Product.Id);
        Assert.DoesNotContain("HEINEKEN", scan.Product.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static void BeginStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    private static int SeedLuckyAndHeinekenTrap()
    {
        SeedProduct("HEI01", $"HEINEKEN LONG NECK {TrapEanInHeinekenName}", HeinekenEan, 8.90, 50);
        return SeedProduct("LCK01", "LUCKY STRIKE ORIGINAL BOX", LuckyEan, 11.50, 80, group: "Cigarros");
    }

    private static int SeedPack() =>
        SeedProduct(
            "FD12",
            "REFRIGERANTE CX 12",
            PackUnitEan,
            2.50,
            100,
            group: "Bebidas",
            extraJson: new ProductExtra
            {
                FatorEmbalagem = 12,
                PrecoAtacado = 24,
                QtdAtacado = 12,
                BarcodeEmbalagem = PackBoxEan,
            }.ToJson());

    private static int SeedCigarette() =>
        SeedProduct(
            "CIGL",
            "CIGARRO HOLLYWOOD VERMELHO",
            CigaretteEan,
            8.50,
            200,
            group: "Cigarros",
            extraJson: new ProductExtra
            {
                FatorEmbalagem = 20,
                PrecoAtacado = 8.50,
                QtdAtacado = 20,
            }.ToJson());

    private static int SeedProduct(
        string code, string name, string? barcode, double salePrice, double stock,
        string? group = null, string? extraJson = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, barcode, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $bc, $name, $group, 'UN', $sale, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$bc", (object?)barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$group", (object?)group ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sale", salePrice);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", salePrice * 0.5);
        cmd.Parameters.AddWithValue("$extra", extraJson ?? "{}");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void IncludeResolved(
        PdvIncludeQuantitySession session,
        List<PdvCartLine> cart,
        PdvScanResult scan,
        ref int counter)
    {
        var qtyBox = session.OnProductPending(scan.Quantity);
        IncludeFromQtyBox(
            session, cart, scan.Product, qtyBox, ref counter,
            scan.UnitPrice, scan.StockUnitsPerSale);
    }

    private static void IncludeFromQtyBox(
        PdvIncludeQuantitySession session,
        List<PdvCartLine> cart,
        Product product,
        double qtyBoxParsed,
        ref int counter,
        double unitPrice = 4.5,
        double stockUnits = 1)
    {
        var preview = session.PreviewInclude(qtyBoxParsed);
        var qtyCheck = PdvQuantityValidationRules.EvaluateQuantity(preview, product.Barcode, product.Code);
        Assert.True(qtyCheck.Allowed);
        var qty = session.CommitInclude(qtyBoxParsed);
        PdvCartHelper.IncludeOrMerge(cart, product, qty, unitPrice, stockUnits, ref counter);
        session.Cancel();
    }

    private static (List<PdvCartLine> Cart, int Counter) NewCart() => ([], 0);

    private static Product LuckyProduct() => new()
    {
        Id = 501,
        Name = "LUCKY STRIKE ORIGINAL BOX",
        Code = "LCK01",
        Barcode = LuckyEan,
        SalePrice = 11.50,
        Unit = "UN",
    };
}
