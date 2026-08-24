using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SGDB.Application.Sales;
using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PdvWindow : Window
{
    private readonly ObservableCollection<PdvCartLine> _cart = new();
    private readonly DispatcherTimer _lookupTimer;
    private Product? _pendingProduct;
    private double _pendingUnitPrice;
    private double _pendingStockUnitsPerSale = 1;
    private string? _pendingBuscaLabel;
    private string? _pendingLineDisplayName;
    private int _lineCounter;
    private long _lastBuscaKeyAt;
    private bool _suppressSearchChange;
    private readonly PdvScanMultiplierState _scanMultiplier = new();
    private readonly PdvF6QuantitySession _f6Quantity = new();
    private readonly CultureInfo _qtyCulture = CultureInfo.GetCultureInfo("pt-BR");

    public PdvWindow()
    {
        InitializeComponent();
        CartGrid.ItemsSource = _cart;
        _lookupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _lookupTimer.Tick += (_, _) =>
        {
            _lookupTimer.Stop();
            RunLiveLookup();
        };
        Loaded += OnLoaded;
        DataObject.AddPastingHandler(QtyBox, QtyBox_Pasting);
        DataObject.AddPastingHandler(F6QtyBox, QtyBox_Pasting);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TitleText.Text = $"PDV - Venda de Balcão | CAIXA | {DateTime.Today:dd/MM/yyyy}";
        StatusTerminalText.Text = $"Terminal: {Environment.MachineName}";
        RefreshMultiplierHint();
        ApplyResumoPermissionUi();
        RefreshCaixaState();
        SearchBox.Focus();
    }

    private void ApplyResumoPermissionUi()
    {
        var can = AccessControl.Can("PdvResumoDia");
        BtnResumoDia.IsEnabled = can;
        BtnResumoDiaTop.IsEnabled = can;
        BtnResumoDia.Opacity = can ? 1 : 0.45;
        BtnResumoDiaTop.Opacity = can ? 1 : 0.45;
        var tip = can ? null : "Sem permissão para ver o resumo do dia";
        BtnResumoDia.ToolTip = tip;
        BtnResumoDiaTop.ToolTip = tip;
    }

    private void RefreshCaixaState()
    {
        var op = CashService.IsOperational();
        if (op)
        {
            GatePanel.Visibility = Visibility.Collapsed;
            PdvPanel.Visibility = Visibility.Visible;
            SearchBox.Focus();
        }
        else
        {
            GatePanel.Visibility = Visibility.Visible;
            PdvPanel.Visibility = Visibility.Collapsed;
            GateMessageText.Text = "Caixa não aberto. Informe o saldo inicial e abra o caixa antes de vender.";
        }
    }

    private void OpenCaixa_Click(object sender, RoutedEventArgs e)
    {
        var cash = new CashModuleView();
        var host = new Window
        {
            Title = "Caixa — SGDB",
            Width = 1100,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Owner = this,
            Content = cash,
        };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Themes/SgdbTheme.xaml", UriKind.Relative),
        });
        cash.CloseRequested += (_, _) => host.Close();
        host.ShowDialog();
        RefreshCaixaState();
    }

    private void ClosePdv_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Deseja sair do PDV e voltar ao painel?",
            "Sair do PDV",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
            return;

        DialogResult = false;
        Close();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchChange)
            return;

        var term = SearchBox.Text.Trim();
        if (_pendingProduct is not null && !string.IsNullOrEmpty(_pendingBuscaLabel)
            && !term.Equals(_pendingBuscaLabel, StringComparison.OrdinalIgnoreCase))
        {
            ClearPendingProduct();
        }

        if (PdvScanMultiplierParser.Parse(term).IsExplicit)
        {
            _lookupTimer.Stop();
            HideLookup();
            return;
        }

        if (LooksLikeBarcode(term))
        {
            var digits = DigitsOnly(term);
            var fastScan = Environment.TickCount64 - _lastBuscaKeyAt < 45;
            if (digits.Length >= 8 || fastScan)
            {
                _lookupTimer.Stop();
                HideLookup();
                return;
            }
        }

        _lookupTimer.Stop();
        _lookupTimer.Start();
    }

    private void RunLiveLookup()
    {
        if (GatePanel.Visibility == Visibility.Visible)
            return;

        var term = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(term))
        {
            HideLookup();
            if (string.IsNullOrEmpty(_pendingBuscaLabel))
                ClearPendingProduct();
            return;
        }

        if (LooksLikeBarcode(term))
            return;

        var list = PdvService.SearchProducts(term, 80);
        if (list.Count == 0)
        {
            HideLookup();
            ClearPendingProduct();
            return;
        }

        LookupGrid.ItemsSource = list;
        LookupPanel.Visibility = Visibility.Visible;
        LookupGrid.SelectedIndex = 0;
        SetPendingProductPreview(list[0]);
    }

    private static bool LooksLikeBarcode(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return false;
        var digits = DigitsOnly(term);
        if (digits.Length >= 8)
            return true;
        return digits.Length >= 4 && digits.Length == term.Trim().Length;
    }

    private static string DigitsOnly(string term) =>
        new(term.Where(char.IsDigit).ToArray());

    private bool BuscaMatchesPending()
    {
        if (_pendingProduct is null)
            return false;
        var term = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(_pendingBuscaLabel))
            return term.Equals(_pendingBuscaLabel, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || !CanNavigateLookup())
            return;

        MoveLookupSelection(e.Key == Key.Down ? 1 : -1);
        e.Handled = true;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        _lastBuscaKeyAt = Environment.TickCount64;

        if (e.Key is Key.Up or Key.Down)
            return;

        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        _lookupTimer.Stop();
        var term = SearchBox.Text.Trim();

        var multiplier = PdvScanMultiplierParser.Parse(term);
        if (!multiplier.Check.Allowed)
        {
            ClearScanMultiplier();
            MessageBox.Show(multiplier.Check.Message, "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            SearchBox.SelectAll();
            return;
        }

        if (multiplier.Kind == PdvScanMultiplierKind.Armed)
        {
            _scanMultiplier.TryArm(multiplier.Quantity);
            _suppressSearchChange = true;
            SearchBox.Text = "";
            _suppressSearchChange = false;
            HideLookup();
            RefreshMultiplierHint();
            SearchBox.Focus();
            return;
        }

        if (multiplier.Kind == PdvScanMultiplierKind.Combined)
        {
            term = multiplier.Remainder;
            _scanMultiplier.Clear();
            _scanMultiplier.TryArm(multiplier.Quantity);
        }

        if (string.IsNullOrEmpty(term))
        {
            if (_scanMultiplier.IsArmed)
                return;
            if (_pendingProduct is not null)
            {
                FocusQtyBoxForManualEdit();
                return;
            }
            if (_cart.Count > 0)
                OpenPayment();
            return;
        }

        if (LookupPanel.Visibility == Visibility.Visible && LookupGrid.Items.Count > 0
            && multiplier.Kind != PdvScanMultiplierKind.Combined)
        {
            SelectLookupProduct();
            return;
        }

        if (_pendingProduct is not null && BuscaMatchesPending())
        {
            FocusQtyBoxForManualEdit();
            return;
        }

        if (LooksLikeBarcode(term))
        {
            var scan = PdvService.ResolveScan(term);
            if (scan is not null)
            {
                if (!TryResolveCigaretteModeForUi(scan.Product, out var chosen))
                {
                    ClearScanMultiplier();
                    SearchBox.SelectAll();
                    return;
                }
                scan = chosen ?? scan;
                SetPendingFromScan(scan, fromBarcodeScan: true);
                ApplyArmedMultiplierToQtyBox();
                if (PdvScanFocusPolicy.ShouldAutoInclude(true) || scan.IsPackSale)
                    TryIncludePending();
                return;
            }
            if (DigitsOnly(term).Length >= 8)
            {
                ClearScanMultiplier();
                MessageBox.Show("Produto não encontrado para este código.", "PDV",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                SearchBox.SelectAll();
                return;
            }
        }

        var product = PdvService.FindProduct(term);
        if (product is not null)
        {
            SetPendingProduct(product);
            return;
        }

        var list = PdvService.SearchProducts(term, 80);
        if (list.Count > 0)
        {
            LookupGrid.ItemsSource = list;
            LookupPanel.Visibility = Visibility.Visible;
            LookupGrid.SelectedIndex = 0;
            SelectLookupProduct();
            return;
        }

        ClearScanMultiplier();
        MessageBox.Show("Nenhum produto encontrado.", "PDV", MessageBoxButton.OK, MessageBoxImage.Information);
        SearchBox.SelectAll();
    }

    private void MoveLookupSelection(int delta)
    {
        if (LookupGrid.Items.Count == 0)
            return;
        var next = LookupGrid.SelectedIndex + delta;
        if (next < 0)
            next = 0;
        if (next >= LookupGrid.Items.Count)
            next = LookupGrid.Items.Count - 1;
        LookupGrid.SelectedIndex = next;
        LookupGrid.ScrollIntoView(LookupGrid.SelectedItem);
        if (LookupGrid.SelectedItem is Product p)
            SetPendingProductPreview(p);
    }

    private void LookupGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SelectLookupProduct();

    private void LookupGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LookupGrid.SelectedItem is Product p)
            SetPendingProductPreview(p);
    }

    private void LookupGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SelectLookupProduct();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideLookup();
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void SelectLookupProduct()
    {
        if (LookupGrid.SelectedItem is Product p)
            SetPendingProduct(p);
    }

    private void HideLookup()
    {
        LookupPanel.Visibility = Visibility.Collapsed;
        LookupGrid.ItemsSource = null;
    }

    private void ClearPendingProduct()
    {
        _pendingProduct = null;
        _pendingUnitPrice = 0;
        _pendingStockUnitsPerSale = 1;
        _pendingBuscaLabel = null;
        _pendingLineDisplayName = null;
        CurrentPriceText.Text = "0,00";
        CurrentItemTotalText.Text = "0,00";
    }

    private void SetPendingProductPreview(Product product)
    {
        _pendingProduct = product;
        _pendingUnitPrice = product.SalePrice;
        _pendingStockUnitsPerSale = 1;
        _pendingLineDisplayName = null;
        CurrentPriceText.Text = product.SalePriceDisplay;
        UpdateItemTotalPreview();
    }

    private void SetPendingProduct(Product product)
    {
        if (!TryResolveCigaretteModeForUi(product, out var scan))
        {
            ClearScanMultiplier();
            ClearPendingProduct();
            SearchBox.Focus();
            SearchBox.SelectAll();
            return;
        }
        ClearScanMultiplier();
        SetPendingFromScan(scan ?? PdvService.ResolveManualSale(product), fromBarcodeScan: false);
    }

    /// <summary>
    /// Se cigarro com PrecoAvulso &gt; 0, abre diálogo Avulso/Maço.
    /// Retorna false se o operador cancelar (Esc).
    /// chosen null = sem diálogo (usar resolução padrão do chamador).
    /// </summary>
    private bool TryResolveCigaretteModeForUi(Product product, out PdvScanResult? chosen)
    {
        chosen = null;
        if (!PdvCartHelper.NeedsCigaretteModeChoice(product))
            return true;

        var extra = ProductExtra.Parse(product.ExtraJson);
        var packPrice = extra.PrecoAtacado > 0 ? extra.PrecoAtacado : product.SalePrice;
        var dlg = new PdvCigaretteModeWindow(
            product.Name,
            ProductPriceHelper.RoundPrice(extra.PrecoAvulso),
            ProductPriceHelper.RoundPrice(packPrice))
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.SelectedMode))
            return false;

        chosen = PdvService.ResolveManualSale(product, dlg.SelectedMode);
        return true;
    }

    private void SetPendingFromScan(PdvScanResult scan, bool fromBarcodeScan)
    {
        _pendingProduct = scan.Product;
        _pendingUnitPrice = scan.UnitPrice;
        _pendingStockUnitsPerSale = scan.StockUnitsPerSale;
        _pendingLineDisplayName = PdvCartHelper.LineDisplayName(scan.Product, scan.ModeLabel);
        HideLookup();
        CurrentPriceText.Text = ProductPriceHelper.FormatBr(scan.UnitPrice);
        QtyBox.Text = scan.Quantity.ToString("0.000", _qtyCulture);
        UpdateItemTotalPreview();
        var label = !string.IsNullOrWhiteSpace(scan.ModeLabel)
            ? $"{scan.Product.Name} [{scan.ModeLabel}]"
            : scan.Product.Name;
        _pendingBuscaLabel = label;
        _suppressSearchChange = true;
        SearchBox.Text = label;
        _suppressSearchChange = false;
        if (PdvScanFocusPolicy.ShouldFocusQtyBox(fromBarcodeScan))
            FocusQtyBoxForManualEdit();
    }

    private void FocusQtyBoxForManualEdit()
    {
        QtyBox.Focus();
        Dispatcher.BeginInvoke(() =>
        {
            QtyBox.Focus();
            QtyBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void ApplyArmedMultiplierToQtyBox()
    {
        if (!_scanMultiplier.IsArmed)
        {
            RefreshMultiplierHint();
            return;
        }

        var qty = _scanMultiplier.Consume();
        QtyBox.Text = qty.ToString("0.000", _qtyCulture);
        UpdateItemTotalPreview();
        RefreshMultiplierHint();
    }

    private void ClearScanMultiplier()
    {
        _f6Quantity.Cancel();
        _scanMultiplier.Clear();
        RefreshMultiplierHint();
    }

    private void RefreshMultiplierHint()
    {
        var baseText = $"Terminal: {Environment.MachineName}";
        if (_scanMultiplier.IsArmed)
        {
            var qtyText = _scanMultiplier.Quantity.ToString("0.###", _qtyCulture);
            StatusTerminalText.Text = $"{baseText}  ·  Próxima quantidade: {qtyText}";
        }
        else
        {
            StatusTerminalText.Text = baseText;
        }

        RefreshF6Ui();
    }

    private void RefreshF6Ui()
    {
        F6QtyBox.Visibility = _f6Quantity.IsEditing ? Visibility.Visible : Visibility.Collapsed;
        if (_f6Quantity.IsEditing)
        {
            F6ArmedHint.Visibility = Visibility.Collapsed;
            return;
        }

        if (_scanMultiplier.IsArmed)
        {
            var qtyText = _scanMultiplier.Quantity.ToString("0.###", _qtyCulture);
            F6ArmedHint.Text = $"Próxima quantidade: {qtyText}";
            F6ArmedHint.Visibility = Visibility.Visible;
            return;
        }

        F6ArmedHint.Visibility = Visibility.Collapsed;
    }

    private void BeginF6QuantityMode()
    {
        _f6Quantity.Enter();
        F6QtyBox.Text = "";
        RefreshF6Ui();
        F6QtyBox.Focus();
    }

    private void ConfirmF6Quantity()
    {
        var check = _f6Quantity.Confirm(F6QtyBox.Text, _scanMultiplier);
        RefreshMultiplierHint();
        if (!check.Allowed)
        {
            MessageBox.Show(check.Message, "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            SearchBox.Focus();
            return;
        }

        SearchBox.Focus();
    }

    private void F6QtyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        ConfirmF6Quantity();
        e.Handled = true;
    }

    private void F6QtyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private void QtyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryIncludePending();
            e.Handled = true;
        }
    }

    private void QtyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private void QtyBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !IsQtyChar(e.Text);

    private void QtyBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }
        var text = e.DataObject.GetData(typeof(string)) as string ?? "";
        if (string.IsNullOrEmpty(text) || text.Any(c => !IsQtyChar(c.ToString())))
            e.CancelCommand();
    }

    /// <summary>Quantidade: só dígitos e vírgula/ponto decimal.</summary>
    private static bool IsQtyChar(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        foreach (var c in text)
        {
            if (char.IsDigit(c) || c is ',' or '.')
                continue;
            return false;
        }
        return true;
    }

    private void QtyBox_LostFocus(object sender, RoutedEventArgs e) => UpdateItemTotalPreview();

    private double ParseQty(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1;
        if (double.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v;
        return ProductPriceHelper.ParseBr(text);
    }

    private void UpdateItemTotalPreview()
    {
        if (_pendingProduct is null)
        {
            CurrentItemTotalText.Text = "0,00";
            return;
        }
        var qty = ParseQty(QtyBox.Text);
        var unit = PdvCartHelper.ResolveLineUnitPrice(
            _pendingProduct, qty, _pendingUnitPrice, _pendingStockUnitsPerSale);
        CurrentPriceText.Text = ProductPriceHelper.FormatBr(unit);
        CurrentItemTotalText.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.RoundPrice(qty * unit));
    }

    private void TryIncludePending()
    {
        if (_pendingProduct is null)
            return;

        var guard = PdvQtyBoxGuard.Evaluate(QtyBox.Text, _pendingProduct.Barcode, _pendingProduct.Code);
        if (!guard.Accepted)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PDV-QTY] user={AppSession.UserLogin} field=QtyBox value={QtyBox.Text} reason={guard.Reason}");
            MessageBox.Show(guard.Message, "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            QtyBox.Text = guard.QtyTextAfter;
            ClearScanMultiplier();
            SearchBox.Focus();
            return;
        }

        var qty = ParseQty(QtyBox.Text);
        if (qty <= 0)
        {
            ClearScanMultiplier();
            MessageBox.Show("Quantidade inválida.", "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            QtyBox.Focus();
            return;
        }

        try
        {
            PdvCartHelper.IncludeOrMerge(
                _cart,
                _pendingProduct,
                qty,
                _pendingUnitPrice,
                _pendingStockUnitsPerSale,
                ref _lineCounter,
                _pendingLineDisplayName);
        }
        catch (PdvException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PDV-QTY] user={AppSession.UserLogin} field=cart value={QtyBox.Text} reason={ex.Message}");
            MessageBox.Show(ex.Message, "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            QtyBox.Text = PdvQtyBoxGuard.ResetQtyText;
            ClearScanMultiplier();
            SearchBox.Focus();
            return;
        }

        ClearScanMultiplier();
        _pendingProduct = null;
        _pendingUnitPrice = 0;
        _pendingStockUnitsPerSale = 1;
        _pendingBuscaLabel = null;
        _pendingLineDisplayName = null;
        _suppressSearchChange = true;
        SearchBox.Text = "";
        _suppressSearchChange = false;
        HideLookup();
        QtyBox.Text = "1,000";
        CurrentPriceText.Text = "0,00";
        CurrentItemTotalText.Text = "0,00";
        UpdateGrandTotal();
        RefreshCartGrid();
        SearchBox.Focus();
    }

    private void RefreshCartGrid()
    {
        var idx = 0;
        foreach (var line in _cart)
            line.LineNum = ++idx;
        CartGrid.ItemsSource = null;
        CartGrid.ItemsSource = _cart;
        if (_cart.Count > 0)
            CartGrid.SelectedIndex = _cart.Count - 1;
    }

    private void UpdateGrandTotal()
    {
        var total = ProductPriceHelper.RoundPrice(_cart.Sum(c => c.Subtotal));
        GrandTotalText.Text = ProductPriceHelper.FormatBr(total);
    }

    private void CartGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CartGrid.SelectedItem is PdvCartLine line)
        {
            CurrentPriceText.Text = line.UnitPriceDisplay;
            CurrentItemTotalText.Text = line.SubtotalDisplay;
            QtyBox.Text = line.QuantityDisplay;
        }
    }

    private void Finalize_Click(object sender, RoutedEventArgs e) => OpenPayment();

    private void OpenResumo_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("PdvResumoDia", "ver o resumo do dia no PDV", this))
            return;

        try
        {
            var w = new PdvResumoDiaWindow { Owner = this };
            w.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o resumo do dia.\n{ex.Message}",
                "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenConsulta_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var w = new PdvVendasConsultaWindow { Owner = this };
            w.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir a consulta de vendas.\n{ex.Message}",
                "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenPayment()
    {
        if (_cart.Count == 0)
        {
            MessageBox.Show("Adicione produtos à venda.", "PDV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Confere estoque antes de cobrar — senão o cliente paga (PIX/cartão) e a venda falha depois.
        try
        {
            PdvService.ValidateItemsBeforePayment(_cart.ToList());
        }
        catch (PdvException ex)
        {
            MessageBox.Show(ex.Message, "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var subtotal = ProductPriceHelper.RoundPrice(_cart.Sum(c => c.Subtotal));

        // Loop: se o operador voltar da tela de cupom para alterar pagamento,
        // reabre as formas sem perder o carrinho.
        while (true)
        {
            var pay = new PdvPaymentWindow(subtotal, _cart.ToList()) { Owner = this };
            if (pay.ShowDialog() != true || !pay.Confirmed)
                return;

            try
            {
                var items = _cart.ToList();
                var result = ApplicationServices.FinalizeSale.Execute(new FinalizeSaleCommand
                {
                    Items = items.Select(l => new SaleLine
                    {
                        ProductId = l.ProductId,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice,
                        StockUnitsPerSale = l.StockUnitsPerSale,
                    }).ToList(),
                    PaymentType = pay.SelectedPaymentType,
                    Payments = pay.Payments?.Select(p => new SalePayment
                    {
                        PaymentType = p.PaymentType,
                        Amount = p.Amount,
                    }).ToList(),
                    Discount = pay.Discount,
                    Surcharge = pay.Surcharge,
                    CashReceived = pay.CashReceived,
                    CustomerPersonId = pay.CustomerPersonId,
                    SellerId = pay.SellerId,
                });

                if (pay.PixPaymentId is long pixId)
                    PixIntentService.AttachSale(pixId, result.SaleId);

                PeripheralService.TryOpenCashDrawerAfterCashSale(pay.Payments);

                AuditService.LogJson("venda", "venda", result.SaleId.ToString(),
                    new
                    {
                        sale_id = result.SaleId,
                        total = result.Total,
                        payment_type = pay.SelectedPaymentType,
                        items_count = items.Count,
                        discount = pay.Discount,
                        surcharge = pay.Surcharge,
                    },
                    $"{pay.SelectedPaymentType} · R$ {result.Total:N2}");

                if (pay.Discount > 0.009)
                {
                    var discountPct = subtotal > 0 ? pay.Discount / subtotal * 100.0 : 0;
                    AuditService.LogJson("desconto", "venda", result.SaleId.ToString(),
                        AuditPayloadBuilder.PdvDiscount(result.SaleId, subtotal, pay.Discount, discountPct, result.Total, pay.SelectedPaymentType),
                        $"Desconto de R$ {pay.Discount:N2} ({discountPct:N1}%) na venda #{result.SaleId}");
                }

                var cupom = new PdvCupomConfirmWindow(
                    result.SaleId,
                    items,
                    pay.Payments,
                    subtotal,
                    pay.Discount,
                    pay.Surcharge,
                    result.Total,
                    result.CashReceived > 0.009 ? result.CashReceived : pay.CashReceived,
                    result.ChangeAmount)
                {
                    Owner = this,
                };
                cupom.ShowDialog();

                if (cupom.BackToPaymentRequested || cupom.CancelSaleRequested)
                {
                    try
                    {
                        ApplicationServices.CancelSale.Execute(new CancelSaleCommand
                        {
                            SaleId = result.SaleId,
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Não foi possível estornar a venda #{result.SaleId}.\n{ex.Message}",
                            "PDV",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    PixSaleReverseService.ShowOperatorAlert(this);

                    // Recalcula subtotal (carrinho intacto) e volta às formas de pagamento.
                    subtotal = ProductPriceHelper.RoundPrice(_cart.Sum(c => c.Subtotal));
                    continue;
                }

                NewSale();
                return;
            }
            catch (PdvException ex)
            {
                ShowFinalizeError(ex.Message, pay);
                return;
            }
            catch (CashOperationException ex)
            {
                ShowFinalizeError(ex.Message, pay);
                RefreshCaixaState();
                return;
            }
        }
    }

    private static void ShowFinalizeError(string message, PdvPaymentWindow pay)
    {
        if (pay.PixPaidAmount > 0.009)
        {
            if (pay.PixPaymentId is long pixId)
            {
                try
                {
                    PixCheckoutCoordinator.RefundApprovedWithoutSaleAsync(pixId).GetAwaiter().GetResult();
                }
                catch
                {
                    // intent fica com last_error
                }
            }

            var pid = pay.PixPaymentId is long id ? $"\nPagamento Mercado Pago #{id}" : "";
            MessageBox.Show(
                $"{message}\n\n" +
                $"ATENÇÃO: o cliente JÁ PAGOU R$ {pay.PixPaidAmount:N2} via PIX e a venda não foi registrada." + pid +
                "\n\nFoi solicitado o estorno no Mercado Pago. Confira se o valor voltou ao cliente.",
                "PDV — PIX recebido, venda não registrada",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(message, "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void NewSale()
    {
        _cart.Clear();
        _lineCounter = 0;
        _pendingProduct = null;
        _pendingBuscaLabel = null;
        ClearScanMultiplier();
        HideLookup();
        UpdateGrandTotal();
        CurrentPriceText.Text = "0,00";
        CurrentItemTotalText.Text = "0,00";
        GrandTotalText.Text = "0,00";
        _suppressSearchChange = true;
        SearchBox.Text = "";
        _suppressSearchChange = false;
        SearchBox.Focus();
    }

    private bool IsLookupOpen() =>
        LookupPanel.Visibility == Visibility.Visible && LookupGrid.Items.Count > 0;

    private bool CanNavigateLookup()
    {
        if (!IsLookupOpen())
            return false;
        if (QtyBox.IsKeyboardFocusWithin || F6QtyBox.IsKeyboardFocusWithin)
            return false;
        return true;
    }

    private bool CanNavigateCart()
    {
        if (_cart.Count == 0 || IsLookupOpen())
            return false;
        if (QtyBox.IsKeyboardFocusWithin || F6QtyBox.IsKeyboardFocusWithin)
            return false;
        return true;
    }

    private bool NavigateCart(int delta)
    {
        if (_cart.Count == 0)
            return false;

        var idx = CartGrid.SelectedIndex;
        if (idx < 0)
            idx = delta > 0 ? 0 : _cart.Count - 1;
        else
            idx = Math.Clamp(idx + delta, 0, _cart.Count - 1);

        CartGrid.SelectedIndex = idx;
        CartGrid.ScrollIntoView(CartGrid.SelectedItem);
        return true;
    }

    private void PdvWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            if (GatePanel.Visibility != Visibility.Visible)
            {
                var delta = e.Key == Key.Down ? 1 : -1;
                if (CanNavigateLookup())
                {
                    MoveLookupSelection(delta);
                    e.Handled = true;
                    return;
                }
                if (CanNavigateCart() && NavigateCart(delta))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Escape)
        {
            if (_f6Quantity.IsEditing || _scanMultiplier.IsArmed)
            {
                ClearScanMultiplier();
                HideLookup();
                SearchBox.Focus();
                e.Handled = true;
                return;
            }
            if (LookupPanel.Visibility == Visibility.Visible)
            {
                HideLookup();
                SearchBox.Focus();
                e.Handled = true;
                return;
            }
            ClosePdv_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (GatePanel.Visibility == Visibility.Visible)
            return;

        if (e.Key == Key.F12)
        {
            if (_f6Quantity.IsEditing)
                ClearScanMultiplier();
            SearchBox.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6)
        {
            BeginF6QuantityMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            OpenCaixa_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            OpenResumo_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F9)
        {
            OpenConsulta_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F10 && _cart.Count > 0)
        {
            OpenPayment();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && CartGrid.SelectedItem is PdvCartLine line)
        {
            _cart.Remove(line);
            RefreshCartGrid();
            var cartTotal = _cart.Sum(x => x.Subtotal);
            UpdateGrandTotal();
            AuditService.LogJson("remover", "item", line.ProductId.ToString(),
                AuditPayloadBuilder.PdvRemoveItem(new
                {
                    product_id = line.ProductId,
                    code = line.Code,
                    name = line.Name,
                    quantity = line.Quantity,
                    unit_price = line.UnitPrice,
                    subtotal = line.Subtotal,
                }, cartTotal),
                $"Item removido do PDV: {line.Quantity:0.##}x {line.Name}");
            e.Handled = true;
        }
    }
}
