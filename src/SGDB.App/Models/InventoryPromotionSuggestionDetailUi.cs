namespace SGDB.Models;

/// <summary>
/// Textos da seção AÇÃO COMERCIAL no detalhe. Sem I/O, sem recálculo B4/B5B.
/// Possibilidades apontam para os cards B4 já exibidos — sem duplicar preço/margem.
/// </summary>
public static class InventoryPromotionSuggestionDetailUi
{
    public const string Heading = "AÇÃO COMERCIAL";
    public const string StatusCaption = "Situação";
    public const string SuggestionCaption = "Sugestão";
    public const string ReasonCaption = "Motivo";
    public const string ExplanationCaption = "Explicação";
    public const string SecondaryCaption = "Outros sinais";
    public const string ObjectiveCaption = "Objetivo";
    public const string PriorityCaption = "Prioridade";
    public const string ConfidenceCaption = "Confiança";
    public const string QuantityCaption = "Quantidade em atenção";
    public const string SourceCaption = "Origem";
    public const string PossibilitiesCaption = "Possibilidades";
    public const string SeeCommercialAbove = "ver cenário comercial acima";
    public const string WarningsCaption = "ATENÇÃO";

    public static IReadOnlyList<string> PossibilityLines(
        InventoryPromotionSuggestionPresentationRow? row)
    {
        row ??= InventoryPromotionSuggestionPresentation.MissingRow();
        if (!row.IsSuggested)
            return [];

        var options = row.ScenarioOptions ?? [];
        if (options.Count == 0)
            return [];

        var lines = new List<string>(options.Count);
        foreach (var option in options)
            lines.Add($"• {option.KindLabel} — {SeeCommercialAbove}");
        return lines;
    }

    public static bool ShowPossibilities(InventoryPromotionSuggestionPresentationRow? row) =>
        PossibilityLines(row).Count > 0;

    public static bool ShowQuantity(InventoryPromotionSuggestionPresentationRow? row)
    {
        row ??= InventoryPromotionSuggestionPresentation.MissingRow();
        return row.AttentionQuantityText != InventoryProjectionPresentation.EmDash;
    }

    public static bool ShowSecondary(InventoryPromotionSuggestionPresentationRow? row) =>
        (row?.SecondaryReasonLabels?.Count ?? 0) > 0;

    public static bool ShowWarnings(InventoryPromotionSuggestionPresentationRow? row) =>
        (row?.WarningLabels?.Count ?? 0) > 0;
}
