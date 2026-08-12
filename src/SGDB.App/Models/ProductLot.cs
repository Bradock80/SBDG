namespace SGDB.Models;

public sealed class ProductLot
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Unit { get; init; } = "UN";
    public string LotNumber { get; init; } = "";
    public string? ExpiryDateIso { get; init; }
    public double Quantity { get; init; }
    public int? PurchaseId { get; init; }
    public double UnitCost { get; init; }
    public string CreatedAt { get; init; } = "";
    public string? Notes { get; init; }

    public DateTime? ExpiryDate =>
        DateTime.TryParse(ExpiryDateIso, out var d) ? d.Date : null;

    public int? DaysToExpiry
    {
        get
        {
            if (ExpiryDate is not DateTime e) return null;
            return (e.Date - DateTime.Today).Days;
        }
    }

    public string LotDisplay => string.IsNullOrWhiteSpace(LotNumber) ? "—" : LotNumber;
    public string ExpiryDisplay => ExpiryDate is DateTime e ? e.ToString("dd/MM/yyyy") : "—";
    public string QtyDisplay => Quantity.ToString("N3");
    public string DaysDisplay => DaysToExpiry is int d
        ? (d < 0 ? $"Vencido há {-d}d" : d == 0 ? "Vence hoje" : $"{d} dias")
        : "—";
    public string Tone
    {
        get
        {
            if (DaysToExpiry is not int d) return "ok";
            if (d < 0) return "expired";
            if (d <= 30) return "crit";
            if (d <= 60) return "warn";
            if (d <= 90) return "info";
            return "ok";
        }
    }
}

public sealed class ProductLotReceiveInput
{
    public int ProductId { get; init; }
    public double Quantity { get; init; }
    public string? LotNumber { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public int? PurchaseId { get; init; }
    public double UnitCost { get; init; }
    public string? Notes { get; init; }
}
