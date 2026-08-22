using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Servidor HTTP no PC da loja — notebook acessa produtos/compras/estoque no mesmo banco.
/// </summary>
public sealed class StoreNetworkHost : IDisposable
{
    private static readonly object Sync = new();
    private static StoreNetworkHost? _current;

    private TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private bool _disposed;
    private X509Certificate2? _serverCertificate;

    public static StoreNetworkHost? Current
    {
        get { lock (Sync) return _current; }
    }

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public string Pin { get; set; } = "";
    public string? LanUrl { get; private set; }
    public string LocalUrl => $"https://127.0.0.1:{Port}/";
    public IReadOnlyList<string> Urls { get; private set; } = [];
    public string? CertificateFingerprint { get; private set; }
    public DateTime? CertificateNotAfter { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Capabilities anunciadas em GET /api/status (apiVersion permanece 2).</summary>
    internal static readonly string[] AdvertisedFeatures =
    [
        "movimentacao",
        "pdv_resumo",
        "dashboard",
        "stock_report",
        "pairing",
        "session",
        PurchaseSalePriceRules.AtomicFeature,
        PurchaseAverageCostRules.AtomicFeature,
    ];

    public static StoreNetworkHost StartNew(int? port = null)
    {
        lock (Sync)
        {
            _current?.Dispose();
            var host = new StoreNetworkHost();
            host.Start(port ?? StoreNetworkMode.GetPort());
            _current = host;
            return host;
        }
    }

    public void Start(int port)
    {
        if (IsRunning) return;

        var cert = StoreNetworkCertificateService.LoadOrCreate(AppSettingsService.GetNomeDeposito());
        _serverCertificate?.Dispose();
        _serverCertificate = cert;
        CertificateFingerprint = StoreNetworkCertificateService.ComputeFingerprint(cert);
        CertificateNotAfter = cert.NotAfter;

        Pin = StoreNetworkMode.EnsurePin();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleServer);
        if (port is >= 1024 and <= 65535)
        {
            StoreNetworkMode.SavePort(port);
            TryOpenFirewallRule(port);
        }

        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Não foi possível abrir a porta " + port +
                ". Escolha outra (ex.: 5056) ou clique em Firewall.\n" + ex.Message);
        }

        var lanIps = DeckCompanionHost.GetLanIPv4Addresses();
        var urls = lanIps.Select(ip => $"https://{ip}:{Port}/").ToList();
        urls.Add($"https://127.0.0.1:{Port}/");
        Urls = urls;
        LanUrl = lanIps.Count > 0 ? $"https://{lanIps[0]}:{Port}/" : null;
        IsRunning = true;
        StoreNetworkSessionService.EnsureDeviceRevokedSubscription();
        AuditService.Log("rede_servidor_ligar", "store_network", Port.ToString(),
            $"TLS · fingerprint={CertificateFingerprint} · URLs: {string.Join(", ", urls.Where(u => !u.Contains("127.0.0.1")))}");
        _loop = Task.Run(() => ListenLoop(_cts.Token));
    }

    public static bool TryOpenFirewallRule(int port)
    {
        try
        {
            const string name = "SGDB Rede Loja";
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
        catch { return false; }
    }

    public static bool TryOpenFirewallElevated(int port)
    {
        try
        {
            const string name = "SGDB Rede Loja";
            RunHidden("netsh", $"advfirewall firewall delete rule name=\"{name}\"");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments =
                    $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port} profile=private,domain,public",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(15000);
            return p.ExitCode == 0;
        }
        catch { return false; }
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
        if (!IsRunning && _listener is null) return;
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener = null;
        IsRunning = false;
        StoreNetworkPairingService.ClearActiveCode();
        StoreNetworkSessionService.ClearAll();
        _serverCertificate?.Dispose();
        _serverCertificate = null;
        CertificateFingerprint = null;
        CertificateNotAfter = null;
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
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            catch { continue; }
            _ = Task.Run(() => HandleClient(client), CancellationToken.None);
        }
    }

    private void HandleClient(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 60000;
                client.SendTimeout = 60000;
                var cert = _serverCertificate;
                if (cert is null)
                    return;
                using var network = client.GetStream();
                using var ssl = new SslStream(network, leaveInnerStreamOpen: false);
                try
                {
                    ssl.AuthenticateAsServer(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = cert,
                        ClientCertificateRequired = false,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    });
                }
                catch (Exception tlsEx)
                {
                    LogTlsFailure(tlsEx);
                    return;
                }

                var ex = ReadHttp(ssl);
                if (ex is null) return;
                ex.RemoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
                Handle(ex);
                WriteHttp(ssl, ex);
            }
            catch { /* ignore */ }
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
            if (string.IsNullOrEmpty(path)) path = "/";

            if (path is "/" or "/index.html")
            {
                WriteJson(ex, 200, new
                {
                    ok = true,
                    service = "SGDB Rede Loja",
                    store = AppSettingsService.GetNomeDeposito(),
                    hint = "Use o SGDB no notebook em modo Cliente.",
                });
                return;
            }

            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                WriteJson(ex, 404, new { error = "Não encontrado." });
                return;
            }

            HandleApi(ex, path);
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
            if (!IsAuthorized(ex))
            {
                WriteJson(ex, 401, new { error = "Informe o PIN." });
                return;
            }
            WriteJson(ex, 200, new
            {
                ok = true,
                store = AppSettingsService.GetNomeDeposito(),
                role = "server",
                running = IsRunning,
                apiVersion = 2,
                authModes = new[] { "pin", "pairing", "session" },
                features = AdvertisedFeatures,
            });
            return;
        }

        if (path.Equals("/api/login", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
        {
            var body = ReadJson(ex.Body);
            var pin = (body.GetString("pin")
                       ?? ex.Headers["X-Store-Pin"]
                       ?? "").Trim();
            var expected = StoreNetworkMode.GetServerPin();
            Pin = expected; // sincroniza memória com o PIN salvo
            if (pin.Length == 0 || !SecureEquals(pin, expected))
            {
                WriteJson(ex, 401, new
                {
                    ok = false,
                    error = "PIN incorreto. No PC da loja, aba Servidor, confira o PIN e clique Salvar PIN. Depois Ligar de novo.",
                });
                return;
            }
            WriteJson(ex, 200, new { ok = true, store = AppSettingsService.GetNomeDeposito(), role = "server" });
            return;
        }

        if (path.Equals("/api/pair", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
        {
            HandlePair(ex);
            return;
        }

        if (path.Equals("/api/pair/status", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
        {
            HandlePairStatus(ex);
            return;
        }

        if (path.Equals("/api/session", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
        {
            HandleSessionLogin(ex);
            return;
        }

        if (path.Equals("/api/logout", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
        {
            HandleSessionLogout(ex);
            return;
        }

        StoreNetworkRemoteSession? remoteSession = null;
        var bearer = ReadBearerToken(ex);
        if (!string.IsNullOrEmpty(bearer))
        {
            var resolved = StoreNetworkSessionService.TryResolve(bearer);
            if (!resolved.Ok || resolved.Session is null)
            {
                WriteJson(ex, 401, new { error = StoreNetworkSessionService.SessionInvalidMessage });
                return;
            }
            remoteSession = resolved.Session;
        }
        else if (!IsAuthorized(ex))
        {
            WriteJson(ex, 401, new { error = "Informe o PIN." });
            return;
        }

        var origin = ex.Headers["X-Store-Origin"] ?? "rede";

        // 68C: PIN não carrega o usuário do notebook. Bearer preenche RemoteSession
        // sem tocar AppSession do PC servidor.
        using var remoteScope = AccessControl.EnterRemoteStoreRequest(remoteSession);
        try
        {
            if (path.Equals("/api/products", StringComparison.OrdinalIgnoreCase))
            {
                if (ex.Method == "GET")
                {
                    var list = ProductService.ListLocal(
                        ex.Query["search"],
                        ex.Query["ativo"] ?? "ativos",
                        ex.Query["group"],
                        ex.Query["dateFrom"],
                        ex.Query["dateTo"],
                        ex.Query["dateMode"] ?? "none");
                    WriteJson(ex, 200, new { ok = true, items = list });
                    return;
                }

                if (ex.Method == "POST")
                {
                    var input = JsonSerializer.Deserialize<ProductInput>(ex.Body ?? "{}", JsonOpts)
                        ?? throw new InvalidOperationException("Dados inválidos.");
                    var created = ProductService.CreateLocal(input);
                    AuditService.Log("rede_produto_criar", "product", created.Id.ToString(),
                        $"origem={origin}; {created.Name}");
                    WriteJson(ex, 200, new { ok = true, item = created });
                    return;
                }
            }

            if (path.StartsWith("/api/products/by-barcode", StringComparison.OrdinalIgnoreCase)
                && ex.Method == "GET")
            {
                var q = ex.Query["q"] ?? "";
                var item = ProductService.FindByBarcodeOrPackLocal(q);
                WriteJson(ex, 200, new { ok = true, item });
                return;
            }

            if (path.Equals("/api/products/merge", StringComparison.OrdinalIgnoreCase)
                && ex.Method == "POST")
            {
                var body = ReadJson(ex.Body);
                if (!body.TryGetInt("keepId", out var keepId) || keepId <= 0)
                    throw new InvalidOperationException("keepId obrigatório.");
                if (!body.TryGetInt("absorbId", out var absorbId) || absorbId <= 0)
                    throw new InvalidOperationException("absorbId obrigatório.");
                var merged = ProductService.MergeProductsLocal(keepId, absorbId);
                AuditService.Log("rede_produto_unificar", "product", keepId.ToString(),
                    $"origem={origin}; absorb=#{absorbId} → keep=#{keepId}");
                WriteJson(ex, 200, new { ok = true, item = merged });
                return;
            }

            if (path.StartsWith("/api/products/", StringComparison.OrdinalIgnoreCase))
            {
                var idPart = path["/api/products/".Length..];
                if (int.TryParse(idPart, out var pid))
                {
                    if (ex.Method == "GET")
                    {
                        WriteJson(ex, 200, new { ok = true, item = ProductService.GetByIdLocal(pid) });
                        return;
                    }
                    if (ex.Method == "PUT")
                    {
                        var input = JsonSerializer.Deserialize<ProductInput>(ex.Body ?? "{}", JsonOpts)
                            ?? throw new InvalidOperationException("Dados inválidos.");
                        var updated = ProductService.UpdateLocal(pid, input);
                        AuditService.Log("rede_produto_atualizar", "product", pid.ToString(),
                            $"origem={origin}; {updated.Name}");
                        WriteJson(ex, 200, new { ok = true, item = updated });
                        return;
                    }
                    if (ex.Method == "DELETE")
                    {
                        ProductService.SoftDeleteLocal(pid);
                        AuditService.Log("rede_produto_excluir", "product", pid.ToString(), $"origem={origin}");
                        WriteJson(ex, 200, new { ok = true });
                        return;
                    }
                }
            }

            if (path.Equals("/api/stock/adjust", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
            {
                var body = ReadJson(ex.Body);
                if (!body.TryGetInt("productId", out var productId))
                    throw new InvalidOperationException("productId obrigatório.");
                var modeRaw = body.GetString("mode") ?? "Entrada";
                if (!Enum.TryParse<StockAdjustMode>(modeRaw, true, out var mode))
                    mode = StockAdjustMode.Entrada;
                double? qty = null;
                double? novo = null;
                double? unitCost = null;
                if (body.TryGetDouble("quantity", out var qv)) qty = qv;
                if (body.TryGetDouble("newStock", out var nv)) novo = nv;
                if (body.TryGetDouble("unitCost", out var uc)) unitCost = uc;
                var notes = body.GetString("notes");
                var result = StockService.AdjustLocal(productId, mode, qty, novo,
                    string.IsNullOrWhiteSpace(notes) ? $"Ajuste via rede ({origin})" : $"{notes} [rede:{origin}]",
                    unitCost);
                AuditService.Log("rede_estoque_ajuste", "stock", productId.ToString(),
                    $"origem={origin}; mode={mode}; q={result.Quantity}");
                WriteJson(ex, 200, result);
                return;
            }

            if (path.Equals("/api/stock/adjust-fridge", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
            {
                var body = ReadJson(ex.Body);
                if (!body.TryGetInt("productId", out var fridgeProductId))
                    throw new InvalidOperationException("productId obrigatório.");
                var fridgeModeRaw = body.GetString("mode") ?? "Entrada";
                if (!Enum.TryParse<StockAdjustMode>(fridgeModeRaw, true, out var fridgeMode))
                    fridgeMode = StockAdjustMode.Entrada;
                double? fridgeQty = null;
                double? fridgeNovo = null;
                if (body.TryGetDouble("quantity", out var fq)) fridgeQty = fq;
                if (body.TryGetDouble("newStock", out var fn)) fridgeNovo = fn;
                var fridgeNotes = body.GetString("notes");
                var fridgeResult = StockService.AdjustFridgeLocal(
                    fridgeProductId, fridgeMode, fridgeQty, fridgeNovo,
                    string.IsNullOrWhiteSpace(fridgeNotes)
                        ? null
                        : $"{fridgeNotes.Trim()} [rede:{origin}]");
                AuditService.Log("rede_estoque_ajuste_geladeira", "stock", fridgeProductId.ToString(),
                    $"origem={origin}; mode={fridgeMode}; q={fridgeResult.Quantity}");
                WriteJson(ex, 200, fridgeResult);
                return;
            }

            if (path.Equals("/api/purchases", StringComparison.OrdinalIgnoreCase))
            {
                if (ex.Method == "GET")
                {
                    var list = PurchaseService.ListLocal(
                        ex.Query["search"],
                        ex.Query["status"] ?? "todas",
                        ex.Query["dateFrom"],
                        ex.Query["dateTo"]);
                    WriteJson(ex, 200, new { ok = true, items = list });
                    return;
                }

                if (ex.Method == "POST")
                {
                    using var doc = JsonDocument.Parse(ex.Body ?? "{}");
                    var root = doc.RootElement;
                    var inputEl = root.TryGetProperty("input", out var i) ? i : root;
                    var input = JsonSerializer.Deserialize<PurchaseInput>(inputEl.GetRawText(), JsonOpts)
                        ?? throw new InvalidOperationException("Dados inválidos.");
                    var close = root.TryGetProperty("closeOnSave", out var c) && c.ValueKind == JsonValueKind.True;
                    var id = PurchaseService.CreateLocal(input, close);
                    AuditService.Log("rede_compra_criar", "purchase", id.ToString(),
                        $"origem={origin}; close={close}");
                    var saleUpdates = close
                        ? PurchaseSalePriceRules.CountRequestedSaleUpdates(input.Items)
                        : 0;
                    var costUpdates = close
                        ? PurchaseAverageCostRules.CountAppliedProductUpdates(input)
                        : 0;
                    WriteJson(ex, 200, new { ok = true, id, salePriceUpdates = saleUpdates, averageCostUpdates = costUpdates });
                    return;
                }
            }

            if (path.Equals("/api/purchases/nfe-exists", StringComparison.OrdinalIgnoreCase)
                && ex.Method == "GET")
            {
                var key = ex.Query["key"] ?? "";
                WriteJson(ex, 200, new { exists = PurchaseService.NfeKeyExistsLocal(key) });
                return;
            }

            if (path.StartsWith("/api/purchases/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = path["/api/purchases/".Length..];
                var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0], out var purchaseId))
                {
                    if (parts.Length == 1 && ex.Method == "GET")
                    {
                        WriteJson(ex, 200, new { ok = true, item = PurchaseService.GetByIdLocal(purchaseId) });
                        return;
                    }
                    if (parts.Length == 1 && ex.Method == "PUT")
                    {
                        using var doc = JsonDocument.Parse(ex.Body ?? "{}");
                        var root = doc.RootElement;
                        var inputEl = root.TryGetProperty("input", out var i) ? i : root;
                        var input = JsonSerializer.Deserialize<PurchaseInput>(inputEl.GetRawText(), JsonOpts)
                            ?? throw new InvalidOperationException("Dados inválidos.");
                        var close = root.TryGetProperty("closeOnSave", out var c) && c.ValueKind == JsonValueKind.True;
                        PurchaseService.UpdateLocal(purchaseId, input, close);
                        AuditService.Log("rede_compra_atualizar", "purchase", purchaseId.ToString(),
                            $"origem={origin}; close={close}");
                        var saleUpdates = close
                            ? PurchaseSalePriceRules.CountRequestedSaleUpdates(input.Items)
                            : 0;
                        var costUpdates = close
                            ? PurchaseAverageCostRules.CountAppliedProductUpdates(input)
                            : 0;
                        WriteJson(ex, 200, new { ok = true, salePriceUpdates = saleUpdates, averageCostUpdates = costUpdates });
                        return;
                    }
                    if (parts.Length == 1 && ex.Method == "DELETE")
                    {
                        PurchaseService.DeleteLocal(purchaseId);
                        AuditService.Log("rede_compra_excluir", "purchase", purchaseId.ToString(), $"origem={origin}");
                        WriteJson(ex, 200, new { ok = true });
                        return;
                    }
                    if (parts.Length == 2 && parts[1].Equals("cancel", StringComparison.OrdinalIgnoreCase)
                        && ex.Method == "POST")
                    {
                        PurchaseService.CancelLocal(purchaseId);
                        AuditService.Log("rede_compra_cancelar", "purchase", purchaseId.ToString(), $"origem={origin}");
                        WriteJson(ex, 200, new { ok = true });
                        return;
                    }
                    if (parts.Length == 2 && parts[1].Equals("reopen", StringComparison.OrdinalIgnoreCase)
                        && ex.Method == "POST")
                    {
                        PurchaseService.ReopenLocal(purchaseId);
                        AuditService.Log("rede_compra_reabrir", "purchase", purchaseId.ToString(), $"origem={origin}");
                        WriteJson(ex, 200, new { ok = true });
                        return;
                    }
                }
            }

            if (path.Equals("/api/people", StringComparison.OrdinalIgnoreCase))
            {
                if (ex.Method == "GET")
                {
                    var list = PersonService.ListLocal(
                        ex.Query["search"],
                        ex.Query["ativo"] ?? "ativos",
                        ex.Query["tipo"] ?? "fornecedores");
                    WriteJson(ex, 200, new { ok = true, items = list });
                    return;
                }

                if (ex.Method == "POST")
                {
                    using var doc = JsonDocument.Parse(ex.Body ?? "{}");
                    var root = doc.RootElement;
                    var inputEl = root.TryGetProperty("input", out var i) ? i : root;
                    var input = JsonSerializer.Deserialize<PersonInput>(inputEl.GetRawText(), JsonOpts)
                        ?? throw new InvalidOperationException("Dados inválidos.");
                    var requireCliente = !root.TryGetProperty("requireClienteRole", out var rc)
                                        || rc.ValueKind != JsonValueKind.False;
                    var created = PersonService.CreateLocal(input, requireCliente);
                    AuditService.Log("rede_pessoa_criar", "person", created.Id.ToString(),
                        $"origem={origin}; {created.Name}");
                    WriteJson(ex, 200, new { ok = true, item = created });
                    return;
                }
            }

            if (path.Equals("/api/people/by-doc", StringComparison.OrdinalIgnoreCase)
                && ex.Method == "GET")
            {
                var item = PersonService.FindByCnpjDigitsLocal(ex.Query["q"]);
                WriteJson(ex, 200, new { ok = true, item });
                return;
            }

            if (path.StartsWith("/api/people/", StringComparison.OrdinalIgnoreCase))
            {
                var idPart = path["/api/people/".Length..];
                if (int.TryParse(idPart, out var personId))
                {
                    if (ex.Method == "GET")
                    {
                        WriteJson(ex, 200, new { ok = true, item = PersonService.GetByIdLocal(personId) });
                        return;
                    }

                    if (ex.Method == "PUT")
                    {
                        using var doc = JsonDocument.Parse(ex.Body ?? "{}");
                        var root = doc.RootElement;
                        var inputEl = root.TryGetProperty("input", out var i) ? i : root;
                        var input = JsonSerializer.Deserialize<PersonInput>(inputEl.GetRawText(), JsonOpts)
                            ?? throw new InvalidOperationException("Dados inválidos.");
                        var requireCliente = !root.TryGetProperty("requireClienteRole", out var rc)
                                            || rc.ValueKind != JsonValueKind.False;
                        var updated = PersonService.UpdateLocal(personId, input, requireCliente);
                        AuditService.Log("rede_pessoa_atualizar", "person", personId.ToString(),
                            $"origem={origin}; {updated.Name}");
                        WriteJson(ex, 200, new { ok = true, item = updated });
                        return;
                    }
                }
            }

            if (path.Equals("/api/vasilhame", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
            {
                var somenteDevedor = (ex.Query["somenteDevedor"] ?? "1") is not ("0" or "false" or "False");
                var somenteVencido = (ex.Query["somenteVencido"] ?? "0") is "1" or "true" or "True";
                var from = ParseDate(ex.Query["from"]);
                var to = ParseDate(ex.Query["to"]);
                var result = VasilhameService.ListLocal(
                    somenteDevedor, somenteVencido, ex.Query["search"], from, to);
                WriteJson(ex, 200, result);
                return;
            }

            if (path.Equals("/api/vasilhame/movements", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
            {
                using var doc = JsonDocument.Parse(ex.Body ?? "{}");
                var root = doc.RootElement;
                var kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "saida" : "saida";
                if (!root.TryGetProperty("containerTypeId", out var tidEl) || !tidEl.TryGetInt32(out var typeId))
                    throw new InvalidOperationException("Tipo de vasilhame obrigatório.");
                var qty = root.TryGetProperty("quantity", out var qEl) && qEl.TryGetDouble(out var qv) ? qv : 0;
                int? customerId = null;
                if (root.TryGetProperty("customerId", out var cidEl) && cidEl.ValueKind == JsonValueKind.Number
                    && cidEl.TryGetInt32(out var cid) && cid > 0)
                    customerId = cid;
                var borrowerName = root.TryGetProperty("borrowerName", out var bn) ? bn.GetString() : null;
                var borrowerPhone = root.TryGetProperty("borrowerPhone", out var bp) ? bp.GetString() : null;
                var notes = root.TryGetProperty("notes", out var n) ? n.GetString() : null;
                DateTime? due = null;
                if (root.TryGetProperty("dueDate", out var dueEl) && dueEl.ValueKind == JsonValueKind.String)
                    due = ParseDate(dueEl.GetString());
                var id = VasilhameService.InsertMovementLocal(
                    kind, typeId, qty, customerId, borrowerName, borrowerPhone, due, notes);
                AuditService.Log("rede_vasilhame_mov", "vasilhame", id.ToString(),
                    $"origem={origin}; {kind}; tipo={typeId}; qtd={qty}");
                WriteJson(ex, 200, new { ok = true, id });
                return;
            }

            if (path.StartsWith("/api/vasilhame/movements/", StringComparison.OrdinalIgnoreCase)
                && ex.Method == "DELETE")
            {
                var idPart = path["/api/vasilhame/movements/".Length..];
                if (int.TryParse(idPart, out var movId))
                {
                    VasilhameService.DeleteMovementLocal(movId);
                    AuditService.Log("rede_vasilhame_excluir", "vasilhame", movId.ToString(), $"origem={origin}");
                    WriteJson(ex, 200, new { ok = true });
                    return;
                }
            }

            if (path.Equals("/api/container-types", StringComparison.OrdinalIgnoreCase))
            {
                if (ex.Method == "GET")
                {
                    var onlyActive = (ex.Query["onlyActive"] ?? "0") is "1" or "true" or "True";
                    WriteJson(ex, 200, new { ok = true, items = ContainerTypesService.ListLocal(onlyActive) });
                    return;
                }

                if (ex.Method == "POST")
                {
                    var input = JsonSerializer.Deserialize<ContainerTypeInput>(ex.Body ?? "{}", JsonOpts)
                        ?? throw new InvalidOperationException("Dados inválidos.");
                    var created = ContainerTypesService.CreateLocal(input);
                    AuditService.Log("rede_vasilhame_tipo_criar", "container_type", created.Id.ToString(),
                        $"origem={origin}; {created.Name}");
                    WriteJson(ex, 200, new { ok = true, item = created });
                    return;
                }
            }

            if (path.StartsWith("/api/container-types/", StringComparison.OrdinalIgnoreCase))
            {
                var idPart = path["/api/container-types/".Length..];
                if (int.TryParse(idPart, out var typeId))
                {
                    if (ex.Method == "GET")
                    {
                        WriteJson(ex, 200, new { ok = true, item = ContainerTypesService.GetByIdLocal(typeId) });
                        return;
                    }

                    if (ex.Method == "PUT")
                    {
                        var input = JsonSerializer.Deserialize<ContainerTypeInput>(ex.Body ?? "{}", JsonOpts)
                            ?? throw new InvalidOperationException("Dados inválidos.");
                        var updated = ContainerTypesService.UpdateLocal(typeId, input);
                        AuditService.Log("rede_vasilhame_tipo_atualizar", "container_type", typeId.ToString(),
                            $"origem={origin}; {updated.Name}");
                        WriteJson(ex, 200, new { ok = true, item = updated });
                        return;
                    }
                }
            }

            if (path.Equals("/api/payables/titles", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
            {
                int? supplierId = int.TryParse(ex.Query["supplierId"], out var sid) ? sid : null;
                int? purchaseId = int.TryParse(ex.Query["purchaseId"], out var pid) ? pid : null;
                var list = PayableService.ListTitlesLocal(
                    ex.Query["situacao"] ?? "pendentes",
                    supplierId,
                    ex.Query["dateFrom"],
                    ex.Query["dateTo"],
                    purchaseId);
                WriteJson(ex, 200, new { ok = true, items = list });
                return;
            }

            if (path.Equals("/api/payables/installments", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
            {
                int? supplierId = int.TryParse(ex.Query["supplierId"], out var sid) ? sid : null;
                int? purchaseId = int.TryParse(ex.Query["purchaseId"], out var pid) ? pid : null;
                var list = PayableService.ListInstallmentsLocal(
                    ex.Query["situacao"] ?? "pendentes",
                    supplierId,
                    ex.Query["dateFrom"],
                    ex.Query["dateTo"],
                    purchaseId);
                WriteJson(ex, 200, new { ok = true, items = list });
                return;
            }

            if (path.StartsWith("/api/payables/titles/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = path["/api/payables/titles/".Length..];
                var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out var titleId)
                    && parts[1].Equals("installments", StringComparison.OrdinalIgnoreCase)
                    && ex.Method == "GET")
                {
                    WriteJson(ex, 200, new
                    {
                        ok = true,
                        items = PayableService.ListInstallmentsOfTitleLocal(titleId),
                    });
                    return;
                }
            }

            if (path.StartsWith("/api/payables/installments/", StringComparison.OrdinalIgnoreCase))
            {
                var rest = path["/api/payables/installments/".Length..];
                var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0], out var instId))
                {
                    if (parts.Length == 1 && ex.Method == "GET")
                    {
                        WriteJson(ex, 200, new
                        {
                            ok = true,
                            item = PayableService.GetInstallmentLocal(instId),
                        });
                        return;
                    }
                    if (parts.Length == 2 && parts[1].Equals("pay", StringComparison.OrdinalIgnoreCase)
                        && ex.Method == "POST")
                    {
                        var input = JsonSerializer.Deserialize<PayablePayInput>(ex.Body ?? "{}", JsonOpts)
                            ?? throw new InvalidOperationException("Dados inválidos.");
                        PayableService.PayInstallmentLocal(instId, input);
                        AuditService.Log("rede_pagar_parcela", "payable", instId.ToString(), $"origem={origin}");
                        WriteJson(ex, 200, new { ok = true });
                        return;
                    }
                    if (parts.Length == 2 && parts[1].Equals("reverse", StringComparison.OrdinalIgnoreCase)
                        && ex.Method == "POST")
                    {
                        PayableService.ReversePaymentLocal(instId);
                        AuditService.Log("rede_estornar_parcela", "payable", instId.ToString(), $"origem={origin}");
                        WriteJson(ex, 200, new { ok = true });
                        return;
                    }
                }
            }

            if (path.StartsWith("/api/payables/purchases/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/ensure", StringComparison.OrdinalIgnoreCase)
                && ex.Method == "POST")
            {
                var mid = path["/api/payables/purchases/".Length..];
                mid = mid[..^("/ensure".Length)];
                mid = mid.TrimEnd('/');
                if (int.TryParse(mid, out var purchaseId))
                {
                    var created = PayableService.EnsurePayablesForClosedPurchaseLocal(purchaseId);
                    WriteJson(ex, 200, new { ok = true, created });
                    return;
                }
            }

            if (path.Equals("/api/movimentacao", StringComparison.OrdinalIgnoreCase)
                && (ex.Method == "GET" || ex.Method == "POST"))
            {
                string? kind;
                DateTime from;
                DateTime to;
                int limit;
                string? payment;

                if (ex.Method == "GET")
                {
                    kind = (ex.Query["kind"] ?? "vendas").Trim().ToLowerInvariant();
                    from = ParseDate(ex.Query["from"]) ?? DateTime.Today;
                    to = ParseDate(ex.Query["to"]) ?? DateTime.Today;
                    if (!int.TryParse(ex.Query["limit"], out limit) || limit <= 0)
                        limit = 500;
                    payment = ex.Query["paymentType"];
                    if (string.IsNullOrWhiteSpace(payment)) payment = null;
                }
                else
                {
                    var body = ReadJson(ex.Body);
                    kind = (body.GetString("kind") ?? "vendas").ToLowerInvariant();
                    // Sem body (bug Expect:100-continue): usa HOJE, não 7 dias
                    from = ParseDate(body.GetString("from")) ?? DateTime.Today;
                    to = ParseDate(body.GetString("to")) ?? DateTime.Today;
                    body.TryGetInt("limit", out limit);
                    if (limit <= 0) limit = 500;
                    payment = body.GetString("paymentType");
                }

                object result = kind switch
                {
                    "produtos" => MovimentacaoService.ListProdutosLocal(from, to, payment, limit),
                    "compras" => MovimentacaoService.ListComprasLocal(from, to, limit),
                    _ => MovimentacaoService.ListVendasLocal(from, to, payment, limit),
                };
                WriteJson(ex, 200, result);
                return;
            }

            if (path.Equals("/api/pdv/resumo", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
            {
                DateTime? day = ParseDate(ex.Query["date"]);
                WriteJson(ex, 200, PdvQueryService.GetResumoDiaLocal(day));
                return;
            }

            if (path.Equals("/api/pdv/sales", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
            {
                DateTime? day = ParseDate(ex.Query["date"]);
                var includeCancelled = (ex.Query["includeCancelled"] ?? "1") is not ("0" or "false" or "False");
                var list = PdvQueryService.ListSalesLocal(day, includeCancelled);
                WriteJson(ex, 200, new { ok = true, items = list });
                return;
            }

            if (path.StartsWith("/api/pdv/sales/", StringComparison.OrdinalIgnoreCase) && ex.Method == "GET")
            {
                var idPart = path["/api/pdv/sales/".Length..];
                if (int.TryParse(idPart, out var saleId))
                {
                    WriteJson(ex, 200, PdvQueryService.GetSaleDetailLocal(saleId));
                    return;
                }
            }

            if (path.Equals("/api/dashboard", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
            {
                var body = ReadJson(ex.Body);
                var from = ParseDate(body.GetString("from"));
                var to = ParseDate(body.GetString("to"));
                var dateMode = body.GetString("dateMode") ?? "session";
                var rdMode = body.GetString("rdDateMode") ?? "due";
                WriteJson(ex, 200, BusinessDashboardService.GetDashboardLocal(from, to, dateMode, rdMode));
                return;
            }

            if (path.Equals("/api/stock/report", StringComparison.OrdinalIgnoreCase) && ex.Method == "POST")
            {
                var body = ReadJson(ex.Body);
                var kindRaw = body.GetString("kind") ?? "Minimo";
                if (!Enum.TryParse<StockReportKind>(kindRaw, true, out var kind))
                    kind = StockReportKind.Minimo;
                var from = ParseDate(body.GetString("from"));
                var to = ParseDate(body.GetString("to"));
                body.TryGetInt("limit", out var limit);
                if (limit <= 0) limit = 500;
                WriteJson(ex, 200, StockService.ListReportLocal(kind, from, to, limit));
                return;
            }

            WriteJson(ex, 404, new { error = "API não encontrada." });
        }
        catch (Exception e)
        {
            WriteJson(ex, 400, new { error = e.Message });
        }
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParseExact(raw.Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
            return d.Date;
        if (DateTime.TryParse(raw, out d)) return d.Date;
        return null;
    }

    private static void HandlePair(HttpExchange ex)
    {
        using var remoteScope = AccessControl.EnterRemoteStoreRequest();
        var body = ReadJson(ex.Body);
        var result = StoreNetworkPairingService.TryPair(
            body.GetString("pairingCode"),
            body.GetString("deviceId"),
            body.GetString("deviceName"),
            ex.RemoteIp);
        if (!result.Ok || result.Device is null)
        {
            WriteJson(ex, result.StatusCode, new { ok = false, error = result.Error ?? "Falha no pareamento." });
            return;
        }

        WriteJson(ex, 200, new
        {
            ok = true,
            store = AppSettingsService.GetNomeDeposito(),
            deviceId = result.Device.DeviceId,
            deviceName = result.Device.DeviceName,
            createdAt = result.Device.CreatedAt,
        });
    }

    private static void HandlePairStatus(HttpExchange ex)
    {
        using var remoteScope = AccessControl.EnterRemoteStoreRequest();
        var status = StoreNetworkPairingService.GetDeviceStatus(ex.Query["deviceId"], touchLastSeen: true);
        WriteJson(ex, 200, new
        {
            ok = true,
            authorized = status.Authorized,
            revoked = status.Revoked,
            deviceId = status.DeviceId,
            deviceName = status.DeviceName,
            createdAt = status.CreatedAt,
            lastSeenAt = status.LastSeenAt,
        });
    }

    private static void HandleSessionLogin(HttpExchange ex)
    {
        using var remoteScope = AccessControl.EnterRemoteStoreRequest();
        var body = ReadJson(ex.Body);
        var result = StoreNetworkSessionService.TryCreate(
            body.GetString("login"),
            body.GetString("password"),
            body.GetString("deviceId"),
            ex.RemoteIp,
            ex.Headers["X-Store-Origin"] ?? "notebook");
        if (!result.Ok || result.Session is null)
        {
            WriteJson(ex, result.StatusCode, new { ok = false, error = result.Error ?? "Falha no login." });
            return;
        }

        var s = result.Session;
        WriteJson(ex, 200, new
        {
            ok = true,
            token = s.Token,
            expiresAt = s.ExpiresAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            user = new
            {
                id = s.UserId,
                login = s.Login,
                name = s.UserName,
                role = s.Role,
                permissions = s.Permissions,
            },
        });
    }

    private static void HandleSessionLogout(HttpExchange ex)
    {
        using var remoteScope = AccessControl.EnterRemoteStoreRequest();
        var token = ReadBearerToken(ex);
        if (string.IsNullOrEmpty(token))
        {
            WriteJson(ex, 401, new { ok = false, error = "Informe a sessão." });
            return;
        }

        StoreNetworkSessionService.Logout(token);
        WriteJson(ex, 200, new { ok = true });
    }

    private static string? ReadBearerToken(HttpExchange ex)
    {
        var auth = (ex.Headers["Authorization"] ?? "").Trim();
        const string prefix = "Bearer ";
        if (auth.Length <= prefix.Length
            || !auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var token = auth[prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private bool IsAuthorized(HttpExchange ex)
    {
        var pin = (ex.Headers["X-Store-Pin"] ?? "").Trim();
        var expected = StoreNetworkMode.GetServerPin();
        Pin = expected;
        return pin.Length > 0 && SecureEquals(pin, expected);
    }

    private static void LogTlsFailure(Exception ex)
    {
        var msg = (ex.GetType().Name + ": " + (ex.Message ?? "")).Trim();
        if (msg.Length > 240)
            msg = msg[..240];
        AuditService.Log("rede_tls_falha", "store_network", null, msg);
    }

    private static bool SecureEquals(string a, string b)
    {
        a ??= "";
        b ??= "";
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static void WriteJson(HttpExchange ex, int status, object payload)
    {
        ex.Status = status;
        ex.ContentType = "application/json; charset=utf-8";
        ex.ResponseBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts));
    }

    private readonly struct JsonBag(JsonElement? el)
    {
        public static JsonBag Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new JsonBag(null);
            try { return new JsonBag(JsonDocument.Parse(text).RootElement.Clone()); }
            catch { return new JsonBag(null); }
        }

        public string? GetString(string name)
        {
            if (el is not { } e || e.ValueKind != JsonValueKind.Object) return null;
            if (e.TryGetProperty(name, out var p))
                return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
            foreach (var prop in e.EnumerateObject())
            {
                if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
            }
            return null;
        }

        public bool TryGetInt(string name, out int value)
        {
            value = 0;
            if (el is not { } e || e.ValueKind != JsonValueKind.Object) return false;
            if (!e.TryGetProperty(name, out var p)) return false;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value)) return true;
            return p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value);
        }

        public bool TryGetDouble(string name, out double value)
        {
            value = 0;
            if (el is not { } e || e.ValueKind != JsonValueKind.Object) return false;
            if (!e.TryGetProperty(name, out var p)) return false;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out value)) return true;
            return p.ValueKind == JsonValueKind.String
                && double.TryParse(p.GetString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }

    private static JsonBag ReadJson(string? text) => JsonBag.Parse(text);

    private sealed class HttpExchange
    {
        public string Method { get; set; } = "GET";
        public string Path { get; set; } = "/";
        public NameValueCollection Query { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public NameValueCollection Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Body { get; set; }
        public string RemoteIp { get; set; } = "";
        public int Status { get; set; } = 200;
        public string? ContentType { get; set; }
        public byte[]? ResponseBody { get; set; }
    }

    private static HttpExchange? ReadHttp(Stream stream)
    {
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) return null;
            ms.Write(buf, 0, n);
            headerEnd = IndexOfHeaderEnd(ms.ToArray());
            if (ms.Length > 4 * 1024 * 1024) return null;
        }

        var all = ms.ToArray();
        var headerText = Encoding.ASCII.GetString(all.AsSpan(0, headerEnd));
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
            foreach (var pair in target[(qIdx + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                    query[Uri.UnescapeDataString(pair.Replace('+', ' '))] = "";
                else
                {
                    query[Uri.UnescapeDataString(pair[..eq].Replace('+', ' '))] =
                        Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
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
            contentLength = Math.Min(cl, 8 * 1024 * 1024);
        var transferEncoding = headers["Transfer-Encoding"] ?? "";
        var isChunked = transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase);

        // HttpClient às vezes manda Expect: 100-continue e espera esta resposta antes do body
        var expect = headers["Expect"] ?? "";
        if (expect.Contains("100-continue", StringComparison.OrdinalIgnoreCase)
            && (contentLength > 0 || isChunked))
        {
            var cont = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
            stream.Write(cont, 0, cont.Length);
            stream.Flush();
        }

        var bodyStart = headerEnd + 4;
        var bodyMs = new MemoryStream();
        if (bodyStart < all.Length)
            bodyMs.Write(all, bodyStart, all.Length - bodyStart);

        if (isChunked)
        {
            // Lê até o chunk final "0\r\n" (limite 8 MB)
            while (true)
            {
                if (TryDecodeChunked(bodyMs.ToArray(), out var decoded, out var complete) && complete)
                {
                    bodyMs = new MemoryStream(decoded);
                    break;
                }
                if (bodyMs.Length > 8 * 1024 * 1024) break;
                var n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                bodyMs.Write(buf, 0, n);
            }
            if (TryDecodeChunked(bodyMs.ToArray(), out var decodedBody, out _))
                bodyMs = new MemoryStream(decodedBody);
        }
        else
        {
            while (bodyMs.Length < contentLength)
            {
                var n = stream.Read(buf, 0, Math.Min(buf.Length, contentLength - (int)bodyMs.Length));
                if (n <= 0) break;
                bodyMs.Write(buf, 0, n);
            }
        }

        return new HttpExchange
        {
            Method = method,
            Path = path,
            Query = query,
            Headers = headers,
            Body = bodyMs.Length > 0 ? Encoding.UTF8.GetString(bodyMs.ToArray()) : null,
        };
    }

    /// <summary>Decodifica body HTTP chunked. complete=true quando viu o chunk 0.</summary>
    private static bool TryDecodeChunked(byte[] raw, out byte[] decoded, out bool complete)
    {
        decoded = Array.Empty<byte>();
        complete = false;
        using var output = new MemoryStream();
        var i = 0;
        while (i < raw.Length)
        {
            var lineEnd = IndexOfCrlf(raw, i);
            if (lineEnd < 0) return false;
            var sizeText = Encoding.ASCII.GetString(raw, i, lineEnd - i).Trim();
            var semi = sizeText.IndexOf(';');
            if (semi >= 0) sizeText = sizeText[..semi].Trim();
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var size)
                || size < 0)
                return false;
            i = lineEnd + 2;
            if (size == 0)
            {
                complete = true;
                decoded = output.ToArray();
                return true;
            }
            if (i + size + 2 > raw.Length) return false;
            output.Write(raw, i, size);
            i += size;
            if (i + 1 >= raw.Length || raw[i] != '\r' || raw[i + 1] != '\n')
                return false;
            i += 2;
        }
        return false;
    }

    private static int IndexOfCrlf(byte[] bytes, int start)
    {
        for (var i = start; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n')
                return i;
        }
        return -1;
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

    private static void WriteHttp(Stream stream, HttpExchange ex)
    {
        var body = ex.ResponseBody ?? Array.Empty<byte>();
        var statusText = ex.Status switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            _ => "OK",
        };
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(ex.Status).Append(' ').Append(statusText).Append("\r\n");
        sb.Append("Cache-Control: no-store\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("Access-Control-Allow-Headers: Content-Type, X-Store-Pin, X-Store-Origin, Authorization\r\n");
        sb.Append("Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n");
        sb.Append("Connection: close\r\n");
        if (ex.Status != 204)
        {
            sb.Append("Content-Type: ").Append(ex.ContentType ?? "application/json; charset=utf-8").Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        }
        else sb.Append("Content-Length: 0\r\n");
        sb.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
        stream.Write(headerBytes, 0, headerBytes.Length);
        if (ex.Status != 204 && body.Length > 0)
            stream.Write(body, 0, body.Length);
        stream.Flush();
    }
}
