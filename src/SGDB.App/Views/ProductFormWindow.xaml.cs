using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class ProductFormWindow : Window
{
    private static readonly string[] TipoOptions =
    {
        "00-MERCADORIA PARA REVENDA",
        "01-MATERIA PRIMA",
        "02-EMBALAGEM",
        "03-PRODUTO EM PROCESSO",
        "04-PRODUTO ACABADO",
        "05-SUBPRODUTO",
        "06-PRODUTO INTERMEDIARIO",
        "07-MATERIAL DE USO/CONSUMO",
    };

    private readonly int? _productId;
    private readonly ObservableCollection<ProductCompositionItem> _composition = new();
    private DispatcherTimer? _compSuggestTimer;
    private bool _compSuggestSuppress;
    /// <summary>Cópia legado de extra_json.data_validade — preservada no save, nunca usada como validade operacional.</summary>
    private string? _legacyDataValidade;

    public ProductFormWindow(int? productId)
    {
        _productId = productId;
        InitializeComponent();
        InputUxHelper.Attach(this, CompSearchBox);
        CompGrid.ItemsSource = _composition;
        TipoBox.ItemsSource = TipoOptions;
        TipoBox.SelectedIndex = 0;
        LoadCatalogs();
        SetPriceDefaults();

        if (productId is null)
        {
            TitleText.Text = "Cadastro de Produtos — Novo";
            LastEntryBox.Text = "Sem entrada de compra";
            ControleValidadeBox.IsChecked = true;
            ProximaValidadeBox.Text = ProductExpiryService.UninformedDisplay;
        }
        else
            LoadProduct(productId.Value);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            MaxWidth = Math.Max(780, wa.Width * 0.96);
            MaxHeight = Math.Max(520, wa.Height * 0.94);
            if (ActualWidth > MaxWidth) Width = MaxWidth;
            if (Height <= 0 || double.IsNaN(Height))
                Height = Math.Min(720, MaxHeight);
            else if (Height > MaxHeight)
                Height = MaxHeight;
            // Centraliza na área útil
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
        }
        catch
        {
            // sizing best-effort
        }
    }

    private void SetPriceDefaults()
    {
        PrecoCompraBox.Text = CostBox.Text = SaleBox.Text = LucroBox.Text =
            QtdAtacadoBox.Text = PrecoAtacadoBox.Text = PrecoAvulsoBox.Text = ProductPriceHelper.FormatBr(0);
            FatorEmbalagemBox.Text = "1";
            PackBarcodeBox.Text = "";
        MinStockBox.Text = "5";
        StockBox.Text = ProductPriceHelper.FormatBr(0);
        StockFridgeBox.Text = ProductPriceHelper.FormatBr(0);
        StockFridgeMinBox.Text = "0";
        RefreshFridgeUi();
        RefreshPrecoAvulsoVisibility();
    }

    private void LoadCatalogs()
    {
        UnitBox.ItemsSource = ProductCatalogService.ListUnits();
        GroupBox.ItemsSource = ProductCatalogService.ListGroups();
        BrandBox.ItemsSource = ProductCatalogService.ListBrands();
        UnitBox.Text = "UN";

        var tables = new List<PriceTable> { new() { Id = 0, Description = "— Sem tabela —" } };
        tables.AddRange(PriceTablesService.List(onlyActive: true, includeProductCounts: false));
        PriceTableBox.ItemsSource = tables;
        PriceTableBox.SelectedIndex = 0;

        var tipos = new List<ContainerType> { new() { Id = 0, Name = "— Sem vasilhame —" } };
        tipos.AddRange(ContainerTypesService.List(onlyActive: true));
        VasilhameTipoBox.ItemsSource = tipos;
        VasilhameTipoBox.SelectedIndex = 0;
        VasilhameQtyBox.Text = "1";
    }

    private void LoadProduct(int id)
    {
        var product = ProductService.GetById(id);
        if (product is null)
        {
            MessageBox.Show("Produto não encontrado.", "Produtos", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        TitleText.Text = $"Cadastro de Produtos — {product.Name}";
        IdBox.Text = product.Id.ToString();
        CreatedBox.Text = FormatDate(product.CreatedAt);
        var lastEntry = PurchaseService.GetLastEntry(product.Id);
        LastEntryBox.Text = PurchaseService.FormatLastEntryLong(lastEntry, product.StockUnitLabel);
        NameBox.Text = product.Name;
        BarcodeBox.Text = product.Barcode ?? "";
        CodeBox.Text = product.Code ?? "";
        UnitBox.Text = product.Unit;
        GroupBox.Text = product.GroupName ?? "";
        LocationBox.Text = product.Location ?? "";
        CostBox.Text = ProductPriceHelper.FormatBr(product.CostPrice);
        SaleBox.Text = ProductPriceHelper.FormatBr(product.SalePrice);
        MinStockBox.Text = product.MinStock.ToString();
        StockBox.Text = ProductPriceHelper.FormatBr(product.Stock);
        StockFridgeBox.Text = ProductPriceHelper.FormatBr(product.StockFridge);
        StockFridgeMinBox.Text = product.StockFridgeMin.ToString();
        ActiveBox.IsChecked = product.Active;
        RefreshFridgeUi();

        var extra = ProductExtra.Parse(product.ExtraJson);
        var group = product.GroupName;
        ProductClassificationHelper.FillMissing(product.Name, ref group, extra);
        GroupBox.Text = group ?? "";
        BrandBox.Text = extra.Marca ?? "";
        BalancaBox.Text = extra.CodBalanca ?? "";
        InfoBox.Text = extra.InfoComplementar ?? "";
        TipoBox.SelectedItem = TipoOptions.Contains(extra.Tipo) ? extra.Tipo : TipoOptions[0];
        PermiteVendaBox.IsChecked = extra.PermiteVenda;
        ComposicaoBox.IsChecked = extra.Composicao;
        _composition.Clear();
        foreach (var c in ProductCompositionService.GetItems(extra))
            _composition.Add(c);
        RefreshCompSummary();
        FabricadoBox.IsChecked = extra.Fabricado;
        PesavelBox.IsChecked = extra.Pesavel;
        PrecoCompraBox.Text = ProductPriceHelper.FormatBr(extra.PrecoCompra);
        CustosBox.Text = ProductPriceHelper.FormatBr(extra.CustosPercent);
        LucroBox.Text = ProductPriceHelper.FormatBr(extra.LucroPercent);
        QtdAtacadoBox.Text = ProductPriceHelper.FormatBr(extra.QtdAtacado);
        PrecoAtacadoBox.Text = ProductPriceHelper.FormatBr(extra.PrecoAtacado);
        PrecoAvulsoBox.Text = ProductPriceHelper.FormatBr(extra.PrecoAvulso);
        FatorEmbalagemBox.Text = extra.FatorEmbalagem > 0
            ? extra.FatorEmbalagem.ToString("G", CultureInfo.CurrentCulture)
            : "1";

        // Se Preço Compra ainda estiver como total da CX, converte para unitário
        NormalizePurchaseToUnit();
        RecalcLucroFromCostSale();
        PackBarcodeBox.Text = extra.BarcodeEmbalagem ?? "";
        DescontoBox.Text = ProductPriceHelper.FormatBr(extra.DescontoPercent);
        PesoBrutoBox.Text = extra.PesoBrutoKg.ToString("G", CultureInfo.CurrentCulture);
        PesoLiquidoBox.Text = extra.PesoLiquidoKg.ToString("G", CultureInfo.CurrentCulture);
        ValidadeBalancaBox.Text = extra.ValidadeBalanca.ToString("G", CultureInfo.CurrentCulture);
        _legacyDataValidade = extra.DataValidade;
        ProximaValidadeBox.Text = ProductExpiryService.FormatDisplay(product.NextExpiry);
        ControleValidadeBox.IsChecked = extra.ControleValidade
            ?? ProductClassificationHelper.SuggestsExpiryControl(NameBox.Text, GroupBox.Text);
        PrecoPromoBox.Text = ProductPriceHelper.FormatBr(extra.PrecoPromocional);
        PromoInicioBox.Text = extra.PromoInicio ?? "";
        PromoFimBox.Text = extra.PromoFim ?? "";

        SelectPriceTable(extra.PriceTableId);
        SelectVasilhameTipo(extra.VasilhameTipoId);
        VasilhameQtyBox.Text = extra.VasilhameQty > 0
            ? extra.VasilhameQty.ToString("G", CultureInfo.CurrentCulture)
            : "1";
        RefreshPrecoAvulsoVisibility();
    }

    private void SelectPriceTable(int? id)
    {
        if (PriceTableBox.ItemsSource is not IEnumerable<PriceTable> items)
            return;
        PriceTableBox.SelectedItem = items.FirstOrDefault(t => t.Id == (id ?? 0))
                                     ?? items.FirstOrDefault();
    }

    private void SelectVasilhameTipo(int? id)
    {
        if (VasilhameTipoBox.ItemsSource is not IEnumerable<ContainerType> items)
            return;
        VasilhameTipoBox.SelectedItem = items.FirstOrDefault(t => t.Id == (id ?? 0))
                                        ?? items.FirstOrDefault();
    }

    private static string FormatDate(string raw)
    {
        if (DateTime.TryParse(raw, out var dt))
            return dt.ToString("dd/MM/yyyy HH:mm");
        return raw;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && btn.Tag is string tag)
            SelectTab(tag);
    }

    private void SelectTab(string tag)
    {
        TabDados.IsChecked = tag == "dados";
        TabComposicao.IsChecked = tag == "composicao";
        TabPromocao.IsChecked = tag == "promocao";
        TabImagem.IsChecked = tag == "imagem";

        PanelDados.Visibility = tag == "dados" ? Visibility.Visible : Visibility.Collapsed;
        PanelComposicao.Visibility = tag == "composicao" ? Visibility.Visible : Visibility.Collapsed;
        PanelPromocao.Visibility = tag == "promocao" ? Visibility.Visible : Visibility.Collapsed;
        PanelImagem.Visibility = tag == "imagem" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GearBarcode_Click(object sender, RoutedEventArgs e)
    {
        var source = !string.IsNullOrWhiteSpace(CodeBox.Text) ? CodeBox.Text : IdBox.Text;
        BarcodeBox.Text = TextNorm.NormalizeBarcode(source) ?? "";
    }

    private void OpenBrand_Click(object sender, RoutedEventArgs e) =>
        OpenCatalog(CatalogKind.Brand, BrandBox);

    private void OpenGroup_Click(object sender, RoutedEventArgs e) =>
        OpenCatalog(CatalogKind.Group, GroupBox);

    private void OpenUnit_Click(object sender, RoutedEventArgs e) =>
        OpenCatalog(CatalogKind.Unit, UnitBox);

    private void OpenCatalog(CatalogKind kind, ComboBox target)
    {
        var dlg = new CatalogQuickWindow(kind, target.Text) { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.SelectedName))
        {
            LoadCatalogs();
            target.Text = dlg.SelectedName;
        }
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var inferred = ProductClassificationHelper.Infer(NameBox.Text);
        if (string.IsNullOrWhiteSpace(GroupBox.Text) && !string.IsNullOrWhiteSpace(inferred.Group))
            GroupBox.Text = inferred.Group;
        if (string.IsNullOrWhiteSpace(BrandBox.Text) && !string.IsNullOrWhiteSpace(inferred.Brand))
            BrandBox.Text = inferred.Brand;
        RefreshPrecoAvulsoVisibility();
    }

    private void GroupBox_LostFocus(object sender, RoutedEventArgs e) =>
        RefreshPrecoAvulsoVisibility();

    private void RefreshPrecoAvulsoVisibility()
    {
        var cig = ProductClassificationHelper.IsCigarette(NameBox.Text, GroupBox.Text);
        PrecoAvulsoRow.Visibility = cig ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PrecoAvulsoBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var v = Math.Max(0, ProductPriceHelper.ParseBr(PrecoAvulsoBox.Text));
        PrecoAvulsoBox.Text = ProductPriceHelper.FormatBr(v);
        // Não recalcula SalePrice / margem / custo do maço.
    }

    private void PrecoCompraBox_LostFocus(object sender, RoutedEventArgs e) =>
        SyncCostFromPurchase();

    private void CustosBox_LostFocus(object sender, RoutedEventArgs e) =>
        SyncCostFromPurchase();

    private void FatorEmbalagemBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var f = ProductPriceHelper.ParseBr(FatorEmbalagemBox.Text);
        FatorEmbalagemBox.Text = f >= 1 ? f.ToString("G", CultureInfo.CurrentCulture) : "1";
        NormalizePurchaseToUnit();
        SyncCostFromPurchase();
    }

    private void QtdAtacadoBox_LostFocus(object sender, RoutedEventArgs e)
    {
        QtdAtacadoBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(QtdAtacadoBox.Text));
    }

    private void CostBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CostBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(CostBox.Text));
        RecalcLucroFromCostSale();
    }

    private void LucroBox_LostFocus(object sender, RoutedEventArgs e)
    {
        LucroBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(LucroBox.Text));
        RecalcSaleFromLucro();
    }

    private void SaleBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaleBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(SaleBox.Text));
        RecalcLucroFromCostSale();
    }

    private void PrecoAtacadoBox_LostFocus(object sender, RoutedEventArgs e)
    {
        PrecoAtacadoBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(PrecoAtacadoBox.Text));
        RecalcLucroFromCostSale();
    }

    private double GetPackFactor()
    {
        var fator = ProductPriceHelper.ParseBr(FatorEmbalagemBox.Text);
        if (fator >= 2)
            return fator;
        var qtdAtacado = ProductPriceHelper.ParseBr(QtdAtacadoBox.Text);
        return qtdAtacado >= 2 ? qtdAtacado : 1;
    }

    /// <summary>
    /// Refrigerante/outros: Preço Compra e Custo unitários.
    /// Cigarro: mantém valor do maço (não divide pelo fator).
    /// </summary>
    private void NormalizePurchaseToUnit()
    {
        if (ProductClassificationHelper.UsesPackPurchasePrice(NameBox.Text, GroupBox.Text))
            return;

        var factor = GetPackFactor();
        if (factor < 2.0001)
            return;

        var compra = ProductPriceHelper.ParseBr(PrecoCompraBox.Text);
        var cost = ProductPriceHelper.ParseBr(CostBox.Text);
        var sale = ProductPriceHelper.ParseBr(SaleBox.Text);
        if (compra <= 0)
            return;

        // Compra = total do fardo e custo já unitário (ex.: compra 9,61 / custo 0,80 / fator 12)
        if (cost > 0 && Math.Abs(compra - cost * factor) <= Math.Max(0.05, cost * factor * 0.2))
        {
            PrecoCompraBox.Text = ProductPriceHelper.FormatBr(cost);
            return;
        }

        // Compra e custo iguais ainda no total do fardo (ex.: ambos 9,61 com DP12)
        // Não dividir se já estiver unitário (0,80 ≈ venda 2,00).
        if (cost > 0 && Math.Abs(cost - compra) < 0.05)
        {
            var looksLikePackTotal = sale > 0
                ? compra >= sale * 2
                : compra >= factor * 0.25;
            if (!looksLikePackTotal)
                return;

            var unit = ProductPriceHelper.RoundPrice(compra / factor);
            PrecoCompraBox.Text = ProductPriceHelper.FormatBr(unit);
            CostBox.Text = ProductPriceHelper.FormatBr(unit);
        }
    }

    private void SyncCostFromPurchase()
    {
        PrecoCompraBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(PrecoCompraBox.Text));
        CustosBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(CustosBox.Text));

        var purchase = ProductPriceHelper.ParseBr(PrecoCompraBox.Text);
        if (purchase <= 0)
            return;

        CostBox.Text = ProductPriceHelper.FormatBr(
            ProductPriceHelper.CostFromPurchaseAndPercent(
                purchase,
                ProductPriceHelper.ParseBr(CustosBox.Text)));
        RecalcLucroFromCostSale();
    }

    private void RecalcSaleFromLucro()
    {
        var cost = ProductPriceHelper.ParseBr(CostBox.Text);
        var margin = ProductPriceHelper.ParseBr(LucroBox.Text);
        SaleBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.SaleFromCostAndMargin(cost, margin));
        RecalcLucroFromCostSale();
    }

    private void RecalcLucroFromCostSale()
    {
        var cost = ProductPriceHelper.ParseBr(CostBox.Text);
        var sale = ProductPriceHelper.ParseBr(SaleBox.Text);
        LucroBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.MarginOnSale(cost, sale));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput();
            if (_productId is null)
                ProductService.Create(input);
            else
                ProductService.Update(_productId.Value, input);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Produtos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ProductInput BuildInput()
    {
        var name = TextNorm.UpperStr(NameBox.Text);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Informe a descrição.");

        var extra = new ProductExtra
        {
            Tipo = TipoBox.SelectedItem as string ?? TipoOptions[0],
            Marca = TextNorm.UpperStr(BrandBox.Text),
            CodBalanca = TextNorm.NormalizeBarcode(BalancaBox.Text),
            InfoComplementar = TextNorm.UpperStr(InfoBox.Text),
            PrecoCompra = ProductPriceHelper.ParseBr(PrecoCompraBox.Text),
            CustosPercent = ProductPriceHelper.ParseBr(CustosBox.Text),
            LucroPercent = ProductPriceHelper.ParseBr(LucroBox.Text),
            QtdAtacado = ProductPriceHelper.ParseBr(QtdAtacadoBox.Text),
            PrecoAtacado = ProductPriceHelper.ParseBr(PrecoAtacadoBox.Text),
            PrecoAvulso = Math.Max(0, ProductPriceHelper.ParseBr(PrecoAvulsoBox.Text)),
            FatorEmbalagem = Math.Max(1, ProductPriceHelper.ParseBr(FatorEmbalagemBox.Text)),
            BarcodeEmbalagem = TextNorm.DistinctPackBarcode(PackBarcodeBox.Text, BarcodeBox.Text),
            DescontoPercent = ProductPriceHelper.ParseBr(DescontoBox.Text),
            PesoBrutoKg = ProductPriceHelper.ParseBr(PesoBrutoBox.Text),
            PesoLiquidoKg = ProductPriceHelper.ParseBr(PesoLiquidoBox.Text),
            ValidadeBalanca = ProductPriceHelper.ParseBr(ValidadeBalancaBox.Text),
            DataValidade = _legacyDataValidade,
            ControleValidade = ControleValidadeBox.IsChecked == true,
            PermiteVenda = PermiteVendaBox.IsChecked == true,
            Composicao = ComposicaoBox.IsChecked == true,
            ComposicaoItens = _composition.ToList(),
            Fabricado = FabricadoBox.IsChecked == true,
            Pesavel = PesavelBox.IsChecked == true,
            PrecoPromocional = ProductPriceHelper.ParseBr(PrecoPromoBox.Text),
            PromoInicio = string.IsNullOrWhiteSpace(PromoInicioBox.Text) ? null : PromoInicioBox.Text.Trim(),
            PromoFim = string.IsNullOrWhiteSpace(PromoFimBox.Text) ? null : PromoFimBox.Text.Trim(),
            PriceTableId = PriceTableBox.SelectedItem is PriceTable pt && pt.Id > 0 ? pt.Id : null,
            VasilhameTipoId = VasilhameTipoBox.SelectedItem is ContainerType ct && ct.Id > 0 ? ct.Id : null,
            VasilhameQty = Math.Max(0, ProductPriceHelper.ParseBr(VasilhameQtyBox.Text)),
        };

        var input = new ProductInput
        {
            Code = string.IsNullOrWhiteSpace(CodeBox.Text) ? null : CodeBox.Text.Trim(),
            Barcode = BarcodeBox.Text,
            Name = name,
            GroupName = string.IsNullOrWhiteSpace(GroupBox.Text) ? null : GroupBox.Text.Trim(),
            Unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? "UN" : UnitBox.Text.Trim(),
            CostPrice = ProductPriceHelper.ParseBr(CostBox.Text),
            SalePrice = ProductPriceHelper.ParseBr(SaleBox.Text),
            MinStock = (int)Math.Round(ProductPriceHelper.ParseBr(MinStockBox.Text)),
            Stock = ProductPriceHelper.ParseBr(StockBox.Text),
            StockFridge = Math.Max(0, ProductPriceHelper.ParseBr(StockFridgeBox.Text)),
            StockFridgeMin = (int)Math.Max(0, Math.Round(ProductPriceHelper.ParseBr(StockFridgeMinBox.Text))),
            Location = string.IsNullOrWhiteSpace(LocationBox.Text) ? null : LocationBox.Text.Trim(),
            Extra = extra,
            Active = ActiveBox.IsChecked == true,
        };

        ProductCompositionService.Validate(extra, _productId);
        return input;
    }

    private void CompSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_compSuggestSuppress)
            return;

        _compSuggestTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _compSuggestTimer.Tick -= CompSuggestTimer_Tick;
        _compSuggestTimer.Tick += CompSuggestTimer_Tick;
        _compSuggestTimer.Stop();
        _compSuggestTimer.Start();
    }

    private void CompSuggestTimer_Tick(object? sender, EventArgs e)
    {
        _compSuggestTimer?.Stop();
        RefreshCompSuggestions();
    }

    private void RefreshCompSuggestions()
    {
        var q = CompSearchBox.Text?.Trim() ?? "";
        if (q.Length < 1)
        {
            HideCompSuggestions();
            return;
        }

        var list = ProductService.List(search: q, ativo: "ativos")
            .Where(p => _productId is not int sid || p.Id != sid)
            .Take(20)
            .ToList();

        if (list.Count == 0)
        {
            HideCompSuggestions();
            return;
        }

        CompSuggestList.ItemsSource = list;
        CompSuggestList.SelectedIndex = 0;
        CompSuggestPopup.IsOpen = true;
    }

    private void HideCompSuggestions()
    {
        CompSuggestPopup.IsOpen = false;
        CompSuggestList.ItemsSource = null;
    }

    private void CompSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CompSuggestPopup.IsOpen || CompSuggestList.Items.Count == 0)
            return;

        if (e.Key is Key.Down or Key.Up)
        {
            var count = CompSuggestList.Items.Count;
            var idx = CompSuggestList.SelectedIndex;
            if (e.Key == Key.Down)
                CompSuggestList.SelectedIndex = idx < 0 ? 0 : Math.Min(count - 1, idx + 1);
            else
                CompSuggestList.SelectedIndex = idx <= 0 ? 0 : idx - 1;
            CompSuggestList.ScrollIntoView(CompSuggestList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideCompSuggestions();
            e.Handled = true;
        }
    }

    private void CompSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (CompSuggestPopup.IsOpen && CompSuggestList.SelectedItem is Product picked)
        {
            TryAddComposition(picked);
            e.Handled = true;
            return;
        }

        CompAdd_Click(sender, e);
        e.Handled = true;
    }

    private void CompSearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Fecha a lista após um tick para permitir o clique no item.
        Dispatcher.BeginInvoke(() =>
        {
            if (!CompSuggestList.IsKeyboardFocusWithin && !CompSearchBox.IsKeyboardFocusWithin)
                HideCompSuggestions();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CompSuggestList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CompSuggestList.SelectedItem is Product picked)
            TryAddComposition(picked);
    }

    private void CompSuggestList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CompSuggestList.SelectedItem is Product picked)
        {
            TryAddComposition(picked);
            e.Handled = true;
        }
    }

    private void CompAdd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CompSuggestPopup.IsOpen && CompSuggestList.SelectedItem is Product selected)
            {
                TryAddComposition(selected);
                return;
            }

            var q = CompSearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(q))
                throw new InvalidOperationException("Digite para buscar o componente (ex.: DUNHILL).");

            var found = ProductService.FindByBarcode(q)
                ?? ProductService.List(search: q, ativo: "ativos").FirstOrDefault();
            if (found is null)
                throw new InvalidOperationException("Componente não encontrado.");

            TryAddComposition(found);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Composição", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TryAddComposition(Product found)
    {
        try
        {
            if (_productId is int sid && found.Id == sid)
                throw new InvalidOperationException("O produto não pode ser componente de si mesmo.");
            if (_composition.Any(c => c.ProductId == found.Id))
                throw new InvalidOperationException("Componente já adicionado.");

            var qty = ProductPriceHelper.ParseBr(CompQtyBox.Text);
            if (qty <= 0) qty = 1;

            _composition.Add(new ProductCompositionItem
            {
                ProductId = found.Id,
                Code = found.Code ?? "",
                Name = found.Name,
                Unit = found.Unit,
                Quantity = qty,
                Cost = found.CostPrice,
            });
            ComposicaoBox.IsChecked = true;
            RefreshCompSummary();
            HideCompSuggestions();
            _compSuggestSuppress = true;
            CompSearchBox.Clear();
            _compSuggestSuppress = false;
            CompQtyBox.Text = "1";
            CompSearchBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Kit / Combo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CompApplyCost_Click(object sender, RoutedEventArgs e)
    {
        if (_composition.Count == 0)
        {
            MessageBox.Show("Adicione componentes antes de somar o custo.", "Kit / Combo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sum = ProductPriceHelper.RoundPrice(
            _composition.Sum(c => c.Cost * c.Quantity));
        CostBox.Text = ProductPriceHelper.MoneyBr(sum).Replace("R$", "").Trim();
        PrecoCompraBox.Text = CostBox.Text;
        ComposicaoBox.IsChecked = true;
        RefreshCompSummary();
        MessageBox.Show(
            $"Custo do kit atualizado para {ProductPriceHelper.MoneyBr(sum)}.\n\n" +
            "Confira o Preço de Venda do combo na aba Básicos e salve (F5).",
            "Kit / Combo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshCompSummary()
    {
        if (CompSummaryText is null)
            return;
        if (_composition.Count == 0)
        {
            CompSummaryText.Text = "Nenhum componente — adicione os itens do kit.";
            return;
        }

        var sum = ProductPriceHelper.RoundPrice(
            _composition.Sum(c => c.Cost * c.Quantity));
        CompSummaryText.Text =
            $"{_composition.Count} item(ns) · Custo estimado do kit: {ProductPriceHelper.MoneyBr(sum)} " +
            "(use «Somar custos» para gravar no preço de custo)";
    }

    private void CompRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProductCompositionItem item })
        {
            _composition.Remove(item);
            RefreshCompSummary();
        }
    }

    private void FridgeFields_Changed(object sender, RoutedEventArgs e) => RefreshFridgeUi();

    private void RefreshFridgeUi()
    {
        var minFridge = (int)Math.Max(0, Math.Round(ProductPriceHelper.ParseBr(StockFridgeMinBox.Text)));
        var fridge = Math.Max(0, ProductPriceHelper.ParseBr(StockFridgeBox.Text));
        var warehouse = ProductPriceHelper.ParseBr(StockBox.Text);
        var uses = minFridge > 0 || fridge > 0.0001;

        FridgePanel.Visibility = uses ? Visibility.Visible : Visibility.Collapsed;
        TotalStockPanel.Visibility = uses ? Visibility.Visible : Visibility.Collapsed;
        TransferFridgeBtn.Visibility = uses && _productId is > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReturnFridgeBtn.Visibility = uses && _productId is > 0 && fridge > 0.0001
            ? Visibility.Visible
            : Visibility.Collapsed;
        StockLabel.Text = uses ? "Depósito" : "Estoque Atual";
        TotalStockBox.Text = ProductPriceHelper.FormatBr(warehouse + fridge);
    }

    private void TransferFridge_Click(object sender, RoutedEventArgs e)
    {
        if (_productId is not int pid)
        {
            MessageBox.Show("Salve o produto antes de transferir.", "Geladeira",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Grava o que está na tela antes de transferir
            ProductService.Update(pid, BuildInput());

            var depot = ProductPriceHelper.ParseBr(StockBox.Text);
            if (depot < 0.0001)
                throw new InvalidOperationException(
                    "Depósito sem saldo. Ajuste o estoque antes de repor a geladeira.");

            var qtyText = PromptQuantity(
                "Transferir para geladeira",
                $"Quanto tirar do depósito? (disponível: {ProductPriceHelper.FormatBr(depot)})",
                ProductPriceHelper.FormatBr(Math.Min(depot, Math.Max(1, ProductPriceHelper.ParseBr(StockFridgeMinBox.Text)))));
            if (qtyText is null)
                return;

            var qty = ProductPriceHelper.ParseBr(qtyText);
            StockService.TransferWarehouseToFridge(pid, qty);

            var updated = ProductService.GetById(pid);
            if (updated is not null)
            {
                StockBox.Text = ProductPriceHelper.FormatBr(updated.Stock);
                StockFridgeBox.Text = ProductPriceHelper.FormatBr(updated.StockFridge);
                RefreshFridgeUi();
            }

            MessageBox.Show("Transferência concluída.", "Geladeira",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Geladeira", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ReturnFridge_Click(object sender, RoutedEventArgs e)
    {
        if (_productId is not int pid)
        {
            MessageBox.Show("Salve o produto antes de retornar.", "Geladeira",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            ProductService.Update(pid, BuildInput());

            var fridge = ProductPriceHelper.ParseBr(StockFridgeBox.Text);
            if (fridge < 0.0001)
                throw new InvalidOperationException(
                    "Geladeira sem saldo. Nada a retornar para o depósito.");

            var qtyText = PromptQuantity(
                "Retornar para o depósito",
                $"Quanto tirar da geladeira? (disponível: {ProductPriceHelper.FormatBr(fridge)})",
                ProductPriceHelper.FormatBr(fridge));
            if (qtyText is null)
                return;

            var qty = ProductPriceHelper.ParseBr(qtyText);
            var confirm = MessageBox.Show(
                $"Retornar {ProductPriceHelper.FormatBr(qty)} unidades da geladeira para o depósito?",
                "Geladeira",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            StockService.TransferFridgeToWarehouse(pid, qty);

            var updated = ProductService.GetById(pid);
            if (updated is not null)
            {
                StockBox.Text = ProductPriceHelper.FormatBr(updated.Stock);
                StockFridgeBox.Text = ProductPriceHelper.FormatBr(updated.StockFridge);
                RefreshFridgeUi();
            }

            MessageBox.Show("Retorno concluído.", "Geladeira",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Geladeira", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? PromptQuantity(string title, string message, string defaultValue)
    {
        var win = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };
        if (System.Windows.Application.Current?.MainWindow is Window owner && owner.IsVisible)
            win.Owner = owner;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        var box = new TextBox
        {
            Text = defaultValue,
            Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 0, 6, 0),
        };
        panel.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        string? result = null;
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancelar", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };
        cancel.Click += (_, _) => { win.DialogResult = false; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        win.Content = panel;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? result : null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Save_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            if (BrandBox.IsKeyboardFocusWithin) OpenBrand_Click(sender, e);
            else if (GroupBox.IsKeyboardFocusWithin) OpenGroup_Click(sender, e);
            else if (UnitBox.IsKeyboardFocusWithin) OpenUnit_Click(sender, e);
            e.Handled = true;
        }
    }
}
