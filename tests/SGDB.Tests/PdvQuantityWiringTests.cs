using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 69P-B — orquestração do wiring real do PDV (sem WPF e sem banco).
/// Os testes da 69J só chamavam Consume() na mão; aqui o fluxo é o mesmo do PdvWindow:
/// ConfirmF6 / ArmExplicit → OnProductPending → PreviewInclude → CommitInclude → IncludeOrMerge.
/// </summary>
public class PdvQuantityWiringTests
{
    private const string IncidentEan13 = "7896588700608";
    private const string SampleGtin14 = "12345678901234";
    private const string SampleEan13 = "7891991000178";

    [Fact]
    public void F6_10_ScanNormal_LinhaQty10()
    {
        var session = ArmF6("10");
        Assert.True(session.IsArmed);
        Assert.Equal(10, session.ArmedQuantity);
        var (cart, counter) = NewCart();
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        Assert.Equal(10, cart[0].Quantity);
        Assert.False(session.IsArmed);
    }

    [Fact]
    public void ProximoScan_AposOneShot_Qty1()
    {
        var session = ArmF6("10");
        var (cart, counter) = NewCart();
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        Assert.Single(cart);
        Assert.Equal(11, cart[0].Quantity);
    }

    [Theory]
    [InlineData("10x")]
    [InlineData("10*")]
    [InlineData("10X")]
    public void MultiplicadorSearchBox_Scan_Qty10(string raw)
    {
        var parsed = PdvScanMultiplierParser.Parse(raw);
        Assert.True(parsed.Check.Allowed);
        Assert.Equal(PdvScanMultiplierKind.Armed, parsed.Kind);
        var session = new PdvIncludeQuantitySession();
        Assert.True(session.ArmExplicit(parsed.Quantity).Allowed);
        var (cart, counter) = NewCart();
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        Assert.Equal(10, cart[0].Quantity);
        Assert.False(session.IsArmed);
    }

    [Fact]
    public void ScanNormal_SemMultiplicador_Qty1()
    {
        var session = new PdvIncludeQuantitySession();
        var (cart, counter) = NewCart();
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        Assert.Equal(1, cart[0].Quantity);
    }

    [Fact]
    public void SetPendingProduct_NaoLimpaAntesDoConsume()
    {
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(10);
        // Equivale a SetPendingProduct sem ClearScanMultiplier.
        var qty = session.OnProductPending(1);
        Assert.Equal(10, qty);
        Assert.False(session.IsArmed);
    }

    [Fact]
    public void LookupManual_ComMultiplicador_AplicaNaProximaInclusao()
    {
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(10);
        var qtyBox = session.OnProductPending(1);
        Assert.Equal(10, qtyBox);
        var (cart, counter) = NewCart();
        IncludeFromQtyBox(session, cart, UnitProduct(), qtyBox, ref counter);
        Assert.Equal(10, cart[0].Quantity);
    }

    [Fact]
    public void EdicaoManualDaQtyBox_PrevaleceECancelaResidual()
    {
        var session = new PdvIncludeQuantitySession();
        session.OnProductPending(1);
        session.ConfirmF6("10");
        Assert.True(session.IsArmed);
        session.MarkQtyBoxEdited();
        var (cart, counter) = NewCart();
        IncludeFromQtyBox(session, cart, UnitProduct(), qtyBoxParsed: 7, ref counter);
        Assert.Equal(7, cart[0].Quantity);
        Assert.False(session.IsArmed);
    }

    [Fact]
    public void Esc_LimpaMultiplicador()
    {
        var session = ArmF6("10");
        session.Cancel();
        Assert.False(session.IsArmed);
        Assert.False(session.IsF6Editing);
        Assert.Equal(1, session.OnProductPending(1));
    }

    [Fact]
    public void Erro_LimpaMultiplicador()
    {
        var session = ArmF6("10");
        session.Cancel();
        Assert.False(session.IsArmed);
        var (cart, counter) = NewCart();
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        Assert.Equal(1, cart[0].Quantity);
    }

    [Fact]
    public void ProdutoNaoEncontrado_Limpa()
    {
        var session = ArmF6("23");
        session.Cancel();
        Assert.False(session.IsArmed);
        Assert.Equal(1, session.ArmedQuantity);
    }

    [Fact]
    public void NovaVenda_Limpa()
    {
        var session = ArmF6("10");
        session.ResetForNewSale();
        Assert.False(session.IsArmed);
        Assert.Equal(1, session.BaseQty);
        Assert.Equal(1, session.OnProductPending(1));
    }

    [Fact]
    public void Finalizacao_Limpa()
    {
        var session = ArmF6("10");
        session.Cancel();
        Assert.False(session.IsArmed);
    }

    [Fact]
    public void EnterF6_NaoVazaParaQtyBox()
    {
        var guard = new PdvF6EnterLeakGuard();
        var session = new PdvIncludeQuantitySession();
        session.OnProductPending(1);
        session.F6.Enter();
        guard.CaptureF6Enter();
        Assert.True(session.ConfirmF6("10").Allowed);
        Assert.False(guard.AllowQtyBoxInclude);
        Assert.True(session.IsArmed);
        guard.Release();
        Assert.True(guard.AllowQtyBoxInclude);
        var (cart, counter) = NewCart();
        IncludeFromQtyBox(session, cart, UnitProduct(), qtyBoxParsed: 1, ref counter);
        Assert.Equal(10, cart[0].Quantity);
    }

    [Fact]
    public void Ean13_NuncaViraQty()
    {
        var session = new PdvIncludeQuantitySession();
        session.F6.Enter();
        var check = session.ConfirmF6(IncidentEan13);
        Assert.False(check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, check.Reason);
        Assert.False(session.IsArmed);

        var parsed = PdvScanMultiplierParser.Parse(IncidentEan13);
        Assert.Equal(PdvScanMultiplierKind.None, parsed.Kind);
        Assert.Equal(IncidentEan13, parsed.Remainder);

        var guard = PdvQtyBoxGuard.Evaluate(IncidentEan13, IncidentEan13, "00178");
        Assert.False(guard.Accepted);
        Assert.Equal(PdvQtyBoxGuard.ResetQtyText, guard.QtyTextAfter);
    }

    [Fact]
    public void Gtin14_NuncaViraQty()
    {
        var session = new PdvIncludeQuantitySession();
        session.F6.Enter();
        Assert.False(session.ConfirmF6(SampleGtin14).Allowed);
        Assert.False(PdvQuantityValidationRules.EvaluateRaw(SampleGtin14).Allowed);
        var parsed = PdvScanMultiplierParser.Parse(SampleGtin14 + "*");
        Assert.False(parsed.Check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, parsed.Check.Reason);
    }

    [Fact]
    public void AcimaDe9999_Bloqueado()
    {
        var session = new PdvIncludeQuantitySession();
        session.F6.Enter();
        var check = session.ConfirmF6("10000");
        Assert.False(check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.AboveLineLimit, check.Reason);
        Assert.Equal(9999, PdvQuantityValidationRules.MaxQuantityPerLine);

        var parsed = PdvScanMultiplierParser.Parse("10000*");
        Assert.False(parsed.Check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.AboveLineLimit, parsed.Check.Reason);
    }

    [Fact]
    public void FardoCx_MultiplicadorVezesQuantidadeBase()
    {
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(10);
        var (cart, counter) = NewCart();
        const double packQty = 12;
        AutoIncludeScan(session, cart, UnitProduct(), packQty, ref counter, unitPrice: 1.5, stockUnits: 1);
        Assert.Equal(120, cart[0].Quantity);
        Assert.Equal(120, cart[0].StockQuantity);
        Assert.False(session.IsArmed);
    }

    [Fact]
    public void CigarroMaco_10MacosComerciais_EstoqueFisicoRespeitaFator()
    {
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(10);
        var (cart, counter) = NewCart();
        const double packFactor = 20;
        AutoIncludeScan(
            session, cart, CigaretteProduct(),
            1, ref counter, unitPrice: 8.5, stockUnits: packFactor);
        Assert.Equal(10, cart[0].Quantity);
        Assert.Equal(200, cart[0].StockQuantity);
        Assert.Equal(packFactor, cart[0].StockUnitsPerSale);
    }

    [Fact]
    public void RepeatScan_DepoisDoOneShot_VoltaMaisUm()
    {
        var session = ArmF6("10");
        var (cart, counter) = NewCart();
        var product = UnitProduct();
        AutoIncludeScan(session, cart, product, scanQty: 1, ref counter);
        AutoIncludeScan(session, cart, product, scanQty: 1, ref counter);
        AutoIncludeScan(session, cart, product, scanQty: 1, ref counter);
        Assert.Single(cart);
        Assert.Equal(12, cart[0].Quantity);
    }

    [Fact]
    public void MergeCarrinho_QtyAnteriorMaisMultiplicador()
    {
        var session = new PdvIncludeQuantitySession();
        var (cart, counter) = NewCart();
        var product = UnitProduct();
        AutoIncludeScan(session, cart, product, scanQty: 1, ref counter);
        AutoIncludeScan(session, cart, product, scanQty: 1, ref counter);
        Assert.Equal(2, cart[0].Quantity);
        session.ArmExplicit(10);
        AutoIncludeScan(session, cart, product, scanQty: 1, ref counter);
        Assert.Single(cart);
        Assert.Equal(12, cart[0].Quantity);
    }

    [Fact]
    public void Combined_10xMaisBarcode_Qty10()
    {
        var parsed = PdvScanMultiplierParser.Parse("10*" + SampleEan13);
        Assert.Equal(PdvScanMultiplierKind.Combined, parsed.Kind);
        Assert.Equal(10, parsed.Quantity);
        Assert.Equal(SampleEan13, parsed.Remainder);
        var session = new PdvIncludeQuantitySession();
        session.ArmExplicit(parsed.Quantity);
        var (cart, counter) = NewCart();
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        Assert.Equal(10, cart[0].Quantity);
        Assert.False(session.IsArmed);
    }

    [Fact]
    public void PesquisaManual_Antarctica_Enter_10_Enter()
    {
        Assert.True(PdvScanFocusPolicy.ShouldFocusQtyBox(fromBarcodeScan: false));
        Assert.False(PdvScanFocusPolicy.ShouldAutoInclude(fromBarcodeScan: false));
        var session = new PdvIncludeQuantitySession();
        var qtyBox = session.OnProductPending(1);
        Assert.Equal(1, qtyBox);
        Assert.False(session.IsArmed);
        session.MarkQtyBoxEdited();
        var (cart, counter) = NewCart();
        IncludeFromQtyBox(session, cart, UnitProduct(), qtyBoxParsed: 10, ref counter);
        Assert.Equal(10, cart[0].Quantity);
    }

    [Fact]
    public void F6_AposPendente_ProximaInclusaoUsa10_NaoVariosItens()
    {
        var session = new PdvIncludeQuantitySession();
        Assert.Equal(1, session.OnProductPending(1));
        session.F6.Enter();
        Assert.True(session.ConfirmF6("10").Allowed);
        Assert.True(session.IsArmed);
        var (cart, counter) = NewCart();
        IncludeFromQtyBox(session, cart, UnitProduct(), qtyBoxParsed: 1, ref counter);
        Assert.Equal(10, cart[0].Quantity);
        Assert.False(session.IsArmed);
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        Assert.Equal(11, cart[0].Quantity);
    }

    [Fact]
    public void F6_ProdutoConfirmado_EditaQtyBoxDoMesmoSku_NaoArmaProximoScan()
    {
        Assert.Equal(
            PdvF6Route.FocusConfirmedQtyBox,
            PdvProductIdentityPolicy.RouteF6(pendingConfirmed: true));
        var session = new PdvIncludeQuantitySession();
        Assert.Equal(1, session.OnProductPending(1));
        Assert.False(session.IsArmed);
        session.MarkQtyBoxEdited();
        var (cart, counter) = NewCart();
        IncludeFromQtyBox(session, cart, UnitProduct(), qtyBoxParsed: 10, ref counter);
        Assert.Single(cart);
        Assert.Equal(10, cart[0].Quantity);
        Assert.Equal(UnitProduct().Id, cart[0].ProductId);
        Assert.False(session.IsArmed);
        AutoIncludeScan(session, cart, UnitProduct(), scanQty: 1, ref counter);
        Assert.Equal(11, cart[0].Quantity);
    }

    [Fact]
    public void OnProductPending_NaoDoubleConsumeNoInclude()
    {
        var session = ArmF6("10");
        var qtyBox = session.OnProductPending(1);
        Assert.Equal(10, qtyBox);
        Assert.False(session.IsArmed);
        Assert.Equal(10, session.PreviewInclude(qtyBox));
        Assert.Equal(10, session.CommitInclude(qtyBox));
    }

    [Fact]
    public void PreviewLookup_NaoConsomeMultiplicador()
    {
        var session = ArmF6("10");
        Assert.True(session.IsArmed);
        Assert.Equal(10, session.OnProductPending(1));
    }

    [Fact]
    public void BarcodePuro_ContinuaProdutoNuncaQuantidade()
    {
        var parsed = PdvScanMultiplierParser.Parse(SampleEan13);
        Assert.Equal(PdvScanMultiplierKind.None, parsed.Kind);
        Assert.False(parsed.IsExplicit);
        Assert.Equal(SampleEan13, parsed.Remainder);
    }

    [Fact]
    public void TetoExtremoDaVenda_Permanece100000()
    {
        Assert.Equal(100_000, PdvQuantityValidationRules.ExtremeSaleTotal);
        Assert.True(PdvQuantityValidationRules.EvaluateCartTotal(99_999.99).Allowed);
        Assert.False(PdvQuantityValidationRules.EvaluateCartTotal(100_000.02).Allowed);
    }

    [Fact]
    public void Hint_SomeAposInclusao()
    {
        var session = ArmF6("10");
        Assert.True(session.IsArmed);
        session.OnProductPending(1);
        Assert.False(session.IsArmed);
    }

    static PdvIncludeQuantitySession ArmF6(string raw)
    {
        var session = new PdvIncludeQuantitySession();
        session.F6.Enter();
        var check = session.ConfirmF6(raw);
        Assert.True(check.Allowed);
        Assert.True(session.IsArmed);
        return session;
    }

    static void AutoIncludeScan(
        PdvIncludeQuantitySession session,
        List<PdvCartLine> cart,
        Product product,
        double scanQty,
        ref int counter,
        double unitPrice = 4.5,
        double stockUnits = 1)
    {
        var qtyBox = session.OnProductPending(scanQty);
        IncludeFromQtyBox(session, cart, product, qtyBox, ref counter, unitPrice, stockUnits);
    }

    static void IncludeFromQtyBox(
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

    static (List<PdvCartLine> Cart, int Counter) NewCart() => ([], 0);

    static Product UnitProduct() => new()
    {
        Id = 17,
        Name = "ANTARCTICA PILSEN GARRAFA VIDRO 300ML",
        Code = "00177",
        Barcode = SampleEan13,
        SalePrice = 4.5,
        Unit = "UN",
    };

    static Product CigaretteProduct() => new()
    {
        Id = 90,
        Name = "CIGARRO HOLLYWOOD VERMELHO",
        Code = "00900",
        Barcode = "7891136000008",
        SalePrice = 8.5,
        Unit = "UN",
        GroupName = "CIGARROS",
    };
}
