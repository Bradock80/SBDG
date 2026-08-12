using System.Text;
using System.Windows;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class CashDepositConfirmWindow : Window
{
    private readonly CashDepositRow _row;

    public CashDepositConfirmWindow(CashDepositRow row)
    {
        _row = row;
        InitializeComponent();
        InputUxHelper.Attach(this);
        TitleText.Text = $"Conferir depósito — {_row.DepositDateBr}";

        var sb = new StringBuilder();
        sb.AppendLine($"Dia: {_row.DepositDateBr}");
        sb.AppendLine($"Valor aguardando: {_row.AmountDisplay}");
        if (_row.Status is "depositado" or "divergente")
            sb.AppendLine($"Última conferência: {_row.StatusLabel} ({_row.ConfirmedDisplay})");
        ResumoText.Text = sb.ToString().TrimEnd();

        ValorBox.Text = ProductPriceHelper.FormatBr(_row.Amount);
        ValorBox.LostFocus += (_, _) =>
            ValorBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(ValorBox.Text));

        Loaded += (_, _) =>
        {
            ValorBox.Focus();
            ValorBox.SelectAll();
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => TryConfirm();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TryConfirm()
    {
        var valor = ProductPriceHelper.ParseBr(ValorBox.Text);
        if (valor < 0)
        {
            MessageBox.Show("Valor inválido.", "Conferência de Depósitos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            CashService.ConfirmDepositAwait(_row.Id, valor, ObsBox.Text);
            var bate = Math.Abs(valor - _row.Amount) < 0.02;
            MessageBox.Show(
                bate
                    ? $"Bateu certo.\nDepositado: R$ {valor:N2}"
                    : $"Valor divergente.\nAguardando: R$ {_row.Amount:N2}\nInformado: R$ {valor:N2}",
                "Conferência de Depósitos",
                MessageBoxButton.OK,
                bate ? MessageBoxImage.Information : MessageBoxImage.Warning);
            DialogResult = true;
            Close();
        }
        catch (CashOperationException ex)
        {
            MessageBox.Show(ex.Message, "Conferência de Depósitos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            TryConfirm();
            e.Handled = true;
        }
    }
}
