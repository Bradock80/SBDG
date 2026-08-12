using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public sealed class PurchaseItemDraft : INotifyPropertyChanged
{
    private double _quantity;
    private double _unitPrice;
    private double _margin;
    private double _suggestedPrice;
    private double _salePrice;

    public int Seq { get; set; }
    public int ProductId { get; set; }
    public string? Barcode { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Cfop { get; set; } = "";
    public string Cst { get; set; } = "";

    public double Quantity
    {
        get => _quantity;
        set { _quantity = value; OnPropertyChanged(); OnPropertyChanged(nameof(QuantityDisplay)); OnPropertyChanged(nameof(Subtotal)); OnPropertyChanged(nameof(SubtotalDisplay)); }
    }

    public double UnitPrice
    {
        get => _unitPrice;
        set { _unitPrice = value; OnPropertyChanged(); OnPropertyChanged(nameof(UnitPriceDisplay)); OnPropertyChanged(nameof(Subtotal)); OnPropertyChanged(nameof(SubtotalDisplay)); }
    }

    public double PrevCost { get; set; }
    public double PrevSale { get; set; }

    /// <summary>Itens por maço/fardo (cigarro=20). Usado só no Preço Venda/cadastro.</summary>
    public double PackFactor { get; set; } = 1;
    public string? GroupName { get; set; }

    /// <summary>Custo da NF sem ST / com ST (para alternar o checkbox).</summary>
    public double UnitPriceWithoutSt { get; set; }
    public double UnitPriceWithSt { get; set; }

    /// <summary>Lote/validade para FEFO (XML &lt;rastro&gt; ou informado na entrada).</summary>
    public string LotNumber { get; set; } = "";
    public DateTime? ExpiryDate { get; set; }
    public bool HasXmlRastro { get; set; }

    /// <summary>Base do lucro no cadastro: maço (cigarro = total÷maços) ou unitário.</summary>
    public double CatalogCost =>
        ProductPriceHelper.ResolveCatalogCost(
            UnitPrice, PackFactor, ProductName, GroupName, Subtotal, Quantity);

    public double Margin
    {
        get => _margin;
        set { _margin = value; OnPropertyChanged(); OnPropertyChanged(nameof(MarginDisplay)); }
    }

    public double SuggestedPrice
    {
        get => _suggestedPrice;
        set { _suggestedPrice = value; OnPropertyChanged(); OnPropertyChanged(nameof(SuggestedPriceDisplay)); }
    }

    public double SalePrice
    {
        get => _salePrice;
        set { _salePrice = value; OnPropertyChanged(); OnPropertyChanged(nameof(SalePriceDisplay)); }
    }

    public double Subtotal => Quantity * UnitPrice;

    public string QuantityDisplay
    {
        get => Quantity.ToString("N2");
        set
        {
            var q = ProductPriceHelper.ParseBr(value);
            if (q <= 0) return;
            // Mantém o total da NF e recalcula o custo unitário (ex.: CX R$ 34 → 100 un)
            var total = Quantity * UnitPrice;
            Quantity = q;
            if (total > 0)
            {
                UnitPrice = ProductPriceHelper.RoundPrice(total / q);
                UnitPriceWithoutSt = UnitPrice;
                UnitPriceWithSt = UnitPrice;
                RecalcSuggestedFromMargin();
                if (SalePrice <= 0)
                    SalePrice = SuggestedPrice;
            }
            OnPropertyChanged(nameof(QuantityDisplay));
        }
    }

    public string UnitPriceDisplay
    {
        get => UnitPrice.ToString("N2");
        set
        {
            var p = ProductPriceHelper.ParseBr(value);
            if (p < 0) return;
            UnitPrice = p;
            // Mantém Pr. Venda; só recalcula margem % e sugerido.
            var keepSale = SalePrice > 0 ? SalePrice : PrevSale;
            if (keepSale > 0)
                ApplySalePrice(keepSale);
            else
                RecalcSuggestedFromMargin();
            OnPropertyChanged(nameof(UnitPriceDisplay));
        }
    }

    public string SubtotalDisplay => Subtotal.ToString("N2");
    public string PrevCostDisplay => PrevCost.ToString("N2");
    public string SuggestedPriceDisplay => SuggestedPrice.ToString("N2");

    public string MarginDisplay
    {
        get => Margin.ToString("N2");
        set => ApplyMarginPercent(ProductPriceHelper.ParseBr(value));
    }

    public string SalePriceDisplay
    {
        get => SalePrice.ToString("N2");
        set => ApplySalePrice(ProductPriceHelper.ParseBr(value));
    }

    private void RecalcSuggestedFromMargin()
    {
        SuggestedPrice = ProductPriceHelper.SaleFromCostAndMargin(CatalogCost, Margin);
    }

    /// <summary>
    /// Margem % → Pr. Venda. Cigarro: calcula sobre o maço (ex.: 13,40 → 14,50).
    /// Refrigerante: sobre o unitário.
    /// </summary>
    public void ApplyMarginPercent(double marginPercent)
    {
        if (marginPercent < 0) marginPercent = 0;
        if (marginPercent >= 100) marginPercent = 99.99;
        Margin = ProductPriceHelper.RoundPrice(marginPercent);
        RecalcSuggestedFromMargin();
        SalePrice = SuggestedPrice;
    }

    /// <summary>Define preço de venda (maço no cigarro) e recalcula margem %.</summary>
    public void ApplySalePrice(double sale)
    {
        sale = ProductPriceHelper.RoundPrice(Math.Max(0, sale));
        SalePrice = sale;
        SuggestedPrice = sale;
        Margin = ProductPriceHelper.MarginOnSale(CatalogCost, sale);
    }

    public static void FillPackMeta(PurchaseItemDraft draft, Product product)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        var group = product.GroupName;
        ProductClassificationHelper.FillMissing(product.Name, ref group, extra);
        draft.GroupName = group;
        draft.PackFactor = extra.FatorEmbalagem > 1 ? extra.FatorEmbalagem
            : extra.QtdAtacado > 1 ? extra.QtdAtacado
            : ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group) ? 20 : 1;
    }

    /// <summary>Pr. Venda na grade: cigarro = maço; demais = unitário.</summary>
    public static double ResolveGridSalePrice(
        Product product, double unitCost, double defaultMargin, double packFactor)
    {
        var group = product.GroupName;
        var extra = ProductExtra.Parse(product.ExtraJson);
        ProductClassificationHelper.FillMissing(product.Name, ref group, extra);
        var factor = packFactor > 1 ? packFactor
            : extra.FatorEmbalagem > 1 ? extra.FatorEmbalagem
            : extra.QtdAtacado > 1 ? extra.QtdAtacado : 1;

        if (ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group) && factor >= 2)
        {
            // Já cadastrado como maço (ex.: 14,50) — mantém
            if (product.SalePrice >= 5)
                return ProductPriceHelper.RoundPrice(product.SalePrice);
            return ProductPriceHelper.ResolveCatalogSale(
                0, unitCost, factor, product.Name, group, defaultMargin);
        }

        if (product.SalePrice > 0)
            return ProductPriceHelper.RoundPrice(product.SalePrice);
        return ProductPriceHelper.SaleFromCostAndMargin(unitCost, defaultMargin);
    }

    /// <summary>Preço sugerido com a margem padrão sobre o custo novo da NF.</summary>
    public static double ResolveSuggestedSalePrice(
        Product product, double unitCost, double defaultMargin, double packFactor)
    {
        var group = product.GroupName;
        var extra = ProductExtra.Parse(product.ExtraJson);
        ProductClassificationHelper.FillMissing(product.Name, ref group, extra);
        var factor = packFactor > 1 ? packFactor
            : extra.FatorEmbalagem > 1 ? extra.FatorEmbalagem
            : extra.QtdAtacado > 1 ? extra.QtdAtacado : 1;

        if (ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group) && factor >= 2)
        {
            return ProductPriceHelper.ResolveCatalogSale(
                0, unitCost, factor, product.Name, group, defaultMargin);
        }

        return ProductPriceHelper.SaleFromCostAndMargin(unitCost, defaultMargin);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SupplierOption
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? CpfCnpj { get; init; }
    public string? State { get; init; }
    public string DisplayLabel => string.IsNullOrWhiteSpace(CpfCnpj) ? Name : $"{Name} — {CpfCnpj}";
}

public sealed class ProductOption
{
    public int Id { get; init; }
    public string? Code { get; init; }
    public string? Barcode { get; init; }
    public string Name { get; init; } = "";
    public double CostPrice { get; init; }
    public double SalePrice { get; init; }
    public string DisplayLabel => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} — {Name}";
}

public partial class PurchaseFormWindow : Window
{
    /// <summary>Quando a compra gera títulos a pagar, a UI principal pode abrir Contas a Pagar.</summary>
    public int? OpenPayablesForPurchaseId { get; private set; }

    private enum ItemEntryMode { Idle, Adding, Editing }

    private readonly int? _purchaseId;
    private readonly bool _readOnly;
    private readonly ObservableCollection<PurchaseItemDraft> _items = [];
    private IReadOnlyList<ProductOption> _products = [];
    private IReadOnlyList<SupplierOption> _suppliers = [];
    private ItemEntryMode _itemMode = ItemEntryMode.Idle;
    private PurchaseItemDraft? _editingItem;
    private bool _suppressPriceCalc;
    private bool _uiReady;
    private bool _suppressComboFilter;
    private bool _comboTypingGuard;
    private bool _suppressNfeKeyLookup;
    private string? _lastNfeKeyLookup;
    private string? _lastXmlPath;
    private bool _fromNfeXml;
    private bool _nfeLookupBusy;

    public PurchaseFormWindow(int? purchaseId, bool readOnly = false)
    {
        _purchaseId = purchaseId;
        _readOnly = readOnly;
        try
        {
            InitializeComponent();
            InputUxHelper.Attach(this, BarcodeBox, EanBox, ItemCodeBox, NfeKeyBox);
            SupplierBox.DropDownOpened += EditableCombo_DropDownOpened;
            ProductBox.DropDownOpened += EditableCombo_DropDownOpened;
            SupplierBox.LostFocus += EditableCombo_LostFocus;
            ProductBox.LostFocus += EditableCombo_LostFocus;
            SupplierBox.AddHandler(TextCompositionManager.PreviewTextInputEvent,
                new TextCompositionEventHandler(EditableCombo_PreviewTextInput), true);
            ProductBox.AddHandler(TextCompositionManager.PreviewTextInputEvent,
                new TextCompositionEventHandler(EditableCombo_PreviewTextInput), true);
            _uiReady = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erro ao abrir a janela de compra:\n\n{ex.Message}",
                "Compras",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }

        ItemsGrid.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => UpdateTotals();
        Loaded += Window_Loaded;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadLookups();
            EmissionDateBox.Text = DateBrHelper.TodayBr();
            EntryDateBox.Text = DateBrHelper.TodayBr();
            ClearItemEntry();

            if (_purchaseId is int id)
                LoadPurchase(id);

            ApplyReadOnlyMode();
            UpdateItemButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erro ao carregar a compra:\n\n{ex.Message}",
                "Compras",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void LoadLookups()
    {
        _suppliers = PersonService.List(null, "ativos", "fornecedores")
            .Select(p => new SupplierOption
            {
                Id = p.Id,
                Name = p.Name,
                CpfCnpj = p.CpfCnpj,
                State = p.State,
            })
            .ToList();
        SupplierBox.ItemsSource = _suppliers;

        _products = ProductService.List(null, "ativos")
            .Select(p => new ProductOption
            {
                Id = p.Id,
                Code = p.Code,
                Barcode = p.Barcode,
                Name = p.Name,
                CostPrice = p.CostPrice,
                SalePrice = p.SalePrice,
            })
            .ToList();
        ProductBox.ItemsSource = _products;

        if (_suppliers.Count == 0 && !_readOnly && _purchaseId is null)
        {
            StatusText.Text = "Nenhum fornecedor cadastrado. Use F5 | Clientes e marque Fornecedor.";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
        }
    }

    private void LoadPurchase(int id)
    {
        var purchase = PurchaseService.GetById(id);
        if (purchase is null)
        {
            MessageBox.Show("Compra não encontrada.", "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        TitleText.Text = purchase.Status == "aberta" && !_readOnly
            ? $"Compras — alterar #{purchase.Id}"
            : $"Compras — visualizar #{purchase.Id}";

        SelectSupplier(purchase.SupplierId);
        SetNfeKeyText(purchase.NfeKey ?? "", markLookupDone: true);
        NumberBox.Text = purchase.Number;
        SeriesBox.Text = purchase.Series;
        EmissionDateBox.Text = purchase.EmissionDateDisplay;
        EntryDateBox.Text = purchase.EntryDateDisplay;
        GerarEstoqueBox.IsChecked = purchase.GerarEstoque;

        _items.Clear();
        var seq = 1;
        foreach (var item in purchase.Items)
        {
            var product = ProductService.GetById(item.ProductId);
            var draft = new PurchaseItemDraft
            {
                Seq = seq++,
                ProductId = item.ProductId,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                PrevCost = product is not null
                    ? NormalizePrevUnitCost(product, item.UnitPrice)
                    : item.UnitPrice,
                PrevSale = product?.SalePrice ?? 0,
            };
            if (product is not null)
            {
                PurchaseItemDraft.FillPackMeta(draft, product);
                var saleGrid = PurchaseItemDraft.ResolveGridSalePrice(
                    product, item.UnitPrice, 30, draft.PackFactor);
                draft.SalePrice = saleGrid;
                draft.SuggestedPrice = saleGrid;
                draft.Margin = ProductPriceHelper.MarginOnSale(draft.CatalogCost, saleGrid);
            }
            else
            {
                draft.SalePrice = 0;
                draft.SuggestedPrice = 0;
                draft.Margin = 0;
            }
            _items.Add(draft);
        }

        if (purchase.Status != "aberta")
        {
            StatusText.Text = $"Status: {purchase.StatusDisplay} — somente visualização.";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
        }

        UpdateTotals();
    }

    private void ApplyReadOnlyMode()
    {
        if (!_readOnly && _purchaseId is int id)
        {
            var purchase = PurchaseService.GetById(id);
            if (purchase?.Status != "aberta")
                SetReadOnly(true);
            return;
        }

        if (_readOnly)
            SetReadOnly(true);
    }

    private void SetReadOnly(bool readOnly)
    {
        SupplierBox.IsEnabled = !readOnly;
        NfeKeyBox.IsReadOnly = readOnly;
        NumberBox.IsReadOnly = readOnly;
        ModelBox.IsReadOnly = readOnly;
        SeriesBox.IsReadOnly = readOnly;
        EmissionDateBox.IsReadOnly = readOnly;
        EntryDateBox.IsReadOnly = readOnly;
        ItemEntryPanel.IsEnabled = !readOnly;
        BarcodeBox.IsReadOnly = readOnly;
        AjustaPrecoBox.IsEnabled = !readOnly;
        IncluirStCustoBox.IsEnabled = !readOnly;
        MargemGeralBox.IsEnabled = !readOnly;
        GerarFinanceiroBox.IsEnabled = !readOnly;
        GerarEstoqueBox.IsEnabled = !readOnly;
        BtnFinalize.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyMarginAll_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            MessageBox.Show("Não há itens na compra.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var margin = ProductPriceHelper.ParseBr(MargemGeralBox.Text);
        if (margin <= 0 || margin >= 100)
        {
            MessageBox.Show("Informe uma margem entre 0 e 100 (ex.: 30).", "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
            MargemGeralBox.Focus();
            return;
        }

        foreach (var item in _items)
            item.ApplyMarginPercent(margin);

        StatusText.Text =
            $"Margem {margin:N0}% aplicada em todos. Depois altere Margem ou Pr. Venda na grade nos itens diferentes.";
        StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
    }

    private void ItemsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;
        if (e.Row.Item is not PurchaseItemDraft item)
            return;
        if (e.EditingElement is not TextBox tb)
            return;

        var header = e.Column.Header?.ToString() ?? "";
        var value = ProductPriceHelper.ParseBr(tb.Text);

        if (header.StartsWith("Margem", StringComparison.OrdinalIgnoreCase))
        {
            item.ApplyMarginPercent(value);
            tb.Text = item.MarginDisplay;
        }
        else if (header.Contains("Venda", StringComparison.OrdinalIgnoreCase))
        {
            item.ApplySalePrice(value);
            tb.Text = item.SalePriceDisplay;
        }
        else if (header.StartsWith("Preço", StringComparison.OrdinalIgnoreCase))
        {
            // Aplica na hora (o binding do DataGrid pode gravar depois do evento).
            item.UnitPriceDisplay = tb.Text;
            item.UnitPriceWithoutSt = item.UnitPrice;
            item.UnitPriceWithSt = item.UnitPrice;
            tb.Text = item.UnitPriceDisplay;
        }
        else if (header.Contains("Qtd", StringComparison.OrdinalIgnoreCase))
        {
            item.QuantityDisplay = tb.Text;
            tb.Text = item.QuantityDisplay;
        }

        // Atualiza Subtotal/Total do rodapé após a grade commitir.
        Dispatcher.BeginInvoke(new Action(UpdateTotals), DispatcherPriority.Background);
    }

    private void SelectSupplier(int supplierId)
    {
        var supplier = _suppliers.FirstOrDefault(s => s.Id == supplierId);
        if (supplier is null)
        {
            // Pode ter acabado de ser criado — recarrega fornecedores
            LoadLookups();
            supplier = _suppliers.FirstOrDefault(s => s.Id == supplierId);
            if (supplier is null)
                return;
        }

        SupplierBox.ItemsSource = _suppliers;
        SupplierBox.SelectedItem = supplier;
        SupplierCodeBox.Text = supplier.Id.ToString();
        SupplierUfBox.Text = supplier.State ?? "";
        SupplierDocBox.Text = supplier.CpfCnpj ?? "";

        // Combo editável às vezes limpa o Text ao trocar ItemsSource — força o nome
        Dispatcher.BeginInvoke(() =>
        {
            if (!_uiReady) return;
            var s = _suppliers.FirstOrDefault(x => x.Id == supplierId);
            if (s is null) return;
            SupplierBox.SelectedItem = s;
            SupplierBox.Text = s.DisplayLabel;
            SupplierCodeBox.Text = s.Id.ToString();
            SupplierUfBox.Text = s.State ?? "";
            SupplierDocBox.Text = s.CpfCnpj ?? "";
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private int? GetSelectedSupplierId()
    {
        if (SupplierBox.SelectedItem is SupplierOption selected)
            return selected.Id;

        if (int.TryParse(SupplierCodeBox.Text?.Trim(), out var code) && code > 0
            && _suppliers.Any(s => s.Id == code))
            return code;

        var docDigits = TextNorm.DigitsOnly(SupplierDocBox.Text);
        if (!string.IsNullOrEmpty(docDigits))
        {
            var byDoc = _suppliers.FirstOrDefault(s =>
                TextNorm.DigitsOnly(s.CpfCnpj) == docDigits);
            if (byDoc is not null)
                return byDoc.Id;
        }

        var text = SupplierBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
            return null;

        // Só aceita match exato — Contains pegava o fornecedor errado com facilidade
        var exact = _suppliers.FirstOrDefault(s =>
            s.DisplayLabel.Equals(text, StringComparison.OrdinalIgnoreCase)
            || s.Name.Equals(text, StringComparison.OrdinalIgnoreCase));
        return exact?.Id;
    }

    private string? GetSelectedSupplierCnpj()
    {
        if (SupplierBox.SelectedItem is SupplierOption selected)
            return selected.CpfCnpj;
        var fromBox = SupplierDocBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(fromBox))
            return fromBox;
        if (GetSelectedSupplierId() is int id)
            return _suppliers.FirstOrDefault(s => s.Id == id)?.CpfCnpj;
        return null;
    }

    private void SupplierBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressComboFilter)
            return;
        if (SupplierBox.SelectedItem is not SupplierOption s)
            return;
        SupplierCodeBox.Text = s.Id.ToString();
        SupplierUfBox.Text = s.State ?? "";
        SupplierDocBox.Text = s.CpfCnpj ?? "";
    }

    private void SupplierBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up or Key.Enter or Key.Tab or Key.Escape)
            return;

        ApplyComboFilter(
            SupplierBox,
            SupplierBox.Text ?? "",
            text => string.IsNullOrWhiteSpace(text)
                ? _suppliers
                : _suppliers.Where(s =>
                    s.DisplayLabel.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || s.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || (s.CpfCnpj?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)));
    }

    private void SupplierBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Down or Key.Up))
            return;

        EnsureSupplierDropdownForNavigation();
        if (!NavigateComboSelection(SupplierBox, e.Key == Key.Down))
            return;
        e.Handled = true;
    }

    private void EnsureSupplierDropdownForNavigation()
    {
        if (SupplierBox.IsDropDownOpen && SupplierBox.Items.Count > 0)
            return;

        var text = SupplierBox.Text ?? "";
        ApplyComboFilter(
            SupplierBox,
            text,
            t => string.IsNullOrWhiteSpace(t)
                ? _suppliers
                : _suppliers.Where(s =>
                    s.DisplayLabel.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || s.Name.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || (s.CpfCnpj?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)),
            openIfEmpty: true);
    }

    private void ProductBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressComboFilter)
            return;
        if (ProductBox.SelectedItem is ProductOption p)
            ApplyProductToEntry(p);
    }

    private void ProductBox_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up or Key.Enter or Key.Tab or Key.Escape)
            return;

        ApplyComboFilter(
            ProductBox,
            ProductBox.Text ?? "",
            text => string.IsNullOrWhiteSpace(text)
                ? _products
                : FilterProducts(text).Take(100));
    }

    private void ProductBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up)
        {
            EnsureProductDropdownForNavigation();
            if (!NavigateComboSelection(ProductBox, e.Key == Key.Down))
                return;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && ProductBox.IsDropDownOpen && ProductBox.SelectedItem is ProductOption p)
        {
            ApplyProductToEntry(p);
            ProductBox.IsDropDownOpen = false;
            QtyBox.Focus();
            QtyBox.SelectAll();
            e.Handled = true;
        }
    }

    private void EnsureProductDropdownForNavigation()
    {
        if (ProductBox.IsDropDownOpen && ProductBox.Items.Count > 0)
            return;

        var text = ProductBox.Text ?? "";
        ApplyComboFilter(
            ProductBox,
            text,
            t => string.IsNullOrWhiteSpace(t)
                ? _products.Take(100)
                : FilterProducts(t).Take(100),
            openIfEmpty: true);
    }

    /// <summary>
    /// Filtra o ComboBox editável sem apagar o texto digitado (bug clássico do WPF ao trocar ItemsSource).
    /// </summary>
    private void ApplyComboFilter(
        ComboBox box,
        string typedText,
        Func<string, IEnumerable<object>> filter,
        bool openIfEmpty = false)
    {
        var caret = GetComboCaretIndex(box);
        if (caret < 0 || caret > typedText.Length)
            caret = typedText.Length;
        var filtered = filter(typedText).ToList();

        _suppressComboFilter = true;
        _comboTypingGuard = true;
        try
        {
            box.ItemsSource = filtered;
            box.SelectedIndex = -1;
            RestoreEditableText(box, typedText, caret);

            var shouldOpen = filtered.Count > 0 || openIfEmpty;
            if (shouldOpen)
                box.IsDropDownOpen = filtered.Count > 0;
        }
        finally
        {
            _suppressComboFilter = false;
        }

        // WPF seleciona todo o texto ao abrir a lista — desfaz depois do SelectAll interno.
        var textCopy = typedText;
        var caretCopy = caret;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            RestoreEditableText(box, textCopy, caretCopy));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            RestoreEditableText(box, textCopy, caretCopy));
    }

    private void EditableCombo_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox box)
            return;
        var text = box.Text ?? "";
        var caret = GetComboCaretIndex(box);
        if (caret < 0 || caret > text.Length)
            caret = text.Length;
        _comboTypingGuard = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            RestoreEditableText(box, text, caret));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            RestoreEditableText(box, text, caret));
    }

    /// <summary>
    /// Se o WPF ainda deixou tudo selecionado, a 2ª letra substituiria a 1ª — força caret no fim.
    /// </summary>
    private void EditableCombo_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_comboTypingGuard)
            return;
        var box = sender as ComboBox
            ?? FindParentComboBox(e.OriginalSource as DependencyObject);
        if (box is null)
            return;
        if (GetEditableTextBox(box) is not TextBox tb)
            return;
        if (tb.SelectionLength > 0 && tb.Text.Length > 0 && tb.SelectionLength >= tb.Text.Length)
        {
            var end = tb.Text.Length;
            tb.Select(end, 0);
        }
    }

    private void EditableCombo_LostFocus(object sender, RoutedEventArgs e) =>
        _comboTypingGuard = false;

    private static ComboBox? FindParentComboBox(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ComboBox cb)
                return cb;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static void RestoreEditableText(ComboBox box, string text, int caret)
    {
        box.ApplyTemplate();
        if (GetEditableTextBox(box) is not TextBox tb)
        {
            if (!string.Equals(box.Text, text, StringComparison.Ordinal))
                box.Text = text;
            return;
        }

        if (!string.Equals(tb.Text, text, StringComparison.Ordinal))
            tb.Text = text;

        caret = Math.Clamp(caret, 0, tb.Text?.Length ?? 0);
        tb.Select(caret, 0);
    }

    private static TextBox? GetEditableTextBox(ComboBox box)
    {
        box.ApplyTemplate();
        return box.Template?.FindName("PART_EditableTextBox", box) as TextBox;
    }

    private static int GetComboCaretIndex(ComboBox box)
    {
        if (GetEditableTextBox(box) is TextBox tb)
            return tb.CaretIndex;
        return box.Text?.Length ?? 0;
    }

    private static bool NavigateComboSelection(ComboBox box, bool moveDown)
    {
        var count = box.Items.Count;
        if (count == 0)
            return false;

        box.IsDropDownOpen = true;
        var idx = box.SelectedIndex;
        if (moveDown)
            box.SelectedIndex = idx < 0 ? 0 : Math.Min(count - 1, idx + 1);
        else
            box.SelectedIndex = idx <= 0 ? 0 : idx - 1;
        return true;
    }

    private IEnumerable<ProductOption> FilterProducts(string text) =>
        _products.Where(p =>
            p.DisplayLabel.Contains(text, StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (p.Code?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || (p.Barcode?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));

    private void ApplyProductToEntry(ProductOption product)
    {
        EanBox.Text = product.Barcode ?? "";
        ItemCodeBox.Text = product.Code ?? "";
        // Evita fechar/refiltrar a lista ao navegar com as setas
        if (!string.Equals(ProductBox.Text, product.DisplayLabel, StringComparison.Ordinal))
            ProductBox.Text = product.DisplayLabel;

        _suppressPriceCalc = true;
        PrevCostBox.Text = ProductPriceHelper.FormatBr(product.CostPrice);
        PrevSaleBox.Text = ProductPriceHelper.FormatBr(product.SalePrice);
        PriceBox.Text = ProductPriceHelper.FormatBr(product.CostPrice);
        SalePriceBox.Text = ProductPriceHelper.FormatBr(product.SalePrice);
        var margin = ProductPriceHelper.MarginOnSale(product.CostPrice, product.SalePrice);
        MarginBox.Text = ProductPriceHelper.FormatBr(margin);
        SuggestedPriceBox.Text = ProductPriceHelper.FormatBr(
            ProductPriceHelper.SaleFromCostAndMargin(ProductPriceHelper.ParseBr(PriceBox.Text), margin));
        _suppressPriceCalc = false;
        UpdateLineTotal();
    }

    private ProductOption? ResolveSelectedProduct()
    {
        if (ProductBox.SelectedItem is ProductOption selected)
            return selected;

        var text = ProductBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
            return null;

        return FilterProducts(text).FirstOrDefault();
    }

    private ProductOption? FindProductByBarcodeOrCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var v = value.Trim();
        return _products.FirstOrDefault(p =>
            string.Equals(p.Barcode, v, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Code, v, StringComparison.OrdinalIgnoreCase));
    }

    private void EanBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TryFindProductFromFields();
    }

    private void EanBox_LostFocus(object sender, RoutedEventArgs e) => TryFindProductFromFields();

    private void ItemCodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            TryFindProductFromFields();
    }

    private void ItemCodeBox_LostFocus(object sender, RoutedEventArgs e) => TryFindProductFromFields();

    private void TryFindProductFromFields()
    {
        var product = FindProductByBarcodeOrCode(EanBox.Text)
            ?? FindProductByBarcodeOrCode(ItemCodeBox.Text);
        if (product is not null)
            ApplyProductToEntry(product);
    }

    private void BarcodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        var product = FindProductByBarcodeOrCode(BarcodeBox.Text);
        if (product is null)
        {
            MessageBox.Show("Produto não encontrado.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ItemAdd_Click(sender, e);
        ApplyProductToEntry(product);
        QtyBox.Text = "1,00";
        ItemSave_Click(sender, e);
        BarcodeBox.Text = "";
        BarcodeBox.Focus();
        e.Handled = true;
    }

    private void ItemPriceFields_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || _suppressPriceCalc)
            return;
        UpdateLineTotal();
        if (sender == PriceBox)
            RecalcKeepSaleFromCost();
    }

    private void LineTotalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || _suppressPriceCalc)
            return;

        var qty = ProductPriceHelper.ParseBr(QtyBox.Text);
        var total = ProductPriceHelper.ParseBr(LineTotalBox.Text);
        if (qty <= 0)
            return;

        _suppressPriceCalc = true;
        PriceBox.Text = ProductPriceHelper.FormatBr(total / qty);
        _suppressPriceCalc = false;
        RecalcKeepSaleFromCost();
    }

    private void MarginBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || _suppressPriceCalc)
            return;
        RecalcSaleFromMargin();
    }

    private void SalePriceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || _suppressPriceCalc)
            return;
        var cost = ProductPriceHelper.ParseBr(PriceBox.Text);
        var sale = ProductPriceHelper.ParseBr(SalePriceBox.Text);
        MarginBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.MarginOnSale(cost, sale));
        SuggestedPriceBox.Text = SalePriceBox.Text;
    }

    /// <summary>
    /// Ao mudar o custo: mantém o último Pr. Venda e recalcula só a margem %.
    /// </summary>
    private void RecalcKeepSaleFromCost()
    {
        if (SuggestedPriceBox is null || SalePriceBox is null || PriceBox is null || MarginBox is null || PrevSaleBox is null)
            return;

        var cost = ProductPriceHelper.ParseBr(PriceBox.Text);
        var sale = ProductPriceHelper.ParseBr(SalePriceBox.Text);
        if (sale <= 0)
            sale = ProductPriceHelper.ParseBr(PrevSaleBox.Text);

        if (sale <= 0)
        {
            RecalcSaleFromMargin();
            return;
        }

        _suppressPriceCalc = true;
        SalePriceBox.Text = ProductPriceHelper.FormatBr(sale);
        SuggestedPriceBox.Text = ProductPriceHelper.FormatBr(sale);
        MarginBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.MarginOnSale(cost, sale));
        _suppressPriceCalc = false;
    }

    /// <summary>Ao mudar a margem %: recalcula Pr. Venda sugerido.</summary>
    private void RecalcSaleFromMargin()
    {
        if (SuggestedPriceBox is null || SalePriceBox is null || PriceBox is null || MarginBox is null)
            return;
        var cost = ProductPriceHelper.ParseBr(PriceBox.Text);
        var margin = ProductPriceHelper.ParseBr(MarginBox.Text);
        var suggested = ProductPriceHelper.SaleFromCostAndMargin(cost, margin);
        _suppressPriceCalc = true;
        SuggestedPriceBox.Text = ProductPriceHelper.FormatBr(suggested);
        SalePriceBox.Text = ProductPriceHelper.FormatBr(suggested);
        _suppressPriceCalc = false;
    }

    private void UpdateLineTotal()
    {
        if (LineTotalBox is null || QtyBox is null || PriceBox is null)
            return;
        var qty = ProductPriceHelper.ParseBr(QtyBox.Text);
        var price = ProductPriceHelper.ParseBr(PriceBox.Text);
        _suppressPriceCalc = true;
        LineTotalBox.Text = ProductPriceHelper.FormatBr(qty * price);
        _suppressPriceCalc = false;
    }

    private void ItemAdd_Click(object sender, RoutedEventArgs e)
    {
        _itemMode = ItemEntryMode.Adding;
        _editingItem = null;
        ClearItemEntry();
        ProductBox.Focus();
        UpdateItemButtons();
    }

    private void ItemEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsGrid.SelectedItem is not PurchaseItemDraft item)
        {
            MessageBox.Show("Selecione um item na grade.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _itemMode = ItemEntryMode.Editing;
        _editingItem = item;
        LoadItemToEntry(item);
        UpdateItemButtons();
    }

    private void ItemDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsGrid.SelectedItem is not PurchaseItemDraft item)
        {
            MessageBox.Show("Selecione um item para excluir.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _items.Remove(item);
        RenumberItems();
        ClearItemEntry();
        _itemMode = ItemEntryMode.Idle;
        UpdateItemButtons();
    }

    private void ItemSave_Click(object sender, RoutedEventArgs e)
    {
        var product = ResolveSelectedProduct();
        if (product is null)
        {
            MessageBox.Show("Selecione ou informe o produto.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var qty = ProductPriceHelper.ParseBr(QtyBox.Text);
        var price = ProductPriceHelper.ParseBr(PriceBox.Text);
        if (qty <= 0)
        {
            MessageBox.Show("Quantidade inválida.", "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var draft = new PurchaseItemDraft
        {
            ProductId = product.Id,
            Barcode = product.Barcode,
            ProductCode = product.Code ?? "",
            ProductName = product.Name,
            Cfop = CfopBox.Text.Trim(),
            Cst = CstBox.Text.Trim(),
            Quantity = qty,
            UnitPrice = price,
            PrevCost = ProductPriceHelper.ParseBr(PrevCostBox.Text),
            Margin = ProductPriceHelper.ParseBr(MarginBox.Text),
            SuggestedPrice = ProductPriceHelper.ParseBr(SuggestedPriceBox.Text),
            PrevSale = ProductPriceHelper.ParseBr(PrevSaleBox.Text),
            SalePrice = ProductPriceHelper.ParseBr(SalePriceBox.Text),
        };

        if (_itemMode == ItemEntryMode.Editing && _editingItem is not null)
        {
            var idx = _items.IndexOf(_editingItem);
            if (idx >= 0)
            {
                draft.Seq = _editingItem.Seq;
                _items.RemoveAt(idx);
                _items.Insert(idx, draft);
            }
        }
        else
        {
            draft.Seq = _items.Count + 1;
            _items.Add(draft);
        }

        ClearItemEntry();
        _itemMode = ItemEntryMode.Idle;
        _editingItem = null;
        UpdateItemButtons();
        UpdateTotals();
    }

    private void ItemCancel_Click(object sender, RoutedEventArgs e)
    {
        ClearItemEntry();
        _itemMode = ItemEntryMode.Idle;
        _editingItem = null;
        UpdateItemButtons();
    }

    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady)
            return;
        if (_itemMode == ItemEntryMode.Idle && ItemsGrid.SelectedItem is PurchaseItemDraft item)
            ItemSeqBox.Text = item.Seq.ToString();
    }

    private void LoadItemToEntry(PurchaseItemDraft item)
    {
        var product = _products.FirstOrDefault(p => p.Id == item.ProductId);
        if (product is not null)
            ApplyProductToEntry(product);

        _suppressPriceCalc = true;
        EanBox.Text = item.Barcode ?? product?.Barcode ?? "";
        ItemCodeBox.Text = item.ProductCode;
        ProductBox.Text = item.ProductName;
        CfopBox.Text = item.Cfop;
        CstBox.Text = item.Cst;
        QtyBox.Text = ProductPriceHelper.FormatBr(item.Quantity);
        PriceBox.Text = ProductPriceHelper.FormatBr(item.UnitPrice);
        PrevCostBox.Text = ProductPriceHelper.FormatBr(item.PrevCost);
        MarginBox.Text = ProductPriceHelper.FormatBr(item.Margin);
        SuggestedPriceBox.Text = ProductPriceHelper.FormatBr(item.SuggestedPrice);
        PrevSaleBox.Text = ProductPriceHelper.FormatBr(item.PrevSale);
        SalePriceBox.Text = ProductPriceHelper.FormatBr(item.SalePrice);
        ItemSeqBox.Text = item.Seq.ToString();
        _suppressPriceCalc = false;
        UpdateLineTotal();
    }

    private void ClearItemEntry()
    {
        _suppressPriceCalc = true;
        EanBox.Text = "";
        ItemCodeBox.Text = "";
        ProductBox.Text = "";
        ProductBox.SelectedItem = null;
        CfopBox.Text = "";
        CstBox.Text = "";
        QtyBox.Text = "1,00";
        PriceBox.Text = "0,00";
        LineTotalBox.Text = "0,00";
        PrevCostBox.Text = "0,00";
        MarginBox.Text = "0,00";
        SuggestedPriceBox.Text = "0,00";
        PrevSaleBox.Text = "0,00";
        SalePriceBox.Text = "0,00";
        BaseStBox.Text = "0,00";
        AliqStBox.Text = "0,00";
        ValorStBox.Text = "0,00";
        FcpBox.Text = "0,00";
        CsosnBox.Text = "";
        ItemSeqBox.Text = "0";
        _suppressPriceCalc = false;
    }

    private void RenumberItems()
    {
        var copy = _items.ToList();
        _items.Clear();
        var seq = 1;
        foreach (var item in copy)
        {
            item.Seq = seq++;
            _items.Add(item);
        }
    }

    private void UpdateItemButtons()
    {
        var editing = _itemMode != ItemEntryMode.Idle;
        BtnItemAdd.IsEnabled = !editing;
        BtnItemEdit.IsEnabled = !editing;
        BtnItemDelete.IsEnabled = !editing;
        BtnItemSave.IsEnabled = editing;
        BtnItemCancel.IsEnabled = editing;
    }

    private void UpdateTotals()
    {
        var total = _items.Sum(i => i.Subtotal);
        var formatted = ProductPriceHelper.FormatBr(total);
        SubtotalBox.Text = formatted;
        TotalText.Text = formatted;
    }

    private void NfeKeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = BuscarNfePelaChaveAsync(force: true);
    }

    private void BuscarNfe_Click(object sender, RoutedEventArgs e) =>
        _ = BuscarNfePelaChaveAsync(force: true);

    private void NfeKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressNfeKeyLookup || _readOnly || !_uiReady || _nfeLookupBusy) return;
        var key = DigitsOnly(NfeKeyBox.Text);
        if (key.Length != 44) return;
        _ = BuscarNfePelaChaveAsync(force: false);
    }

    private void SetNfeKeyText(string? text, bool markLookupDone)
    {
        _suppressNfeKeyLookup = true;
        try
        {
            NfeKeyBox.Text = text ?? "";
            if (markLookupDone)
            {
                var key = DigitsOnly(text);
                _lastNfeKeyLookup = key.Length == 44 ? key : null;
            }
        }
        finally
        {
            _suppressNfeKeyLookup = false;
        }
    }

    private async Task BuscarNfePelaChaveAsync(bool force)
    {
        if (_readOnly || _nfeLookupBusy) return;

        var key = DigitsOnly(NfeKeyBox.Text);
        if (key.Length != 44)
        {
            if (force)
            {
                MessageBox.Show(
                    "A chave deve ter 44 dígitos.\n\n" +
                    "Cole a chave completa ou use XML…",
                    "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        if (!force && key == _lastNfeKeyLookup)
            return;

        _lastNfeKeyLookup = key;
        if (NfeKeyBox.Text != key)
            SetNfeKeyText(key, markLookupDone: true);

        var found = FindXmlByChave(key);
        if (found is not null)
        {
            LoadNfeXmlFile(found);
            return;
        }

        if (!MeuDanfeNfeService.IsConfigured())
        {
            MessageBox.Show(
                "Para buscar a nota só com a chave (sem abrir o site):\n\n" +
                "1) Crie uma Api-Key em web.meudanfe.com.br → API / Integração\n" +
                "2) Cole em Dados da Empresa → Busca NF-e pela chave\n" +
                "3) Salve (F9) e cole a chave de novo aqui\n\n" +
                "Ou use o botão XML… se já tiver o arquivo.",
                "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _nfeLookupBusy = true;
        var prevCursor = Cursor;
        try
        {
            Cursor = Cursors.Wait;
            if (BtnBuscarNfe is not null)
                BtnBuscarNfe.IsEnabled = false;
            StatusText.Text = "Buscando NF-e pela chave…";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkSlateBlue;

            var xml = await MeuDanfeNfeService.FetchXmlByChaveAsync(key).ConfigureAwait(true);
            LoadNfeXmlContent(xml);
            StatusText.Text = "NF-e importada pela chave.";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Falha ao buscar NF-e pela chave.";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
            MessageBox.Show(
                ex.Message + "\n\nVocê ainda pode usar XML… se tiver o arquivo.",
                "Compras — busca NF-e",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = prevCursor;
            if (BtnBuscarNfe is not null)
                BtnBuscarNfe.IsEnabled = true;
            _nfeLookupBusy = false;
        }
    }

    private void LoadXml_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecionar XML da NF-e",
            Filter = "XML NF-e (*.xml)|*.xml|Todos (*.*)|*.*",
            InitialDirectory = Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        var key = DigitsOnly(NfeKeyBox.Text);
        if (key.Length == 44)
        {
            var hint = FindXmlByChave(key);
            if (hint is not null)
                dlg.FileName = hint;
        }

        if (dlg.ShowDialog() != true) return;
        LoadNfeXmlFile(dlg.FileName);
    }

    private static string DigitsOnly(string? text) =>
        new((text ?? "").Where(char.IsDigit).ToArray());

    private static string? FindXmlByChave(string chave44)
    {
        foreach (var folder in XmlSearchFolders())
        {
            if (!Directory.Exists(folder)) continue;

            try
            {
                var files = Directory.EnumerateFiles(folder, "*.xml")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(120);

                foreach (var path in files)
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.Contains(chave44, StringComparison.Ordinal))
                        return path;

                    if (XmlFileContainsChave(path, chave44))
                        return path;
                }
            }
            catch
            {
                // ignore folder access errors
            }
        }

        return null;
    }

    private static IEnumerable<string> XmlSearchFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(profile, "Downloads");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Path.Combine(profile, "Documents");
    }

    private static bool XmlFileContainsChave(string path, string chave44)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > 8_000_000)
                return false;

            var buffer = new byte[4096];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var read = fs.Read(buffer, 0, buffer.Length);
            var head = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            if (head.Contains(chave44, StringComparison.Ordinal))
                return true;

            if (info.Length <= 4096) return false;
            fs.Position = Math.Max(0, info.Length - 2048);
            read = fs.Read(buffer, 0, 2048);
            var tail = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            return tail.Contains(chave44, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void LoadNfeXmlFile(string path)
    {
        try
        {
            _lastXmlPath = path;
            var preview = NfeXmlImportService.ParseFile(
                path,
                includeIcmsStInCost: IncluirStCustoBox.IsChecked == true);
            ApplyNfePreview(preview);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Não foi possível ler o XML:\n\n" + ex.Message,
                "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadNfeXmlContent(string xml)
    {
        try
        {
            _lastXmlPath = null;
            var preview = NfeXmlImportService.ParseXml(
                xml,
                includeIcmsStInCost: IncluirStCustoBox.IsChecked == true);
            ApplyNfePreview(preview);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Não foi possível ler o XML da NF-e:\n\n" + ex.Message,
                "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyNfePreview(NfeImportPreview preview)
    {
        try
        {
            if (preview.Items.Count == 0)
                throw new InvalidOperationException("XML sem itens.");

            if (_items.Count > 0)
            {
                if (MessageBox.Show(
                        "A compra já tem itens. Substituir pelos itens do XML?",
                        "Compras", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            _fromNfeXml = true;

            // Fornecedor antes de criar produtos (evita misturar erros de people × products)
            int supplierId;
            try
            {
                supplierId = NfeXmlImportService.EnsureSupplierId(preview);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Não foi possível cadastrar/vincular o fornecedor da NF:\n\n" + ex.Message,
                    "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Revalida vínculos (nome sanitizado / EAN) antes de perguntar sobre criar
            foreach (var it in preview.Items.Where(i => i.MatchedProductId is null))
            {
                var resolved = NfeXmlImportService.ResolveExistingProduct(it);
                if (resolved is null) continue;
                it.MatchedProductId = resolved.Id;
                it.MatchedProductName = resolved.Name;
            }
            var matchedCount = preview.Items.Count(i => i.MatchedProductId is not null);
            var missing = preview.Items.Where(i => i.MatchedProductId is null).ToList();
            var missingCount = missing.Count;

            var createMissing = false;
            if (missingCount > 0)
            {
                var sample = string.Join("\n", missing.Take(8).Select(i => "• " + i.Name));
                if (missingCount > 8)
                    sample += $"\n• … e mais {missingCount - 8}";

                var ask = MessageBox.Show(
                    $"{matchedCount} de {preview.Items.Count} itens já batem com o cadastro.\n" +
                    $"Nos {missingCount} restantes: criar produtos novos?\n\n" +
                    "Recomendado: Não — se o produto já existe na loja, corrija o código de barras no cadastro e importe de novo.\n\n" +
                    $"Não encontrados:\n{sample}\n\n" +
                    "Sim = criar só os que faltam\nNão = cancelar importação\nCancelar = abortar",
                    "Compras — XML",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (ask == MessageBoxResult.Cancel || ask == MessageBoxResult.No)
                {
                    if (ask == MessageBoxResult.No)
                    {
                        MessageBox.Show(
                            "Importação cancelada para não criar produtos duplicados.\n\n" +
                            "Itens sem vínculo:\n" + sample,
                            "Compras",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    return;
                }

                createMissing = true;
            }

            SetNfeKeyText(preview.Chave, markLookupDone: true);
            NumberBox.Text = preview.Numero;
            SeriesBox.Text = preview.Serie;
            ModelBox.Text = "55";
            if (!string.IsNullOrWhiteSpace(preview.EmissionDisplay))
                EmissionDateBox.Text = preview.EmissionDisplay;
            EntryDateBox.Text = DateBrHelper.TodayBr();

            LoadLookups();
            SelectSupplier(supplierId);

            _items.Clear();
            var created = 0;
            var seq = 1;
            var defaultMargin = ProductPriceHelper.ParseBr(MargemGeralBox.Text);
            if (defaultMargin <= 0 || defaultMargin >= 100)
                defaultMargin = 30;
            AjustaPrecoBox.IsChecked = false;

            foreach (var item in preview.Items)
            {
                Product? product = null;
                if (item.MatchedProductId is int mid)
                    product = ProductService.GetById(mid);
                product ??= NfeXmlImportService.ResolveExistingProduct(item);

                var saleComMargem = ProductPriceHelper.SaleFromCostAndMargin(item.UnitPrice, defaultMargin);

                if (product is null)
                {
                    if (!createMissing)
                        throw new InvalidOperationException(
                            $"Produto não cadastrado: {item.Name}. Cadastre-o ou permita criar automaticamente.");

                    var catalogName = ProductClassificationHelper.SanitizeProductName(item.Name);
                    var newPackFactor = item.PackFactor > 1 ? item.PackFactor : 1;
                    var sale = item.SalePrice > 0 ? item.SalePrice : saleComMargem;
                    var inferred = ProductClassificationHelper.Infer(catalogName);
                    var isCigPack = ProductClassificationHelper.UsesPackPurchasePrice(catalogName, inferred.Group);
                    if (isCigPack)
                        newPackFactor = ProductPriceHelper.ResolveCigarettesPerPack(catalogName, newPackFactor);
                    var lineTotal = ProductPriceCalculator.RoundPrice(item.Quantity * item.UnitPrice);
                    var costStore = ProductPriceHelper.ResolveCatalogCost(
                        item.UnitPrice, newPackFactor, catalogName, inferred.Group,
                        lineTotal, item.Quantity);
                    var saleStore = ProductPriceHelper.ResolveCatalogSale(
                        item.SalePrice > 0 ? item.SalePrice : saleComMargem,
                        item.UnitPrice, newPackFactor, catalogName, inferred.Group);

                    // Trava: nunca cria se o EAN/fardo já existir
                    product = ProductService.FindByBarcodeOrPack(item.Barcode)
                        ?? ProductService.FindByBarcodeOrPack(item.PackBarcode);
                    if (product is null)
                    {
                        product = ProductService.Create(new ProductInput
                        {
                            Barcode = item.Barcode ?? item.PackBarcode,
                            Name = catalogName,
                            GroupName = inferred.Group,
                            Unit = "UN",
                            CostPrice = costStore,
                            SalePrice = saleStore,
                            Stock = 0,
                            Extra = new ProductExtra
                            {
                                Marca = inferred.Brand,
                                FatorEmbalagem = newPackFactor,
                                BarcodeEmbalagem = TextNorm.DistinctPackBarcode(item.PackBarcode, item.Barcode),
                                QtdAtacado = newPackFactor > 1 ? newPackFactor : 0,
                                PrecoAtacado = isCigPack ? saleStore : (newPackFactor > 1 ? ProductPriceCalculator.RoundPrice(saleStore * newPackFactor) : saleStore),
                                PrecoCompra = Math.Round(costStore, 4),
                                LucroPercent = saleStore > 0
                                    ? ProductPriceHelper.MarginOnSale(costStore, saleStore)
                                    : defaultMargin,
                                ControleValidade = ProductClassificationHelper.SuggestsExpiryControl(catalogName, inferred.Group),
                            },
                        });
                        created++;
                    }
                }
                else
                {
                    // Produto já existia: ainda assim limpa o nome se estiver sujo de embalagem.
                    product = ProductService.EnsureCleanCatalogName(product, item.Name);
                    product = EnsureProductClassification(product);
                }

                var prevUnitCost = NormalizePrevUnitCost(product, item.UnitPrice);
                var packFactor = ProductExtra.Parse(product.ExtraJson).FatorEmbalagem;
                if (packFactor < 2)
                {
                    if (item.PackFactor > 1)
                        packFactor = item.PackFactor;
                    else if (ProductClassificationHelper.UsesPackPurchasePrice(product.Name, product.GroupName))
                        packFactor = 20;
                    else
                        packFactor = 1;
                }

                var saleKeep = PurchaseItemDraft.ResolveGridSalePrice(
                    product, item.UnitPrice, defaultMargin, packFactor);
                var suggested = PurchaseItemDraft.ResolveSuggestedSalePrice(
                    product, item.UnitPrice, defaultMargin, packFactor);
                var qty = item.Quantity;
                var unit = item.UnitPrice;
                var unitNoSt = item.UnitPriceWithoutSt > 0 ? item.UnitPriceWithoutSt : item.UnitPrice;
                var unitSt = item.UnitPriceWithSt > 0 ? item.UnitPriceWithSt : item.UnitPrice;
                // Fecha o Total Item com o XML (centavos do rateio de ST/desconto).
                if (qty > 0 && item.TotalValue > 0.009)
                    unit = Math.Round(item.TotalValue / qty, 6);

                var draft = new PurchaseItemDraft
                {
                    Seq = seq++,
                    ProductId = product.Id,
                    Barcode = product.Barcode,
                    ProductCode = product.Code ?? "",
                    ProductName = product.Name,
                    Quantity = qty,
                    UnitPrice = unit,
                    UnitPriceWithoutSt = unitNoSt,
                    UnitPriceWithSt = unitSt,
                    PrevCost = prevUnitCost,
                    PrevSale = product.SalePrice,
                    SuggestedPrice = suggested,
                    SalePrice = saleKeep > 0 ? saleKeep : suggested,
                    LotNumber = item.LotNumber ?? "",
                    ExpiryDate = item.ExpiryDate,
                    HasXmlRastro = item.HasXmlRastro,
                };
                PurchaseItemDraft.FillPackMeta(draft, product);
                if (draft.PackFactor < 2 && item.PackFactor >= 2)
                    draft.PackFactor = item.PackFactor;
                draft.Margin = ProductPriceHelper.MarginOnSale(draft.CatalogCost, draft.SalePrice);
                _items.Add(draft);
            }

            UpdateTotals();
            LoadLookups();
            SelectSupplier(supplierId);

            var hasCig = _items.Any(i =>
                ProductClassificationHelper.UsesPackPurchasePrice(i.ProductName, i.GroupName));
            var negMargin = _items.Count(i => i.Margin < -0.01);
            var stDiffItems = _items
                .Where(i => Math.Abs(i.UnitPriceWithSt - i.UnitPriceWithoutSt) >= 0.009)
                .ToList();
            StatusText.Text =
                $"XML carregado: {preview.Items.Count} itens · Qtd/Preço = unitário (estoque)" +
                (hasCig ? " · Pr. Venda cigarro = maço" : "") +
                (created > 0 ? $" · {created} produto(s) novo(s)" : "") +
                (negMargin > 0 ? $" · {negMargin} com margem negativa" : "") +
                (stDiffItems.Count > 0 ? " · custo sem ICMS-ST" : "") +
                " · edite na grade se quiser";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;

            var msg =
                $"Nota carregada com {_items.Count} itens.\n\n" +
                "Na grade:\n" +
                "• Qtd e Preço = unitário da NF (bate com estoque e XML)\n" +
                "• Pr. Sugerido = custo novo com a margem padrão\n" +
                "• Pr. Venda = preço atual do cadastro (só muda se marcar a opção abaixo)\n";
            if (stDiffItems.Count > 0)
            {
                msg += "\nCusto sem ICMS-ST (padrão) × com ST:\n";
                foreach (var it in stDiffItems.Take(6))
                {
                    msg +=
                        $"• {it.ProductName}\n" +
                        $"  sem ST R$ {it.UnitPriceWithoutSt:N4} (total R$ {it.Quantity * it.UnitPriceWithoutSt:N2})\n" +
                        $"  com ST R$ {it.UnitPriceWithSt:N4} (total R$ {it.Quantity * it.UnitPriceWithSt:N2})\n";
                }
                if (stDiffItems.Count > 6)
                    msg += $"• … e mais {stDiffItems.Count - 6}\n";
                msg += "Marque “Incluir ICMS-ST no custo” se quiser o valor cheio.\n";
            }
            if (preview.HeaderSt > 0.05 || preview.FatLiq > 0.05 || preview.HeaderVNf > 0.05)
            {
                msg += "\nTotais da NF:";
                if (preview.HeaderVProd > 0.05) msg += $" produtos R$ {preview.HeaderVProd:N2}";
                if (preview.HeaderSt > 0.05) msg += $" · ST R$ {preview.HeaderSt:N2}";
                if (preview.HeaderDesc > 0.05) msg += $" · desc. R$ {preview.HeaderDesc:N2}";
                if (preview.HeaderVNf > 0.05) msg += $" · vNF R$ {preview.HeaderVNf:N2}";
                if (preview.FatLiq > 0.05) msg += $" · fatura líquida R$ {preview.FatLiq:N2}";
                msg += "\n";
            }
            if (hasCig)
            {
                msg +=
                    "\nCigarro: Pr. Venda/custo no cadastro usam o valor do maço.\n";
            }
            if (negMargin > 0)
            {
                msg +=
                    $"\nAtenção: {negMargin} item(ns) com margem negativa — " +
                    "o custo da NF ficou maior que o preço de venda atual. " +
                    "Ajuste o Pr. Venda ou use a margem % e Aplicar.\n";
            }
            msg +=
                "\nAo finalizar (F3):\n" +
                "• Atualiza preço de compra/custo\n" +
                "• Preço de venda só muda se “Também ajustar preço de venda” estiver marcado";

            MessageBox.Show(msg, "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Se o custo cadastrado ainda for do fardo (ex.: 30,00 no DP16), devolve o custo unitário.
    /// </summary>
    private static double NormalizePrevUnitCost(Product product, double nfUnitPrice)
    {
        var cost = product.CostPrice;
        if (cost <= 0)
            return nfUnitPrice;

        var extra = ProductExtra.Parse(product.ExtraJson);
        var group = product.GroupName;
        ProductClassificationHelper.FillMissing(product.Name, ref group, extra);

        // Cigarro: custo ant. sempre o maço do cadastro (não divide pelo fator).
        if (ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group))
            return ProductPriceHelper.RoundPrice(cost);

        var factor = extra.FatorEmbalagem > 1 ? extra.FatorEmbalagem
            : extra.QtdAtacado > 1 ? extra.QtdAtacado : 1;
        if (factor <= 1.0001)
            return cost;

        var packFromNf = nfUnitPrice * factor;
        var looksLikePack =
            cost > nfUnitPrice * 1.5
            && (
                Math.Abs(cost - packFromNf) / Math.Max(packFromNf, 0.01) < 0.25
                || (extra.PrecoCompra > 0 && Math.Abs(cost - extra.PrecoCompra) < 0.05)
            );

        return looksLikePack
            ? ProductPriceHelper.RoundPrice(cost / factor)
            : cost;
    }

    /// <summary>Completa marca/grupo vazios no produto já existente e salva no catálogo.</summary>
    private static Product EnsureProductClassification(Product product)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        var group = product.GroupName;
        var brandBefore = extra.Marca ?? "";
        var groupBefore = group ?? "";
        ProductClassificationHelper.FillMissing(product.Name, ref group, extra);
        if (string.Equals(brandBefore, extra.Marca ?? "", StringComparison.OrdinalIgnoreCase)
            && string.Equals(groupBefore, group ?? "", StringComparison.OrdinalIgnoreCase))
            return product;

        return ProductService.Update(product.Id, new ProductInput
        {
            Code = product.Code,
            Barcode = product.Barcode,
            Name = product.Name,
            GroupName = group,
            Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
            CostPrice = product.CostPrice,
            SalePrice = product.SalePrice,
            MinStock = product.MinStock,
            Stock = product.Stock,
            Location = product.Location,
            Extra = extra,
            Active = product.Active,
        });
    }

    private void IncluirStCustoBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _items.Count == 0)
            return;

        var withSt = IncluirStCustoBox.IsChecked == true;
        var changed = 0;
        foreach (var item in _items)
        {
            var noSt = item.UnitPriceWithoutSt;
            var st = item.UnitPriceWithSt;
            if (noSt <= 0 && st <= 0)
                continue;
            if (Math.Abs(noSt - st) < 0.009)
                continue;

            var newCost = withSt
                ? (st > 0 ? st : item.UnitPrice)
                : (noSt > 0 ? noSt : item.UnitPrice);
            if (Math.Abs(newCost - item.UnitPrice) < 0.009)
                continue;

            item.UnitPrice = newCost;
            // Mantém Pr. Venda; recalcula margem % e Pr. Sugerido (não sobrescreve sugerido com a venda).
            var keepSale = item.SalePrice > 0 ? item.SalePrice : item.PrevSale;
            var margemPadrao = ProductPriceHelper.ParseBr(MargemGeralBox.Text);
            if (margemPadrao <= 0 || margemPadrao >= 100)
                margemPadrao = 30;
            item.SuggestedPrice = ProductPriceHelper.SaleFromCostAndMargin(item.CatalogCost, margemPadrao);
            if (keepSale > 0)
            {
                item.SalePrice = keepSale;
                item.Margin = ProductPriceHelper.MarginOnSale(item.CatalogCost, keepSale);
            }
            else
                item.ApplyMarginPercent(margemPadrao);
            changed++;
        }

        UpdateTotals();
        if (changed > 0)
        {
            StatusText.Text = withSt
                ? $"ICMS-ST incluído no custo de {changed} item(ns)."
                : $"Custo sem ST (preço da nota) em {changed} item(ns).";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }
        else if (!string.IsNullOrWhiteSpace(_lastXmlPath))
        {
            StatusText.Text =
                "Nenhum item com ST diferente. Carregue o XML de novo (XML…) com a opção marcada/desmarcada.";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
        }
    }

    private void Finalize_Click(object sender, RoutedEventArgs e)
    {
        PurchaseFinanceiroMeta? financeiro = null;
        if (GerarFinanceiroBox.IsChecked == true)
        {
            if (!TryValidatePurchase(out var error))
            {
                MessageBox.Show(error, "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var total = _items.Sum(i => i.Subtotal);
            var dlg = new PurchaseParcelasWindow(total, EntryDateBox.Text) { Owner = this };
            if (dlg.ShowDialog() != true)
                return;
            financeiro = dlg.Result;
        }

        Save(closeOnSave: true, financeiro);
    }

    private bool TryValidatePurchase(out string error)
    {
        error = "";
        if (GetSelectedSupplierId() is not int supplierId || supplierId <= 0)
        {
            error = "Selecione o fornecedor.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(NumberBox.Text))
        {
            error = "Informe o número da nota.";
            return false;
        }

        if (_items.Count == 0)
        {
            error = "Adicione ao menos um item à compra.";
            return false;
        }

        return true;
    }

    private void Save(bool closeOnSave, PurchaseFinanceiroMeta? financeiro = null)
    {
        if (GetSelectedSupplierId() is not int supplierId || supplierId <= 0)
        {
            MessageBox.Show("Selecione o fornecedor.", "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
            SupplierBox.Focus();
            return;
        }

        var gerarEstoque = GerarEstoqueBox.IsChecked == true;
        if (closeOnSave && gerarEstoque && _fromNfeXml && _items.Count > 0)
        {
            var proxy = _items.Select(i => new NfeImportItem
            {
                Name = i.ProductName,
                Quantity = i.Quantity,
                LotNumber = i.LotNumber ?? "",
                ExpiryDate = i.ExpiryDate,
                HasXmlRastro = i.HasXmlRastro,
                MatchedProductId = i.ProductId > 0 ? i.ProductId : null,
            }).ToList();

            if (!NfeLotValidityWindow.ConfirmOrSkip(this, proxy))
                return;

            for (var i = 0; i < _items.Count && i < proxy.Count; i++)
            {
                _items[i].LotNumber = proxy[i].LotNumber ?? "";
                _items[i].ExpiryDate = proxy[i].ExpiryDate;
            }

            var missing = proxy
                .Where(p => NfeLotValidityWindow.ResolveRequiresExpiry(p) && p.NeedsManualExpiry)
                .ToList();
            if (missing.Count > 0)
            {
                MessageBox.Show(
                    "Falta Data de Validade nos produtos com controle de validade ativado.",
                    "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var emission = DateBrHelper.ToIso(EmissionDateBox.Text) ?? DateBrHelper.TodayIso();
        var entry = DateBrHelper.ToIso(EntryDateBox.Text) ?? emission;
        var notes = PurchaseFinanceHelper.AppendFinanceiroToNotes(null, financeiro);

        var input = new PurchaseInput
        {
            SupplierId = supplierId,
            SupplierCnpj = GetSelectedSupplierCnpj(),
            EmissionDate = emission,
            EntryDate = entry,
            Series = string.IsNullOrWhiteSpace(SeriesBox.Text) ? "1" : SeriesBox.Text.Trim(),
            Number = NumberBox.Text.Trim(),
            NfeKey = string.IsNullOrWhiteSpace(NfeKeyBox.Text) ? null : NfeKeyBox.Text.Trim(),
            GerarEstoque = gerarEstoque,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
            Items = _items.Select(i => new PurchaseItemInput
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LotNumber = i.LotNumber,
                ExpiryDate = i.ExpiryDate,
            }).ToList(),
        };

        try
        {
            int purchaseId;
            if (_purchaseId is int id)
            {
                PurchaseService.Update(id, input, closeOnSave);
                purchaseId = id;
            }
            else
            {
                purchaseId = PurchaseService.Create(input, closeOnSave);
            }

            if (closeOnSave)
                ApplyPurchasePricesToProducts(updateSale: AjustaPrecoBox.IsChecked == true);

            // Abre Contas a Pagar sempre que gerou financeiro (parcelas), com ou sem estoque
            if (closeOnSave && financeiro is not null)
                OpenPayablesForPurchaseId = purchaseId;

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Ao finalizar: atualiza Preço Compra (desta NF) e Preço Custo (média ponderada se gerou estoque).
    /// Preço Venda / maço só mudam se updateSale = true.
    /// Preço 0,00 é permitido (brinde/prêmio) e entra na média.
    /// </summary>
    private void ApplyPurchasePricesToProducts(bool updateSale)
    {
        var gerarEstoque = GerarEstoqueBox.IsChecked == true;

        foreach (var item in _items)
        {
            if (item.ProductId <= 0)
                continue;
            if (item.UnitPrice < 0)
                continue;

            var product = ProductService.GetById(item.ProductId);
            if (product is null)
                continue;

            var costUnit = Math.Round(item.UnitPrice, 4);
            var extra = ProductExtra.Parse(product.ExtraJson);
            var group = product.GroupName;
            ProductClassificationHelper.FillMissing(product.Name, ref group, extra);

            var packFactor = extra.FatorEmbalagem > 1 ? extra.FatorEmbalagem
                : extra.QtdAtacado > 1 ? extra.QtdAtacado : 1;
            if (packFactor > 1)
            {
                if (extra.QtdAtacado <= 1)
                    extra.QtdAtacado = packFactor;
                if (extra.FatorEmbalagem <= 1)
                    extra.FatorEmbalagem = packFactor;
            }

            var isCigPack = ProductClassificationHelper.UsesPackPurchasePrice(product.Name, group);
            var cigsPerPack = isCigPack
                ? ProductPriceHelper.ResolveCigarettesPerPack(product.Name, packFactor)
                : packFactor;
            if (isCigPack && cigsPerPack >= 2)
            {
                packFactor = cigsPerPack;
                extra.FatorEmbalagem = cigsPerPack;
                extra.QtdAtacado = cigsPerPack;
            }

            var lineTotal = ProductPriceCalculator.RoundPrice(item.Quantity * item.UnitPrice);
            var lineCost = ProductPriceHelper.ResolveCatalogCost(
                costUnit, packFactor, product.Name, group, lineTotal, item.Quantity);
            extra.PrecoCompra = lineCost; // custo desta NF (pode ser 0 = brinde)

            var sale = product.SalePrice;
            if (updateSale)
            {
                var newSale = item.SalePrice > 0 ? item.SalePrice : item.SuggestedPrice;
                if (newSale > 0)
                    sale = ProductPriceHelper.ResolveCatalogSale(
                        newSale, costUnit, packFactor, product.Name, group);

                if (packFactor > 1 && sale > 0)
                    extra.PrecoAtacado = isCigPack ? sale : ProductPriceCalculator.RoundPrice(sale * packFactor);
            }
            else if (isCigPack && sale > 0 && sale < 5 && packFactor >= 10)
            {
                // Venda ainda unitária no cadastro → promove a maço sem obrigar checkbox
                sale = ProductPriceCalculator.RoundPrice(sale * packFactor);
                if (extra.PrecoAtacado <= 0)
                    extra.PrecoAtacado = sale;
            }

            double costToStore;
            if (gerarEstoque)
            {
                // Estoque já foi somado em PurchaseService.ApplyStock
                var stockBefore = Math.Max(0, product.Stock - item.Quantity);
                if (isCigPack && cigsPerPack >= 2)
                {
                    var packsBefore = stockBefore / cigsPerPack;
                    var packsIn = item.Quantity / cigsPerPack;
                    costToStore = ProductPriceHelper.WeightedAverageCost(
                        packsBefore, product.CostPrice, packsIn, lineCost);
                }
                else
                {
                    costToStore = ProductPriceHelper.WeightedAverageCost(
                        stockBefore, product.CostPrice, item.Quantity, lineCost);
                }
            }
            else if (item.UnitPrice > 0)
            {
                // Sem movimentar estoque: mantém comportamento de “último custo” (não zera com brinde)
                costToStore = lineCost;
            }
            else
            {
                costToStore = product.CostPrice;
            }

            if (sale > 0 && costToStore > 0)
                extra.LucroPercent = ProductPriceHelper.MarginOnSale(costToStore, sale);

            ProductService.Update(product.Id, new ProductInput
            {
                Code = product.Code,
                Barcode = product.Barcode,
                Name = product.Name,
                GroupName = group,
                Unit = string.IsNullOrWhiteSpace(product.Unit) ? "UN" : product.Unit,
                CostPrice = costToStore,
                SalePrice = sale,
                MinStock = product.MinStock,
                Stock = product.Stock,
                Location = product.Location,
                Extra = extra,
                Active = product.Active,
            });
        }
    }

    private void OpenProducts_Click(object sender, RoutedEventArgs e)
    {
        var form = new ProductFormWindow(null) { Owner = this };
        if (form.ShowDialog() == true)
            ReloadProducts();
    }

    private void OpenClients_Click(object sender, RoutedEventArgs e)
    {
        var form = new PersonFormWindow(null) { Owner = this };
        if (form.ShowDialog() == true)
            ReloadSuppliers();
    }

    private void ReloadProducts()
    {
        _products = ProductService.List(null, "ativos")
            .Select(p => new ProductOption
            {
                Id = p.Id,
                Code = p.Code,
                Barcode = p.Barcode,
                Name = p.Name,
                CostPrice = p.CostPrice,
                SalePrice = p.SalePrice,
            })
            .ToList();
        ProductBox.ItemsSource = _products;
    }

    private void ReloadSuppliers()
    {
        _suppliers = PersonService.List(null, "ativos", "fornecedores")
            .Select(p => new SupplierOption
            {
                Id = p.Id,
                Name = p.Name,
                CpfCnpj = p.CpfCnpj,
                State = p.State,
            })
            .ToList();
        SupplierBox.ItemsSource = _suppliers;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_itemMode != ItemEntryMode.Idle)
                ItemCancel_Click(sender, e);
            else
                Cancel_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F11)
        {
            BarcodeBox.Focus();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.F3 when BtnFinalize.IsVisible:
                Finalize_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F4:
                OpenProducts_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F5:
                OpenClients_Click(sender, e);
                e.Handled = true;
                break;
        }
    }
}
