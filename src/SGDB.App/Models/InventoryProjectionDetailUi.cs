namespace SGDB.Models;

/// <summary>
/// Textos e composição visual do detalhe B5. Sem I/O, sem recálculo, sem valor 30d.
/// </summary>
public static class InventoryProjectionDetailUi
{
    public const string Heading = "Detalhe da projeção";
    public const string VmvTooltip = "Venda média diária observada nos últimos 30 dias.";
    public const string MissingSelectionMessage = "Selecione um produto para detalhar a projeção.";
    public const string UnavailableDetailMessage = "Não foi possível detalhar a projeção deste produto.";

    public const string RecordedValueExplanation =
        "Valor estimado da sobra até a validade, com custo lançado no lote.";
    public const string EstimatedValueExplanation =
        "Valor estimado com custo médio atual do cadastro — não é o custo lançado no lote.";
    public const string PartialValueExplanation =
        "Soma somente dos lotes com custo disponível; os demais não entram no total.";
    public const string UnavailableValueExplanation =
        "Sem custo confiável para estimar valor.";

    public static string EmptyLotsMessage(InventoryProjectedProductPresentation projection)
    {
        projection ??= new InventoryProjectedProductPresentation();
        if (projection.Lots.Count > 0)
            return "";

        if (projection.ValidityStatus == InventoryProjectionValidityStatus.InvalidExpiry)
            return string.IsNullOrEmpty(projection.ExpiryBlockedExplanation)
                ? InventoryProjectionPresentation.ValidityInvalidLabel
                : projection.ExpiryBlockedExplanation;

        if (projection.ValidityStatus == InventoryProjectionValidityStatus.NoLot)
            return InventoryProjectionPresentation.ValidityNoLotLabel;

        if (!string.IsNullOrWhiteSpace(projection.ExpiryBlockedExplanation))
            return projection.ExpiryBlockedExplanation;

        return string.IsNullOrWhiteSpace(projection.ValidityRiskDisplay)
            ? InventoryProjectionPresentation.ValidityNoLotLabel
            : projection.ValidityRiskDisplay;
    }

    public static string SurplusValueExplanation(InventoryProjectedProductPresentation projection)
    {
        projection ??= new InventoryProjectedProductPresentation();
        if (projection.SurplusValueQuality == InventoryProjectionSurplusValueQuality.Unavailable
            || projection.ProjectedExpirySurplusValue is not double value
            || !double.IsFinite(value))
            return UnavailableValueExplanation;

        return projection.SurplusValueQuality switch
        {
            InventoryProjectionSurplusValueQuality.CompleteRecorded => RecordedValueExplanation,
            InventoryProjectionSurplusValueQuality.CompleteWithEstimate => EstimatedValueExplanation,
            InventoryProjectionSurplusValueQuality.Partial => PartialValueExplanation,
            _ => UnavailableValueExplanation,
        };
    }

    public static IReadOnlyList<string> ObservationLines(InventoryProjectedProductPresentation projection)
    {
        projection ??= new InventoryProjectedProductPresentation();
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (projection.Lots.Count > 0)
            Add(lines, seen, projection.ExpiryBlockedExplanation);

        foreach (var alert in projection.Alerts)
        {
            if (alert == projection.FridgeLimitationAlert)
                continue;
            if (alert == projection.UntrackedWarehouseAlert)
                continue;
            if (alert == projection.SkuBlockedShortText)
                continue;
            if (alert == projection.ExpiryBlockedShortText)
                continue;
            Add(lines, seen, alert);
        }

        return lines;
    }

    static void Add(List<string> lines, HashSet<string> seen, string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0)
            return;
        if (seen.Add(value))
            lines.Add(value);
    }
}
