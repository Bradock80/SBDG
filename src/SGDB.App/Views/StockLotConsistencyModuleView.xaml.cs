using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class StockLotConsistencyModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    public StockLotConsistencyModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            Load();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Load();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Load();
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { Load(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }

    private void Load()
    {
        try
        {
            var rows = StockLotConsistencyService.List(new StockLotConsistencyQuery
            {
                OnlyDivergent = OnlyDivergentBox.IsChecked == true,
                OnlyWithLotsOrExpiryControl = OnlyLotsBox.IsChecked == true,
                Search = SearchBox.Text,
            });
            Grid.ItemsSource = rows;
            SummaryText.Text = rows.Count == 0
                ? "Nenhuma divergência no filtro atual."
                : $"{rows.Count} produto(s) · diferença = estoque global − soma dos lotes · somente leitura";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Consistência Estoque × Lotes",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
