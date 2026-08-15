using System.Windows;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class FiadoDetailWindow : Window
{
    private readonly int _customerId;
    public bool Changed { get; private set; }

    public FiadoDetailWindow(int customerId)
    {
        _customerId = customerId;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Reload();

    /// <summary>
    /// DataGrid engole a roda do mouse; força o scroll da página de lançamentos.
    /// </summary>
    private void DetailScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
            return;

        DetailScroll.ScrollToVerticalOffset(DetailScroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void Reload()
    {
        try
        {
            var detail = FiadoService.GetDetail(_customerId);
            ClienteNomeText.Text = detail.CustomerName;
            ClientePhoneText.Text = detail.Phone ?? "";
            ClientePhoneText.Visibility = string.IsNullOrWhiteSpace(detail.Phone)
                ? Visibility.Collapsed
                : Visibility.Visible;

            VendidoText.Text = $"R$ {detail.TotalCharges:N2}";
            RecebidoText.Text = $"R$ {detail.TotalPaid:N2}";
            SaldoText.Text = $"R$ {detail.Balance:N2}";

            SalesList.ItemsSource = detail.Sales;
            SalesEmptyText.Visibility = detail.Sales.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            PaymentsGrid.ItemsSource = detail.Payments;
            var hasPayments = detail.Payments.Count > 0;
            PaymentsGrid.Visibility = hasPayments ? Visibility.Visible : Visibility.Collapsed;
            PaymentsEmptyText.Visibility = hasPayments ? Visibility.Collapsed : Visibility.Visible;

            BtnReceber.Visibility = detail.Balance > 0.005 && AccessControl.Can("FiadoReceber")
                ? Visibility.Visible
                : Visibility.Collapsed;
            BtnEstornar.Visibility = AccessControl.Can("FiadoEstornar")
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fiado", MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
            Close();
        }
    }

    private void Receber_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("FiadoReceber", "receber fiado", this))
            return;
        if (!CashService.IsOperational())
        {
            MessageBox.Show("Abra o caixa antes de registrar o recebimento.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new FiadoReceberWindow(_customerId) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            Changed = true;
            Reload();
        }
    }

    private void Estornar_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("FiadoEstornar", "estornar recebimento de fiado", this))
            return;
        if (PaymentsGrid.Visibility != Visibility.Visible
            || PaymentsGrid.SelectedItem is not FiadoPaymentRow pay)
        {
            MessageBox.Show("Selecione um recebimento na lista.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (pay.Reversed)
        {
            MessageBox.Show("Recebimento já estornado.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            "Deseja estornar este recebimento?\n\nO saldo do cliente voltará a aumentar.",
            "Confirmação",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            FiadoService.ReversePayment(pay.Id);
            Changed = true;
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fiado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Fechar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = Changed;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Fechar_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F7 && BtnReceber.Visibility == Visibility.Visible)
        {
            Receber_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F8 && BtnEstornar.Visibility == Visibility.Visible)
        {
            Estornar_Click(sender, e);
            e.Handled = true;
        }
    }
}
