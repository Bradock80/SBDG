namespace SGDB.Models;

/// <summary>
/// Textos da seção ORIENTAÇÃO DE REPOSIÇÃO no detalhe. Sem I/O, sem recálculo B1.
/// Exibe a Presentation B3 já composta — não decide Action.
/// </summary>
public static class InventoryPurchaseGuidanceDetailUi
{
    public const string Heading = "ORIENTAÇÃO DE REPOSIÇÃO";
    public const string ActionCaption = "Orientação";
    public const string ReasonCaption = "Motivo";
    public const string ConfidenceCaption = "Confiança";
    public const string ExplanationCaption = "Explicação";
    public const string SecondaryCaption = "Fatores adicionais";
    public const int ExpectedQueryCount = 0;

    public static InventoryPurchaseGuidancePresentationRow Row(
        InventoryPurchaseGuidancePresentationRow? row) =>
        row ?? InventoryPurchaseGuidancePresentation.MissingRow();

    public static string Explanation(InventoryPurchaseGuidancePresentationRow? row) =>
        Row(row).DetailExplanation;

    public static bool ShowSecondary(InventoryPurchaseGuidancePresentationRow? row) =>
        (row?.SecondaryReasonLabels?.Count ?? 0) > 0;

    public static bool ShowConsiderNote(InventoryPurchaseGuidancePresentationRow? row)
    {
        var value = Row(row);
        return value.IsConsiderReplenishment
            && !string.IsNullOrWhiteSpace(value.ConsiderLimitationNote);
    }
}
