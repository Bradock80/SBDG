using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class InventoryModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    private InventorySession? _session;
    private List<InventoryItem> _allRows = [];
    private List<InventoryItem> _rows = [];
    private bool _onlyDivergences;
    private bool _suppressSearch;
    private bool _processingScan;

    public InventoryModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            GroupBox.ItemsSource = new[] { "" }.Concat(ProductCatalogService.ListGroups()).ToList();
            GroupBox.SelectedIndex = 0;
            ResetCountDefault();
            _session = InventoryService.GetOpenSession();
            Refresh();
            SearchBox.Focus();
        };
    }

    private void ResetCountDefault() => CountBox.Text = "1";

    private void Refresh(string? keepSearch = null)
    {
        if (_session is null)
        {
            SessionText.Text = "Nenhum inventário aberto. Escolha um grupo (opcional) e clique em Abrir inventário.";
            ItemsGrid.ItemsSource = null;
            SummaryText.Text = "";
            _allRows = [];
            _rows = [];
            return;
        }

        SessionText.Text =
            $"Inventário #{_session.Id} · {_session.StatusDisplay} · Grupo: {_session.GroupDisplay} · aberto em {_session.CreatedAt}";
        _allRows = InventoryService.ListItems(_session.Id).ToList();
        _rows = _allRows;
        if (_onlyDivergences)
            _rows = _rows.Where(r => r.HasDivergence).ToList();

        var filter = keepSearch ?? "";
        if (!string.IsNullOrWhiteSpace(filter) && !LooksLikeEan13(filter))
        {
            _rows = _rows.Where(r =>
                r.ProductName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || r.ProductCode.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || r.ProductBarcode.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        ItemsGrid.ItemsSource = _rows;
        var counted = _allRows.Count(r => r.IsCounted);
        var div = _allRows.Count(r => r.HasDivergence);
        SummaryText.Text = $"{_allRows.Count} produto(s) · {counted} contado(s) · {div} divergência(s)"
            + (_onlyDivergences ? " · filtro: só divergências" : "");
    }

    private static bool LooksLikeEan13(string text)
    {
        var digits = Regex.Replace(text ?? "", @"\D", "");
        return digits.Length == 13;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var group = GroupBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(group)) group = null;
            _session = InventoryService.CreateSession(group);
            _onlyDivergences = false;
            Refresh();
            ResetCountDefault();
            SearchBox.Focus();
            MessageBox.Show(
                $"Inventário #{_session.Id} aberto com {_allRows.Count} produto(s).\nEstoque teórico congelado — bipe ou informe as contagens.",
                "Inventário", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Inventário", MessageBoxButton.OK, MessageBoxImage.Warning);
            _session = InventoryService.GetOpenSession();
            Refresh();
        }
    }

    private void Divergences_Click(object sender, RoutedEventArgs e)
    {
        _onlyDivergences = !_onlyDivergences;
        Refresh();
    }

    private void Register_Click(object sender, RoutedEventArgs e) => RegisterCount(addMode: false);

    /// <param name="addMode">true = soma à contagem atual (bipe); false = define o valor do campo Qtd.</param>
    private void RegisterCount(bool addMode, InventoryItem? forcedItem = null)
    {
        if (_session is null)
        {
            MessageBox.Show("Abra um inventário primeiro.", "Inventário", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var item = forcedItem ?? ResolveSelectedItem();
        if (item is null)
        {
            MessageBox.Show("Produto não encontrado. Bipe o código ou selecione na grade.",
                "Inventário", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var delta = ProductPriceHelper.ParseBr(CountBox.Text);
        if (delta <= 0) delta = 1;

        var newQty = addMode
            ? (item.CountedQty ?? 0) + delta
            : delta;

        try
        {
            InventoryService.SetCounted(item.Id, newQty);
            _suppressSearch = true;
            SearchBox.Clear();
            _suppressSearch = false;
            ResetCountDefault();
            Refresh();
            var shown = _rows.FirstOrDefault(r => r.Id == item.Id);
            if (shown is not null)
                ItemsGrid.SelectedItem = shown;
            SearchBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Inventário", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private InventoryItem? ResolveSelectedItem()
    {
        if (ItemsGrid.SelectedItem is InventoryItem selected)
            return selected;

        var q = SearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(q))
            return null;

        return FindItemByScan(q);
    }

    private InventoryItem? FindItemByScan(string raw)
    {
        var digits = Regex.Replace(raw, @"\D", "");
        var list = _allRows.Count > 0 ? _allRows : (_session is null ? [] : InventoryService.ListItems(_session.Id).ToList());

        if (!string.IsNullOrEmpty(digits))
        {
            var byBar = list.FirstOrDefault(r =>
                string.Equals(Regex.Replace(r.ProductBarcode ?? "", @"\D", ""), digits, StringComparison.Ordinal));
            if (byBar is not null) return byBar;

            // zeros à esquerda
            var stripped = digits.TrimStart('0');
            if (!string.IsNullOrEmpty(stripped))
            {
                byBar = list.FirstOrDefault(r =>
                {
                    var b = Regex.Replace(r.ProductBarcode ?? "", @"\D", "").TrimStart('0');
                    return b == stripped;
                });
                if (byBar is not null) return byBar;
            }
        }

        return list.FirstOrDefault(r =>
            string.Equals(r.ProductCode, raw, StringComparison.OrdinalIgnoreCase)
            || r.ProductName.Contains(raw, StringComparison.OrdinalIgnoreCase));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearch || _processingScan || _session is null)
            return;

        var text = SearchBox.Text?.Trim() ?? "";
        if (!LooksLikeEan13(text))
            return;

        _processingScan = true;
        try
        {
            CountBox.Text = "1";
            var item = FindItemByScan(text);
            if (item is null)
            {
                MessageBox.Show($"Código {text} não está neste inventário.", "Inventário",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                _suppressSearch = true;
                SearchBox.Clear();
                _suppressSearch = false;
                return;
            }

            // Bipe EAN-13: +1 automático, limpa busca para o próximo
            RegisterCount(addMode: true, forcedItem: item);
        }
        finally
        {
            _processingScan = false;
        }
    }

    private void Consolidate_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var div = InventoryService.ListDivergences(_session.Id);
        var msg = div.Count > 0
            ? $"Há {div.Count} divergência(s).\nConsolidar aplica o saldo contado no estoque. Continuar?"
            : "Consolidar inventário e aplicar saldos contados?";
        if (MessageBox.Show(msg, "Inventário", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            var result = InventoryService.Consolidate(_session.Id);
            MessageBox.Show(
                $"{result.AdjustedCount} produto(s) ajustado(s).\nEntradas: {result.TotalPositiveQty:N3}\nSaídas: {result.TotalNegativeQty:N3}",
                "Inventário", MessageBoxButton.OK, MessageBoxImage.Information);
            _session = null;
            Refresh();
            ResetCountDefault();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Inventário", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (MessageBox.Show("Cancelar inventário aberto? Contagens serão descartadas.",
                "Inventário", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            InventoryService.Cancel(_session.Id);
            _session = null;
            Refresh();
            ResetCountDefault();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Inventário", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_processingScan) return;
        if (ItemsGrid.SelectedItem is InventoryItem row)
        {
            // Mantém qtd padrão 1 para bipagem; se já contado, mostra o valor atual para edição manual
            CountBox.Text = row.IsCounted ? row.CountedDisplay : "1";
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        var text = SearchBox.Text?.Trim() ?? "";
        if (LooksLikeEan13(text) || !string.IsNullOrWhiteSpace(text))
        {
            var item = FindItemByScan(text);
            if (item is not null)
            {
                RegisterCount(addMode: LooksLikeEan13(text), forcedItem: item);
                return;
            }
        }

        Refresh(keepSearch: text);
        if (_rows.Count == 1)
        {
            ItemsGrid.SelectedItem = _rows[0];
            CountBox.Focus();
            CountBox.SelectAll();
        }
    }

    private void CountBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        RegisterCount(addMode: false);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.F9) { Consolidate_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
