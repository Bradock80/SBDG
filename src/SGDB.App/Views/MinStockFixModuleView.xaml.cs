using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class MinStockFixModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly ObservableCollection<MinStockFixRow> _rows = [];
    private List<MinStockFixRow> _all = [];

    public MinStockFixModuleView()
    {
        InitializeComponent();
        Grid.ItemsSource = _rows;
        Loaded += (_, _) =>
        {
            Focus();
            Reload();
        };
    }

    private void Reload()
    {
        try
        {
            var result = StockService.ListReport(StockReportKind.Minimo, limit: 500);
            _all = result.Rows.Select(r => new MinStockFixRow
            {
                ProductId = r.ProductId,
                Code = r.Code,
                Name = r.Name,
                GroupName = r.GroupName,
                StockEdit = r.Stock,
                MinEdit = (int)Math.Round(r.MinStock),
                OriginalStock = r.Stock,
                OriginalMin = (int)Math.Round(r.MinStock),
            }).ToList();

            ApplyFilter();
            MetaText.Text = _all.Count == 0
                ? "Nenhum produto no estoque mínimo. Tudo ok."
                : $"{_all.Count} produto(s) no mínimo ou abaixo — edite Estoque ou Mín. e clique em Salvar na linha (ou F8).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Estoque Mínimo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyFilter()
    {
        var term = (SearchBox?.Text ?? "").Trim();
        var onlyDrinks = OnlyDrinksBox?.IsChecked == true;

        IEnumerable<MinStockFixRow> q = _all;
        if (onlyDrinks)
            q = q.Where(r => StockAlertService.IsBeerOrSoda(r.GroupName, r.Name));
        if (!string.IsNullOrWhiteSpace(term))
        {
            q = q.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.GroupName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        _rows.Clear();
        foreach (var row in q)
            _rows.Add(row);

        CountText.Text = $"{_rows.Count} na lista";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnlyDrinksBox_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void SaveSelected_Click(object sender, RoutedEventArgs e) => SaveFocusedOrSelected();

    private void SaveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MinStockFixRow row })
            SaveRow(row);
    }

    private void SaveFocusedOrSelected()
    {
        Grid.CommitEdit(DataGridEditingUnit.Cell, true);
        Grid.CommitEdit(DataGridEditingUnit.Row, true);

        var row = Grid.SelectedItem as MinStockFixRow;
        if (row is null)
        {
            MessageBox.Show("Selecione uma linha para salvar.", "Estoque Mínimo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveRow(row);
    }

    private void SaveAllChanged_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Cell, true);
        Grid.CommitEdit(DataGridEditingUnit.Row, true);

        var changed = _rows.Where(r => r.IsDirty).ToList();
        if (changed.Count == 0)
        {
            MessageBox.Show("Nenhuma alteração pendente na lista.", "Estoque Mínimo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ok = 0;
        var errors = new List<string>();
        foreach (var row in changed)
        {
            try
            {
                ApplyRow(row);
                ok++;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.Name}: {ex.Message}");
            }
        }

        Reload();
        if (errors.Count == 0)
        {
            MessageBox.Show($"{ok} produto(s) atualizado(s).", "Estoque Mínimo",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                $"{ok} ok · {errors.Count} com erro:\n\n" + string.Join("\n", errors.Take(8)),
                "Estoque Mínimo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveRow(MinStockFixRow row)
    {
        try
        {
            ApplyRow(row);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Estoque Mínimo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void ApplyRow(MinStockFixRow row)
    {
        if (row.MinEdit < 0)
            throw new InvalidOperationException("Estoque mínimo inválido.");
        if (row.StockEdit < 0)
            throw new InvalidOperationException("Estoque inválido.");

        var stockChanged = Math.Abs(row.StockEdit - row.OriginalStock) > 0.0001;
        var minChanged = row.MinEdit != row.OriginalMin;

        if (!stockChanged && !minChanged)
            return;

        if (stockChanged)
            StockService.SetTotalStock(row.ProductId, row.StockEdit);

        if (minChanged)
            StockService.UpdateMinStock(row.ProductId, row.MinEdit);

        AuditService.Log("estoque_minimo_correcao", "product", row.ProductId.ToString(),
            $"Estoque {row.OriginalStock:G}→{row.StockEdit:G} · Mín {row.OriginalMin}→{row.MinEdit}");
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Reload();
            e.Handled = true;
        }
        else if (e.Key == Key.F8)
        {
            SaveFocusedOrSelected();
            e.Handled = true;
        }
    }
}

public sealed class MinStockFixRow : INotifyPropertyChanged
{
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string GroupName { get; init; } = "";
    public double OriginalStock { get; set; }
    public int OriginalMin { get; set; }

    private double _stockEdit;
    public double StockEdit
    {
        get => _stockEdit;
        set { if (Math.Abs(_stockEdit - value) > 1e-9) { _stockEdit = value; OnChanged(); OnChanged(nameof(IsDirty)); OnChanged(nameof(Sugestao)); } }
    }

    private int _minEdit;
    public int MinEdit
    {
        get => _minEdit;
        set { if (_minEdit != value) { _minEdit = value; OnChanged(); OnChanged(nameof(IsDirty)); OnChanged(nameof(Sugestao)); } }
    }

    public double Sugestao => Math.Max(0, Math.Ceiling(MinEdit - StockEdit));
    public string SugestaoDisplay => Sugestao.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"));
    public string StockDisplay => StockEdit.ToString("N3", CultureInfo.GetCultureInfo("pt-BR"));
    public bool IsDirty =>
        Math.Abs(StockEdit - OriginalStock) > 0.0001 || MinEdit != OriginalMin;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
