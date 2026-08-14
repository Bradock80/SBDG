using System.Net.Http;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// PIX Mercado Pago: só <c>approved</c> da API libera a venda.
/// pending / in_process / authorized nunca confirmam localmente.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PixCheckoutCoordinatorTests
{
    [Fact]
    public async Task Pending_DoesNotReleaseSale()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "pending" };
        var c = await Started(gw);

        var ok = await c.TryConfirmFromApiAsync();

        Assert.False(ok);
        Assert.False(c.PaidConfirmed);
        Assert.DoesNotContain("CONFIRMADO", c.UiStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("pending", PixIntentService.GetByMpPaymentId(gw.PaymentId)!.Status);
    }

    [Fact]
    public async Task InProcess_DoesNotReleaseSale()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "in_process" };
        var c = await Started(gw);

        Assert.False(await c.TryConfirmFromApiAsync());
        Assert.False(c.PaidConfirmed);
    }

    [Fact]
    public async Task Authorized_DoesNotReleaseSale()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "authorized" };
        var c = await Started(gw);

        Assert.False(await c.TryConfirmFromApiAsync());
        Assert.False(c.PaidConfirmed);
    }

    [Fact]
    public async Task Approved_ReleasesSale_AfterPersist()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "approved" };
        var c = await Started(gw);

        var ok = await c.TryConfirmFromApiAsync();

        Assert.True(ok);
        Assert.True(c.PaidConfirmed);
        Assert.Equal(PixMpStatus.ConfirmedMessage, c.UiStatus);
        var intent = PixIntentService.GetByMpPaymentId(gw.PaymentId)!;
        Assert.Equal("approved", intent.Status);
        Assert.False(string.IsNullOrWhiteSpace(intent.ApprovedAt));
    }

    [Fact]
    public async Task VerifyButton_Pending_DoesNotConfirm()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "pending" };
        var c = await Started(gw);

        Assert.False(await c.TryConfirmFromApiAsync());
        Assert.False(c.PaidConfirmed);
        Assert.Equal(0, c.ConfirmReleaseCount);
    }

    [Fact]
    public async Task VerifyButton_Approved_Confirms()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "approved" };
        var c = await Started(gw);

        Assert.True(await c.TryConfirmFromApiAsync());
        Assert.Equal(1, c.ConfirmReleaseCount);
    }

    [Fact]
    public async Task Abort_QueriesStatusBeforeCancel()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "pending" };
        var c = await Started(gw);

        await c.AbortAsync();

        Assert.True(gw.GetCount >= 1);
        var getAt = gw.CallLog.IndexOf("get");
        var cancelAt = gw.CallLog.IndexOf("cancel");
        Assert.True(getAt >= 0 && cancelAt > getAt);
    }

    [Fact]
    public async Task Abort_Pending_CallsCancel_NotRefund()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "pending" };
        var c = await Started(gw);

        await c.AbortAsync();

        Assert.Equal(1, c.CancelCalls);
        Assert.Equal(0, c.RefundCalls);
        Assert.False(c.PaidConfirmed);
        Assert.Equal("cancelled", PixIntentService.GetByMpPaymentId(gw.PaymentId)!.Status);
    }

    [Fact]
    public async Task Abort_Approved_DoesNotCancel_CallsRefund()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "approved" };
        var c = await Started(gw);

        await c.AbortAsync();

        Assert.Equal(0, c.CancelCalls);
        Assert.Equal(1, c.RefundCalls);
        Assert.False(c.PaidConfirmed);
        var intent = PixIntentService.GetByMpPaymentId(gw.PaymentId)!;
        Assert.Equal("refunded", intent.Status);
        Assert.False(string.IsNullOrWhiteSpace(intent.RefundedAt));
    }

    [Fact]
    public async Task Abort_ApprovedWithoutSale_Refunds()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "approved" };
        var c = await Started(gw);
        await c.TryConfirmFromApiAsync();
        Assert.True(c.PaidConfirmed);
        Assert.Null(PixIntentService.GetByMpPaymentId(gw.PaymentId)!.SaleId);

        await c.AbortAsync();

        Assert.Equal(1, c.RefundCalls);
        Assert.Equal(0, c.CancelCalls);
    }

    [Fact]
    public async Task Abort_PendingBecomesApproved_UsesRefundNotCancel()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { CreateStatus = "pending" };
        gw.GetStatusQueue.Enqueue("approved");
        var c = await Started(gw);

        await c.AbortAsync();

        Assert.Equal(0, c.CancelCalls);
        Assert.Equal(1, c.RefundCalls);
        Assert.False(c.PaidConfirmed);
    }

    [Fact]
    public async Task InternetError_DoesNotMarkPaid()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            GetError = new HttpRequestException("sem internet"),
        };
        var c = await Started(gw);

        Assert.False(await c.TryConfirmFromApiAsync());
        Assert.False(c.PaidConfirmed);
    }

    [Fact]
    public async Task Api500_DoesNotMarkPaid()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            GetError = new InvalidOperationException("Mercado Pago (500): internal"),
        };
        var c = await Started(gw);

        Assert.False(await c.TryConfirmFromApiAsync());
        Assert.False(c.PaidConfirmed);
    }

    [Fact]
    public async Task Timeout_DoesNotMarkPaid()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway
        {
            GetError = new TaskCanceledException("timeout"),
        };
        var c = await Started(gw);

        Assert.False(await c.TryConfirmFromApiAsync());
        Assert.False(c.PaidConfirmed);
    }

    [Fact]
    public async Task MultipleApprovedPolls_ReleaseOnce()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "approved" };
        var c = await Started(gw);

        Assert.True(await c.TryConfirmFromApiAsync());
        Assert.True(await c.TryConfirmFromApiAsync());
        Assert.True(await c.TryConfirmFromApiAsync());
        Assert.Equal(1, c.ConfirmReleaseCount);
        Assert.True(c.PaidConfirmed);
    }

    [Fact]
    public async Task WaitingTransfer_DoesNotReleaseSale()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "waiting_transfer" };
        var c = await Started(gw);

        Assert.False(await c.TryConfirmFromApiAsync());
        Assert.False(c.PaidConfirmed);
    }

    [Fact]
    public async Task InProcess_Abort_CallsCancel()
    {
        using var db = TempDatabase.Create();
        var gw = new FakeMercadoPagoPixGateway { DefaultGetStatus = "in_process" };
        var c = await Started(gw);

        await c.AbortAsync();

        Assert.Equal(1, c.CancelCalls);
        Assert.Equal(0, c.RefundCalls);
    }

    private static async Task<PixCheckoutCoordinator> Started(FakeMercadoPagoPixGateway gw)
    {
        var c = new PixCheckoutCoordinator(12.34, "teste pix", gw);
        await c.StartAsync();
        return c;
    }
}
