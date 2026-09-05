using SGDB.Domain.Commercial;
using SGDB.Utils;

namespace SGDB.Models;

/// <summary>
/// Identidade e layout 71B-B6. Sem I/O, SQL ou recálculo B1–B5.
/// </summary>
public static class CommercialGoalUi
{
    public const string ModuleId = "meta_comercial";
    public const int ExpectedQueryCount = 0;

    public const string ModuleTitle = CommercialGoalPresentation.ModuleTitle;
    public const string ToolbarTitle = "Meta";
    public const string Subtitle = "Acompanhe o lucro bruto do mês em relação à meta configurada.";

    public const string LoadErrorMessage = "Não foi possível carregar a Meta Comercial.";
    public const string RefreshKeepDataMessage =
        "Não foi possível atualizar a Meta Comercial. Os últimos dados carregados foram mantidos.";

    public const string ConfigureAction = "Configurar meta";
    public const string ActionPlanSectionTitle = CommercialGoalActionPlanPresentation.SectionTitle;
    public const string AboutNumbersTitle = "Sobre estes números";
    public const string CurrentMonthAction = "Mês atual";
    public const string PreviousMonthAction = "Mês anterior";
    public const string NextMonthAction = "Mês seguinte";

    public const string DefaultCaption = "Meta padrão";
    public const string DefaultExplanation =
        "Usada nos meses sem meta específica.";
    public const string MonthlyCaptionPrefix = "Meta específica";
    public const string MonthlyExplanation =
        "Substitui a meta padrão somente neste mês.";

    public static LoadFailureDecision ResolveLoadFailure(bool hasValidSnapshot) =>
        hasValidSnapshot
            ? new LoadFailureDecision(true, RefreshKeepDataMessage)
            : new LoadFailureDecision(false, LoadErrorMessage);

    public static string FormatCompetenceTitle(CommercialCompetence competence)
    {
        var text = CommercialGoalPresentation.FormatCompetence(competence);
        if (text.Length == 0)
            return text;
        return char.ToUpper(text[0], ProductPriceHelper.Br) + text[1..];
    }

    public static string MonthlyCaption(CommercialCompetence competence) =>
        $"{MonthlyCaptionPrefix} — {FormatCompetenceTitle(competence)}";

    public static (string Bg, string Fg, string Accent) ToneColors(
        CommercialGoalPresentationTone tone) =>
        tone switch
        {
            CommercialGoalPresentationTone.Positive => ("#ECFDF5", "#065F46", "#0F766E"),
            CommercialGoalPresentationTone.Attention => ("#FFFBEB", "#92400E", "#B45309"),
            CommercialGoalPresentationTone.Warning => ("#FEF3C7", "#92400E", "#B45309"),
            CommercialGoalPresentationTone.Unavailable => ("#F1F5F9", "#475569", "#64748B"),
            _ => ("#F8FAFC", "#1E293B", "#334155"),
        };

    public static bool ShowCallout(CommercialGoalPresentationSnapshot presented) =>
        presented.Headline.Length > 0
        && (presented.GoalSource is CommercialGoalSettingSource.None
            or CommercialGoalSettingSource.InvalidDefault
            or CommercialGoalSettingSource.InvalidMonthlyOverride
            || presented.StatusTone == CommercialGoalPresentationTone.Unavailable
            || presented.Headline == CommercialGoalPresentation.HeadlineUnavailable);

    public static bool ShowEstimatedBanner(CommercialGoalPresentationSnapshot presented) =>
        presented.ShowEstimatedBadge;
}

/// <summary>Hierarquia visual B6: herói + decisão + contexto. Textos vêm da B5.</summary>
public sealed class CommercialGoalKpiLayout
{
    public required CommercialGoalMetricPresentation Hero { get; init; }
    public IReadOnlyList<CommercialGoalMetricPresentation> Decision { get; init; } = [];
    public IReadOnlyList<CommercialGoalMetricPresentation> Context { get; init; } = [];

    public static CommercialGoalKpiLayout From(CommercialGoalPresentationSnapshot presented) =>
        new()
        {
            Hero = presented.Realized,
            Decision = [presented.Goal, presented.Remaining, presented.RequiredPace],
            Context = [presented.Achievement, presented.LinearProjection, presented.Status],
        };
}
