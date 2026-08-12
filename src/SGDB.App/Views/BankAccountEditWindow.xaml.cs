using System.Windows;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class BankAccountEditWindow : Window
{
    private readonly int? _id;

    public BankAccountEditWindow(int? id)
    {
        _id = id;
        InitializeComponent();
        TypeBox.ItemsSource = new[] { "corrente", "poupanca", "aplicacao" };
        TypeBox.SelectedIndex = 0;
        OperatorBox.ItemsSource = BankService.CommonOperators;
        OpeningBox.Text = "0,00";

        if (id is int accountId)
        {
            Title = "Editar Conta Bancária";
            var a = BankService.GetAccount(accountId);
            NameBox.Text = a.Name;
            BankBox.Text = a.BankName;
            AgencyBox.Text = a.Agency;
            NumberBox.Text = a.AccountNumber;
            TypeBox.SelectedItem = a.AccountType;
            PixBox.Text = a.PixKey;
            OperatorBox.Text = a.DefaultOperator;
            OpeningBox.Text = ProductPriceHelper.FormatBr(a.OpeningBalance);
            ActiveBox.IsChecked = a.Active;
            NotesBox.Text = a.Notes;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BankService.SaveAccount(
                _id,
                NameBox.Text,
                BankBox.Text,
                AgencyBox.Text,
                NumberBox.Text,
                TypeBox.SelectedItem as string ?? "corrente",
                PixBox.Text,
                ProductPriceHelper.ParseBr(OpeningBox.Text),
                ActiveBox.IsChecked == true,
                NotesBox.Text,
                OperatorBox.Text);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Conta Bancária", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
