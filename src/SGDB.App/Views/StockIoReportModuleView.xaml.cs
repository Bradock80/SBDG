using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class StockIoReportModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    public StockIoReportModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            ApplyEsteMes();
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

    private void Hoje_Click(object sender, RoutedEventArgs e)
    {
        ApplyHoje();
        Load();
    }

    private void Ultimos7_Click(object sender, RoutedEventArgs e)
    {
        DateFrom.SetDate(DateTime.Today.AddDays(-6));
        DateTo.SetDate(DateTime.Today);
        Load();
    }

    private void EsteMes_Click(object sender, RoutedEventArgs e)
    {
        ApplyEsteMes();
        Load();
    }

    private void MesAnterior_Click(object sender, RoutedEventArgs e)
    {
        var firstThis = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var lastPrev = firstThis.AddDays(-1);
        DateFrom.SetDate(new DateTime(lastPrev.Year, lastPrev.Month, 1));
        DateTo.SetDate(lastPrev);
        Load();
    }

    private void Pesquisar_Click(object sender, RoutedEventArgs e) => Load();

    private void FilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Load();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Load();
            e.Handled = true;
        }
    }

    private void ApplyEsteMes()
    {
        DateFrom.SetDate(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        DateTo.SetDate(DateTime.Today);
    }

    private void ApplyHoje()
    {
        DateFrom.SetDate(DateTime.Today);
        DateTo.SetDate(DateTime.Today);
    }

    private void Load()
    {
        if (!DateFrom.TryGetDate(out var from) || !DateTo.TryGetDate(out var to))
        {
            MessageBox.Show("Selecione as datas inicial e final.", "Entradas e Saídas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var direction = DirectionBox.SelectedIndex switch
        {
            1 => StockIoDirectionFilter.Entradas,
            2 => StockIoDirectionFilter.Saidas,
            _ => StockIoDirectionFilter.Todas,
        };

        try
        {
            var result = StockIoService.List(from, to, direction, SearchBox.Text);
            Grid.ItemsSource = result.Rows;
            TotalEntradasText.Text = $"Entradas: {result.TotalEntradasDisplay}";
            TotalSaidasText.Text = $"Saídas: {result.TotalSaidasDisplay}";
            MetaText.Text = result.Registros == 0
                ? $"Nenhuma movimentação de {result.DateFrom:dd/MM/yyyy} a {result.DateTo:dd/MM/yyyy}."
                : $"{result.Registros} registro(s) · {result.DateFrom:dd/MM/yyyy} a {result.DateTo:dd/MM/yyyy}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Entradas e Saídas", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
