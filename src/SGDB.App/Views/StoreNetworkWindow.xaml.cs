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
        ClientFingerprintBox.Text = StoreNetworkMode.GetServerFingerprint();
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
                ? $"Rede Loja segura ligada só em {host.LocalUrl} — confira o cabo/IP."
                : $"Rede Loja segura (HTTPS). PIN: {host.Pin} · Porta: {host.Port}";
            RenderQr(url);
            ServerFingerprintBox.Text = FormatFingerprint(host.CertificateFingerprint);
            CertValidityText.Text = host.CertificateNotAfter is DateTime until
                ? $"Válido até {until:dd/MM/yyyy}"
                : "";
        }
        else
        {
            UrlText.Text = "—";
            ServerStatusText.Text = "Servidor desligado.";
            QrImage.Source = null;
            FillServerCertificateUiWhenStopped();
        }

        var role = StoreNetworkMode.GetRole();
        ClientStatusText.Text = role switch
        {
            StoreNetworkMode.RoleClient =>
                $"Modo Cliente → {StoreNetworkMode.GetClientHost()}:{StoreNetworkMode.GetPort()}",
            StoreNetworkMode.RoleServer => "Modo Servidor (este PC é a loja).",
            _ => "Modo PC único (sem rede).",
        };
        RefreshClientTlsStatus();
        RefreshPairingUi();
    }

    private void RefreshPairingUi()
    {
        var active = StoreNetworkPairingService.PeekActiveCode();
        if (active is not null)
        {
            PairingCodeText.Text = active.Code;
            PairingExpiryText.Text =
                $"Expira em 5 minutos (até {active.ExpiresAtUtc.ToLocalTime():HH:mm}).";
        }
        else
        {
            PairingCodeText.Text = "";
            PairingExpiryText.Text = "Gere um código para autorizar um notebook.";
        }

        try
        {
            DevicesList.ItemsSource = StoreNetworkPairingService.ListDevices()
                .Select(StoreNetworkDeviceRow.From)
                .ToList();
        }
        catch (Exception ex)
        {
            DevicesList.ItemsSource = null;
            PairingExpiryText.Text = ex.Message;
        }

        RefreshClientPairingStatus();
    }

    private void RefreshClientPairingStatus()
    {
        try
        {
            var localId = "";
            try { localId = StoreNetworkPairingService.EnsureDeviceId(); }
            catch { /* identidade inválida: mostra texto padrão */ }

            var abbr = string.IsNullOrEmpty(localId)
                ? ""
                : StoreNetworkPairingService.AbbreviateDeviceId(localId);
            var name = StoreNetworkPairingService.GetDeviceName();

            if (!StoreNetworkMode.IsClient || !StoreNetworkMode.HasServerFingerprint())
            {
                PairingStatusText.Text = string.IsNullOrEmpty(abbr)
                    ? "Este computador ainda não foi autorizado pela loja."
                    : $"Este computador ({name} · {abbr}) ainda não foi autorizado pela loja.";
                return;
            }

            var status = StoreNetworkClient.GetPairingStatus();
            if (status.Revoked)
            {
                PairingStatusText.Text = StoreNetworkClient.DeviceRevokedMessage;
                return;
            }

            if (status.Authorized)
            {
                PairingStatusText.Text =
                    $"Este computador está autorizado pela loja.\n{status.DeviceName} · {StoreNetworkPairingService.AbbreviateDeviceId(status.DeviceId)}";
                return;
            }

            PairingStatusText.Text =
                $"Este computador ({name} · {abbr}) ainda não foi autorizado pela loja.";
        }
        catch
        {
            PairingStatusText.Text = "Este computador ainda não foi autorizado pela loja.";
        }
    }

    private void FillServerCertificateUiWhenStopped()
    {
        var status = StoreNetworkCertificateService.GetFileStatus();
        switch (status)
        {
            case StoreNetworkCertificateStatus.Ok:
                ServerFingerprintBox.Text = FormatFingerprint(StoreNetworkCertificateService.TryReadFingerprint());
                CertValidityText.Text = "Certificado pronto. Clique em Ligar.";
                break;
            case StoreNetworkCertificateStatus.Missing:
                ServerFingerprintBox.Text = "";
                CertValidityText.Text = "O certificado será criado ao Ligar.";
                break;
            case StoreNetworkCertificateStatus.Expired:
                ServerFingerprintBox.Text = FormatFingerprint(StoreNetworkCertificateService.TryReadFingerprint());
                CertValidityText.Text = "Certificado expirado — a Rede Loja não liga até regenerar (muda o fingerprint).";
                break;
            default:
                ServerFingerprintBox.Text = "";
                CertValidityText.Text = "Arquivo de certificado ilegível. A Rede Loja não será ligada (fingerprint não muda sozinho).";
                break;
        }
    }

    private void RefreshClientTlsStatus()
    {
        if (StoreNetworkMode.HasServerFingerprint())
        {
            ClientTlsStatusText.Text =
                "TLS: fingerprint configurado. A conexão usará HTTPS; sem fallback para HTTP.";
        }
        else
        {
            ClientTlsStatusText.Text =
                "TLS: este computador ainda não confia no certificado da loja. " +
                "Configure o fingerprint exibido no PC servidor.";
        }
    }

    private static string FormatFingerprint(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return "";
        if (!StoreNetworkCertificateService.TryNormalizeFingerprint(hex, out var n))
            return hex.Trim();
        var sb = new System.Text.StringBuilder(80);
        for (var i = 0; i < n.Length; i += 4)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(n.AsSpan(i, Math.Min(4, n.Length - i)));
        }
        return sb.ToString();
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

    private void CopyFingerprint_Click(object sender, RoutedEventArgs e)
    {
        var fp = ServerFingerprintBox.Text;
        if (string.IsNullOrWhiteSpace(fp)) return;
        Clipboard.SetText(fp.Replace(" ", ""));
        MessageBox.Show("Fingerprint copiado.", "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveFingerprint_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var typed = ClientFingerprintBox.Text;
            var confirm = MessageBox.Show(
                "Confirmar o fingerprint do PC da loja?\n\n" +
                "Só aceite se você copiou este valor da tela Servidor neste estabelecimento.\n" +
                "O certificado apresentado na rede NÃO será aceito automaticamente.",
                "Confirmar fingerprint",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;
            StoreNetworkMode.SaveServerFingerprint(typed);
            ClientFingerprintBox.Text = FormatFingerprint(StoreNetworkMode.GetServerFingerprint());
            RefreshClientTlsStatus();
            MessageBox.Show("Fingerprint salvo. Agora use Testar ou Salvar como Cliente.",
                "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool EnsureFingerprintConfigured()
    {
        if (StoreNetworkMode.HasServerFingerprint())
            return true;
        MessageBox.Show(
            StoreNetworkClient.MissingFingerprintMessage,
            "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
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
            if (!EnsureFingerprintConfigured())
                return;
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
            if (!EnsureFingerprintConfigured())
                return;
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

    private void AddComputer_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("SistemaUsuarios", "adicionar computador na Rede Loja", this))
            return;
        var generated = StoreNetworkPairingService.GenerateCode();
        PairingCodeText.Text = generated.Code;
        PairingExpiryText.Text =
            "Código: válido por 5 minutos" +
            (StoreNetworkHost.Current is { IsRunning: true }
                ? $" (até {generated.ExpiresAtUtc.ToLocalTime():HH:mm})."
                : " — ligue o servidor para o notebook usar o código.");
    }

    private void RevokeDevice_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("SistemaUsuarios", "revogar computador da Rede Loja", this))
            return;
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string deviceId)
            return;
        var confirm = MessageBox.Show(
            "Revogar este computador?\nEle precisará de um novo código de pareamento.",
            "Revogar computador",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;
        try
        {
            StoreNetworkPairingService.Revoke(deviceId);
            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PairClient_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureFingerprintConfigured())
                return;
            ApplyClientFieldsFromBoxes();
            var result = StoreNetworkClient.Pair(PairingCodeBox.Text);
            PairingCodeBox.Clear();
            PairingStatusText.Text =
                $"Este computador está autorizado pela loja.\n{result.DeviceName} · {StoreNetworkPairingService.AbbreviateDeviceId(result.DeviceId)}";
            MessageBox.Show(
                "Este computador está autorizado pela loja.",
                "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshUi();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("não está mais autorizado", StringComparison.OrdinalIgnoreCase))
                PairingStatusText.Text = StoreNetworkClient.DeviceRevokedMessage;
            MessageBox.Show(msg, "Rede Loja", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class StoreNetworkDeviceRow
{
    public string DeviceId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Details { get; init; } = "";
    public bool CanRevoke { get; init; }

    public static StoreNetworkDeviceRow From(StoreNetworkPairedDevice d)
    {
        var status = d.Revoked ? "Revogado" : "Autorizado";
        var created = FormatWhen(d.CreatedAt);
        var seen = FormatWhen(d.LastSeenAt);
        return new StoreNetworkDeviceRow
        {
            DeviceId = d.DeviceId,
            Title = $"{d.DeviceName} · {StoreNetworkPairingService.AbbreviateDeviceId(d.DeviceId)}",
            Details = $"{status} · pareado {created} · último contato {seen}",
            CanRevoke = !d.Revoked,
        };
    }

    private static string FormatWhen(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "—";
        if (DateTime.TryParse(raw, out var dt))
            return dt.ToLocalTime().ToString("dd/MM HH:mm");
        return raw;
    }
}
