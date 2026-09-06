using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

/// <summary>
/// Módulo visual executivo 71C-B5. Consome CentralDecisionLoader.
/// Sem ranking, SQL ou segundo loader.
/// </summary>
public partial class CentralDecisionModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    CommercialCompetence _competence;
    DateOnly _referenceDate;
    CentralDecisionPresentationSnapshot _presented;
    bool _hasValidSnapshot;
    bool _clientBlocked;
    bool _loading;

    public CentralDecisionModuleView()
    {
        _referenceDate = Today();
        _competence = CommercialCompetence.FromDate(_referenceDate);
        _presented = CentralDecisionUi.UnavailablePresentation(
            _competence,
            _referenceDate,
            "");
        InitializeComponent();
        Loaded += OnLoaded;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Focus();
        RefreshViewToggle();
        Load();
    }

    void RefreshViewToggle()
    {
        ViewModeHost.Children.Clear();
        var mode = InventorySmartViewPreference.LoadIfNeeded();
        var toggle = InventorySmartCardUi.CreateViewToggle(
            mode,
            (_, _) => SetView(InventorySmartViewMode.Simple),
            (_, _) => SetView(InventorySmartViewMode.Detailed));
        ViewModeHost.Children.Add(toggle);
    }

    void SetView(InventorySmartViewMode mode)
    {
        InventorySmartViewPreference.Set(mode);
        RefreshViewToggle();
        ApplyView();
    }

    static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

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
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_clientBlocked)
            Load();
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_clientBlocked) return;
        _competence = CommercialCompetence.FromDate(_competence.StartDate.AddMonths(-1));
        Load();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_clientBlocked) return;
        _competence = CommercialCompetence.FromDate(_competence.StartDate.AddMonths(1));
        Load();
    }

    private void CurrentMonth_Click(object sender, RoutedEventArgs e)
    {
        if (_clientBlocked) return;
        _competence = CommercialCompetence.FromDate(Today());
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

        var referenceDate = Today();
        _referenceDate = referenceDate;

        var previousCursor = Cursor;
        LoadFailureDecision? failure = null;
        _loading = true;
        BtnRefresh.IsEnabled = false;
        try
        {
            Cursor = Cursors.Wait;
            var result = CentralDecisionLoader.Load(_competence, referenceDate);
            _presented = result.Presentation;
            _hasValidSnapshot = true;
            ApplyView();
            OriginFooter.Text = QueryFooter(result.QueryCount);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            failure = CentralDecisionUi.ResolveLoadFailure(_hasValidSnapshot);
            if (!failure.Value.KeepPreviousSnapshot)
            {
                _presented = CentralDecisionUi.UnavailablePresentation(
                    _competence,
                    referenceDate,
                    failure.Value.OperatorMessage);
                ApplyView();
                OriginFooter.Text = failure.Value.OperatorMessage;
            }
            else
            {
                OriginFooter.Text = failure.Value.OperatorMessage;
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
                CentralDecisionUi.ModuleTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowClientBlocked()
    {
        _clientBlocked = true;
        ContentRoot.Visibility = Visibility.Collapsed;
        ClientBlockOverlay.Visibility = Visibility.Visible;
        ClientBlockText.Text = StoreNetworkMode.ClientBlockedModuleMessage;
        BtnRefresh.IsEnabled = false;
    }

    private void ApplyView()
    {
        CompetenceText.Text = CentralDecisionUi.FormatCompetenceTitle(_presented.Competence);

        ApplyStateBanner();
        ApplyGoalStrip();
        ApplyDecisionEmpty();
        ApplyActNow();
        ApplyPreserve();
        ApplyLimitations();
    }

    void ApplyStateBanner()
    {
        var show = CentralDecisionUi.ShowStateBanner(_presented)
            || _presented.Headline.Length > 0;
        StateBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        StateHeadline.Text = _presented.Headline;
        StateSupport.Text = _presented.SupportingText == _presented.Headline
            ? ""
            : _presented.SupportingText;

        var tone = _presented.State switch
        {
            CentralDecisionState.Unavailable => CommercialGoalPresentationTone.Unavailable,
            CentralDecisionState.Limited or CentralDecisionState.Future =>
                CommercialGoalPresentationTone.Attention,
            CentralDecisionState.Empty => CommercialGoalPresentationTone.Neutral,
            _ => CommercialGoalPresentationTone.Neutral,
        };
        var colors = CentralDecisionUi.ToneColors(tone);
        StateBanner.Background = Brush(colors.Bg);
        StateHeadline.Foreground = Brush(colors.Fg);
    }

    void ApplyGoalStrip()
    {
        var strip = _presented.GoalStrip;
        GoalTitle.Text = strip.Goal.Title;
        GoalValue.Text = strip.Goal.ValueText;
        RealizedTitle.Text = strip.Realized.Title;
        RealizedValue.Text = strip.Realized.ValueText;
        RemainingTitle.Text = strip.Remaining.Title;
        RemainingValue.Text = strip.Remaining.ValueText;
        StatusTitle.Text = strip.Status.Title;
        StatusValue.Text = strip.Status.ValueText;

        var statusColors = CentralDecisionUi.ToneColors(strip.Status.Tone);
        StatusBadge.Background = Brush(statusColors.Bg);
        StatusValue.Foreground = Brush(statusColors.Fg);

        EstimatedBadgeText.Text = strip.EstimatedBadge;
        EstimatedBadgeText.Visibility = strip.ShowEstimatedBadge
            ? Visibility.Visible
            : Visibility.Collapsed;

        InventoryOnlyNote.Text = strip.InventoryOnlyNote;
        InventoryOnlyNote.Visibility = strip.InventoryOnlyNote.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    void ApplyDecisionEmpty()
    {
        var show = CentralDecisionUi.ShowEmptyDecisionText(_presented);
        DecisionEmptyText.Text = _presented.EmptyText;
        DecisionEmptyText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void ApplyActNow()
    {
        ActNowTitle.Text = _presented.ActNowTitle;
        ActNowItems.ItemsSource = _presented.ActNow;
        var simple = InventorySmartViewPreference.Current == InventorySmartViewMode.Simple;
        ActNowItems.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        SimpleActNowHost.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        RebuildSimpleActNow(simple);
        ActNowSection.Visibility = _presented.ActNow.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    void ApplyPreserve()
    {
        PreserveTitle.Text = _presented.PreserveTitle;
        PreserveSubtitle.Text = _presented.PreserveSubtitle;
        PreserveItems.ItemsSource = _presented.Preserve;
        var simple = InventorySmartViewPreference.Current == InventorySmartViewMode.Simple;
        PreserveItems.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        SimplePreserveHost.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        RebuildSimplePreserve(simple);
        PreserveSection.Visibility = _presented.Preserve.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    void RebuildSimpleActNow(bool simple)
    {
        SimpleActNowHost.Children.Clear();
        if (!simple)
            return;
        foreach (var item in _presented.ActNow)
        {
            var card = new InventorySmartCard
            {
                ProductId = item.ProductId,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                ProductTitle = item.ProductTitle,
                ActionText = item.WhatText,
                QuantityOrDeadlineText = item.QuantityText,
                ReasonText = item.WhyText,
                UrgencyText = item.AreaText,
                Tone = item.Tone switch
                {
                    CommercialGoalPresentationTone.Warning
                        or CommercialGoalPresentationTone.Unavailable => "alert",
                    CommercialGoalPresentationTone.Attention => "attention",
                    _ => "notice",
                },
            };
            SimpleActNowHost.Children.Add(InventorySmartCardUi.Create(card, null));
        }
    }

    void RebuildSimplePreserve(bool simple)
    {
        SimplePreserveHost.Children.Clear();
        if (!simple)
            return;
        foreach (var item in _presented.Preserve)
        {
            var card = new InventorySmartCard
            {
                ProductId = item.ProductId,
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                ProductTitle = item.ProductTitle,
                ActionText = "Manter como está",
                ReasonText = item.CoverageText,
                UrgencyText = InventorySmartPresentation.AreaPreserve,
                Tone = "positive",
            };
            SimplePreserveHost.Children.Add(InventorySmartCardUi.Create(card, null));
        }
    }

    void ApplyLimitations()
    {
        LimitationsList.ItemsSource = _presented.Limitations;
        LimitationsSection.Visibility = _presented.Limitations.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    static string QueryFooter(int queryCount) =>
        queryCount > 0 ? $"Consultas neste refresh: {queryCount}" : "";

    static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
