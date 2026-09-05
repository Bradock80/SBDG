using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class CommercialGoalModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    CommercialCompetence _competence;
    CommercialGoalPresentationSnapshot _presented;
    bool _hasValidSnapshot;
    bool _clientBlocked;
    bool _loading;
    bool _settingsOpen;

    public CommercialGoalModuleView()
    {
        _competence = CommercialCompetence.FromDate(Today());
        _presented = CommercialGoalPresentation.Empty(_competence, Today());
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            ApplyConfigurePermission();
            Load();
        };
    }

    static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
        {
            if (!_clientBlocked && !_settingsOpen)
                Load();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_settingsOpen)
                CloseSettings();
            else
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

    private void Configure_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => CloseSettings();

    private void SaveDefault_Click(object sender, RoutedEventArgs e) =>
        ApplyAdminResult(CommercialGoalAdminService.TrySaveDefault(_competence, DefaultBox.Text));

    private void SaveMonthly_Click(object sender, RoutedEventArgs e) =>
        ApplyAdminResult(CommercialGoalAdminService.TrySaveOverride(_competence, MonthlyBox.Text));

    private void ClearDefault_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmClear("Remover a meta padrão?\n\nMeses sem meta específica deixarão de ter meta."))
            return;
        ApplyAdminResult(CommercialGoalAdminService.TryClearDefault(_competence, confirmed: true));
    }

    private void ClearMonthly_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmClear("Remover a meta específica deste mês?\n\nEste mês voltará a usar a meta padrão, se houver."))
            return;
        ApplyAdminResult(CommercialGoalAdminService.TryClearOverride(_competence, confirmed: true));
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
        if (!_settingsOpen)
            ContentRoot.Visibility = Visibility.Visible;

        var previousCursor = Cursor;
        LoadFailureDecision? failure = null;
        _loading = true;
        BtnRefresh.IsEnabled = false;
        try
        {
            Cursor = Cursors.Wait;
            _presented = CommercialGoalLoader.Load(_competence, Today());
            _hasValidSnapshot = true;
            ApplyView();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            failure = CommercialGoalUi.ResolveLoadFailure(_hasValidSnapshot);
            if (!failure.Value.KeepPreviousSnapshot)
            {
                _presented = CommercialGoalPresentation.Empty(
                    _competence, Today(),
                    CommercialGoalUi.LoadErrorMessage,
                    failure.Value.OperatorMessage);
                ApplyView();
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
                CommercialGoalUi.ModuleTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowClientBlocked()
    {
        _clientBlocked = true;
        _presented = CommercialGoalPresentation.Empty(_competence, Today());
        ContentRoot.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        ClientBlockOverlay.Visibility = Visibility.Visible;
        ClientBlockText.Text = StoreNetworkMode.ClientBlockedModuleMessage;
    }

    private void ApplyConfigurePermission()
    {
        var can = CommercialGoalAdminService.CanMutate()
            && CommercialGoalAdminService.StationAllowsWrite();
        BtnConfigure.Visibility = can ? Visibility.Visible : Visibility.Collapsed;
        BtnCalloutConfigure.Visibility = can ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyView()
    {
        var layout = CommercialGoalKpiLayout.From(_presented);
        CompetenceText.Text = CommercialGoalUi.FormatCompetenceTitle(_presented.Competence);
        OriginFooter.Text = _presented.GoalOriginText;

        ApplyCallout();
        ApplyEstimated();
        ApplyHero(layout.Hero);
        ApplyMetric(Decision0, Decision0Title, Decision0Value, Decision0Support, layout.Decision[0]);
        ApplyMetric(Decision1, Decision1Title, Decision1Value, Decision1Support, layout.Decision[1]);
        ApplyMetric(Decision2, Decision2Title, Decision2Value, Decision2Support, layout.Decision[2]);
        ApplyMetric(Context0, Context0Title, Context0Value, null, layout.Context[0]);
        ApplyMetric(Context1, Context1Title, Context1Value, Context1Support, layout.Context[1]);
        ApplyStatus(layout.Context[2]);
        LimitationsList.ItemsSource = _presented.Limitations;
        ApplyConfigurePermission();
    }

    private void ApplyCallout()
    {
        var show = CommercialGoalUi.ShowCallout(_presented);
        CalloutBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CalloutHeadline.Text = _presented.Headline;
        CalloutSupport.Text = _presented.SupportingText;
        var can = CommercialGoalAdminService.CanMutate()
            && CommercialGoalAdminService.StationAllowsWrite();
        BtnCalloutConfigure.Visibility = show && can ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyEstimated()
    {
        var show = CommercialGoalUi.ShowEstimatedBanner(_presented);
        EstimatedBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        EstimatedTitle.Text = _presented.EstimatedBadge;
        EstimatedBody.Text = _presented.EstimatedExplanation;
    }

    private void ApplyHero(CommercialGoalMetricPresentation metric)
    {
        HeroTitle.Text = metric.Title;
        HeroValue.Text = metric.ValueText;
        HeroSupport.Text = metric.SupportingText;
        Paint(HeroCard, HeroValue, metric.Tone);
        var showBadge = _presented.ShowEstimatedBadge;
        HeroBadge.Visibility = showBadge ? Visibility.Visible : Visibility.Collapsed;
        HeroBadgeText.Text = _presented.EstimatedBadge;
    }

    private void ApplyStatus(CommercialGoalMetricPresentation metric)
    {
        Context2Title.Text = metric.Title;
        Context2Value.Text = metric.ValueText;
        var colors = CommercialGoalUi.ToneColors(metric.Tone);
        StatusBadge.Background = Brush(colors.Bg);
        Context2Value.Foreground = Brush(colors.Fg);
    }

    static void ApplyMetric(
        Border card,
        TextBlock title,
        TextBlock value,
        TextBlock? support,
        CommercialGoalMetricPresentation metric)
    {
        title.Text = metric.Title;
        value.Text = metric.ValueText;
        if (support is not null)
            support.Text = metric.SupportingText;
        Paint(card, value, metric.Tone);
    }

    static void Paint(Border card, TextBlock value, CommercialGoalPresentationTone tone)
    {
        var colors = CommercialGoalUi.ToneColors(tone);
        card.Background = Brush(colors.Bg);
        value.Foreground = Brush(colors.Fg);
    }

    static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);

    private void OpenSettings()
    {
        if (_clientBlocked) return;
        BindSettings(CommercialGoalAdminService.LoadEditor(_competence), feedback: "");
        _settingsOpen = true;
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSettings()
    {
        _settingsOpen = false;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        SettingsFeedback.Text = "";
    }

    private void ApplyAdminResult(CommercialGoalAdminResult result)
    {
        BindSettings(result.Snapshot, result.Message, isError: !result.Succeeded);
        if (result.Succeeded)
            Load();
    }

    private void BindSettings(CommercialGoalAdminSnapshot snapshot, string feedback, bool isError = false)
    {
        SettingsOrigin.Text = snapshot.OriginText;
        DefaultStatus.Text = snapshot.DefaultStatusText;
        MonthlyStatus.Text = snapshot.MonthlyStatusText;
        MonthlyCaption.Text = CommercialGoalUi.MonthlyCaption(snapshot.Competence);
        HistoricalNote.Text = snapshot.HistoricalDefaultNote;
        DefaultBox.Text = snapshot.DefaultEditorText;
        MonthlyBox.Text = snapshot.MonthlyEditorText;

        var canWrite = snapshot.CanMutate && snapshot.StationAllowsWrite;
        DefaultBox.IsEnabled = canWrite;
        MonthlyBox.IsEnabled = canWrite;
        SaveDefaultBtn.IsEnabled = canWrite;
        ClearDefaultBtn.IsEnabled = canWrite;
        SaveMonthlyBtn.IsEnabled = canWrite;
        ClearMonthlyBtn.IsEnabled = canWrite;

        SettingsFeedback.Text = feedback ?? "";
        SettingsFeedback.Foreground = Brush(isError ? "#B91C1C" : "#0F766E");
    }

    static bool ConfirmClear(string message) =>
        MessageBox.Show(
            message,
            CommercialGoalUi.ModuleTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;
}
