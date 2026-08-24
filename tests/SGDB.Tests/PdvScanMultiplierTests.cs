using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69J — quantidade no PDV: pesquisa manual, SelectAll só no fluxo digitado,
/// multiplicador explícito 10*/10x, one-shot, sem reabrir o incidente 25338.
/// </summary>
public class PdvScanMultiplierTests
{
    private const string IncidentEan13 = "7896588700608";
    private const string SampleEan13 = "7891991000178";
    private const string SampleGtin14 = "12345678901234";

    [Fact]
    public void PesquisaManual_FocaQtyBox()
    {
        Assert.True(PdvScanFocusPolicy.ShouldFocusQtyBox(fromBarcodeScan: false));
        Assert.False(PdvScanFocusPolicy.ShouldAutoInclude(fromBarcodeScan: false));
    }

    [Fact]
    public void PesquisaManual_SelecionaConteudoDaQtyBox()
    {
        Assert.True(PdvScanFocusPolicy.ShouldSelectAllQty(fromBarcodeScan: false));
    }

    [Fact]
    public void ScannerNormal_NaoFocaNemSelectAllNaQtyBox()
    {
        Assert.False(PdvScanFocusPolicy.ShouldFocusQtyBox(fromBarcodeScan: true));
        Assert.False(PdvScanFocusPolicy.ShouldSelectAllQty(fromBarcodeScan: true));
        Assert.False(PdvScanFocusPolicy.SelectAllQuantityAfterScan);
        Assert.True(PdvScanFocusPolicy.ShouldAutoInclude(fromBarcodeScan: true));
    }

    [Fact]
    public void ScannerNormal_IncluiQuantidade1()
    {
        var (cart, counter) = NewCart();
        var product = SampleProduct();
        PdvCartHelper.IncludeOrMerge(cart, product, QtyForScan(new PdvScanMultiplierState()), 4.5, 1, ref counter);
        Assert.Single(cart);
        Assert.Equal(1, cart[0].Quantity);
    }

    [Fact]
    public void ScannerRepetido_SomaMaisUm()
    {
        var (cart, counter) = NewCart();
        var product = SampleProduct();
        PdvCartHelper.IncludeOrMerge(cart, product, 1, 4.5, 1, ref counter);
        PdvCartHelper.IncludeOrMerge(cart, product, 1, 4.5, 1, ref counter);
        Assert.Single(cart);
        Assert.Equal(2, cart[0].Quantity);
    }

    [Fact]
    public void Multiplicador10Asterisco_MaisScanner_Qty10()
    {
        AssertIncludeFromSearch("10*", 10);
    }

    [Fact]
    public void Multiplicador10x_MaisScanner_Qty10()
    {
        AssertIncludeFromSearch("10x", 10);
        AssertIncludeFromSearch("10X", 10);
    }

    [Fact]
    public void Multiplicador23Asterisco_MaisScanner_Qty23()
    {
        AssertIncludeFromSearch("23*", 23);
    }

    [Fact]
    public void MultiplicadorCombinado_CodigoNaMesmaLeitura()
    {
        var parsed = PdvScanMultiplierParser.Parse("10*" + SampleEan13);
        Assert.True(parsed.Check.Allowed);
        Assert.Equal(PdvScanMultiplierKind.Combined, parsed.Kind);
        Assert.Equal(10, parsed.Quantity);
        Assert.Equal(SampleEan13, parsed.Remainder);

        var (cart, counter) = NewCart();
        PdvCartHelper.IncludeOrMerge(cart, SampleProduct(), parsed.Quantity, 4.5, 1, ref counter);
        Assert.Equal(10, cart[0].Quantity);
    }

    [Fact]
    public void Multiplicador_ConsumidoUmaUnicaVez()
    {
        var state = new PdvScanMultiplierState();
        Assert.True(state.TryArm(10).Allowed);
        Assert.Equal(10, QtyForScan(state));
        Assert.False(state.IsArmed);
        Assert.Equal(1, QtyForScan(state));
        Assert.Equal(1, QtyForScan(state));
    }

    [Fact]
    public void Erro_LimpaMultiplicador()
    {
        var state = new PdvScanMultiplierState();
        Assert.True(state.TryArm(10).Allowed);
        state.Clear();
        Assert.False(state.IsArmed);
        Assert.Equal(1, state.Quantity);
        Assert.Equal(1, QtyForScan(state));
    }

    [Fact]
    public void Esc_LimpaMultiplicador()
    {
        var state = new PdvScanMultiplierState();
        Assert.True(state.TryArm(10).Allowed);
        state.Clear();
        Assert.False(state.IsArmed);
        Assert.Equal(1, QtyForScan(state));
    }

    [Fact]
    public void FinalizarOuLimparVenda_LimpaMultiplicador()
    {
        var state = new PdvScanMultiplierState();
        Assert.True(state.TryArm(23).Allowed);
        state.Clear();
        Assert.False(state.IsArmed);
        Assert.Equal(1, state.Quantity);
    }

    [Fact]
    public void ZeroAsterisco_Rejeitado()
    {
        var parsed = PdvScanMultiplierParser.Parse("0*");
        Assert.False(parsed.Check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.Invalid, parsed.Check.Reason);
        Assert.Equal(PdvQuantityValidationRules.MessageInvalidQuantity, parsed.Check.Message);
        Assert.False(parsed.IsExplicit);
    }

    [Fact]
    public void Limite10000Asterisco_Rejeitado()
    {
        var parsed = PdvScanMultiplierParser.Parse("10000*");
        Assert.False(parsed.Check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.AboveLineLimit, parsed.Check.Reason);
        Assert.Equal(PdvQuantityValidationRules.MessageQuantityLimit, parsed.Check.Message);
        Assert.Equal(PdvQuantityValidationRules.MaxQuantityPerLine, 9999);
    }

    [Fact]
    public void Ean13_NaoPodeSerMultiplicadorNemQuantidade()
    {
        var parsed = PdvScanMultiplierParser.Parse(IncidentEan13 + "*");
        Assert.False(parsed.Check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, parsed.Check.Reason);

        var bare = PdvScanMultiplierParser.Parse(IncidentEan13);
        Assert.Equal(PdvScanMultiplierKind.None, bare.Kind);
        Assert.True(bare.Check.Allowed);
        Assert.Equal(IncidentEan13, bare.Remainder);

        var qty = PdvQuantityValidationRules.EvaluateRaw(IncidentEan13);
        Assert.False(qty.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, qty.Reason);
    }

    [Fact]
    public void Gtin14_NaoPodeSerQuantidadeNemMultiplicador()
    {
        Assert.False(PdvQuantityValidationRules.EvaluateRaw(SampleGtin14).Allowed);
        var parsed = PdvScanMultiplierParser.Parse(SampleGtin14 + "*");
        Assert.False(parsed.Check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.LooksLikeGtin, parsed.Check.Reason);
    }

    [Fact]
    public void QuantidadeDecimalManual_ContinuaValida()
    {
        Assert.True(PdvQuantityValidationRules.EvaluateRaw("0,250").Allowed);
        Assert.True(PdvQuantityValidationRules.EvaluateRaw("1,5").Allowed);
        Assert.True(PdvQuantityValidationRules.EvaluateRaw("12,5").Allowed);

        var decimalMul = PdvScanMultiplierParser.Parse("0,250*");
        Assert.True(decimalMul.Check.Allowed);
        Assert.Equal(PdvScanMultiplierKind.Armed, decimalMul.Kind);
        Assert.Equal(0.250, decimalMul.Quantity, 3);

        var umEMeio = PdvScanMultiplierParser.Parse("1,5x");
        Assert.True(umEMeio.Check.Allowed);
        Assert.Equal(1.5, umEMeio.Quantity, 3);
    }

    [Fact]
    public void BarcodePuro_NaoArmaMultiplicador()
    {
        var parsed = PdvScanMultiplierParser.Parse(SampleEan13);
        Assert.Equal(PdvScanMultiplierKind.None, parsed.Kind);
        Assert.True(parsed.Check.Allowed);
        Assert.False(parsed.IsExplicit);
    }

    [Fact]
    public void NomeComX_NaoArmaMultiplicador()
    {
        var parsed = PdvScanMultiplierParser.Parse("ANTARCTICAx");
        Assert.Equal(PdvScanMultiplierKind.None, parsed.Kind);
        Assert.False(parsed.IsExplicit);
    }

    [Fact]
    public void NegativoAsterisco_Rejeitado()
    {
        var parsed = PdvScanMultiplierParser.Parse("-1*");
        Assert.False(parsed.Check.Allowed);
        Assert.Equal(PdvQuantityRejectReason.Invalid, parsed.Check.Reason);
    }

    [Fact]
    public void Merge_10MaisLinhaComQty2_Resulta12()
    {
        var (cart, counter) = NewCart();
        var product = SampleProduct();
        PdvCartHelper.IncludeOrMerge(cart, product, 2, 4.5, 1, ref counter);
        var parsed = PdvScanMultiplierParser.Parse("10*");
        Assert.True(parsed.Check.Allowed);
        var state = new PdvScanMultiplierState();
        state.TryArm(parsed.Quantity);
        PdvCartHelper.IncludeOrMerge(cart, product, QtyForScan(state), 4.5, 1, ref counter);
        Assert.Single(cart);
        Assert.Equal(12, cart[0].Quantity);
        Assert.False(state.IsArmed);
    }

    [Fact]
    public void MaxQuantityPerLine_PermaneceCentral()
    {
        Assert.Equal(9999, PdvQuantityValidationRules.MaxQuantityPerLine);
        Assert.True(PdvQuantityValidationRules.EvaluateQuantity(PdvQuantityValidationRules.MaxQuantityPerLine).Allowed);
        Assert.False(PdvQuantityValidationRules.EvaluateQuantity(PdvQuantityValidationRules.MaxQuantityPerLine + 1).Allowed);
    }

    static void AssertIncludeFromSearch(string search, double expectedQty)
    {
        var parsed = PdvScanMultiplierParser.Parse(search);
        Assert.True(parsed.Check.Allowed);
        Assert.Equal(PdvScanMultiplierKind.Armed, parsed.Kind);
        var state = new PdvScanMultiplierState();
        Assert.True(state.TryArm(parsed.Quantity).Allowed);
        var (cart, counter) = NewCart();
        PdvCartHelper.IncludeOrMerge(cart, SampleProduct(), QtyForScan(state), 4.5, 1, ref counter);
        Assert.Equal(expectedQty, cart[0].Quantity);
        Assert.False(state.IsArmed);
    }

    static double QtyForScan(PdvScanMultiplierState state, double barcodeQty = 1) =>
        state.IsArmed ? state.Consume() : barcodeQty;

    static (List<PdvCartLine> Cart, int Counter) NewCart() => ([], 0);

    static Product SampleProduct() => new()
    {
        Id = 17,
        Name = "ANTARCTICA PILSEN GARRAFA VIDRO 300ML",
        Code = "00177",
        Barcode = SampleEan13,
        SalePrice = 4.5,
        Unit = "UN",
    };
}
