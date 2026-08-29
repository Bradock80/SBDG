using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Models;

/// <summary>Linha somente leitura da janela Lotes e validades.</summary>
public sealed class ProductLotListRow
{
    public required ProductLot Lot { get; init; }
    public ProductExpiryStatus Status { get; init; }

    public string LotDisplay =>
        string.IsNullOrWhiteSpace(Lot.LotNumber) ? "—" : Lot.LotNumber.Trim();

    public string QtyDisplay => FormatQty(Lot.Quantity);

    public string ExpiryDisplay => ProductExpiryService.FormatDisplay(Lot.ExpiryDate);

    public string DaysDisplay => ProductExpiryService.FormatDays(Status.Days);

    public string StatusDisplay => Status.Label;

    public string CostDisplay => ProductPriceHelper.MoneyBr(Lot.UnitCost);

    public string OriginDisplay =>
        Lot.PurchaseId is int id && id > 0 ? $"Compra #{id}" : "—";

    public string CreatedDisplay => FormatCreated(Lot.CreatedAt);

    public string NotesDisplay =>
        string.IsNullOrWhiteSpace(Lot.Notes) ? "—" : Lot.Notes.Trim();

    public string HistoryDisplay =>
        NotesDisplay == "—"
            ? CreatedDisplay
            : $"{CreatedDisplay} · {NotesDisplay}";

    public static IReadOnlyList<ProductLotListRow> FromLots(
        IEnumerable<ProductLot> lots, DateTime? today = null)
    {
        var list = new List<ProductLotListRow>();
        foreach (var lot in lots)
        {
            list.Add(new ProductLotListRow
            {
                Lot = lot,
                Status = ProductExpiryService.Classify(lot.ExpiryDate, today),
            });
        }
        return list;
    }

    public static string FormatQty(double qty)
    {
        if (Math.Abs(qty - Math.Round(qty)) < 0.0001)
            return Math.Round(qty).ToString("N0", ProductPriceHelper.Br);
        return qty.ToString("N3", ProductPriceHelper.Br);
    }

    private static string FormatCreated(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "—";
        return DateTime.TryParse(raw, out var dt)
            ? dt.ToString("dd/MM/yyyy HH:mm")
            : raw.Trim();
    }
}
