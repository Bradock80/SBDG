using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69N-B — pagamento misto vs substituicao de forma no PDV.
/// Nao confunde alocacao PIX parcial (split) com abandono do PIX.
/// </summary>
public static class PdvPaymentSplitRules
{
    /// <summary>
    /// Decide se a alocacao PIX ainda nao confirmada no Mercado Pago deve ser
    /// zerada ao trocar para outra forma.
    /// Split intencional (PIX &lt; total): preservar.
    /// Substituicao (PIX cobre o total / valor integral): liberar.
    /// </summary>
    public static bool ShouldClearUnpaidPixOnMethodSwitch(
        double pixAllocated,
        double pixPaidConfirmed,
        double totalAPagar,
        bool switchingToPixMethod)
    {
        if (switchingToPixMethod)
            return false;
        if (pixPaidConfirmed > 0.009)
            return false;
        if (pixAllocated < 0.009)
            return false;
        if (!double.IsFinite(totalAPagar) || totalAPagar < 0)
            return false;

        // Pagamento misto: PIX parcial permanece na composicao.
        if (pixAllocated + 0.02 < totalAPagar)
            return false;

        // PIX cobria o total (substituicao de forma ainda nao paga no MP).
        return true;
    }

    /// <summary>Saldo restante para a forma atual, excluindo as demais ja alocadas.</summary>
    public static double RemainingAmount(double totalAPagar, double sumOtherMethods)
    {
        if (!double.IsFinite(totalAPagar) || !double.IsFinite(sumOtherMethods))
            return 0;
        return ProductPriceHelper.RoundPrice(Math.Max(0, totalAPagar - sumOtherMethods));
    }
}
