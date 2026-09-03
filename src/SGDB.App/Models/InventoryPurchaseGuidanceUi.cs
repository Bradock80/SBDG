namespace SGDB.Models;

/// <summary>Card operacional 70G-B4. All = resultados úteis, sem NotApplicable.</summary>
public enum InventoryPurchaseGuidanceCardKind
{
    All = 0,
    ConsiderReplenishment,
    DoNotReplenishNow,
    Monitor,
    ReviewData,
}

/// <summary>Filtros em memória. Nenhum campo dispara consulta.</summary>
public sealed class InventoryPurchaseGuidanceUiFilter
{
    public InventoryPurchaseGuidanceCardKind Card { get; set; } = InventoryPurchaseGuidanceCardKind.All;
    public InventoryCoverageBand? CoverageBand { get; set; }
    public string Search { get; set; } = "";

    public static InventoryPurchaseGuidanceUiFilter Cleared() => new();
}

public sealed class InventoryPurchaseGuidanceCardCounts
{
    public int All { get; init; }
    public int ConsiderReplenishment { get; init; }
    public int DoNotReplenishNow { get; init; }
    public int Monitor { get; init; }
    public int ReviewData { get; init; }
    public int NotApplicable { get; init; }

    public int Of(InventoryPurchaseGuidanceCardKind kind) => kind switch
    {
        InventoryPurchaseGuidanceCardKind.All => All,
        InventoryPurchaseGuidanceCardKind.ConsiderReplenishment => ConsiderReplenishment,
        InventoryPurchaseGuidanceCardKind.DoNotReplenishNow => DoNotReplenishNow,
        InventoryPurchaseGuidanceCardKind.Monitor => Monitor,
        InventoryPurchaseGuidanceCardKind.ReviewData => ReviewData,
        _ => All,
    };
}

/// <summary>Linha da grade 70G-B4. Textos vêm da B3; nome/código da 70C.</summary>
public sealed class InventoryPurchaseGuidanceGridRow
{
    public required InventoryPurchaseGuidancePresentationRow Guidance { get; init; }
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public InventoryCoverageBand CoverageBand { get; init; }

    public int ProductId => Guidance.ProductId;
    public string ActionLabel => Guidance.ActionLabel;
    public string PrimaryReasonLabel => Guidance.PrimaryReasonLabel;
    public string ConfidenceLabel => Guidance.ConfidenceLabel;
    public string TotalStockDisplay => Guidance.TotalStockDisplay;
    public string Vmv30Display => Guidance.Vmv30Display;
    public string CoverageDisplay => Guidance.CoverageDisplay;
    public string ValidityLabel => Guidance.ValidityLabel;
    public InventoryPurchaseGuidanceAction Action => Guidance.Action;
    public InventoryPurchaseGuidanceStatus Status => Guidance.Status;
    public string Tone => ToneOf(Guidance.Action);
    public string ShortExplanation => Guidance.ShortExplanation;
    public string DetailExplanation => Guidance.DetailExplanation;

    public static string ToneOf(InventoryPurchaseGuidanceAction action) =>
        action switch
        {
            InventoryPurchaseGuidanceAction.ConsiderReplenishment => "attention",
            InventoryPurchaseGuidanceAction.DoNotReplenishNow => "info",
            InventoryPurchaseGuidanceAction.Monitor => "notice",
            InventoryPurchaseGuidanceAction.ReviewData => "alert",
            _ => "",
        };
}

/// <summary>
/// Filtro, contagens e grade 70G-B4. Sem I/O, SQL, quantidade ou recálculo B1.
/// </summary>
public static class InventoryPurchaseGuidanceUi
{
    public const string ModuleId = "reposicao_inteligente";
    public const int ExpectedQueryCount = 0;

    public const string EmptySnapshotMessage = "Nenhum produto disponível para análise.";
    public const string EmptyFilterMessage = "Nenhum produto encontrado para este filtro.";
    public const string LoadErrorMessage = "Não foi possível carregar a Reposição Inteligente.";
    public const string RefreshKeepDataMessage =
        "Não foi possível atualizar a Reposição Inteligente. Os últimos dados carregados foram mantidos.";
    public const string SelectRowHint = "Selecione uma linha para ver o detalhe.";

    public static readonly (InventoryPurchaseGuidanceCardKind Kind, string Title, string Bg, string Fg)[] Cards =
    [
        (InventoryPurchaseGuidanceCardKind.All, "Todos", "#E2E8F0", "#334155"),
        (InventoryPurchaseGuidanceCardKind.ConsiderReplenishment,
            InventoryPurchaseGuidancePresentation.CardConsiderReplenishment, "#FEF3C7", "#92400E"),
        (InventoryPurchaseGuidanceCardKind.DoNotReplenishNow,
            InventoryPurchaseGuidancePresentation.CardDoNotReplenishNow, "#E0F2FE", "#075985"),
        (InventoryPurchaseGuidanceCardKind.Monitor,
            InventoryPurchaseGuidancePresentation.CardMonitor, "#FEF9C3", "#854D0E"),
        (InventoryPurchaseGuidanceCardKind.ReviewData,
            InventoryPurchaseGuidancePresentation.CardReviewData, "#FEE2E2", "#991B1B"),
    ];

    public static readonly (InventoryCoverageBand? Band, string Title)[] CoverageOptions =
    [
        (null, "Todas"),
        (InventoryCoverageBand.Critical, "Crítica"),
        (InventoryCoverageBand.Low, "Baixa"),
        (InventoryCoverageBand.Attention, "Atenção"),
        (InventoryCoverageBand.Normal, "Normal"),
        (InventoryCoverageBand.NotCalculable, "Não calculável"),
    ];

    public static bool IsOperational(InventoryPurchaseGuidancePresentationRow row) =>
        row.Status != InventoryPurchaseGuidanceStatus.NotApplicable
        && row.Action != InventoryPurchaseGuidanceAction.None;

    public static InventoryPurchaseGuidanceCardCounts CountCards(
        IReadOnlyList<InventoryPurchaseGuidancePresentationRow> rows)
    {
        var consider = 0;
        var doNot = 0;
        var monitor = 0;
        var review = 0;
        var notApplicable = 0;
        foreach (var row in rows)
        {
            if (!IsOperational(row))
            {
                notApplicable++;
                continue;
            }

            switch (row.Action)
            {
                case InventoryPurchaseGuidanceAction.ConsiderReplenishment:
                    consider++;
                    break;
                case InventoryPurchaseGuidanceAction.DoNotReplenishNow:
                    doNot++;
                    break;
                case InventoryPurchaseGuidanceAction.Monitor:
                    monitor++;
                    break;
                case InventoryPurchaseGuidanceAction.ReviewData:
                    review++;
                    break;
            }
        }

        return new InventoryPurchaseGuidanceCardCounts
        {
            All = consider + doNot + monitor + review,
            ConsiderReplenishment = consider,
            DoNotReplenishNow = doNot,
            Monitor = monitor,
            ReviewData = review,
            NotApplicable = notApplicable,
        };
    }

    public static bool MatchesCard(
        InventoryPurchaseGuidancePresentationRow row,
        InventoryPurchaseGuidanceCardKind card) =>
        card switch
        {
            InventoryPurchaseGuidanceCardKind.All => IsOperational(row),
            InventoryPurchaseGuidanceCardKind.ConsiderReplenishment =>
                row.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryPurchaseGuidanceCardKind.DoNotReplenishNow =>
                row.Action == InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryPurchaseGuidanceCardKind.Monitor =>
                row.Action == InventoryPurchaseGuidanceAction.Monitor,
            InventoryPurchaseGuidanceCardKind.ReviewData =>
                row.Action == InventoryPurchaseGuidanceAction.ReviewData,
            _ => IsOperational(row),
        };

    public static bool Matches(
        InventoryPurchaseGuidanceGridRow row,
        InventoryPurchaseGuidanceUiFilter filter)
    {
        if (!MatchesCard(row.Guidance, filter.Card))
            return false;

        if (filter.CoverageBand is InventoryCoverageBand band && row.CoverageBand != band)
            return false;

        var search = (filter.Search ?? "").Trim();
        if (search.Length > 0)
        {
            if (!row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !row.Code.Contains(search, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static IReadOnlyList<InventoryPurchaseGuidanceGridRow> Apply(
        InventoryPurchaseGuidancePresentationSnapshot? presented,
        IReadOnlyList<ProductTurnoverRow>? rows,
        InventoryPurchaseGuidanceUiFilter? filter)
    {
        presented ??= new InventoryPurchaseGuidancePresentationSnapshot();
        filter ??= InventoryPurchaseGuidanceUiFilter.Cleared();
        var turnover = IndexTurnover(rows);
        var list = new List<InventoryPurchaseGuidanceGridRow>();
        foreach (var guidance in presented.Rows ?? [])
        {
            turnover.TryGetValue(guidance.ProductId, out var product);
            var row = ToGridRow(guidance, product);
            if (Matches(row, filter))
                list.Add(row);
        }

        return list;
    }

    public static InventoryPurchaseGuidanceGridRow ToGridRow(
        InventoryPurchaseGuidancePresentationRow guidance,
        ProductTurnoverRow? turnover) =>
        new()
        {
            Guidance = guidance,
            Name = turnover?.Name ?? "",
            Code = turnover?.Code ?? "",
            CoverageBand = turnover?.CoverageBand ?? InventoryCoverageBand.NotCalculable,
        };

    public static string EmptyStateMessage(int snapshotCount, int filteredCount, string? loadError)
    {
        if (!string.IsNullOrWhiteSpace(loadError))
            return loadError;
        if (snapshotCount <= 0)
            return EmptySnapshotMessage;
        if (filteredCount <= 0)
            return EmptyFilterMessage;
        return "";
    }

    public static LoadFailureDecision ResolveLoadFailure(bool hasValidSnapshot) =>
        hasValidSnapshot
            ? new LoadFailureDecision(true, RefreshKeepDataMessage)
            : new LoadFailureDecision(false, LoadErrorMessage);

    static Dictionary<int, ProductTurnoverRow> IndexTurnover(IReadOnlyList<ProductTurnoverRow>? rows)
    {
        var map = new Dictionary<int, ProductTurnoverRow>();
        foreach (var row in rows ?? [])
            map.TryAdd(row.ProductId, row);
        return map;
    }
}
