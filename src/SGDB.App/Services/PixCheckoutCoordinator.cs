namespace SGDB.Services;

/// <summary>
/// Ciclo PIX QR: só libera venda após persistir status <c>approved</c> da API.
/// Abort (X/Cancelar) consulta o MP antes de cancel vs refund.
/// </summary>
public sealed class PixCheckoutCoordinator
{
    private readonly IMercadoPagoPixGateway _gateway;
    private readonly double _amount;
    private readonly string _description;
    private readonly object _gate = new();
    private bool _released;
    private bool _aborted;

    public PixCheckoutCoordinator(double amount, string description, IMercadoPagoPixGateway? gateway = null)
    {
        _amount = amount;
        _description = description;
        _gateway = gateway ?? MercadoPagoPixService.Gateway;
        UiStatus = PixMpStatus.WaitingMessage;
        UiHint = PixMpStatus.ProcessingHint;
    }

    public long? PaymentId { get; private set; }
    public bool PaidConfirmed { get; private set; }
    public string UiStatus { get; private set; }
    public string UiHint { get; private set; }
    public MercadoPagoPixCharge? LastCharge { get; private set; }
    public int ConfirmReleaseCount { get; private set; }
    public int CancelCalls { get; private set; }
    public int RefundCalls { get; private set; }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var key = Guid.NewGuid().ToString("N");
        var charge = await _gateway.CreatePixAsync(_amount, _description, key, payerEmail: null, ct)
            .ConfigureAwait(false);
        LastCharge = charge;
        PaymentId = charge.PaymentId;
        PixIntentService.Create(charge.PaymentId, _amount, key, charge.Status);
        ApplyRemoteStatus(charge, persistApprovedIfNeeded: true);
    }

    /// <summary>GET na API. Só retorna true depois de persistir approved.</summary>
    public async Task<bool> TryConfirmFromApiAsync(CancellationToken ct = default)
    {
        if (PaidConfirmed)
            return true;
        if (_aborted || PaymentId is not long pid || pid <= 0)
            return false;

        MercadoPagoPixCharge charge;
        try
        {
            charge = await _gateway.GetPaymentAsync(pid, ct).ConfigureAwait(false);
        }
        catch
        {
            UiHint = "Não foi possível consultar o Mercado Pago. O pagamento NÃO foi confirmado.";
            return false;
        }

        ApplyRemoteStatus(charge, persistApprovedIfNeeded: true);
        return PaidConfirmed;
    }

    /// <summary>Fecha o checkout sem venda: GET recente, cancel se pendente, refund se approved.</summary>
    public async Task AbortAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_aborted)
                return;
            _aborted = true;
        }

        PaidConfirmed = false;
        if (PaymentId is not long pid || pid <= 0)
            return;

        MercadoPagoPixCharge? latest = null;
        try
        {
            latest = await _gateway.GetPaymentAsync(pid, ct).ConfigureAwait(false);
        }
        catch
        {
            PixIntentService.MarkStatus(pid, "unknown", "Falha ao consultar status no aborto.");
        }

        var status = latest?.Status ?? "";
        if (PixMpStatus.IsApproved(status))
        {
            PixIntentService.MarkApproved(pid);
            await RefundCoreAsync(pid, ct).ConfigureAwait(false);
            return;
        }

        if (latest is null || PixMpStatus.IsCancellable(status))
        {
            await CancelCoreAsync(pid, ct).ConfigureAwait(false);
            return;
        }

        PixIntentService.MarkStatus(pid, status);
    }

    /// <summary>Approved já persistido, venda não gravada (falha SQLite / operador desistiu depois).</summary>
    public static async Task RefundApprovedWithoutSaleAsync(
        long paymentId, IMercadoPagoPixGateway? gateway = null, CancellationToken ct = default)
    {
        if (paymentId <= 0)
            return;
        var gw = gateway ?? MercadoPagoPixService.Gateway;
        try
        {
            await gw.RefundPaymentAsync(paymentId, Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
            PixIntentService.MarkRefunded(paymentId);
        }
        catch (Exception ex)
        {
            PixIntentService.MarkRefundPending(paymentId, ex.Message);
        }
    }

    private void ApplyRemoteStatus(MercadoPagoPixCharge charge, bool persistApprovedIfNeeded)
    {
        if (PixMpStatus.IsApproved(charge.Status))
        {
            if (persistApprovedIfNeeded)
                ReleaseApproved(charge.PaymentId);
            return;
        }

        PixIntentService.MarkStatus(charge.PaymentId, charge.Status);
        UiStatus = PixMpStatus.WaitingMessage;
        UiHint = PixMpStatus.ProcessingHint;
        if (charge.Status is "cancelled" or "rejected" or "expired")
        {
            UiHint = string.IsNullOrWhiteSpace(charge.StatusDetail)
                ? $"Pagamento {charge.Status}. Não entregue a mercadoria."
                : charge.StatusDetail;
        }
    }

    private void ReleaseApproved(long paymentId)
    {
        lock (_gate)
        {
            if (_aborted)
                return;
            if (_released)
                return;
            PixIntentService.MarkApproved(paymentId);
            _released = true;
            PaidConfirmed = true;
            ConfirmReleaseCount++;
            UiStatus = PixMpStatus.ConfirmedMessage;
            UiHint = "PIX via QR Code aprovado. Finalizando a venda…";
        }
    }

    private async Task CancelCoreAsync(long paymentId, CancellationToken ct)
    {
        CancelCalls++;
        try
        {
            await _gateway.CancelPaymentAsync(paymentId, ct).ConfigureAwait(false);
            PixIntentService.MarkCancelled(paymentId);
        }
        catch (Exception ex)
        {
            PixIntentService.MarkCancelled(paymentId, ex.Message);
        }
    }

    private async Task RefundCoreAsync(long paymentId, CancellationToken ct)
    {
        RefundCalls++;
        try
        {
            await _gateway.RefundPaymentAsync(paymentId, Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
            PixIntentService.MarkRefunded(paymentId);
        }
        catch (Exception ex)
        {
            PixIntentService.MarkRefundPending(paymentId, ex.Message);
        }
    }
}
