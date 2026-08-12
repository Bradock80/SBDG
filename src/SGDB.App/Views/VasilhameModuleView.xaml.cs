using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class VasilhameModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    private DispatcherTimer? _searchTimer;
    private static readonly Brush OverdueRowBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
    private static readonly Brush PartialRowBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB));

    public VasilhameModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            FromDateBox.SelectedDate = DateTime.Today.AddDays(-30);
            ToDateBox.SelectedDate = DateTime.Today;
            UpdateSearchPlaceholder();
            LoadData();
            Focus();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) LoadData();
    }

    private void Period_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) LoadData();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchPlaceholder();
        _searchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchTimer.Stop();
        _searchTimer.Tick -= SearchTick;
        _searchTimer.Tick += SearchTick;
        _searchTimer.Start();
    }

    private void UpdateSearchPlaceholder() =>
        SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void SearchTick(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        LoadData();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadData();

    private void LoadData()
    {
        try
        {
            var result = VasilhameService.List(
                somenteDevedor: SomenteDevedor.IsChecked == true,
                somenteVencido: SomenteVencido.IsChecked == true,
                search: SearchBox.Text,
                movimentosFrom: FromDateBox.SelectedDate,
                movimentosTo: ToDateBox.SelectedDate);

            SaldosGrid.ItemsSource = result.Saldos;
            MovGrid.ItemsSource = result.Movimentos;
            ResumoPanel.ItemsSource = result.ResumoPorTipo.Count > 0
                ? result.ResumoPorTipo
                : [new VasilhameTypeSummary { TypeName = "sem pendências", Quantity = 0 }];

            var meta = $"{result.Registros} pendência(s) · {result.TotalItens:0.###} un. em aberto";
            if (result.Vencidos > 0)
                meta += $" · {result.Vencidos} vencida(s)";
            if (result.TotalCaucao > 0.009)
                meta += $" · Caução total R$ {result.TotalCaucao:N2}";
            MetaText.Text = meta;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vasilhame", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaldosGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not VasilhameSaldoRow row)
            return;
        e.Row.Background = row.IsOverdue
            ? OverdueRowBrush
            : row.IsPartialReturn
                ? PartialRowBrush
                : Brushes.White;
    }

    private void Tipos_Click(object sender, RoutedEventArgs e)
    {
        var view = new ContainerTypesModuleView();
        var win = new Window
        {
            Title = "Tipos de vasilhame",
            Content = view,
            Owner = Window.GetWindow(this),
            Width = 720,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        view.CloseRequested += (_, _) => win.Close();
        win.ShowDialog();
        LoadData();
    }

    private void QuemPegou_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new VasilhameLancamentoWindow(isDevolucao: false) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            LoadData();
    }

    private void Devolver_Click(object sender, RoutedEventArgs e)
    {
        VasilhameSaldoRow? prefill = SaldosGrid.SelectedItem as VasilhameSaldoRow;
        var dlg = new VasilhameLancamentoWindow(isDevolucao: true, prefill) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            LoadData();
    }

    private void SaldosGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        Devolver_Click(sender, e);

    private void WhatsApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not VasilhameSaldoRow row)
            return;
        try
        {
            VasilhameService.OpenWhatsAppCobrança(row);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Comprovante_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SaldosGrid.SelectedItem is VasilhameSaldoRow saldo)
            {
                VasilhameService.PrintComprovanteSaldo(saldo);
                return;
            }

            if (MovGrid.SelectedItem is VasilhameMovementRow mov)
            {
                VasilhameService.PrintComprovanteMovimento(mov);
                return;
            }

            MessageBox.Show(
                "Selecione um saldo à esquerda ou um movimento à direita para imprimir o comprovante.",
                "Comprovante", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Comprovante", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExcluirMov_Click(object sender, RoutedEventArgs e)
    {
        if (MovGrid.SelectedItem is not VasilhameMovementRow row)
        {
            MessageBox.Show("Selecione um lançamento à direita.", "Vasilhame",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show("Excluir este lançamento?", "Vasilhame",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            VasilhameService.DeleteMovement(row.Id);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vasilhame", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            QuemPegou_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            LoadData();
            e.Handled = true;
        }
        else if (e.Key == Key.F7)
        {
            Devolver_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F8 || (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control))
        {
            Comprovante_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            ExcluirMov_Click(sender, e);
            e.Handled = true;
        }
    }
}
