using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PayableBaixaWindow : Window
{
    private const string ContaNenhuma = "— (não informar)";

    private readonly int _installmentId;
    private PayableInstallmentDetail? _detail;
    private bool _loaded;

    public PayableBaixaWindow(int installmentId)
    {
        _installmentId = installmentId;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TipoBox.ItemsSource = PayablePaymentTypes.All;
        LoadContas();

        _detail = PayableService.GetInstallment(_installmentId);
        if (_detail is null)
        {
            MessageBox.Show("Parcela não encontrada.", "Contas a Pagar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
            Close();
            return;
        }

        ResumoText.Text =
            $"{_detail.SupplierName} — {_detail.DisplayNumber} · Venc. {DateBrHelper.FormatIso(_detail.DueDate)}";
        ValorBox.Text = ProductPriceHelper.FormatBr(_detail.Amount);
        DescontoBox.Text = ProductPriceHelper.FormatBr(_detail.Discount);
        JurosBox.Text = ProductPriceHelper.FormatBr(_detail.Interest);
        MultaBox.Text = ProductPriceHelper.FormatBr(_detail.Multa);
        ObsBox.Text = _detail.Notes ?? "";

        if (_detail.Status == "pago")
        {
            DataBox.Text = DateBrHelper.FormatIso(_detail.PaidDate) is { Length: > 0 } d
                ? d
                : DateBrHelper.TodayBr();
            BtnEstornar.Visibility = AccessControl.Can("ContasPagarEstornar")
                ? Visibility.Visible
                : Visibility.Collapsed;
            BtnSalvar.Content = "Corrigir baixa";
        }
        else
        {
            DataBox.Text = DateBrHelper.TodayBr();
        }

        TipoBox.SelectedItem = PayablePaymentTypes.All.Contains(_detail.PaymentType)
            ? _detail.PaymentType
            : "Boleto";
        if (TipoBox.SelectedItem is null)
            TipoBox.SelectedIndex = 0;

        ContaBox.SelectedItem =
            !string.IsNullOrWhiteSpace(_detail.FinancialAccount)
            && ContaBox.Items.Contains(_detail.FinancialAccount)
                ? _detail.FinancialAccount
                : ContaNenhuma;

        _loaded = true;
        Recalcular();
        DescontoBox.Focus();
        DescontoBox.SelectAll();
    }

    private void LoadContas()
    {
        var itens = new List<string> { ContaNenhuma };
        try
        {
            itens.AddRange(BankService.ListAccounts(onlyActive: true).Select(a => a.Name));
        }
        catch
        {
            // sem contas cadastradas — segue apenas com a opção neutra
        }
        ContaBox.ItemsSource = itens;
        ContaBox.SelectedIndex = 0;
    }

    private void Money_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            box.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(box.Text));
        Recalcular();
    }

    private void Recalc_TextChanged(object sender, TextChangedEventArgs e) => Recalcular();

    private void Recalcular()
    {
        if (!_loaded)
            return;

        var valor = ProductPriceHelper.ParseBr(ValorBox.Text);
        var desconto = ProductPriceHelper.ParseBr(DescontoBox.Text);
        var juros = ProductPriceHelper.ParseBr(JurosBox.Text);
        var multa = ProductPriceHelper.ParseBr(MultaBox.Text);
        var pago = ProductPriceHelper.RoundPrice(valor - desconto + juros + multa);
        if (pago < 0)
            pago = 0;
        PagoBox.Text = ProductPriceHelper.FormatBr(pago);
    }

    private void Anexar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Anexar comprovante",
            Filter = "Comprovantes (*.pdf;*.jpg;*.jpeg;*.png)|*.pdf;*.jpg;*.jpeg;*.png|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SGDB", "comprovantes", _installmentId.ToString());
            Directory.CreateDirectory(baseDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var destName = $"{stamp}_{Path.GetFileName(dlg.FileName)}";
            var dest = Path.Combine(baseDir, destName);
            File.Copy(dlg.FileName, dest, overwrite: true);

            AnexoText.Text = $"📎 Comprovante anexado: {destName}";
            AnexoText.Visibility = Visibility.Visible;

            var linha = $"Comprovante: {destName}";
            ObsBox.Text = string.IsNullOrWhiteSpace(ObsBox.Text)
                ? linha
                : ObsBox.Text.TrimEnd() + Environment.NewLine + linha;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível anexar o comprovante:\n" + ex.Message,
                "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var conta = ContaBox.SelectedItem as string;
            if (conta == ContaNenhuma)
                conta = null;

            PayableService.PayInstallment(_installmentId, new PayablePayInput
            {
                PaidAmount = ProductPriceHelper.ParseBr(PagoBox.Text),
                PaidDate = DataBox.Text.Trim(),
                Discount = ProductPriceHelper.ParseBr(DescontoBox.Text),
                Interest = ProductPriceHelper.ParseBr(JurosBox.Text),
                Multa = ProductPriceHelper.ParseBr(MultaBox.Text),
                PaymentType = TipoBox.SelectedItem as string ?? "Boleto",
                Notes = ObsBox.Text,
                FinancialAccount = conta,
            });
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Estornar_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("ContasPagarEstornar", "estornar pagamento de contas a pagar", this))
            return;
        var confirm = MessageBox.Show(
            "Deseja estornar esta baixa?\n\nA parcela voltará a ficar pendente.",
            "Confirmação",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            PayableService.ReversePayment(_installmentId);
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
