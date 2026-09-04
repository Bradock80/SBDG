using SGDB.Utils;

namespace SGDB.Models;

/// <summary>Cards resumo 71A-B7. Contagens vêm do snapshot apresentado; nenhum score novo.</summary>
public enum InventoryComboUiCardKind
{
    Combinations = 0,
    NeedTurnover,
    WithSuggestions,
    WithoutSafeCombination,
}

/// <summary>Filtro de situação. Não altera o snapshot B5/B6.</summary>
public enum InventoryComboUiStatusFilter
{
    All = 0,
    WithSuggestions,
    WithoutSafeCombination,
}

/// <summary>Filtro de motivo do alvo. Textos vêm de B6.</summary>
public enum InventoryComboUiReasonFilter
{
    All = 0,
    ExpirySurplus,
    ProjectedExcess,
    Idle,
}

/// <summary>Filtros em memória. Nenhum campo dispara consulta.</summary>
public sealed class InventoryComboUiFilter
{
    public InventoryComboUiStatusFilter Status { get; set; } = InventoryComboUiStatusFilter.All;
    public InventoryComboUiReasonFilter Reason { get; set; } = InventoryComboUiReasonFilter.All;
    public string Search { get; set; } = "";

    public static InventoryComboUiFilter Cleared() => new();
}

public sealed class InventoryComboCardCounts
{
    public int Combinations { get; init; }
    public int NeedTurnover { get; init; }
    public int WithSuggestions { get; init; }
    public int WithoutSafeCombination { get; init; }

    public int Of(InventoryComboUiCardKind kind) => kind switch
    {
        InventoryComboUiCardKind.Combinations => Combinations,
        InventoryComboUiCardKind.NeedTurnover => NeedTurnover,
        InventoryComboUiCardKind.WithSuggestions => WithSuggestions,
        InventoryComboUiCardKind.WithoutSafeCombination => WithoutSafeCombination,
        _ => NeedTurnover,
    };
}

/// <summary>
/// Linha da grade 71A-B7. Textos B6; ordem = snapshot B5/B4.
/// Combinações 0 permanece visível.
/// </summary>
public sealed class InventoryComboTargetGridRow
{
    public required InventoryComboTargetPresentationGroup Target { get; init; }

    public int ProductId => Target.ProductId;
    public string Name => Target.Name;
    public string Code => Target.Code;
    public string ProductTitle => Target.TargetTitle;
    public string ReasonText => Target.TargetReasonText;
    public string StockText => Target.TargetStockText;
    public int SuggestionCount => Target.SuggestionCount;
    public string CombinationsText =>
        Target.SuggestionCount.ToString("0", ProductPriceHelper.Br);
    public string CombinationsStatusText =>
        Target.SuggestionCount == 0
            ? InventoryComboPresentation.EmptyTargetMessage
            : Target.SuggestionCountText;
    public string ConfidenceText => Target.ConfidenceText;
    public string EmptyMessage => Target.EmptyMessage;
    public ComboTargetEligibilityReason Reason => Target.Reason;
    public IReadOnlyList<InventoryComboSuggestionPresentationRow> Suggestions => Target.Suggestions;
}

/// <summary>
/// Filtro, contagens e grade 71A-B7. Sem I/O, SQL, recálculo B1–B6 ou reordenação de sugestões.
/// Lista de alvos preserva a ordem do snapshot B5.
/// </summary>
public static class InventoryComboIntelligenceUi
{
    public const string ModuleId = "combos_inteligentes";
    public const int ExpectedQueryCount = 0;

    public const string ModuleTitle = "Combos Inteligentes";
    public const string ToolbarTitle = "Combos";
    public const string Subtitle =
        "Sugestões para ajudar produtos que precisam girar, preservando estoque e margem.";

    public const string EmptyFilterMessage = "Nenhum produto encontrado para este filtro.";
    public const string LoadErrorMessage = "Não foi possível carregar os Combos Inteligentes.";
    public const string RefreshKeepDataMessage =
        "Não foi possível atualizar os Combos Inteligentes. Os últimos dados carregados foram mantidos.";
    public const string SelectRowHint = "Selecione um produto-alvo para ver as combinações.";
    public const string ZeroSuggestionStatus = "Nenhuma combinação segura";

    public static readonly (InventoryComboUiCardKind Kind, string Title, string Bg, string Fg)[] Cards =
    [
        (InventoryComboUiCardKind.NeedTurnover, "Produtos com necessidade de giro", "#E2E8F0", "#334155"),
        (InventoryComboUiCardKind.WithSuggestions, "Com combinações sugeridas", "#E0F2FE", "#075985"),
        (InventoryComboUiCardKind.WithoutSafeCombination, "Sem combinação segura", "#FEF9C3", "#854D0E"),
        (InventoryComboUiCardKind.Combinations, "Combinações sugeridas", "#F1F5F9", "#334155"),
    ];

    public static readonly (InventoryComboUiStatusFilter Status, string Title)[] StatusOptions =
    [
        (InventoryComboUiStatusFilter.All, "Todos"),
        (InventoryComboUiStatusFilter.WithSuggestions, "Com sugestões"),
        (InventoryComboUiStatusFilter.WithoutSafeCombination, "Sem combinação segura"),
    ];

    public static readonly (InventoryComboUiReasonFilter Reason, string Title)[] ReasonOptions =
    [
        (InventoryComboUiReasonFilter.All, "Todos os motivos"),
        (InventoryComboUiReasonFilter.ExpirySurplus, "Sobra antes da validade"),
        (InventoryComboUiReasonFilter.ProjectedExcess, "Excesso projetado"),
        (InventoryComboUiReasonFilter.Idle, "Produto parado"),
    ];

    public static InventoryComboUiStatusFilter StatusOf(InventoryComboUiCardKind kind) =>
        kind switch
        {
            InventoryComboUiCardKind.WithSuggestions => InventoryComboUiStatusFilter.WithSuggestions,
            InventoryComboUiCardKind.Combinations => InventoryComboUiStatusFilter.WithSuggestions,
            InventoryComboUiCardKind.WithoutSafeCombination =>
                InventoryComboUiStatusFilter.WithoutSafeCombination,
            _ => InventoryComboUiStatusFilter.All,
        };

    public static InventoryComboCardCounts CountCards(
        IReadOnlyList<InventoryComboTargetPresentationGroup>? targets)
    {
        var need = 0;
        var with = 0;
        var without = 0;
        var combinations = 0;
        foreach (var target in targets ?? [])
        {
            if (target is null)
                continue;
            need++;
            var count = target.SuggestionCount;
            combinations += count;
            if (count > 0)
                with++;
            else
                without++;
        }

        return new InventoryComboCardCounts
        {
            Combinations = combinations,
            NeedTurnover = need,
            WithSuggestions = with,
            WithoutSafeCombination = without,
        };
    }

    public static bool Matches(
        InventoryComboTargetGridRow row,
        InventoryComboUiFilter filter)
    {
        if (!MatchesStatus(row.SuggestionCount, filter.Status))
            return false;

        if (!MatchesReason(row.Reason, filter.Reason))
            return false;

        var search = (filter.Search ?? "").Trim();
        if (search.Length == 0)
            return true;

        if (Contains(row.Name, search)
            || Contains(row.Code, search)
            || Contains(row.ProductTitle, search))
            return true;

        foreach (var suggestion in row.Suggestions)
        {
            if (Contains(suggestion.AnchorTitle, search)
                || Contains(suggestion.AnchorProductId.ToString(), search))
                return true;
        }

        return false;
    }

    public static IReadOnlyList<InventoryComboTargetGridRow> Apply(
        InventoryComboPresentationSnapshot? presented,
        InventoryComboUiFilter? filter)
    {
        presented ??= new InventoryComboPresentationSnapshot();
        filter ??= InventoryComboUiFilter.Cleared();
        var list = new List<InventoryComboTargetGridRow>();
        foreach (var target in presented.Targets ?? [])
        {
            if (target is null)
                continue;
            var row = ToGridRow(target);
            if (Matches(row, filter))
                list.Add(row);
        }

        return list;
    }

    public static InventoryComboTargetGridRow ToGridRow(
        InventoryComboTargetPresentationGroup target) =>
        new() { Target = target };

    public static string EmptyStateMessage(
        int snapshotCount,
        int filteredCount,
        string? loadError)
    {
        if (!string.IsNullOrWhiteSpace(loadError))
            return loadError;
        if (snapshotCount <= 0)
            return InventoryComboPresentation.EmptySnapshotMessage;
        if (filteredCount <= 0)
            return EmptyFilterMessage;
        return "";
    }

    public static LoadFailureDecision ResolveLoadFailure(bool hasValidSnapshot) =>
        hasValidSnapshot
            ? new LoadFailureDecision(true, RefreshKeepDataMessage)
            : new LoadFailureDecision(false, LoadErrorMessage);

    public static string LimitationsDisplay(InventoryComboSuggestionPresentationRow suggestion)
    {
        var items = suggestion.LimitationsText ?? [];
        return items.Count == 0 ? "" : string.Join(" ", items);
    }

    static bool MatchesStatus(int suggestionCount, InventoryComboUiStatusFilter status) =>
        status switch
        {
            InventoryComboUiStatusFilter.WithSuggestions => suggestionCount > 0,
            InventoryComboUiStatusFilter.WithoutSafeCombination => suggestionCount == 0,
            _ => true,
        };

    static bool MatchesReason(
        ComboTargetEligibilityReason reason,
        InventoryComboUiReasonFilter filter) =>
        filter switch
        {
            InventoryComboUiReasonFilter.ExpirySurplus =>
                reason == ComboTargetEligibilityReason.ExpirySurplus,
            InventoryComboUiReasonFilter.ProjectedExcess =>
                reason == ComboTargetEligibilityReason.ProjectedExcess,
            InventoryComboUiReasonFilter.Idle =>
                reason == ComboTargetEligibilityReason.Idle,
            _ => true,
        };

    static bool Contains(string? value, string search) =>
        !string.IsNullOrEmpty(value)
        && value.Contains(search, StringComparison.OrdinalIgnoreCase);
}
