using System.Windows;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PayableNovoWindow : Window
{
    public PayableNovoWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SupplierBox.ItemsSource = PersonService.List(null, "ativos", "fornecedores");
        TipoBox.ItemsSource = PayablePaymentTypes.All;
        TipoBox.SelectedItem = "Boleto";

        var cats = new List<string> { "— Mesmo nome do fornecedor —" };
        cats.AddRange(ExpenseCategoriesService.ListActiveNames());
        CategoriaBox.ItemsSource = cats;
        CategoriaBox.SelectedIndex = 0;

        EmissaoBox.Text = DateBrHelper.TodayBr();
        VencimentoBox.Text = DateBrHelper.TodayBr();
        ValorBox.Text = ProductPriceHelper.FormatBr(0);
        NumeroBox.Focus();
    }

    private void Money_LostFocus(object sender, RoutedEventArgs e) =>
        ValorBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(ValorBox.Text));

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        if (SupplierBox.SelectedItem is not Person supplier)
        {
            MessageBox.Show("Selecione o fornecedor.", "Contas a Pagar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? category = null;
        if (CategoriaBox.SelectedIndex > 0 && CategoriaBox.SelectedItem is string cat)
            category = cat;

        try
        {
            PayableService.CreateTitle(new PayableTitleCreateInput
            {
                SupplierId = supplier.Id,
                Number = NumeroBox.Text.Trim(),
                EmissionDate = EmissaoBox.Text.Trim(),
                DueDate = VencimentoBox.Text.Trim(),
                TotalAmount = ProductPriceHelper.ParseBr(ValorBox.Text),
                PaymentType = TipoBox.SelectedItem as string ?? "Boleto",
                ExpenseCategory = category,
            });
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancelar_Click(sender, e);
            e.Handled = true;
        }
    }
}
