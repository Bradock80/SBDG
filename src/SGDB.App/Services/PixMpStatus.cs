namespace SGDB.Services;

/// <summary>Interpretação do status PIX do Mercado Pago. Só <c>approved</c> libera venda.</summary>
public static class PixMpStatus
{
    public const string WaitingMessage =
        "NÃO ENTREGUE A MERCADORIA.\nAguardando confirmação do Mercado Pago.";

    public const string ConfirmedMessage = "PIX CONFIRMADO PELO MERCADO PAGO.";

    public const string ProcessingHint =
        "PIX aguardando confirmação do Mercado Pago.\nNão entregue a mercadoria ainda.";

    public static bool IsApproved(string? status) =>
        string.Equals((status ?? "").Trim(), "approved", StringComparison.OrdinalIgnoreCase);

    /// <summary>Estados em que o MP ainda aceita cancel (não refund).</summary>
    public static bool IsCancellable(string? status)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s is "pending" or "in_process" or "authorized" or "waiting_transfer" or "";
    }

    public static bool IsRefunded(string? status, string? statusDetail = null)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        if (s is "refunded" or "charged_back")
            return true;
        var d = (statusDetail ?? "").Trim().ToLowerInvariant();
        return d.Contains("refunded", StringComparison.Ordinal)
               || d.Contains("charged_back", StringComparison.Ordinal);
    }

    public static bool IsTerminalNoRefund(string? status)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s is "cancelled" or "rejected" or "expired";
    }
}
