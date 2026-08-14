using System.ComponentModel;
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
    private readonly PixCheckoutCoordinator _checkout;
    private readonly CancellationTokenSource _cts = new();
    private DispatcherTimer? _pollTimer;
    private bool _allowClose;
    private bool _closingInProgress;

    public long? PaymentId => _checkout.PaymentId;
    public bool PaidConfirmed => _checkout.PaidConfirmed;
    public bool PaidViaQrCode => _checkout.PaidConfirmed;

    public PdvPixQrWindow(double amount, string? description = null)
    {
        var desc = string.IsNullOrWhiteSpace(description)
            ? $"Venda PDV R$ {ProductPriceHelper.RoundPrice(amount):N2}"
            : description.Trim();
        _checkout = new PixCheckoutCoordinator(ProductPriceHelper.RoundPrice(amount), desc);
        InitializeComponent();
        ValorText.Text = $"R$ {ProductPriceHelper.RoundPrice(amount):N2}";
        ApplyUi();
        Loaded += async (_, _) => await StartAsync();
    }

    private void ApplyUi()
    {
        StatusText.Text = _checkout.UiStatus;
        HintText.Text = _checkout.UiHint;
        StatusText.Foreground = _checkout.PaidConfirmed
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gold;
    }

    private async Task StartAsync()
    {
        try
        {
            StatusText.Text = "Gerando cobrança PIX…";
            await _checkout.StartAsync(_cts.Token);
            var charge = _checkout.LastCharge;
            if (charge is not null)
            {
                CopiaColaBox.Text = charge.QrCode;
                ShowQr(charge.QrCodeBase64);
            }

            ApplyUi();
            if (_checkout.PaidConfirmed)
            {
                await FinishPaidAsync();
                return;
            }

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
            StatusText.Text = PixMpStatus.WaitingMessage;
            HintText.Text = ex.Message;
            QrPlaceholder.Text = "Erro";
            MessageBox.Show(ex.Message, "PIX Mercado Pago", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task PollOnceAsync()
    {
        if (_closingInProgress || _checkout.PaidConfirmed || _cts.IsCancellationRequested)
            return;

        var ok = await _checkout.TryConfirmFromApiAsync(_cts.Token);
        ApplyUi();
        if (ok)
            await FinishPaidAsync();
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        BtnVerify.IsEnabled = false;
        try
        {
            var ok = await _checkout.TryConfirmFromApiAsync(_cts.Token);
            ApplyUi();
            if (ok)
            {
                await FinishPaidAsync();
                return;
            }

            MessageBox.Show(
                "O Mercado Pago ainda não confirmou este PIX (status diferente de approved).\n\n" +
                "Não entregue a mercadoria. Aguarde ou peça ao cliente para concluir o pagamento.",
                "PIX aguardando",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        finally
        {
            if (IsVisible && !_checkout.PaidConfirmed)
                BtnVerify.IsEnabled = true;
        }
    }

    private async Task FinishPaidAsync()
    {
        _pollTimer?.Stop();
        BtnVerify.IsEnabled = false;
        ApplyUi();
        try
        {
            await Task.Delay(400);
        }
        catch
        {
            // ignore
        }

        _allowClose = true;
        if (IsVisible)
        {
            DialogResult = true;
            Close();
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

    private async void Cancel_Click(object sender, RoutedEventArgs e) =>
        await RequestAbortAndCloseAsync();

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await RequestAbortAndCloseAsync();
        }
    }

    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        await RequestAbortAndCloseAsync();
    }

    private async Task RequestAbortAndCloseAsync()
    {
        if (_allowClose || _closingInProgress)
            return;
        _closingInProgress = true;
        _pollTimer?.Stop();
        try
        {
            await _checkout.AbortAsync(_cts.Token);
        }
        catch
        {
            // persistência do erro fica no intent
        }

        _allowClose = true;
        try
        {
            DialogResult = false;
        }
        catch (InvalidOperationException)
        {
            // janela ainda não modal
        }
        Close();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _pollTimer?.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }
}
