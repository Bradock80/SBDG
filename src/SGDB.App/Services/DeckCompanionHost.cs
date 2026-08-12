using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Servidor HTTP local para lançar Decks pelo celular (mesma Wi-Fi).
/// Usa TcpListener (não HttpListener) para escutar na rede sem URL ACL / admin.
/// </summary>
public sealed class DeckCompanionHost : IDisposable
{
    public const string SettingPin = "deck_companion_pin";
    public const string SettingPort = "deck_companion_port";
    public const int DefaultPort = 5050;

    private static readonly object Sync = new();
    private static DeckCompanionHost? _current;

    private TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private bool _disposed;

    public static DeckCompanionHost? Current
    {
        get { lock (Sync) return _current; }
    }

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public string Pin { get; private set; } = "";
    public string? LanUrl { get; private set; }
    public string LocalUrl => $"http://127.0.0.1:{Port}/";
    public IReadOnlyList<string> Urls { get; private set; } = [];

    public static string EnsurePin()
    {
        var pin = AppSettingsService.GetSetting(SettingPin);
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
        {
            pin = Random.Shared.Next(1000, 9999).ToString();
            AppSettingsService.SetSetting(SettingPin, pin);
        }
        return pin.Trim();
    }

    public static void SavePin(string pin)
    {
        pin = (pin ?? "").Trim();
        if (pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
            throw new InvalidOperationException("PIN deve ter 4 a 8 dígitos.");
        AppSettingsService.SetSetting(SettingPin, pin);
        lock (Sync)
        {
            if (_current is not null)
                _current.Pin = pin;
        }
    }

    public static int GetConfiguredPort()
    {
        var raw = AppSettingsService.GetSetting(SettingPort);
        if (int.TryParse(raw, out var p) && p is >= 1024 and <= 65535)
            return p;
        return DefaultPort;
    }

    public static DeckCompanionHost StartNew(int? port = null)
    {
        lock (Sync)
        {
            _current?.Dispose();
            var host = new DeckCompanionHost();
            host.Start(port ?? GetConfiguredPort());
            _current = host;
            return host;
        }
    }

    public void Start(int port)
    {
        if (IsRunning)
            return;

        Port = port;
        Pin = EnsurePin();
        AppSettingsService.SetSetting(SettingPort, port.ToString());

        TryOpenFirewallRule(port);

        var lanIps = GetLanIPv4Addresses();
        Exception? last = null;
        TcpListener? listener = null;

        try
        {
            // Escuta em todas as interfaces — celular alcança pelo IP do cabo
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            _listener = listener;
        }
        catch (Exception ex)
        {
            last = ex;
            try { listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
            throw new InvalidOperationException(
                "Não foi possível abrir a porta " + port +
                ". Feche outro programa na porta, escolha outra (ex.: 5055) ou clique em Firewall." +
                "\n" + last.Message);
        }

        var urls = new List<string>();
        foreach (var ip in lanIps)
            urls.Add($"http://{ip}:{port}/");
        urls.Add($"http://127.0.0.1:{port}/");

        Urls = urls;
        LanUrl = lanIps.Count > 0 ? $"http://{lanIps[0]}:{port}/" : null;
        IsRunning = true;
        _loop = Task.Run(() => ListenLoop(_cts.Token));
    }

    /// <summary>Abre regra de entrada TCP no Firewall do Windows para o companion.</summary>
    public static bool TryOpenFirewallRule(int port)
    {
        try
        {
            var name = "SGDB Deck Celular";
            RunHidden("netsh", $"advfirewall firewall delete rule name=\"{name}\"");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments =
                    $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port} profile=private,domain,public",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(8000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Libera firewall pedindo admin (UAC). Usar no botão Firewall.</summary>
    public static bool TryOpenFirewallElevated(int port)
    {
        try
        {
            var name = "SGDB Deck Celular";
            RunHidden("netsh", $"advfirewall firewall delete rule name=\"{name}\"");
            var args =
                $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port} profile=private,domain,public";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(15000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunHidden(string file, string args)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            p?.WaitForExit(5000);
        }
        catch { /* ignore */ }
    }

    public void Stop()
    {
        if (!IsRunning && _listener is null)
            return;
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener = null;
        IsRunning = false;
        lock (Sync)
        {
            if (ReferenceEquals(_current, this))
                _current = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        var listener = _listener;
        if (listener is null) return;

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleClient(client), CancellationToken.None);
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 15000;
                using var stream = client.GetStream();
                var exchange = ReadHttp(stream);
                if (exchange is null) return;
                Handle(exchange);
                WriteHttp(stream, exchange);
            }
            catch
            {
                /* ignore connection errors */
            }
        }
    }

    private void Handle(HttpExchange ex)
    {
        try
        {
            if (ex.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                ex.Status = 204;
                return;
            }

            var path = ex.Path.TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/";

            if (path is "/" or "/index.html")
            {
                WriteHtml(ex, DeckCompanionHtml.Page);
                return;
            }

            if (path.Equals("/manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
            {
                WriteManifest(ex);
                return;
            }

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                HandleApi(ex, path);
                return;
            }

            WriteJson(ex, 404, new { error = "Não encontrado." });
        }
        catch (Exception e)
        {
            try { WriteJson(ex, 500, new { error = e.Message }); }
            catch { /* ignore */ }
        }
    }

    private void HandleApi(HttpExchange ex, string path)
    {
        if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
        {
            WriteJson(ex, 200, new
            {
                ok = true,
                store = AppSettingsService.GetNomeDeposito(),
                running = IsRunning,
            });
            return;
        }

        if (path.Equals("/api/login", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
        {
            var body = ReadJson(ex.Body);
            var pin = body.GetString("pin") ?? "";
            if (!SecureEquals(pin, Pin))
            {
                WriteJson(ex, 401, new { error = "PIN incorreto." });
                return;
            }
            WriteJson(ex, 200, new { ok = true, store = AppSettingsService.GetNomeDeposito() });
            return;
        }

        if (!IsAuthorized(ex))
        {
            WriteJson(ex, 401, new { error = "Informe o PIN." });
            return;
        }

        try
        {
            if (path.Equals("/api/decks", StringComparison.OrdinalIgnoreCase))
            {
                if (ex.Method == "GET")
                {
                    var open = OpenTabService.ListOpen();
                    var tableCount = 24;
                    if (int.TryParse(AppSettingsService.GetSetting("decks_table_count"), out var n)
                        && n is >= 1 and <= 80)
                        tableCount = n;

                    var map = DeckTableHelper.BuildCards(open, tableCount);
                    var balcao = DeckTableHelper.BuildBalcaoCards(open, tableCount);

                    WriteJson(ex, 200, new
                    {
                        ok = true,
                        tableCount,
                        occupied = map.Count(c => !c.IsFree),
                        free = map.Count(c => c.IsFree),
                        map = map.Select(MapCard),
                        balcao = balcao.Select(MapCard),
                        decks = open.Select(d => new
                        {
                            id = d.Id,
                            name = d.Name,
                            items = d.ItemsCount,
                            total = d.Total,
                            totalDisplay = d.TotalDisplay,
                            createdAt = d.CreatedAtBr,
                            notes = d.Notes,
                            preconta = d.HasPreConta,
                        }),
                    });
                    return;
                }

                if (ex.Method == "POST")
                {
                    var body = ReadJson(ex.Body);
                    var name = body.GetString("name") ?? "";
                    var notes = body.GetString("notes");
                    var id = OpenTabService.Create(name, notes: notes);
                    WriteJson(ex, 200, new { ok = true, deck = MapDetail(OpenTabService.Get(id)) });
                    return;
                }
            }

            if (path.StartsWith("/api/decks/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = path["/api/decks/".Length..];
                var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0], out var tabId))
                {
                    if (parts.Length == 1 && ex.Method == "GET")
                    {
                        WriteJson(ex, 200, new { ok = true, deck = MapDetail(OpenTabService.Get(tabId)) });
                        return;
                    }

                    if (parts.Length == 2
                        && parts[1].Equals("items", StringComparison.OrdinalIgnoreCase)
                        && ex.Method == "POST")
                    {
                        var body = ReadJson(ex.Body);
                        var qty = body.GetDouble("qty", 1);
                        if (qty <= 0) qty = 1;

                        OpenTabItemRow item;
                        if (body.TryGetInt("productId", out var productId) && productId > 0)
                        {
                            // unitPrice / stockUnitsPerSale do body são ignorados de propósito.
                            var mode = body.GetString("mode");
                            item = DeckCompanionSaleHelper.AddByProductId(tabId, productId, qty, mode);
                        }
                        else
                        {
                            var term = body.GetString("term") ?? "";
                            if (string.IsNullOrWhiteSpace(term))
                                throw new OpenTabException("Informe o produto.");
                            item = OpenTabService.AddFromScan(tabId, term, qty);
                        }

                        WriteJson(ex, 200, new
                        {
                            ok = true,
                            item = MapItem(item),
                            deck = MapDetail(OpenTabService.Get(tabId)),
                        });
                        return;
                    }

                    // /api/decks/{tabId}/preconta
                    if (parts.Length == 2
                        && parts[1].Equals("preconta", StringComparison.OrdinalIgnoreCase)
                        && ex.Method == "POST")
                    {
                        OpenTabService.RequestPreConta(tabId);
                        WriteJson(ex, 200, new { ok = true, deck = MapDetail(OpenTabService.Get(tabId)) });
                        return;
                    }

                    // /api/decks/{tabId}/items/{itemId}
                    if (parts.Length == 3
                        && parts[1].Equals("items", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(parts[2], out var itemId))
                    {
                        EnsureItemOnDeck(tabId, itemId);

                        if (ex.Method == "DELETE"
                            || (ex.Method == "POST" && string.Equals(
                                ReadJson(ex.Body).GetString("action"), "delete",
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            OpenTabService.RemoveItem(itemId);
                            WriteJson(ex, 200, new { ok = true, deck = MapDetail(OpenTabService.Get(tabId)) });
                            return;
                        }

                        if (ex.Method is "PUT" or "PATCH" or "POST")
                        {
                            var body = ReadJson(ex.Body);
                            var qty = body.GetDouble("qty", -1);
                            if (qty < 0)
                                throw new OpenTabException("Informe a quantidade (qty).");
                            OpenTabService.SetItemQuantity(itemId, qty);
                            WriteJson(ex, 200, new { ok = true, deck = MapDetail(OpenTabService.Get(tabId)) });
                            return;
                        }
                    }
                }
            }

            if (path.Equals("/api/products", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
            {
                var q = ex.Query["q"] ?? "";
                var products = PdvService.SearchProducts(q, 25)
                    .Select(DeckCompanionSaleHelper.MapProductForApi);
                WriteJson(ex, 200, new { ok = true, products });
                return;
            }

            WriteJson(ex, 404, new { error = "API não encontrada." });
        }
        catch (OpenTabException e)
        {
            WriteJson(ex, 400, new { error = e.Message });
        }
        catch (Exception e)
        {
            WriteJson(ex, 500, new { error = e.Message });
        }
    }

    private bool IsAuthorized(HttpExchange ex)
    {
        var pin = ex.Headers["X-Deck-Pin"] ?? ex.Query["pin"] ?? "";
        return SecureEquals(pin, Pin);
    }

    private static void EnsureItemOnDeck(int tabId, int itemId)
    {
        var detail = OpenTabService.Get(tabId);
        if (!detail.IsOpen)
            throw new OpenTabException("Este deck não está aberto.");
        if (detail.Items.All(i => i.Id != itemId))
            throw new OpenTabException("Item não encontrado neste deck.");
    }

    private static object MapCard(DeckTableCard c) => new
    {
        tableNumber = c.TableNumber,
        number = c.NumberDisplay,
        free = c.IsFree,
        preconta = c.IsPreConta,
        balcao = c.IsBalcao,
        id = c.Tab?.Id,
        name = c.ClientNameDisplay,
        title = c.IsBalcao ? (c.Tab?.Name ?? "Avulso") : c.NumberDisplay,
        footer = c.FooterLine,
        totalDisplay = c.TotalDisplay,
        elapsed = c.ElapsedShort,
    };

    private static object MapDetail(OpenTabDetail d) => new
    {
        id = d.Id,
        name = d.Name,
        status = d.Status,
        preconta = d.HasPreConta,
        total = d.Total,
        totalDisplay = ProductPriceHelper.MoneyBr(d.Total),
        createdAt = d.CreatedAt,
        items = d.Items.Select(MapItem),
    };

    private static object MapItem(OpenTabItemRow i) => new
    {
        id = i.Id,
        productId = i.ProductId,
        code = i.ProductCode,
        name = i.ProductName,
        unit = i.Unit,
        qty = i.Quantity,
        price = i.UnitPrice,
        subtotal = i.Subtotal,
        qtyDisplay = i.QuantityDisplay,
        priceDisplay = i.UnitPriceDisplay,
        subtotalDisplay = i.SubtotalDisplay,
    };

    private static JsonBag ReadJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new JsonBag(null);
        try
        {
            return new JsonBag(JsonDocument.Parse(text).RootElement.Clone());
        }
        catch
        {
            return new JsonBag(null);
        }
    }

    private static void WriteJson(HttpExchange ex, int status, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        ex.Status = status;
        ex.ContentType = "application/json; charset=utf-8";
        ex.ResponseBody = Encoding.UTF8.GetBytes(json);
    }

    private static void WriteHtml(HttpExchange ex, string html)
    {
        ex.Status = 200;
        ex.ContentType = "text/html; charset=utf-8";
        ex.ResponseBody = Encoding.UTF8.GetBytes(html);
    }

    private static void WriteManifest(HttpExchange ex)
    {
        const string json = """
            {
              "name": "SGDB Decks",
              "short_name": "Decks",
              "start_url": "/",
              "display": "standalone",
              "background_color": "#0f172a",
              "theme_color": "#0f172a",
              "description": "Comandas SGDB pelo celular"
            }
            """;
        ex.Status = 200;
        ex.ContentType = "application/manifest+json; charset=utf-8";
        ex.ResponseBody = Encoding.UTF8.GetBytes(json);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static bool SecureEquals(string a, string b)
    {
        a ??= "";
        b ??= "";
        if (a.Length != b.Length)
            return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    /// <summary>
    /// IPs privados da LAN (192.168 / 10 / 172.16-31), priorizando Ethernet (cabo).
    /// Ignora VPN pública, APIPA e loopback.
    /// </summary>
    public static IReadOnlyList<string> GetLanIPv4Addresses()
    {
        var scored = new List<(string Ip, int Score)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                var typeScore = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => 100,
                    NetworkInterfaceType.GigabitEthernet => 100,
                    NetworkInterfaceType.FastEthernetT => 100,
                    NetworkInterfaceType.Wireless80211 => 50,
                    _ => 10,
                };

                // Preferir nome com Ethernet/Cabo
                var name = ni.Name + " " + ni.Description;
                if (name.Contains("Ethernet", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Cabo", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Local Area", StringComparison.OrdinalIgnoreCase))
                    typeScore += 20;
                if (name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Topaz", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("VPN", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("TAP", StringComparison.OrdinalIgnoreCase))
                    typeScore -= 80;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    var ip = ua.Address.ToString();
                    if (ip.StartsWith("127.") || ip.StartsWith("169.254."))
                        continue;
                    if (!IsPrivateIpv4(ip))
                        continue; // ignora IP público de VPN/adaptador especial

                    scored.Add((ip, typeScore));
                }
            }
        }
        catch { /* ignore */ }

        return scored
            .OrderByDescending(x => x.Score)
            .Select(x => x.Ip)
            .Distinct()
            .ToList();
    }

    private static bool IsPrivateIpv4(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b))
            return false;
        if (a == 10) return true;
        if (a == 192 && b == 168) return true;
        if (a == 172 && b is >= 16 and <= 31) return true;
        return false;
    }

    private static HttpExchange? ReadHttp(NetworkStream stream)
    {
        using var ms = new MemoryStream();
        var buf = new byte[4096];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) return null;
            ms.Write(buf, 0, n);
            var bytes = ms.ToArray();
            headerEnd = IndexOfHeaderEnd(bytes);
            if (ms.Length > 1024 * 1024) return null;
        }

        var all = ms.ToArray();
        var headerBytes = all.AsSpan(0, headerEnd);
        var headerText = Encoding.ASCII.GetString(headerBytes);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0) return null;

        var parts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var method = parts[0].ToUpperInvariant();
        var target = parts[1];
        var path = target;
        var query = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
        var qIdx = target.IndexOf('?');
        if (qIdx >= 0)
        {
            path = target[..qIdx];
            var qs = target[(qIdx + 1)..];
            foreach (var pair in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                    query[Uri.UnescapeDataString(pair.Replace('+', ' '))] = "";
                else
                {
                    var k = Uri.UnescapeDataString(pair[..eq].Replace('+', ' '));
                    var v = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                    query[k] = v;
                }
            }
        }

        var headers = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line)) break;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var contentLength = 0;
        if (int.TryParse(headers["Content-Length"], out var cl) && cl > 0)
            contentLength = Math.Min(cl, 2 * 1024 * 1024);

        var bodyStart = headerEnd + 4;
        var bodyMs = new MemoryStream();
        if (bodyStart < all.Length)
            bodyMs.Write(all, bodyStart, all.Length - bodyStart);

        while (bodyMs.Length < contentLength)
        {
            var n = stream.Read(buf, 0, Math.Min(buf.Length, contentLength - (int)bodyMs.Length));
            if (n <= 0) break;
            bodyMs.Write(buf, 0, n);
        }

        var bodyText = bodyMs.Length > 0
            ? Encoding.UTF8.GetString(bodyMs.ToArray())
            : null;

        return new HttpExchange
        {
            Method = method,
            Path = path,
            Query = query,
            Headers = headers,
            Body = bodyText,
        };
    }

    private static int IndexOfHeaderEnd(byte[] bytes)
    {
        for (var i = 0; i + 3 < bytes.Length; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                return i;
        }
        return -1;
    }

    private static void WriteHttp(NetworkStream stream, HttpExchange ex)
    {
        var body = ex.ResponseBody ?? Array.Empty<byte>();
        var statusText = ex.Status switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "OK",
        };

        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(ex.Status).Append(' ').Append(statusText).Append("\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("Access-Control-Allow-Headers: Content-Type, X-Deck-Pin\r\n");
        sb.Append("Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS\r\n");
        sb.Append("Connection: close\r\n");
        if (ex.Status != 204)
        {
            sb.Append("Content-Type: ").Append(ex.ContentType ?? "text/plain; charset=utf-8").Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        }
        else
        {
            sb.Append("Content-Length: 0\r\n");
        }
        sb.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(headerBytes, 0, headerBytes.Length);
        if (ex.Status != 204 && body.Length > 0)
            stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    private sealed class HttpExchange
    {
        public string Method { get; set; } = "GET";
        public string Path { get; set; } = "/";
        public NameValueCollection Query { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public NameValueCollection Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Body { get; set; }
        public int Status { get; set; } = 200;
        public string? ContentType { get; set; }
        public byte[]? ResponseBody { get; set; }
    }

    private readonly struct JsonBag(JsonElement? el)
    {
        public string? GetString(string name)
        {
            if (el is not { } e || e.ValueKind != JsonValueKind.Object) return null;
            if (!e.TryGetProperty(name, out var p)) return null;
            return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
        }

        public double GetDouble(string name, double fallback = 0)
        {
            if (el is not { } e || e.ValueKind != JsonValueKind.Object) return fallback;
            if (!e.TryGetProperty(name, out var p)) return fallback;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
            if (p.ValueKind == JsonValueKind.String
                && double.TryParse(p.GetString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out d))
                return d;
            return fallback;
        }

        public bool TryGetInt(string name, out int value)
        {
            value = 0;
            if (el is not { } e || e.ValueKind != JsonValueKind.Object) return false;
            if (!e.TryGetProperty(name, out var p)) return false;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value)) return true;
            return p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value);
        }
    }
}
