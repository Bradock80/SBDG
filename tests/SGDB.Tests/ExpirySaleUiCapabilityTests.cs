using System.IO;
using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// 70I-B2 — callers de UI capturam ExpirySaleException no caminho de negócio,
/// não no dispatcher. PIX reutiliza ShowFinalizeError.
/// </summary>
public class ExpirySaleUiCapabilityTests
{
    [Fact]
    public void Pdv_OpenPayment_CapturaExpirySale_EUsaShowFinalizeError()
    {
        var pdv = Read("Views", "PdvWindow.xaml.cs");
        var open = Slice(pdv, "private void OpenPayment()", "private static void ShowFinalizeError");
        Assert.Contains("catch (ExpirySaleException", open);
        Assert.Contains("ExpirySaleUi.Format", open);
        Assert.Contains("ShowFinalizeError", open);
        Assert.Contains("Operation.Sale", open);
        Assert.DoesNotContain("NewSale()", Slice(open, "catch (ExpirySaleException", "catch (PdvException"));
    }

    [Fact]
    public void Pdv_ShowFinalizeError_DelegaAoHelperCompartilhado()
    {
        var pdv = Read("Views", "PdvWindow.xaml.cs");
        var show = Slice(pdv, "private static void ShowFinalizeError", "private void NewSale()");
        Assert.Contains("PdvPostPaymentRecovery.Show", show);
        Assert.Contains("PixPaidAmount", show);
        Assert.Contains("PixPaymentId", show);
        Assert.DoesNotContain("RefundApprovedWithoutSaleAsync", show);
    }

    [Fact]
    public void Dispatcher_NaoEOunicoTratamento70I()
    {
        var app = Read("App.xaml.cs");
        Assert.Contains("OnDispatcherUnhandledException", app);
        Assert.Contains("Erro inesperado", app);
        Assert.DoesNotContain("ExpirySaleException", app);
    }

    [Fact]
    public void Deck_CapturaExpirySale_AntesDePdvException()
    {
        var src = Read("Views", "OpenTabDetailWindow.xaml.cs");
        var settle = Slice(src, "result = ApplicationServices.SettleOpenTab", "private void OfferSplitInfo");
        var expiryAt = settle.IndexOf("catch (ExpirySaleException", StringComparison.Ordinal);
        var pdvAt = settle.IndexOf("catch (PdvException", StringComparison.Ordinal);
        Assert.True(expiryAt >= 0, "Deck precisa capturar ExpirySaleException");
        Assert.True(pdvAt > expiryAt, "ExpirySaleException deve vir antes de PdvException");
        Assert.Contains("Operation.Deck", settle);
        Assert.Contains("PdvPostPaymentRecovery.Show", Slice(settle, "catch (ExpirySaleException", "catch (PdvException"));
    }

    [Fact]
    public void Swap_CapturaExpirySale_AntesDoFiltroPdv()
    {
        var src = Read("Views", "PdvVendasConsultaWindow.xaml.cs");
        var swap = Slice(src, "private void Trocar_Click", "private bool TryResolveCigaretteModeForSwap");
        var expiryAt = swap.IndexOf("catch (ExpirySaleException", StringComparison.Ordinal);
        var pdvAt = swap.IndexOf("PdvException or CashOperationException", StringComparison.Ordinal);
        Assert.True(expiryAt >= 0);
        Assert.True(pdvAt > expiryAt);
        Assert.Contains("Operation.Swap", swap);
    }

    [Fact]
    public void Exchange_CapturaExpirySale_AntesDoCatchGenerico()
    {
        var src = Read("Views", "SaleExchangeWindow.xaml.cs");
        var confirm = Slice(src, "var result = SaleExchangeService.Confirm", "private void Cancel_Click");
        var expiryAt = confirm.IndexOf("catch (ExpirySaleException", StringComparison.Ordinal);
        var genericAt = confirm.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(expiryAt >= 0);
        Assert.True(genericAt > expiryAt);
        Assert.Contains("Operation.Exchange", confirm);
    }

    [Fact]
    public void Transferencia_CapturaExpirySale_AntesDoCatchGenerico()
    {
        var src = Read("Views", "ProductFormWindow.xaml.cs");
        var transfer = Slice(src, "private void TransferFridge_Click", "private void ReturnFridge_Click");
        var expiryAt = transfer.IndexOf("catch (ExpirySaleException", StringComparison.Ordinal);
        var genericAt = transfer.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(expiryAt >= 0);
        Assert.True(genericAt > expiryAt);
        Assert.Contains("Operation.Transfer", transfer);
        Assert.Contains("TransferWarehouseToFridge", transfer);
    }

    [Fact]
    public void MotorB1_NaoFoiReimplementadoNaUi()
    {
        var ui = Read("Models", "ExpirySaleUi.cs");
        Assert.DoesNotContain("AggregateLots", ui);
        Assert.DoesNotContain("product_lots", ui);
        Assert.DoesNotContain("HasExpiredStock", ui);
        Assert.Contains("ExpirySaleException", ui);
    }

    private static string Read(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SGDB.App"));
        return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
    }

    private static string Slice(string src, string start, string end)
    {
        var i = src.IndexOf(start, StringComparison.Ordinal);
        Assert.True(i >= 0, start);
        var j = src.IndexOf(end, i, StringComparison.Ordinal);
        Assert.True(j > i, end);
        return src[i..j];
    }
}
