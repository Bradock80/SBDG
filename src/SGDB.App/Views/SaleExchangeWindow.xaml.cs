using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class SaleExchangeWindow : Window
{
    private readonly ObservableCollection<SaleExchangeSaleItemVm> _returnItems = new();
    private readonly ObservableCollection<SaleExchangeNewItemVm> _newItems = new();
    private int? _saleId;
    private bool _busy;
    private int _lastDiffSign; // -1 refund, 0 zero, +1 pay — evita resetar forma a cada Recalc

    public SaleExchangeWindow(int? preselectedSaleId = null)
    {
        InitializeComponent();
        ReturnGrid.ItemsSource = _returnItems;
        NewGrid.ItemsSource = _newItems;
        _returnItems.CollectionChanged += (_, _) => Recalc();
        _newItems.CollectionChanged += (_, _) => Recalc();

        PayBox.ItemsSource = PaymentMethodsService.List()
            .Where(m => m.Active)
            .ToList();
        PayBox.SelectedIndex = 0;
        // Prefer Pix if available (caso da loja)
        var pix = PaymentMethodsService.List().FirstOrDefault(m =>
            m.Active && m.ApiLabel.Contains("Pix", StringComparison.OrdinalIgnoreCase));
        if (pix is not null)
            PayBox.SelectedItem = pix;

        Loaded += (_, _) =>
        {
            RefreshSales();
            if (preselectedSaleId is int id)
                LoadSale(id);
            SearchBox.Focus();
        };
    }

    private void RefreshSales()
    {
        try
        {
            SalesGrid.ItemsSource = SaleExchangeService.SearchSales(SearchBox.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Troca / Devolução", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshSales();

    private void SalesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SalesGrid.SelectedItem is SaleExchangeSearchRow row)
            LoadSale(row.Id);
    }

    private void LoadSale(int saleId)
    {
        try
        {
            var detail = PdvService.GetSaleDetail(saleId);
            if (detail.Cancelled)
            {
                MessageBox.Show("Venda cancelada.", "Troca / Devolução",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _saleId = saleId;
            SaleHeaderText.Text =
                $"Venda #{saleId} · {detail.CreatedAtBr} · {detail.PaymentLabel} · {ProductPriceHelper.MoneyBr(detail.Total)}" +
                (string.IsNullOrWhiteSpace(detail.CustomerName) ? "" : $" · {detail.CustomerName}");

            foreach (var item in _returnItems)
                item.PropertyChanged -= ReturnItem_PropertyChanged;
            _returnItems.Clear();
            foreach (var vm in SaleExchangeService.LoadReturnableItems(saleId))
            {
                vm.PropertyChanged += ReturnItem_PropertyChanged;
                _returnItems.Add(vm);
            }

            _newItems.Clear();
            Recalc();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Troca / Devolução", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ReturnItem_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Recalc();

    private void ReturnAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _returnItems)
            item.ReturnQty = item.AvailableQty;
        Recalc();
    }

    private void ReturnGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(Recalc);

    private void NewGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(Recalc);

    private void ProductSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshProductSuggestions();

    private void RefreshProductSuggestions()
    {
        var term = ProductSearchBox.Text?.Trim() ?? "";
        if (term.Length < 1)
        {
            HideProductSuggestions();
            return;
        }

        try
        {
            var list = PdvService.SearchProducts(term, 20);
            if (list.Count == 0)
            {
                HideProductSuggestions();
                return;
            }

            ProductSuggestList.ItemsSource = list;
            ProductSuggestList.SelectedIndex = 0;
            ProductSuggestPopup.IsOpen = true;
            ProductSuggestPopup.PlacementTarget = ProductSearchBox;
            // Largura acompanha o campo
            if (ProductSuggestPopup.Child is FrameworkElement fe)
                fe.MinWidth = Math.Max(280, ProductSearchBox.ActualWidth);
        }
        catch
        {
            HideProductSuggestions();
        }
    }

    private void HideProductSuggestions()
    {
        ProductSuggestPopup.IsOpen = false;
        ProductSuggestList.ItemsSource = null;
    }

    private void ProductSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ProductSuggestPopup.IsOpen || ProductSuggestList.Items.Count == 0)
            return;

        if (e.Key is Key.Down or Key.Up)
        {
            var count = ProductSuggestList.Items.Count;
            var idx = ProductSuggestList.SelectedIndex;
            if (e.Key == Key.Down)
                ProductSuggestList.SelectedIndex = idx < 0 ? 0 : Math.Min(count - 1, idx + 1);
            else
                ProductSuggestList.SelectedIndex = idx <= 0 ? 0 : idx - 1;
            if (ProductSuggestList.SelectedItem is not null)
                ProductSuggestList.ScrollIntoView(ProductSuggestList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideProductSuggestions();
            e.Handled = true;
        }
    }

    private void ProductSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (ProductSuggestPopup.IsOpen && ProductSuggestList.SelectedItem is Product picked)
        {
            AddProductToNewItems(picked);
            e.Handled = true;
            return;
        }

        AddProduct_Click(sender, e);
        e.Handled = true;
    }

    private void ProductSearchBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Fecha depois de um tick para permitir clique na lista
        Dispatcher.BeginInvoke(() =>
        {
            if (!ProductSuggestList.IsKeyboardFocusWithin && !ProductSearchBox.IsKeyboardFocusWithin)
                HideProductSuggestions();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ProductSuggestList_Click(object sender, MouseButtonEventArgs e)
    {
        if (ProductSuggestList.SelectedItem is Product p)
            AddProductToNewItems(p);
    }

    private void ProductSuggestList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ProductSuggestList.SelectedItem is Product p)
        {
            AddProductToNewItems(p);
            e.Handled = true;
        }
    }

    private void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        if (ProductSuggestPopup.IsOpen && ProductSuggestList.SelectedItem is Product selected)
        {
            AddProductToNewItems(selected);
            return;
        }

        var term = ProductSearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(term))
        {
            MessageBox.Show("Digite código ou nome do produto.", "Troca / Devolução",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var products = PdvService.SearchProducts(term, 15);
            if (products.Count == 0)
            {
                MessageBox.Show("Produto não encontrado.", "Troca / Devolução",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var exact = products.FirstOrDefault(p =>
                string.Equals(p.Code, term, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Barcode, term, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Name, term, StringComparison.OrdinalIgnoreCase));
            AddProductToNewItems(exact ?? products[0]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Troca / Devolução", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddProductToNewItems(Product product)
    {
        var existing = _newItems.FirstOrDefault(n => n.ProductId == product.Id);
        if (existing is not null)
        {
            existing.Qty += 1;
        }
        else
        {
            var vm = new SaleExchangeNewItemVm
            {
                ProductId = product.Id,
                ProductCode = product.Code ?? "",
                ProductName = product.Name,
                Unit = product.Unit,
                Qty = 1,
                UnitPrice = product.SalePrice,
            };
            vm.PropertyChanged += (_, _) => Recalc();
            _newItems.Add(vm);
        }

        HideProductSuggestions();
        ProductSearchBox.Clear();
        ProductSearchBox.Focus();
        Recalc();
    }

    private void RemoveNew_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SaleExchangeNewItemVm vm })
            _newItems.Remove(vm);
        Recalc();
    }

    private void Recalc()
    {
        var ret = ProductPriceHelper.RoundPrice(_returnItems.Sum(i => i.ReturnAmount));
        var neu = ProductPriceHelper.RoundPrice(_newItems.Sum(i => i.Amount));
        var diff = ProductPriceHelper.RoundPrice(neu - ret);

        ReturnTotalText.Text = ProductPriceHelper.MoneyBr(ret);
        NewTotalText.Text = ProductPriceHelper.MoneyBr(neu);
        DiffValueText.Text = ProductPriceHelper.MoneyBr(diff);

        if (diff > 0.009)
        {
            DiffLabelText.Text = "Cliente paga (complemento)";
            DiffValueText.Foreground = Brush("#15803D");
            PayLabelText.Text = "Forma do complemento";
            PayBox.IsEnabled = true;
            if (_lastDiffSign != 1)
            {
                var pix = PaymentMethodsService.List().FirstOrDefault(m =>
                    m.Active && m.ApiLabel.Contains("Pix", StringComparison.OrdinalIgnoreCase));
                if (pix is not null)
                    PayBox.SelectedItem = pix;
            }
            _lastDiffSign = 1;
        }
        else if (diff < -0.009)
        {
            DiffLabelText.Text = "Devolver ao cliente";
            DiffValueText.Foreground = Brush("#B91C1C");
            PayLabelText.Text = "Forma do reembolso";
            PayBox.IsEnabled = true;
            if (_lastDiffSign != -1)
            {
                var money = PaymentMethodsService.List().FirstOrDefault(m =>
                    m.Active && PaymentMethodsService.IsDinheiroLabel(m.ApiLabel));
                if (money is not null)
                    PayBox.SelectedItem = money;
            }
            _lastDiffSign = -1;
        }
        else
        {
            DiffLabelText.Text = "Diferença";
            DiffValueText.Foreground = Brush("#312E81");
            PayLabelText.Text = "Forma (sem diferença)";
            PayBox.IsEnabled = false;
            _lastDiffSign = 0;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (_saleId is not int saleId)
        {
            MessageBox.Show("Selecione uma venda na lista à esquerda.", "Troca / Devolução",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var returns = _returnItems
            .Where(i => i.ReturnQty > 0.0001)
            .Select(i => new SaleExchangeReturnLine { SaleItemId = i.SaleItemId, Qty = i.ReturnQty })
            .ToList();
        if (returns.Count == 0)
        {
            MessageBox.Show("Informe a quantidade a devolver em pelo menos um item.", "Troca / Devolução",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var news = _newItems
            .Where(i => i.Qty > 0.0001)
            .Select(i => new SaleExchangeNewLine
            {
                ProductId = i.ProductId,
                Qty = i.Qty,
                UnitPrice = i.UnitPrice,
            })
            .ToList();

        var ret = ProductPriceHelper.RoundPrice(_returnItems.Sum(i => i.ReturnAmount));
        var neu = ProductPriceHelper.RoundPrice(_newItems.Sum(i => i.Amount));
        var diff = ProductPriceHelper.RoundPrice(neu - ret);

        string? pay = null;
        if (Math.Abs(diff) >= 0.01)
        {
            pay = PayBox.SelectedItem is PaymentMethodRow m
                ? m.ApiLabel
                : PayBox.Text;
            if (string.IsNullOrWhiteSpace(pay))
            {
                MessageBox.Show("Selecione a forma de pagamento da diferença.", "Troca / Devolução",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var ask = diff > 0.009
            ? $"Confirmar troca?\n\nDevolvido: {ProductPriceHelper.MoneyBr(ret)}\nNovos: {ProductPriceHelper.MoneyBr(neu)}\nCliente paga: {ProductPriceHelper.MoneyBr(diff)} ({pay})"
            : diff < -0.009
                ? $"Confirmar devolução?\n\nDevolvido: {ProductPriceHelper.MoneyBr(ret)}\nNovos: {ProductPriceHelper.MoneyBr(neu)}\nDevolver ao cliente: {ProductPriceHelper.MoneyBr(Math.Abs(diff))} ({pay})"
                : $"Confirmar troca sem diferença?\n\nDevolvido: {ProductPriceHelper.MoneyBr(ret)}\nNovos: {ProductPriceHelper.MoneyBr(neu)}";

        if (MessageBox.Show(ask, "Troca / Devolução", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes)
            return;

        _busy = true;
        ConfirmBtn.IsEnabled = false;
        try
        {
            var result = SaleExchangeService.Confirm(new SaleExchangeRequest
            {
                OriginalSaleId = saleId,
                Returns = returns,
                NewItems = news,
                PaymentType = pay,
                Notes = NotesBox.Text,
            });

            var msg = result.Message;
            if (result.WarnManualPixRefund)
                msg += "\n\nLembrete: se o pagamento original foi PIX/cartão, faça o estorno manual no app/maquininha.";

            MessageBox.Show(msg, "Troca / Devolução", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Troca / Devolução", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            ConfirmBtn.IsEnabled = true;
        }
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
            if (ProductSuggestPopup.IsOpen)
            {
                HideProductSuggestions();
                e.Handled = true;
                return;
            }
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex)
    {
        var b = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
        if (b.CanFreeze) b.Freeze();
        return b;
    }
}
