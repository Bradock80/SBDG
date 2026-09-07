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
    InventoryComboLifecycleStatus? _lifecycleTab;
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
            RefreshViewToggle();
            Load();
            _ready = true;
        };
    }

    void RefreshViewToggle()
    {
        for (var i = FilterPanel.Children.Count - 1; i >= 0; i--)
        {
            if (FilterPanel.Children[i] is FrameworkElement { Tag: "view-mode" })
                FilterPanel.Children.RemoveAt(i);
        }

        var mode = InventorySmartViewPreference.LoadIfNeeded();
        var toggle = InventorySmartCardUi.CreateViewToggle(
            mode,
            (_, _) => SetView(InventorySmartViewMode.Simple),
            (_, _) => SetView(InventorySmartViewMode.Detailed));
        toggle.Tag = "view-mode";
        FilterPanel.Children.Insert(0, toggle);
    }

    void SetView(InventorySmartViewMode mode)
    {
        InventorySmartViewPreference.Set(mode);
        RefreshViewToggle();
        ApplyView();
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

    void ContentRoot_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        InventoryNestedWheelScroll.TryHandleOutside(
            e, ModuleScroll, SimpleCardsScroll, Grid, DetailScroll, CampaignScroll);

    /// <summary>
    /// Roda sobre painéis aninhados: o viewer interno costuma marcar Handled sem mover.
    /// Encaminha o delta ao viewer com espaço. ComboBox aberto não entra nesta rota.
    /// </summary>
    private void DetailScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        InventoryNestedWheelScroll.TryHandle(e, DetailScroll, ModuleScroll);
    }

    private void SimpleCardsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        InventoryNestedWheelScroll.TryHandle(e, SimpleCardsScroll, ModuleScroll);

    private void Grid_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        InventoryNestedWheelScroll.TryHandleDataGrid(e, Grid, ModuleScroll);

    void CampaignScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        InventoryNestedWheelScroll.TryHandle(e, CampaignScroll, ModuleScroll);

    void ApproveCombo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InventoryComboSuggestionPresentationRow row })
            return;
        try
        {
            var draft = InventoryComboApprovalService.ReloadDraft(row.TargetProductId, row.AnchorProductId);
            var win = new InventoryComboApprovalWindow(draft) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
                Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, InventoryComboIntelligenceUi.ModuleTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void SuggestionDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InventoryComboSuggestionPresentationRow row })
            DetailContext.Text = $"{row.AnchorTitle}\n{row.EvidenceDetailText}\n{row.FloorExplanation}";
    }

    void RejectSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InventoryComboSuggestionPresentationRow row })
            return;
        var key = InventoryComboLifecycleRules.SuggestionKey(row.TargetProductId, row.AnchorProductId);
        var result = InventoryComboLifecycleService.Reject(key, row.Signature, "Descartado na tela de Combos");
        if (!result.Ok)
        {
            MessageBox.Show(result.Error, InventoryComboIntelligenceUi.ModuleTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Load();
    }

    void RebuildLifecycleTabs()
    {
        LifecycleTabs.Children.Clear();
        AddLifecycleTab(InventoryComboLifecycleUi.SuggestionsTab, null);
        AddLifecycleTab(InventoryComboLifecycleUi.ActiveTab, InventoryComboLifecycleStatus.Active);
        AddLifecycleTab(InventoryComboLifecycleUi.PausedTab, InventoryComboLifecycleStatus.Paused);
        AddLifecycleTab(InventoryComboLifecycleUi.FinishedTab, InventoryComboLifecycleStatus.Finished);
        AddLifecycleTab(InventoryComboLifecycleUi.ExpiredTab, InventoryComboLifecycleStatus.Expired);
        AddLifecycleTab(InventoryComboLifecycleUi.RejectedTab, InventoryComboLifecycleStatus.Rejected);
    }

    void AddLifecycleTab(string title, InventoryComboLifecycleStatus? status)
    {
        var selected = _lifecycleTab == status;
        var btn = new Button
        {
            Content = title,
            Tag = status as object ?? "",
            Margin = new Thickness(0, 0, 8, 4),
            Padding = new Thickness(12, 6, 12, 6),
            BorderBrush = selected ? Brushes.Black : Brushes.Transparent,
            BorderThickness = new Thickness(selected ? 2 : 1),
            Cursor = Cursors.Hand,
        };
        btn.Click += (_, _) =>
        {
            _lifecycleTab = status;
            ApplyView();
        };
        LifecycleTabs.Children.Add(btn);
    }

    void RebuildCampaignCards()
    {
        CampaignHost.Children.Clear();
        if (_lifecycleTab is not InventoryComboLifecycleStatus status)
            return;
        foreach (var campaign in InventoryComboLifecycleService.ListByStatus(status))
        {
            var possible = InventoryComboLifecycleService.PossibleStock(campaign);
            var remaining = campaign.MaxQty > 0
                ? Math.Min(campaign.RemainingQty, possible)
                : possible;
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            box.Children.Add(new TextBlock
            {
                Text = campaign.CommercialName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            });
            box.Children.Add(new TextBlock
            {
                Text =
                    $"{campaign.TargetName} ({campaign.TargetQty:0.##}) + {campaign.AnchorName} ({campaign.AnchorQty:0.##})\n" +
                    $"Preço {campaign.Price:N2} · margem aprovada {campaign.MarginPercent:0.#}% · " +
                    $"estoque possível {possible:0.##} · máximo {campaign.MaxQty:0.##} · " +
                    $"vendidos {campaign.SoldQty:0.##} · saldo {remaining:0.##}\n" +
                    $"{campaign.StartDate:dd/MM/yyyy}–{campaign.EndDate:dd/MM/yyyy} · " +
                    InventoryComboLifecycleRules.StatusLabel(campaign.EffectiveStatus),
                FontSize = 12,
                Foreground = (Brush)new BrushConverter().ConvertFromString("#475569")!,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 6),
            });
            var actions = new WrapPanel();
            if (status == InventoryComboLifecycleStatus.Active)
            {
                actions.Children.Add(CampaignButton("Pausar", () => ApplyResult(InventoryComboLifecycleService.Pause(campaign.Id))));
                actions.Children.Add(CampaignButton("Encerrar", () => ApplyResult(InventoryComboLifecycleService.Finish(campaign.Id))));
            }
            if (status == InventoryComboLifecycleStatus.Paused)
            {
                actions.Children.Add(CampaignButton("Reativar", () => ApplyResult(InventoryComboLifecycleService.Reactivate(campaign.Id))));
                actions.Children.Add(CampaignButton("Encerrar", () => ApplyResult(InventoryComboLifecycleService.Finish(campaign.Id))));
            }
            actions.Children.Add(CampaignButton("Alterar data final", () => ChangeEndDate(campaign)));
            actions.Children.Add(CampaignButton("Consultar vendas", () => ShowSales(campaign)));
            actions.Children.Add(CampaignButton("Histórico", () => ShowHistory(campaign)));
            box.Children.Add(actions);
            CampaignHost.Children.Add(new Border
            {
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E2E8F0")!,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = box,
            });
        }
    }

    void ChangeEndDate(InventoryComboCampaign campaign)
    {
        var win = new Window
        {
            Title = "Alterar data final",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
        };
        var box = new TextBox
        {
            Margin = new Thickness(16),
            Text = campaign.EndDate.ToString("dd/MM/yyyy"),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var ok = new Button { Content = "Confirmar", Width = 100, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) =>
        {
            if (!DateOnly.TryParse(box.Text, out var end)
                && !DateOnly.TryParseExact(box.Text, "dd/MM/yyyy", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
                    System.Globalization.DateTimeStyles.None, out end))
            {
                MessageBox.Show("Informe a data no formato dd/MM/aaaa.", win.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            win.Tag = end;
            win.DialogResult = true;
        };
        var cancel = new Button { Content = "Cancelar", Width = 100, Height = 32 };
        cancel.Click += (_, _) => win.DialogResult = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 16) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new StackPanel();
        root.Children.Add(box);
        root.Children.Add(buttons);
        win.Content = root;
        if (win.ShowDialog() == true && win.Tag is DateOnly endDate)
            ApplyResult(InventoryComboLifecycleService.ChangeEndDate(campaign.Id, endDate));
    }

    void ShowSales(InventoryComboCampaign campaign)
    {
        var summary = InventoryComboLifecycleService.SalesSummary(campaign.ProductId ?? 0);
        MessageBox.Show(summary, campaign.CommercialName, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void ShowHistory(InventoryComboCampaign campaign)
    {
        MessageBox.Show(
            $"Origem: {campaign.Origin}\nAprovador: {campaign.ApprovedBy}\nAprovado em: {campaign.ApprovedAt}\n" +
            $"Motivo: {campaign.Reason}\nConfiança: {campaign.Confidence}\n{campaign.Limitations}",
            "Histórico do combo",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    Button CampaignButton(string title, Action action)
    {
        var btn = new Button { Content = title, Margin = new Thickness(0, 0, 8, 4), Padding = new Thickness(10, 4, 10, 4) };
        btn.Click += (_, _) => action();
        return btn;
    }

    void ApplyResult(InventoryComboApprovalResult result)
    {
        if (!result.Ok)
        {
            MessageBox.Show(result.Error, InventoryComboIntelligenceUi.ModuleTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Load();
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
            _presented = InventoryComboLifecycleService.OverlaySuggestions(presented);
            InventoryComboLifecycleService.ReviewUnsafeMargins();
            _hasValidSnapshot = true;
            _loadError = null;
            RebuildLifecycleTabs();
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
        RebuildLifecycleTabs();
        var campaignMode = _lifecycleTab is not null;
        CampaignScroll.Visibility = campaignMode ? Visibility.Visible : Visibility.Collapsed;
        if (campaignMode)
        {
            Grid.Visibility = Visibility.Collapsed;
            SimpleCardsScroll.Visibility = Visibility.Collapsed;
            RebuildCampaignCards();
            ShowEmpty(CampaignHost.Children.Count == 0 ? "Nenhum combo neste estado." : "");
            MetaText.Text = $"{CampaignHost.Children.Count} combo(s).";
            return;
        }

        var rows = InventoryComboIntelligenceUi.Apply(_presented, _filter);
        var simple = InventorySmartViewPreference.Current == InventorySmartViewMode.Simple;
        Grid.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        SimpleCardsScroll.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        Grid.ItemsSource = null;
        Grid.Items.SortDescriptions.Clear();
        Grid.ItemsSource = rows;
        RestoreSelection(rows);
        RebuildSimpleCards(rows);

        var empty = InventoryComboIntelligenceUi.EmptyStateMessage(
            _presented.Targets.Count, rows.Count, _loadError);
        ShowEmpty(simple && SimpleCardsHost.Children.Count > 0 ? "" : empty);

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

    void RebuildSimpleCards(IReadOnlyList<InventoryComboTargetGridRow> rows)
    {
        SimpleCardsHost.Children.Clear();
        if (InventorySmartViewPreference.Current != InventorySmartViewMode.Simple)
            return;

        AddComboSection("Combos recomendados", rows.Where(r => r.SuggestionCount > 0).Take(12), urgent: false);
        AddComboSection(
            "Urgentes sem combo",
            rows.Where(r => r.SuggestionCount == 0
                && r.Reason == ComboTargetEligibilityReason.ExpirySurplus).Take(8),
            urgent: true);
        AddComboSection(
            "Rejeitados",
            rows.Where(r => r.SuggestionCount == 0
                && r.Reason != ComboTargetEligibilityReason.ExpirySurplus).Take(8),
            urgent: false);
    }

    void AddComboSection(string title, IEnumerable<InventoryComboTargetGridRow> items, bool urgent)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return;
        SimpleCardsHost.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#334155")!,
            Margin = new Thickness(0, 4, 0, 6),
        });
        foreach (var row in list)
        {
            var card = new InventorySmartCard
            {
                ProductId = row.ProductId,
                ProductCode = row.Code,
                ProductName = row.Name,
                ProductTitle = row.ProductTitle,
                PrincipalAction = row.SuggestionCount > 0
                    ? InventorySmartPrincipalAction.EvaluateCombo
                    : InventorySmartPrincipalAction.NoSafePromotion,
                ActionText = row.SuggestionCount > 0 ? "Avaliar combo" : "Sem combo seguro",
                QuantityOrDeadlineText = "Estoque " + row.StockText,
                ReasonText = row.SuggestionCount > 0
                    ? row.ReasonText
                    : (row.EmptyMessage.Length > 0 ? row.EmptyMessage : row.ReasonText),
                Urgency = urgent ? InventorySmartUrgency.High : InventorySmartUrgency.None,
                UrgencyText = urgent ? "Urgente" : row.ConfidenceText,
                Tone = row.SuggestionCount > 0 ? "notice" : "info",
            };
            SimpleCardsHost.Children.Add(InventorySmartCardUi.Create(card, ComboCardDetails_Click));
        }
    }

    void ComboCardDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
            return;
        foreach (var item in Grid.Items)
        {
            if (item is InventoryComboTargetGridRow row && row.ProductId == id)
            {
                Grid.SelectedItem = row;
                UpdateDetail();
                return;
            }
        }
    }
}

/// <summary>
/// Compatibilidade 71A-B8F. Delega ao helper compartilhado testável.
/// </summary>
public static class InventoryComboWheelScroll
{
    public static InventoryComboWheelRoute Route(
        double innerOffset,
        double innerScrollable,
        double outerOffset,
        double outerScrollable,
        int delta) =>
        InventoryNestedWheelScroll.Route(
            innerOffset, innerScrollable, outerOffset, outerScrollable, delta);

    public static bool TryApplyVertical(
        double verticalOffset,
        double scrollableHeight,
        int delta,
        out double nextOffset) =>
        InventoryNestedWheelScroll.TryApplyVertical(
            verticalOffset, scrollableHeight, delta, out nextOffset);
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
