using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Application.OpenTabs;
using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class OpenTabDetailWindow : Window
{
    private readonly int _tabId;
    private readonly DispatcherTimer _lookupTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private OpenTabDetail? _detail;
    private Product? _selectedLookup;
    private bool _suppressSearchChange;

    public OpenTabDetailWindow(int tabId)
    {
        _tabId = tabId;
        InitializeComponent();
        // Search/Qty tratam Enter sozinhos (não avançar como Tab)
        InputUxHelper.Attach(this, SearchBox, QtyBox);
        _lookupTimer.Tick += (_, _) =>
        {
            _lookupTimer.Stop();
            RunLiveLookup();
        };
        Loaded += (_, _) =>
        {
            Reload();
            SearchBox.Focus();
        };
    }

    private OpenTabItemRow? SelectedItem => ItemsGrid.SelectedItem as OpenTabItemRow;

    private void Reload()
    {
        try
        {
            _detail = OpenTabService.Get(_tabId);
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        Title = $"Deck — {_detail.Name}";
        TitleText.Text = _detail.Name;
        var opened = DateBrHelper.FormatUtcToBrazil(_detail.CreatedAt, "dd/MM HH:mm");
        var notesBit = string.IsNullOrWhiteSpace(_detail.Notes) ? "" : $" · {_detail.Notes.Trim()}";
        SubtitleText.Text = _detail.CustomerName is { Length: > 0 } c
            ? $"Cliente: {c} · Aberto {opened}{notesBit}"
            : $"Aberto {opened}{notesBit} · {_detail.Items.Count} item(ns) · F9 pré-conta · F10 cobra";
        TotalText.Text = _detail.TotalDisplay;
        ItemsGrid.ItemsSource = _detail.Items;

        var canEdit = _detail.IsOpen;
        SearchBox.IsEnabled = canEdit;
        QtyBox.IsEnabled = canEdit;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchChange)
            return;

        // Digitou de novo: não mantém produto antigo selecionado
        if (_selectedLookup is not null
            && !string.Equals(SearchBox.Text.Trim(), _selectedLookup.Name, StringComparison.OrdinalIgnoreCase))
            _selectedLookup = null;

        var term = SearchBox.Text.Trim();
        if (LooksLikeBarcode(term))
        {
            _lookupTimer.Stop();
            HideLookup(clearSelection: false);
            return;
        }

        _lookupTimer.Stop();
        _lookupTimer.Start();
    }

    private void RunLiveLookup()
    {
        var term = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(term) || LooksLikeBarcode(term))
        {
            HideLookup(clearSelection: false);
            return;
        }

        var list = PdvService.SearchProducts(term, 60);
        if (list.Count == 0)
        {
            HideLookup(clearSelection: false);
            return;
        }

        var keepId = _selectedLookup?.Id;
        LookupList.ItemsSource = list;
        LookupPanel.Visibility = Visibility.Visible;

        var idx = keepId is int id
            ? list.ToList().FindIndex(p => p.Id == id)
            : 0;
        if (idx < 0) idx = 0;
        LookupList.SelectedIndex = idx;
        if (LookupList.SelectedItem is not null)
            LookupList.ScrollIntoView(LookupList.SelectedItem);
        _selectedLookup = LookupList.SelectedItem as Product ?? list[0];
    }

    private void HideLookup(bool clearSelection = true)
    {
        LookupPanel.Visibility = Visibility.Collapsed;
        LookupList.ItemsSource = null;
        if (clearSelection)
            _selectedLookup = null;
    }

    private bool IsLookupOpen() =>
        LookupPanel.Visibility == Visibility.Visible && LookupList.Items.Count > 0;

    private void MoveLookupSelection(int delta)
    {
        if (!IsLookupOpen())
            return;

        var count = LookupList.Items.Count;
        var next = LookupList.SelectedIndex + delta;
        if (LookupList.SelectedIndex < 0)
            next = 0;
        if (next < 0) next = 0;
        if (next >= count)
            next = count - 1;

        LookupList.SelectedIndex = next;
        if (LookupList.SelectedItem is not null)
            LookupList.ScrollIntoView(LookupList.SelectedItem);
        if (LookupList.SelectedItem is Product p)
            _selectedLookup = p;
    }

    private static Key ResolveKey(KeyEventArgs e)
    {
        if (e.Key == Key.System)
            return e.SystemKey;
        if (e.Key == Key.ImeProcessed)
            return e.ImeProcessedKey;
        return e.Key;
    }

    private bool TryNavigateLookup(KeyEventArgs e)
    {
        var key = ResolveKey(e);
        if (key is not (Key.Up or Key.Down))
            return false;
        if (!IsLookupOpen())
            return false;
        if (QtyBox.IsKeyboardFocusWithin)
            return false;

        MoveLookupSelection(key == Key.Down ? 1 : -1);
        e.Handled = true;
        return true;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e) => TryNavigateLookup(e);

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        var key = ResolveKey(e);
        if (key is Key.Up or Key.Down)
            return;

        if (key == Key.Enter)
        {
            if (IsLookupOpen() && LookupList.SelectedItem is Product)
                ConfirmLookupAndFocusQty();
            else if (_selectedLookup is not null)
                FocusQty();
            else
                TryAdd();
            e.Handled = true;
        }
        else if (key == Key.Escape && IsLookupOpen())
        {
            HideLookup();
            e.Handled = true;
        }
    }

    private void QtyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (ResolveKey(e) == Key.Enter)
        {
            TryAdd();
            e.Handled = true;
        }
    }

    private void QtyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        QtyBox.SelectAll();

    private void LookupList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        ConfirmLookupAndFocusQty();

    private void LookupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LookupList.SelectedItem is Product p)
            _selectedLookup = p;
    }

    /// <summary>Escolhe o produto da lista e vai para a quantidade (ainda não lança).</summary>
    private void ConfirmLookupAndFocusQty()
    {
        if (LookupList.SelectedItem is not Product p)
            return;

        _selectedLookup = p;
        _suppressSearchChange = true;
        SearchBox.Text = p.Name;
        _suppressSearchChange = false;
        HideLookup(clearSelection: false);
        FocusQty();
    }

    private void FocusQty()
    {
        QtyBox.Focus();
        Keyboard.Focus(QtyBox);
        QtyBox.SelectAll();
        Dispatcher.BeginInvoke(() =>
        {
            if (!QtyBox.IsKeyboardFocusWithin)
            {
                QtyBox.Focus();
                Keyboard.Focus(QtyBox);
            }
            QtyBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (IsLookupOpen() && LookupList.SelectedItem is Product)
        {
            ConfirmLookupAndFocusQty();
            return;
        }
        TryAdd();
    }

    private void TryAdd()
    {
        if (_detail is null || !_detail.IsOpen)
            return;

        var qty = ParseQty();

        if (_selectedLookup is not null)
        {
            AddProductDirect(_selectedLookup, qty);
            return;
        }

        var term = SearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(term))
        {
            SearchBox.Focus();
            return;
        }

        try
        {
            // Localiza produto sem abrir UI no service; modalidade só na View.
            var scan = PdvService.ResolveScan(term);
            if (scan is not null
                && ProductClassificationHelper.IsCigarette(scan.Product.Name, scan.Product.GroupName))
            {
                if (!TryResolveCigaretteSaleForDeck(scan.Product, out var chosen) || chosen is null)
                    return;
                OpenTabService.AddProduct(
                    _tabId,
                    chosen.Product.Id,
                    qty * chosen.Quantity,
                    chosen.UnitPrice,
                    chosen.StockUnitsPerSale,
                    PdvCartHelper.LineDisplayName(chosen.Product, chosen.ModeLabel));
                AfterAddOk();
                return;
            }

            // Não-cigarro (inclui CX/fardo): comportamento histórico de AddFromScan.
            OpenTabService.AddFromScan(_tabId, term, qty);
            AfterAddOk();
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            SearchBox.SelectAll();
            SearchBox.Focus();
        }
        catch (PdvException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            SearchBox.SelectAll();
            SearchBox.Focus();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            SearchBox.SelectAll();
            SearchBox.Focus();
        }
    }

    private void AddProductDirect(Product product, double qty)
    {
        if (_detail is null || !_detail.IsOpen)
            return;

        try
        {
            if (ProductClassificationHelper.IsCigarette(product.Name, product.GroupName))
            {
                if (!TryResolveCigaretteSaleForDeck(product, out var chosen) || chosen is null)
                    return;
                OpenTabService.AddProduct(
                    _tabId,
                    chosen.Product.Id,
                    qty * chosen.Quantity,
                    chosen.UnitPrice,
                    chosen.StockUnitsPerSale,
                    PdvCartHelper.LineDisplayName(chosen.Product, chosen.ModeLabel));
            }
            else
            {
                OpenTabService.AddProduct(_tabId, product.Id, qty);
            }

            AfterAddOk();
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            SearchBox.SelectAll();
            SearchBox.Focus();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            SearchBox.SelectAll();
            SearchBox.Focus();
        }
    }

    /// <summary>
    /// Cigarro com PrecoAvulso → diálogo; sem PrecoAvulso → MAÇO direto.
    /// Retorna false se o operador cancelar (Esc).
    /// </summary>
    private bool TryResolveCigaretteSaleForDeck(Product product, out PdvScanResult? chosen)
    {
        chosen = null;

        if (PdvCartHelper.NeedsCigaretteModeChoice(product))
        {
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

        // Cigarro sem preço avulso: MAÇO explícito (corrige SalePrice + fator 1).
        chosen = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);
        return true;
    }

    private double ParseQty()
    {
        var qty = ProductPriceHelper.ParseBr(QtyBox.Text);
        return qty > 0 ? qty : 1;
    }

    private void AfterAddOk()
    {
        HideLookup();
        _selectedLookup = null;
        _suppressSearchChange = true;
        SearchBox.Clear();
        _suppressSearchChange = false;
        QtyBox.Text = "1";
        Reload();
        SearchBox.Focus();
    }

    private static bool LooksLikeBarcode(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return false;
        var digits = new string(term.Where(char.IsDigit).ToArray());
        if (digits.Length >= 8)
            return true;
        return digits.Length >= 4 && digits.Length == term.Trim().Length;
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => TryRemoveSelected();

    private void TryRemoveSelected()
    {
        if (SelectedItem is null)
        {
            MessageBox.Show("Selecione um item para remover.", "Decks",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            OpenTabService.RemoveItem(SelectedItem.Id);
            Reload();
            SearchBox.Focus();
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelTab_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null || !_detail.IsOpen)
            return;

        var ask = MessageBox.Show(
            $"Cancelar o deck \"{_detail.Name}\"?\nOs itens lançados serão descartados (estoque não foi baixado).",
            "Decks", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
            return;

        try
        {
            OpenTabService.Cancel(_tabId);
            DialogResult = true;
            Close();
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Settle_Click(object sender, RoutedEventArgs e) => Settle();

    private void PreConta_Click(object sender, RoutedEventArgs e) => PrintPreConta();

    private void PrintPreConta()
    {
        try
        {
            OpenTabService.PrintPreConta(_tabId);
            MessageBox.Show("Pré-conta enviada à impressora.", "Decks",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Decks — Pré-conta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Settle()
    {
        if (_detail is null || !_detail.IsOpen)
            return;

        if (!CashService.IsOperational())
        {
            MessageBox.Show("Abra o caixa antes de cobrar o deck.", "Decks",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        List<PdvCartLine> lines;
        try
        {
            lines = OpenTabService.ToCartLines(_tabId).ToList();
            PdvService.ValidateItemsBeforePayment(lines);
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (PdvException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var subtotal = ProductPriceHelper.RoundPrice(lines.Sum(c => c.Subtotal));
        OfferSplitInfo(subtotal);

        while (true)
        {
            var pay = new PdvPaymentWindow(subtotal, lines) { Owner = this };
            if (pay.ShowDialog() != true || !pay.Confirmed)
                return;

            try
            {
                var customerId = pay.CustomerPersonId ?? _detail.CustomerId;
                var result = ApplicationServices.SettleOpenTab.Execute(new SettleOpenTabCommand
                {
                    TabId = _tabId,
                    Items = lines.Select(l => new SaleLine
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
                    CustomerPersonId = customerId,
                    SellerId = pay.SellerId,
                });

                PeripheralService.TryOpenCashDrawerAfterCashSale(pay.Payments);

                AuditService.LogJson("venda", "deck", result.SaleId.ToString(),
                    new
                    {
                        tab_id = _tabId,
                        tab_name = _detail.Name,
                        sale_id = result.SaleId,
                        total = result.Total,
                        payment_type = pay.SelectedPaymentType,
                    },
                    $"Deck {_detail.Name} · {pay.SelectedPaymentType} · R$ {result.Total:N2}");

                var cupom = new PdvCupomConfirmWindow(
                    result.SaleId,
                    lines,
                    pay.Payments,
                    subtotal,
                    pay.Discount,
                    pay.Surcharge,
                    result.Total,
                    result.CashReceived,
                    result.ChangeAmount)
                {
                    Owner = this,
                };
                cupom.ShowDialog();

                DialogResult = true;
                Close();
                return;
            }
            catch (PdvException ex)
            {
                MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (OpenTabException ex)
            {
                MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
    }

    /// <summary>Mostra quanto dá por pessoa se o usuário quiser dividir a conta.</summary>
    private void OfferSplitInfo(double total)
    {
        var ask = MessageBox.Show(
            $"Total da conta: {ProductPriceHelper.MoneyBr(total)}\n\n" +
            "Deseja ver a divisão por pessoas (split)?",
            "Dividir conta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
            return;

        var nText = PromptSplitPeople();
        if (nText is null)
            return;
        if (!int.TryParse(nText, out var n) || n < 2 || n > 50)
        {
            MessageBox.Show("Informe um número de pessoas entre 2 e 50.", "Dividir conta",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var each = ProductPriceHelper.RoundPrice(total / n);
        MessageBox.Show(
            $"Total: {ProductPriceHelper.MoneyBr(total)}\n" +
            $"Pessoas: {n}\n" +
            $"Cada um: {ProductPriceHelper.MoneyBr(each)}\n\n" +
            "A cobrança no caixa continua pelo valor total da conta.",
            "Dividir conta",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private string? PromptSplitPeople()
    {
        var dlg = new Window
        {
            Title = "Dividir conta",
            Width = 320,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Tahoma, Segoe UI"),
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Quantas pessoas?",
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8),
        });
        var box = new TextBox
        {
            Text = "2",
            Height = 32,
            FontSize = 16,
            Padding = new Thickness(8, 4, 8, 4),
        };
        panel.Children.Add(box);
        string? result = null;
        var ok = new Button
        {
            Content = "Calcular",
            IsDefault = true,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(16, 6, 16, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Click += (_, _) =>
        {
            result = box.Text?.Trim();
            dlg.DialogResult = true;
        };
        panel.Children.Add(ok);
        dlg.Content = panel;
        dlg.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dlg.ShowDialog() == true ? result : null;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryNavigateLookup(e))
            return;

        var key = ResolveKey(e);
        if (key == Key.Escape)
        {
            if (IsLookupOpen())
            {
                HideLookup();
                e.Handled = true;
                return;
            }
            Close();
            e.Handled = true;
        }
        else if (key == Key.F9)
        {
            PrintPreConta();
            e.Handled = true;
        }
        else if (key == Key.F10)
        {
            Settle();
            e.Handled = true;
        }
        else if (key == Key.Delete && !SearchBox.IsKeyboardFocusWithin && !QtyBox.IsKeyboardFocusWithin)
        {
            TryRemoveSelected();
            e.Handled = true;
        }
    }
}
