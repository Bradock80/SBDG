using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Regras da Central de Validades: faixas exclusivas da B2, ordenação, cards e alertas.
/// Não lê extra_json.data_validade. Não inventa lote.
/// </summary>
public static class ValidityControlEngine
{
    public const string MissingExpiryLabel = "Validade não informada";
    public const string UntrackedStockLabel = "Estoque sem validade/lote identificado";

    public static ProductExpiryStatusKind? BucketOf(ValidityControlFilterKind filter) =>
        filter switch
        {
            ValidityControlFilterKind.Expired => ProductExpiryStatusKind.Expired,
            ValidityControlFilterKind.Today => ProductExpiryStatusKind.Today,
            ValidityControlFilterKind.Days7 => ProductExpiryStatusKind.Within7,
            ValidityControlFilterKind.Days15 => ProductExpiryStatusKind.Within15,
            ValidityControlFilterKind.Days30 => ProductExpiryStatusKind.Within30,
            ValidityControlFilterKind.Days60 => ProductExpiryStatusKind.Within60,
            ValidityControlFilterKind.Days90 => ProductExpiryStatusKind.Within90,
            ValidityControlFilterKind.Uninformed => ProductExpiryStatusKind.Uninformed,
            _ => null,
        };

    public static string ToneFor(ProductExpiryStatusKind kind) =>
        kind switch
        {
            ProductExpiryStatusKind.Expired => "expired",
            ProductExpiryStatusKind.Today or ProductExpiryStatusKind.Within7 => "alert",
            ProductExpiryStatusKind.Within15 or ProductExpiryStatusKind.Within30 => "attention",
            ProductExpiryStatusKind.Within60 or ProductExpiryStatusKind.Within90 => "notice",
            ProductExpiryStatusKind.Uninformed => "info",
            _ => "ok",
        };

    public static bool MatchesFilter(ValidityControlRow row, ValidityControlFilterKind filter)
    {
        var bucket = BucketOf(filter);
        return bucket is null || row.Status.Kind == bucket;
    }

    public static bool MatchesSearch(
        ValidityControlRow row, string? search, string? group, string? brand)
    {
        if (!string.IsNullOrWhiteSpace(group)
            && !string.Equals(row.GroupName, group.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(brand)
            && !string.Equals(row.BrandName, brand.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(search))
            return true;
        var term = search.Trim();
        return row.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || row.ProductCode.Contains(term, StringComparison.OrdinalIgnoreCase)
            || row.LotDisplay.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ValidityControlRow> Apply(
        IEnumerable<ValidityControlRow> rows,
        ValidityControlFilterKind filter,
        string? search = null,
        string? group = null,
        string? brand = null)
    {
        return Sort(rows.Where(r => MatchesFilter(r, filter) && MatchesSearch(r, search, group, brand)));
    }

    public static IReadOnlyList<ValidityControlRow> Sort(IEnumerable<ValidityControlRow> rows) =>
        rows
            .OrderBy(r => Rank(r.Status.Kind))
            .ThenBy(r => r.DaysRemaining ?? int.MaxValue)
            .ThenBy(r => r.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LotDisplay, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static ValidityControlCards CountCards(IEnumerable<ValidityControlRow> rows)
    {
        var cards = new ValidityControlCards();
        foreach (var row in rows)
        {
            switch (row.Status.Kind)
            {
                case ProductExpiryStatusKind.Expired: cards.Expired++; break;
                case ProductExpiryStatusKind.Today: cards.Today++; break;
                case ProductExpiryStatusKind.Within7: cards.Days7++; break;
                case ProductExpiryStatusKind.Within15: cards.Days15++; break;
                case ProductExpiryStatusKind.Within30: cards.Days30++; break;
                case ProductExpiryStatusKind.Within60: cards.Days60++; break;
                case ProductExpiryStatusKind.Within90: cards.Days90++; break;
                case ProductExpiryStatusKind.Ok: cards.Ok++; break;
                default: cards.Uninformed++; break;
            }
        }
        return cards;
    }

    public static IReadOnlyList<ValidityControlRow> BuildRows(
        IEnumerable<ValidityControlProductInput> products, DateTime? today = null)
    {
        var day = (today ?? DateTime.Today).Date;
        var rows = new List<ValidityControlRow>();
        foreach (var product in products)
        {
            var activeLots = product.Lots
                .Where(l => l.Quantity > 0.0001)
                .ToList();
            var lotsQty = activeLots.Sum(l => l.Quantity);

            foreach (var lot in activeLots)
                rows.Add(FromLot(lot, product, day));

            if (!product.ExplicitExpiryControl)
                continue;

            var leftover = Math.Round(product.Stock - lotsQty, 4);
            if (leftover <= StockLotConsistencyService.Tolerance)
                continue;

            if (activeLots.Count == 0)
                rows.Add(AlertRow(product, leftover, ValidityControlRowKind.MissingExpiry, MissingExpiryLabel));
            else
                rows.Add(AlertRow(product, leftover, ValidityControlRowKind.UntrackedStock, UntrackedStockLabel));
        }

        return Sort(rows);
    }

    public static ValidityControlSnapshot Snapshot(
        IEnumerable<ValidityControlProductInput> products, DateTime? today = null)
    {
        var rows = BuildRows(products, today);
        return new ValidityControlSnapshot
        {
            Rows = rows,
            Cards = CountCards(rows),
        };
    }

    public static ValidityControlRow FromLot(
        ProductLot lot, ValidityControlProductInput product, DateTime today)
    {
        var status = ProductExpiryService.Classify(lot.ExpiryDate, today);
        var uninformed = status.Kind == ProductExpiryStatusKind.Uninformed;
        return new ValidityControlRow
        {
            ProductId = product.ProductId,
            LotId = lot.Id == 0 ? null : lot.Id,
            ProductName = product.Name,
            ProductCode = product.Code,
            GroupName = product.GroupName,
            BrandName = product.BrandName,
            LotDisplay = string.IsNullOrWhiteSpace(lot.LotNumber) ? "—" : lot.LotNumber.Trim(),
            Quantity = lot.Quantity,
            ExpiryDate = lot.ExpiryDate,
            DaysRemaining = status.Days,
            Status = status,
            StatusDisplay = status.Label,
            UnitCost = lot.UnitCost,
            OriginDisplay = lot.PurchaseId is int id && id > 0 ? $"Compra #{id}" : "—",
            RowKind = uninformed ? ValidityControlRowKind.UninformedLot : ValidityControlRowKind.Lot,
            Tone = ToneFor(status.Kind),
        };
    }

    public static string FormatHomeSummary(ValidityControlCards cards)
    {
        var parts = new List<string>();
        if (cards.Expired > 0)
            parts.Add($"{cards.Expired} vencido{(cards.Expired == 1 ? "" : "s")}");
        if (cards.Today > 0)
            parts.Add($"{cards.Today} hoje");
        if (cards.Days7 > 0)
            parts.Add($"{cards.Days7} até 7 dias");
        var until30 = cards.Days15 + cards.Days30;
        if (until30 > 0)
            parts.Add($"{until30} até 30 dias");
        if (parts.Count == 0)
            return "";
        return "Validades: " + string.Join(" • ", parts);
    }

    public static bool ShouldShowHomeAlert(ValidityControlCards cards) =>
        cards.Expired + cards.Today + cards.Days7 + cards.Days15 + cards.Days30 > 0;

    static int Rank(ProductExpiryStatusKind kind) =>
        kind switch
        {
            ProductExpiryStatusKind.Expired => 0,
            ProductExpiryStatusKind.Today => 1,
            ProductExpiryStatusKind.Within7 => 2,
            ProductExpiryStatusKind.Within15 => 3,
            ProductExpiryStatusKind.Within30 => 4,
            ProductExpiryStatusKind.Within60 => 5,
            ProductExpiryStatusKind.Within90 => 6,
            ProductExpiryStatusKind.Ok => 7,
            _ => 8,
        };

    static ValidityControlRow AlertRow(
        ValidityControlProductInput product,
        double qty,
        ValidityControlRowKind kind,
        string label) =>
        new()
        {
            ProductId = product.ProductId,
            ProductName = product.Name,
            ProductCode = product.Code,
            GroupName = product.GroupName,
            BrandName = product.BrandName,
            LotDisplay = "—",
            Quantity = qty,
            Status = ProductExpiryStatus.Uninformed,
            StatusDisplay = label,
            RowKind = kind,
            Tone = ToneFor(ProductExpiryStatusKind.Uninformed),
        };
}
