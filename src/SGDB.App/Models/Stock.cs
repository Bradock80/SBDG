namespace SGDB.Models;

using SGDB.Domain.Products;
using SGDB.Utils;

public enum ProductCatalogKind
{
    Groups,
    Units,
    Brands,
}

public sealed class CatalogItem
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Active { get; set; } = true;
    public string CreatedAt { get; init; } = "";

    public string ActiveDisplay => Active ? "Ativo" : "Inativo";
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description;
}

public enum StockAdjustMode
{
    Entrada,
    Saida,
    Saldo,
}

public enum StockReportKind
{
    Negativo,
    Minimo,
    FridgeRestock,
    Validade7d,
    MaisVendidos,
    MenosVendidos,
    MaisLucrativos,
    MenosLucrativos,
    ZeraNegativo,
    CurvaAbc,
}

public sealed class StockMovementRow
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string MovementType { get; init; } = "";
    public double Quantity { get; init; }
    public double UnitPrice { get; init; }
    public string? Notes { get; init; }
    public string CreatedAt { get; init; } = "";

    public string TypeDisplay => MovementType.Equals("entrada", StringComparison.OrdinalIgnoreCase) ? "ENTRADA" : "SAÍDA";
    public string QtyDisplay => Quantity.ToString("N3");
    public string CreatedDisplay => CreatedAt.Length >= 16 ? CreatedAt[..16].Replace('T', ' ') : CreatedAt;
}

public sealed class StockReportRow
{
    public int Posicao { get; init; }
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string GroupName { get; init; } = "";
    public double Stock { get; init; }
    public double MinStock { get; init; }
    public string Unit { get; init; } = "UN";
    public string Location { get; init; } = "";
    public double StockValue { get; init; }
    public string DataValidade { get; init; } = "";
    public int? DiasValidade { get; init; }
    public double Qty { get; init; }
    public double Total { get; init; }
    public double Lucro { get; init; }
    public double CostTotal { get; init; }
    public string? AbcClass { get; init; }
    public double? DaysOfStock { get; init; }
    public double CapitalParado { get; init; }
    public double AvgDailySales { get; init; }

    public string StockDisplay => Stock.ToString("N3");
    public string StockValueDisplay => ProductPriceHelper.MoneyBr(StockValue);
    public string QtyDisplay => Qty.ToString("N3");
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    public string LucroDisplay => ProductPriceHelper.MoneyBr(Lucro);
    public string AbcClassDisplay => string.IsNullOrEmpty(AbcClass) ? "—" : AbcClass;
    public string DaysOfStockDisplay => DaysOfStock is double d ? d.ToString("N0") : "—";
    public string CapitalParadoDisplay => ProductPriceHelper.MoneyBr(CapitalParado);
    public string AvgDailySalesDisplay => AvgDailySales.ToString("N3");
}

public sealed class StockReportResult
{
    public StockReportKind Kind { get; set; }
    public List<StockReportRow> Rows { get; set; } = [];
    public int Registros { get; set; }
    public double TotalStock { get; set; }
    public double TotalValor { get; set; }
    public double TotalQty { get; set; }
    public double TotalLucro { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool HasEstimatedLegacyCost { get; set; }
    public bool CmvUsesHistoricalSnapshot { get; set; }
    public string? CmvReliabilityNote { get; set; }
}

public sealed class StockAdjustResult
{
    public int ProductId { get; set; }
    public double StockBefore { get; set; }
    public double StockAfter { get; set; }
    public string? MovementType { get; set; }
    public double Quantity { get; set; }
    public int? MovementId { get; set; }
}

public sealed class PriceAdjustRow : System.ComponentModel.INotifyPropertyChanged
{
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string? Barcode { get; init; }
    public string Name { get; init; } = "";
    public string? Brand { get; init; }
    public double CostPercent { get; init; }
    public double MarginPercent { get; init; }
    public double SalePrice { get; init; }

    public double OriginalPurchasePrice { get; set; }
    public double OriginalSalePrice { get; set; }

    private double _purchasePrice;
    public double PurchasePrice
    {
        get => _purchasePrice;
        set
        {
            if (Math.Abs(_purchasePrice - value) < 0.0001) return;
            _purchasePrice = ProductPriceCalculator.RoundPrice(Math.Max(0, value));
            OnPropertyChanged(nameof(PurchasePrice));
            OnPropertyChanged(nameof(PurchaseDisplay));
            RecalcFromPurchase();
            NotifyFlags();
        }
    }

    private double _costPrice;
    public double CostPrice
    {
        get => _costPrice;
        set
        {
            if (Math.Abs(_costPrice - value) < 0.0001) return;
            _costPrice = ProductPriceCalculator.RoundPrice(Math.Max(0, value));
            OnPropertyChanged(nameof(CostPrice));
            OnPropertyChanged(nameof(CostDisplay));
            NotifyFlags();
        }
    }

    private double _newMarginPercent;
    public double NewMarginPercent
    {
        get => _newMarginPercent;
        set
        {
            if (Math.Abs(_newMarginPercent - value) < 0.0001) return;
            // Margem % alinhada ao arredondamento monetário do Domain (mesmo Midpoint que MarginOnSale).
            _newMarginPercent = ProductPriceCalculator.RoundPrice(value);
            OnPropertyChanged(nameof(NewMarginPercent));
            OnPropertyChanged(nameof(NewMarginDisplay));
        }
    }

    private double _newSalePrice;
    public double NewSalePrice
    {
        get => _newSalePrice;
        set
        {
            if (Math.Abs(_newSalePrice - value) < 0.0001) return;
            _newSalePrice = ProductPriceCalculator.RoundPrice(Math.Max(0, value));
            OnPropertyChanged(nameof(NewSalePrice));
            OnPropertyChanged(nameof(NewSaleDisplay));
            // Alterou venda → recalcula Nova Margem com base no Pr.Custo
            NewMarginPercent = MarginOnSale(_costPrice, _newSalePrice);
            NotifyFlags();
        }
    }

    /// <summary>Carrega valores iniciais sem disparar recálculos em cascata.</summary>
    public void LoadPrices(double purchase, double cost, double newMargin, double newSale)
    {
        _purchasePrice = ProductPriceCalculator.RoundPrice(Math.Max(0, purchase));
        _costPrice = ProductPriceCalculator.RoundPrice(Math.Max(0, cost));
        _newSalePrice = ProductPriceCalculator.RoundPrice(Math.Max(0, newSale));
        _newMarginPercent = ProductPriceCalculator.RoundPrice(newMargin);
        OriginalPurchasePrice = _purchasePrice;
        OriginalSalePrice = ProductPriceCalculator.RoundPrice(SalePrice);
    }

    public string PurchaseDisplay => ProductPriceHelper.MoneyBr(PurchasePrice);
    public string CostDisplay => ProductPriceHelper.MoneyBr(CostPrice);
    public string MarginDisplay => MarginPercent.ToString("N2");
    public string SaleDisplay => ProductPriceHelper.MoneyBr(SalePrice);
    public string NewMarginDisplay => NewMarginPercent.ToString("N2");
    public string NewSaleDisplay => ProductPriceHelper.MoneyBr(NewSalePrice);
    public string BarcodeDisplay => string.IsNullOrWhiteSpace(Barcode) ? "SEM GTIN" : Barcode;
    public string ModifiedMark => IsModified ? "●" : "";

    public bool PurchaseChanged => Math.Abs(PurchasePrice - OriginalPurchasePrice) > 0.009;
    public bool SaleChanged => Math.Abs(NewSalePrice - OriginalSalePrice) > 0.009;
    public bool IsModified => PurchaseChanged || SaleChanged;
    /// <summary>Venda nova menor ou igual ao custo → prejuízo.</summary>
    public bool IsBelowCost => CostPrice > 0.009 && NewSalePrice <= CostPrice + 0.0001;

    /// <summary>
    /// Compra alterada → atualiza Pr.Custo (% custo) e sugere Pr.Venda Novo
    /// mantendo a margem original do produto.
    /// </summary>
    public void RecalcFromPurchase()
    {
        var newCost = CostPercent > 0
            ? ProductPriceCalculator.CostFromPurchaseAndPercent(PurchasePrice, CostPercent)
            : ProductPriceCalculator.RoundPrice(PurchasePrice);

        _costPrice = newCost;
        OnPropertyChanged(nameof(CostPrice));
        OnPropertyChanged(nameof(CostDisplay));

        var suggestedSale = SaleFromMargin(newCost, MarginPercent);
        _newSalePrice = suggestedSale;
        _newMarginPercent = ProductPriceCalculator.RoundPrice(MarginPercent);
        OnPropertyChanged(nameof(NewSalePrice));
        OnPropertyChanged(nameof(NewSaleDisplay));
        OnPropertyChanged(nameof(NewMarginPercent));
        OnPropertyChanged(nameof(NewMarginDisplay));
    }

    public static double MarginOnSale(double cost, double sale) =>
        ProductPriceCalculator.MarginOnSale(cost, sale);

    /// <summary>
    /// Venda sugerida pela margem. Mantém edges locais (custo≤0 / margem≤0 / margem≥100%)
    /// que diferem de <see cref="ProductPriceCalculator.SaleFromCostAndMargin"/>.
    /// </summary>
    public static double SaleFromMargin(double cost, double marginPct)
    {
        if (cost <= 0) return 0;
        var m = marginPct / 100.0;
        if (m >= 1) return ProductPriceCalculator.RoundPrice(cost);
        if (m <= 0) return ProductPriceCalculator.RoundPrice(cost);
        return ProductPriceCalculator.SaleFromCostAndMargin(cost, marginPct);
    }

    private void NotifyFlags()
    {
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IsBelowCost));
        OnPropertyChanged(nameof(ModifiedMark));
        OnPropertyChanged(nameof(PurchaseChanged));
        OnPropertyChanged(nameof(SaleChanged));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
