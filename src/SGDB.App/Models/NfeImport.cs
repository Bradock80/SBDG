using System.ComponentModel;
using System.Runtime.CompilerServices;
using SGDB.Domain.Products;
using SGDB.Utils;

namespace SGDB.Models;

public sealed class NfeImportItem : INotifyPropertyChanged
{
    private double _quantity;
    private double _unitPrice;
    private double _salePrice;
    private double _totalValue;

    public string Cprod { get; set; } = "";
    public string? Barcode { get; set; }
    public string? PackBarcode { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "UN";

    /// <summary>Preço unitário original da NF (antes da conversão fardo→UN), só referência.</summary>
    public double NfUnitPrice { get; set; }
    public double NfQuantity { get; set; }
    public string NfUnit { get; set; } = "UN";

    /// <summary>Custo unitário sem ICMS-ST (preço da nota).</summary>
    public double UnitPriceWithoutSt { get; set; }
    /// <summary>Custo unitário com ICMS-ST/FCP-ST.</summary>
    public double UnitPriceWithSt { get; set; }

    public double Quantity
    {
        get => _quantity;
        set
        {
            if (Math.Abs(_quantity - value) < 0.0000001) return;
            _quantity = Math.Max(0, value);
            RecalcTotal();
            OnPropertyChanged();
            OnPropertyChanged(nameof(QtyDisplay));
            OnPropertyChanged(nameof(TotalDisplay));
        }
    }

    public double UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (Math.Abs(_unitPrice - value) < 0.0000001) return;
            _unitPrice = Math.Max(0, value);
            RecalcTotal();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnitPriceDisplay));
            OnPropertyChanged(nameof(TotalDisplay));
            OnPropertyChanged(nameof(CostPackDisplayValue));
            OnPropertyChanged(nameof(CatalogCostDisplay));
        }
    }

    /// <summary>Preço de venda sugerido/editável (unidade de venda).</summary>
    public double SalePrice
    {
        get => _salePrice;
        set
        {
            if (Math.Abs(_salePrice - value) < 0.0000001) return;
            _salePrice = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SalePriceDisplay));
        }
    }

    public double TotalValue
    {
        get => _totalValue;
        set
        {
            if (Math.Abs(_totalValue - value) < 0.0000001) return;
            _totalValue = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalDisplay));
        }
    }

    /// <summary>Unidades por embalagem (ex.: 12 latas/fardo). 1 = sem conversão.</summary>
    public double PackFactor { get; set; } = 1;
    public string? PackNote { get; set; }
    public int? MatchedProductId { get; set; }
    public string? MatchedProductName { get; set; }
    public bool IsNew => MatchedProductId is null;
    public bool ConvertedFromPack => PackFactor > 1.0001;

    /// <summary>Lote vindo do XML (&lt;nLote&gt;) ou informado manualmente.</summary>
    public string LotNumber { get; set; } = "";
    /// <summary>Validade (yyyy-MM-dd) do XML (&lt;dVal&gt;) ou informada manualmente.</summary>
    public string? ExpiryDateIso { get; set; }
    public bool HasXmlRastro { get; set; }

    public DateTime? ExpiryDate
    {
        get => DateTime.TryParse(ExpiryDateIso, out var d) ? d.Date : null;
        set => ExpiryDateIso = value?.ToString("yyyy-MM-dd");
    }

    public bool NeedsManualExpiry => string.IsNullOrWhiteSpace(ExpiryDateIso);
    public string LotDisplay => string.IsNullOrWhiteSpace(LotNumber) ? "—" : LotNumber;
    public string ExpiryDisplay => ExpiryDate is DateTime e ? e.ToString("dd/MM/yyyy") : "—";
    public string RastroBadge => HasXmlRastro ? "XML" : (NeedsManualExpiry ? "Informar" : "Manual");

    public double CostPackDisplayValue =>
        PackFactor > 1.0001 ? Math.Round(UnitPrice * PackFactor, 4) : UnitPrice;

    /// <summary>Cigarro: maço no cadastro. Demais: unitário.</summary>
    public bool UsesPackCatalogPricing =>
        ProductClassificationHelper.UsesPackPurchasePrice(Name, ResolveGroup())
        && PackFactor >= 2;

    public string CatalogCostDisplay => ProductPriceHelper.MoneyBr(ResolveCatalogCost());

    public string CatalogCostLabel => UsesPackCatalogPricing ? "Custo maço" : "Custo cad.";

    public double ResolveCatalogCost() =>
        ProductPriceHelper.ResolveCatalogCost(
            UnitPrice, PackFactor, Name, ResolveGroup(), TotalValue, Quantity);

    public double ResolveCatalogSale(double? marginPercent = null) =>
        ProductPriceHelper.ResolveCatalogSale(SalePrice, UnitPrice, PackFactor, Name, ResolveGroup(), marginPercent);

    private string? ResolveGroup() => ProductClassificationHelper.Infer(Name).Group;

    public string QtyDisplay => Quantity.ToString("N3");
    public string UnitPriceDisplay => ProductPriceHelper.MoneyBr(UnitPrice);
    public string SalePriceDisplay => ProductPriceHelper.MoneyBr(SalePrice);
    public string TotalDisplay => ProductPriceHelper.MoneyBr(TotalValue);
    public string NfPriceDisplay => ProductPriceHelper.MoneyBr(NfUnitPrice);
    public string BarcodeDisplay => string.IsNullOrWhiteSpace(Barcode) ? "SEM GTIN" : Barcode;
    public string PackNoteDisplay => string.IsNullOrWhiteSpace(PackNote) ? "—" : PackNote;
    public string StatusBadge => IsNew ? "Novo" : "Ok";
    public string StatusDisplay => IsNew
        ? "Produto novo — será cadastrado se a opção estiver marcada"
        : $"Vinculado: {MatchedProductName}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RecalcTotal()
    {
        TotalValue = ProductPriceCalculator.RoundPrice(Quantity * UnitPrice);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class NfeImportPreview
{
    public string Chave { get; set; } = "";
    public string EmitenteCnpj { get; set; } = "";
    public string EmitenteNome { get; set; } = "";
    public string EmitenteFantasia { get; set; } = "";
    public string EmitenteIe { get; set; } = "";
    public string? EmitentePhone { get; set; }
    public string? EmitenteCep { get; set; }
    public string? EmitenteAddress { get; set; }
    public string? EmitenteAddressNumber { get; set; }
    public string? EmitenteComplement { get; set; }
    public string? EmitenteNeighborhood { get; set; }
    public string? EmitenteCity { get; set; }
    public string? EmitenteState { get; set; }
    public string Numero { get; set; } = "";
    public string Serie { get; set; } = "1";
    public string DataEmissao { get; set; } = "";
    public double HeaderVProd { get; set; }
    public double HeaderSt { get; set; }
    public double HeaderDesc { get; set; }
    public double HeaderVNf { get; set; }
    public double FatOrig { get; set; }
    public double FatDesc { get; set; }
    public double FatLiq { get; set; }
    public List<NfeImportItem> Items { get; set; } = [];

    public int NewProductsCount => Items.Count(i => i.IsNew);
    public int MatchedProductsCount => Items.Count(i => !i.IsNew);
    public double TotalValue => ProductPriceCalculator.RoundPrice(Items.Sum(i => i.TotalValue));

    public string ChaveDisplay => string.IsNullOrWhiteSpace(Chave) ? "—" : Chave;
    public string EmissionDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DataEmissao))
                return "";
            if (DateTime.TryParse(DataEmissao, out var dt))
                return dt.ToString("dd/MM/yyyy");
            return DataEmissao;
        }
    }
}

public sealed class NfeImportApplyResult
{
    public int PurchaseId { get; set; }
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public bool SupplierCreated { get; set; }
    public int ProductsCreated { get; set; }
    public bool StockUpdated { get; set; }
    public bool CostUpdated { get; set; }
}
