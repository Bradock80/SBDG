using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Domain.Inventory;
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
    private bool _suppressPackLoose;
    private bool _cigarettePackMode;
    private int _activePackFactor;

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

    private void ResetCountDefault()
    {
        CountBox.Text = "1";
        SetPackLooseFields(0, 0);
        UpdatePhysicalTotalLabel();
    }

    private void Refresh(string? keepSearch = null)
    {
        if (_session is null)
        {
            SessionText.Text = "Nenhum inventário aberto. Escolha um grupo (opcional) e clique em Abrir inventário.";
            ItemsGrid.ItemsSource = null;
            SummaryText.Text = "";
            _allRows = [];
            _rows = [];
            ShowCommonCountUi();
            return;
        }

            SessionText.Text =
                $"Inventário #{_session.Id} · {_session.StatusDisplay} · Grupo: {_session.GroupDisplay} · aberto em {_session.CreatedAt} · somente depósito";
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
                $"Inventário #{_session.Id} aberto com {_allRows.Count} produto(s).\n" +
                "Contagem SOMENTE do DEPÓSITO — não incluir a geladeira.",
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

        double newQty;
        if (addMode)
        {
            // Bipe = +1 unidade física (nunca interpreta como maço).
            var delta = ProductPriceHelper.ParseBr(CountBox.Text);
            if (delta <= 0) delta = 1;
            newQty = (item.CountedQty ?? 0) + delta;
        }
        else if (_cigarettePackMode
                 && TryResolveCigarettePackMode(item, out var factor)
                 && IsPackLooseEligibleCounted(item.CountedQty))
        {
            // Só usa Maços/Avulsos quando a UI já está nesse modo (produto selecionado).
            // Busca+Enter sem seleção continua no caminho físico (CountBox), preservando o padrão 1 UN.
            if (!TryReadPackLoose(out var packs, out var loose, out var error))
            {
                MessageBox.Show(error, "Inventário", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var normalized = InventoryPhysicalQuantityCalculator.Normalize(packs, loose, factor);
                SetPackLooseFields(normalized.Packs, normalized.Loose);
                newQty = InventoryPhysicalQuantityCalculator.Calculate(normalized.Packs, normalized.Loose, factor);
                UpdatePhysicalTotalLabel();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Inventário", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            var delta = ProductPriceHelper.ParseBr(CountBox.Text);
            if (delta <= 0) delta = 1;
            newQty = delta;
        }

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
            {
                ItemsGrid.SelectedItem = shown;
                ApplySelectionUi(shown);
            }
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
            ? $"Há {div.Count} divergência(s) no depósito.\nConsolidar aplica o saldo contado no DEPÓSITO. Continuar?"
            : "Consolidar inventário e aplicar os saldos contados no DEPÓSITO?";
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
        catch (InventoryConcurrencyException ex)
        {
            MessageBox.Show(ex.Message, "Inventário — recontagem necessária",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
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
            ApplySelectionUi(row);
    }

    private void ApplySelectionUi(InventoryItem row)
    {
        if (TryResolveCigarettePackMode(row, out var factor)
            && IsPackLooseEligibleCounted(row.CountedQty))
        {
            ShowCigaretteCountUi(row, factor);
            return;
        }

        // Cigarro com counted decimal legado: modo físico normal (não trunca silenciosamente).
        var extra = row.IsCounted
            ? "Contagem com decimal — use Qtd no depósito (unidades físicas)."
            : null;
        ShowCommonCountUi(row, extra);
        CountBox.Text = row.IsCounted ? row.CountedDisplay : "1";
    }

    private void ShowCommonCountUi(InventoryItem? row = null, string? extra = null)
    {
        _cigarettePackMode = false;
        _activePackFactor = 0;
        CommonCountPanel.Visibility = Visibility.Visible;
        CigaretteCountPanel.Visibility = Visibility.Collapsed;

        var hint = row is null
            ? extra
            : string.IsNullOrWhiteSpace(extra) ? row.WarehouseHint : $"{row.WarehouseHint}\n{extra}";
        if (string.IsNullOrWhiteSpace(hint))
        {
            StockHintText.Visibility = Visibility.Collapsed;
            StockHintText.Text = "";
        }
        else
        {
            StockHintText.Text = hint;
            StockHintText.Visibility = Visibility.Visible;
        }
    }

    private void ShowCigaretteCountUi(InventoryItem row, int factor)
    {
        _cigarettePackMode = true;
        _activePackFactor = factor;
        CommonCountPanel.Visibility = Visibility.Collapsed;
        CigaretteCountPanel.Visibility = Visibility.Visible;

        var theoretical = row.TheoreticalQty;
        var theoText = FormatStockLine(theoretical, factor);
        var hint = $"Contagem do depósito: {theoText}";
        if (row.UsesFridge)
            hint += $"\nGeladeira atual: {row.StockFridge:G} UN. Não incluir na contagem.";
        StockHintText.Text = hint;
        StockHintText.Visibility = Visibility.Visible;

        if (row.CountedQty is double counted && InventoryPhysicalQuantityCalculator.IsWholeNumber(counted))
        {
            var split = InventoryPhysicalQuantityCalculator.SplitPhysicalQuantity(counted, factor);
            SetPackLooseFields(split.Packs, split.Loose);
        }
        else
        {
            SetPackLooseFields(0, 0);
        }

        UpdatePhysicalTotalLabel();
    }

    private static string FormatStockLine(double physical, int factor)
    {
        var qtyText = InventoryPhysicalQuantityCalculator.IsWholeNumber(physical)
            ? ((long)Math.Round(physical)).ToString(CultureInfo.CurrentCulture)
            : physical.ToString("0.####", CultureInfo.CurrentCulture);

        if (!InventoryPhysicalQuantityCalculator.IsWholeNumber(physical) || physical < 0)
            return $"{qtyText} UN";

        try
        {
            var split = InventoryPhysicalQuantityCalculator.SplitPhysicalQuantity(physical, factor);
            return $"{qtyText} UN · {split.Packs} maços + {split.Loose} avulsos";
        }
        catch
        {
            return $"{qtyText} UN";
        }
    }

    private static bool TryResolveCigarettePackMode(InventoryItem item, out int factor)
    {
        factor = 0;
        var product = ProductService.GetById(item.ProductId);
        if (product is null)
            return false;

        if (!ProductClassificationHelper.IsCigarette(product.Name, product.GroupName))
            return false;

        var fator = ProductExtra.Parse(product.ExtraJson).FatorEmbalagem;
        return InventoryPhysicalQuantityCalculator.TryResolveFactor(fator, out factor);
    }

    /// <summary>
    /// Maços/Avulsos só se não houver contagem OU contagem inteira.
    /// Contagem decimal legada → modo Qtd contada (sem truncar).
    /// </summary>
    private static bool IsPackLooseEligibleCounted(double? countedQty)
    {
        if (countedQty is null)
            return true;
        return InventoryPhysicalQuantityCalculator.IsWholeNumber(countedQty.Value);
    }

    private bool TryReadPackLoose(out long packs, out long loose, out string error)
    {
        packs = 0;
        loose = 0;
        error = "";

        if (!TryParseNonNegativeInt(PacksBox.Text, out packs))
        {
            error = "Maços deve ser um número inteiro ≥ 0 (sem decimais).";
            return false;
        }

        if (!TryParseNonNegativeInt(LooseBox.Text, out loose))
        {
            error = "Avulsos deve ser um número inteiro ≥ 0 (sem decimais).";
            return false;
        }

        return true;
    }

    private static bool TryParseNonNegativeInt(string? text, out long value)
    {
        value = 0;
        var raw = (text ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
        {
            value = 0;
            return true;
        }

        if (raw.Contains(',') || raw.Contains('.'))
            return false;

        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            return false;

        return value >= 0;
    }

    private void SetPackLooseFields(long packs, long loose)
    {
        _suppressPackLoose = true;
        PacksBox.Text = packs.ToString(CultureInfo.InvariantCulture);
        LooseBox.Text = loose.ToString(CultureInfo.InvariantCulture);
        _suppressPackLoose = false;
    }

    private void PackLoose_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPackLoose || !_cigarettePackMode)
            return;

        // Normalização visual quando avulsos ≥ fator (sem mensagem de erro).
        if (TryReadPackLoose(out var packs, out var loose, out _)
            && _activePackFactor >= 2
            && loose >= _activePackFactor)
        {
            try
            {
                var n = InventoryPhysicalQuantityCalculator.Normalize(packs, loose, _activePackFactor);
                if (n.Packs != packs || n.Loose != loose)
                    SetPackLooseFields(n.Packs, n.Loose);
            }
            catch
            {
                // overflow: deixa o rótulo de total mostrar o problema no Registrar
            }
        }

        UpdatePhysicalTotalLabel();
    }

    private void UpdatePhysicalTotalLabel()
    {
        if (!_cigarettePackMode || _activePackFactor < 2)
        {
            PhysicalTotalText.Text = "Depósito: —";
            return;
        }

        if (!TryReadPackLoose(out var packs, out var loose, out _))
        {
            PhysicalTotalText.Text = "Depósito: —";
            return;
        }

        try
        {
            var total = InventoryPhysicalQuantityCalculator.Calculate(packs, loose, _activePackFactor);
            PhysicalTotalText.Text = $"Depósito: {total:0.####} UN";
        }
        catch
        {
            PhysicalTotalText.Text = "Depósito: —";
        }
    }

    private void PackLoose_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        RegisterCount(addMode: false);
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
            FocusActiveCountField();
        }
    }

    private void FocusActiveCountField()
    {
        if (_cigarettePackMode)
        {
            PacksBox.Focus();
            PacksBox.SelectAll();
        }
        else
        {
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
