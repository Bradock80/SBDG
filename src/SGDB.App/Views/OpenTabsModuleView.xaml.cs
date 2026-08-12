using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class OpenTabsModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private const string SettingViewMode = "decks_view_mode";
    private const string SettingTableCount = "decks_table_count";

    private readonly List<OpenTabListRow> _allRows = [];
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _livePollTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _gridMode;
    private bool _suppressTableCount;
    private int _tableCount = 24;
    private DeckTableCard? _selectedCard;
    private IReadOnlyList<DeckTableCard> _allCards = [];

    public OpenTabsModuleView()
    {
        InitializeComponent();
        _elapsedTimer.Tick += (_, _) => RefreshElapsedOnly();
        _livePollTimer.Tick += (_, _) =>
        {
            if (IsVisible)
                Reload();
        };
        Loaded += (_, _) =>
        {
            Focus();
            LoadViewPrefs();
            Reload();
            _elapsedTimer.Start();
            _livePollTimer.Start();
            OpenTabService.OpenTabsChanged += OnOpenTabsChanged;
        };
        Unloaded += (_, _) =>
        {
            _elapsedTimer.Stop();
            _livePollTimer.Stop();
            OpenTabService.OpenTabsChanged -= OnOpenTabsChanged;
        };
    }

    private void OnOpenTabsChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (IsLoaded && IsVisible)
                Reload();
        });
    }

    private OpenTabListRow? Selected =>
        _gridMode
            ? _selectedCard?.Tab
            : TabsGrid.SelectedItem as OpenTabListRow;

    private IReadOnlyList<OpenTabListRow> SelectedRows =>
        _gridMode
            ? (Selected is null ? [] : [Selected])
            : TabsGrid.SelectedItems.OfType<OpenTabListRow>().ToList();

    private void LoadViewPrefs()
    {
        _suppressTableCount = true;
        try
        {
            if (int.TryParse(AppSettingsService.GetSetting(SettingTableCount), out var n) && n is >= 1 and <= 80)
                _tableCount = n;

            foreach (ComboBoxItem item in TableCountBox.Items)
            {
                if (item.Content?.ToString() == _tableCount.ToString())
                {
                    TableCountBox.SelectedItem = item;
                    break;
                }
            }

            var mode = AppSettingsService.GetSetting(SettingViewMode) ?? "list";
            SetViewMode(string.Equals(mode, "grid", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressTableCount = false;
        }
    }

    private void SetViewMode(bool grid)
    {
        _gridMode = grid;
        ViewListBtn.IsChecked = !grid;
        ViewGridBtn.IsChecked = grid;
        TabsGrid.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        CardsScroller.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        TableCountPanel.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        LegendPanel.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        AppSettingsService.SetSetting(SettingViewMode, grid ? "grid" : "list");
        ApplyFilter();
    }

    private void ViewList_Click(object sender, RoutedEventArgs e) => SetViewMode(false);

    private void ViewGrid_Click(object sender, RoutedEventArgs e) => SetViewMode(true);

    private void TableCountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTableCount || !IsLoaded)
            return;
        if (TableCountBox.SelectedItem is not ComboBoxItem item)
            return;
        if (!int.TryParse(item.Content?.ToString(), out var n) || n < 1)
            return;
        _tableCount = n;
        AppSettingsService.SetSetting(SettingTableCount, n.ToString());
        if (_gridMode)
            ApplyFilter();
    }

    private void Reload()
    {
        _allRows.Clear();
        _allRows.AddRange(OpenTabService.ListOpen());
        ApplyFilter();
    }

    private void RefreshElapsedOnly()
    {
        var selectedIds = SelectedRows.Select(r => r.Id).ToHashSet();
        ApplyFilter();
        if (!_gridMode)
        {
            foreach (var row in TabsGrid.Items.OfType<OpenTabListRow>())
            {
                if (selectedIds.Contains(row.Id))
                    TabsGrid.SelectedItems.Add(row);
            }
        }
    }

    private void ApplyFilter()
    {
        var term = (SearchBox?.Text ?? "").Trim();
        IEnumerable<OpenTabListRow> query = _allRows;
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = _allRows.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.CustomerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Notes?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var rows = query.ToList();
        TabsGrid.ItemsSource = new ObservableCollection<OpenTabListRow>(rows);

        if (_gridMode)
        {
            // Mapa fixo 01..N: a mesa permanece no mesmo lugar; só muda cor/status.
            _allCards = DeckTableHelper.BuildCards(_allRows, _tableCount);
            MapCardsHost.ItemsSource = _allCards;
            var ocupadas = _allCards.Count(c => !c.IsFree);
            var livres = _allCards.Count(c => c.IsFree);
            MapHeaderText.Text = $"Mapa de mesas ({_allCards.Count})";
            MapStatsText.Text = $"{ocupadas} aberta(s) · {livres} livre(s)";

            var balcaoAll = DeckTableHelper.BuildBalcaoCards(_allRows, _tableCount);
            IEnumerable<DeckTableCard> balcao = balcaoAll;
            if (!string.IsNullOrWhiteSpace(term))
            {
                balcao = balcaoAll.Where(c =>
                    (c.Tab?.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (c.Tab?.CustomerName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (c.Tab?.Notes?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || c.NumberDisplay.Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            var balcaoList = balcao.ToList();
            BalcaoCardsHost.ItemsSource = balcaoList;
            BalcaoHeaderText.Text = $"Balcão / Avulso ({balcaoList.Count})";
            BalcaoSection.Visibility = balcaoList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_selectedCard is not null)
            {
                _selectedCard = _allCards.FirstOrDefault(c => c.TableNumber == _selectedCard.TableNumber)
                    ?? balcaoList.FirstOrDefault(c => c.Tab?.Id == _selectedCard.Tab?.Id);
            }
        }

        var total = rows.Sum(r => r.Total);
        SummaryCountText.Text = $"{rows.Count} deck(s) aberto(s)";
        SummaryTotalText.Text = $"Total: R$ {total:N2}";
        MetaText.Text = _gridMode
            ? "A mesa fica no mesmo lugar — só muda a cor ao abrir · F2 novo · F9 pré-conta"
            : rows.Count == 0
                ? (string.IsNullOrWhiteSpace(term)
                    ? "Nenhum deck aberto. Pressione F2 para criar (ex.: Fernando)."
                    : "Nenhum deck corresponde à busca.")
                : "Ctrl+clique para selecionar vários · F4 junta · F9 pré-conta · Enter abre";

        if (!_gridMode && rows.Count > 0 && TabsGrid.SelectedItems.Count == 0)
            TabsGrid.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void New_Click(object sender, RoutedEventArgs e) => CreateNew();

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void PreConta_Click(object sender, RoutedEventArgs e) => PrintPreContaSelected();

    private void Merge_Click(object sender, RoutedEventArgs e) => MergeSelected();

    private void Companion_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dlg = new DeckCompanionWindow { Owner = owner };
        dlg.ShowDialog();
        Reload();
    }

    private void DeckCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DeckTableCard card)
            return;

        _selectedCard = card;
        if (card.IsFree)
        {
            var label = $"Mesa {card.NumberDisplay}";
            CreateNew(suggestedName: label, suggestedNotes: label);
            return;
        }

        OpenTab(card.Tab!.Id);
    }

    private void TabsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d
            && FindParent<DataGridCell>(d) is { } cell
            && cell.Column?.Header?.ToString()?.Contains("Obs", StringComparison.OrdinalIgnoreCase) == true)
            return;

        OpenSelected();
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
                return match;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void TabsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;
        if (e.Row.Item is not OpenTabListRow row)
            return;
        if (e.EditingElement is not TextBox box)
            return;

        var notes = box.Text?.Trim() ?? "";
        try
        {
            OpenTabService.UpdateNotes(row.Id, notes);
            Reload();
            var again = _allRows.FirstOrDefault(r => r.Id == row.Id);
            if (again is not null)
                TabsGrid.SelectedItem = again;
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            Reload();
        }
    }

    private void CreateNew(string? suggestedName = null, string? suggestedNotes = null)
    {
        var owner = Window.GetWindow(this);
        if (!PromptNewDeck(owner, out var name, out var notes, suggestedName, suggestedNotes))
            return;

        try
        {
            var id = OpenTabService.Create(name, notes: notes);
            Reload();
            OpenTab(id);
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool PromptNewDeck(
        Window? owner,
        out string name,
        out string? notes,
        string? suggestedName = null,
        string? suggestedNotes = null)
    {
        name = "";
        notes = null;
        var dlg = new Window
        {
            Title = "Novo deck",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Tahoma, Segoe UI"),
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Nome da conta / mesa (ex.: Fernando ou Mesa 5)",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x47, 0x55, 0x69)),
        });
        var box = new TextBox
        {
            Height = 32,
            FontSize = 14,
            Padding = new Thickness(8, 4, 8, 4),
            MaxLength = 80,
            Text = suggestedName ?? "",
        };
        panel.Children.Add(box);

        panel.Children.Add(new TextBlock
        {
            Text = "Obs / Mesa (opcional)",
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 6),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x47, 0x55, 0x69)),
        });
        var notesBox = new TextBox
        {
            Height = 32,
            FontSize = 13,
            Padding = new Thickness(8, 4, 8, 4),
            MaxLength = 120,
            Text = suggestedNotes ?? "",
        };
        panel.Children.Add(notesBox);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        string? resultName = null;
        string? resultNotes = null;
        var ok = new Button
        {
            Content = "Abrir",
            IsDefault = true,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 90,
        };
        ok.Click += (_, _) =>
        {
            resultName = box.Text?.Trim();
            resultNotes = string.IsNullOrWhiteSpace(notesBox.Text) ? null : notesBox.Text.Trim();
            dlg.DialogResult = true;
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            IsCancel = true,
            Padding = new Thickness(16, 6, 16, 6),
            MinWidth = 90,
        };
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        dlg.Content = panel;
        dlg.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(resultName))
            return false;

        name = resultName;
        notes = resultNotes;
        return true;
    }

    private void OpenSelected()
    {
        if (Selected is null)
        {
            MessageBox.Show(
                _gridMode
                    ? "Selecione um card ocupado (ou toque nele)."
                    : "Selecione um deck na lista.",
                "Decks",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenTab(Selected.Id);
    }

    private void OpenTab(int tabId)
    {
        var owner = Window.GetWindow(this);
        var w = new OpenTabDetailWindow(tabId) { Owner = owner };
        w.ShowDialog();
        Reload();
    }

    private void PrintPreContaSelected()
    {
        if (Selected is null)
        {
            MessageBox.Show("Selecione um deck para imprimir a pré-conta.", "Decks",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            OpenTabService.PrintPreConta(Selected.Id);
            MessageBox.Show($"Pré-conta de \"{Selected.Name}\" enviada à impressora.", "Decks",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Decks — Pré-conta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MergeSelected()
    {
        var rows = SelectedRows;
        if (rows.Count < 2)
        {
            MessageBox.Show(
                "Selecione 2 ou mais decks (Ctrl+clique na lista) para juntar.\n\n" +
                "Na visão Mesas, use a Lista para juntar várias contas.",
                "Decks", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = rows.OrderByDescending(r => r.Total).ThenBy(r => r.Id).First();
        var sources = rows.Where(r => r.Id != target.Id).ToList();
        var names = string.Join(", ", sources.Select(s => s.Name));
        var ask = MessageBox.Show(
            $"Juntar os decks abaixo em \"{target.Name}\"?\n\n" +
            $"Origens: {names}\n\n" +
            "Os decks de origem serão cancelados e os itens vão para a conta destino.",
            "Juntar decks", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
            return;

        try
        {
            OpenTabService.MergeTabs(target.Id, sources.Select(s => s.Id).ToList());
            Reload();
            var again = _allRows.FirstOrDefault(r => r.Id == target.Id);
            if (again is not null && !_gridMode)
                TabsGrid.SelectedItem = again;
            MessageBox.Show($"Decks unidos em \"{target.Name}\".", "Decks",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OpenTabException ex)
        {
            MessageBox.Show(ex.Message, "Decks", MessageBoxButton.OK, MessageBoxImage.Warning);
            Reload();
        }
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
        else if (e.Key == Key.F1)
        {
            Reload();
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            CreateNew();
            e.Handled = true;
        }
        else if (e.Key == Key.F4)
        {
            MergeSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.F9)
        {
            PrintPreContaSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !SearchBox.IsKeyboardFocusWithin)
        {
            OpenSelected();
            e.Handled = true;
        }
    }
}
