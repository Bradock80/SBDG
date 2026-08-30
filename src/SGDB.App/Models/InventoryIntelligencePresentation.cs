using SGDB.Utils;

namespace SGDB.Models;

/// <summary>Card exclusivo da tela 70C-B3A. Não altera o motor.</summary>
public enum InventoryIntelligenceCardKind
{
    All = 0,
    ZeroStock,
    ZeroStockWithTurnover,
    Critical,
    Low,
    Idle,
    LocationAnomaly,
}

/// <summary>
/// Filtros combináveis em memória. Nenhum campo dispara consulta.
/// Card e CoverageBand são independentes: um não limpa o outro.
/// </summary>
public sealed class InventoryIntelligenceUiFilter
{
    public InventoryIntelligenceCardKind Card { get; set; } = InventoryIntelligenceCardKind.All;
    public InventoryCoverageBand? CoverageBand { get; set; }
    public string Search { get; set; } = "";
    public bool Silence30 { get; set; }
    public bool Silence60 { get; set; }
    public bool Silence90 { get; set; }
    public bool InsufficientHistory { get; set; }

    public static InventoryIntelligenceUiFilter Cleared() => new();
}

public sealed class InventoryIntelligenceCardCounts
{
    public int All { get; init; }
    public int ZeroStock { get; init; }
    public int ZeroStockWithTurnover { get; init; }
    public int Critical { get; init; }
    public int Low { get; init; }
    public int Idle { get; init; }
    public int LocationAnomaly { get; init; }

    public int Of(InventoryIntelligenceCardKind kind) => kind switch
    {
        InventoryIntelligenceCardKind.All => All,
        InventoryIntelligenceCardKind.ZeroStock => ZeroStock,
        InventoryIntelligenceCardKind.ZeroStockWithTurnover => ZeroStockWithTurnover,
        InventoryIntelligenceCardKind.Critical => Critical,
        InventoryIntelligenceCardKind.Low => Low,
        InventoryIntelligenceCardKind.Idle => Idle,
        InventoryIntelligenceCardKind.LocationAnomaly => LocationAnomaly,
        _ => All,
    };
}

/// <summary>Linha somente leitura do grid. Formatação de tela, não regra de estoque.</summary>
public sealed class InventoryIntelligenceGridRow
{
    public int ProductId { get; init; }
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";
    public string StockDisplay { get; init; } = "";
    public string StockFridgeDisplay { get; init; } = "";
    public string TotalStockDisplay { get; init; } = "";
    public string Vmv30Display { get; init; } = "";
    public string CoverageDisplay { get; init; } = "";
    public string LastSaleDisplay { get; init; } = "";
    public string DaysWithoutSaleDisplay { get; init; } = "";
    public string SituationDisplay { get; init; } = "";
    public string AlertDisplay { get; init; } = "";
    public string HistoryDisplay { get; init; } = "";
    public string Tone { get; init; } = "";
}

/// <summary>
/// Apresentação do Estoque Inteligente (70C-B3A). Filtro e rótulos em memória.
/// Não lê banco. Não chama Load/GetByProductId. Não altera VMV nem CoverageDays.
/// </summary>
public static class InventoryIntelligencePresentation
{
    public const string EmDash = "—";
    public const string EmptySnapshotMessage = "Nenhum produto disponível para análise.";
    public const string EmptyFilterMessage = "Nenhum produto encontrado para este filtro.";
    public const string LoadErrorMessage = "Não foi possível carregar o Estoque Inteligente.";
    public const string RefreshKeepDataMessage =
        "Não foi possível atualizar o Estoque Inteligente. Os últimos dados carregados foram mantidos.";
    public const string CoverageDisclaimer =
        "Cobertura = estoque total ÷ VMV 30. Os indicadores são informativos e não representam sugestão automática de compra.";
    public const string CompositionDisclaimer =
        "Produtos de composição podem ter sua movimentação física registrada nos componentes.";

    public static readonly (InventoryIntelligenceCardKind Kind, string Title, string Bg, string Fg)[] Cards =
    [
        (InventoryIntelligenceCardKind.All, "Todos", "#E2E8F0", "#334155"),
        (InventoryIntelligenceCardKind.ZeroStock, "Sem estoque", "#E0F2FE", "#075985"),
        (InventoryIntelligenceCardKind.ZeroStockWithTurnover, "Sem estoque + giro recente", "#FEE2E2", "#991B1B"),
        (InventoryIntelligenceCardKind.Critical, "Crítica ≤ 3 dias", "#7F1D1D", "White"),
        (InventoryIntelligenceCardKind.Low, "Baixa 3–7 dias", "#FEF3C7", "#92400E"),
        (InventoryIntelligenceCardKind.Idle, "Parados 90+", "#FEF9C3", "#854D0E"),
        (InventoryIntelligenceCardKind.LocationAnomaly, "Conferir estoque", "#FECACA", "#991B1B"),
    ];

    public static readonly (InventoryCoverageBand? Band, string Title)[] CoverageOptions =
    [
        (null, "Todas"),
        (InventoryCoverageBand.Negative, "Estoque negativo"),
        (InventoryCoverageBand.Zero, "Sem estoque"),
        (InventoryCoverageBand.Critical, "Crítica"),
        (InventoryCoverageBand.Low, "Baixa"),
        (InventoryCoverageBand.Attention, "Atenção"),
        (InventoryCoverageBand.Normal, "Normal"),
        (InventoryCoverageBand.NotCalculable, "Não calculável"),
    ];

    public static InventoryIntelligenceCardCounts CountCards(IReadOnlyList<ProductTurnoverRow> rows)
    {
        var all = rows.Count;
        var zero = 0;
        var zeroTurnover = 0;
        var critical = 0;
        var low = 0;
        var idle = 0;
        var anomaly = 0;
        foreach (var row in rows)
        {
            if (row.CoverageBand == InventoryCoverageBand.Zero) zero++;
            if (row.IsZeroStockWithTurnover) zeroTurnover++;
            if (row.CoverageBand == InventoryCoverageBand.Critical) critical++;
            if (row.CoverageBand == InventoryCoverageBand.Low) low++;
            if (row.IsIdle) idle++;
            if (row.HasLocationStockAnomaly) anomaly++;
        }

        return new InventoryIntelligenceCardCounts
        {
            All = all,
            ZeroStock = zero,
            ZeroStockWithTurnover = zeroTurnover,
            Critical = critical,
            Low = low,
            Idle = idle,
            LocationAnomaly = anomaly,
        };
    }

    public static bool MatchesCard(ProductTurnoverRow row, InventoryIntelligenceCardKind card) =>
        card switch
        {
            InventoryIntelligenceCardKind.All => true,
            InventoryIntelligenceCardKind.ZeroStock => row.CoverageBand == InventoryCoverageBand.Zero,
            InventoryIntelligenceCardKind.ZeroStockWithTurnover => row.IsZeroStockWithTurnover,
            InventoryIntelligenceCardKind.Critical => row.CoverageBand == InventoryCoverageBand.Critical,
            InventoryIntelligenceCardKind.Low => row.CoverageBand == InventoryCoverageBand.Low,
            InventoryIntelligenceCardKind.Idle => row.IsIdle,
            InventoryIntelligenceCardKind.LocationAnomaly => row.HasLocationStockAnomaly,
            _ => true,
        };

    public static bool Matches(ProductTurnoverRow row, InventoryIntelligenceUiFilter filter)
    {
        if (!MatchesCard(row, filter.Card))
            return false;

        if (filter.CoverageBand is InventoryCoverageBand band && row.CoverageBand != band)
            return false;

        var search = (filter.Search ?? "").Trim();
        if (search.Length > 0)
        {
            var name = row.Name ?? "";
            var code = row.Code ?? "";
            if (!name.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !code.Contains(search, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (filter.Silence30 && !row.QualifiesSilence30)
            return false;
        if (filter.Silence60 && !row.QualifiesSilence60)
            return false;
        if (filter.Silence90 && !row.QualifiesSilence90)
            return false;
        if (filter.InsufficientHistory && !row.IsHistoryInsufficient30)
            return false;

        return true;
    }

    public static IReadOnlyList<InventoryIntelligenceGridRow> Apply(
        IReadOnlyList<ProductTurnoverRow> rows,
        InventoryIntelligenceUiFilter filter)
    {
        var list = new List<InventoryIntelligenceGridRow>();
        foreach (var row in rows)
        {
            if (Matches(row, filter))
                list.Add(ToGridRow(row));
        }

        return list;
    }

    public static InventoryIntelligenceGridRow ToGridRow(ProductTurnoverRow row) =>
        new()
        {
            ProductId = row.ProductId,
            Name = row.Name ?? "",
            Code = row.Code ?? "",
            StockDisplay = FormatQty(row.Stock),
            StockFridgeDisplay = FormatQty(row.StockFridge),
            TotalStockDisplay = FormatQty(row.TotalStock),
            Vmv30Display = FormatVmv30(row.Vmv30),
            CoverageDisplay = FormatCoverageDays(row.CoverageDays),
            LastSaleDisplay = FormatLastSale(row.LastValidSaleDate),
            DaysWithoutSaleDisplay = FormatDaysWithoutSale(row.DaysWithoutSale),
            SituationDisplay = SituationText(row),
            AlertDisplay = AlertText(row),
            HistoryDisplay = FormatHistory(row.HistoryDays),
            Tone = SituationTone(row),
        };

    public static string SituationText(ProductTurnoverRow row) =>
        row.CoverageBand switch
        {
            InventoryCoverageBand.Negative => "Estoque negativo — conferir",
            InventoryCoverageBand.Zero when row.IsZeroStockWithTurnover => "Sem estoque — há giro recente",
            InventoryCoverageBand.Zero => "Sem estoque",
            InventoryCoverageBand.Critical => "Cobertura crítica",
            InventoryCoverageBand.Low => "Cobertura baixa",
            InventoryCoverageBand.Attention => "Atenção à cobertura",
            InventoryCoverageBand.Normal => "Cobertura normal",
            InventoryCoverageBand.NotCalculable => "Cobertura não calculável",
            _ => "Cobertura não calculável",
        };

    /// <summary>
    /// Alerta ortogonal à faixa de cobertura. Prioridade: anomalia local &gt; parado &gt; giro recente.
    /// </summary>
    public static string AlertText(ProductTurnoverRow row)
    {
        if (row.HasLocationStockAnomaly)
            return "Conferir estoque por local";
        if (row.IsIdle)
            return "Sem venda há 90+ dias";
        if (row.IsZeroStockWithTurnover)
            return "Há giro recente";
        return EmDash;
    }

    public static string SituationTone(ProductTurnoverRow row) =>
        row.CoverageBand switch
        {
            InventoryCoverageBand.Negative => "expired",
            InventoryCoverageBand.Critical => "alert",
            InventoryCoverageBand.Zero when row.IsZeroStockWithTurnover => "alert",
            InventoryCoverageBand.Zero => "attention",
            InventoryCoverageBand.Low => "attention",
            InventoryCoverageBand.Attention => "notice",
            InventoryCoverageBand.Normal => "info",
            _ => "",
        };

    public static string FormatQty(double qty) => ProductLotListRow.FormatQty(qty);

    public static string FormatVmv30(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            return "0";
        if (Math.Abs(value - Math.Round(value)) < 0.0001)
            return Math.Round(value).ToString("N0", ProductPriceHelper.Br);
        return value.ToString("N2", ProductPriceHelper.Br);
    }

    public static string FormatCoverageDays(double? days)
    {
        if (days is not double value || !double.IsFinite(value))
            return EmDash;
        if (Math.Abs(value - Math.Round(value)) < 0.05)
            return Math.Round(value).ToString("N0", ProductPriceHelper.Br);
        return value.ToString("N1", ProductPriceHelper.Br);
    }

    public static string FormatLastSale(DateTime? date) =>
        date is DateTime d ? d.ToString("dd/MM/yyyy", ProductPriceHelper.Br) : EmDash;

    public static string FormatDaysWithoutSale(int? days) =>
        days is int value ? value.ToString("N0", ProductPriceHelper.Br) : EmDash;

    public static string FormatHistory(int days)
    {
        var safe = Math.Max(0, days);
        return $"{safe.ToString("N0", ProductPriceHelper.Br)} dias";
    }

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

    /// <summary>
    /// Falha de Load: snapshot vazio só na primeira carga. Refresh preserva o último sucesso.
    /// </summary>
    public static LoadFailureDecision ResolveLoadFailure(bool hasValidSnapshot) =>
        hasValidSnapshot
            ? new LoadFailureDecision(true, RefreshKeepDataMessage)
            : new LoadFailureDecision(false, LoadErrorMessage);
}

public readonly record struct LoadFailureDecision(bool KeepPreviousSnapshot, string OperatorMessage);
