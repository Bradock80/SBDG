using SGDB.Domain.Common;

namespace SGDB.Domain.Finance;

/// <summary>
/// Geração pura de plano de parcelas (entrada + N parcelas).
/// Determinístico: todas as datas são parâmetros — sem DateTime.Now/Today.
/// </summary>
public static class InstallmentCalculator
{
    /// <summary>
    /// Gera o plano de parcelas conforme a regra legada de <c>PurchaseFinanceHelper.GenerateParcelas</c>.
    /// </summary>
    /// <param name="total">Valor total.</param>
    /// <param name="downPayment">Entrada (pode gerar linha extra com <paramref name="baseDate"/>).</param>
    /// <param name="installmentCount">Qtd de parcelas (mínimo 1).</param>
    /// <param name="firstDueDate">Vencimento da 1ª parcela (não da entrada).</param>
    /// <param name="interval">Intervalo em dias ou meses (mínimo 1).</param>
    /// <param name="intervalInMonths">true = meses via <see cref="DateTime.AddMonths"/>; false = dias.</param>
    /// <param name="baseDate">Data da linha de entrada quando <paramref name="downPayment"/> &gt; 0,001 (ex.: “hoje” do App).</param>
    public static IReadOnlyList<InstallmentPlanItem> Generate(
        double total,
        double downPayment,
        int installmentCount,
        DateTime firstDueDate,
        int interval,
        bool intervalInMonths,
        DateTime baseDate)
    {
        var list = new List<InstallmentPlanItem>();
        var rest = Math.Max(0, total - downPayment);
        installmentCount = Math.Max(1, installmentCount);
        interval = Math.Max(1, interval);

        var number = 1;
        if (downPayment > 0.001)
        {
            list.Add(new InstallmentPlanItem
            {
                Number = number++,
                DueDate = baseDate.Date,
                ChargeType = "Dinheiro",
                Amount = MonetaryRounding.Round(downPayment),
            });
        }

        var installmentValue = MonetaryRounding.Round(rest / installmentCount);
        // Legado: acumulado inicia com a entrada bruta (não arredondada).
        var accumulated = downPayment;
        for (var i = 0; i < installmentCount; i++)
        {
            var amount = installmentValue;
            if (i == installmentCount - 1)
                amount = MonetaryRounding.Round(total - accumulated);
            accumulated += amount;

            var due = intervalInMonths
                ? firstDueDate.Date.AddMonths(interval * i)
                : firstDueDate.Date.AddDays(interval * i);

            list.Add(new InstallmentPlanItem
            {
                Number = number++,
                DueDate = due,
                ChargeType = "Boleto",
                Amount = Math.Max(0, amount),
            });
        }

        return list;
    }
}
