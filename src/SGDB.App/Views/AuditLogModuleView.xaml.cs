using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class AuditLogModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private const int PageSize = 25;
    private int _currentPage;
    private int _totalCount;

    public AuditLogModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DateFromBox.SelectedDate = DateTime.Today.AddDays(-7);
            DateToBox.SelectedDate = DateTime.Today;
            ActionFilterBox.ItemsSource = AuditActionFilterOption.All;
            ActionFilterBox.SelectedValue = "";
            UserFilterBox.ItemsSource = AuditService.ListUserFilters();
            UserFilterBox.SelectedValue = "";
            Reload(resetPage: true);
            Focus();
        };
    }

    private AuditQuery BuildQuery(int offset) => new()
    {
        From = DateFromBox.SelectedDate,
        To = DateToBox.SelectedDate,
        Search = SearchBox.Text,
        UserLogin = UserFilterBox.SelectedValue as string,
        ActionFilter = ActionFilterBox.SelectedValue as string,
        Offset = offset,
        Limit = PageSize,
    };

    private void Reload(bool resetPage = false)
    {
        try
        {
            if (resetPage)
                _currentPage = 0;

            var query = BuildQuery(_currentPage * PageSize);
            _totalCount = AuditService.Count(query);
            LogGrid.ItemsSource = AuditService.List(query);
            UpdatePaginationUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Auditoria", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePaginationUi()
    {
        if (_totalCount == 0)
        {
            PageInfoText.Text = "Nenhum registro encontrado";
            PrevPageBtn.IsEnabled = false;
            NextPageBtn.IsEnabled = false;
            return;
        }

        var from = _currentPage * PageSize + 1;
        var to = Math.Min(_totalCount, (_currentPage + 1) * PageSize);
        PageInfoText.Text = $"Mostrando {from}-{to} de {_totalCount} registros";
        PrevPageBtn.IsEnabled = _currentPage > 0;
        NextPageBtn.IsEnabled = (_currentPage + 1) * PageSize < _totalCount;
    }

    private void UpdateSearchPlaceholder() =>
        SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void Buscar_Click(object sender, RoutedEventArgs e) => Reload(resetPage: true);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateSearchPlaceholder();

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Reload(resetPage: true);
            e.Handled = true;
        }
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 0) return;
        _currentPage--;
        Reload();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if ((_currentPage + 1) * PageSize >= _totalCount) return;
        _currentPage++;
        Reload();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rows = AuditService.ListForExport(BuildQuery(0));
            if (rows.Count == 0)
            {
                MessageBox.Show("Nenhum registro para exportar com os filtros atuais.",
                    "Auditoria", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"auditoria_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            };
            if (dialog.ShowDialog() != true)
                return;

            using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
            writer.WriteLine("Data/Hora;Usuário;Login;Ação;Entidade;ID;Detalhes;Gravidade");
            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(';',
                    Csv(row.DateDisplay),
                    Csv(row.UserName),
                    Csv(row.UserLogin),
                    Csv(row.ActionBadgeDisplay),
                    Csv(row.EntityDisplay),
                    Csv(row.EntityId ?? ""),
                    Csv(row.DetailsDisplay),
                    Csv(row.BadgeKind)));
            }

            MessageBox.Show($"{rows.Count} registro(s) exportado(s).", "Auditoria",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Exportar CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string Csv(string value)
    {
        value = (value ?? "").Replace("\r", " ").Replace("\n", " ");
        if (value.Contains(';') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private void LogGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LogGrid.SelectedItem is not AuditLogRow row)
            return;

        var win = new AuditLogDetailWindow(row, Window.GetWindow(this));
        win.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7) { Reload(resetPage: true); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
