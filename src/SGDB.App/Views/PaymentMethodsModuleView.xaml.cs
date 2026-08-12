using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PaymentMethodsModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private sealed class DestOption
    {
        public string Kind { get; init; } = "banco";
        public int? Id { get; init; }
        public string Label { get; init; } = "";
    }

    private bool _isNew;
    private string? _editingId;
    private bool _suppressSelect;

    public PaymentMethodsModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            Reload();
        };
    }

    private PaymentMethodRow? Selected => MethodsGrid.SelectedItem as PaymentMethodRow;

    private void Reload(string? selectId = null)
    {
        _suppressSelect = true;
        var list = PaymentMethodsService.List();
        MethodsGrid.ItemsSource = list;
        _suppressSelect = false;

        var keep = selectId ?? _editingId;
        var pick = list.FirstOrDefault(m => m.Id == keep)
                   ?? list.FirstOrDefault(m => m.Active)
                   ?? list.FirstOrDefault();
        if (pick is not null)
            MethodsGrid.SelectedItem = pick;
        else
            ClearForm();
    }

    private void BindDetail()
    {
        if (_isNew)
            return;

        var m = Selected;
        if (m is null)
        {
            EmptyHint.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            FormTitle.Text = "Detalhes";
            return;
        }

        _editingId = m.Id;
        _isNew = false;
        EmptyHint.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        FormTitle.Text = m.IsSystem ? $"Editar · {m.Name}" : $"Editar · {m.Name} (customizada)";
        StatusMsg.Text = "";

        NameBox.Text = m.Name;
        ApiBox.Text = m.ApiLabel;
        SelectMov(m.MovementType);
        PdvKeyBox.Text = m.PdvKeyDisplay == "—" ? "" : m.PdvKeyDisplay;
        NotesBox.Text = m.Notes ?? "";
        SettlementBox.Text = m.SettlementDays.ToString();
        ActiveBox.IsChecked = m.Active;

        CreditFeeHint.Visibility = string.Equals(m.Id, "credito", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;

        FeeBox.IsEnabled = m.FeeEditable;
        FeeFixedBox.IsEnabled = m.FeeEditable;
        FeeBox.Text = m.FeePercent.ToString("N2");
        FeeFixedBox.Text = ProductPriceHelper.FormatBr(m.FeeFixed);

        BindDestination(m);
        DeleteBtn.Visibility = m.CanDelete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BindDestination(PaymentMethodRow m)
    {
        var options = new List<DestOption>();
        if (m.DestinationLocked)
        {
            options.Add(new DestOption
            {
                Kind = m.DestinationKind,
                Id = null,
                Label = m.DestinationDisplay,
            });
            DestAccountBox.ItemsSource = options;
            DestAccountBox.SelectedIndex = 0;
            DestAccountBox.IsEnabled = false;
            return;
        }

        DestAccountBox.IsEnabled = true;
        options.Add(new DestOption { Kind = "caixa", Label = "Caixa físico (Gaveta)" });
        options.Add(new DestOption { Kind = "receber", Label = "Contas a receber" });
        options.Add(new DestOption { Kind = "banco", Id = null, Label = "Banco — (não vinculado)" });
        foreach (var a in BankService.ListAccounts(onlyActive: true))
            options.Add(new DestOption { Kind = "banco", Id = a.Id, Label = $"Banco — {a.Name}" });

        DestAccountBox.ItemsSource = options;
        DestOption? pick = m.DestinationKind switch
        {
            "caixa" => options.FirstOrDefault(o => o.Kind == "caixa"),
            "receber" => options.FirstOrDefault(o => o.Kind == "receber"),
            _ => options.FirstOrDefault(o => o.Kind == "banco" && o.Id == m.BankAccountId)
                 ?? options.FirstOrDefault(o => o.Kind == "banco" && o.Id is null),
        };
        DestAccountBox.SelectedItem = pick ?? options[0];
    }

    private void ClearForm()
    {
        _isNew = true;
        _editingId = null;
        MethodsGrid.SelectedItem = null;
        EmptyHint.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        FormTitle.Text = "Nova forma de pagamento";
        StatusMsg.Text = "";

        NameBox.Text = "";
        ApiBox.Text = "";
        SelectMov("Entrada");
        PdvKeyBox.Text = "";
        NotesBox.Text = "";
        FeeBox.Text = "0,00";
        FeeFixedBox.Text = "0,00";
        SettlementBox.Text = "0";
        ActiveBox.IsChecked = true;
        FeeBox.IsEnabled = true;
        FeeFixedBox.IsEnabled = true;
        CreditFeeHint.Visibility = Visibility.Collapsed;
        DeleteBtn.Visibility = Visibility.Collapsed;

        var options = new List<DestOption>
        {
            new() { Kind = "caixa", Label = "Caixa físico (Gaveta)" },
            new() { Kind = "receber", Label = "Contas a receber" },
            new() { Kind = "banco", Id = null, Label = "Banco — (não vinculado)" },
        };
        foreach (var a in BankService.ListAccounts(onlyActive: true))
            options.Add(new DestOption { Kind = "banco", Id = a.Id, Label = $"Banco — {a.Name}" });
        DestAccountBox.IsEnabled = true;
        DestAccountBox.ItemsSource = options;
        DestAccountBox.SelectedItem = options.FirstOrDefault(o => o.Kind == "banco" && o.Id is null) ?? options[0];

        NameBox.Focus();
    }

    private void SelectMov(string movementType)
    {
        foreach (ComboBoxItem item in MovBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), movementType, StringComparison.OrdinalIgnoreCase))
            {
                MovBox.SelectedItem = item;
                return;
            }
        }
        MovBox.SelectedIndex = 0;
    }

    private PaymentMethodInput BuildInput()
    {
        var dest = DestAccountBox.SelectedItem as DestOption
                   ?? new DestOption { Kind = "banco" };
        var mov = (MovBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Entrada";

        if (!int.TryParse(SettlementBox.Text?.Trim(), out var days))
            throw new InvalidOperationException("Informe o prazo de recebimento em dias inteiros (ex.: 0 ou 30).");

        return new PaymentMethodInput
        {
            Id = _isNew ? null : _editingId,
            Name = NameBox.Text ?? "",
            ApiLabel = ApiBox.Text ?? "",
            MovementType = mov,
            FeePercent = ProductPriceHelper.ParseBr(FeeBox.Text),
            FeeFixed = ProductPriceHelper.ParseBr(FeeFixedBox.Text),
            SettlementDays = days,
            BankAccountId = dest.Kind == "banco" ? dest.Id : null,
            Active = ActiveBox.IsChecked == true,
            PdvKey = PdvKeyBox.Text ?? "",
            Notes = NotesBox.Text ?? "",
            FeeEditable = FeeBox.IsEnabled,
            DestinationKind = dest.Kind,
        };
    }

    private void MethodsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelect) return;
        if (MethodsGrid.SelectedItem is null && _isNew) return;
        _isNew = false;
        BindDetail();
    }

    private void New_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_isNew)
        {
            _isNew = false;
            Reload();
            return;
        }
        BindDetail();
        ShowStatus("Alterações descartadas.", ok: true);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = PaymentMethodsService.Save(BuildInput());
            _isNew = false;
            _editingId = saved.Id;
            Reload(saved.Id);
            ShowStatus("Forma de pagamento salva.", ok: true);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, ok: false);
            MessageBox.Show(ex.Message, "Formas de Pagamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var m = Selected;
        if (m is null || !m.CanDelete) return;

        var confirm = MessageBox.Show(
            $"Excluir a forma \"{m.Name}\"?\n\nFormas do sistema não podem ser excluídas — apenas inativadas.",
            "Excluir forma",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            PaymentMethodsService.Delete(m.Id);
            _editingId = null;
            Reload();
            ShowStatus("Forma excluída.", ok: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Formas de Pagamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ActiveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb)
            return;
        if (cb.DataContext is not PaymentMethodRow row)
            return;

        try
        {
            var want = cb.IsChecked == true;
            PaymentMethodsService.SetActive(row.Id, want);
            Reload(row.Id);
        }
        catch (Exception ex)
        {
            // Reverte visual
            cb.IsChecked = row.Active;
            MessageBox.Show(ex.Message, "Formas de Pagamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        PaymentMethodsService.MoveOrder(Selected.Id, -1);
        Reload(Selected.Id);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        PaymentMethodsService.MoveOrder(Selected.Id, +1);
        Reload(Selected.Id);
    }

    private void PdvKeyBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Length != 1 || !char.IsLetter(e.Text[0]);
    }

    private void ShowStatus(string msg, bool ok)
    {
        StatusMsg.Text = msg;
        StatusMsg.Foreground = new SolidColorBrush(
            ok ? Color.FromRgb(0x15, 0x80, 0x3D) : Color.FromRgb(0xB9, 0x1C, 0x1C));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            Save_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
