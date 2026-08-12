using System.Windows;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class CashLancamentoWindow : Window
{
    public CashLancamentoWindow()
    {
        InitializeComponent();
        InputUxHelper.Attach(this);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var names = ExpenseCategoriesService.ListActiveNames();
        CategoriaBox.ItemsSource = names;
        CategoriaBox.SelectedItem = names.Contains(ExpenseCategories.Default)
            ? ExpenseCategories.Default
            : names.FirstOrDefault();

        FornecedorBox.ItemsSource = PersonService.List(tipo: "fornecedores");
        if (FornecedorBox.Items.Count > 0)
            FornecedorBox.SelectedIndex = 0;

        UpdateFieldsVisibility();
        ValorBox.Focus();
        ValorBox.SelectAll();
    }

    private void OperationType_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        UpdateFieldsVisibility();
    }

    private void UpdateFieldsVisibility()
    {
        var isDespesa = OpDespesa.IsChecked == true;
        CategoriaRow.Visibility = isDespesa ? Visibility.Visible : Visibility.Collapsed;
        FornecedorRow.Visibility = isDespesa ? Visibility.Visible : Visibility.Collapsed;
    }

    private string PaymentType => PayCheque.IsChecked == true ? "Cheque" : "Dinheiro";

    private void ValorBox_LostFocus(object sender, RoutedEventArgs e) =>
        ValorBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(ValorBox.Text));

    private void Voltar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySave())
            return;
        DialogResult = true;
        Close();
    }

    private bool TrySave()
    {
        var valor = ProductPriceHelper.ParseBr(ValorBox.Text);
        var obs = ObsBox.Text.Trim();

        if (valor <= 0)
        {
            MessageBox.Show("Informe um valor maior que zero.", "Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
            ValorBox.Focus();
            return false;
        }

        try
        {
            if (OpDespesa.IsChecked == true)
            {
                if (FornecedorBox.SelectedItem is not Person supplier)
                {
                    MessageBox.Show("Selecione o fornecedor.", "Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
                    FornecedorBox.Focus();
                    return false;
                }

                var categoria = CategoriaBox.SelectedItem as string
                    ?? ExpenseCategoriesService.ListActiveNames().FirstOrDefault()
                    ?? ExpenseCategories.Default;
                CashService.RegisterDespesa(valor, PaymentType, categoria, supplier.Id, supplier.Name, obs);
            }
            else if (OpSangria.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(obs))
                {
                    MessageBox.Show("Informe o motivo na observação.", "Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ObsBox.Focus();
                    return false;
                }
                CashService.AddSangria(valor, obs);
            }
            else
            {
                CashService.AddSuprimento(valor, string.IsNullOrWhiteSpace(obs) ? null : obs);
            }
        }
        catch (CashOperationException ex)
        {
            MessageBox.Show(ex.Message, "Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}
