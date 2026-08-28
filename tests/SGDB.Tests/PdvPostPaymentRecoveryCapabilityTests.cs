using System.IO;

namespace SGDB.Tests;

/// <summary>
/// 70I-B2-P1 — PDV e Deck usam o mesmo helper; refund não está duplicado nas views.
/// </summary>
public class PdvPostPaymentRecoveryCapabilityTests
{
    [Fact]
    public void Pdv_DelegaShowFinalizeErrorAoHelper()
    {
        var pdv = Read("Views", "PdvWindow.xaml.cs");
        var open = Slice(pdv, "private void OpenPayment()", "private void NewSale()");
        Assert.Contains("catch (ExpirySaleException", open);
        Assert.Contains("ShowFinalizeError", open);
        Assert.Contains("PdvPostPaymentRecovery.Show", open);
        Assert.Contains("PdvPostPaymentRecovery.RecoverUnexpected", open);
        Assert.Contains("AttachSaleAfterSuccess", open);
        Assert.Contains("PresentPostCommitFailure", open);
        Assert.DoesNotContain("RefundApprovedWithoutSaleAsync", pdv);
        Assert.DoesNotContain("AbortAsync", pdv);

        var phaseA = Slice(open, "result = ApplicationServices.FinalizeSale", "PdvPostPaymentRecovery.AttachSaleAfterSuccess");
        Assert.Contains("RecoverUnexpected", phaseA);
        Assert.Contains("ShowFinalizeError", phaseA);
        var phaseB = Slice(open, "PdvPostPaymentRecovery.AttachSaleAfterSuccess", "private static void ShowFinalizeError");
        Assert.Contains("PresentPostCommitFailure", phaseB);
        Assert.DoesNotContain("RecoverUnexpected", phaseB);
        Assert.DoesNotContain("ShowFinalizeError", phaseB);
    }

    [Fact]
    public void Helper_UnicoRefundApprovedWithoutSale()
    {
        var helper = Read("Services", "PdvPostPaymentRecovery.cs");
        Assert.Contains("RefundApprovedWithoutSaleAsync", helper);
        Assert.DoesNotContain("AbortAsync", helper);
        Assert.Contains("AttachSaleAfterSuccess", helper);

        var deck = Read("Views", "OpenTabDetailWindow.xaml.cs");
        Assert.DoesNotContain("RefundApprovedWithoutSaleAsync", deck);
        Assert.Contains("PdvPostPaymentRecovery.Show", deck);
        Assert.Contains("AttachSaleAfterSuccess", deck);
        Assert.Contains("RecoverUnexpected", deck);
    }

    [Fact]
    public void Deck_NaoReabrePagamentoAposFalha()
    {
        var src = Read("Views", "OpenTabDetailWindow.xaml.cs");
        var settle = Slice(src, "private void Settle()", "private void OfferSplitInfo");
        Assert.DoesNotContain("while (true)", settle);
        Assert.Contains("catch (ExpirySaleException", settle);
        Assert.Contains("catch (PdvException", settle);
        Assert.Contains("catch (OpenTabException", settle);
        Assert.Contains("catch (CashOperationException", settle);
        Assert.Contains("RecoverUnexpected", settle);
        Assert.Contains("PdvPostPaymentRecovery.Show", settle);
        Assert.Contains("AttachSaleAfterSuccess", settle);
        Assert.Contains("PresentPostCommitFailure", settle);

        var phaseA = Slice(settle, "result = ApplicationServices.SettleOpenTab", "PdvPostPaymentRecovery.AttachSaleAfterSuccess");
        Assert.Contains("RecoverUnexpected", phaseA);
        Assert.Contains("PdvPostPaymentRecovery.Show", phaseA);
        var attachAt = settle.IndexOf("PdvPostPaymentRecovery.AttachSaleAfterSuccess", StringComparison.Ordinal);
        Assert.True(attachAt >= 0);
        var phaseB = settle[attachAt..];
        Assert.Contains("PresentPostCommitFailure", phaseB);
        Assert.DoesNotContain("RecoverUnexpected", phaseB);
        Assert.DoesNotContain("PdvPostPaymentRecovery.Show", phaseB);
    }

    [Fact]
    public void MotorB1_IntactoNoDiffDeRecuperacao()
    {
        var helper = Read("Services", "PdvPostPaymentRecovery.cs");
        Assert.DoesNotContain("AggregateLots", helper);
        Assert.DoesNotContain("EnsureWarehouseSellable", helper);
        Assert.DoesNotContain("product_lots", helper);
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
