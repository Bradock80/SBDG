namespace SGDB.Domain.Commercial;

/// <summary>
/// Chaves app_settings da Meta Comercial 71B-B3.
/// Competência mensal sempre via <see cref="CommercialCompetence"/>.
/// </summary>
public static class CommercialGoalSettingKeys
{
    public const string Default = "negocio_meta_lucro_bruto_default";
    public const string MonthlyPrefix = "negocio_meta_lucro_bruto_";

    public static string Monthly(CommercialCompetence competence) =>
        MonthlyPrefix + competence.ToString();
}

public enum CommercialGoalStoredSettingStatus
{
    Missing = 0,
    Configured,
    Invalid,
}

public enum CommercialGoalStoredSettingReason
{
    None = 0,
    Missing,
    EmptyValue,
    NonInvariantFormat,
    NotPositive,
    Invalid,
}

/// <summary>
/// Origem da meta resolvida para uma competência.
/// Invalid* = chave presente e corrompida; não usa fallback silencioso.
/// </summary>
public enum CommercialGoalSettingSource
{
    None = 0,
    Default,
    MonthlyOverride,
    InvalidDefault,
    InvalidMonthlyOverride,
}

/// <summary>
/// Leitura de uma chave isolada (default ou override).
/// </summary>
public sealed class CommercialGoalStoredSetting
{
    public CommercialGoalStoredSettingStatus Status { get; init; } =
        CommercialGoalStoredSettingStatus.Missing;

    public decimal? GoalAmount { get; init; }
    public string? RawValue { get; init; }
    public IReadOnlyList<CommercialGoalStoredSettingReason> Reasons { get; init; } = [];
    public int QueryCount { get; init; }
}

/// <summary>
/// Resolução default + override para uma competência.
/// GoalAmount null = SemMeta ou configuração inválida (nunca 0 como N/A).
/// </summary>
public sealed class CommercialGoalSettingResolution
{
    public CommercialCompetence Competence { get; init; }
    public CommercialGoalSettingSource Source { get; init; }
    public decimal? GoalAmount { get; init; }
    public bool HasValidGoal { get; init; }

    public CommercialGoalStoredSetting MonthlyOverride { get; init; } = new();
    public CommercialGoalStoredSetting? DefaultSetting { get; init; }

    public int QueryCount { get; init; }

    public string HistoricalDefaultLimitation { get; init; } =
        CommercialGoalSettingsSemantics.HistoricalDefaultLimitation;
}

public static class CommercialGoalSettingsSemantics
{
    public const string HistoricalDefaultLimitation =
        "Meses sem override usam o default vigente na consulta. "
        + "Alterar o default pode mudar metas históricas que nunca tiveram override. "
        + "Meses com override permanecem reproduzíveis.";
}

public sealed class CommercialGoalSettingSaveResult
{
    public bool Written { get; init; }
    public CommercialGoalStoredSetting Setting { get; init; } = new();
}
