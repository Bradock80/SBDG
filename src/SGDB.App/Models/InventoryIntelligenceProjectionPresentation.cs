namespace SGDB.Models;

/// <summary>
/// Linha composta 70C + 70D para a grade. Não recalcula giro nem projeção.
/// </summary>
public sealed class InventoryIntelligenceProjectionGridRow
{
    public required InventoryIntelligenceGridRow Intelligence { get; init; }
    public InventoryProjectedProductPresentation? Projection { get; init; }

    public int ProductId => Intelligence.ProductId;
    public string Name => Intelligence.Name;
    public string Code => Intelligence.Code;
    public string StockDisplay => Intelligence.StockDisplay;
    public string StockFridgeDisplay => Intelligence.StockFridgeDisplay;
    public string TotalStockDisplay => Intelligence.TotalStockDisplay;
    public string Vmv30Display => Intelligence.Vmv30Display;
    public string CoverageDisplay => Intelligence.CoverageDisplay;
    public string LastSaleDisplay => Intelligence.LastSaleDisplay;
    public string DaysWithoutSaleDisplay => Intelligence.DaysWithoutSaleDisplay;
    public string SituationDisplay => Intelligence.SituationDisplay;
    public string AlertDisplay => Intelligence.AlertDisplay;
    public string HistoryDisplay => Intelligence.HistoryDisplay;
    public string Tone => Intelligence.Tone;

    public string Surplus30Display =>
        Projection?.Surplus30Display ?? InventoryProjectionPresentation.EmDash;

    public string ExcessStatusDisplay =>
        Projection?.ExcessStatusDisplay ?? InventoryProjectionPresentation.ExcessUnavailableLabel;

    public InventoryProjectionExcessStatus ExcessStatus =>
        Projection?.ExcessStatus ?? InventoryProjectionExcessStatus.Unavailable;

    public double? ProjectedExcessQuantity => Projection?.ProjectedExcessQuantity;

    public string ValidityRiskDisplay =>
        Projection?.ValidityRiskDisplay ?? InventoryProjectionPresentation.ValidityUnavailableLabel;

    public InventoryProjectionValidityStatus ValidityStatus =>
        Projection?.ValidityStatus ?? InventoryProjectionValidityStatus.ProjectionUnavailable;
}

/// <summary>
/// Combina grade 70C com apresentação 70D por ProductId. Sem I/O, sem recálculo.
/// Autoridade da lista: rows 70C já filtradas.
/// </summary>
public static class InventoryIntelligenceProjectionPresentation
{
    public static IReadOnlyList<InventoryIntelligenceProjectionGridRow> Apply(
        IReadOnlyList<ProductTurnoverRow> rows,
        InventoryIntelligenceUiFilter filter,
        InventoryProjectionPresentationSnapshot projection)
    {
        var giro = InventoryIntelligencePresentation.Apply(rows, filter);
        return Combine(giro, projection);
    }

    public static IReadOnlyList<InventoryIntelligenceProjectionGridRow> Combine(
        IReadOnlyList<InventoryIntelligenceGridRow> giroRows,
        InventoryProjectionPresentationSnapshot? projection)
    {
        projection ??= new InventoryProjectionPresentationSnapshot();
        IReadOnlyDictionary<int, InventoryProjectedProductPresentation> lookup;
        HashSet<int>? conflicts = null;

        if (projection.ByProductId.Count > 0)
            lookup = projection.ByProductId;
        else
            lookup = IndexProjections(projection.Products, out conflicts);

        return Combine(giroRows, lookup, conflicts);
    }

    public static IReadOnlyList<InventoryIntelligenceProjectionGridRow> Combine(
        IReadOnlyList<InventoryIntelligenceGridRow> giroRows,
        IReadOnlyList<InventoryProjectedProductPresentation> projections)
    {
        var lookup = IndexProjections(projections, out var conflicts);
        return Combine(giroRows, lookup, conflicts);
    }

    static IReadOnlyList<InventoryIntelligenceProjectionGridRow> Combine(
        IReadOnlyList<InventoryIntelligenceGridRow> giroRows,
        IReadOnlyDictionary<int, InventoryProjectedProductPresentation> lookup,
        HashSet<int>? conflicts)
    {
        giroRows ??= [];
        var list = new List<InventoryIntelligenceProjectionGridRow>(giroRows.Count);
        foreach (var giro in giroRows)
        {
            InventoryProjectedProductPresentation? projection = null;
            if (conflicts is null || !conflicts.Contains(giro.ProductId))
                lookup.TryGetValue(giro.ProductId, out projection);

            list.Add(new InventoryIntelligenceProjectionGridRow
            {
                Intelligence = giro,
                Projection = projection,
            });
        }

        return list;
    }

    /// <summary>
    /// Primeiro ganha; ProductId repetido na lista vira conflito (70D indisponível).
    /// Evita escolher um valor financeiro entre duas projeções inconsistentes.
    /// </summary>
    static Dictionary<int, InventoryProjectedProductPresentation> IndexProjections(
        IReadOnlyList<InventoryProjectedProductPresentation>? projections,
        out HashSet<int> conflicts)
    {
        var map = new Dictionary<int, InventoryProjectedProductPresentation>();
        conflicts = [];
        if (projections is null)
            return map;

        foreach (var item in projections)
        {
            if (conflicts.Contains(item.ProductId))
                continue;
            if (map.TryAdd(item.ProductId, item))
                continue;
            map.Remove(item.ProductId);
            conflicts.Add(item.ProductId);
        }

        return map;
    }
}
