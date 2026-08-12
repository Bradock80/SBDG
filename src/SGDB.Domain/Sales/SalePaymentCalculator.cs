using SGDB.Domain.Common;

namespace SGDB.Domain.Sales;

/// <summary>
/// Regras puras de partes de pagamento, troco e fiado puro.
/// Labels (Dinheiro/Fiado/aliases) são classificados pelo App via predicados.
/// </summary>
public static class SalePaymentCalculator
{
    /// <summary>Tolerância legada: soma das partes vs total da venda.</summary>
    public const double PaymentSumTolerance = 0.02;

    /// <summary>
    /// Normaliza/valida partes. Esperado: <paramref name="paymentType"/> e cada
    /// <see cref="PaymentPart.PaymentType"/> já normalizados pelo App.
    /// </summary>
    public static IReadOnlyList<PaymentPart> NormalizeParts(
        string paymentType,
        double total,
        IReadOnlyList<PaymentPart>? payments)
    {
        if (payments is { Count: > 0 })
        {
            var parts = payments
                .Select(p => new PaymentPart
                {
                    PaymentType = p.PaymentType,
                    Amount = MonetaryRounding.Round(p.Amount),
                })
                .Where(p => p.Amount > 0)
                .ToList();
            if (parts.Count == 0)
                throw new ArgumentException("Informe ao menos uma forma de pagamento.");
            var sum = MonetaryRounding.Round(parts.Sum(p => p.Amount));
            if (Math.Abs(sum - total) > PaymentSumTolerance)
                throw new ArgumentException(
                    $"Soma dos pagamentos (R$ {sum:N2}) difere do total (R$ {total:N2}).");
            return parts;
        }

        return
        [
            new PaymentPart
            {
                PaymentType = paymentType,
                Amount = total,
            },
        ];
    }

    /// <summary>
    /// Calcula cash_received / change_amount. Troco só sobre partes de dinheiro
    /// identificadas por <paramref name="isCash"/>.
    /// </summary>
    public static CashChangeResult ResolveCashChange(
        IReadOnlyList<PaymentPart> parts,
        double total,
        double cashReceivedInput,
        Func<string, bool> isCash)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(isCash);

        var recv = MonetaryRounding.Round(cashReceivedInput);
        if (recv <= 0)
            return new CashChangeResult(null, 0);

        var dinheiroAmt = MonetaryRounding.Round(
            parts.Where(p => isCash(p.PaymentType)).Sum(p => p.Amount));
        if (dinheiroAmt <= 0 && parts.Count == 1 && isCash(parts[0].PaymentType))
            dinheiroAmt = total;

        // Sem componente em dinheiro: ignora cashReceived (evita troco fantasma em PIX/cartão).
        if (dinheiroAmt <= 0)
            return new CashChangeResult(null, 0);

        if (recv <= dinheiroAmt + 0.009)
            return new CashChangeResult(null, 0);

        return new CashChangeResult(recv, MonetaryRounding.Round(recv - dinheiroAmt));
    }

    /// <summary>
    /// True somente quando há exatamente uma parte e ela é fiado (<paramref name="isFiado"/>).
    /// </summary>
    public static bool IsPureFiadoPayment(
        IReadOnlyList<PaymentPart> parts,
        Func<string, bool> isFiado)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(isFiado);
        return parts.Count == 1 && isFiado(parts[0].PaymentType);
    }
}
