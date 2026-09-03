using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class InventoryPurchaseGuidanceModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    InventoryProjectionSnapshot _snapshot = new();
    InventoryProjectionPresentationSnapshot _presented = new();
    InventoryAttentionSnapshot _attention = new();
    InventoryAttentionPresentationSnapshot _attentionPresented = new();
    InventoryCommercialScenarioSnapshot _commercial = new();
    InventoryCommercialScenarioPresentationSnapshot _commercialPresented = new();
    InventoryPromotionSuggestionSnapshot _promotion = new();
    InventoryPromotionSuggestionPresentationSnapshot _promotionPresented = new();
    InventoryPurchaseGuidanceSnapshot _guidance = new();
    InventoryPurchaseGuidancePresentationSnapshot _guidancePresented = new();
    readonly InventoryPurchaseGuidanceUiFilter _filter = new();
    string? _loadError;
    bool _hasValidSnapshot;
    bool _ready;
    bool _clientBlocked;

    public InventoryPurchaseGuidanceModuleView()
    {
        InitializeComponent();
        foreach (var opt in InventoryPurchaseGuidanceUi.CoverageOptions)
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
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OpenProjectionDetail();
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
        _filter.Card = InventoryPurchaseGuidanceCardKind.All;
        _filter.CoverageBand = null;
        _filter.Search = "";
        SearchBox.Text = "";
        CoverageBox.SelectedIndex = 0;
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

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (_clientBlocked) return;
        if (sender is not Button btn || btn.Tag is not InventoryPurchaseGuidanceCardKind kind)
            return;
        _filter.Card = kind;
        ApplyView();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenProjectionDetail();

    private void OpenProduct_Click(object sender, RoutedEventArgs e) => OpenProduct();

    private void OpenProjectionDetail_Click(object sender, RoutedEventArgs e) => OpenProjectionDetail();

    private void OpenProjectionDetail()
    {
        if (_clientBlocked)
            return;
        if (Grid.SelectedItem is not InventoryPurchaseGuidanceGridRow row || row.ProductId <= 0)
            return;

        var detail = InventoryProjectionDetail.TryCreate(
            _snapshot, _presented, row.ProductId, _attentionPresented, _commercialPresented, _promotionPresented);
        if (detail is null)
        {
            MessageBox.Show(
                InventoryProjectionDetailUi.UnavailableDetailMessage,
                InventoryPurchaseGuidancePresentation.ModuleTitle,
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
        if (Grid.SelectedItem is not InventoryPurchaseGuidanceGridRow row || row.ProductId <= 0)
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
            var attention = InventoryAttentionComposer.Build(snapshot);
            var attentionPresented = InventoryAttentionPresentation.Apply(attention, presented);
            var eligibility = InventoryCommercialEligibilityComposer.Build(snapshot, attention);
            var facts = InventoryCommercialFactsService.Load(
                InventoryCommercialEligibilityComposer.ProductIds(snapshot));
            var setting = InventoryCommercialMarginSettingsService.Load();
            var policy = InventoryCommercialMarginPolicyResolver.Resolve(setting);
            var commercial = InventoryCommercialScenarioComposer.Compose(
                snapshot.Intelligence, snapshot, attention, eligibility, facts, policy);
            var commercialPresented = InventoryCommercialScenarioPresentation.Apply(commercial);
            var promotion = InventoryPromotionSuggestionComposer.Compose(snapshot.Intelligence, commercial);
            var promotionPresented = InventoryPromotionSuggestionPresentation.Apply(promotion);
            var guidance = InventoryPurchaseGuidanceComposer.Compose(snapshot);
            var guidancePresented = InventoryPurchaseGuidancePresentation.Apply(
                guidance, snapshot.Intelligence, snapshot);
            _snapshot = snapshot;
            _presented = presented;
            _attention = attention;
            _attentionPresented = attentionPresented;
            _commercial = commercial;
            _commercialPresented = commercialPresented;
            _promotion = promotion;
            _promotionPresented = promotionPresented;
            _guidance = guidance;
            _guidancePresented = guidancePresented;
            _hasValidSnapshot = true;
            _loadError = null;
            RebuildCards();
            ApplyView();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            failure = InventoryPurchaseGuidanceUi.ResolveLoadFailure(_hasValidSnapshot);
            if (failure.Value.KeepPreviousSnapshot)
            {
                MetaText.Text = failure.Value.OperatorMessage;
            }
            else
            {
                _snapshot = new InventoryProjectionSnapshot();
                _presented = new InventoryProjectionPresentationSnapshot();
                _attention = new InventoryAttentionSnapshot();
                _attentionPresented = new InventoryAttentionPresentationSnapshot();
                _commercial = new InventoryCommercialScenarioSnapshot();
                _commercialPresented = new InventoryCommercialScenarioPresentationSnapshot();
                _promotion = new InventoryPromotionSuggestionSnapshot();
                _promotionPresented = new InventoryPromotionSuggestionPresentationSnapshot();
                _guidance = new InventoryPurchaseGuidanceSnapshot();
                _guidancePresented = new InventoryPurchaseGuidancePresentationSnapshot();
                _loadError = failure.Value.OperatorMessage;
                RebuildCards();
                Grid.ItemsSource = null;
                Grid.Items.SortDescriptions.Clear();
                ShowEmpty(_loadError);
                DetailText.Text = InventoryPurchaseGuidanceUi.SelectRowHint;
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
                InventoryPurchaseGuidancePresentation.ModuleTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowClientBlocked()
    {
        _clientBlocked = true;
        _snapshot = new InventoryProjectionSnapshot();
        _presented = new InventoryProjectionPresentationSnapshot();
        _attention = new InventoryAttentionSnapshot();
        _attentionPresented = new InventoryAttentionPresentationSnapshot();
        _commercial = new InventoryCommercialScenarioSnapshot();
        _commercialPresented = new InventoryCommercialScenarioPresentationSnapshot();
        _promotion = new InventoryPromotionSuggestionSnapshot();
        _promotionPresented = new InventoryPromotionSuggestionPresentationSnapshot();
        _guidance = new InventoryPurchaseGuidanceSnapshot();
        _guidancePresented = new InventoryPurchaseGuidancePresentationSnapshot();
        _loadError = null;
        ContentRoot.Visibility = Visibility.Collapsed;
        ClientBlockOverlay.Visibility = Visibility.Visible;
        ClientBlockText.Text = StoreNetworkMode.ClientBlockedModuleMessage;
    }

    private void ApplyView()
    {
        var rows = InventoryPurchaseGuidanceUi.Apply(
            _guidancePresented, _snapshot.Intelligence.Rows, _filter);
        Grid.ItemsSource = null;
        Grid.Items.SortDescriptions.Clear();
        Grid.ItemsSource = rows;

        var empty = InventoryPurchaseGuidanceUi.EmptyStateMessage(
            _guidancePresented.Rows.Count, rows.Count, _loadError);
        ShowEmpty(empty);

        var cards = InventoryPurchaseGuidanceUi.CountCards(_guidancePresented.Rows);
        MetaText.Text = string.IsNullOrEmpty(empty)
            ? $"{rows.Count} produto(s) · {cards.ConsiderReplenishment} considerar · {cards.DoNotReplenishNow} não repor · {cards.ReviewData} revisar."
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
        if (Grid.SelectedItem is not InventoryPurchaseGuidanceGridRow row || row.ProductId <= 0)
        {
            DetailText.Text = InventoryPurchaseGuidanceUi.SelectRowHint;
            BtnDetailProjection.IsEnabled = false;
            return;
        }

        BtnDetailProjection.IsEnabled = !_clientBlocked;
        var g = row.Guidance;
        DetailText.Text =
            $"{g.ActionLabel} · {g.PrimaryReasonLabel} · {g.ConfidenceLabel}\n" +
            $"{row.Name} · {row.Code} · Estoque {g.TotalStockDisplay} · {g.Vmv30Text} · Cobertura {g.CoverageDisplay}\n" +
            g.DetailExplanation;
    }

    private void RebuildCards()
    {
        CardsPanel.Children.Clear();
        var counts = InventoryPurchaseGuidanceUi.CountCards(_guidancePresented.Rows);
        foreach (var card in InventoryPurchaseGuidanceUi.Cards)
            AddCard(card.Title, counts.Of(card.Kind), card.Kind, card.Bg, card.Fg);
    }

    private void AddCard(string title, int count, InventoryPurchaseGuidanceCardKind kind, string bg, string fg)
    {
        var btn = new Button
        {
            Tag = kind,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(12, 8, 12, 8),
            Background = (Brush)new BrushConverter().ConvertFromString(bg)!,
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            MinWidth = 112,
            ToolTip = $"{title}: {count} produto(s)",
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
            Text = $"{count} produtos",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString(fg)!,
        });
        btn.Content = stack;
        CardsPanel.Children.Add(btn);
    }
}
