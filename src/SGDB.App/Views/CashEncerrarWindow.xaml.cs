using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class CashEncerrarWindow : Window
{
    private static readonly Brush DiffOkBg = BrushFrom("#ECFDF5");
    private static readonly Brush DiffOkBorder = BrushFrom("#A7F3D0");
    private static readonly Brush DiffOkFg = BrushFrom("#047857");
    private static readonly Brush DiffOkPrefix = BrushFrom("#D1FAE5");

    private static readonly Brush DiffShortBg = BrushFrom("#FEF2F2");
    private static readonly Brush DiffShortBorder = BrushFrom("#FECACA");
    private static readonly Brush DiffShortFg = BrushFrom("#B91C1C");
    private static readonly Brush DiffShortPrefix = BrushFrom("#FEE2E2");

    private static readonly Brush DiffOverBg = BrushFrom("#EFF6FF");
    private static readonly Brush DiffOverBorder = BrushFrom("#BFDBFE");
    private static readonly Brush DiffOverFg = BrushFrom("#1D4ED8");
    private static readonly Brush DiffOverPrefix = BrushFrom("#DBEAFE");

    private static readonly Brush DiffNeutralBg = BrushFrom("#F8FAFC");
    private static readonly Brush DiffNeutralBorder = BrushFrom("#CBD5E1");
    private static readonly Brush DiffNeutralFg = BrushFrom("#0F172A");
    private static readonly Brush DiffNeutralPrefix = BrushFrom("#E2E8F0");

    private static readonly double[] NoteValues =
        { 200, 100, 50, 20, 10, 5, 2, 1, 0.50, 0.25, 0.10, 0.05 };

    private readonly CashOperacaoView _view;
    private readonly List<TextBox> _noteBoxOrder = new();
    private readonly Dictionary<double, TextBox> _noteBoxes = new();
    private bool _notesBuilt;
    private bool _busy;
    private bool _esperadoRevelado;
    private bool _hasDifference;

    public double Contado { get; private set; }
    public string? Observacao { get; private set; }

    public CashEncerrarWindow(CashOperacaoView view)
    {
        _view = view;
        InitializeComponent();
        InputUxHelper.Attach(this, ContadoBox, ObsBox);

        SessionDateText.Text = string.IsNullOrWhiteSpace(view.SessionDateBr)
            ? ""
            : $"Caixa {view.SessionDateBr}";

        SaldoInicialText.Text = Money(view.SaldoInicial);
        EntradasText.Text = Money(view.EntradasCaixa);
        SaidasText.Text = Money(view.SaidasCaixa);

        BuildFormas();

        ContadoBox.Text = "";
        UpdateDiferenca();
        UpdateConfirmEnabled();

        ContadoBox.LostFocus += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(ContadoBox.Text))
                ContadoBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(ContadoBox.Text));
        };

        Loaded += (_, _) => ContadoBox.Focus();
    }

    private void BuildFormas()
    {
        if (_view.EntradasPorForma.Count == 0)
        {
            FormasPanel.Visibility = Visibility.Collapsed;
            return;
        }

        FormasPanel.Visibility = Visibility.Visible;
        FormasList.ItemsSource = _view.EntradasPorForma
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new FormaLinha(kv.Key, Money(kv.Value)))
            .ToList();
    }

    private void ContadoBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDiferenca();
        UpdateConfirmEnabled();
    }

    private void ObsBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateConfirmEnabled();

    private void UpdateDiferenca()
    {
        var hasCount = !string.IsNullOrWhiteSpace(ContadoBox.Text);
        if (hasCount && !_esperadoRevelado)
            RevealEsperado();

        if (!hasCount)
        {
            DiferencaBox.Text = "—";
            DiffStatusText.Text = "";
            DiffAlertText.Visibility = Visibility.Collapsed;
            ApplyDiffStyle(DiffNeutralBg, DiffNeutralBorder, DiffNeutralFg, DiffNeutralPrefix);
            _hasDifference = false;
            SetObsRequired(false);
            return;
        }

        var contado = ProductPriceHelper.ParseBr(ContadoBox.Text);
        var diff = ProductPriceHelper.RoundPrice(contado - _view.SaldoFinalGaveta);
        DiferencaBox.Text = ProductPriceHelper.FormatBr(diff);
        _hasDifference = Math.Abs(diff) >= 0.009;

        if (!_hasDifference)
        {
            DiffStatusText.Text = "Bateu";
            DiffAlertText.Visibility = Visibility.Collapsed;
            ApplyDiffStyle(DiffOkBg, DiffOkBorder, DiffOkFg, DiffOkPrefix);
            SetObsRequired(false);
        }
        else if (diff < 0)
        {
            DiffStatusText.Text = "Falta";
            DiffAlertText.Text = $"Quebra: falta R$ {ProductPriceHelper.FormatBr(Math.Abs(diff))} em relação ao esperado.";
            DiffAlertText.Foreground = DiffShortFg;
            DiffAlertText.Visibility = Visibility.Visible;
            ApplyDiffStyle(DiffShortBg, DiffShortBorder, DiffShortFg, DiffShortPrefix);
            SetObsRequired(true);
        }
        else
        {
            DiffStatusText.Text = "Sobra";
            DiffAlertText.Text = $"Quebra: sobra R$ {ProductPriceHelper.FormatBr(diff)} em relação ao esperado.";
            DiffAlertText.Foreground = DiffOverFg;
            DiffAlertText.Visibility = Visibility.Visible;
            ApplyDiffStyle(DiffOverBg, DiffOverBorder, DiffOverFg, DiffOverPrefix);
            SetObsRequired(true);
        }
    }

    private void RevealEsperado()
    {
        _esperadoRevelado = true;
        EsperadoText.Text = Money(_view.SaldoFinalGaveta);
        EsperadoHintText.Text = "Valor do sistema após a contagem";
        EsperadoHintText.Foreground = BrushFrom("#64748B");
    }

    private void ApplyDiffStyle(Brush bg, Brush border, Brush fg, Brush prefixBg)
    {
        DiffShell.Background = bg;
        DiffShell.BorderBrush = border;
        DiffPrefix.Background = prefixBg;
        DiffPrefix.BorderBrush = border;
        DiffPrefixText.Foreground = fg;
        DiferencaBox.Foreground = fg;
        DiffStatusText.Foreground = fg;
    }

    private void SetObsRequired(bool required)
    {
        ObsRequiredBadge.Visibility = required ? Visibility.Visible : Visibility.Collapsed;
        ObsHintText.Visibility = required ? Visibility.Visible : Visibility.Collapsed;
        ObsBox.BorderBrush = required
            ? (Brush)new BrushConverter().ConvertFromString("#FCA5A5")!
            : (Brush)new BrushConverter().ConvertFromString("#CBD5E1")!;
        ObsLabel.Text = required ? "Observação / Justificativa de Quebra" : "Observação";
    }

    private void UpdateConfirmEnabled()
    {
        if (_busy)
        {
            ConfirmBtn.IsEnabled = false;
            return;
        }

        var hasCount = !string.IsNullOrWhiteSpace(ContadoBox.Text)
            && ProductPriceHelper.ParseBr(ContadoBox.Text) >= 0;
        var obsOk = !_hasDifference || !string.IsNullOrWhiteSpace(ObsBox.Text);
        ConfirmBtn.IsEnabled = hasCount && obsOk;
    }

    private void ToggleNotes_Click(object sender, RoutedEventArgs e)
    {
        EnsureNotesBuilt();
        var open = NotesPanel.Visibility != Visibility.Visible;
        NotesPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ToggleNotesBtn.Content = open ? "Ocultar cédulas" : "Somar cédulas";
        if (open && _noteBoxOrder.Count > 0)
        {
            _noteBoxOrder[0].Focus();
            _noteBoxOrder[0].SelectAll();
        }
    }

    private void EnsureNotesBuilt()
    {
        if (_notesBuilt)
            return;
        _notesBuilt = true;

        foreach (var value in NoteValues)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 6, 4) };
            var label = new TextBlock
            {
                Text = value >= 1
                    ? $"R$ {value:0}"
                    : $"R$ {value.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}",
                Width = 48,
                FontSize = 11,
                Foreground = BrushFrom("#78350F"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var qty = new TextBox
            {
                Width = 44,
                Height = 26,
                Text = "0",
                TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 4, 0),
                BorderBrush = BrushFrom("#FCD34D"),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                FontSize = 12,
                Tag = value,
            };
            qty.TextChanged += (_, _) => RecalcNotesTotal();
            qty.GotKeyboardFocus += (_, _) => qty.SelectAll();
            qty.PreviewKeyDown += NoteQty_PreviewKeyDown;
            _noteBoxes[value] = qty;
            _noteBoxOrder.Add(qty);

            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(qty);
            NotesGrid.Children.Add(row);
        }
    }

    private void NoteQty_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        if (e.Key is not (Key.Enter or Key.Tab))
            return;

        var idx = _noteBoxOrder.IndexOf(box);
        if (idx < 0)
            return;

        var reverse = e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Shift;
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.None)
            return;

        var nextIdx = reverse ? idx - 1 : idx + 1;
        if (nextIdx >= 0 && nextIdx < _noteBoxOrder.Count)
        {
            _noteBoxOrder[nextIdx].Focus();
            _noteBoxOrder[nextIdx].SelectAll();
        }
        else if (!reverse)
        {
            ContadoBox.Focus();
            ContadoBox.SelectAll();
        }

        e.Handled = true;
    }

    private double _notesTotal;

    private void RecalcNotesTotal()
    {
        double total = 0;
        foreach (var (value, box) in _noteBoxes)
        {
            if (int.TryParse(box.Text.Trim(), out var qty) && qty > 0)
                total += value * qty;
        }
        _notesTotal = ProductPriceHelper.RoundPrice(total);
        NotesTotalText.Text = Money(_notesTotal);
    }

    private void UsarNotasNoContado_Click(object sender, RoutedEventArgs e)
    {
        ContadoBox.Text = ProductPriceHelper.FormatBr(_notesTotal);
        ContadoBox.Focus();
        ContadoBox.SelectAll();
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        await TryConfirmAsync();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        DialogResult = false;
        Close();
    }

    private async Task TryConfirmAsync()
    {
        if (!Validate())
            return;

        Contado = ProductPriceHelper.ParseBr(ContadoBox.Text);
        Observacao = string.IsNullOrWhiteSpace(ObsBox.Text) ? null : ObsBox.Text.Trim();

        SetBusy(true);
        try
        {
            await Task.Run(() => CashService.CloseSession(Contado, Observacao));
            DialogResult = true;
            Close();
        }
        catch (CashOperationException ex)
        {
            MessageBox.Show(ex.Message, "Fechar caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível fechar o caixa.\n{ex.Message}", "Fechar caixa",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateConfirmEnabled();
        }
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(ContadoBox.Text))
        {
            MessageBox.Show("Informe o dinheiro contado na gaveta.", "Fechar caixa",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ContadoBox.Focus();
            return false;
        }

        var contado = ProductPriceHelper.ParseBr(ContadoBox.Text);
        if (contado < 0)
        {
            MessageBox.Show("Valor contado inválido.", "Fechar caixa",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ContadoBox.Focus();
            return false;
        }

        var diff = ProductPriceHelper.RoundPrice(contado - _view.SaldoFinalGaveta);
        if (Math.Abs(diff) >= 0.009 && string.IsNullOrWhiteSpace(ObsBox.Text))
        {
            MessageBox.Show(
                "Há quebra de caixa (diferença).\nPreencha a Observação / Justificativa de Quebra.",
                "Fechar caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
            ObsBox.Focus();
            return false;
        }

        return true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CancelBtn.IsEnabled = !busy;
        ContadoBox.IsEnabled = !busy;
        ObsBox.IsEnabled = !busy;
        LoadingPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ConfirmIcon.Text = busy ? "…" : "✓";
        ConfirmLabel.Text = busy ? "Fechando…" : "Confirmar fechamento";
        UpdateConfirmEnabled();
    }

    private bool IsNoteBoxFocused() =>
        Keyboard.FocusedElement is TextBox tb && _noteBoxOrder.Contains(tb);

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_busy)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        // Enter em cédula: navega (já tratado no Preview do campo). Não confirma.
        if (IsNoteBoxFocused() && e.Key == Key.Enter)
            return;

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None
            && !ReferenceEquals(Keyboard.FocusedElement, ObsBox)
            && ConfirmBtn.IsEnabled)
        {
            e.Handled = true;
            await TryConfirmAsync();
        }
    }

    private static string Money(double value) =>
        "R$ " + ProductPriceHelper.FormatBr(value);

    private static SolidColorBrush BrushFrom(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    private sealed record FormaLinha(string Key, string ValueDisplay);
}
