namespace SGDB.Models;

/// <summary>
/// Resultado 71C-B4. Referências aos snapshots existentes; sem duplicar payload.
/// </summary>
public sealed class CentralDecisionLoadResult
{
    public required CentralDecisionSnapshot Decision { get; init; }
    public required CentralDecisionPresentationSnapshot Presentation { get; init; }

    public int QueryCount => Decision.QueryCount;
}
