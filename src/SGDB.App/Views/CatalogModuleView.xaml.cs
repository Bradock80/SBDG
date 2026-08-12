using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class CatalogModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly ProductCatalogKind _kind;
    private int? _editingId;
    private bool _suppress;

    public CatalogModuleView(ProductCatalogKind kind)
    {
        _kind = kind;
        InitializeComponent();
        TitleText.Text = ProductCatalogService.Title(kind);
        SearchLabel.Text = ProductCatalogService.SearchHint(kind);
        NameLabel.Text = ProductCatalogService.NameLabel(kind);
        ColName.Header = ProductCatalogService.NameLabel(kind);

        if (_kind != ProductCatalogKind.Units)
        {
            NameBox.MaxLength = 40;
            DescHint.Text = "Texto auxiliar para identificar o item";
            // Nome mais largo para grupos/marcas
            ColName.Width = new DataGridLength(160);
        }

        Loaded += (_, _) =>
        {
            Focus();
            ClearForm();
            Reload();
            SearchBox.Focus();
        };
    }

    private CatalogItem? Selected => ItemsGrid.SelectedItem as CatalogItem;

    private bool? ActiveFilter =>
        FilterAtivos.IsChecked == true ? true
        : FilterInativos.IsChecked == true ? false
        : null;

    private void Reload()
    {
        _suppress = true;
        ItemsGrid.ItemsSource = ProductCatalogService.ListItems(_kind, ActiveFilter, SearchBox.Text);
        _suppress = false;
    }

    private void ClearForm()
    {
        _editingId = null;
        NameBox.Text = "";
        DescriptionBox.Text = "";
        ActiveBox.IsChecked = true;
        FormTitle.Text = "Novo";
        DeleteBtn.Visibility = Visibility.Collapsed;
        _suppress = true;
        ItemsGrid.SelectedItem = null;
        _suppress = false;
        NameBox.Focus();
    }

    private void BindForm(CatalogItem item)
    {
        _editingId = item.Id;
        NameBox.Text = item.Name;
        DescriptionBox.Text = item.Description ?? "";
        ActiveBox.IsChecked = item.Active;
        FormTitle.Text = "Alterar";
        DeleteBtn.Visibility = item.Active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || Selected is null) return;
        BindForm(Selected);
    }

    private void ItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null)
        {
            BindForm(Selected);
            NameBox.Focus();
            NameBox.SelectAll();
        }
    }

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CatalogItem item })
        {
            ItemsGrid.SelectedItem = item;
            BindForm(item);
            NameBox.Focus();
            NameBox.SelectAll();
        }
    }

    private void InactivateRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CatalogItem item })
            return;

        if (!item.Active)
        {
            MessageBox.Show("Este item já está inativo.", TitleText.Text,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"Inativar \"{item.Name}\"?", TitleText.Text,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        ProductCatalogService.SoftDelete(_kind, item.Id);
        if (_editingId == item.Id)
            ClearForm();
        Reload();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Reload();

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Reload();
    }

    private void New_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var active = ActiveBox.IsChecked == true;
            var desc = DescriptionBox.Text;
            var saved = _editingId is null
                ? ProductCatalogService.Create(_kind, NameBox.Text, active, desc)
                : ProductCatalogService.Update(_kind, _editingId.Value, NameBox.Text, active, desc);
            Reload();
            ItemsGrid.SelectedItem = ProductCatalogService.ListItems(_kind)
                .FirstOrDefault(x => x.Id == saved.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, TitleText.Text, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId is null) return;
        if (MessageBox.Show("Inativar este item?", TitleText.Text, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        ProductCatalogService.SoftDelete(_kind, _editingId.Value);
        ClearForm();
        Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F2) { New_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F5) { Save_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        else if (e.Key == Key.Delete && _editingId is not null
                 && Keyboard.FocusedElement is not TextBox) { Delete_Click(sender, e); e.Handled = true; }
    }
}
