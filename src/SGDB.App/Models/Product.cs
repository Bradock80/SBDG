using SGDB.Utils;

namespace SGDB.Models;

public sealed class Product
{
    public int Id { get; init; }
    public string? Code { get; init; }
    public string? Barcode { get; init; }
    public required string Name { get; init; }
    public string? GroupName { get; init; }
    public string Unit { get; init; } = "UN";
    public double CostPrice { get; init; }
    public double SalePrice { get; init; }
    public int MinStock { get; init; } = 5;
    /// <summary>Depósito / câmara (estoque fora da geladeira de venda).</summary>
    public double Stock { get; init; }
    /// <summary>Quantidade na geladeira (opcional; 0 = não usa ou vazio).</summary>
    public double StockFridge { get; init; }
    /// <summary>Mínimo na geladeira. &gt; 0 ativa o recurso de reposição.</summary>
    public int StockFridgeMin { get; init; }
    public string? Location { get; init; }
    public string ExtraJson { get; init; } = "{}";
    public bool Active { get; init; } = true;
    public string CreatedAt { get; init; } = "";

    public bool UsesFridge => StockFridgeMin > 0 || StockFridge > 0.0001;
    public double TotalStock => Stock + StockFridge;

    public string BarcodeDisplay => string.IsNullOrWhiteSpace(Barcode) ? "SEM GTIN" : Barcode;
    public string SalePriceDisplay => ProductPriceHelper.MoneyBr(SalePrice);
    /// <summary>Estoque em UN; com fator mostra também CX (ex.: 19 UN (1 CX + 7)).</summary>
    public string StockDisplay => FormatStockWithPacks(TotalStock, StockUnitLabel, ExtraJson);
    public string GroupDisplay => GroupName ?? "";

    /// <summary>Rótulo de estoque/lista: nunca CX/FD quando o PDV trabalha em unidades.</summary>
    public string StockUnitLabel => ResolveStockUnitLabel(Unit, ExtraJson);

    /// <summary>Preenchido na listagem: ex. "100 UN · 27/07/2026".</summary>
    public string LastEntryDisplay { get; set; } = "";

    /// <summary>
    /// Ex.: 24 UN (2 CX) · 19 UN (1 CX + 7) · 7 UN (sem CX fechada).
    /// </summary>
    public static string FormatStockWithPacks(double quantity, string? unitLabel, string? extraJson)
    {
        var u = string.IsNullOrWhiteSpace(unitLabel) ? "UN" : unitLabel.Trim();
        var qtyText = FormatStockQty(quantity);
        var baseText = $"{qtyText} {u}";

        var factor = ProductExtra.Parse(extraJson).FatorEmbalagem;
        if (factor < 2)
            return baseText;

        // Quantidade negativa: só mostra o número (ajuste/inventário)
        if (quantity < -0.0001)
            return baseText;

        var packs = (int)Math.Floor((quantity + 1e-9) / factor);
        var remainder = Math.Round(quantity - packs * factor, 4);
        if (Math.Abs(remainder) < 0.0001)
            remainder = 0;

        if (packs <= 0)
            return baseText;

        if (remainder <= 0)
            return $"{baseText} ({packs} CX)";

        return $"{baseText} ({packs} CX + {FormatStockQty(remainder)})";
    }

    private static string FormatStockQty(double v) =>
        Math.Abs(v - Math.Round(v)) < 0.0001
            ? ((long)Math.Round(v)).ToString()
            : v.ToString("0.####");

    public static string ResolveStockUnitLabel(string? unit, string? extraJson = null)
    {
        var u = string.IsNullOrWhiteSpace(unit) ? "UN" : unit.Trim();
        if (IsPackUnitLabel(u))
            return "UN";

        if (!string.IsNullOrWhiteSpace(extraJson))
        {
            var factor = ProductExtra.Parse(extraJson).FatorEmbalagem;
            if (factor >= 2 && IsPackUnitLabel(u))
                return "UN";
        }

        return u;
    }

    public static bool IsPackUnitLabel(string? unit)
    {
        var u = (unit ?? "").Trim().ToUpperInvariant();
        return u is "EB" or "CX" or "CXA" or "FD" or "FARDO" or "PCT" or "DP" or "DZ"
            or "DISPLAY" or "CJ" or "KIT" or "SC" or "BDJ" or "BANDEJA"
            or "CT" or "CRT" or "CARTELA" or "CART" or "BOX" or "PACK";
    }
}
