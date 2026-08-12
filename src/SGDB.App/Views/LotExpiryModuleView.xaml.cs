using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Services;

namespace SGDB.Views;

public partial class LotExpiryModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    public LotExpiryModuleView(int? initialDays = null)
    {
        InitializeComponent();
        if (initialDays is 60) Days60.IsChecked = true;
        else if (initialDays is 90) Days90.IsChecked = true;
        else Days30.IsChecked = true;

        Loaded += (_, _) =>
        {
            Focus();
            Load();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Load();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void Days_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Load();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    private int SelectedDays() =>
        Days90.IsChecked == true ? 90 : Days60.IsChecked == true ? 60 : 30;

    private void Load()
    {
        try
        {
            var days = SelectedDays();
            var rows = ProductLotService.ListExpiring(days);
            Grid.ItemsSource = rows;
            var expired = rows.Count(r => r.DaysToExpiry is < 0);
            var crit = rows.Count(r => r.DaysToExpiry is >= 0 and <= 30);
            MetaText.Text = rows.Count == 0
                ? $"Nenhum lote com validade nos próximos {days} dias."
                : $"{rows.Count} lote(s) · {crit} em até 30 dias · {expired} já vencido(s). Priorize a saída (FEFO).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Validade por lote", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
