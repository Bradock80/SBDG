using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class NfeLotValidityWindow : Window
{
    private readonly ObservableCollection<LotEditRow> _rows;
    private bool _skipModal;

    /// <summary>True quando nenhum item exige validade — o chamador pode seguir sem diálogo.</summary>
    public bool SkippedBecauseNotRequired => _skipModal;

    public NfeLotValidityWindow(IEnumerable<NfeImportItem> items)
    {
        InitializeComponent();
        var list = items.ToList();
        _rows = new ObservableCollection<LotEditRow>(
            list.Select(i => new LotEditRow(i, ResolveRequiresExpiry(i))));
        Grid.ItemsSource = _rows;

        var required = _rows.Count(r => r.RequiresExpiry);
        _skipModal = required == 0;
        BulkMetaText.Text = required == 0
            ? "Nenhum item com controle de validade"
            : $"{required} de {_rows.Count} com validade obrigatória";
        HintText.Text = required == 0
            ? "Nenhum produto desta nota exige controle de validade no cadastro. Pode confirmar ou cancelar."
            : "Digite a validade (dd/MM/aaaa). Tab / Enter avança para o próximo item obrigatório. Lote é opcional.";
    }

    /// <summary>
    /// Abre o modal só se houver itens com controle de validade.
    /// Retorna false se o usuário cancelou; true se confirmou ou se não era necessário.
    /// </summary>
    public static bool ConfirmOrSkip(Window? owner, IList<NfeImportItem> items)
    {
        if (items.Count == 0)
            return true;

        var anyRequired = items.Any(ResolveRequiresExpiry);
        if (!anyRequired)
        {
            // Sem controle: limpa exigência e segue (lote/validade opcionais já no item).
            return true;
        }

        var dlg = new NfeLotValidityWindow(items) { Owner = owner };
        if (dlg._skipModal)
        {
            // Aplica o que já veio do XML sem mostrar UI.
            foreach (var row in dlg._rows)
                row.TryApply(out _);
            return true;
        }

        return dlg.ShowDialog() == true;
    }

    public static bool ResolveRequiresExpiry(NfeImportItem item)
    {
        if (item.MatchedProductId is int pid)
        {
            var product = ProductService.GetById(pid);
            if (product is not null)
            {
                var extra = ProductExtra.Parse(product.ExtraJson);
                if (extra.ControleValidade is bool explicitFlag)
                    return explicitFlag;
                return ProductClassificationHelper.SuggestsExpiryControl(product.Name, product.GroupName);
            }
        }

        return ProductClassificationHelper.SuggestsExpiryControl(item.Name);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_skipModal)
        {
            DialogResult = true;
            Close();
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusFirstExpiry);
    }

    private void FocusFirstExpiry()
    {
        var idx = IndexOfNextRequired(-1);
        if (idx < 0)
            idx = 0;
        BeginExpiryEdit(idx);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            Ok_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BulkExpiryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyBulk_Click(sender, e);
            e.Handled = true;
        }
    }

    private void ApplyBulk_Click(object sender, RoutedEventArgs e)
    {
        var raw = (BulkExpiryBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            MessageBox.Show("Informe a data de validade para aplicar.", "Validade e Lote",
                MessageBoxButton.OK, MessageBoxImage.Information);
            BulkExpiryBox.Focus();
            return;
        }

        if (!TryParseExpiry(raw, out var dt, out var error))
        {
            MessageBox.Show(error, "Validade e Lote", MessageBoxButton.OK, MessageBoxImage.Warning);
            BulkExpiryBox.Focus();
            BulkExpiryBox.SelectAll();
            return;
        }

        var formatted = dt.ToString("dd/MM/yyyy");
        var n = 0;
        foreach (var row in _rows)
        {
            row.ExpiryEdit = formatted;
            n++;
        }

        BulkMetaText.Text = $"Aplicado em {n} item(ns)";
        FocusFirstExpiry();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Commit edição atual
        Grid.CommitEdit(DataGridEditingUnit.Cell, true);
        Grid.CommitEdit(DataGridEditingUnit.Row, true);

        foreach (var row in _rows)
        {
            if (!row.TryApply(out var error))
            {
                MessageBox.Show(error, "Validade e Lote", MessageBoxButton.OK, MessageBoxImage.Warning);
                var idx = _rows.IndexOf(row);
                if (idx >= 0) BeginExpiryEdit(idx);
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Column == ExpiryCol && e.Row.Item is LotEditRow row && !row.CanEditExpiry)
            e.Cancel = true;
    }

    private void Grid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column != ExpiryCol) return;
        if (e.EditingElement is TextBox tb)
        {
            tb.SelectAll();
            tb.Focus();
        }
    }

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // binding UpdateSourceTrigger=PropertyChanged already updates
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Tab or Key.Enter))
            return;

        // Só intercepta na coluna de validade (edição rápida em lote)
        if (Grid.CurrentColumn != ExpiryCol && Grid.CurrentCell.Column != ExpiryCol)
            return;

        e.Handled = true;
        Grid.CommitEdit(DataGridEditingUnit.Cell, true);

        var current = Grid.Items.IndexOf(Grid.CurrentItem);
        if (current < 0 && Grid.CurrentCell.Item is LotEditRow curRow)
            current = _rows.IndexOf(curRow);

        var next = e.KeyboardDevice.Modifiers == ModifierKeys.Shift
            ? IndexOfPrevRequired(current)
            : IndexOfNextRequired(current);

        if (next < 0)
        {
            if (e.Key == Key.Enter)
                Ok_Click(sender, e);
            else
                BulkExpiryBox.Focus();
            return;
        }

        BeginExpiryEdit(next);
    }

    private int IndexOfNextRequired(int afterIndex)
    {
        for (var i = afterIndex + 1; i < _rows.Count; i++)
        {
            if (_rows[i].RequiresExpiry)
                return i;
        }
        return -1;
    }

    private int IndexOfPrevRequired(int beforeIndex)
    {
        for (var i = beforeIndex - 1; i >= 0; i--)
        {
            if (_rows[i].RequiresExpiry)
                return i;
        }
        return -1;
    }

    private void BeginExpiryEdit(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count) return;
        Grid.ScrollIntoView(_rows[rowIndex]);
        Grid.CurrentCell = new DataGridCellInfo(_rows[rowIndex], ExpiryCol);
        Grid.SelectedCells.Clear();
        Grid.SelectedCells.Add(Grid.CurrentCell);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Grid.BeginEdit();
        });
    }

    internal static bool TryParseExpiry(string raw, out DateTime dt, out string error)
    {
        error = "";
        dt = default;
        if (!DateTime.TryParseExact(raw, ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd"],
                CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out dt)
            && !DateTime.TryParse(raw, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out dt))
        {
            error = "Validade inválida. Use dd/MM/aaaa.";
            return false;
        }

        if (dt.Date < DateTime.Today.AddYears(-2))
        {
            error = "Validade muito antiga.";
            return false;
        }

        return true;
    }

    private sealed class LotEditRow : INotifyPropertyChanged
    {
        private readonly NfeImportItem _item;
        private string _lot;
        private string _expiryEdit;

        public LotEditRow(NfeImportItem item, bool requiresExpiry)
        {
            _item = item;
            RequiresExpiry = requiresExpiry;
            _lot = item.LotNumber ?? "";
            _expiryEdit = item.ExpiryDate is DateTime d
                ? d.ToString("dd/MM/yyyy")
                : "";
        }

        public bool RequiresExpiry { get; }
        public bool CanEditExpiry => true; // todos editáveis; só obrigatório muda
        public string Name => _item.Name;
        public string QtyDisplay => _item.QtyDisplay;
        public string ControlBadge => RequiresExpiry
            ? (string.IsNullOrWhiteSpace(_expiryEdit) ? "Obrigatório" : "OK")
            : "Opcional";

        public string LotNumber
        {
            get => _lot;
            set
            {
                _lot = value ?? "";
                OnPropertyChanged();
            }
        }

        public string ExpiryEdit
        {
            get => _expiryEdit;
            set
            {
                _expiryEdit = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(ControlBadge));
            }
        }

        public bool TryApply(out string error)
        {
            error = "";
            var raw = (_expiryEdit ?? "").Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (!RequiresExpiry)
                {
                    _item.LotNumber = (_lot ?? "").Trim();
                    // mantém validade existente do XML se houver; senão limpa
                    if (_item.ExpiryDate is null)
                        _item.ExpiryDateIso = null;
                    return true;
                }

                error = $"Informe a validade de \"{_item.Name}\".";
                return false;
            }

            if (!TryParseExpiry(raw, out var dt, out error))
            {
                error = $"{error} ({_item.Name})";
                return false;
            }

            _item.LotNumber = (_lot ?? "").Trim();
            _item.ExpiryDate = dt.Date;
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
