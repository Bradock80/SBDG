namespace SGDB.Models;

using SGDB.Utils;

public class OpenTabListRow
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string Status { get; init; } = "open";
    public int ItemsCount { get; init; }
    public double Total { get; init; }
    public string CreatedAt { get; init; } = "";
    public string? Notes { get; set; }
    public string? PrecontaAt { get; set; }

    public bool HasPreConta => !string.IsNullOrWhiteSpace(PrecontaAt);

    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    public string CreatedAtBr
    {
        get
        {
            try
            {
                return DateBrHelper.FormatUtcToBrazil(CreatedAt, "dd/MM HH:mm");
            }
            catch
            {
                if (DateTime.TryParse(CreatedAt, out var dt))
                    return dt.ToString("dd/MM HH:mm");
                return CreatedAt;
            }
        }
    }

    public string ElapsedDisplay
    {
        get
        {
            try
            {
                var opened = DateBrHelper.ParseUtcToBrazil(CreatedAt);
                var span = DateTime.Now - opened;
                if (span < TimeSpan.Zero)
                    span = TimeSpan.Zero;
                if (span.TotalHours >= 24)
                    return $"{(int)span.TotalHours}:{span.Minutes:00} h";
                return $"{(int)span.TotalMinutes:00}:{span.Seconds:00} min";
            }
            catch
            {
                return "—";
            }
        }
    }

    public string NotesDisplay
    {
        get => string.IsNullOrWhiteSpace(Notes) ? "" : Notes.Trim();
        set => Notes = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string CustomerDisplay =>
        string.IsNullOrWhiteSpace(CustomerName) ? "—" : CustomerName!;
}

public class OpenTabItemRow
{
    public int Id { get; init; }
    public int TabId { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Unit { get; init; } = "UN";
    public double Quantity { get; init; }
    public double UnitPrice { get; init; }
    public double Subtotal { get; init; }
    public double StockUnitsPerSale { get; init; } = 1;
    public string CreatedAt { get; init; } = "";

    public string QuantityDisplay => Quantity.ToString("N3");
    public string UnitPriceDisplay => ProductPriceHelper.MoneyBr(UnitPrice);
    public string SubtotalDisplay => ProductPriceHelper.MoneyBr(Subtotal);
}

public class OpenTabDetail
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string Status { get; init; } = "open";
    public int? SaleId { get; init; }
    public string? Notes { get; init; }
    public string CreatedAt { get; init; } = "";
    public string? SettledAt { get; init; }
    public string? PrecontaAt { get; init; }
    public IReadOnlyList<OpenTabItemRow> Items { get; init; } = [];

    public double Total => ProductPriceHelper.RoundPrice(Items.Sum(i => i.Subtotal));
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    public bool IsOpen => string.Equals(Status, "open", StringComparison.OrdinalIgnoreCase);
    public bool HasPreConta => !string.IsNullOrWhiteSpace(PrecontaAt);
}

public class OpenTabException : Exception
{
    public OpenTabException(string message) : base(message) { }
}

/// <summary>Card da visão Mesas (livre, ocupada, pré-conta ou balcão/avulso).</summary>
public sealed class DeckTableCard
{
    public int TableNumber { get; init; }
    public OpenTabListRow? Tab { get; init; }
    public bool IsBalcao { get; init; }

    public bool IsFree => Tab is null && !IsBalcao;
    public bool IsPreConta => Tab?.HasPreConta == true;
    public bool IsOccupied => Tab is not null && !IsPreConta;

    public string NumberDisplay => IsBalcao
        ? (Tab is null ? "AV" : Truncate(Tab.Name, 8))
        : TableNumber.ToString("00");

    public string ClientNameDisplay
    {
        get
        {
            if (Tab is null)
                return "";
            if (IsBalcao)
                return string.IsNullOrWhiteSpace(Tab.CustomerName) ? "Balcão" : Tab.CustomerName!;
            if (!string.IsNullOrWhiteSpace(Tab.CustomerName))
                return Truncate(Tab.CustomerName!, 14);
            // Evita repetir "Mesa 05" sob o número
            if (TryLooksLikeMesaLabel(Tab.Name))
                return "";
            return Truncate(Tab.Name, 14);
        }
    }

    public string TotalDisplay => Tab is null ? "" : Tab.TotalDisplay;

    public string ElapsedShort
    {
        get
        {
            if (Tab is null)
                return "";
            try
            {
                var opened = DateBrHelper.ParseUtcToBrazil(Tab.CreatedAt);
                var span = DateTime.Now - opened;
                if (span < TimeSpan.Zero)
                    span = TimeSpan.Zero;
                if (span.TotalHours >= 1)
                    return $"{(int)span.TotalHours}h{span.Minutes:00}";
                return $"{Math.Max(0, (int)span.TotalMinutes)}m";
            }
            catch
            {
                return "—";
            }
        }
    }

    public string FooterLine
    {
        get
        {
            if (Tab is null)
                return "";
            return $"{Tab.TotalDisplay} · {ElapsedShort}";
        }
    }

    public string ToolTipText
    {
        get
        {
            if (Tab is null)
                return $"Mesa {NumberDisplay} — livre. Clique para abrir.";
            var who = string.IsNullOrWhiteSpace(Tab.CustomerName) ? Tab.Name : Tab.CustomerName!;
            var estado = IsPreConta ? "Pré-conta" : IsBalcao ? "Balcão / avulso" : "Em andamento";
            return $"{(IsBalcao ? "Balcão" : $"Mesa {NumberDisplay}")} · {who}\n{estado} · {Tab.TotalDisplay} · {Tab.ItemsCount} it. · {ElapsedShort}";
        }
    }

    private static bool TryLooksLikeMesaLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return System.Text.RegularExpressions.Regex.IsMatch(name.Trim(), @"(?i)^mesa\s*#?\s*\d{1,3}$");
    }

    private static string Truncate(string value, int max)
    {
        value = value.Trim();
        if (value.Length <= max)
            return value;
        return value[..(max - 1)] + "…";
    }
}

public static class DeckTableHelper
{
    private static readonly System.Text.RegularExpressions.Regex MesaRegex = new(
        @"(?i)\bmesa\s*#?\s*(\d{1,3})\b|^(\d{1,3})$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static int? TryParseTableNumber(string? name, string? notes)
    {
        foreach (var raw in new[] { notes, name })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var m = MesaRegex.Match(raw.Trim());
            if (!m.Success)
                continue;
            var g = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (int.TryParse(g, out var n) && n is >= 1 and <= 200)
                return n;
        }
        return null;
    }

    public static IReadOnlyList<DeckTableCard> BuildCards(
        IEnumerable<OpenTabListRow> openTabs, int tableCount)
    {
        tableCount = Math.Clamp(tableCount, 1, 80);
        var tabs = openTabs.ToList();
        var byTable = new Dictionary<int, OpenTabListRow>();

        foreach (var tab in tabs)
        {
            var n = TryParseTableNumber(tab.Name, tab.Notes);
            if (n is int mesa && mesa >= 1 && mesa <= tableCount && !byTable.ContainsKey(mesa))
                byTable[mesa] = tab;
        }

        var cards = new List<DeckTableCard>(tableCount);
        for (var i = 1; i <= tableCount; i++)
        {
            byTable.TryGetValue(i, out var tab);
            cards.Add(new DeckTableCard { TableNumber = i, Tab = tab });
        }

        return cards;
    }

    public static IReadOnlyList<DeckTableCard> BuildBalcaoCards(
        IEnumerable<OpenTabListRow> openTabs, int tableCount)
    {
        tableCount = Math.Clamp(tableCount, 1, 80);
        return openTabs
            .Where(r =>
            {
                var n = TryParseTableNumber(r.Name, r.Notes);
                return n is null || n < 1 || n > tableCount;
            })
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new DeckTableCard
            {
                TableNumber = 0,
                Tab = r,
                IsBalcao = true,
            })
            .ToList();
    }
}
