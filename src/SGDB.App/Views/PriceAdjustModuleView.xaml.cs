using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PriceAdjustModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    private List<PriceAdjustRow> _rows = [];
    private bool _committing;
    private DispatcherTimer? _suggestTimer;
    private bool _suggestSuppress;

    public PriceAdjustModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            GroupBox.ItemsSource = new[] { "TODOS" }.Concat(ProductCatalogService.ListGroups()).ToList();
            BrandBox.ItemsSource = new[] { "TODOS" }.Concat(ProductCatalogService.ListBrands()).ToList();
            GroupBox.SelectedIndex = 0;
            BrandBox.SelectedIndex = 0;
            SearchBox.Focus();
        };
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suggestSuppress) return;
        _suggestTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _suggestTimer.Tick -= SuggestTimer_Tick;
        _suggestTimer.Tick += SuggestTimer_Tick;
        _suggestTimer.Stop();
        _suggestTimer.Start();
    }

    private void SuggestTimer_Tick(object? sender, EventArgs e)
    {
        _suggestTimer?.Stop();
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        var term = (SearchBox.Text ?? "").Trim();
        if (term.Length < 1)
        {
            SuggestPopup.IsOpen = false;
            SuggestList.ItemsSource = null;
            return;
        }

        var hits = ProductService.List(search: term, ativo: "ativos").Take(20).ToList();
        SuggestList.ItemsSource = hits;
        SuggestPopup.IsOpen = hits.Count > 0;
        if (hits.Count > 0)
            SuggestList.SelectedIndex = 0;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && SuggestPopup.IsOpen && SuggestList.Items.Count > 0)
        {
            SuggestList.Focus();
            if (SuggestList.SelectedIndex < 0)
                SuggestList.SelectedIndex = 0;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (SuggestPopup.IsOpen && SuggestList.SelectedItem is Product p)
                PickSuggestion(p);
            else
            {
                SuggestPopup.IsOpen = false;
                RunPreview();
            }
            return;
        }

        if (e.Key == Key.Escape && SuggestPopup.IsOpen)
        {
            SuggestPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!SuggestList.IsKeyboardFocusWithin && !SearchBox.IsKeyboardFocusWithin)
                SuggestPopup.IsOpen = false;
        });
    }

    private void SuggestList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SuggestList.SelectedItem is Product p)
            PickSuggestion(p);
    }

    private void SuggestList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SuggestList.SelectedItem is Product p)
        {
            e.Handled = true;
            PickSuggestion(p);
        }
        else if (e.Key == Key.Escape)
        {
            SuggestPopup.IsOpen = false;
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void PickSuggestion(Product product)
    {
        _suggestSuppress = true;
        SearchBox.Text = product.Name;
        _suggestSuppress = false;
        SuggestPopup.IsOpen = false;
        RunPreview();
        if (_rows.Count > 0)
            PriceGrid.SelectedIndex = 0;
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => RunPreview();

    private void MarginBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        RunPreview();
    }

    private void RunPreview()
    {
        try
        {
            double? margem = null;
            if (!string.IsNullOrWhiteSpace(MarginBox.Text))
            {
                var m = ProductPriceHelper.ParseBr(MarginBox.Text);
                if (m > 0) margem = m;
            }

            var group = GroupBox.Text;
            if (string.Equals(group, "TODOS", StringComparison.OrdinalIgnoreCase))
                group = null;

            var brand = BrandBox.Text;
            if (string.Equals(brand, "TODOS", StringComparison.OrdinalIgnoreCase))
                brand = null;

            _rows = PriceAdjustService.Preview(
                search: SearchBox.Text,
                brand: brand,
                group: group,
                novaMargem: margem,
                purchaseFrom: PurchaseFrom.SelectedDate,
                purchaseTo: PurchaseTo.SelectedDate).ToList();

            PriceGrid.ItemsSource = _rows;
            ApplyBtn.IsEnabled = _rows.Count > 0;

            var abaixo = _rows.Count(r => r.IsBelowCost);
            if (_rows.Count == 0)
            {
                SummaryText.Text = "Nenhum produto encontrado.";
            }
            else if (margem is not null)
            {
                SummaryText.Text = abaixo > 0
                    ? $"{_rows.Count} produto(s) com margem {margem:N2}% — {abaixo} com venda ≤ custo (vermelho)."
                    : $"{_rows.Count} produto(s) com margem {margem:N2}% aplicada. Revise e Aplicar (F9).";
            }
            else
            {
                SummaryText.Text = abaixo > 0
                    ? $"{_rows.Count} produto(s) — {abaixo} com alerta de prejuízo (vermelho)."
                    : $"{_rows.Count} produto(s) — edite as células amarela/verde e Aplicar (F9).";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ajusta Preço", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PriceCell_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (tb.IsKeyboardFocusWithin)
                tb.SelectAll();
        });
    }

    private void PriceCell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox tb)
            return;

        if (tb.DataContext is PriceAdjustRow row)
            PriceGrid.SelectedItem = row;

        if (!tb.IsKeyboardFocusWithin)
        {
            tb.Focus();
            e.Handled = true;
        }
    }

    private void PriceCell_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb)
            return;
        e.Handled = true;
        CommitPriceCell(tb);
        MoveFocusToNextEditable(tb);
    }

    private void PriceCell_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            CommitPriceCell(tb);
    }

    private void CommitPriceCell(TextBox tb)
    {
        if (_committing || tb.DataContext is not PriceAdjustRow row)
            return;

        var kind = tb.Tag as string ?? "";
        _committing = true;
        try
        {
            var value = ProductPriceHelper.ParseBr(tb.Text);
            if (kind == "compra")
            {
                row.PurchasePrice = value;
                tb.Text = row.PurchaseDisplay;
            }
            else if (kind == "venda")
            {
                row.NewSalePrice = value;
                tb.Text = row.NewSaleDisplay;
            }

            RefreshSummary();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ajusta Preço", MessageBoxButton.OK, MessageBoxImage.Warning);
            tb.Text = kind == "compra" ? row.PurchaseDisplay : row.NewSaleDisplay;
        }
        finally
        {
            _committing = false;
        }
    }

    private void MoveFocusToNextEditable(TextBox current)
    {
        var req = new TraversalRequest(FocusNavigationDirection.Next);
        current.MoveFocus(req);
    }

    private void RefreshSummary()
    {
        ApplyBtn.IsEnabled = _rows.Count > 0;
        var mudou = _rows.Count(r => r.IsModified);
        var abaixo = _rows.Count(r => r.IsBelowCost);
        if (mudou > 0)
        {
            SummaryText.Text = abaixo > 0
                ? $"{mudou} linha(s) alterada(s), {abaixo} com prejuízo (vermelho). Pressione Aplicar (F9)."
                : $"{mudou} linha(s) alterada(s). Pressione Aplicar (F9).";
        }
        else
        {
            SummaryText.Text = $"{_rows.Count} produto(s) — edite Pr.Compra ou Pr.Venda Novo.";
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox focused)
            CommitPriceCell(focused);

        if (_rows.Count == 0)
        {
            MessageBox.Show("Visualize os produtos (F7) antes de aplicar.", "Ajusta Preço",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var changed = _rows.Where(r => r.IsModified).ToList();
        if (changed.Count == 0)
        {
            MessageBox.Show(
                "Nenhum preço de compra ou venda alterado.\n\nEdite as células amarela (compra) ou verde (venda), ou informe Nova Margem % e Visualizar.",
                "Ajusta Preço", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var abaixo = changed.Where(r => r.IsBelowCost).ToList();
        if (abaixo.Count > 0)
        {
            if (MessageBox.Show(
                    $"{abaixo.Count} produto(s) com Pr.Venda Novo menor ou igual ao Pr.Custo (prejuízo).\n\nDeseja continuar mesmo assim?",
                    "Ajusta Preço — Alerta", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        var mudouCompra = changed.Any(r => r.PurchaseChanged);
        var mudouVenda = changed.Any(r => r.SaleChanged);
        var partes = new List<string>();
        if (mudouCompra) partes.Add("compra");
        if (mudouVenda) partes.Add("venda");

        if (MessageBox.Show(
                $"Aplicar novo preço de {string.Join(" e ", partes)} em {changed.Count} produto(s)?",
                "Ajusta Preço", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var items = changed.Select(r => (
                r.ProductId,
                r.NewSalePrice,
                r.PurchaseChanged ? (double?)r.PurchasePrice : null
            ));
            var n = PriceAdjustService.Apply(items);
            MessageBox.Show($"{n} produto(s) atualizado(s).", "Ajusta Preço",
                MessageBoxButton.OK, MessageBoxImage.Information);
            RunPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erro ao gravar preços:\n\n{ex.Message}",
                "Ajusta Preço", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.F7) { RunPreview(); e.Handled = true; }
        else if (e.Key == Key.F9) { Apply_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
