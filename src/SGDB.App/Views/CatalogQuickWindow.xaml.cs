using System.Windows;
using System.Windows.Input;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public enum CatalogKind { Brand, Group, Unit }

public partial class CatalogQuickWindow : Window
{
    private readonly CatalogKind _kind;

    public string? SelectedName { get; private set; }

    public CatalogQuickWindow(CatalogKind kind, string? currentValue = null)
    {
        _kind = kind;
        InitializeComponent();
        Title = TitleText.Text = kind switch
        {
            CatalogKind.Brand => "Cadastro de Marca",
            CatalogKind.Group => "Cadastro de Grupo",
            CatalogKind.Unit => "Cadastro de Unidade",
            _ => "Cadastro",
        };
        NameLabel.Text = kind switch
        {
            CatalogKind.Brand => "Marca",
            CatalogKind.Group => "Grupo",
            CatalogKind.Unit => "Unidade",
            _ => "Nome",
        };
        ReloadList();
        if (!string.IsNullOrWhiteSpace(currentValue))
            NameBox.Text = currentValue;
    }

    private void ReloadList()
    {
        var items = _kind switch
        {
            CatalogKind.Brand => ProductCatalogService.ListBrands(),
            CatalogKind.Group => ProductCatalogService.ListGroups(),
            CatalogKind.Unit => ProductCatalogService.ListUnits(),
            _ => Array.Empty<string>(),
        };
        ItemsList.ItemsSource = items;
    }

    private void ItemsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is string name)
            NameBox.Text = name;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        var name = TextNorm.UpperStr(NameBox.Text);
        if (name is null)
        {
            ErrorText.Text = "Informe o nome.";
            return;
        }

        try
        {
            switch (_kind)
            {
                case CatalogKind.Brand: ProductCatalogService.EnsureBrand(name); break;
                case CatalogKind.Group: ProductCatalogService.EnsureGroup(name); break;
                case CatalogKind.Unit: ProductCatalogService.EnsureUnit(name); break;
            }
            SelectedName = name;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(NameBox.Text))
            SelectedName = NameBox.Text.Trim().ToUpperInvariant();
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Save_Click(sender, e);
            e.Handled = true;
        }
    }
}
