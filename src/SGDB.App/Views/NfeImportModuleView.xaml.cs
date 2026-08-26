using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SGDB.Domain.Purchases;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class NfeImportModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    private NfeImportPreview? _preview;
    private ObservableCollection<NfeImportItem> _items = [];

    private string? _lastXmlPath;

    public NfeImportModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SetImportEnabled(false);
            Focus();
        };
    }

    private void SetImportEnabled(bool enabled) => ApplyBtn.IsEnabled = enabled;

    private void ClearPreview()
    {
        _preview = null;
        _items = [];
        ItemsGrid.ItemsSource = null;
        FileText.Text = "Nenhum arquivo selecionado";
        HeaderInfo.Text = "";
        SummaryText.Text = "Selecione um XML da NF-e para começar.";
        if (LineDetailText is not null)
            LineDetailText.Text = "";
        SetImportEnabled(false);
    }

    private void RefreshSummary()
    {
        if (_preview is null)
            return;
        var recon = _preview.Reconciliation;
        var charges = _preview.HeaderIpi + _preview.HeaderSt + _preview.HeaderFrete + _preview.HeaderOutro;
        var reconLine = recon is null
            ? ""
            : $" · efetivo R$ {recon.CalculatedEffectiveCost:N2} · {recon.ExpectedSource} R$ {recon.ExpectedPayable:N2} · dif. R$ {recon.Difference:N2} · {recon.FooterStatus}";
        SummaryText.Text =
            $"{_items.Count} itens · {_preview.MatchedProductsCount} Ok · {_preview.NewProductsCount} Novo(s) · " +
            $"bruto R$ {_preview.HeaderVProd:N2} · desc. R$ {_preview.HeaderDesc:N2} · " +
            $"encargos R$ {charges:N2} · total grade R$ {_preview.TotalValue:N2}{reconLine}";
        HeaderInfo.Text =
            $"NF {_preview.Numero}/{_preview.Serie} · {_preview.EmissionDisplay} · " +
            $"{_preview.EmitenteNome} · CNPJ {_preview.EmitenteCnpj} · vNF R$ {_preview.HeaderVNf:N2}";
        if (ReconText is not null && recon is not null)
        {
            ReconText.Text = recon.FooterStatus + " — " + recon.Explanation;
            ReconText.Foreground = recon.IsReconciled
                ? System.Windows.Media.Brushes.DarkGreen
                : System.Windows.Media.Brushes.DarkOrange;
        }
    }

    private void PickXml_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecionar XML da NF-e",
            Filter = "XML NF-e (*.xml)|*.xml|Todos (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _lastXmlPath = dlg.FileName;
            _preview = NfeXmlImportService.ParseFile(
                dlg.FileName,
                includeIcmsStInCost: NfeEffectiveCostImportPolicy.IncludeIcmsStFromAdvancedOverride(
                    IncluirStCustoBox.IsChecked));
            FileText.Text = System.IO.Path.GetFileName(dlg.FileName);
            _items = new ObservableCollection<NfeImportItem>(_preview.Items);
            _preview.Items = _items.ToList();
            ItemsGrid.ItemsSource = _items;
            ApplySuggestedSales();
            RefreshSummary();
            SetImportEnabled(_items.Count > 0);
        }
        catch (Exception ex)
        {
            ClearPreview();
            MessageBox.Show(ex.Message, "Importar XML", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void IncluirStCustoBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
            return;

        var withSt = NfeEffectiveCostImportPolicy.IncludeIcmsStFromAdvancedOverride(IncluirStCustoBox.IsChecked);
        var margin = ProductPriceHelper.ParseBr(MarginBox.Text);
        if (margin <= 0) margin = 30;

        foreach (var item in _items)
        {
            if (item.IsManualCost)
                continue;
            item.ApplyStCostOverride(withSt);
            var group = ProductClassificationHelper.Infer(item.Name).Group;
            item.SalePrice = ProductPriceHelper.ResolveCatalogSale(
                0, item.UnitPrice, item.PackFactor, item.Name, group, margin);
        }

        if (_preview is not null)
        {
            _preview.Items = _items.ToList();
            var payable = _items.Where(i => i.IncludeInPayable).Select(i => i.EffectiveLineCost).ToList();
            _preview.Reconciliation = NfeCostReconciliation.Reconcile(
                payable, _preview.FatLiq, _preview.DupSum, _preview.PagSum, _preview.HeaderVNf);
        }
        RefreshSummary();
    }

    private void ApplySuggestedSales()
    {
        var margin = ProductPriceHelper.ParseBr(MarginBox.Text);
        foreach (var item in _items)
        {
            var catalogCost = item.ResolveCatalogCost();
            if (margin > 0 && item.UnitPrice > 0)
            {
                item.SalePrice = item.ResolveCatalogSale(margin);
                // Se o cadastro antigo tinha venda menor que o custo do maço, força margem da tela.
                if (catalogCost > 0.009 && item.SalePrice + 0.009 < catalogCost)
                    item.SalePrice = ProductPriceHelper.SaleFromCostAndMargin(catalogCost, margin);
            }
            else if (item.MatchedProductId is int id)
            {
                var existing = ProductService.GetById(id);
                if (existing is not null && existing.SalePrice > 0)
                {
                    item.SalePrice = existing.SalePrice;
                    if (catalogCost > 0.009 && item.SalePrice + 0.009 < catalogCost && margin > 0)
                        item.SalePrice = ProductPriceHelper.SaleFromCostAndMargin(catalogCost, margin);
                }
            }
        }
    }

    private void RecalcMargin_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
        {
            MessageBox.Show("Selecione um XML primeiro.", "Importar XML",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var margin = ProductPriceHelper.ParseBr(MarginBox.Text);
        if (margin <= 0)
        {
            MessageBox.Show("Informe a margem % (ex.: 30).", "Importar XML",
                MessageBoxButton.OK, MessageBoxImage.Information);
            MarginBox.Focus();
            return;
        }

        foreach (var item in _items)
            item.SalePrice = item.ResolveCatalogSale(margin);

        RefreshSummary();
        MessageBox.Show(
            $"Vendas recalculadas com margem {margin:N1}%.\nVocê ainda pode editar a coluna Venda antes de aplicar.",
            "Importar XML", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ItemsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;

        // Garante binding aplicado e total atualizado
        if (e.EditingElement is TextBox box && e.Row.Item is NfeImportItem item)
        {
            var header = e.Column.Header?.ToString() ?? "";
            var raw = box.Text;
            if (header is "Qtd")
                item.Quantity = ProductPriceHelper.ParseBr(raw);
            else if (header is "Custo" or "Custo un." or "Custo fís.")
                item.UnitPrice = ProductPriceHelper.ParseBr(raw);
            else if (header is "Venda cad.")
                item.SalePrice = ProductPriceHelper.ParseBr(raw);
        }

        Dispatcher.BeginInvoke(RefreshSummary);
    }

    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineDetailText is null)
            return;
        if (ItemsGrid.CurrentItem is NfeImportItem item)
            LineDetailText.Text = item.CostDetailLine;
        else if (ItemsGrid.SelectedItem is NfeImportItem selected)
            LineDetailText.Text = selected.CostDetailLine;
    }

    private void SyncPreviewItems()
    {
        if (_preview is null) return;
        _preview.Items = _items.ToList();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyBtn.IsEnabled || _preview is null)
            return;

        SyncPreviewItems();

        if (_preview.Items.Any(i => i.Quantity <= 0))
        {
            MessageBox.Show("Há itens com quantidade inválida. Corrija antes de aplicar.",
                "Importar XML", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var updateStock = UpdateStockBox.IsChecked == true;
        if (updateStock)
        {
            if (!NfeLotValidityWindow.ConfirmOrSkip(Window.GetWindow(this), _preview.Items))
                return;

            var missing = _preview.Items
                .Where(i => NfeLotValidityWindow.ResolveRequiresExpiry(i) && i.NeedsManualExpiry)
                .ToList();
            if (missing.Count > 0)
            {
                MessageBox.Show(
                    "Falta Data de Validade nos produtos com controle de validade ativado.",
                    "Importar XML", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var reconWarn = _preview.Reconciliation is { IsReconciled: false } recon
            ? $"\n\n{recon.FooterStatus}: {recon.Explanation}"
            : "";
        if (MessageBox.Show(
                "Confirmar importação da NF-e?\nQtd, custo, venda, lote e validade serão aplicados." + reconWarn,
                "Importar XML", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            double? margin = null;
            var m = ProductPriceHelper.ParseBr(MarginBox.Text);
            if (m > 0) margin = m;

            var result = NfeXmlImportService.Apply(
                _preview,
                createMissingProducts: CreateMissingBox.IsChecked == true,
                updateStock: updateStock,
                updateCost: UpdatePriceBox.IsChecked == true,
                marginPercent: margin);

            var supplierInfo = result.SupplierCreated
                ? $"Fornecedor cadastrado: {result.SupplierName}"
                : $"Fornecedor: {result.SupplierName}";
            MessageBox.Show(
                $"Importação concluída.\n\n{supplierInfo}\n" +
                $"Compra #{result.PurchaseId} (em Compras)\n" +
                $"Contas a Pagar: parcela em aberto (pendente — ainda não paga)\n" +
                $"Produtos novos: {result.ProductsCreated}\n\n" +
                "O pagamento você lança depois em Contas a Pagar.",
                "Importar XML", MessageBoxButton.OK, MessageBoxImage.Information);
            ClearPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Importar XML", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            if (ApplyBtn.IsEnabled)
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
