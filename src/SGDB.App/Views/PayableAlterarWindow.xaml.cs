using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PayableAlterarWindow : Window
{
    private int _installmentId;
    private bool _loading;

    public PayableAlterarWindow(int installmentId)
    {
        _installmentId = installmentId;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TipoBox.ItemsSource = PayablePaymentTypes.All;
        var detail = PayableService.GetInstallment(_installmentId);
        if (detail is null)
        {
            MessageBox.Show("Parcela não encontrada.", "Contas a Pagar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
            Close();
            return;
        }

        var pending = PayableService.ListInstallmentsOfTitle(detail.TitleId)
            .Where(i => i.Status != "pago")
            .ToList();
        if (pending.Count > 1)
        {
            ParcelaRow.Visibility = Visibility.Visible;
            _loading = true;
            ParcelaBox.ItemsSource = pending;
            ParcelaBox.SelectedItem = pending.FirstOrDefault(i => i.Id == _installmentId) ?? pending[0];
            _loading = false;
        }

        FillForm(detail);
        VencimentoBox.Focus();
    }

    private void FillForm(PayableInstallmentDetail detail)
    {
        _installmentId = detail.Id;
        VencimentoBox.Text = DateBrHelper.FormatIso(detail.DueDate);
        ValorBox.Text = ProductPriceHelper.FormatBr(detail.Amount);
        DescontoBox.Text = ProductPriceHelper.FormatBr(detail.Discount);
        JurosBox.Text = ProductPriceHelper.FormatBr(detail.Interest);
        TipoBox.SelectedItem = PayablePaymentTypes.All.Contains(detail.PaymentType)
            ? detail.PaymentType
            : "Boleto";
        if (TipoBox.SelectedItem is null)
            TipoBox.SelectedIndex = 0;
    }

    private void ParcelaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ParcelaBox.SelectedItem is not PayableInstallmentDetail detail)
            return;
        FillForm(detail);
    }

    private void Money_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            box.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(box.Text));
    }

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PayableService.UpdateInstallment(_installmentId, new PayableInstallmentUpdateInput
            {
                DueDate = VencimentoBox.Text.Trim(),
                Amount = ProductPriceHelper.ParseBr(ValorBox.Text),
                Discount = ProductPriceHelper.ParseBr(DescontoBox.Text),
                Interest = ProductPriceHelper.ParseBr(JurosBox.Text),
                PaymentType = TipoBox.SelectedItem as string ?? "Boleto",
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
