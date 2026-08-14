namespace SGDB.Services;

/// <summary>
/// Cliente da API PIX Mercado Pago. A UI/coordenador nunca inventa status:
/// só o retorno desta API pode confirmar pagamento.
/// </summary>
public interface IMercadoPagoPixGateway
{
    Task<MercadoPagoPixCharge> CreatePixAsync(
        double amount,
        string description,
        string idempotencyKey,
        string? payerEmail,
        CancellationToken ct);

    Task<MercadoPagoPixCharge> GetPaymentAsync(long paymentId, CancellationToken ct);

    Task CancelPaymentAsync(long paymentId, CancellationToken ct);

    Task RefundPaymentAsync(long paymentId, string idempotencyKey, CancellationToken ct);
}
