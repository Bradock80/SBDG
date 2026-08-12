using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PdvTrocarProdutoWindow : Window
{
    private readonly PdvSaleItemRow _item;
    private readonly DispatcherTimer _searchTimer;
    public Product? SelectedProduct { get; private set; }
    public double NewQuantity { get; private set; }
    public bool KeepLinePrice => ManterPrecoCheck.IsChecked == true;
    public bool Confirmed { get; private set; }

    public PdvTrocarProdutoWindow(PdvSaleItemRow item)
    {
        _item = item;
        InitializeComponent();
        DeText.Text = $"De: {_item.ProductCode} — {_item.ProductName}  ·  Qtd {_item.QuantityDisplay}  ·  {_item.UnitPriceDisplay}";
        QtdBox.Text = ProductPriceHelper.FormatBr(_item.Quantity);
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RunSearch();
        };
        Loaded += (_, _) => BuscaBox.Focus();
        UpdatePreview();
    }

    private double DraftQty()
    {
        var q = ProductPriceHelper.ParseBr(QtdBox.Text);
        return q > 0 ? q : _item.Quantity;
    }

    private void BuscaBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void QtdBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void RunSearch()
    {
        var term = BuscaBox.Text.Trim();
        if (term.Length < 1)
        {
            ProdutosGrid.ItemsSource = null;
            return;
        }
        // Inclui o produto atual para permitir só mudar a quantidade.
        var list = PdvService.SearchProducts(term, limit: 20).ToList();
        ProdutosGrid.ItemsSource = list;
        if (list.Count > 0)
            ProdutosGrid.SelectedIndex = 0;
        UpdatePreview();
    }

    private void ManterPreco_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void ProdutosGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (ProdutosGrid.SelectedItem is not Product p)
        {
            PrecoPreviewText.Text = "Selecione o produto substituto e a quantidade nova.";
            return;
        }
        var qty = DraftQty();
        var price = KeepLinePrice ? _item.UnitPrice : p.SalePrice;
        var newSub = ProductPriceHelper.RoundPrice(qty * price);
        var diff = ProductPriceHelper.RoundPrice(newSub - _item.Subtotal);
        var diffTxt = diff > 0.009 ? $" · cobrar + R$ {diff:N2}"
            : diff < -0.009 ? $" · devolver R$ {Math.Abs(diff):N2}"
            : "";
        PrecoPreviewText.Text =
            $"Novo: {p.Name} · Qtd {ProductPriceHelper.FormatBr(qty)} × R$ {price:N2} = R$ {newSub:N2}{diffTxt}";
    }

    private void ProdutosGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        QtdBox.Focus();
        QtdBox.SelectAll();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => TryConfirm();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TryConfirm()
    {
        if (ProdutosGrid.SelectedItem is not Product p)
        {
            MessageBox.Show("Selecione um produto.", "Trocar produto", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var qty = ProductPriceHelper.ParseBr(QtdBox.Text);
        if (qty <= 0)
        {
            MessageBox.Show("Informe a quantidade nova (maior que zero).", "Trocar produto",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            QtdBox.Focus();
            QtdBox.SelectAll();
            return;
        }

        SelectedProduct = p;
        NewQuantity = ProductPriceHelper.RoundPrice(qty);
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            if (Keyboard.FocusedElement == BuscaBox && ProdutosGrid.Items.Count > 0)
            {
                QtdBox.Focus();
                QtdBox.SelectAll();
                e.Handled = true;
                return;
            }
            TryConfirm();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Up or Key.Down && ProdutosGrid.Items.Count > 0
            && Keyboard.FocusedElement != QtdBox)
        {
            var idx = ProdutosGrid.SelectedIndex;
            if (e.Key == Key.Down && idx < ProdutosGrid.Items.Count - 1)
                ProdutosGrid.SelectedIndex = idx + 1;
            else if (e.Key == Key.Up && idx > 0)
                ProdutosGrid.SelectedIndex = idx - 1;
            UpdatePreview();
            e.Handled = true;
        }
    }
}
