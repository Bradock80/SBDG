using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PdvPixQrWindow : Window
{
    private readonly double _amount;
    private readonly string _description;
    private readonly CancellationTokenSource _cts = new();
    private long _paymentId;
    private bool _closingOk;
    private DispatcherTimer? _pollTimer;
    private int _pollErrors;

    public long? PaymentId => _paymentId > 0 ? _paymentId : null;
    public bool PaidConfirmed { get; private set; }

    /// <summary>True se o PIX foi detectado pelo QR (API), não só confirmação manual.</summary>
    public bool PaidViaQrCode { get; private set; }

    public PdvPixQrWindow(double amount, string? description = null)
    {
        _amount = ProductPriceHelper.RoundPrice(amount);
        _description = string.IsNullOrWhiteSpace(description)
            ? $"Venda PDV R$ {_amount:N2}"
            : description.Trim();
        InitializeComponent();
        ValorText.Text = $"R$ {_amount:N2}";
        Loaded += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            StatusText.Text = "Gerando cobrança PIX…";
            var charge = await MercadoPagoPixService.CreatePixAsync(
                _amount, _description, ct: _cts.Token);

            if (_cts.IsCancellationRequested)
                return;

            _paymentId = charge.PaymentId;
            CopiaColaBox.Text = charge.QrCode;
            ShowQr(charge.QrCodeBase64);

            if (string.Equals(charge.Status, "approved", StringComparison.OrdinalIgnoreCase))
            {
                FinishPaid();
                return;
            }

            StatusText.Text = "Aguardando pagamento…";
            HintText.Text = charge.ExpiresAt is DateTime exp
                ? $"Válido até {exp:HH:mm}. Cliente lê o QR no app do banco."
                : "Peça ao cliente para abrir o app do banco e ler o QR Code.";

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _pollTimer.Tick += async (_, _) => await PollOnceAsync();
            _pollTimer.Start();
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível gerar o QR";
            HintText.Text = ex.Message;
            QrPlaceholder.Text = "Erro";
            MessageBox.Show(ex.Message, "PIX Mercado Pago", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task PollOnceAsync()
    {
        if (_paymentId <= 0 || _cts.IsCancellationRequested || PaidConfirmed)
            return;

        try
        {
            var charge = await MercadoPagoPixService.GetPaymentAsync(_paymentId, _cts.Token);
            _pollErrors = 0;
            var st = (charge.Status ?? "").ToLowerInvariant();
            if (st == "approved")
            {
                FinishPaid();
                return;
            }

            if (st is "cancelled" or "rejected" or "expired")
            {
                _pollTimer?.Stop();
                StatusText.Text = $"Pagamento {st}";
                HintText.Text = string.IsNullOrWhiteSpace(charge.StatusDetail)
                    ? "Gere novamente ou cancele a venda."
                    : charge.StatusDetail;
                return;
            }

            StatusText.Text = "Aguardando pagamento…";
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            _pollErrors++;
            if (_pollErrors >= 3)
                StatusText.Text = "Aguardando… (falha temporária na consulta)";
        }
    }

    private void ShowQr(string? base64)
    {
        QrImage.Source = null;
        if (string.IsNullOrWhiteSpace(base64))
        {
            QrPlaceholder.Text = "Sem imagem do QR.\nUse o código Pix Copia e Cola.";
            QrPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            // MP às vezes manda com prefixo data:image/png;base64,
            var raw = base64;
            var idx = raw.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                raw = raw[(idx + 7)..];

            var bytes = Convert.FromBase64String(raw);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            QrImage.Source = bmp;
            QrPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            QrPlaceholder.Text = "QR inválido — use Copia e Cola";
            QrPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private async void FinishPaid()
    {
        _pollTimer?.Stop();
        PaidConfirmed = true;
        PaidViaQrCode = true;
        _closingOk = true;
        StatusText.Text = "Pagamento confirmado!";
        HintText.Text = "PIX via QR Code aprovado. Finalizando a venda…";
        StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        BtnManual.IsEnabled = false;

        try
        {
            await Task.Delay(800);
        }
        catch
        {
            // ignore
        }

        if (IsVisible)
        {
            DialogResult = true;
            Close();
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CopiaColaBox.Text))
            return;
        try
        {
            Clipboard.SetText(CopiaColaBox.Text);
            HintText.Text = "Código copiado. Cole no app do banco se preferir.";
        }
        catch
        {
            MessageBox.Show("Não foi possível copiar.", "PIX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        var ask = MessageBox.Show(
            "Confirma que o PIX já caiu na conta Mercado Pago?\n\nUse só se o cliente já pagou.",
            "Confirmar PIX manual",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (ask != MessageBoxResult.Yes)
            return;

        PaidConfirmed = true;
        _closingOk = true;
        DialogResult = true;
        Close();
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        await CancelAndCloseAsync();
    }

    private async Task CancelAndCloseAsync()
    {
        _pollTimer?.Stop();
        _cts.Cancel();
        if (_paymentId > 0 && !PaidConfirmed)
        {
            try { await MercadoPagoPixService.CancelPaymentAsync(_paymentId); }
            catch { /* ignore */ }
        }
        _closingOk = false;
        DialogResult = false;
        Close();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await CancelAndCloseAsync();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _pollTimer?.Stop();
        _cts.Cancel();
        _cts.Dispose();
        if (!_closingOk && DialogResult != true)
            DialogResult = false;
    }
}
