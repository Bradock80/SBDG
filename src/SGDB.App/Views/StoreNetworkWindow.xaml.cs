using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
using SGDB.Services;

namespace SGDB.Views;

public partial class StoreNetworkWindow : Window
{
    public StoreNetworkWindow()
    {
        InitializeComponent();
        ServerPinBox.Text = StoreNetworkMode.EnsurePin();
        ServerPortBox.Text = StoreNetworkMode.GetPort().ToString();
        ClientHostBox.Text = StoreNetworkMode.GetClientHost();
        ClientPinBox.Text = StoreNetworkMode.GetClientPin();
        ClientPortBox.Text = StoreNetworkMode.GetPort().ToString();
        Loaded += (_, _) => RefreshUi();
    }

    private void RefreshUi()
    {
        var host = StoreNetworkHost.Current;
        var running = host is { IsRunning: true };
        StartBtn.IsEnabled = !running;
        StopBtn.IsEnabled = running;
        ServerPortBox.IsEnabled = !running;

        if (running && host is not null)
        {
            var url = host.LanUrl ?? host.LocalUrl;
            UrlText.Text = url;
            ServerStatusText.Text = host.LanUrl is null
                ? $"Ligado só em {host.LocalUrl} — confira o cabo/IP."
                : $"Ligado. PIN: {host.Pin} · Porta: {host.Port}";
            RenderQr(url);
        }
        else
        {
            UrlText.Text = "—";
            ServerStatusText.Text = "Servidor desligado.";
            QrImage.Source = null;
        }

        var role = StoreNetworkMode.GetRole();
        ClientStatusText.Text = role switch
        {
            StoreNetworkMode.RoleClient =>
                $"Modo Cliente → {StoreNetworkMode.GetClientHost()}:{StoreNetworkMode.GetPort()}",
            StoreNetworkMode.RoleServer => "Modo Servidor (este PC é a loja).",
            _ => "Modo PC único (sem rede).",
        };
    }

    private void RenderQr(string url)
    {
        try
        {
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var qr = new PngByteQRCode(data);
            var bytes = qr.GetGraphic(6);
            var img = new BitmapImage();
            using var ms = new System.IO.MemoryStream(bytes);
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            QrImage.Source = img;
        }
        catch
        {
            QrImage.Source = null;
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(ServerPortBox.Text.Trim(), out var port) || port is < 1024 or > 65535)
                throw new InvalidOperationException("Porta inválida.");
            StoreNetworkMode.SavePin(ServerPinBox.Text);
            StoreNetworkHost.StartNew(port);
            RefreshUi();
            if (StoreNetworkHost.Current?.LanUrl is null)
            {
                MessageBox.Show(
                    "Servidor ligado, mas não achou IP 192.168… Confira o cabo/rede.",
                    "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshUi();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        StoreNetworkHost.Current?.Dispose();
        RefreshUi();
    }

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ServerPortBox.Text.Trim(), out var port) || port is < 1024 or > 65535)
            port = StoreNetworkMode.GetPort();
        var ok = StoreNetworkHost.TryOpenFirewallRule(port)
                 || StoreNetworkHost.TryOpenFirewallElevated(port);
        MessageBox.Show(
            ok
                ? $"Porta {port} liberada no Firewall."
                : "Não liberou o Firewall. Aceite o UAC ou rode netsh como Admin.",
            "Firewall", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void SaveServerPin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StoreNetworkMode.SavePin(ServerPinBox.Text);
            MessageBox.Show(
                StoreNetworkHost.Current?.IsRunning == true
                    ? "PIN salvo e já vale no servidor ligado."
                    : "PIN salvo. Clique em Ligar para usar.",
                "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlText.Text;
        if (string.IsNullOrWhiteSpace(url) || url == "—") return;
        Clipboard.SetText(url);
        MessageBox.Show("Endereço copiado.", "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void TestClient_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyClientFieldsFromBoxes();
            var st = StoreNetworkClient.Login(ClientPinBox.Text.Trim());
            MessageBox.Show(
                $"Conectou em: {st.Store ?? "loja"}\n\nAgora clique em \"Salvar como Cliente\".",
                "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message + "\n\nChecklist:\n" +
                "1) Na LOJA: Rede Loja → Servidor → Ligar (deixe aberto)\n" +
                "2) Firewall na loja\n" +
                "3) Notebook na MESMA Wi‑Fi/rede do PC da loja\n" +
                "4) IP só com números e pontos (ex.: 192.168.18.138), sem :5055",
                "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshUi();
        }
    }

    private void SaveClient_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyClientFieldsFromBoxes();
            StoreNetworkHost.Current?.Dispose();
            var st = StoreNetworkClient.Login(ClientPinBox.Text.Trim());
            MessageBox.Show(
                $"Notebook em modo Cliente.\nLoja: {st.Store}\nIP: {StoreNetworkMode.GetClientHost()}:{StoreNetworkMode.GetPort()}",
                "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message + "\n\nNa loja o servidor precisa estar Ligado e o notebook na mesma rede.",
                "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshUi();
        }
    }

    private void ApplyClientFieldsFromBoxes()
    {
        if (!int.TryParse(ClientPortBox.Text.Trim(), out var port) || port is < 1024 or > 65535)
            port = StoreNetworkMode.DefaultPort;
        var (host, p) = StoreNetworkMode.NormalizeHostPort(ClientHostBox.Text, port);
        ClientHostBox.Text = host;
        ClientPortBox.Text = p.ToString();
        StoreNetworkMode.SaveClient(host, ClientPinBox.Text, p);
    }

    private void Standalone_Click(object sender, RoutedEventArgs e)
    {
        StoreNetworkHost.Current?.Dispose();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        MessageBox.Show("Voltou ao modo PC único.", "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshUi();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
