namespace SGDB.Domain.Purchases;

public sealed record NfeEffectiveCostDecision
{
    public double GrossProductValue { get; init; }
    public double DiscountValue { get; init; }
    public double IncludedCharges { get; init; }
    public double StCharges { get; init; }
    public double EffectiveLineCost { get; init; }
    public double DanfeLineCostWithoutSt { get; init; }
    public double EffectiveCommercialUnitCost { get; init; }
    public double EffectivePhysicalUnitCost { get; init; }
    public string Source { get; init; } = NfeEffectiveCostSources.Landed;
    public NfeEffectiveCostStatus Status { get; init; } = NfeEffectiveCostStatus.Calculado;
    public double Confidence { get; init; }
    public bool NeedsManualReview { get; init; }
    public bool IncludeInPayable { get; init; } = true;
    public string Explanation { get; init; } = "";

    public bool IsNonPayable => !IncludeInPayable;

    public string StatusBadge => NfeEffectiveCostStatusText.Badge(Status);

    public NfeEffectiveCostDecision WithPhysicalQuantity(double physicalQty)
    {
        var phys = physicalQty > 0.0000001
            ? Math.Round(EffectiveLineCost / physicalQty, 6)
            : EffectiveCommercialUnitCost;
        return this with { EffectivePhysicalUnitCost = phys };
    }

    public NfeEffectiveCostDecision WithDanfeWithoutStOverride(double qCom, double physicalQty)
    {
        var line = DanfeLineCostWithoutSt;
        var commercial = qCom > 0.0000001 ? Math.Round(line / qCom, 6) : line;
        var phys = physicalQty > 0.0000001 ? Math.Round(line / physicalQty, 6) : commercial;
        return this with
        {
            EffectiveLineCost = line,
            EffectiveCommercialUnitCost = commercial,
            EffectivePhysicalUnitCost = phys,
            Source = NfeEffectiveCostSources.DanfeSemSt,
            Explanation = string.IsNullOrWhiteSpace(Explanation)
                ? "Override avançado: custo DANFE sem ICMS-ST/FCP-ST."
                : Explanation + " Override avançado: sem ST.",
        };
    }

    public NfeEffectiveCostDecision AsManual(double lineCost, double commercialQty, double physicalQty)
    {
        var commercial = commercialQty > 0.0000001
            ? Math.Round(lineCost / commercialQty, 6)
            : lineCost;
        var phys = physicalQty > 0.0000001
            ? Math.Round(lineCost / physicalQty, 6)
            : commercial;
        return this with
        {
            EffectiveLineCost = Math.Round(lineCost, 4),
            EffectiveCommercialUnitCost = commercial,
            EffectivePhysicalUnitCost = phys,
            Source = NfeEffectiveCostSources.Manual,
            Status = NfeEffectiveCostStatus.ConferidoManual,
            Confidence = 1,
            NeedsManualReview = false,
            Explanation = "Custo informado manualmente pelo operador.",
        };
    }
}
