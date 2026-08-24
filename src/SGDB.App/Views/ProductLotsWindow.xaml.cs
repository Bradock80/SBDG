using System.Windows;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class ProductLotsWindow : Window
{
    private readonly int _productId;
    private readonly string _productName;

    public ProductLotsWindow(int productId, string? productName = null)
    {
        _productId = productId;
        _productName = (productName ?? "").Trim();
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => Reload();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Reload();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Reload()
    {
        try
        {
            var product = ProductService.GetById(_productId);
            var name = string.IsNullOrWhiteSpace(_productName)
                ? product?.Name ?? $"#{_productId}"
                : _productName;
            Title = $"Lotes e validades — {name}";
            TitleText.Text = Title;

            var lots = ProductLotService.ListByProduct(_productId);
            var rows = ProductLotListRow.FromLots(lots);
            Grid.ItemsSource = rows;

            var next = ProductExpiryService.NextFromLots(lots);
            NextExpiryText.Text = ProductExpiryService.FormatDisplay(next);
            var status = ProductExpiryService.Classify(next);
            DaysHintText.Text = status.Days is int d
                ? (d < 0 ? $"Vencido há {-d} dia(s)." : d == 0 ? "Vence hoje." : $"Vence em {d} dia(s).")
                : "Sem validade informada nos lotes ativos.";

            StockText.Text = product is null
                ? "—"
                : ProductLotListRow.FormatQty(product.TotalStock);

            var empty = rows.Count == 0;
            EmptyText.Text = "Este produto não possui lotes ativos com estoque.";
            EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            Grid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            Grid.ItemsSource = null;
            Grid.Visibility = Visibility.Collapsed;
            EmptyText.Text = ex.Message;
            EmptyText.Visibility = Visibility.Visible;
        }
    }
}
