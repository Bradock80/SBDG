namespace SGDB.Models;

public enum StockIoDirectionFilter
{
    Todas,
    Entradas,
    Saidas,
}

public sealed class StockIoRow
{
    public long SortKey { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public string Unit { get; init; } = "UN";
    public string Operation { get; init; } = "";
    public double? StockBefore { get; set; }
    public double Quantity { get; init; }
    public bool IsEntry { get; init; }
    public double? StockAfter { get; set; }
    public string UserName { get; init; } = "";
    public string CreatedAtRaw { get; init; } = "";
    public string Notes { get; init; } = "";

    public string EsLabel => IsEntry ? "E" : "S";
    public string EsTone => IsEntry ? "entrada" : "saida";
    public string QtyDisplay => Quantity.ToString("N3");
    public string StockBeforeDisplay => StockBefore is double v ? v.ToString("N3") : "—";
    public string StockAfterDisplay => StockAfter is double v ? v.ToString("N3") : "—";
    public string DateTimeDisplay => FormatDateTime(CreatedAtRaw);
    public string UserDisplay => string.IsNullOrWhiteSpace(UserName) ? "—" : UserName;
    public string NotesDisplay => string.IsNullOrWhiteSpace(Notes) ? "—" : Notes;
    public string UnitDisplay => string.IsNullOrWhiteSpace(Unit) ? "UN" : Unit;

    private static string FormatDateTime(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "—";
        if (DateTime.TryParse(raw, out var dt))
            return dt.ToString("dd/MM/yyyy HH:mm");
        return raw.Length >= 16 ? raw[..16].Replace('T', ' ') : raw;
    }
}

public sealed class StockIoReportResult
{
    public DateTime DateFrom { get; init; }
    public DateTime DateTo { get; init; }
    public IReadOnlyList<StockIoRow> Rows { get; init; } = [];
    public int Registros { get; init; }
    public double TotalEntradas { get; init; }
    public double TotalSaidas { get; init; }
    public string TotalEntradasDisplay => TotalEntradas.ToString("N3");
    public string TotalSaidasDisplay => TotalSaidas.ToString("N3");
}
