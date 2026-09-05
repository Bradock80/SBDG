using SGDB.Domain.Commercial;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Carga única 71B-B6: B4 compositor + B5 presentation. 0 SQL próprio.
/// </summary>
public static class CommercialGoalLoader
{
    public const int ExpectedQueryCount = 0;

    public static CommercialGoalPresentationSnapshot Load(
        CommercialCompetence competence,
        DateOnly referenceDate) =>
        CommercialGoalPresentation.Apply(
            CommercialGoalComposerService.Load(competence, referenceDate));
}
