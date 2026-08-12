using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Services;

namespace SGDB.Views;

public partial class StockAbcModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    public StockAbcModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DateFrom.SelectedDate = DateTime.Today.AddDays(-30);
            DateTo.SelectedDate = DateTime.Today;
            Refresh();
            Focus();
        };
    }

    private void Refresh()
    {
        try
        {
            var result = ReportsService.ListCurvaAbc(DateFrom.SelectedDate, DateTo.SelectedDate);
            AbcGrid.ItemsSource = result.Rows;
            var capital = result.Rows.Sum(r => r.CapitalParado);
            SummaryText.Text =
                $"{result.Registros} produtos · A:{result.CountA} B:{result.CountB} C:{result.CountC} · " +
                $"Faturamento R$ {result.TotalValor:N2} · Capital parado R$ {capital:N2} · " +
                $"{result.DateFrom:dd/MM/yyyy} a {result.DateTo:dd/MM/yyyy}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Curva ABC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7) { Refresh(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
