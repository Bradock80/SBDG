using System.Windows;

namespace SGDB.Services;

public sealed class PixSaleReverseResult
{
    public static PixSaleReverseResult None { get; } = new();

    public string Outcome { get; init; } = "none";
    public bool CalledRefund { get; init; }
    public bool CalledCancel { get; init; }
    public long? MpPaymentId { get; init; }
    public string? OperatorMessage { get; init; }
    public bool IsCriticalAlert { get; init; }
}

/// <summary>
/// Estorno PIX Mercado Pago no cancelamento de venda já gravada.
/// Cupom, consulta e relatórios passam por aqui via <see cref="PdvService.CancelSale"/>.
/// </summary>
public static class PixSaleReverseService
{
    public static PixSaleReverseResult LastResult { get; private set; } = PixSaleReverseResult.None;

    public static PixSaleReverseResult ReverseForSale(int saleId, IMercadoPagoPixGateway? gateway = null)
    {
        try
        {
            LastResult = ReverseCore(saleId, gateway);
        }
        catch (Exception ex)
        {
            var intent = PixIntentService.GetBySaleId(saleId);
            if (intent is not null)
                PixIntentService.MarkRefundPending(intent.MpPaymentId, ex.Message);
            LastResult = CriticalPending(intent?.MpPaymentId);
        }

        return LastResult;
    }

    public static void ShowOperatorAlert(Window? owner)
    {
        var msg = LastResult.OperatorMessage;
        if (string.IsNullOrWhiteSpace(msg))
            return;
        MessageBox.Show(
            owner,
            msg,
            LastResult.IsCriticalAlert ? "PIX — atenção" : "PIX",
            MessageBoxButton.OK,
            LastResult.IsCriticalAlert ? MessageBoxImage.Error : MessageBoxImage.Information);
    }

    private static PixSaleReverseResult ReverseCore(int saleId, IMercadoPagoPixGateway? gateway)
    {
        var paymentType = LoadPaymentType(saleId);
        var intent = PixIntentService.GetBySaleId(saleId);

        if (intent is null)
        {
            if (PaymentMethodsService.RequiresMercadoPagoQr(null, paymentType))
                return LegacyManual(saleId, paymentType);
            return PixSaleReverseResult.None;
        }

        if (IsLocalRefunded(intent.Status))
        {
            return new PixSaleReverseResult
            {
                Outcome = "already_refunded",
                MpPaymentId = intent.MpPaymentId,
            };
        }

        var gw = gateway ?? MercadoPagoPixService.Gateway;
        MercadoPagoPixCharge? remote = null;
        try
        {
            remote = gw.GetPaymentAsync(intent.MpPaymentId, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch
        {
            PixIntentService.MarkStatus(intent.MpPaymentId, intent.Status,
                "Falha ao consultar status no Mercado Pago.");
        }

        if (remote is not null)
            return ApplyRemote(intent, remote, gw);

        return ApplyLocalFallback(intent, gw);
    }

    private static PixSaleReverseResult ApplyRemote(
        PixIntent intent, MercadoPagoPixCharge remote, IMercadoPagoPixGateway gw)
    {
        if (PixMpStatus.IsRefunded(remote.Status, remote.StatusDetail))
        {
            PixIntentService.MarkRefunded(intent.MpPaymentId);
            Audit(intent, "already_refunded", "Pagamento já reembolsado no Mercado Pago.");
            return new PixSaleReverseResult
            {
                Outcome = "already_refunded",
                MpPaymentId = intent.MpPaymentId,
            };
        }

        if (PixMpStatus.IsApproved(remote.Status))
            return RefundNow(intent, gw);

        if (PixMpStatus.IsCancellable(remote.Status))
            return CancelNow(intent, gw);

        PixIntentService.MarkStatus(intent.MpPaymentId, remote.Status);
        return new PixSaleReverseResult
        {
            Outcome = remote.Status.Trim().ToLowerInvariant(),
            MpPaymentId = intent.MpPaymentId,
        };
    }

    private static PixSaleReverseResult ApplyLocalFallback(PixIntent intent, IMercadoPagoPixGateway gw)
    {
        if (PixMpStatus.IsApproved(intent.Status) || intent.Status == "refund_pending")
            return RefundNow(intent, gw);

        if (PixMpStatus.IsCancellable(intent.Status))
            return CancelNow(intent, gw);

        return new PixSaleReverseResult
        {
            Outcome = intent.Status,
            MpPaymentId = intent.MpPaymentId,
        };
    }

    private static PixSaleReverseResult RefundNow(PixIntent intent, IMercadoPagoPixGateway gw)
    {
        try
        {
            gw.RefundPaymentAsync(intent.MpPaymentId, Guid.NewGuid().ToString("N"), CancellationToken.None)
                .GetAwaiter().GetResult();
            PixIntentService.MarkRefunded(intent.MpPaymentId);
            Audit(intent, "refunded", "Reembolso PIX total no Mercado Pago.");
            return new PixSaleReverseResult
            {
                Outcome = "refunded",
                CalledRefund = true,
                MpPaymentId = intent.MpPaymentId,
            };
        }
        catch (Exception ex)
        {
            PixIntentService.MarkRefundPending(intent.MpPaymentId, ex.Message);
            Audit(intent, "refund_pending", "Falha no reembolso PIX; venda local segue.");
            return CriticalPending(intent.MpPaymentId);
        }
    }

    private static PixSaleReverseResult CancelNow(PixIntent intent, IMercadoPagoPixGateway gw)
    {
        try
        {
            gw.CancelPaymentAsync(intent.MpPaymentId, CancellationToken.None)
                .GetAwaiter().GetResult();
            PixIntentService.MarkCancelled(intent.MpPaymentId);
            Audit(intent, "cancelled", "Cancelamento PIX no Mercado Pago.");
            return new PixSaleReverseResult
            {
                Outcome = "cancelled",
                CalledCancel = true,
                MpPaymentId = intent.MpPaymentId,
            };
        }
        catch (Exception ex)
        {
            PixIntentService.MarkRefundPending(intent.MpPaymentId, ex.Message);
            Audit(intent, "refund_pending", "Falha ao cancelar PIX no Mercado Pago; venda local segue.");
            var pending = CriticalPending(intent.MpPaymentId);
            return new PixSaleReverseResult
            {
                Outcome = pending.Outcome,
                CalledCancel = true,
                MpPaymentId = pending.MpPaymentId,
                IsCriticalAlert = true,
                OperatorMessage = pending.OperatorMessage,
            };
        }
    }

    private static PixSaleReverseResult LegacyManual(int saleId, string paymentType)
    {
        AuditService.LogJson("pix_estorno_manual", "venda", saleId.ToString(),
            new { sale_id = saleId, payment_type = paymentType, reason = "legado_sem_payment_id" },
            $"Venda PIX #{saleId} sem payment_id — estorno manual no Mercado Pago");
        return new PixSaleReverseResult
        {
            Outcome = "legacy_manual",
            IsCriticalAlert = true,
            OperatorMessage =
                "Esta venda PIX é anterior ao vínculo automático com o Mercado Pago.\n" +
                "Não há payment_id para estornar pela API.\n" +
                "Estorne manualmente no Mercado Pago. Não considere o caso encerrado.",
        };
    }

    private static PixSaleReverseResult CriticalPending(long? paymentId) => new()
    {
        Outcome = "refund_pending",
        CalledRefund = true,
        MpPaymentId = paymentId,
        IsCriticalAlert = true,
        OperatorMessage =
            "Venda cancelada no SGDB, mas o reembolso PIX ainda está pendente.\n" +
            $"Pagamento Mercado Pago: {paymentId?.ToString() ?? "—"}.\n" +
            "Não considere o caso encerrado.",
    };

    private static void Audit(PixIntent intent, string outcome, string summary)
    {
        AuditService.LogJson("pix_estorno", "venda", (intent.SaleId ?? 0).ToString(),
            new
            {
                sale_id = intent.SaleId,
                mp_payment_id = intent.MpPaymentId,
                outcome,
            },
            $"PIX venda #{intent.SaleId} · pagamento {intent.MpPaymentId} · {summary}");
    }

    private static bool IsLocalRefunded(string? status) =>
        string.Equals((status ?? "").Trim(), "refunded", StringComparison.OrdinalIgnoreCase);

    private static string LoadPaymentType(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(payment_type,'') FROM sales WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
