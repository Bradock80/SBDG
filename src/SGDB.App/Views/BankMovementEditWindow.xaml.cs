using System.Windows;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class BankMovementEditWindow : Window
{
    private readonly int _accountId;

    public BankMovementEditWindow(int accountId)
    {
        _accountId = accountId;
        InitializeComponent();
        DateBox.SelectedDate = DateTime.Today;
        KindBox.ItemsSource = new[] { "credito", "debito", "tarifa", "transferencia", "ajuste" };
        KindBox.SelectedIndex = 0;
        var pays = PaymentMethodsService.List().Where(m => m.Active).Select(m => m.ApiLabel).ToList();
        PayBox.ItemsSource = pays;
        OperatorBox.ItemsSource = BankService.CommonOperators;
        try
        {
            var acc = BankService.GetAccount(accountId);
            if (!string.IsNullOrWhiteSpace(acc.DefaultOperator))
                OperatorBox.Text = acc.DefaultOperator;
        }
        catch
        {
            /* ignore */
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DateBox.SelectedDate is not DateTime date)
                throw new BankException("Informe a data.");
            BankService.AddMovement(
                _accountId,
                date,
                KindBox.SelectedItem as string ?? "credito",
                DescBox.Text,
                ProductPriceHelper.ParseBr(InBox.Text),
                ProductPriceHelper.ParseBr(OutBox.Text),
                ProductPriceHelper.ParseBr(FeeBox.Text),
                PayBox.Text,
                operatorName: OperatorBox.Text);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lançamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
