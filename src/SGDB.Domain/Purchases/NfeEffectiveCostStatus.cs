namespace SGDB.Domain.Purchases;

public enum NfeEffectiveCostStatus
{
    Conferido = 1,
    Calculado,
    Revisar,
    Bonificacao,
    Remessa,
    Divergente,
    ConferidoManual,
}

public static class NfeEffectiveCostSources
{
    public const string Landed = "Landed";
    public const string PrecoUnitarioFinal = "Preco Unitario Final";
    public const string Manual = "MANUAL";
    public const string Bonificacao = "Bonificacao";
    public const string Remessa = "Remessa";
    public const string IndTotZero = "indTot=0";
    public const string DanfeSemSt = "DANFE sem ST";
}

public static class NfeEffectiveCostStatusText
{
    public static string Badge(NfeEffectiveCostStatus status) => status switch
    {
        NfeEffectiveCostStatus.Conferido => "CONFERIDO",
        NfeEffectiveCostStatus.Calculado => "CALCULADO",
        NfeEffectiveCostStatus.Revisar => "REVISAR",
        NfeEffectiveCostStatus.Bonificacao => "BONIFICAÇÃO",
        NfeEffectiveCostStatus.Remessa => "REMESSA",
        NfeEffectiveCostStatus.Divergente => "DIVERGENTE",
        NfeEffectiveCostStatus.ConferidoManual => "CONFERIDO_MANUAL",
        _ => "REVISAR",
    };

    public static string Compact(NfeEffectiveCostStatus status) => status switch
    {
        NfeEffectiveCostStatus.Conferido => "OK",
        NfeEffectiveCostStatus.ConferidoManual => "OK",
        NfeEffectiveCostStatus.Calculado => "CALC.",
        NfeEffectiveCostStatus.Revisar => "REVISAR",
        NfeEffectiveCostStatus.Divergente => "REVISAR",
        NfeEffectiveCostStatus.Bonificacao => "BONIF.",
        NfeEffectiveCostStatus.Remessa => "REMESSA",
        _ => "REVISAR",
    };
}
