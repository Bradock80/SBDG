using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class InventoryComboIntelligenceModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    InventoryComboPresentationSnapshot _presented = new();
    readonly InventoryComboUiFilter _filter = new();
    string? _loadError;
    bool _hasValidSnapshot;
    bool _ready;
    bool _clientBlocked;
    bool _loading;
    int? _selectedProductId;

    public InventoryComboIntelligenceModuleView()
    {
        InitializeComponent();
        foreach (var opt in InventoryComboIntelligenceUi.StatusOptions)
            StatusBox.Items.Add(new ComboBoxItem { Content = opt.Title, Tag = opt.Status });
        foreach (var opt in InventoryComboIntelligenceUi.ReasonOptions)
            ReasonBox.Items.Add(new ComboBoxItem { Content = opt.Title, Tag = opt.Reason });
        StatusBox.SelectedIndex = 0;
        ReasonBox.SelectedIndex = 0;
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
        if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
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
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            UpdateDetail();
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
        _filter.Status = InventoryComboUiStatusFilter.All;
        _filter.Reason = InventoryComboUiReasonFilter.All;
        _filter.Search = "";
        SearchBox.Text = "";
        StatusBox.SelectedIndex = 0;
        ReasonBox.SelectedIndex = 0;
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
        if (StatusBox.SelectedItem is ComboBoxItem statusItem
            && statusItem.Tag is InventoryComboUiStatusFilter status)
            _filter.Status = status;
        if (ReasonBox.SelectedItem is ComboBoxItem reasonItem
            && reasonItem.Tag is InventoryComboUiReasonFilter reason)
            _filter.Reason = reason;
        ApplyView();
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (_clientBlocked) return;
        if (sender is not Button btn || btn.Tag is not InventoryComboUiCardKind kind)
            return;
        _ready = false;
        _filter.Status = InventoryComboIntelligenceUi.StatusOf(kind);
        SelectStatusBox(_filter.Status);
        _ready = true;
        ApplyView();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => UpdateDetail();

    /// <summary>
    /// Roda sobre o painel direito: o ScrollViewer interno costuma marcar Handled
    /// sem mover (aninhado em ModuleScroll). Encaminha o delta ao viewer com espaço.
    /// O DataGrid esquerdo não entra nesta rota — o Preview é só do DetailScroll.
    /// </summary>
    private void DetailScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers == ModifierKeys.Shift)
            return;

        var route = InventoryComboWheelScroll.Route(
            DetailScroll.VerticalOffset,
            DetailScroll.ScrollableHeight,
            ModuleScroll.VerticalOffset,
            ModuleScroll.ScrollableHeight,
            e.Delta);
        if (!route.Handled)
            return;

        if (route.MoveInner)
            DetailScroll.ScrollToVerticalOffset(route.InnerOffset);
        else if (route.MoveOuter)
            ModuleScroll.ScrollToVerticalOffset(route.OuterOffset);

        e.Handled = true;
    }

    private void Load()
    {
        if (_loading)
            return;

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
        _loading = true;
        BtnRefresh.IsEnabled = false;
        try
        {
            Cursor = Cursors.Wait;
            var presented = InventoryComboIntelligenceLoader.Load();
            _presented = presented;
            _hasValidSnapshot = true;
            _loadError = null;
            RebuildCards();
            ApplyView();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            failure = InventoryComboIntelligenceUi.ResolveLoadFailure(_hasValidSnapshot);
            if (failure.Value.KeepPreviousSnapshot)
            {
                MetaText.Text = failure.Value.OperatorMessage;
            }
            else
            {
                _presented = new InventoryComboPresentationSnapshot();
                _loadError = failure.Value.OperatorMessage;
                RebuildCards();
                Grid.ItemsSource = null;
                ShowEmpty(_loadError);
                DetailTitle.Text = InventoryComboIntelligenceUi.SelectRowHint;
                DetailContext.Text = "";
                TargetEmptyText.Visibility = Visibility.Collapsed;
                SuggestionList.ItemsSource = null;
                MetaText.Text = failure.Value.OperatorMessage;
            }
        }
        finally
        {
            Cursor = previousCursor;
            BtnRefresh.IsEnabled = !_clientBlocked;
            _loading = false;
        }

        if (failure is { } shown)
        {
            MessageBox.Show(
                shown.OperatorMessage,
                InventoryComboIntelligenceUi.ModuleTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowClientBlocked()
    {
        _clientBlocked = true;
        _presented = new InventoryComboPresentationSnapshot();
        _loadError = null;
        ContentRoot.Visibility = Visibility.Collapsed;
        ClientBlockOverlay.Visibility = Visibility.Visible;
        ClientBlockText.Text = StoreNetworkMode.ClientBlockedModuleMessage;
    }

    private void ApplyView()
    {
        var rows = InventoryComboIntelligenceUi.Apply(_presented, _filter);
        Grid.ItemsSource = null;
        Grid.Items.SortDescriptions.Clear();
        Grid.ItemsSource = rows;
        RestoreSelection(rows);

        var empty = InventoryComboIntelligenceUi.EmptyStateMessage(
            _presented.Targets.Count, rows.Count, _loadError);
        ShowEmpty(empty);

        var cards = InventoryComboIntelligenceUi.CountCards(_presented.Targets);
        MetaText.Text = string.IsNullOrEmpty(empty)
            ? $"{cards.NeedTurnover} produto(s) com necessidade de giro · {cards.WithSuggestions} com combinações · {cards.WithoutSafeCombination} sem combinação segura · {cards.Combinations} combinação(ões)."
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
        if (Grid.SelectedItem is not InventoryComboTargetGridRow row || row.ProductId <= 0)
        {
            _selectedProductId = null;
            DetailTitle.Text = InventoryComboIntelligenceUi.SelectRowHint;
            DetailContext.Text = "";
            TargetEmptyText.Text = "";
            TargetEmptyText.Visibility = Visibility.Collapsed;
            SuggestionList.ItemsSource = null;
            return;
        }

        _selectedProductId = row.ProductId;
        DetailTitle.Text = row.ProductTitle;
        DetailContext.Text =
            $"{row.ReasonText} · {row.ConfidenceText}\n" +
            $"{InventoryComboPresentation.TargetStockLabel} {row.StockText} · {row.CombinationsStatusText}";
        if (row.SuggestionCount == 0)
        {
            TargetEmptyText.Text = row.EmptyMessage;
            TargetEmptyText.Visibility = Visibility.Visible;
            SuggestionList.ItemsSource = null;
            return;
        }

        TargetEmptyText.Visibility = Visibility.Collapsed;
        SuggestionList.ItemsSource = row.Suggestions;
    }

    private void RestoreSelection(IReadOnlyList<InventoryComboTargetGridRow> rows)
    {
        if (_selectedProductId is int id)
        {
            foreach (var row in rows)
            {
                if (row.ProductId == id)
                {
                    Grid.SelectedItem = row;
                    return;
                }
            }
        }

        if (rows.Count > 0)
            Grid.SelectedIndex = 0;
    }

    private void RebuildCards()
    {
        CardsPanel.Children.Clear();
        var counts = InventoryComboIntelligenceUi.CountCards(_presented.Targets);
        foreach (var card in InventoryComboIntelligenceUi.Cards)
            AddCard(card.Title, counts.Of(card.Kind), card.Kind, card.Bg, card.Fg);
    }

    private void AddCard(string title, int count, InventoryComboUiCardKind kind, string bg, string fg)
    {
        var btn = new Button
        {
            Tag = kind,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 8, 12, 8),
            Background = (Brush)new BrushConverter().ConvertFromString(bg)!,
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            MinWidth = 148,
            ToolTip = $"{title}: {count}",
        };
        btn.Click += Card_Click;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 180,
            Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!,
        });
        stack.Children.Add(new TextBlock
        {
            Text = count.ToString("0"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!,
        });
        btn.Content = stack;
        CardsPanel.Children.Add(btn);
    }

    private void SelectStatusBox(InventoryComboUiStatusFilter status)
    {
        for (var i = 0; i < StatusBox.Items.Count; i++)
        {
            if (StatusBox.Items[i] is ComboBoxItem item
                && item.Tag is InventoryComboUiStatusFilter value
                && value == status)
            {
                StatusBox.SelectedIndex = i;
                return;
            }
        }
    }
}

/// <summary>
/// Roteamento puro da roda no painel de sugestões. Delta WPF: positivo = para cima.
/// Sem query, ranking ou regra B1–B6.
/// </summary>
public static class InventoryComboWheelScroll
{
    public static InventoryComboWheelRoute Route(
        double innerOffset,
        double innerScrollable,
        double outerOffset,
        double outerScrollable,
        int delta)
    {
        if (TryApplyVertical(innerOffset, innerScrollable, delta, out var innerNext))
            return new InventoryComboWheelRoute(true, true, false, innerNext, outerOffset);
        if (TryApplyVertical(outerOffset, outerScrollable, delta, out var outerNext))
            return new InventoryComboWheelRoute(true, false, true, innerOffset, outerNext);
        return new InventoryComboWheelRoute(false, false, false, innerOffset, outerOffset);
    }

    public static bool TryApplyVertical(
        double verticalOffset,
        double scrollableHeight,
        int delta,
        out double nextOffset)
    {
        nextOffset = verticalOffset;
        if (delta == 0 || scrollableHeight <= 0 || !IsFinite(verticalOffset) || !IsFinite(scrollableHeight))
            return false;

        var candidate = verticalOffset - delta;
        if (candidate < 0)
            candidate = 0;
        else if (candidate > scrollableHeight)
            candidate = scrollableHeight;

        if (candidate == verticalOffset)
            return false;

        nextOffset = candidate;
        return true;
    }

    static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

public readonly struct InventoryComboWheelRoute
{
    public InventoryComboWheelRoute(
        bool handled,
        bool moveInner,
        bool moveOuter,
        double innerOffset,
        double outerOffset)
    {
        Handled = handled;
        MoveInner = moveInner;
        MoveOuter = moveOuter;
        InnerOffset = innerOffset;
        OuterOffset = outerOffset;
    }

    public bool Handled { get; }
    public bool MoveInner { get; }
    public bool MoveOuter { get; }
    public double InnerOffset { get; }
    public double OuterOffset { get; }
}
