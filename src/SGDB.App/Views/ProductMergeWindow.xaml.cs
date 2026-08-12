using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class ProductMergeWindow : Window
{
    private readonly Product _keep;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public Product? MergedProduct { get; private set; }

    public ProductMergeWindow(Product keepProduct)
    {
        InitializeComponent();
        _keep = keepProduct;
        KeepSummaryText.Text =
            $"#{_keep.Id}  {_keep.Name}\n" +
            $"Barras: {_keep.BarcodeDisplay}  ·  Estoque: {_keep.StockDisplay}  ·  Preço: {_keep.SalePriceDisplay}";

        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            LoadCandidates();
        };

        Loaded += (_, _) =>
        {
            LoadCandidates();
            SearchBox.Focus();
        };
    }

    private Product? SelectedAbsorb => CandidatesGrid.SelectedItem as Product;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void LoadCandidates()
    {
        var items = ProductService.List(SearchBox.Text, "ativos")
            .Where(p => p.Id != _keep.Id)
            .Take(200)
            .ToList();
        CandidatesGrid.ItemsSource = items;
        UpdatePreview();
    }

    private void CandidatesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdatePreview();

    private void UpdatePreview()
    {
        var absorb = SelectedAbsorb;
        if (absorb is null)
        {
            PreviewText.Text = "Selecione o produto duplicado na lista.";
            return;
        }

        var newStock = _keep.Stock + absorb.Stock;
        var barcode = string.IsNullOrWhiteSpace(_keep.Barcode) ? absorb.BarcodeDisplay : _keep.BarcodeDisplay;
        PreviewText.Text =
            $"Será mantido: #{_keep.Id} {_keep.Name}\n" +
            $"Será inativado: #{absorb.Id} {absorb.Name}\n" +
            $"Estoque: {_keep.Stock:G} + {absorb.Stock:G} = {newStock:G}\n" +
            $"Código de barras resultante: {barcode}\n" +
            $"Preço de venda: mantém o do principal ({_keep.SalePriceDisplay})\n" +
            "(No notebook a unificação roda no PC da loja automaticamente.)";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var absorb = SelectedAbsorb;
        if (absorb is null)
        {
            MessageBox.Show("Selecione o produto duplicado a juntar.", "Unificar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Unificar #{absorb.Id} \"{absorb.Name}\" em #{_keep.Id} \"{_keep.Name}\"?\n\n" +
            $"Estoque final: {_keep.Stock + absorb.Stock:G}\n" +
            "O duplicado será inativado e o histórico (vendas/compras) passará para o principal.",
            "Confirmar unificação",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            MergedProduct = ProductService.MergeProducts(_keep.Id, absorb.Id);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Unificar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            Confirm_Click(sender, e);
            e.Handled = true;
        }
    }
}
