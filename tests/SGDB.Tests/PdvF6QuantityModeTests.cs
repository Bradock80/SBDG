using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// Complemento 69J — F6 modo quantidade explícito, one-shot, sem QtyBox do item.
/// </summary>
public class PdvF6QuantityModeTests
{
    private const string IncidentEan13 = "7896588700608";
    private const string SampleGtin14 = "12345678901234";

    [Fact]
    public void F6_EntraEmModoQuantidade()
    {
        var f6 = new PdvF6QuantitySession();
        Assert.False(f6.IsEditing);
        f6.Enter();
        Assert.True(f6.IsEditing);
        Assert.Equal(PdvF6Mode.Editing, f6.Mode);
    }

    [Fact]
    public void F6_10_Enter_Arma10()
    {
        var f6 = new PdvF6QuantitySession();
        var state = new PdvScanMultiplierState();
        f6.Enter();
        var check = f6.Confirm("10", state);
        Assert.True(check.Allowed);
        Assert.False(f6.IsEditing);
        Assert.True(state.IsArmed);
        Assert.Equal(10, state.Quantity);
    }

    [Fact]
    public void F6_ProximaLeituraUsa10()
    {
        var (f6, state) = ArmF6("10");
        Assert.False(f6.IsEditing);
        var (cart, counter) = NewCart();
        PdvCartHelper.IncludeOrMerge(cart, SampleProduct(), QtyForScan(state), 4.5, 1, ref counter);
        Assert.Equal(10, cart[0].Quantity);
        Assert.False(state.IsArmed);
    }

    [Fact]
    public void F6_LeituraSeguinteVoltaPara1()
    {
        var (_, state) = ArmF6("10");
        Assert.Equal(10, QtyForScan(state));
        Assert.Equal(1, QtyForScan(state));
        var (cart, counter) = NewCart();
        PdvCartHelper.IncludeOrMerge(cart, SampleProduct(), QtyForScan(state), 4.5, 1, ref counter);
        Assert.Equal(1, cart[0].Quantity);
    }

    [Fact]
    public void Esc_CancelaModoF6()
    {
        var f6 = new PdvF6QuantitySession();
        var state = new PdvScanMultiplierState();
        f6.Enter();
        f6.Confirm("10", state);
        Assert.True(state.IsArmed);
        f6.Cancel();
        state.Clear();
        Assert.False(f6.IsEditing);
        Assert.False(state.IsArmed);
        Assert.Equal(1, QtyForScan(state));
    }

    [Fact]
    public void F6_ZeroBloqueado()
    {
        AssertF6Rejected("0", PdvQuantityRejectReason.Invalid);
    }

    [Fact]
    public void F6_NegativoBloqueado()
    {
        AssertF6Rejected("-1", PdvQuantityRejectReason.Invalid);
    }

    [Fact]
    public void F6_10000Bloqueado()
    {
        var check = AssertF6Rejected("10000", PdvQuantityRejectReason.AboveLineLimit);
        Assert.Equal(PdvQuantityValidationRules.MessageQuantityLimit, check.Message);
        Assert.Equal(9999, PdvQuantityValidationRules.MaxQuantityPerLine);
    }

    [Fact]
    public void F6_DecimalValidoContinuaFuncionando()
    {
        var (f6, state) = ArmF6("0,250");
        Assert.False(f6.IsEditing);
        Assert.Equal(0.250, state.Quantity, 3);
        var (_, state2) = ArmF6("1,5");
        Assert.Equal(1.5, state2.Quantity, 3);
    }

    [Fact]
    public void F6_Ean13NaoViraQuantidade()
    {
        var check = AssertF6Rejected(IncidentEan13, PdvQuantityRejectReason.LooksLikeGtin);
        Assert.Equal(PdvQuantityValidationRules.MessageGtinInQuantity, check.Message);
    }

    [Fact]
    public void F6_Gtin14NaoViraQuantidade()
    {
        AssertF6Rejected(SampleGtin14, PdvQuantityRejectReason.LooksLikeGtin);
    }

    [Fact]
    public void F6_NaoQuebra10x()
    {
        var f6 = new PdvF6QuantitySession();
        f6.Enter();
        f6.Cancel();
        var parsed = PdvScanMultiplierParser.Parse("10x");
        Assert.True(parsed.Check.Allowed);
        Assert.Equal(PdvScanMultiplierKind.Armed, parsed.Kind);
        Assert.Equal(10, parsed.Quantity);
    }

    [Fact]
    public void F6_NaoQuebra10Asterisco()
    {
        var f6 = new PdvF6QuantitySession();
        var state = new PdvScanMultiplierState();
        f6.Enter();
        f6.Confirm("5", state);
        state.Clear();
        var parsed = PdvScanMultiplierParser.Parse("10*");
        Assert.True(parsed.Check.Allowed);
        Assert.Equal(10, parsed.Quantity);
        Assert.True(state.TryArm(parsed.Quantity).Allowed);
        Assert.Equal(10, QtyForScan(state));
    }

    [Fact]
    public void F6_NaoQuebraPesquisaManual()
    {
        Assert.True(PdvScanFocusPolicy.ShouldFocusQtyBox(fromBarcodeScan: false));
        Assert.True(PdvScanFocusPolicy.ShouldSelectAllQty(fromBarcodeScan: false));
        Assert.False(PdvScanFocusPolicy.ShouldAutoInclude(fromBarcodeScan: false));
    }

    [Fact]
    public void F6_ScannerNormalContinua1()
    {
        Assert.True(PdvScanFocusPolicy.ShouldAutoInclude(fromBarcodeScan: true));
        Assert.False(PdvScanFocusPolicy.ShouldFocusQtyBox(fromBarcodeScan: true));
        Assert.False(PdvScanFocusPolicy.ShouldSelectAllQty(fromBarcodeScan: true));
        var state = new PdvScanMultiplierState();
        Assert.Equal(1, QtyForScan(state));
        var (cart, counter) = NewCart();
        PdvCartHelper.IncludeOrMerge(cart, SampleProduct(), QtyForScan(state), 4.5, 1, ref counter);
        Assert.Equal(1, cart[0].Quantity);
    }

    [Fact]
    public void FinalizarOuLimparVenda_LimpaEstadoF6()
    {
        var (f6, state) = ArmF6("23");
        f6.Cancel();
        state.Clear();
        Assert.False(f6.IsEditing);
        Assert.False(state.IsArmed);
        Assert.Equal(1, state.Quantity);
    }

    [Fact]
    public void Erro_LimpaEstadoF6()
    {
        var f6 = new PdvF6QuantitySession();
        var state = new PdvScanMultiplierState();
        f6.Enter();
        var check = f6.Confirm("0", state);
        Assert.False(check.Allowed);
        Assert.False(f6.IsEditing);
        Assert.False(state.IsArmed);
        Assert.Equal(1, QtyForScan(state));
    }

    static PdvQuantityCheckResult AssertF6Rejected(string raw, PdvQuantityRejectReason reason)
    {
        var f6 = new PdvF6QuantitySession();
        var state = new PdvScanMultiplierState();
        f6.Enter();
        var check = f6.Confirm(raw, state);
        Assert.False(check.Allowed);
        Assert.Equal(reason, check.Reason);
        Assert.False(f6.IsEditing);
        Assert.False(state.IsArmed);
        return check;
    }

    static (PdvF6QuantitySession F6, PdvScanMultiplierState State) ArmF6(string raw)
    {
        var f6 = new PdvF6QuantitySession();
        var state = new PdvScanMultiplierState();
        f6.Enter();
        var check = f6.Confirm(raw, state);
        Assert.True(check.Allowed);
        Assert.True(state.IsArmed);
        return (f6, state);
    }

    static double QtyForScan(PdvScanMultiplierState state, double barcodeQty = 1) =>
        state.IsArmed ? state.Consume() : barcodeQty;

    static (List<PdvCartLine> Cart, int Counter) NewCart() => ([], 0);

    static Product SampleProduct() => new()
    {
        Id = 17,
        Name = "ANTARCTICA PILSEN GARRAFA VIDRO 300ML",
        Code = "00177",
        Barcode = "7891991000178",
        SalePrice = 4.5,
        Unit = "UN",
    };
}
