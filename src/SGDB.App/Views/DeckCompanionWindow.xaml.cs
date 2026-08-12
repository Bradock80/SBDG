using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
using SGDB.Services;

namespace SGDB.Views;

public partial class DeckCompanionWindow : Window
{
    public DeckCompanionWindow()
    {
        InitializeComponent();
        PinBox.Text = DeckCompanionHost.EnsurePin();
        PortBox.Text = DeckCompanionHost.GetConfiguredPort().ToString();
        Loaded += (_, _) => RefreshUi();
    }

    private void RefreshUi()
    {
        var host = DeckCompanionHost.Current;
        var running = host is { IsRunning: true };
        StartBtn.IsEnabled = !running;
        StopBtn.IsEnabled = running;
        PortBox.IsEnabled = !running;

        if (running && host is not null)
        {
            var url = host.LanUrl ?? host.LocalUrl;
            UrlText.Text = url;
            var lanList = host.Urls
                .Where(u => !u.Contains("127.0.0.1", StringComparison.Ordinal))
                .Distinct()
                .ToList();
            AltUrlsText.Text = lanList.Count > 1
                ? "Outros IPs deste PC:\n" + string.Join("\n", lanList.Where(u => u != url))
                : lanList.Count == 0
                    ? "Atenção: só 127.0.0.1 — o celular NÃO vai conectar. Verifique o cabo/rede."
                    : "Use este endereço no Wi‑Fi do celular (mesmo roteador do cabo).";

            StatusText.Text = host.LanUrl is null
                ? $"Ligado só em {host.LocalUrl} — celular não alcança. Confira o cabo e o IP."
                : $"Ligado. PIN: {host.Pin} · Porta: {host.Port}";
            RenderQr(url);
        }
        else
        {
            UrlText.Text = "—";
            AltUrlsText.Text = "";
            StatusText.Text = "Servidor desligado.";
            QrImage.Source = null;
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1024 or > 65535)
                throw new InvalidOperationException("Porta inválida (use 1024–65535).");

            DeckCompanionHost.SavePin(PinBox.Text);
            DeckCompanionHost.StartNew(port);
            RefreshUi();

            if (DeckCompanionHost.Current?.LanUrl is null)
            {
                MessageBox.Show(
                    "O servidor ligou, mas não achou IP privado da rede (192.168… / 10…).\n\n" +
                    "No Prompt de Comando digite: ipconfig\n" +
                    "Procure o IPv4 da placa Ethernet e abra no celular:\n" +
                    "http://ESSE-IP:porta\n\n" +
                    "Não use 127.0.0.1 no celular.",
                    "Deck no celular", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Deck no celular", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshUi();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        DeckCompanionHost.Current?.Dispose();
        RefreshUi();
    }

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port is < 1024 or > 65535)
            port = DeckCompanionHost.GetConfiguredPort();

        // Tenta sem admin; se falhar, pede UAC
        var ok = DeckCompanionHost.TryOpenFirewallRule(port)
                 || DeckCompanionHost.TryOpenFirewallElevated(port);
        MessageBox.Show(
            ok
                ? $"Porta {port} liberada no Firewall.\n\nAgora clique em Ligar e abra no celular o endereço http://192.168.…"
                : "Não consegui liberar o Firewall.\n\n" +
                  "Aceite o aviso do Windows (Sim) ou abra o PowerShell como Administrador e rode:\n\n" +
                  $"netsh advfirewall firewall add rule name=\"SGDB Deck Celular\" dir=in action=allow protocol=TCP localport={port}",
            "Firewall",
            MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void SavePin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DeckCompanionHost.SavePin(PinBox.Text);
            MessageBox.Show("PIN salvo.", "Deck no celular", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Deck no celular", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url) || url == "—")
            return;
        try
        {
            Clipboard.SetText(url);
            MessageBox.Show("Endereço copiado.\nCole no navegador do celular.", "Deck no celular",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Deck no celular", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLocal_Click(object sender, RoutedEventArgs e)
    {
        var host = DeckCompanionHost.Current;
        if (host is not { IsRunning: true })
        {
            MessageBox.Show("Ligue o servidor antes.", "Deck no celular",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(host.LocalUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Deck no celular", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RenderQr(string url)
    {
        try
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var qr = new PngByteQRCode(data);
            var bytes = qr.GetGraphic(8);
            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            QrImage.Source = bmp;
        }
        catch
        {
            QrImage.Source = null;
        }
    }
}
