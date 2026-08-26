using SGDB.Domain.Common;

namespace SGDB.Domain.Purchases;

public sealed record NfeCostReconciliationResult
{
    public double ExpectedPayable { get; init; }
    public double CalculatedEffectiveCost { get; init; }
    public double Difference { get; init; }
    public bool IsReconciled { get; init; }
    public string ExpectedSource { get; init; } = "";
    public string Explanation { get; init; } = "";
    public string FooterStatus { get; init; } = "Revisar antes de finalizar";
}

/// <summary>
/// Concilia soma dos custos efetivos pagos com fatura/vPag/duplicatas/vNF.
/// Não mistura bonificação, remessa nem indTot=0.
/// </summary>
public static class NfeCostReconciliation
{
    public static NfeCostReconciliationResult Reconcile(
        IReadOnlyList<double> payableLineCosts,
        double fatLiq,
        double dupSum,
        double pagSum,
        double headerVNf,
        double excludedGross = 0)
    {
        var calculated = MonetaryRounding.Round(payableLineCosts.Sum());
        var (expected, source) = PickExpected(fatLiq, dupSum, pagSum, headerVNf, excludedGross);
        var diff = MonetaryRounding.Round(calculated - expected);
        var ok = expected > 0 && NfeCostTolerance.NearlyEqual(calculated, expected, expected);
        if (expected <= 0)
        {
            return new NfeCostReconciliationResult
            {
                ExpectedPayable = 0,
                CalculatedEffectiveCost = calculated,
                Difference = calculated,
                IsReconciled = false,
                ExpectedSource = "nenhum",
                Explanation = "NF sem fatura/vPag/vNF para conciliar.",
                FooterStatus = "Revisar antes de finalizar",
            };
        }

        return new NfeCostReconciliationResult
        {
            ExpectedPayable = expected,
            CalculatedEffectiveCost = calculated,
            Difference = diff,
            IsReconciled = ok,
            ExpectedSource = source,
            Explanation = ok
                ? $"Custo efetivo {calculated:N2} confere com {source} {expected:N2}."
                : $"Custo efetivo {calculated:N2} vs {source} {expected:N2} (diferença {diff:N2}).",
            FooterStatus = ok ? "NF-e conferida" : "Revisar antes de finalizar",
        };
    }

    static (double Value, string Source) PickExpected(
        double fatLiq, double dupSum, double pagSum, double headerVNf, double excludedGross)
    {
        if (fatLiq > 0.05)
            return (MonetaryRounding.Round(fatLiq), "fat.vLiq");
        if (dupSum > 0.05)
            return (MonetaryRounding.Round(dupSum), "duplicatas");
        if (pagSum > 0.05)
            return (MonetaryRounding.Round(pagSum), "vPag");
        if (headerVNf > 0.05)
        {
            var adjusted = MonetaryRounding.Round(Math.Max(0, headerVNf - Math.Max(0, excludedGross)));
            return (adjusted, excludedGross > 0.05 ? "vNF − itens não pagos" : "vNF");
        }
        return (0, "nenhum");
    }
}
