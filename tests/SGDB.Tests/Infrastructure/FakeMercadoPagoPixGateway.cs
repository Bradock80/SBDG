using SGDB.Services;

namespace SGDB.Tests.Infrastructure;

internal sealed class FakeMercadoPagoPixGateway : IMercadoPagoPixGateway
{
    public long PaymentId { get; set; } = 88001;
    public string CreateStatus { get; set; } = "pending";
    public string DefaultGetStatus { get; set; } = "pending";
    public Queue<string> GetStatusQueue { get; } = new();
    public Exception? GetError { get; set; }
    public Exception? CreateError { get; set; }
    public Exception? RefundError { get; set; }
    public Exception? CancelError { get; set; }
    public string StatusDetail { get; set; } = "";

    public int CreateCount { get; private set; }
    public int GetCount { get; private set; }
    public int CancelCount { get; private set; }
    public int RefundCount { get; private set; }
    public List<string> CallLog { get; } = [];

    public Task<MercadoPagoPixCharge> CreatePixAsync(
        double amount, string description, string idempotencyKey, string? payerEmail, CancellationToken ct)
    {
        CreateCount++;
        CallLog.Add("create");
        if (CreateError is not null)
            throw CreateError;
        return Task.FromResult(Charge(CreateStatus));
    }

    public Task<MercadoPagoPixCharge> GetPaymentAsync(long paymentId, CancellationToken ct)
    {
        GetCount++;
        CallLog.Add("get");
        if (GetError is not null)
            throw GetError;
        var status = GetStatusQueue.Count > 0 ? GetStatusQueue.Dequeue() : DefaultGetStatus;
        return Task.FromResult(Charge(status, paymentId));
    }

    public Task CancelPaymentAsync(long paymentId, CancellationToken ct)
    {
        CancelCount++;
        CallLog.Add("cancel");
        if (CancelError is not null)
            throw CancelError;
        return Task.CompletedTask;
    }

    public Task RefundPaymentAsync(long paymentId, string idempotencyKey, CancellationToken ct)
    {
        RefundCount++;
        CallLog.Add("refund");
        if (RefundError is not null)
            throw RefundError;
        return Task.CompletedTask;
    }

    private MercadoPagoPixCharge Charge(string status, long? id = null) => new()
    {
        PaymentId = id ?? PaymentId,
        Status = status,
        StatusDetail = StatusDetail,
        QrCode = "00020126fake",
    };
}
