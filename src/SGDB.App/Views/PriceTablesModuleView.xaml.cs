using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PriceTablesModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int? _editingId;
    private bool _suppress;
    private List<PriceTable> _all = [];
    private readonly ObservableCollection<MethodCheckRow> _methodChecks = new();

    private sealed class ProductLinkRow
    {
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public string Status { get; init; } = "";
    }

    private sealed class MethodCheckRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public required string Id { get; init; }
        public required string Label { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public PriceTablesModuleView()
    {
        InitializeComponent();
        MethodsPanel.ItemsSource = _methodChecks;
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RenderList();
        };
        Loaded += (_, _) =>
        {
            Focus();
            RebuildMethodChecks();
            ClearForm();
            Reload();
        };
    }

    private PriceTable? Selected => TablesGrid.SelectedItem as PriceTable;

    private void RebuildMethodChecks(IReadOnlyCollection<string>? selected = null)
    {
        var selectedSet = new HashSet<string>(
            selected ?? DefaultSelectedIds(),
            StringComparer.OrdinalIgnoreCase);

        var methods = PaymentMethodsService.List()
            .Where(m => m.Active || selectedSet.Contains(m.Id))
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Name)
            .ToList();

        _methodChecks.Clear();
        foreach (var m in methods)
        {
            _methodChecks.Add(new MethodCheckRow
            {
                Id = m.Id,
                Label = m.Active ? m.ApiLabel : $"{m.ApiLabel} (inativo)",
                IsSelected = selectedSet.Contains(m.Id),
            });
        }
    }

    private static IReadOnlyList<string> DefaultSelectedIds() => PriceTablesService.DefaultMethods;

    private void Reload()
    {
        _all = PriceTablesService.List().ToList();
        RenderList();
        if (_all.Count > 0 && TablesGrid.SelectedItem is null)
        {
            _suppress = true;
            TablesGrid.SelectedIndex = 0;
            _suppress = false;
            if (Selected is not null) BindForm(Selected);
        }
    }

    private void RenderList()
    {
        var termo = (SearchBox.Text ?? "").Trim().ToUpperInvariant();
        var rows = string.IsNullOrEmpty(termo)
            ? _all
            : _all.Where(t => t.Description.Contains(termo, StringComparison.OrdinalIgnoreCase)).ToList();
        _suppress = true;
        var keepId = Selected?.Id;
        TablesGrid.ItemsSource = rows;
        if (keepId is not null)
            TablesGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == keepId);
        _suppress = false;
    }

    private void ClearForm()
    {
        _editingId = null;
        FormTitle.Text = "Nova tabela";
        DescBox.Text = "";
        PctBox.Text = "0,00";
        FixedBox.Text = "0,00";
        ActiveBox.IsChecked = true;
        RebuildMethodChecks(DefaultSelectedIds());
        ProductsEmpty.Text = "Clique em uma tabela acima para ver os produtos.";
        ProductsEmpty.Visibility = Visibility.Visible;
        ProductsGrid.Visibility = Visibility.Collapsed;
        ProductsTitle.Text = "Produtos vinculados";
        UpdatePreview();
        _suppress = true;
        TablesGrid.SelectedItem = null;
        _suppress = false;
        DescBox.Focus();
    }

    private void BindForm(PriceTable t)
    {
        _editingId = t.Id;
        FormTitle.Text = "Alterar tabela";
        DescBox.Text = t.Description;
        PctBox.Text = t.SurchargePercent.ToString("N2");
        FixedBox.Text = t.SurchargeFixed.ToString("N2");
        ActiveBox.IsChecked = t.Active;
        RebuildMethodChecks(t.ApplyPaymentMethods);
        UpdatePreview();
        LoadProducts(t);
    }

    private void LoadProducts(PriceTable t)
    {
        ProductsTitle.Text = $"Produtos vinculados — {t.Description}";
        var products = PriceTablesService.ListProductsForTable(t.Id)
            .Select(p => new ProductLinkRow
            {
                Code = string.IsNullOrWhiteSpace(p.Code) ? "—" : p.Code,
                Name = p.Name,
                Status = p.Active ? "" : "inativo",
            })
            .ToList();

        if (products.Count == 0)
        {
            ProductsEmpty.Text = "Nenhum produto vinculado a esta tabela. Vincule em Cadastro de Produtos → Tabela de preço.";
            ProductsEmpty.Visibility = Visibility.Visible;
            ProductsGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            ProductsEmpty.Visibility = Visibility.Collapsed;
            ProductsGrid.Visibility = Visibility.Visible;
            ProductsGrid.ItemsSource = products;
        }
    }

    private List<string> SelectedMethods() =>
        _methodChecks.Where(m => m.IsSelected).Select(m => m.Id).ToList();

    private void UpdatePreview()
    {
        var pct = ProductPriceHelper.ParseBr(PctBox.Text);
        var fix = ProductPriceHelper.ParseBr(FixedBox.Text);
        if (pct <= 0 && fix <= 0)
            PreviewText.Text = "Sem acréscimo configurado.";
        else if (pct > 0 && fix > 0)
            PreviewText.Text = $"Ex.: produto R$ 10,00 → +{pct:N2}% + R$ {fix:N2} = R$ {10 * pct / 100 + fix:N2} de acréscimo.";
        else if (pct > 0)
            PreviewText.Text = $"Ex.: produto R$ 10,00 → +{pct:N2}% = R$ {10 * pct / 100:N2} de acréscimo.";
        else
            PreviewText.Text = $"Acréscimo fixo de R$ {fix:N2} por unidade.";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void Preview_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) UpdatePreview();
    }

    private void ZeroPct_Click(object sender, RoutedEventArgs e)
    {
        PctBox.Text = "0,00";
        UpdatePreview();
    }

    private void TablesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || Selected is null) return;
        BindForm(Selected);
    }

    private void New_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = new PriceTableInput
            {
                Description = DescBox.Text,
                SurchargePercent = ProductPriceHelper.ParseBr(PctBox.Text),
                SurchargeFixed = ProductPriceHelper.ParseBr(FixedBox.Text),
                ApplyPaymentMethods = SelectedMethods(),
                Active = ActiveBox.IsChecked == true,
            };
            var saved = _editingId is null
                ? PriceTablesService.Create(input)
                : PriceTablesService.Update(_editingId.Value, input);
            Reload();
            _suppress = true;
            TablesGrid.SelectedItem = _all.FirstOrDefault(x => x.Id == saved.Id);
            _suppress = false;
            BindForm(saved);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Tabela de Preço", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId is null)
        {
            MessageBox.Show("Selecione uma tabela.", "Tabela de Preço", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Excluir a tabela \"{DescBox.Text}\"?", "Tabela de Preço",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            PriceTablesService.Delete(_editingId.Value);
            ClearForm();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Tabela de Preço", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { New_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F5) { Save_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F3) { SearchBox.Focus(); e.Handled = true; }
        else if (e.Key == Key.Delete && !(Keyboard.FocusedElement is TextBox)) { Delete_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
