using SGDB.Domain.Commercial;

namespace SGDB.Models;

/// <summary>
/// Identidade e helpers visuais 71C-B5. Sem I/O, SQL, ranking ou decisão.
/// </summary>
public static class CentralDecisionUi
{
    public const string ModuleId = "central_decisao";
    public const int ExpectedQueryCount = 0;

    public const string ModuleTitle = CentralDecisionPresentation.Title;
    public const string ToolbarTitle = "Central";
    public const string Subtitle = CentralDecisionPresentation.Subtitle;

    public const string LoadErrorMessage =
        "Não foi possível carregar a Central de Decisão.";
    public const string RefreshKeepDataMessage =
        "Não foi possível atualizar a Central de Decisão. Os últimos dados carregados foram mantidos.";

    public const string CurrentMonthAction = "Mês atual";
    public const string PreviousMonthAction = "Mês anterior";
    public const string NextMonthAction = "Mês seguinte";
    public const string AboutNumbersTitle = "Sobre estes números";

    public static LoadFailureDecision ResolveLoadFailure(bool hasValidSnapshot) =>
        hasValidSnapshot
            ? new LoadFailureDecision(true, RefreshKeepDataMessage)
            : new LoadFailureDecision(false, LoadErrorMessage);

    public static string FormatCompetenceTitle(CommercialCompetence competence) =>
        CommercialGoalUi.FormatCompetenceTitle(competence);

    public static (string Bg, string Fg, string Accent) ToneColors(
        CommercialGoalPresentationTone tone) =>
        CommercialGoalUi.ToneColors(tone);

    /// <summary>
    /// Snapshot visual Indisponível para falha física sem snapshot válido.
    /// Nunca Empty — não sugere ausência de decisões.
    /// </summary>
    public static CentralDecisionPresentationSnapshot UnavailablePresentation(
        CommercialCompetence competence,
        DateOnly referenceDate,
        string operatorMessage)
    {
        return new CentralDecisionPresentationSnapshot
        {
            State = CentralDecisionState.Unavailable,
            Competence = competence,
            ReferenceDate = referenceDate,
            QueryCount = ExpectedQueryCount,
            Headline = CentralDecisionPresentation.HeadlineUnavailable,
            SupportingText = operatorMessage,
            EmptyText = CentralDecisionPresentation.HeadlineUnavailable,
            GoalStrip = new CentralDecisionGoalStripPresentation
            {
                Goal = Metric("goal", CommercialGoalPresentation.CardGoal,
                    CommercialGoalPresentation.EmDash,
                    CommercialGoalPresentationTone.Unavailable, false),
                Realized = Metric("realized", CommercialGoalPresentation.CardRealized,
                    CommercialGoalPresentation.EmDash,
                    CommercialGoalPresentationTone.Unavailable, false),
                Remaining = Metric("remaining", CommercialGoalPresentation.CardRemaining,
                    CommercialGoalPresentation.EmDash,
                    CommercialGoalPresentationTone.Unavailable, false),
                Status = Metric("status", CommercialGoalPresentation.CardStatus,
                    CommercialGoalPresentation.EmDash,
                    CommercialGoalPresentationTone.Unavailable, false),
            },
            ActNow = [],
            Preserve = [],
            Limitations =
            [
                new CommercialGoalLimitationPresentation
                {
                    Key = "load_failure",
                    Title = LoadErrorMessage,
                    Body = operatorMessage,
                    IsProminent = true,
                },
            ],
        };
    }

    public static bool ShowStateBanner(CentralDecisionPresentationSnapshot presented) =>
        presented.Headline.Length > 0;

    public static bool ShowEmptyDecisionText(CentralDecisionPresentationSnapshot presented) =>
        presented.ActNow.Count == 0
        && presented.EmptyText.Length > 0
        && presented.State is CentralDecisionState.Empty
            or CentralDecisionState.Unavailable
            or CentralDecisionState.Future;

    static CommercialGoalMetricPresentation Metric(
        string key,
        string title,
        string value,
        CommercialGoalPresentationTone tone,
        bool available) =>
        new()
        {
            Key = key,
            Title = title,
            ValueText = value,
            IsAvailable = available,
            Tone = tone,
        };
}
