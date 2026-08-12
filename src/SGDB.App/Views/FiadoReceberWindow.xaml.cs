using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class FiadoReceberWindow : Window
{
    private readonly int _customerId;
    private double _saldo;
    private bool _syncing;

    public FiadoReceberWindow(int customerId)
    {
        _customerId = customerId;
        InitializeComponent();
        InputUxHelper.Attach(this, ObsBox);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var detail = FiadoService.GetDetail(_customerId);
            _saldo = detail.Balance;
            ClienteNomeText.Text = detail.CustomerName;
            ClientePhoneText.Text = string.IsNullOrWhiteSpace(detail.Phone)
                ? ""
                : detail.Phone;
            ClientePhoneText.Visibility = string.IsNullOrWhiteSpace(detail.Phone)
                ? Visibility.Collapsed
                : Visibility.Visible;
            VendidoText.Text = $"R$ {detail.TotalCharges:N2}";
            RecebidoText.Text = $"R$ {detail.TotalPaid:N2}";
            SaldoText.Text = $"R$ {detail.Balance:N2}";

            _syncing = true;
            AbateBox.Text = ProductPriceHelper.FormatBr(_saldo);
            JurosPctBox.Text = ProductPriceHelper.FormatBr(0);
            JurosValorBox.Text = ProductPriceHelper.FormatBr(0);
            TotalValueText.Text = $"R$ {ProductPriceHelper.FormatBr(_saldo)}";
            DataBox.Text = DateBrHelper.TodayBr();
            DinheiroBox.Text = ProductPriceHelper.FormatBr(_saldo);
            PixBox.Text = DebitoBox.Text = CreditoBox.Text = ProductPriceHelper.FormatBr(0);
            _syncing = false;

            UpdateHints();
            DinheiroBox.Focus();
            DinheiroBox.SelectAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fiado", MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
            Close();
        }
    }

    private void Money_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            box.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(box.Text));
        RecalcTotalFromAbateJuros();
    }

    private void Values_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
            RecalcTotalFromAbateJuros();
    }

    private void JurosPct_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        var pct = ProductPriceHelper.ParseBr(JurosPctBox.Text);
        var juros = ProductPriceHelper.RoundPrice(_saldo * (pct / 100.0));
        JurosValorBox.Text = ProductPriceHelper.FormatBr(juros);
        _syncing = false;
        RecalcTotalFromAbateJuros();
    }

    private void JurosPct_LostFocus(object sender, RoutedEventArgs e) =>
        JurosPctBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(JurosPctBox.Text));

    private void JurosValor_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        var juros = ProductPriceHelper.ParseBr(JurosValorBox.Text);
        var pct = _saldo > 0.009
            ? ProductPriceHelper.RoundPrice((juros / _saldo) * 100.0)
            : 0;
        JurosPctBox.Text = ProductPriceHelper.FormatBr(pct);
        _syncing = false;
        RecalcTotalFromAbateJuros();
    }

    private void JurosValor_LostFocus(object sender, RoutedEventArgs e) =>
        JurosValorBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(JurosValorBox.Text));

    private void RecalcTotalFromAbateJuros()
    {
        if (_syncing) return;
        var abate = ProductPriceHelper.ParseBr(AbateBox.Text);
        var juros = ProductPriceHelper.ParseBr(JurosValorBox.Text);
        var total = ProductPriceHelper.RoundPrice(abate + juros);
        TotalValueText.Text = $"R$ {ProductPriceHelper.FormatBr(total)}";
        UpdateHints();
    }

    private void Pay_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            box.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(box.Text));
        UpdateHints();
    }

    private void Pay_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
            UpdateHints();
    }

    private double CurrentTotal
    {
        get
        {
            var abate = ProductPriceHelper.ParseBr(AbateBox.Text);
            var juros = ProductPriceHelper.ParseBr(JurosValorBox.Text);
            return ProductPriceHelper.RoundPrice(abate + juros);
        }
    }

    private double DinheiroEntered => ProductPriceHelper.ParseBr(DinheiroBox.Text);
    private double NonCashSum =>
        ProductPriceHelper.RoundPrice(
            ProductPriceHelper.ParseBr(PixBox.Text)
            + ProductPriceHelper.ParseBr(DebitoBox.Text)
            + ProductPriceHelper.ParseBr(CreditoBox.Text));

    private double CashNeed => ProductPriceHelper.RoundPrice(Math.Max(0, CurrentTotal - NonCashSum));

    private double Troco =>
        DinheiroEntered > CashNeed + 0.009
            ? ProductPriceHelper.RoundPrice(DinheiroEntered - CashNeed)
            : 0;

    private void UpdateHints()
    {
        var abate = ProductPriceHelper.ParseBr(AbateBox.Text);
        var faltando = ProductPriceHelper.RoundPrice(Math.Max(0, CashNeed - DinheiroEntered));

        if (NonCashSum > CurrentTotal + 0.02)
        {
            PagRestanteText.Text = $"Pix/cartão (R$ {NonCashSum:N2}) passam do total (R$ {CurrentTotal:N2}).";
            PagRestanteText.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
        else if (faltando > 0.009)
        {
            PagRestanteText.Text = $"Falta R$ {faltando:N2} — informe em Dinheiro ou outra forma.";
            PagRestanteText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xB4, 0x53, 0x09));
        }
        else if (Troco > 0.009)
        {
            PagRestanteText.Text =
                $"Cliente deu R$ {DinheiroEntered:N2} · entra no caixa e sai R$ {Troco:N2} de troco (líquido R$ {CashNeed:N2}).";
            PagRestanteText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x16, 0x65, 0x34));
        }
        else
        {
            PagRestanteText.Text = "Formas de pagamento conferem com o total.";
            PagRestanteText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xB4, 0x53, 0x09));
        }

        if (Troco > 0.009)
        {
            TrocoText.Text = $"Troco: R$ {Troco:N2}";
            TrocoText.Visibility = Visibility.Visible;
        }
        else
        {
            TrocoText.Text = "";
            TrocoText.Visibility = Visibility.Collapsed;
        }

        var after = ProductPriceHelper.RoundPrice(_saldo - abate);
        SaldoAposText.Text = after <= 0.005
            ? "Esta baixa quita todo o saldo do fiado."
            : $"Após esta baixa, ainda ficará em aberto no fiado: R$ {after:N2}.";
    }

    private void UsarRestante(TextBox target)
    {
        var others = PaidWithout(target);
        var need = ProductPriceHelper.RoundPrice(Math.Max(0, CurrentTotal - others));
        target.Text = ProductPriceHelper.FormatBr(need);
        UpdateHints();
        target.Focus();
        target.SelectAll();
    }

    private double PaidWithout(TextBox target)
    {
        double Sum(TextBox box) => ReferenceEquals(box, target) ? 0 : ProductPriceHelper.ParseBr(box.Text);
        return ProductPriceHelper.RoundPrice(
            Sum(DinheiroBox) + Sum(PixBox) + Sum(DebitoBox) + Sum(CreditoBox));
    }

    private void UsarRestante_Dinheiro(object sender, RoutedEventArgs e) => UsarRestante(DinheiroBox);
    private void UsarRestante_Pix(object sender, RoutedEventArgs e) => UsarRestante(PixBox);
    private void UsarRestante_Debito(object sender, RoutedEventArgs e) => UsarRestante(DebitoBox);
    private void UsarRestante_Credito(object sender, RoutedEventArgs e) => UsarRestante(CreditoBox);

    private void Salvar_Click(object sender, RoutedEventArgs e)
    {
        var abate = ProductPriceHelper.ParseBr(AbateBox.Text);
        var juros = ProductPriceHelper.ParseBr(JurosValorBox.Text);
        var total = ProductPriceHelper.RoundPrice(abate + juros);

        var parts = new List<FiadoReceberPart>();
        void AddPart(string type, TextBox box)
        {
            var amt = ProductPriceHelper.ParseBr(box.Text);
            if (amt > 0.009)
                parts.Add(new FiadoReceberPart { PaymentType = type, Amount = amt });
        }
        AddPart("Dinheiro", DinheiroBox);
        AddPart("Pix", PixBox);
        AddPart("Cartão Débito", DebitoBox);
        AddPart("Cartão Crédito", CreditoBox);

        try
        {
            FiadoService.RegisterPayment(_customerId, new FiadoReceberInput
            {
                PrincipalAmount = abate,
                InterestAmount = juros,
                Amount = total,
                PaymentDate = DataBox.Text.Trim(),
                Notes = ObsBox.Text.Trim(),
                Payments = parts,
                CashReceived = DinheiroEntered,
            });

            var msg = Troco > 0.009
                ? $"Recebimento registrado.\n\n" +
                  $"Entra no caixa: R$ {DinheiroEntered:N2}\n" +
                  $"Sai do caixa (troco): R$ {Troco:N2}\n" +
                  $"Líquido no caixa: R$ {CashNeed:N2}"
                : "Recebimento registrado — valor entrou no caixa de hoje.";
            MessageBox.Show(msg, "Fiado", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fiado", MessageBoxButton.OK, MessageBoxImage.Warning);
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
