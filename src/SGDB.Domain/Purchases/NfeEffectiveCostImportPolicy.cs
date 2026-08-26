namespace SGDB.Domain.Purchases;

/// <summary>
/// Semântica única do override ST nas duas telas (Movimento e Compras).
/// Padrão = custo efetivo do resolver. Checkbox avançado = DANFE sem ST.
/// </summary>
public static class NfeEffectiveCostImportPolicy
{
    public const bool DefaultIncludeIcmsStInCost = true;

    public const string AdvancedDanfeWithoutStLabel = "Avançado: custo DANFE sem ICMS-ST";

    /// <summary>
    /// Checkbox avançado marcado → não incluir ST. Desmarcado (padrão) → resolver com ST.
    /// </summary>
    public static bool IncludeIcmsStFromAdvancedOverride(bool? danfeWithoutStChecked) =>
        danfeWithoutStChecked != true;
}
