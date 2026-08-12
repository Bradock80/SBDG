using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SGDB.Application.Sales;
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
    private int _lineCounter;
    private long _lastBuscaKeyAt;
    private bool _suppressSearchChange;

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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TitleText.Text = $"PDV - Venda de Balcão | CAIXA | {DateTime.Today:dd/MM/yyyy}";
        StatusTerminalText.Text = $"Terminal: {Environment.MachineName}";
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

        if (string.IsNullOrEmpty(term))
        {
            if (_pendingProduct is not null)
            {
                QtyBox.Focus();
                QtyBox.SelectAll();
                return;
            }
            if (_cart.Count > 0)
                OpenPayment();
            return;
        }

        if (LookupPanel.Visibility == Visibility.Visible && LookupGrid.Items.Count > 0)
        {
            SelectLookupProduct();
            return;
        }

        if (_pendingProduct is not null && BuscaMatchesPending())
        {
            QtyBox.Focus();
            QtyBox.SelectAll();
            return;
        }

        if (LooksLikeBarcode(term))
        {
            var scan = PdvService.ResolveScan(term);
            if (scan is not null)
            {
                SetPendingFromScan(scan);
                if (scan.IsPackSale)
                    TryIncludePending();
                return;
            }
            if (DigitsOnly(term).Length >= 8)
            {
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
        CurrentPriceText.Text = "0,00";
        CurrentItemTotalText.Text = "0,00";
    }

    private void SetPendingProductPreview(Product product)
    {
        _pendingProduct = product;
        _pendingUnitPrice = product.SalePrice;
        CurrentPriceText.Text = product.SalePriceDisplay;
        UpdateItemTotalPreview();
    }

    private void SetPendingProduct(Product product)
    {
        SetPendingFromScan(PdvService.ResolveManualSale(product));
    }

    private void SetPendingFromScan(PdvScanResult scan)
    {
        _pendingProduct = scan.Product;
        _pendingUnitPrice = scan.UnitPrice;
        _pendingStockUnitsPerSale = scan.StockUnitsPerSale;
        HideLookup();
        CurrentPriceText.Text = ProductPriceHelper.FormatBr(scan.UnitPrice);
        QtyBox.Text = scan.Quantity.ToString("0.000", CultureInfo.GetCultureInfo("pt-BR"));
        UpdateItemTotalPreview();
        var label = scan.IsPackSale
            ? $"{scan.Product.Name} [{scan.ModeLabel}]"
            : scan.Product.Name;
        _pendingBuscaLabel = label;
        _suppressSearchChange = true;
        SearchBox.Text = label;
        _suppressSearchChange = false;
        if (!scan.IsPackSale)
        {
            QtyBox.Focus();
            QtyBox.SelectAll();
        }
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
        var unit = ResolveLineUnitPrice(_pendingProduct, qty, _pendingUnitPrice, _pendingStockUnitsPerSale);
        CurrentPriceText.Text = ProductPriceHelper.FormatBr(unit);
        CurrentItemTotalText.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.RoundPrice(qty * unit));
    }

    private void TryIncludePending()
    {
        if (_pendingProduct is null)
            return;

        var qty = ParseQty(QtyBox.Text);
        if (qty <= 0)
        {
            MessageBox.Show("Quantidade inválida.", "PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            QtyBox.Focus();
            return;
        }

        var unitPrice = ResolveLineUnitPrice(_pendingProduct, qty, _pendingUnitPrice, _pendingStockUnitsPerSale);

        // Junta linhas do mesmo produto (mesmo modo de estoque)
        var existing = _cart.FirstOrDefault(c =>
            c.ProductId == _pendingProduct.Id
            && Math.Abs(c.StockUnitsPerSale - _pendingStockUnitsPerSale) < 0.0001);
        if (existing is not null)
        {
            var newQty = ProductPriceHelper.RoundPrice(existing.Quantity + qty);
            var mergedPrice = ResolveLineUnitPrice(_pendingProduct, newQty, _pendingUnitPrice, _pendingStockUnitsPerSale);
            _cart.Remove(existing);
            _cart.Add(new PdvCartLine
            {
                ProductId = existing.ProductId,
                Code = existing.Code,
                Name = existing.Name,
                Unit = existing.Unit,
                Quantity = newQty,
                UnitPrice = mergedPrice,
                StockUnitsPerSale = existing.StockUnitsPerSale,
            });
        }
        else
        {
            _cart.Add(new PdvCartLine
            {
                LineNum = ++_lineCounter,
                ProductId = _pendingProduct.Id,
                Code = _pendingProduct.Code ?? "",
                Name = _pendingProduct.Name,
                Unit = _pendingProduct.Unit,
                Quantity = qty,
                UnitPrice = unitPrice,
                StockUnitsPerSale = _pendingStockUnitsPerSale,
            });
        }

        _pendingProduct = null;
        _pendingUnitPrice = 0;
        _pendingStockUnitsPerSale = 1;
        _pendingBuscaLabel = null;
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

    private static double ResolveLineUnitPrice(
        Product product, double qty, double pendingUnitPrice, double stockUnitsPerSale = 1)
    {
        // Maço cigarro: preço fixo por maço (qtd 1, 2, 3…)
        if (stockUnitsPerSale > 1.0001
            && ProductClassificationHelper.IsCigarette(product.Name, product.GroupName)
            && pendingUnitPrice > 0)
            return pendingUnitPrice;

        var extra = ProductExtra.Parse(product.ExtraJson);
        var packQty = extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem
            : (extra.QtdAtacado >= 2 ? extra.QtdAtacado : 0);

        // Bipou CX/fardo (refrigerante): preço unitário só na qtd exata do fardo
        if (pendingUnitPrice > 0 && packQty >= 2 && extra.PrecoAtacado > 0)
        {
            var packUnit = PdvService.WholesaleUnitPrice(product.SalePrice, extra.PrecoAtacado, packQty);
            if (Math.Abs(pendingUnitPrice - packUnit) < 0.009)
            {
                if (Math.Abs(qty - packQty) < 0.0001)
                    return packUnit;
                return PdvService.UnitPriceForQuantity(product, qty);
            }
        }

        if (pendingUnitPrice > 0 && Math.Abs(qty - 1) < 0.0001)
            return pendingUnitPrice;

        return PdvService.UnitPriceForQuantity(product, qty);
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

                    if (pay.PixPaidAmount > 0.009)
                    {
                        MessageBox.Show(
                            $"Atenção: já houve PIX de R$ {pay.PixPaidAmount:N2} nesta tentativa.\n" +
                            "Se for cobrar de novo, devolva o PIX anterior no Mercado Pago " +
                            "ou use outra forma sem gerar novo QR.",
                            "PIX já recebido",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

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
            var pid = pay.PixPaymentId is long id ? $"\nPagamento Mercado Pago #{id}" : "";
            MessageBox.Show(
                $"{message}\n\n" +
                $"ATENÇÃO: o cliente JÁ PAGOU R$ {pay.PixPaidAmount:N2} via PIX e a venda não foi registrada." + pid +
                "\n\nResolva o problema acima e lance a venda novamente, ou devolva o valor pelo Mercado Pago.",
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
        if (QtyBox.IsKeyboardFocusWithin)
            return false;
        return true;
    }

    private bool CanNavigateCart()
    {
        if (_cart.Count == 0 || IsLookupOpen())
            return false;
        if (QtyBox.IsKeyboardFocusWithin)
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
            SearchBox.Focus();
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
