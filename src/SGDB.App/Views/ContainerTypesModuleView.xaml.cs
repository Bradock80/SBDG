using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class ContainerTypesModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    private int? _editingId;
    private bool _suppress;

    public ContainerTypesModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            ClearForm();
            Reload();
        };
    }

    private ContainerType? Selected => TypesGrid.SelectedItem as ContainerType;

    private void Reload()
    {
        _suppress = true;
        TypesGrid.ItemsSource = ContainerTypesService.List(onlyActive: false);
        _suppress = false;
    }

    private void ClearForm()
    {
        _editingId = null;
        NameBox.Text = "";
        StockBox.Text = "0,000";
        PriceBox.Text = "0,00";
        ActiveBox.IsChecked = true;
        SaveBtn.Content = "Cadastrar tipo";
        _suppress = true;
        TypesGrid.SelectedItem = null;
        _suppress = false;
        NameBox.Focus();
    }

    private void BindForm(ContainerType t)
    {
        _editingId = t.Id;
        NameBox.Text = t.Name;
        StockBox.Text = t.Stock.ToString("N3");
        PriceBox.Text = t.SalePrice.ToString("N2");
        ActiveBox.IsChecked = t.Active;
        SaveBtn.Content = "Gravar alteração";
    }

    private void TypesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || Selected is null) return;
        BindForm(Selected);
    }

    private void New_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = new ContainerTypeInput
            {
                Name = NameBox.Text,
                SalePrice = ProductPriceHelper.ParseBr(PriceBox.Text),
                Stock = ProductPriceHelper.ParseBr(StockBox.Text),
                Active = ActiveBox.IsChecked == true,
            };
            var saved = _editingId is null
                ? ContainerTypesService.Create(input)
                : ContainerTypesService.Update(_editingId.Value, input);
            Reload();
            ClearForm();
            // destaca o item gravado na lista
            _suppress = true;
            TypesGrid.SelectedItem = ContainerTypesService.List(false).FirstOrDefault(x => x.Id == saved.Id);
            _suppress = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Tipos de vasilhame", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.FocusedElement == NameBox)
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
