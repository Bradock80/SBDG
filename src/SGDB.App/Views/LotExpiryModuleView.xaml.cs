using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class LotExpiryModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    static readonly (ValidityControlFilterKind Kind, string Title)[] FilterOptions =
    [
        (ValidityControlFilterKind.All, "Todos"),
        (ValidityControlFilterKind.Expired, "Vencidos"),
        (ValidityControlFilterKind.Today, "Hoje"),
        (ValidityControlFilterKind.Days7, "7 dias"),
        (ValidityControlFilterKind.Days15, "15 dias"),
        (ValidityControlFilterKind.Days30, "30 dias"),
        (ValidityControlFilterKind.Days60, "60 dias"),
        (ValidityControlFilterKind.Days90, "90 dias"),
        (ValidityControlFilterKind.Uninformed, "Sem validade"),
    ];

    ValidityControlSnapshot _snapshot = new();
    ValidityControlFilterKind _filter;
    bool _ready;

    public LotExpiryModuleView(int? initialDays = null)
        : this(ValidityControlService.FilterFromLegacyDays(initialDays))
    {
    }

    public LotExpiryModuleView(ValidityControlFilterKind initialFilter)
    {
        InitializeComponent();
        _filter = initialFilter;
        foreach (var opt in FilterOptions)
            FilterBox.Items.Add(new ComboBoxItem { Content = opt.Title, Tag = opt.Kind });
        FilterBox.SelectedIndex = Math.Max(0, Array.FindIndex(FilterOptions, o => o.Kind == _filter));
        Loaded += (_, _) =>
        {
            Focus();
            Load();
            _ready = true;
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

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (FilterBox.SelectedItem is ComboBoxItem item && item.Tag is ValidityControlFilterKind kind)
            _filter = kind;
        ApplyView();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) ApplyView();
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ValidityControlFilterKind kind)
            return;
        _filter = kind;
        for (var i = 0; i < FilterBox.Items.Count; i++)
        {
            if (FilterBox.Items[i] is ComboBoxItem item && item.Tag is ValidityControlFilterKind k && k == kind)
            {
                FilterBox.SelectedIndex = i;
                break;
            }
        }
        ApplyView();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProduct();

    private void OpenProduct_Click(object sender, RoutedEventArgs e) => OpenProduct();

    private void OpenLots_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not ValidityControlRow row || row.ProductId <= 0)
            return;
        var win = new ProductLotsWindow(row.ProductId, row.ProductName)
        {
            Owner = Window.GetWindow(this),
        };
        win.ShowDialog();
        Load();
    }

    private void OpenMaintain_Click(object sender, RoutedEventArgs e) => OpenMaintain();

    private void OpenMaintain()
    {
        if (Grid.SelectedItem is not ValidityControlRow row || row.ProductId <= 0)
        {
            MessageBox.Show(
                LotCoverageUi.SelectProductHint,
                "Controle de Validades",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var win = new LotCoverageMaintenanceWindow(row.ProductId)
        {
            Owner = Window.GetWindow(this),
        };
        win.ShowDialog();
        Load();
    }

    private void OpenProduct()
    {
        if (Grid.SelectedItem is not ValidityControlRow row || row.ProductId <= 0)
            return;
        var form = new ProductFormWindow(row.ProductId) { Owner = Window.GetWindow(this) };
        form.ShowDialog();
        Load();
    }

    private void Load()
    {
        try
        {
            _snapshot = ValidityControlService.GetSnapshot();
            RebuildCards();
            FillCombo(GroupBox, _snapshot.Rows.Select(r => r.GroupName));
            FillCombo(BrandBox, _snapshot.Rows.Select(r => r.BrandName));
            ApplyView();
        }
        catch (Exception ex)
        {
            Grid.ItemsSource = null;
            DetailText.Text = "Selecione uma linha para ver o detalhe.";
            MetaText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Controle de Validades", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyView()
    {
        var search = SearchBox.Text;
        var group = SelectedCombo(GroupBox);
        var brand = SelectedCombo(BrandBox);
        var rows = ValidityControlEngine.Apply(_snapshot.Rows, _filter, search, group, brand);
        Grid.ItemsSource = null;
        Grid.Items.SortDescriptions.Clear();
        Grid.ItemsSource = rows;
        var cards = _snapshot.Cards;
        MetaText.Text = rows.Count == 0
            ? "Nenhum lote nesta faixa."
            : $"{rows.Count} item(ns) · {cards.Expired} vencido(s) · {cards.Today} hoje · {cards.Days7} até 7 dias · {cards.Uninformed} sem validade.";
        UpdateDetail();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetail();

    private void UpdateDetail()
    {
        DetailText.Text = Grid.SelectedItem is ValidityControlRow row
            ? ValidityControlUi.FormatSelectionDetail(row)
            : "Selecione uma linha para ver o detalhe.";
    }

    private void RebuildCards()
    {
        CardsPanel.Children.Clear();
        var c = _snapshot.Cards;
        AddCard("Todos", c.Total, ValidityControlFilterKind.All, "#E2E8F0", "#334155");
        AddCard("Vencidos", c.Expired, ValidityControlFilterKind.Expired, "#7F1D1D", "White");
        AddCard("Vence hoje", c.Today, ValidityControlFilterKind.Today, "#FEE2E2", "#991B1B");
        AddCard("Até 7 dias", c.Days7, ValidityControlFilterKind.Days7, "#FECACA", "#991B1B");
        AddCard("Até 15 dias", c.Days15, ValidityControlFilterKind.Days15, "#FEF3C7", "#92400E");
        AddCard("Até 30 dias", c.Days30, ValidityControlFilterKind.Days30, "#FDE68A", "#92400E");
        AddCard("Até 60 dias", c.Days60, ValidityControlFilterKind.Days60, "#FEF9C3", "#854D0E");
        AddCard("Até 90 dias", c.Days90, ValidityControlFilterKind.Days90, "#FEF9C3", "#854D0E");
        AddCard("Sem validade", c.Uninformed, ValidityControlFilterKind.Uninformed, "#E0F2FE", "#075985");
    }

    private void AddCard(string title, int count, ValidityControlFilterKind kind, string bg, string fg)
    {
        var btn = new Button
        {
            Tag = kind,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 8, 12, 8),
            Background = (Brush)new BrushConverter().ConvertFromString(bg)!,
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            MinWidth = 96,
        };
        btn.Click += Card_Click;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!,
        });
        stack.Children.Add(new TextBlock
        {
            Text = count.ToString(),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!,
        });
        btn.Content = stack;
        CardsPanel.Children.Add(btn);
    }

    private static void FillCombo(ComboBox box, IEnumerable<string> values)
    {
        var selected = SelectedCombo(box);
        box.Items.Clear();
        box.Items.Add("Todos");
        foreach (var value in values
                     .Where(v => !string.IsNullOrWhiteSpace(v))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
            box.Items.Add(value);
        if (!string.IsNullOrWhiteSpace(selected)
            && box.Items.Cast<object>().Any(i => string.Equals(i.ToString(), selected, StringComparison.OrdinalIgnoreCase)))
            box.SelectedItem = selected;
        else
            box.SelectedIndex = 0;
    }

    private static string? SelectedCombo(ComboBox box)
    {
        var text = box.SelectedItem?.ToString();
        return string.IsNullOrWhiteSpace(text) || text == "Todos" ? null : text;
    }
}
