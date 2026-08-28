namespace SGDB.Services;

/// <summary>70I-B1 — códigos e mensagens da barreira de saída com validade vencida.</summary>
public static class ExpirySaleRules
{
    public const string InsufficientNonExpired = "ExpirySaleInsufficientNonExpired";
    public const string TransferInsufficientNonExpired = "ExpirySaleTransferInsufficientNonExpired";

    public const string InsufficientNonExpiredMessage =
        "Venda bloqueada: a quantidade pedida no depósito dependeria de unidades comprovadamente vencidas. " +
        "Regularize lotes e validades ou venda apenas a quantidade não vencida.";

    public const string TransferInsufficientNonExpiredMessage =
        "Transferência bloqueada: a quantidade pedida só poderia vir de unidades comprovadamente vencidas no depósito. " +
        "Regularize lotes e validades antes de mover para a geladeira.";

    /// <summary>
    /// Alinha com ProductExpiryService / LotCoverageService:
    /// vencido = expiry_date.Date &lt; DateTime.Today (vence hoje ainda é válido).
    /// </summary>
    public static bool IsExpired(DateTime? expiry, DateTime? today = null)
    {
        if (expiry is not DateTime d)
            return false;
        var day = (today ?? DateTime.Today).Date;
        return d.Date < day;
    }

    public static bool IsValidDated(DateTime? expiry, DateTime? today = null)
    {
        if (expiry is not DateTime d)
            return false;
        var day = (today ?? DateTime.Today).Date;
        return d.Date >= day;
    }
}
