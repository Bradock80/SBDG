using SGDB.Domain.Commercial;
using SGDB.Utils;

namespace SGDB.Models;

public enum CommercialGoalPresentationTone
{
    Neutral = 0,
    Positive,
    Attention,
    Warning,
    Unavailable,
}

public sealed class CommercialGoalMetricPresentation
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string ValueText { get; init; } = CommercialGoalPresentation.EmDash;
    public string SupportingText { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public bool IsAvailable { get; init; }
    public CommercialGoalPresentationTone Tone { get; init; }
}

public sealed class CommercialGoalLimitationPresentation
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public bool IsProminent { get; init; }
}

/// <summary>
/// Snapshot PT-BR 71B-B5. Sem WPF. Cards e limitações prontos para binding.
/// </summary>
public sealed class CommercialGoalPresentationSnapshot
{
    public CommercialCompetence Competence { get; init; }
    public DateOnly ReferenceDate { get; init; }
    public int QueryCount { get; init; }

    public string ModuleTitle { get; init; } = CommercialGoalPresentation.ModuleTitle;
    public string CompetenceText { get; init; } = "";

    public string GoalOriginText { get; init; } = "";
    public string Headline { get; init; } = "";
    public string SupportingText { get; init; } = "";
    public string StatusText { get; init; } = "";
    public CommercialGoalPresentationTone StatusTone { get; init; }

    public required CommercialGoalMetricPresentation Goal { get; init; }
    public required CommercialGoalMetricPresentation Realized { get; init; }
    public required CommercialGoalMetricPresentation Remaining { get; init; }
    public required CommercialGoalMetricPresentation Achievement { get; init; }
    public required CommercialGoalMetricPresentation RequiredPace { get; init; }
    public required CommercialGoalMetricPresentation LinearProjection { get; init; }
    public required CommercialGoalMetricPresentation Status { get; init; }

    public string EstimatedBadge { get; init; } = "";
    public string EstimatedExplanation { get; init; } = "";
    public bool ShowEstimatedBadge => EstimatedBadge.Length > 0;

    public IReadOnlyList<CommercialGoalMetricPresentation> Cards { get; init; } = [];
    public IReadOnlyList<CommercialGoalLimitationPresentation> Limitations { get; init; } = [];
}

/// <summary>
/// Apresentação PT-BR 71B-B5. Consome B4. Sem I/O, SQL, WPF ou recálculo.
/// </summary>
public static class CommercialGoalPresentation
{
    public const int ExpectedQueryCount = 0;
    public const string EmDash = InventoryProjectionPresentation.EmDash;
    public const string ModuleTitle = "Meta Comercial";

    public const string CardGoal = "Meta de lucro bruto";
    public const string CardRealized = "Realizado";
    public const string CardRemaining = "Falta para a meta";
    public const string CardAchievement = "Atingimento";
    public const string CardPace = "Ritmo necessário";
    public const string CardProjection = "Projeção linear";
    public const string CardStatus = "Status";

    public const string OriginOverride = "Meta específica do mês";
    public const string OriginDefault = "Meta padrão";
    public const string OriginNone = "Meta não configurada";
    public const string OriginInvalidOverride = "Meta mensal inválida";
    public const string OriginInvalidDefault = "Meta padrão inválida";

    public const string GoalNotConfigured = "Não configurada";
    public const string GoalInvalid = "Configuração inválida";

    public const string StatusNoGoal = "Sem meta";
    public const string StatusInvalidGoal = "Meta inválida";
    public const string StatusNotStarted = "Mês ainda não iniciado";
    public const string StatusAchieved = "Meta atingida";
    public const string StatusAbovePace = "Acima do ritmo";
    public const string StatusOnPace = "No ritmo da meta";
    public const string StatusBelowPace = "Abaixo do ritmo";

    public const string HeadlineNoGoal = "Defina uma meta de lucro bruto";
    public const string SupportingNoGoal =
        "Os resultados financeiros do mês continuam disponíveis, mas o acompanhamento de progresso exige uma meta.";

    public const string HeadlineUnavailable = "Lucro bruto indisponível";
    public const string SupportingUnavailable =
        "Não foi possível determinar o custo de todas as vendas desta competência.";

    public const string SupportingInvalidOverride =
        "A configuração específica desta competência precisa ser corrigida.";
    public const string SupportingInvalidDefault =
        "A configuração padrão da meta precisa ser corrigida.";

    public const string RealizedEstimatedMark = "Estimado";
    public const string EstimatedBadge = "Lucro estimado";
    public const string EstimatedExplanation =
        "Parte do CMV usa custo atual como estimativa porque algumas vendas antigas não possuem custo histórico registrado.";

    public const string ProjectionSupporting = "Projeção linear no ritmo atual";
    public const string ProjectionTooltip =
        "Extrapolação baseada nos dias civis já decorridos; não é previsão de vendas.";

    public const string LimitationLegacyTitle = EstimatedBadge;
    public const string LimitationLegacyBody = EstimatedExplanation;
    public const string LimitationExchangesTitle = "Devoluções e trocas";
    public const string LimitationExchangesBody =
        "Devoluções e trocas ainda não estornam automaticamente os valores desta análise.";
    public const string LimitationLinearTitle = "Ritmo por dias civis";
    public const string LimitationLinearBody = "Ritmo calculado por dias civis.";
    public const string LimitationCurrentDayTitle = "Dia atual integral";
    public const string LimitationCurrentDayBody =
        "O dia atual é considerado integralmente no cálculo.";
    public const string LimitationHistoricalDefaultTitle = "Meta padrão vigente";
    public const string LimitationHistoricalDefaultBody =
        "Esta competência usa a meta padrão. Alterações futuras na meta padrão podem mudar a meta exibida para este mês enquanto não houver uma meta específica.";

    public static CommercialGoalPresentationSnapshot Apply(CommercialGoalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var unavailable = snapshot.ProgressSkipReason.HasFlag(
            CommercialGoalProgressSkipReason.GrossProfitUnavailable);
        var invalid = snapshot.ProgressSkipReason.HasFlag(
            CommercialGoalProgressSkipReason.InvalidGoalConfiguration);
        var estimated = snapshot.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate);

        var goal = PresentGoal(snapshot);
        var realized = PresentRealized(snapshot, unavailable, estimated);
        var remaining = PresentRemaining(snapshot, unavailable, invalid);
        var achievement = PresentAchievement(snapshot, unavailable, invalid);
        var pace = PresentPace(snapshot, unavailable, invalid);
        var projection = PresentProjection(snapshot, unavailable, invalid);
        var status = PresentStatus(snapshot, unavailable, invalid);
        var (headline, supporting) = PresentHeadline(snapshot, unavailable, invalid, status.ValueText);
        var limitations = PresentLimitations(snapshot);

        var cards = new CommercialGoalMetricPresentation[]
        {
            goal, realized, remaining, achievement, pace, projection, status,
        };

        return new CommercialGoalPresentationSnapshot
        {
            Competence = snapshot.Competence,
            ReferenceDate = snapshot.ReferenceDate,
            QueryCount = snapshot.QueryCount,
            ModuleTitle = ModuleTitle,
            CompetenceText = FormatCompetence(snapshot.Competence),
            GoalOriginText = GoalOriginText(snapshot.GoalSource),
            Headline = headline,
            SupportingText = supporting,
            StatusText = status.ValueText,
            StatusTone = status.Tone,
            Goal = goal,
            Realized = realized,
            Remaining = remaining,
            Achievement = achievement,
            RequiredPace = pace,
            LinearProjection = projection,
            Status = status,
            EstimatedBadge = estimated ? EstimatedBadge : "",
            EstimatedExplanation = estimated ? EstimatedExplanation : "",
            Cards = cards,
            Limitations = limitations,
        };
    }

    public static string GoalOriginText(CommercialGoalSettingSource source) =>
        source switch
        {
            CommercialGoalSettingSource.MonthlyOverride => OriginOverride,
            CommercialGoalSettingSource.Default => OriginDefault,
            CommercialGoalSettingSource.InvalidMonthlyOverride => OriginInvalidOverride,
            CommercialGoalSettingSource.InvalidDefault => OriginInvalidDefault,
            _ => OriginNone,
        };

    public static string StatusText(CommercialGoalStatus status) =>
        status switch
        {
            CommercialGoalStatus.NoGoal => StatusNoGoal,
            CommercialGoalStatus.InvalidGoal => StatusInvalidGoal,
            CommercialGoalStatus.NotStarted => StatusNotStarted,
            CommercialGoalStatus.Achieved => StatusAchieved,
            CommercialGoalStatus.AbovePace => StatusAbovePace,
            CommercialGoalStatus.OnPace => StatusOnPace,
            CommercialGoalStatus.BelowPace => StatusBelowPace,
            _ => EmDash,
        };

    public static CommercialGoalPresentationTone StatusTone(CommercialGoalStatus status) =>
        status switch
        {
            CommercialGoalStatus.Achieved or CommercialGoalStatus.AbovePace =>
                CommercialGoalPresentationTone.Positive,
            CommercialGoalStatus.BelowPace => CommercialGoalPresentationTone.Attention,
            CommercialGoalStatus.InvalidGoal => CommercialGoalPresentationTone.Warning,
            _ => CommercialGoalPresentationTone.Neutral,
        };

    public static string FormatMoney(decimal? value)
    {
        if (value is not decimal amount)
            return EmDash;
        return ProductPriceHelper.MoneyBr((double)amount);
    }

    public static string FormatPercent(decimal? ratio)
    {
        if (ratio is not decimal value)
            return EmDash;
        var percent = value * 100m;
        return percent.ToString("N2", ProductPriceHelper.Br) + "%";
    }

    public static string FormatDays(int days)
    {
        var n = days < 0 ? 0 : days;
        if (n == 1)
            return "1 dia";
        return $"{n.ToString("N0", ProductPriceHelper.Br)} dias";
    }

    public static string FormatCompetence(CommercialCompetence competence) =>
        competence.StartDate.ToString("MMMM 'de' yyyy", ProductPriceHelper.Br);

    public static string FormatPace(decimal? perDay)
    {
        var money = FormatMoney(perDay);
        return money == EmDash ? EmDash : money + "/dia";
    }

    static CommercialGoalMetricPresentation PresentGoal(CommercialGoalSnapshot snapshot)
    {
        if (snapshot.HasValidGoal)
        {
            return Metric(
                "goal", CardGoal, FormatMoney(snapshot.GoalAmount),
                GoalOriginText(snapshot.GoalSource),
                available: true);
        }

        if (snapshot.GoalSource is CommercialGoalSettingSource.InvalidDefault
            or CommercialGoalSettingSource.InvalidMonthlyOverride)
        {
            return Metric(
                "goal", CardGoal, GoalInvalid, GoalOriginText(snapshot.GoalSource),
                available: false, CommercialGoalPresentationTone.Warning);
        }

        return Metric(
            "goal", CardGoal, GoalNotConfigured, OriginNone,
            available: false);
    }

    static CommercialGoalMetricPresentation PresentRealized(
        CommercialGoalSnapshot snapshot, bool unavailable, bool estimated)
    {
        if (unavailable || !snapshot.GrossProfitAvailable || snapshot.GrossProfit is null)
        {
            return Metric(
                "realized", CardRealized, EmDash, "",
                available: false, CommercialGoalPresentationTone.Unavailable);
        }

        var supporting = estimated ? RealizedEstimatedMark : "";
        var tone = estimated
            ? CommercialGoalPresentationTone.Attention
            : CommercialGoalPresentationTone.Neutral;
        return Metric(
            "realized", CardRealized, FormatMoney(snapshot.GrossProfit), supporting,
            available: true, tone);
    }

    static CommercialGoalMetricPresentation PresentRemaining(
        CommercialGoalSnapshot snapshot, bool unavailable, bool invalid)
    {
        if (unavailable || invalid || snapshot.Progress?.RemainingAmount is not decimal remaining)
            return Metric("remaining", CardRemaining, EmDash, available: false);

        return Metric("remaining", CardRemaining, FormatMoney(remaining), available: true);
    }

    static CommercialGoalMetricPresentation PresentAchievement(
        CommercialGoalSnapshot snapshot, bool unavailable, bool invalid)
    {
        if (unavailable || invalid || snapshot.Progress?.AchievementRatio is not decimal ratio)
            return Metric("achievement", CardAchievement, EmDash, available: false);

        return Metric("achievement", CardAchievement, FormatPercent(ratio), available: true);
    }

    static CommercialGoalMetricPresentation PresentPace(
        CommercialGoalSnapshot snapshot, bool unavailable, bool invalid)
    {
        if (unavailable || invalid
            || snapshot.Progress is not { HasRequiredPace: true } progress
            || progress.RequiredGrossProfitPerRemainingDay is not decimal pace)
        {
            return Metric("pace", CardPace, EmDash, available: false);
        }

        return Metric("pace", CardPace, FormatPace(pace), available: true);
    }

    static CommercialGoalMetricPresentation PresentProjection(
        CommercialGoalSnapshot snapshot, bool unavailable, bool invalid)
    {
        if (unavailable || invalid
            || snapshot.Progress is not { HasLinearProjection: true } progress
            || progress.ProjectedMonthEndGrossProfit is not decimal projected)
        {
            return Metric("projection", CardProjection, EmDash, available: false);
        }

        return Metric(
            "projection", CardProjection, FormatMoney(projected),
            ProjectionSupporting, available: true,
            tooltip: ProjectionTooltip);
    }

    static CommercialGoalMetricPresentation PresentStatus(
        CommercialGoalSnapshot snapshot, bool unavailable, bool invalid)
    {
        if (snapshot.Progress is not null)
        {
            var text = StatusText(snapshot.Progress.Status);
            return Metric(
                "status", CardStatus, text, available: true,
                tone: StatusTone(snapshot.Progress.Status));
        }

        if (invalid)
        {
            return Metric(
                "status", CardStatus, StatusInvalidGoal, available: false,
                tone: CommercialGoalPresentationTone.Warning);
        }

        if (unavailable)
        {
            return Metric(
                "status", CardStatus, EmDash, available: false,
                tone: CommercialGoalPresentationTone.Unavailable);
        }

        return Metric("status", CardStatus, EmDash, available: false);
    }

    static (string Headline, string Supporting) PresentHeadline(
        CommercialGoalSnapshot snapshot,
        bool unavailable,
        bool invalid,
        string statusText)
    {
        if (unavailable)
            return (HeadlineUnavailable, SupportingUnavailable);

        if (invalid)
        {
            return snapshot.GoalSource == CommercialGoalSettingSource.InvalidMonthlyOverride
                ? (OriginInvalidOverride, SupportingInvalidOverride)
                : (OriginInvalidDefault, SupportingInvalidDefault);
        }

        if (snapshot.GoalSource == CommercialGoalSettingSource.None)
            return (HeadlineNoGoal, SupportingNoGoal);

        return (statusText, "");
    }

    static IReadOnlyList<CommercialGoalLimitationPresentation> PresentLimitations(
        CommercialGoalSnapshot snapshot)
    {
        var list = new List<CommercialGoalLimitationPresentation>(5);
        AddLimitation(
            list, snapshot, CommercialGoalLimitation.LegacyCostEstimate,
            "legacy", LimitationLegacyTitle, LimitationLegacyBody, prominent: true);
        AddLimitation(
            list, snapshot, CommercialGoalLimitation.ExchangesNotAdjusted,
            "exchanges", LimitationExchangesTitle, LimitationExchangesBody, prominent: false);
        AddLimitation(
            list, snapshot, CommercialGoalLimitation.LinearCalendarProjection,
            "linear", LimitationLinearTitle, LimitationLinearBody, prominent: false);
        AddLimitation(
            list, snapshot, CommercialGoalLimitation.CurrentDayTreatedAsWholeDay,
            "current-day", LimitationCurrentDayTitle, LimitationCurrentDayBody, prominent: false);
        AddLimitation(
            list, snapshot, CommercialGoalLimitation.HistoricalDefaultCanChange,
            "historical-default", LimitationHistoricalDefaultTitle, LimitationHistoricalDefaultBody,
            prominent: false);
        return list;
    }

    static void AddLimitation(
        List<CommercialGoalLimitationPresentation> list,
        CommercialGoalSnapshot snapshot,
        CommercialGoalLimitation flag,
        string key,
        string title,
        string body,
        bool prominent)
    {
        if (!snapshot.HasLimitation(flag))
            return;
        list.Add(new CommercialGoalLimitationPresentation
        {
            Key = key,
            Title = title,
            Body = body,
            IsProminent = prominent,
        });
    }

    static CommercialGoalMetricPresentation Metric(
        string key,
        string title,
        string value,
        string supporting = "",
        bool available = true,
        CommercialGoalPresentationTone tone = CommercialGoalPresentationTone.Neutral,
        string tooltip = "") =>
        new()
        {
            Key = key,
            Title = title,
            ValueText = value,
            SupportingText = supporting,
            Tooltip = tooltip,
            IsAvailable = available,
            Tone = tone,
        };
}
