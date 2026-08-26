using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

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

        var keepExtra = ProductExtra.Parse(_keep.ExtraJson);
        var absorbExtra = ProductExtra.Parse(absorb.ExtraJson);
        var isCig = ProductClassificationHelper.UsesPackPurchasePrice(_keep.Name, _keep.GroupName);
        var packFactor = ProductMergeRules.ResolvePackFactor(_keep.Name, _keep.GroupName, keepExtra);
        double avgCost;
        try
        {
            avgCost = ProductMergeRules.WeightedPhysicalAverage(
                _keep.Stock, _keep.StockFridge, _keep.CostPrice,
                absorb.Stock, absorb.StockFridge, absorb.CostPrice,
                isCig, packFactor);
        }
        catch
        {
            avgCost = _keep.CostPrice;
        }

        var eans = new List<string>();
        void AddEan(string? label, string? bc)
        {
            var d = TextNorm.NormalizeBarcode(bc);
            if (d is null)
                return;
            eans.Add($"{label}:{d}");
        }
        AddEan("A", _keep.Barcode);
        AddEan("A-pack", keepExtra.BarcodeEmbalagem);
        AddEan("B→alias", absorb.Barcode);
        AddEan("B-pack", absorbExtra.BarcodeEmbalagem);

        var eanLine = eans.Count == 0 ? "-" : string.Join(" | ", eans);
        PreviewText.Text =
            $"Mantem #{_keep.Id} {_keep.Name} / inativa #{absorb.Id} {absorb.Name}\n" +
            $"Deposito: {_keep.Stock:G} + {absorb.Stock:G} = {_keep.Stock + absorb.Stock:G}  |  " +
            $"Geladeira: {_keep.StockFridge:G} + {absorb.StockFridge:G} = {_keep.StockFridge + absorb.StockFridge:G}\n" +
            $"Custo: {_keep.CostPrice:N2} / {absorb.CostPrice:N2} -> medio {avgCost:N2}  |  " +
            $"Venda: mantem {_keep.SalePriceDisplay}\n" +
            $"EANs preservados: {eanLine}";
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
            $"Estoque final: {_keep.Stock + absorb.Stock:G} (depósito) + " +
            $"{_keep.StockFridge + absorb.StockFridge:G} (geladeira)\n" +
            "EANs do duplicado passam a reconhecer o principal.\n" +
            "O duplicado será inativado e o histórico passará para o principal.",
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
