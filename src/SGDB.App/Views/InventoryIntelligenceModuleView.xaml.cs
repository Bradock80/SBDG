using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class InventoryIntelligenceModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    InventoryProjectionSnapshot _snapshot = new();
    InventoryProjectionPresentationSnapshot _presented = new();
    readonly InventoryIntelligenceUiFilter _filter = new();
    string? _loadError;
    bool _hasValidSnapshot;
    bool _ready;
    bool _clientBlocked;

    public InventoryIntelligenceModuleView()
    {
        InitializeComponent();
        foreach (var opt in InventoryIntelligencePresentation.CoverageOptions)
            CoverageBox.Items.Add(new ComboBoxItem { Content = opt.Title, Tag = opt.Band });
        CoverageBox.SelectedIndex = 0;
        Loaded += (_, _) =>
        {
            Focus();
            ApplyEditPermissionUi();
            Load();
            _ready = true;
        };
    }

    private void ApplyEditPermissionUi()
    {
        var canEdit = AccessControl.Can("ProdutosEditar");
        BtnOpenProduct.IsEnabled = canEdit;
        BtnOpenProduct.Opacity = canEdit ? 1 : 0.4;
        BtnOpenProduct.ToolTip = canEdit ? null : "Sem permissão para alterar produtos";
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            if (!_clientBlocked)
                Load();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_clientBlocked)
            Load();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        if (_clientBlocked) return;
        _ready = false;
        _filter.Card = InventoryIntelligenceCardKind.All;
        _filter.CoverageBand = null;
        _filter.Search = "";
        _filter.Silence30 = false;
        _filter.Silence60 = false;
        _filter.Silence90 = false;
        _filter.InsufficientHistory = false;
        SearchBox.Text = "";
        CoverageBox.SelectedIndex = 0;
        Silence30Box.IsChecked = false;
        Silence60Box.IsChecked = false;
        Silence90Box.IsChecked = false;
        HistoryBox.IsChecked = false;
        _ready = true;
        ApplyView();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _clientBlocked) return;
        _filter.Search = SearchBox.Text ?? "";
        ApplyView();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _clientBlocked) return;
        if (CoverageBox.SelectedItem is ComboBoxItem item)
            _filter.CoverageBand = item.Tag as InventoryCoverageBand?;
        ApplyView();
    }

    private void ExtraFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _clientBlocked) return;
        _filter.Silence30 = Silence30Box.IsChecked == true;
        _filter.Silence60 = Silence60Box.IsChecked == true;
        _filter.Silence90 = Silence90Box.IsChecked == true;
        _filter.InsufficientHistory = HistoryBox.IsChecked == true;
        ApplyView();
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (_clientBlocked) return;
        if (sender is not Button btn || btn.Tag is not InventoryIntelligenceCardKind kind)
            return;
        _filter.Card = kind;
        ApplyView();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProduct();

    private void OpenProduct_Click(object sender, RoutedEventArgs e) => OpenProduct();

    private void OpenLots_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not InventoryIntelligenceProjectionGridRow row || row.ProductId <= 0)
            return;
        var win = new ProductLotsWindow(row.ProductId, row.Name)
        {
            Owner = Window.GetWindow(this),
        };
        win.ShowDialog();
    }

    private void OpenProjectionDetail_Click(object sender, RoutedEventArgs e)
    {
        if (_clientBlocked)
            return;
        if (Grid.SelectedItem is not InventoryIntelligenceProjectionGridRow row || row.ProductId <= 0)
            return;

        var detail = InventoryProjectionDetail.TryCreate(_snapshot, _presented, row.ProductId);
        if (detail is null)
        {
            MessageBox.Show(
                InventoryProjectionDetailUi.UnavailableDetailMessage,
                "Estoque Inteligente",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var win = new InventoryProjectionDetailWindow(detail)
        {
            Owner = Window.GetWindow(this),
        };
        win.ShowDialog();
    }

    private void OpenProduct()
    {
        if (Grid.SelectedItem is not InventoryIntelligenceProjectionGridRow row || row.ProductId <= 0)
            return;
        if (!AccessControl.Ensure("ProdutosEditar", "cadastrar e alterar produtos", Window.GetWindow(this)))
            return;
        var form = new ProductFormWindow(row.ProductId) { Owner = Window.GetWindow(this) };
        form.ShowDialog();
    }

    private void Load()
    {
        if (StoreNetworkMode.IsClient)
        {
            ShowClientBlocked();
            return;
        }

        _clientBlocked = false;
        ClientBlockOverlay.Visibility = Visibility.Collapsed;
        ContentRoot.Visibility = Visibility.Visible;

        var previousCursor = Cursor;
        LoadFailureDecision? failure = null;
        try
        {
            Cursor = Cursors.Wait;
            var snapshot = InventoryProjectionService.Load();
            var presented = InventoryProjectionPresentation.Apply(snapshot);
            _snapshot = snapshot;
            _presented = presented;
            _hasValidSnapshot = true;
            _loadError = null;
            RebuildCards();
            ApplyView();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            failure = InventoryIntelligencePresentation.ResolveLoadFailure(_hasValidSnapshot);
            if (failure.Value.KeepPreviousSnapshot)
            {
                MetaText.Text = failure.Value.OperatorMessage;
            }
            else
            {
                _snapshot = new InventoryProjectionSnapshot();
                _presented = new InventoryProjectionPresentationSnapshot();
                _loadError = failure.Value.OperatorMessage;
                RebuildCards();
                Grid.ItemsSource = null;
                Grid.Items.SortDescriptions.Clear();
                ShowEmpty(_loadError);
                DetailText.Text = "Selecione uma linha para ver o detalhe.";
                BtnDetailProjection.IsEnabled = false;
                MetaText.Text = failure.Value.OperatorMessage;
            }
        }
        finally
        {
            Cursor = previousCursor;
        }

        if (failure is { } shown)
        {
            MessageBox.Show(
                shown.OperatorMessage,
                "Estoque Inteligente",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowClientBlocked()
    {
        _clientBlocked = true;
        _snapshot = new InventoryProjectionSnapshot();
        _presented = new InventoryProjectionPresentationSnapshot();
        _loadError = null;
        ContentRoot.Visibility = Visibility.Collapsed;
        ClientBlockOverlay.Visibility = Visibility.Visible;
        ClientBlockText.Text = StoreNetworkMode.ClientBlockedModuleMessage;
    }

    private void ApplyView()
    {
        var rows = InventoryIntelligenceProjectionPresentation.Apply(
            _snapshot.Intelligence.Rows, _filter, _presented);
        Grid.ItemsSource = null;
        Grid.Items.SortDescriptions.Clear();
        Grid.ItemsSource = rows;

        var empty = InventoryIntelligencePresentation.EmptyStateMessage(
            _snapshot.Intelligence.Rows.Count, rows.Count, _loadError);
        ShowEmpty(empty);

        var cards = InventoryIntelligencePresentation.CountCards(_snapshot.Intelligence.Rows);
        MetaText.Text = string.IsNullOrEmpty(empty)
            ? $"{rows.Count} produto(s) · {cards.Critical} crítica(s) · {cards.Low} baixa(s) · {cards.Idle} parado(s)."
            : empty;
        UpdateDetail();
    }

    private void ShowEmpty(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            EmptyOverlay.Visibility = Visibility.Collapsed;
            EmptyText.Text = "";
            return;
        }

        EmptyText.Text = message;
        EmptyOverlay.Visibility = Visibility.Visible;
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDetail();

    private void UpdateDetail()
    {
        if (Grid.SelectedItem is not InventoryIntelligenceProjectionGridRow row || row.ProductId <= 0)
        {
            DetailText.Text = "Selecione uma linha para ver o detalhe.";
            BtnDetailProjection.IsEnabled = false;
            return;
        }

        BtnDetailProjection.IsEnabled = !_clientBlocked;
        var giro = row.Intelligence;
        DetailText.Text =
            $"{giro.Name} · {giro.Code} · Depósito {giro.StockDisplay} · Geladeira {giro.StockFridgeDisplay} · " +
            $"Total {giro.TotalStockDisplay} · VMV 30 {giro.Vmv30Display} · Cobertura {giro.CoverageDisplay} · " +
            $"{giro.SituationDisplay} · Alerta {giro.AlertDisplay}.";
    }

    private void RebuildCards()
    {
        CardsPanel.Children.Clear();
        var counts = InventoryIntelligencePresentation.CountCards(_snapshot.Intelligence.Rows);
        foreach (var card in InventoryIntelligencePresentation.Cards)
            AddCard(card.Title, counts.Of(card.Kind), card.Kind, card.Bg, card.Fg);
    }

    private void AddCard(string title, int count, InventoryIntelligenceCardKind kind, string bg, string fg)
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
}
