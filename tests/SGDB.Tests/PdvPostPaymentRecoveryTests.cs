using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70I-B2-P1 — um único caminho de recuperação após pagamento confirmado (PDV/Deck).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvPostPaymentRecoveryTests
{
    [Fact]
    public void SemPix_NaoChamaMercadoPago_MantemTituloDeNegocio()
    {
        using var _ = Begin();
        var gw = new FakeMercadoPagoPixGateway();
        var ui = ExpirySaleUi.Format(BlockedEx(), ExpirySaleUi.Operation.Deck, _ => "Coca", canMaintainLots: true);

        var display = PdvPostPaymentRecovery.Recover(ui.Body, pixPaidAmount: 0, pixPaymentId: 99001, ui.Title, gw);

        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(0, gw.CancelCount);
        Assert.Equal(PdvPostPaymentRecovery.OutcomeNone, display.RefundOutcome);
        Assert.False(display.RefundAttempted);
        Assert.Equal("Venda não realizada", display.Title);
        Assert.Contains("Coca", display.Body);
        Assert.Contains("unidades comprovadamente vencidas", display.Body);
        Assert.DoesNotContain("JÁ PAGOU", display.Body);
        Assert.DoesNotContain("estornado", display.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PixAprovado_RefundUmaVez_NaoChamaAbort()
    {
        using var _ = Begin();
        var (gw, pid) = SeedApprovedPix(12.5);
        var ui = ExpirySaleUi.Format(BlockedEx(), ExpirySaleUi.Operation.Sale, _ => "Coca-Cola 2L", true);

        var display = PdvPostPaymentRecovery.Recover(ui.Body, 12.5, pid, ui.Title, gw);

        Assert.Equal(1, gw.RefundCount);
        Assert.Equal(0, gw.CancelCount);
        Assert.DoesNotContain("abort", string.Join(',', gw.CallLog), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PdvPostPaymentRecovery.OutcomeRefunded, display.RefundOutcome);
        Assert.True(display.RefundAttempted);
        Assert.Equal(PdvPostPaymentRecovery.PixTitle, display.Title);
        Assert.Contains("Coca-Cola 2L", display.Body);
        Assert.Contains("unidades comprovadamente vencidas", display.Body);
        Assert.Contains("JÁ PAGOU R$", display.Body);
        Assert.Contains("Foi solicitado o estorno", display.Body);
        Assert.DoesNotContain("PIX estornado", display.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("refunded", PixIntentService.GetByMpPaymentId(pid)!.Status);
    }

    [Fact]
    public void PixAprovado_RefundFalha_NaoAfirmaSucesso_PreservaPending()
    {
        using var _ = Begin();
        var (gw, pid) = SeedApprovedPix(20);
        gw.RefundError = new InvalidOperationException("mp-down");

        var display = PdvPostPaymentRecovery.Recover("Venda falhou", 20, pid, "Venda não realizada", gw);

        Assert.Equal(1, gw.RefundCount);
        Assert.Equal(PdvPostPaymentRecovery.OutcomeRefundPending, display.RefundOutcome);
        Assert.Contains("pendente", display.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Não considere o caso encerrado", display.Body);
        Assert.DoesNotContain("Foi solicitado o estorno", display.Body);
        var intent = PixIntentService.GetByMpPaymentId(pid)!;
        Assert.Equal("refund_pending", intent.Status);
        Assert.Contains("mp-down", intent.LastError);
    }

    [Fact]
    public void PixChaveSemPaymentId_NaoChamaApi()
    {
        using var _ = Begin();
        var gw = new FakeMercadoPagoPixGateway();

        var display = PdvPostPaymentRecovery.Recover("Falha", pixPaidAmount: 15, pixPaymentId: null, "Venda", gw);

        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(PdvPostPaymentRecovery.OutcomeNoPaymentId, display.RefundOutcome);
        Assert.False(display.RefundAttempted);
        Assert.Contains("manualmente", display.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foi solicitado o estorno", display.Body);
    }

    [Fact]
    public void RecoverDuasVezes_NaoEOcontratoDaView_MasUmaChamadaEUmaRefund()
    {
        using var _ = Begin();
        var (gw, pid) = SeedApprovedPix(9);
        var first = PdvPostPaymentRecovery.Recover("falha", 9, pid, gateway: gw);
        Assert.Equal(1, gw.RefundCount);
        Assert.Equal(PdvPostPaymentRecovery.OutcomeRefunded, first.RefundOutcome);
    }

    [Fact]
    public void DeckPix_70I_VendaNaoCriada_ComandaAberta_RefundUmaVez()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "p1-70i");
        var id = SeedExpired();
        var tabId = OpenTabService.Create("Deck P1");
        OpenTabService.AddProduct(tabId, id, 1, 5);
        var lines = OpenTabService.ToCartLines(tabId).ToList();
        var (gw, pid) = SeedApprovedPix(5);

        var thrown = Assert.Throws<ExpirySaleException>(() =>
            OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
            {
                Items = lines,
                PaymentType = "Pix",
                CashReceived = 0,
            }));

        var ui = ExpirySaleUi.Format(thrown, ExpirySaleUi.Operation.Deck, _ => "Item vencível", true);
        var display = PdvPostPaymentRecovery.Recover(ui.Body, 5, pid, ui.Title, gw);

        Assert.Equal(1, gw.RefundCount);
        Assert.Equal(0, gw.CancelCount);
        Assert.Equal(0, CountSales());
        Assert.Equal("open", GetTabStatus(tabId));
        Assert.Contains("comanda permanece aberta", display.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unidades comprovadamente vencidas", display.Body);
        Assert.Contains("JÁ PAGOU", display.Body);
    }

    [Fact]
    public void DeckPix_PdvException_RecoveryUmaVez()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "p1-pdv");
        var tabId = OpenTabService.Create("Deck PdvEx");
        var (gw, pid) = SeedApprovedPix(8);

        var thrown = Assert.Throws<PdvException>(() =>
            OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
            {
                Items = [],
                PaymentType = "Pix",
            }));

        var display = PdvPostPaymentRecovery.Recover(thrown.Message, 8, pid, "Decks", gw);

        Assert.Equal(1, gw.RefundCount);
        Assert.Equal("open", GetTabStatus(tabId));
        Assert.Equal(0, CountSales());
        Assert.True(display.RefundAttempted);
        Assert.Contains("Adicione pelo menos um produto", display.Body);
    }

    [Fact]
    public void DeckPix_OpenTabException_RecoverySemSegundaCobranca()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "p1-open");
        var productId = TestDataHelper.SeedSimpleProduct(20, 5, 2);
        var tabId = OpenTabService.Create("Deck settled");
        OpenTabService.AddProduct(tabId, productId, 1, 5);
        var lines = OpenTabService.ToCartLines(tabId).ToList();
        OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Dinheiro",
            CashReceived = 5,
        });
        var (gw, pid) = SeedApprovedPix(5);

        var thrown = Assert.Throws<OpenTabException>(() =>
            OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
            {
                Items = lines,
                PaymentType = "Pix",
            }));

        var display = PdvPostPaymentRecovery.Recover(thrown.Message, 5, pid, "Decks", gw);

        Assert.Equal(1, gw.RefundCount);
        Assert.Contains("já foi fechado", display.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("settled", GetTabStatus(tabId));
    }

    [Fact]
    public void DeckPix_CashOperationException_Recovery()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "p1-cash");
        var productId = TestDataHelper.SeedSimpleProduct(20, 5, 2);
        var tabId = OpenTabService.Create("Deck caixa");
        OpenTabService.AddProduct(tabId, productId, 1, 5);
        var lines = OpenTabService.ToCartLines(tabId).ToList();
        CashService.CloseSession(50, "fecha");
        var (gw, pid) = SeedApprovedPix(5);

        var thrown = Assert.Throws<CashOperationException>(() =>
            OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
            {
                Items = lines,
                PaymentType = "Pix",
            }));

        var display = PdvPostPaymentRecovery.Recover(thrown.Message, 5, pid, "Decks", gw);

        Assert.Equal(1, gw.RefundCount);
        Assert.Equal("open", GetTabStatus(tabId));
        Assert.Contains("caixa", display.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unexpected_TentaRefund_EContinuaDiagnosticavel()
    {
        using var _ = Begin();
        var (gw, pid) = SeedApprovedPix(11);
        var boom = new InvalidOperationException("falha-interna-p1");

        var display = PdvPostPaymentRecovery.RecoverUnexpected(boom, 11, pid, gw);

        Assert.Equal(1, gw.RefundCount);
        Assert.True(display.IsUnexpected);
        Assert.Contains("Erro inesperado", display.Body);
        Assert.Contains("InvalidOperationException", display.Body);
        Assert.Contains("falha-interna-p1", display.Body);
        Assert.Contains("JÁ PAGOU", display.Body);
        Assert.Contains("crash.log", display.Body);
    }

    [Fact]
    public void SucessoDeck_AssociaPixIntentAoSaleId()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "p1-ok");
        var productId = TestDataHelper.SeedSimpleProduct(20, 5, 2);
        var tabId = OpenTabService.Create("Deck ok");
        OpenTabService.AddProduct(tabId, productId, 1, 5);
        var lines = OpenTabService.ToCartLines(tabId).ToList();
        var (gw, pid) = SeedApprovedPix(5);

        var result = OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Pix",
        });

        Assert.True(result.SaleId > 0);
        Assert.Null(PixIntentService.GetByMpPaymentId(pid)!.SaleId);

        PdvPostPaymentRecovery.AttachSaleAfterSuccess(pid, result.SaleId);

        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(result.SaleId, PixIntentService.GetByMpPaymentId(pid)!.SaleId);
        Assert.Equal("settled", GetTabStatus(tabId));
    }

    [Fact]
    public void AttachSale_SemPaymentId_NaoAssocia()
    {
        using var _ = Begin();
        PdvPostPaymentRecovery.AttachSaleAfterSuccess(null, 1);
        PdvPostPaymentRecovery.AttachSaleAfterSuccess(0, 1);
        Assert.Null(PixIntentService.GetByMpPaymentId(1));
    }

    [Fact]
    public void PdvPix_ExecuteLanca_RefundUmaVez()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "p2-pdv-fail");
        var id = SeedExpired();
        var (gw, pid) = SeedApprovedPix(5);

        var thrown = Assert.Throws<ExpirySaleException>(() =>
            TestDataHelper.FinalizeSimpleCashSale(id, qty: 1, unitPrice: 5, cashReceived: 5));
        var ui = ExpirySaleUi.Format(thrown, ExpirySaleUi.Operation.Sale, _ => "Coca", true);
        var display = PdvPostPaymentRecovery.Recover(ui.Body, 5, pid, ui.Title, gw);

        Assert.Equal(1, gw.RefundCount);
        Assert.Equal(0, CountSales());
        Assert.Contains("unidades comprovadamente vencidas", display.Body);
    }

    [Fact]
    public void PdvPix_ExecuteSucesso_AttachSaleLanca_ZeroRefund()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "p2-pdv-ok");
        var productId = TestDataHelper.SeedSimpleProduct(20, 5, 2);
        var (gw, pid) = SeedApprovedPix(5);
        var result = TestDataHelper.FinalizeSimpleCashSale(productId, qty: 1, unitPrice: 5, cashReceived: 5);

        PdvPostPaymentRecovery.TestThrowOnAttachSale = new InvalidOperationException("attach-p2-pdv");
        try
        {
            var thrown = Assert.Throws<InvalidOperationException>(() =>
                PdvPostPaymentRecovery.AttachSaleAfterSuccess(pid, result.SaleId));
            var display = PdvPostPaymentRecovery.FormatPostCommitFailure(thrown, result.SaleId);

            Assert.Equal(0, gw.RefundCount);
            Assert.Equal("approved", PixIntentService.GetByMpPaymentId(pid)!.Status);
            Assert.Null(PixIntentService.GetByMpPaymentId(pid)!.SaleId);
            Assert.True(result.SaleId > 0);
            Assert.Equal(1, CountSales());
            Assert.DoesNotContain("não foi registrada", display.Body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("já foi registrada", display.Body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            PdvPostPaymentRecovery.TestThrowOnAttachSale = null;
        }
    }

    [Fact]
    public void DeckPix_ExecuteSucesso_AttachSaleLanca_ZeroRefund()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "p2-deck-ok");
        var productId = TestDataHelper.SeedSimpleProduct(20, 5, 2);
        var tabId = OpenTabService.Create("Deck P2 attach");
        OpenTabService.AddProduct(tabId, productId, 1, 5);
        var lines = OpenTabService.ToCartLines(tabId).ToList();
        var (gw, pid) = SeedApprovedPix(5);
        var result = OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Pix",
        });

        PdvPostPaymentRecovery.TestThrowOnAttachSale = new InvalidOperationException("attach-p2-deck");
        try
        {
            var thrown = Assert.Throws<InvalidOperationException>(() =>
                PdvPostPaymentRecovery.AttachSaleAfterSuccess(pid, result.SaleId));
            var display = PdvPostPaymentRecovery.FormatPostCommitFailure(thrown, result.SaleId);

            Assert.Equal(0, gw.RefundCount);
            Assert.Equal("settled", GetTabStatus(tabId));
            Assert.Equal(result.SaleId, GetTabSaleId(tabId));
            Assert.Null(PixIntentService.GetByMpPaymentId(pid)!.SaleId);
            Assert.DoesNotContain("não foi registrada", display.Body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("já foi registrada", display.Body);
        }
        finally
        {
            PdvPostPaymentRecovery.TestThrowOnAttachSale = null;
        }
    }

    [Fact]
    public void PosSucesso_AuditOuCupom_ZeroRefund_MensagemNaoNegaVenda()
    {
        using var _ = Begin();
        var (gw, pid) = SeedApprovedPix(9);
        var display = PdvPostPaymentRecovery.FormatPostCommitFailure(
            new InvalidOperationException("cupom-p2"), saleId: 42);

        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(PdvPostPaymentRecovery.OutcomeNone, display.RefundOutcome);
        Assert.False(display.RefundAttempted);
        Assert.Contains("já foi registrada", display.Body);
        Assert.DoesNotContain("não foi registrada", display.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Foi solicitado o estorno", display.Body);
        Assert.Equal("approved", PixIntentService.GetByMpPaymentId(pid)!.Status);
    }

    [Fact]
    public void DinheiroECartao_ZeroChamadasMp()
    {
        using var _ = Begin();
        var gw = new FakeMercadoPagoPixGateway();
        var ui = ExpirySaleUi.Format(BlockedEx(), ExpirySaleUi.Operation.Sale, _ => "X", true);

        var cash = PdvPostPaymentRecovery.Recover(ui.Body, 0, null, ui.Title, gw);
        var card = PdvPostPaymentRecovery.Recover(ui.Body, 0, 88001, ui.Title, gw);

        Assert.Equal(0, gw.RefundCount);
        Assert.Equal(0, gw.CreateCount);
        Assert.DoesNotContain("Mercado Pago", cash.Body);
        Assert.DoesNotContain("estorn", card.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    private static (FakeMercadoPagoPixGateway Gw, long PaymentId) SeedApprovedPix(double amount)
    {
        var gw = new FakeMercadoPagoPixGateway { PaymentId = 77_001 };
        PixIntentService.Create(gw.PaymentId, amount, "p1-idem", "approved");
        PixIntentService.MarkApproved(gw.PaymentId);
        return (gw, gw.PaymentId);
    }

    private static int SeedExpired()
    {
        var id = TestDataHelper.SeedSimpleProduct(10, 5, 2,
            code: "P1" + Guid.NewGuid().ToString("N")[..6],
            name: "P1 " + Guid.NewGuid().ToString("N")[..6]);
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = id,
            Quantity = 10,
            LotNumber = "V",
            ExpiryDate = DateTime.Today.AddDays(-1),
        });
        return id;
    }

    private static ExpirySaleException BlockedEx() =>
        new(ExpirySaleRules.InsufficientNonExpired, "bruto", new ExpirySaleDecision
        {
            ProductId = 7,
            RequestedWarehouseQty = 7,
            SellableWarehouseQty = 0,
            ExpiredQty = 10,
            IsBlocked = true,
            ErrorCode = ExpirySaleRules.InsufficientNonExpired,
        });

    private static int CountSales()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sales;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string GetTabStatus(int tabId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(status,'') FROM open_tabs WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", tabId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static int GetTabSaleId(int tabId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sale_id FROM open_tabs WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", tabId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
