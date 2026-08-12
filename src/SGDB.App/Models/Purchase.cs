using SGDB.Utils;

namespace SGDB.Models;

public sealed class Purchase
{
    public int Id { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = "";
    public string? SupplierCnpj { get; init; }
    public string? SupplierState { get; init; }
    public string EmissionDate { get; init; } = "";
    public string EntryDate { get; init; } = "";
    public string Series { get; init; } = "1";
    public string Number { get; init; } = "";
    public string? NfeKey { get; init; }
    public string Status { get; init; } = "aberta";
    public double Total { get; init; }
    public bool GerarEstoque { get; init; } = true;
    public string? Notes { get; init; }
    public string CreatedAt { get; init; } = "";
    public IReadOnlyList<PurchaseItem> Items { get; init; } = Array.Empty<PurchaseItem>();

    public string EmissionDateDisplay => DateBrHelper.FormatIso(EmissionDate);
    public string EntryDateDisplay => DateBrHelper.FormatIso(EntryDate);
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    public string NfeKeyDisplay => string.IsNullOrEmpty(NfeKey) ? "" :
        NfeKey.Length > 12 ? NfeKey[..12] + "..." : NfeKey;
    public string StatusDisplay => Status switch
    {
        "fechada" => "Fechada",
        "cancelada" => "Cancelada",
        _ => "Aberta",
    };
}

public sealed class PurchaseItem
{
    public int Id { get; init; }
    public int PurchaseId { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public double Quantity { get; init; }
    public double UnitPrice { get; init; }
    public double Subtotal { get; init; }

    public string QuantityDisplay => Quantity.ToString("G");
    public string UnitPriceDisplay => ProductPriceHelper.MoneyBr(UnitPrice);
    public string SubtotalDisplay => ProductPriceHelper.MoneyBr(Subtotal);
}
