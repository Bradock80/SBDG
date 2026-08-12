using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class ProductsModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private string _ativoFilter = "ativos";

    public ProductsModuleView()
    {
        InitializeComponent();
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            LoadProducts();
        };
        Loaded += (_, _) =>
        {
            Focus();
            ApplyEditPermissionUi();
            LoadGroupFilter();
            LoadProducts();
        };
    }

    private void ApplyEditPermissionUi()
    {
        var canEdit = AccessControl.Can("ProdutosEditar");
        BtnNovo.IsEnabled = canEdit;
        BtnAlterar.IsEnabled = canEdit;
        BtnDuplicar.IsEnabled = canEdit;
        BtnUnificar.IsEnabled = canEdit;
        BtnExcluir.IsEnabled = canEdit;
        BtnNovo.Opacity = canEdit ? 1 : 0.4;
        BtnAlterar.Opacity = canEdit ? 1 : 0.4;
        BtnDuplicar.Opacity = canEdit ? 1 : 0.4;
        BtnUnificar.Opacity = canEdit ? 1 : 0.4;
        BtnExcluir.Opacity = canEdit ? 1 : 0.4;
        if (!canEdit)
        {
            BtnNovo.ToolTip = "Sem permissão para cadastrar";
            BtnAlterar.ToolTip = "Sem permissão para alterar";
            BtnDuplicar.ToolTip = "Sem permissão para duplicar";
            BtnUnificar.ToolTip = "Sem permissão para unificar";
            BtnExcluir.ToolTip = "Sem permissão para excluir";
        }
    }

    private void LoadGroupFilter()
    {
        var selected = GroupFilterBox.SelectedItem as string;
        GroupFilterBox.Items.Clear();
        GroupFilterBox.Items.Add("Todos");
        foreach (var g in ProductCatalogService.ListGroups())
            GroupFilterBox.Items.Add(g);
        GroupFilterBox.SelectedItem = selected is not null && GroupFilterBox.Items.Contains(selected)
            ? selected
            : "Todos";
    }

    private void LoadProducts()
    {
        var group = GroupFilterBox.SelectedItem as string;
        if (group == "Todos")
            group = null;

        var dateMode = "none";
        var fromText = DateFromBox.Text?.Trim();
        var toText = DateToBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(fromText) || !string.IsNullOrWhiteSpace(toText))
        {
            dateMode = (DateModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "created";
        }

        var items = ProductService.List(
            SearchBox.Text, _ativoFilter, group,
            fromText, toText, dateMode);
        ProductsGrid.ItemsSource = items;
    }

    private Product? SelectedProduct =>
        ProductsGrid.SelectedItem as Product;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _ativoFilter = FilterInativos.IsChecked == true ? "inativos"
            : FilterTodos.IsChecked == true ? "todos"
            : "ativos";
        LoadProducts();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadGroupFilter();
        LoadProducts();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        GroupFilterBox.SelectedItem = "Todos";
        FilterAtivos.IsChecked = true;
        DateFromBox.Text = "";
        DateToBox.Text = "";
        DateModeBox.SelectedIndex = 0;
        LoadProducts();
    }

    private void NewProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("ProdutosEditar", "cadastrar e alterar produtos", Window.GetWindow(this)))
            return;
        OpenForm(null);
    }

    private void EditProduct_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProduct is null)
        {
            MessageBox.Show("Selecione um produto na lista.", "Produtos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!AccessControl.Ensure("ProdutosEditar", "cadastrar e alterar produtos", Window.GetWindow(this)))
            return;
        OpenForm(SelectedProduct.Id);
    }

    private void ProductsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        EditProduct_Click(sender, e);

    private void ProductsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void DuplicateProduct_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProduct is null)
        {
            MessageBox.Show("Selecione um produto para duplicar.", "Produtos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!AccessControl.Ensure("ProdutosEditar", "cadastrar e alterar produtos", Window.GetWindow(this)))
            return;

        try
        {
            ProductService.Duplicate(SelectedProduct.Id);
            LoadGroupFilter();
            LoadProducts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Produtos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MergeProduct_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProduct is null)
        {
            MessageBox.Show("Selecione o produto principal (o que será mantido).", "Unificar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!AccessControl.Ensure("ProdutosEditar", "cadastrar e alterar produtos", Window.GetWindow(this)))
            return;

        var win = new ProductMergeWindow(SelectedProduct) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            LoadGroupFilter();
            LoadProducts();
            MessageBox.Show(
                $"Produtos unificados.\nEstoque: {win.MergedProduct?.StockDisplay}\nBarras: {win.MergedProduct?.BarcodeDisplay}",
                "Unificar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void DeleteProduct_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProduct is null)
        {
            MessageBox.Show("Selecione um produto para excluir.", "Produtos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!AccessControl.Ensure("ProdutosEditar", "cadastrar e alterar produtos", Window.GetWindow(this)))
            return;

        var confirm = MessageBox.Show(
            $"Inativar o produto \"{SelectedProduct.Name}\"?",
            "Excluir produto",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            ProductService.SoftDelete(SelectedProduct.Id);
            LoadProducts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Produtos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var group = GroupFilterBox.SelectedItem as string;
        if (group == "Todos")
            group = null;

        var dateMode = "none";
        var fromText = DateFromBox.Text?.Trim();
        var toText = DateToBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(fromText) || !string.IsNullOrWhiteSpace(toText))
            dateMode = (DateModeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "created";

        var items = ProductService.List(
            SearchBox.Text, _ativoFilter, group, fromText, toText, dateMode);

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "produtos.csv",
        };
        if (dialog.ShowDialog() != true)
            return;

        using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
        writer.WriteLine("Código;Referência;Cód. Barras;Descrição;Preço;Estoque;Última entrada;Unidade;Grupo");
        foreach (var p in items)
        {
            writer.WriteLine(string.Join(';', new[]
            {
                p.Id.ToString(),
                p.Code ?? "",
                p.Barcode ?? "",
                p.Name,
                p.SalePrice.ToString("F2"),
                p.Stock.ToString("G"),
                p.LastEntryDisplay,
                p.Unit,
                p.GroupName ?? "",
            }));
        }
    }

    private void OpenForm(int? productId)
    {
        var form = new ProductFormWindow(productId) { Owner = Window.GetWindow(this) };
        if (form.ShowDialog() == true)
        {
            LoadGroupFilter();
            LoadProducts();
        }
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ProductsModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2:
                NewProduct_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F3:
                EditProduct_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F4:
                MergeProduct_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F5:
                Refresh_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F6:
                SearchBox.Focus();
                e.Handled = true;
                break;
            case Key.F7:
                DeleteProduct_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F8:
                DuplicateProduct_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F9:
                Export_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Enter when ProductsGrid.SelectedItem is not null:
                EditProduct_Click(sender, e);
                e.Handled = true;
                break;
        }
    }
}
