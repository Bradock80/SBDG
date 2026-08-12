using SGDB.Domain.Common;
using SGDB.Domain.Sales;

namespace SGDB.Tests;

/// <summary>
/// Testes unitários do núcleo puro de pagamento (ETAPA 33).
/// </summary>
public class SalePaymentCalculatorTests
{
    private static PaymentPart P(string type, double amount) =>
        new() { PaymentType = type, Amount = amount };

    private static bool IsCash(string t) =>
        t.Equals("Dinheiro", StringComparison.OrdinalIgnoreCase);

    private static bool IsFiado(string t) =>
        t.Equals("Fiado", StringComparison.OrdinalIgnoreCase);

    // ── NormalizeParts ──────────────────────────────────────────────

    [Fact]
    public void Normalize_UmaForma_ListaComUmaParte()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 28.50, [P("Dinheiro", 28.50)]);
        Assert.Single(parts);
        Assert.Equal("Dinheiro", parts[0].PaymentType);
        Assert.Equal(28.50, parts[0].Amount);
    }

    [Fact]
    public void Normalize_DuasFormas_PreservaOrdemEValores()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 28.50,
            [P("Dinheiro", 20), P("Fiado", 8.50)]);
        Assert.Equal(2, parts.Count);
        Assert.Equal("Dinheiro", parts[0].PaymentType);
        Assert.Equal(20, parts[0].Amount);
        Assert.Equal("Fiado", parts[1].PaymentType);
        Assert.Equal(8.50, parts[1].Amount);
    }

    [Fact]
    public void Normalize_TresOuMaisFormas_Permite()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 100,
            [P("Dinheiro", 40), P("Pix", 30), P("Fiado", 30)]);
        Assert.Equal(3, parts.Count);
        Assert.Equal(100, parts.Sum(p => p.Amount));
    }

    [Fact]
    public void Normalize_Duplicatas_Permitidas()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 30,
            [P("Dinheiro", 10), P("Dinheiro", 20)]);
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal("Dinheiro", p.PaymentType));
    }

    [Fact]
    public void Normalize_ValorZero_Filtrado()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 10,
            [P("Dinheiro", 10), P("Pix", 0)]);
        Assert.Single(parts);
        Assert.Equal("Dinheiro", parts[0].PaymentType);
    }

    [Fact]
    public void Normalize_ValorNegativo_Filtrado()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 10,
            [P("Dinheiro", 10), P("Pix", -5)]);
        Assert.Single(parts);
    }

    [Fact]
    public void Normalize_ListaNula_FallbackUmaForma()
    {
        var parts = SalePaymentCalculator.NormalizeParts("Pix", 42, null);
        Assert.Single(parts);
        Assert.Equal("Pix", parts[0].PaymentType);
        Assert.Equal(42, parts[0].Amount);
    }

    [Fact]
    public void Normalize_ListaVazia_FallbackUmaForma()
    {
        var parts = SalePaymentCalculator.NormalizeParts("Fiado", 15, Array.Empty<PaymentPart>());
        Assert.Single(parts);
        Assert.Equal("Fiado", parts[0].PaymentType);
        Assert.Equal(15, parts[0].Amount);
    }

    [Fact]
    public void Normalize_Fallback_UsaPaymentTypeInformado()
    {
        var parts = SalePaymentCalculator.NormalizeParts("Cartão Débito", 9.99, null);
        Assert.Equal("Cartão Débito", parts[0].PaymentType);
    }

    [Fact]
    public void Normalize_SomaExata_Aceita()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 50,
            [P("Dinheiro", 20), P("Pix", 30)]);
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Normalize_Diferenca_0_01_Aceita()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 10,
            [P("Dinheiro", 5), P("Pix", 4.99)]);
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Normalize_Diferenca_0_02_Aceita()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 10,
            [P("Dinheiro", 5), P("Pix", 4.98)]);
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Normalize_Diferenca_0_03_Rejeita()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SalePaymentCalculator.NormalizeParts(
                "Dinheiro", 10,
                [P("Dinheiro", 5), P("Pix", 4.97)]));
        Assert.Contains("difere do total", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_SoZeros_Lanca()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SalePaymentCalculator.NormalizeParts(
                "Dinheiro", 10,
                [P("Dinheiro", 0), P("Pix", 0)]));
        Assert.Contains("ao menos uma forma", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_RoundingMidpoint_AwayFromZero()
    {
        // 10.125 → 10.13; 9.875 → 9.88; soma 20.01 vs total 20.00 → |0.01| ≤ 0.02 OK
        Assert.Equal(10.13, MonetaryRounding.Round(10.125));
        Assert.Equal(9.88, MonetaryRounding.Round(9.875));
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 20.00,
            [P("Dinheiro", 10.125), P("Pix", 9.875)]);
        Assert.Equal(2, parts.Count);
        Assert.Equal(10.13, parts[0].Amount);
        Assert.Equal(9.88, parts[1].Amount);
    }

    [Fact]
    public void Normalize_DinheiroMaisFiadoParcial_28_50()
    {
        var parts = SalePaymentCalculator.NormalizeParts(
            "Dinheiro", 28.50,
            [P("Dinheiro", 20), P("Fiado", 8.50)]);
        Assert.Equal(2, parts.Count);
        Assert.Equal(8.50, parts.Single(p => p.PaymentType == "Fiado").Amount);
        Assert.Equal(20, parts.Single(p => p.PaymentType == "Dinheiro").Amount);
    }

    // ── ResolveCashChange ───────────────────────────────────────────

    [Fact]
    public void Troco_DinheiroExato_SemTroco()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Dinheiro", 50)], 50, 50, IsCash);
        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void Troco_Overpay_Calcula()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Dinheiro", 50)], 50, 70, IsCash);
        Assert.Equal(70, r.CashReceived);
        Assert.Equal(20, r.ChangeAmount);
    }

    [Fact]
    public void Troco_SemDinheiro_IgnoraCashReceived()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Pix", 50)], 50, 100, IsCash);
        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void Troco_PixComCashReceivedPositivo_SemTrocoFantasma()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Pix", 10)], 10, 50, IsCash);
        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void Troco_CartaoComCashReceived_SemTroco()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Cartão Débito", 10)], 10, 50, IsCash);
        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void Troco_MistoDinheiroPix_SobreComponenteDinheiro()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Dinheiro", 10), P("Pix", 20)], 30, 15, IsCash);
        Assert.Equal(15, r.CashReceived);
        Assert.Equal(5, r.ChangeAmount);
    }

    [Fact]
    public void Troco_DuasPartesDinheiro_SomaComponentes()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Dinheiro", 10), P("Dinheiro", 20)], 30, 40, IsCash);
        Assert.Equal(40, r.CashReceived);
        Assert.Equal(10, r.ChangeAmount);
    }

    [Fact]
    public void Troco_CashReceivedZero_SemTroco()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Dinheiro", 50)], 50, 0, IsCash);
        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void Troco_CashReceivedNegativo_SemTroco()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Dinheiro", 50)], 50, -10, IsCash);
        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void Troco_Limite_DinheiroMais_0_009_SemTroco()
    {
        // Após Round(2), recv=10,00 e dinheiro=10 → 10 <= 10+0,009 → sem troco.
        // (10,009 arredonda para 10,01 e já ultrapassa o buffer.)
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Dinheiro", 10)], 10, 10.004, IsCash);
        Assert.Null(r.CashReceived);
        Assert.Equal(0, r.ChangeAmount);
    }

    [Fact]
    public void Troco_AcimaDoLimite_GeraTroco()
    {
        var r = SalePaymentCalculator.ResolveCashChange(
            [P("Dinheiro", 10)], 10, 10.02, IsCash);
        Assert.Equal(10.02, r.CashReceived);
        Assert.Equal(0.02, r.ChangeAmount);
    }

    // ── IsPureFiadoPayment ──────────────────────────────────────────

    [Fact]
    public void PureFiado_Unico_True()
    {
        Assert.True(SalePaymentCalculator.IsPureFiadoPayment([P("Fiado", 100)], IsFiado));
    }

    [Fact]
    public void PureFiado_DinheiroMaisFiado_False()
    {
        Assert.False(SalePaymentCalculator.IsPureFiadoPayment(
            [P("Dinheiro", 20), P("Fiado", 8.50)], IsFiado));
    }

    [Fact]
    public void PureFiado_PixMaisFiado_False()
    {
        Assert.False(SalePaymentCalculator.IsPureFiadoPayment(
            [P("Pix", 10), P("Fiado", 10)], IsFiado));
    }

    [Fact]
    public void PureFiado_DuasPartesFiado_False_ConformeRegraAtual()
    {
        // Count == 2 → não é puro, mesmo ambas fiado.
        Assert.False(SalePaymentCalculator.IsPureFiadoPayment(
            [P("Fiado", 40), P("Fiado", 60)], IsFiado));
    }

    [Fact]
    public void PureFiado_ListaVazia_False()
    {
        Assert.False(SalePaymentCalculator.IsPureFiadoPayment([], IsFiado));
    }

    [Fact]
    public void PureFiado_LabelDesconhecido_DependeDoPredicado()
    {
        Assert.False(SalePaymentCalculator.IsPureFiadoPayment(
            [P("Cheque", 50)], IsFiado));
        Assert.True(SalePaymentCalculator.IsPureFiadoPayment(
            [P("Cheque", 50)], _ => true));
    }
}
