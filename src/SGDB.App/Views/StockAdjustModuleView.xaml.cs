using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class StockAdjustModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly StockAdjustMode _initialMode;
    private Product? _selected;
    private DispatcherTimer? _searchTimer;
    private bool _suppress;

    private static readonly SolidColorBrush GreenBorder = MakeBrush("#86EFAC");
    private static readonly SolidColorBrush GreenBg = MakeBrush("#F0FDF4");
    private static readonly SolidColorBrush RedBorder = MakeBrush("#FCA5A5");
    private static readonly SolidColorBrush RedBg = MakeBrush("#FEF2F2");
    private static readonly SolidColorBrush NeutralBorder = MakeBrush("#94A3B8");
    private static readonly SolidColorBrush NeutralBg = MakeBrush("#F8FAFC");
    private static readonly SolidColorBrush PreviewOk = MakeBrush("#166534");
    private static readonly SolidColorBrush PreviewWarn = MakeBrush("#B45309");
    private static readonly SolidColorBrush PreviewErr = MakeBrush("#DC2626");

    private static readonly string[] WarehouseReasons =
    [
        "— (livre)", "Inventário", "Avaria", "Perda / Roubo", "Brinde", "Devolução", "Correção",
    ];

    private static readonly string[] FridgeReasons =
    [
        "— selecione —", "Quebra", "Avaria", "Vencimento", "Consumo interno", "Erro de contagem", "Perda", "Outro",
    ];

    public StockAdjustModuleView(StockAdjustMode mode)
    {
        _initialMode = mode;
        InitializeComponent();

        TitleText.Text = mode == StockAdjustMode.Saldo ? "Ajusta Saldo de Estoque" : "Ajusta Estoque";

        Loaded += (_, _) =>
        {
            SelectInitialMode();
            ApplyLocationUi();
            ClearForm(keepSearch: false);
            FocusSearch();
        };
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        b.Freeze();
        return b;
    }

    private void FocusSearch()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
            SearchBox.SelectAll();
        });
    }

    private void SelectInitialMode()
    {
        var tag = _initialMode switch
        {
            StockAdjustMode.Saida => "saida",
            StockAdjustMode.Saldo => "saldo",
            _ => "entrada",
        };
        foreach (ComboBoxItem item in ModeBox.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                ModeBox.SelectedItem = item;
                break;
            }
        }
        UpdateModeVisual();
        UpdateQtyLabel();
        UpdatePreview();
    }

    private StockAdjustMode EffectiveMode
    {
        get
        {
            var tag = (ModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "entrada";
            return tag switch
            {
                "saida" => StockAdjustMode.Saida,
                "saldo" => StockAdjustMode.Saldo,
                _ => StockAdjustMode.Entrada,
            };
        }
    }

    private bool IsFridgeLocation =>
        string.Equals((LocationBox.SelectedItem as ComboBoxItem)?.Tag as string, "fridge",
            StringComparison.OrdinalIgnoreCase);

    private double OperationStock =>
        _selected is null ? 0 : (IsFridgeLocation ? _selected.StockFridge : _selected.Stock);

    private void LocationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyLocationUi();
        if (_selected is not null)
            BindSelectedProductFields();
        UpdateQtyLabel();
        UpdatePreview();
    }

    private void ApplyLocationUi()
    {
        var fridge = IsFridgeLocation;
        FridgeHintBorder.Visibility = fridge ? Visibility.Visible : Visibility.Collapsed;
        HelpText.Text = fridge
            ? "Use este ajuste apenas para diferença física da geladeira. O depósito não muda, o custo médio não é atualizado e lotes não são alterados."
            : "Localize o produto, escolha o tipo de ajuste e use Aplicar ou F9. Na entrada, informe o custo unitário para atualizar a média (Preço Custo).";
        NotesLabel.Text = fridge ? "Observação (obrigatória se Outro)" : "Observação (opcional)";
        FillReasons(fridge);
        UpdateUnitCostVisibility();
    }

    private void FillReasons(bool fridge)
    {
        var current = (ReasonBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        ReasonBox.Items.Clear();
        foreach (var reason in fridge ? FridgeReasons : WarehouseReasons)
            ReasonBox.Items.Add(new ComboBoxItem { Content = reason });
        ReasonBox.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(current))
        {
            foreach (ComboBoxItem item in ReasonBox.Items)
            {
                if (string.Equals(item.Content?.ToString(), current, StringComparison.OrdinalIgnoreCase))
                {
                    ReasonBox.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private void UpdateQtyLabel()
    {
        QtyLabel.Text = EffectiveMode == StockAdjustMode.Saldo ? "Novo saldo" : "Quantidade";
        if (_selected is not null && EffectiveMode == StockAdjustMode.Saldo)
            QtyBox.Text = OperationStock.ToString("N3");
        UpdateUnitCostVisibility();
    }

    private void UpdateUnitCostVisibility()
    {
        if (IsFridgeLocation)
        {
            UnitCostPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var show = EffectiveMode is StockAdjustMode.Entrada
            || (EffectiveMode == StockAdjustMode.Saldo
                && _selected is not null
                && TryParseQty(out var novo)
                && novo > _selected.Stock + 1e-9);
        UnitCostPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateModeVisual()
    {
        switch (EffectiveMode)
        {
            case StockAdjustMode.Entrada:
                ModeBorder.BorderBrush = GreenBorder;
                ModeBorder.Background = GreenBg;
                break;
            case StockAdjustMode.Saida:
                ModeBorder.BorderBrush = RedBorder;
                ModeBorder.Background = RedBg;
                break;
            default:
                ModeBorder.BorderBrush = NeutralBorder;
                ModeBorder.Background = NeutralBg;
                break;
        }
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateModeVisual();
        UpdateQtyLabel();
        UpdatePreview();
    }

    private void QtyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUnitCostVisibility();
        UpdatePreview();
    }

    private void UnitCostBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private bool TryParseQty(out double qty)
    {
        qty = 0;
        var text = QtyBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
            return false;
        try
        {
            qty = ProductPriceHelper.ParseBr(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryParseUnitCost(out double cost)
    {
        cost = 0;
        var text = UnitCostBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
            return false;
        try
        {
            cost = ProductPriceHelper.ParseBr(text);
            return cost >= 0;
        }
        catch
        {
            return false;
        }
    }

    private void UpdatePreview()
    {
        if (_selected is null)
        {
            PreviewText.Visibility = Visibility.Collapsed;
            DValorAjuste.Text = "—";
            return;
        }

        if (!TryParseQty(out var qty))
        {
            PreviewText.Visibility = Visibility.Collapsed;
            DValorAjuste.Text = "—";
            return;
        }

        var before = OperationStock;
        double after;
        string label;
        SolidColorBrush color;
        var unitForValue = TryParseUnitCost(out var typedCost) ? typedCost : _selected.CostPrice;
        var place = IsFridgeLocation ? "Geladeira" : "Estoque";

        switch (EffectiveMode)
        {
            case StockAdjustMode.Entrada:
                after = before + qty;
                if (IsFridgeLocation)
                {
                    label = $"{place} após ajuste: {after:N3} {_selected.Unit}  (+{qty:N3}) · depósito permanece {_selected.Stock:N3}";
                    DValorAjuste.Text = "— (geladeira não altera custo)";
                }
                else if (TryParseUnitCost(out var entradaCost))
                {
                    var avg = ProductPriceHelper.WeightedAverageCost(
                        PurchaseAverageCostRules.PhysicalStock(_selected.Stock, _selected.StockFridge),
                        _selected.CostPrice, qty, entradaCost);
                    label =
                        $"Estoque: {after:N3} {_selected.Unit} (+{qty:N3}) · Custo médio → R$ {avg:N2}";
                    DValorAjuste.Text = $"R$ {qty * unitForValue:N2} (entrada)";
                }
                else
                {
                    label =
                        $"Estoque após ajuste: {after:N3} {_selected.Unit}  (+{qty:N3}) — informe o custo unit. para média";
                    DValorAjuste.Text = $"R$ {qty * unitForValue:N2} (entrada)";
                }
                color = PreviewOk;
                break;
            case StockAdjustMode.Saida:
                after = before - qty;
                if (qty > before + 1e-9)
                {
                    label = IsFridgeLocation
                        ? $"Saída maior que a geladeira atual ({before:N3}). Não será permitido aplicar."
                        : $"Saída maior que o estoque atual ({before:N3}). Não será permitido aplicar.";
                    color = PreviewErr;
                }
                else
                {
                    label = IsFridgeLocation
                        ? $"{place} após ajuste: {after:N3} {_selected.Unit}  (−{qty:N3}) · depósito permanece {_selected.Stock:N3}"
                        : $"Estoque após ajuste: {after:N3} {_selected.Unit}  (−{qty:N3})";
                    color = after < 0 ? PreviewWarn : PreviewOk;
                }
                DValorAjuste.Text = IsFridgeLocation
                    ? "— (geladeira não altera custo)"
                    : $"R$ {qty * _selected.CostPrice:N2} (saída)";
                break;
            default:
                after = qty;
                var delta = after - before;
                if (!IsFridgeLocation && delta > 1e-9 && TryParseUnitCost(out var saldoCost))
                {
                    var avg = ProductPriceHelper.WeightedAverageCost(
                        PurchaseAverageCostRules.PhysicalStock(_selected.Stock, _selected.StockFridge),
                        _selected.CostPrice, delta, saldoCost);
                    label =
                        $"Estoque: {after:N3} {_selected.Unit} (Δ {delta:+0.###;-0.###;0}) · Custo médio → R$ {avg:N2}";
                }
                else if (IsFridgeLocation)
                {
                    label =
                        $"{place}: {after:N3} {_selected.Unit} (Δ {delta:+0.###;-0.###;0}) · depósito permanece {_selected.Stock:N3}";
                }
                else
                {
                    label = $"Estoque após ajuste: {after:N3} {_selected.Unit}  (Δ {delta:+0.###;-0.###;0})";
                }
                color = after < 0 ? PreviewWarn : PreviewOk;
                DValorAjuste.Text = IsFridgeLocation
                    ? "— (geladeira não altera custo)"
                    : $"R$ {Math.Abs(delta) * unitForValue:N2} (ajuste)";
                break;
        }

        PreviewText.Text = label;
        PreviewText.Foreground = color;
        PreviewText.Visibility = Visibility.Visible;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchTimer.Tick -= SearchTimer_Tick;
        _searchTimer.Tick += SearchTimer_Tick;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        ReloadSearch();
    }

    private void ReloadSearch()
    {
        var term = SearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(term))
        {
            ProductGrid.ItemsSource = null;
            StatusText.Text = "Digite referência, código de barras ou descrição";
            ClearSelection();
            return;
        }

        var rows = ProductService.List(search: term, ativo: "todos").Take(50).ToList();
        _suppress = true;
        ProductGrid.ItemsSource = rows;
        _suppress = false;

        if (rows.Count == 0)
        {
            StatusText.Text = "Nenhum produto encontrado";
            ClearSelection();
            return;
        }

        StatusText.Text = $"{rows.Count} produto(s) — clique para selecionar";
        if (rows.Count == 1)
            SelectProduct(rows[0]);
    }

    private void ProductGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (ProductGrid.SelectedItem is Product p)
            SelectProduct(p);
    }

    private void ProductGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProductGrid.SelectedItem is Product p)
        {
            SelectProduct(p);
            QtyBox.Focus();
            QtyBox.SelectAll();
        }
    }

    private void SelectProduct(Product p)
    {
        _selected = p;
        ProdutoPanel.Visibility = Visibility.Visible;
        BindSelectedProductFields();
        ApplyBtn.IsEnabled = true;

        if (!IsFridgeLocation && (string.IsNullOrWhiteSpace(UnitCostBox.Text) || UnitCostBox.Text == "0,00"))
            UnitCostBox.Text = ProductPriceHelper.FormatBr(p.CostPrice);

        if (EffectiveMode == StockAdjustMode.Saldo)
            QtyBox.Text = OperationStock.ToString("N3");

        MovGrid.ItemsSource = StockService.ListMovementsByProduct(p.Id);
        UpdateUnitCostVisibility();
        UpdatePreview();
    }

    private void BindSelectedProductFields()
    {
        if (_selected is null) return;
        var p = _selected;
        DCodigo.Text = string.IsNullOrWhiteSpace(p.Code) ? "—" : p.Code;
        DNome.Text = p.Name;
        DGrupo.Text = string.IsNullOrWhiteSpace(p.GroupName) ? "—" : p.GroupName;
        DUn.Text = p.Unit;
        DCusto.Text = $"R$ {p.CostPrice:N2}";

        if (IsFridgeLocation)
        {
            DEstoqueLabel.Text = "Estoque atual da Geladeira";
            DEstoque.Text = $"{p.StockFridge:N3} {p.Unit}";
            DEstoque.Foreground = p.StockFridge < 0 ? PreviewErr : PreviewOk;
            DMinLabel.Text = "Depósito (não altera)";
            DMin.Text = p.Stock.ToString("N3");
        }
        else
        {
            DEstoqueLabel.Text = "Est. atual";
            DEstoque.Text = $"{p.Stock:N3} {p.Unit}";
            DEstoque.Foreground = p.Stock < 0 ? PreviewErr : PreviewOk;
            DMinLabel.Text = "Est. mín";
            DMin.Text = p.MinStock.ToString("N0");
        }
    }

    private void ClearSelection()
    {
        _selected = null;
        ProdutoPanel.Visibility = Visibility.Collapsed;
        DCodigo.Text = DNome.Text = DGrupo.Text = DEstoque.Text = DMin.Text = DUn.Text = "—";
        DEstoqueLabel.Text = IsFridgeLocation ? "Estoque atual da Geladeira" : "Est. atual";
        DMinLabel.Text = IsFridgeLocation ? "Depósito (não altera)" : "Est. mín";
        DCusto.Text = DValorAjuste.Text = "—";
        PreviewText.Visibility = Visibility.Collapsed;
        ApplyBtn.IsEnabled = false;
        MovGrid.ItemsSource = null;
        UpdateUnitCostVisibility();
    }

    private void ClearForm(bool keepSearch)
    {
        if (!keepSearch)
            SearchBox.Text = "";
        ClearSelection();
        LocationBox.SelectedIndex = 0;
        QtyBox.Text = "";
        UnitCostBox.Text = "";
        NotesBox.Text = "";
        ReasonBox.SelectedIndex = 0;
        if (!keepSearch)
        {
            ProductGrid.ItemsSource = null;
            StatusText.Text = "Digite referência, código de barras ou descrição";
        }
        UpdatePreview();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClearForm(keepSearch: false);
        FocusSearch();
    }

    private string SelectedReason()
    {
        var reason = (ReasonBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "";
        if (reason.StartsWith("—", StringComparison.Ordinal))
            return "";
        return reason;
    }

    private bool IsFridgeOutroReason =>
        string.Equals(SelectedReason(), "Outro", StringComparison.OrdinalIgnoreCase);

    private string BuildNotes()
    {
        var reason = SelectedReason();
        var obs = NotesBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(reason))
            return obs;
        if (string.IsNullOrEmpty(obs))
            return reason;
        return $"{reason}. {obs}";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            MessageBox.Show("Selecione um produto na lista.", TitleText.Text,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (!TryParseQty(out var value))
                throw new InvalidOperationException("Informe a quantidade.");

            if (IsFridgeLocation)
            {
                if (EffectiveMode == StockAdjustMode.Saida && value > _selected.StockFridge + 1e-9)
                    throw new InvalidOperationException(
                        $"Saída ({value:N3}) maior que a geladeira atual ({_selected.StockFridge:N3}).");

                if (IsFridgeOutroReason && string.IsNullOrWhiteSpace(NotesBox.Text))
                    throw new InvalidOperationException(
                        "Informe a observação para o motivo Outro.");

                var fridgeNotes = BuildNotes();
                if (string.IsNullOrWhiteSpace(fridgeNotes))
                    throw new InvalidOperationException("Informe o motivo do ajuste da geladeira.");

                var fridgeResult = EffectiveMode == StockAdjustMode.Saldo
                    ? StockService.AdjustFridge(_selected.Id, StockAdjustMode.Saldo, newStock: value, notes: fridgeNotes)
                    : StockService.AdjustFridge(_selected.Id, EffectiveMode, quantity: value, notes: fridgeNotes);

                var fridgeRefreshed = ProductService.GetById(_selected.Id);
                if (fridgeRefreshed is not null)
                    SelectProduct(fridgeRefreshed);

                StatusText.Text = fridgeRefreshed is null
                    ? $"Aplicado (geladeira): {fridgeResult.StockBefore:N3} → {fridgeResult.StockAfter:N3}"
                    : $"Aplicado (geladeira): {fridgeResult.StockBefore:N3} → {fridgeResult.StockAfter:N3} · Depósito {fridgeRefreshed.Stock:N3}";
                NotesBox.Text = "";
                ReasonBox.SelectedIndex = 0;
                if (EffectiveMode != StockAdjustMode.Saldo)
                    QtyBox.Text = "";
                else if (fridgeRefreshed is not null)
                    QtyBox.Text = fridgeRefreshed.StockFridge.ToString("N3");

                ReloadSearch();
                QtyBox.Focus();
                return;
            }

            if (EffectiveMode == StockAdjustMode.Saida && value > _selected.Stock + 1e-9)
                throw new InvalidOperationException(
                    $"Saída ({value:N3}) maior que o estoque atual ({_selected.Stock:N3}).");

            var notes = BuildNotes();
            double? unitCost = null;
            var needsCost = EffectiveMode == StockAdjustMode.Entrada
                || (EffectiveMode == StockAdjustMode.Saldo && value > _selected.Stock + 1e-9);
            if (needsCost)
            {
                if (!TryParseUnitCost(out var costVal))
                    throw new InvalidOperationException(
                        "Informe o custo unitário desta entrada para calcular a média.");
                unitCost = costVal;
            }

            var result = EffectiveMode == StockAdjustMode.Saldo
                ? StockService.Adjust(_selected.Id, StockAdjustMode.Saldo, newStock: value, notes: notes, unitCost: unitCost)
                : StockService.Adjust(_selected.Id, EffectiveMode, quantity: value, notes: notes, unitCost: unitCost);

            var refreshed = ProductService.GetById(_selected.Id);
            if (refreshed is not null)
                SelectProduct(refreshed);

            StatusText.Text = refreshed is null
                ? $"Aplicado: {result.StockBefore:N3} → {result.StockAfter:N3}"
                : $"Aplicado: {result.StockBefore:N3} → {result.StockAfter:N3} · Custo R$ {refreshed.CostPrice:N2}";
            NotesBox.Text = "";
            ReasonBox.SelectedIndex = 0;
            if (EffectiveMode != StockAdjustMode.Saldo)
                QtyBox.Text = "";
            else if (refreshed is not null)
                QtyBox.Text = refreshed.Stock.ToString("N3");

            ReloadSearch();
            QtyBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, TitleText.Text, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6)
        {
            FocusSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.F9)
        {
            Apply_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
