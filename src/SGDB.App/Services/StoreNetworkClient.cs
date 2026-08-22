using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>Cliente HTTP do notebook → PC da loja.</summary>
public static class StoreNetworkClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Tempo máximo para o handshake TCP. Sem isso o Windows espera ~21 s
    /// quando o PC da loja não responde (fora da rede / servidor desligado).
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    /// <summary>O parser do host só fala HTTP/1.1 — HTTPS não pode negociar HTTP/2.</summary>
    public static readonly Version RequiredHttpVersion = HttpVersion.Version11;

    public static readonly HttpVersionPolicy RequiredVersionPolicy =
        HttpVersionPolicy.RequestVersionOrLower;

    public const string MissingFingerprintMessage =
        "Este computador ainda não confia no certificado da loja.\n" +
        "Configure o fingerprint exibido no PC servidor.";

    public const string FingerprintMismatchMessage =
        "O certificado da loja mudou.\n" +
        "Não foi possível estabelecer conexão segura.\n" +
        "Confirme com o responsável e configure novamente.";

    public const string TlsRequiredMessage =
        "Não foi possível estabelecer conexão segura com o PC da loja.\n" +
        "O PC da loja precisa ser atualizado (Rede Loja com HTTPS).\n" +
        "Não há fallback para HTTP.";

    public const string PairingNotSupportedMessage =
        "O PC da loja precisa ser atualizado para permitir o pareamento seguro.";

    public const string DeviceRevokedMessage =
        "Este computador não está mais autorizado pela loja.\n" +
        "Solicite um novo pareamento no PC servidor.";

    public const string SessionNotSupportedMessage =
        "O PC da loja precisa ser atualizado para usar login seguro da Rede Loja.";

    private static string? _sessionToken;
    private static DateTime? _sessionExpiresAt;
    private static StoreNetworkRemoteUserDto? _remoteUser;
    /// <summary>null = ainda não consultou /api/status nesta sessão.</summary>
    private static IReadOnlyList<string>? _cachedFeatures;

    internal static IReadOnlyList<string>? TestStatusFeatures { get; set; }
    internal static int TestStatusFetchCount { get; set; }
    internal static int TestPurchaseSendCount { get; set; }

    public static string? SessionToken => _sessionToken;
    public static DateTime? SessionExpiresAt => _sessionExpiresAt;
    public static StoreNetworkRemoteUserDto? RemoteUser => _remoteUser;
    public static bool HasSession => !string.IsNullOrEmpty(_sessionToken);

    internal static HttpClient CreateHttpClient(string baseUrl, TimeSpan requestTimeout)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(TlsRequiredMessage);
        }

        if (!StoreNetworkMode.HasServerFingerprint())
            throw new InvalidOperationException(MissingFingerprintMessage);

        var expected = StoreNetworkCertificateService.NormalizeFingerprint(
            StoreNetworkMode.GetServerFingerprint());

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = ConnectTimeout,
            SslOptions =
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                    StoreNetworkCertificateService.MatchesFingerprint(cert, expected),
            },
        };
        var c = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = requestTimeout,
            DefaultRequestVersion = RequiredHttpVersion,
            DefaultVersionPolicy = RequiredVersionPolicy,
        };
        // Servidor TCP próprio não trata 100-continue — sem isso o body do POST pode não chegar
        c.DefaultRequestHeaders.ExpectContinue = false;
        return c;
    }

    private static HttpClient CreateClient()
    {
        StoreNetworkMode.EnsureClientConfigured();
        var c = CreateHttpClient(StoreNetworkMode.ClientBaseUrl, TimeSpan.FromSeconds(60));
        c.DefaultRequestHeaders.Add("X-Store-Pin", StoreNetworkMode.GetClientPin());
        c.DefaultRequestHeaders.Add("X-Store-Origin", "notebook");
        if (!string.IsNullOrEmpty(_sessionToken))
            c.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + _sessionToken);
        return c;
    }

    private static async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var client = CreateClient();
        using var req = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null)
        {
            // StringContent define Content-Length. JsonContent costuma ir em
            // Transfer-Encoding: chunked, e o servidor TCP da loja não decodifica chunked
            // (gera erro tipo: 'C' is an invalid end of a number).
            var json = JsonSerializer.Serialize(body, JsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage res;
        try
        {
            res = await client.SendAsync(req).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(FormatConnectError(ex), ex);
        }

        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            string err = "Erro " + (int)res.StatusCode;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("error", out var e))
                    err = e.GetString() ?? err;
            }
            catch { /* ignore */ }
            err = RedactSecrets(err);
            var hadBearer = !string.IsNullOrEmpty(_sessionToken);
            if (hadBearer && (int)res.StatusCode == 401)
                ApplicationLoginService.HandleAuthenticatedRequestUnauthorized(err);
            if ((int)res.StatusCode == 404
                || err.Contains("não encontrada", StringComparison.OrdinalIgnoreCase))
            {
                err += "\n\nO PC da loja está com SGDB antigo. Atualize a loja e o notebook "
                     + "com a mesma pasta SGDB_Para_Outro_PC (ATUALIZAR_NESTE_PC.bat) "
                     + "e ligue de novo o servidor em Rede Loja.";
            }
            throw new InvalidOperationException(err);
        }

        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text))
            return default!;

        return JsonSerializer.Deserialize<T>(text, JsonOpts)
               ?? throw new InvalidOperationException("Resposta inválida do servidor.");
    }

    private static T Run<T>(Func<Task<T>> fn)
    {
        try
        {
            // Task.Run evita deadlock WPF (sync-over-async na UI thread)
            return Task.Run(fn).GetAwaiter().GetResult();
        }
        catch (AggregateException ae)
        {
            throw ae.InnerException ?? ae;
        }
    }

    public static StoreNetworkStatusDto Ping() =>
        Run(async () =>
        {
            if (TestStatusFeatures is not null)
            {
                TestStatusFetchCount++;
                var fake = new StoreNetworkStatusDto
                {
                    Ok = true,
                    ApiVersion = 2,
                    Features = TestStatusFeatures.ToList(),
                };
                CacheFeatures(fake.Features);
                return fake;
            }

            using var client = CreateClient();
            HttpResponseMessage res;
            try
            {
                res = await client.GetAsync("api/status").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(FormatConnectError(ex), ex);
            }
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException("Servidor não respondeu. PIN ou IP incorreto?");
            var dto = JsonSerializer.Deserialize<StoreNetworkStatusDto>(text, JsonOpts)
                   ?? throw new InvalidOperationException("Resposta inválida.");
            TestStatusFetchCount++;
            CacheFeatures(dto.Features);
            return dto;
        });

    public static StoreNetworkStatusDto Login(string pin) =>
        Run(async () =>
        {
            pin = (pin ?? "").Trim();
            var baseUrl = StoreNetworkMode.ClientBaseUrl.TrimEnd('/') + "/";
            using var client = CreateHttpClient(baseUrl, TimeSpan.FromSeconds(20));
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Store-Pin", pin);
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/login")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { pin }, JsonOpts),
                    Encoding.UTF8,
                    "application/json"),
            };
            HttpResponseMessage res;
            try
            {
                res = await client.SendAsync(req).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(FormatConnectError(ex), ex);
            }
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<StoreNetworkStatusDto>(text, JsonOpts);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException(dto?.Error ?? "PIN incorreto.");
            return dto ?? throw new InvalidOperationException("Resposta inválida.");
        });

    public static StoreNetworkPairDto Pair(string pairingCode)
    {
        pairingCode = (pairingCode ?? "").Trim();
        var deviceId = StoreNetworkPairingService.EnsureDeviceId();
        var deviceName = StoreNetworkPairingService.GetDeviceName();
        return Run(async () =>
        {
            var baseUrl = StoreNetworkMode.ClientBaseUrl.TrimEnd('/') + "/";
            using var client = CreateHttpClient(baseUrl, TimeSpan.FromSeconds(20));
            var pin = StoreNetworkMode.GetClientPin();
            if (!string.IsNullOrWhiteSpace(pin))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Store-Pin", pin);
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/pair")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        pairingCode,
                        deviceId,
                        deviceName,
                    }, JsonOpts),
                    Encoding.UTF8,
                    "application/json"),
            };
            HttpResponseMessage res;
            try
            {
                res = await client.SendAsync(req).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(FormatConnectError(ex), ex);
            }

            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if ((int)res.StatusCode == 404)
                throw new InvalidOperationException(PairingNotSupportedMessage);

            var dto = JsonSerializer.Deserialize<StoreNetworkPairDto>(text, JsonOpts);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException(dto?.Error ?? FormatPairHttpError((int)res.StatusCode));
            return dto ?? throw new InvalidOperationException("Resposta inválida do servidor.");
        });
    }

    public static StoreNetworkDeviceStatusDto GetPairingStatus()
    {
        var deviceId = StoreNetworkPairingService.EnsureDeviceId();
        return Run(async () =>
        {
            var baseUrl = StoreNetworkMode.ClientBaseUrl.TrimEnd('/') + "/";
            using var client = CreateHttpClient(baseUrl, TimeSpan.FromSeconds(20));
            var pin = StoreNetworkMode.GetClientPin();
            if (!string.IsNullOrWhiteSpace(pin))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Store-Pin", pin);
            HttpResponseMessage res;
            try
            {
                res = await client.GetAsync("api/pair/status?deviceId=" + Uri.EscapeDataString(deviceId))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(FormatConnectError(ex), ex);
            }

            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if ((int)res.StatusCode == 404)
                throw new InvalidOperationException(PairingNotSupportedMessage);
            if (!res.IsSuccessStatusCode)
            {
                string err = "Erro " + (int)res.StatusCode;
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("error", out var e))
                        err = e.GetString() ?? err;
                }
                catch { /* ignore */ }
                throw new InvalidOperationException(err);
            }

            return JsonSerializer.Deserialize<StoreNetworkDeviceStatusDto>(text, JsonOpts)
                   ?? throw new InvalidOperationException("Resposta inválida do servidor.");
        });
    }

    internal static string FormatPairHttpError(int statusCode) =>
        statusCode == 404
            ? PairingNotSupportedMessage
            : "Erro " + statusCode;

    public static StoreNetworkSessionDto LoginRemote(string login, string password)
    {
        login = (login ?? "").Trim();
        password ??= "";
        var deviceId = StoreNetworkPairingService.EnsureDeviceId();
        var status = GetPairingStatus();
        if (status.Revoked)
            throw new InvalidOperationException(DeviceRevokedMessage);
        if (!status.Authorized)
            throw new InvalidOperationException(StoreNetworkSessionService.DeviceNotAuthorizedMessage);

        return Run(async () =>
        {
            var baseUrl = StoreNetworkMode.ClientBaseUrl.TrimEnd('/') + "/";
            using var client = CreateHttpClient(baseUrl, TimeSpan.FromSeconds(20));
            var pin = StoreNetworkMode.GetClientPin();
            if (!string.IsNullOrWhiteSpace(pin))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-Store-Pin", pin);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Store-Origin", "notebook");
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/session")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { login, password, deviceId }, JsonOpts),
                    Encoding.UTF8,
                    "application/json"),
            };
            HttpResponseMessage res;
            try
            {
                res = await client.SendAsync(req).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(FormatConnectError(ex), ex);
            }

            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if ((int)res.StatusCode == 404)
                throw new InvalidOperationException(SessionNotSupportedMessage);

            var dto = JsonSerializer.Deserialize<StoreNetworkSessionDto>(text, JsonOpts);
            if (!res.IsSuccessStatusCode)
                throw new InvalidOperationException(RedactSecrets(dto?.Error ?? "Erro " + (int)res.StatusCode));
            if (dto is null || string.IsNullOrWhiteSpace(dto.Token))
                throw new InvalidOperationException("Resposta inválida do servidor.");

            _sessionToken = dto.Token;
            _remoteUser = dto.User;
            if (DateTime.TryParse(dto.ExpiresAt, out var exp))
                _sessionExpiresAt = exp.ToUniversalTime();
            else
                _sessionExpiresAt = DateTime.UtcNow.Add(StoreNetworkSessionService.Ttl);
            return dto;
        });
    }

    public static void LogoutRemote()
    {
        var token = _sessionToken;
        try
        {
            if (!string.IsNullOrEmpty(token) && StoreNetworkMode.IsClient && StoreNetworkMode.HasServerFingerprint())
            {
                Run(async () =>
                {
                    var baseUrl = StoreNetworkMode.ClientBaseUrl.TrimEnd('/') + "/";
                    using var client = CreateHttpClient(baseUrl, TimeSpan.FromSeconds(10));
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + token);
                    using var req = new HttpRequestMessage(HttpMethod.Post, "api/logout");
                    try
                    {
                        await client.SendAsync(req).ConfigureAwait(false);
                    }
                    catch
                    {
                        /* limpa estado local mesmo offline */
                    }
                    return true;
                });
            }
        }
        catch
        {
            /* limpa estado local mesmo se o servidor recusar */
        }
        finally
        {
            ClearSessionState();
        }
    }

    internal static void ClearSessionState()
    {
        _sessionToken = null;
        _sessionExpiresAt = null;
        _remoteUser = null;
        _cachedFeatures = null;
    }

    internal static void ResetPurchaseSalePriceTestHooks()
    {
        TestStatusFeatures = null;
        TestStatusFetchCount = 0;
        TestPurchaseSendCount = 0;
        _cachedFeatures = null;
    }

    internal static void SeedCachedFeatures(IReadOnlyList<string> features) =>
        CacheFeatures(features);

    internal static void EnsurePurchaseSalePriceCapability(PurchaseInput input)
    {
        if (!PurchaseSalePriceRules.NeedsAtomicSalePriceCapability(input))
            return;

        if (_cachedFeatures is not null)
        {
            if (PurchaseSalePriceRules.SupportsAtomicSalePrice(_cachedFeatures))
                return;
            throw new InvalidOperationException(PurchaseSalePriceRules.HostNeedsUpgradeBeforeCloseMessage);
        }

        var status = Ping();
        if (!PurchaseSalePriceRules.SupportsAtomicSalePrice(status.Features))
            throw new InvalidOperationException(PurchaseSalePriceRules.HostNeedsUpgradeBeforeCloseMessage);
    }

    private static void CacheFeatures(IReadOnlyList<string>? features) =>
        _cachedFeatures = features is null ? [] : features.ToList();

    internal static string RedactSecrets(string? text)
    {
        var s = text ?? "";
        if (!string.IsNullOrEmpty(_sessionToken) && s.Contains(_sessionToken, StringComparison.Ordinal))
            s = s.Replace(_sessionToken, "[redacted]", StringComparison.Ordinal);
        return s;
    }

    internal static string FormatConnectError(Exception ex)
    {
        if (IsCertificateMismatch(ex))
            return FingerprintMismatchMessage;
        if (IsTlsFailure(ex))
            return TlsRequiredMessage + "\n" + RedactSecrets(ex.Message ?? "");
        return "Não conectou no PC da loja. Confira IP, PIN, Firewall, fingerprint e se o servidor está Ligado.\n"
               + RedactSecrets(ex.Message ?? "");
    }

    internal static bool IsCertificateMismatch(Exception? ex)
    {
        while (ex is not null)
        {
            var msg = ex.Message ?? "";
            if (msg.Contains("RemoteCertificateValidationCallback", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("remote certificate was rejected", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("the remote certificate is invalid", StringComparison.OrdinalIgnoreCase))
                return true;
            ex = ex.InnerException;
        }
        return false;
    }

    private static bool IsTlsFailure(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is AuthenticationException)
                return true;
            var msg = ex.Message ?? "";
            if (msg.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("TLS", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("secure channel", StringComparison.OrdinalIgnoreCase))
                return true;
            ex = ex.InnerException;
        }
        return false;
    }

    /// <summary>Testa IP/PIN sem gravar modo Cliente definitivamente (grava draft temporário).</summary>
    public static StoreNetworkStatusDto TestConnection(string host, string pin, int port)
    {
        StoreNetworkMode.SaveClient(host, pin, port);
        return Login(pin);
    }

    public static IReadOnlyList<Product> ListProducts(
        string? search, string ativo, string? group, string? dateFrom, string? dateTo, string dateMode)
    {
        var q = new StringBuilder("api/products?");
        q.Append("search=").Append(Uri.EscapeDataString(search ?? ""));
        q.Append("&ativo=").Append(Uri.EscapeDataString(ativo ?? "ativos"));
        if (!string.IsNullOrWhiteSpace(group))
            q.Append("&group=").Append(Uri.EscapeDataString(group));
        if (!string.IsNullOrWhiteSpace(dateFrom))
            q.Append("&dateFrom=").Append(Uri.EscapeDataString(dateFrom));
        if (!string.IsNullOrWhiteSpace(dateTo))
            q.Append("&dateTo=").Append(Uri.EscapeDataString(dateTo));
        q.Append("&dateMode=").Append(Uri.EscapeDataString(dateMode ?? "none"));
        var dto = Run(() => SendAsync<StoreNetworkListDto<Product>>(HttpMethod.Get, q.ToString()));
        return dto.Items ?? [];
    }

    public static Product? GetProduct(int id) =>
        Run(() => SendAsync<StoreNetworkItemDto<Product>>(HttpMethod.Get, $"api/products/{id}")).Item;

    public static Product? FindProductByBarcode(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;
        var dto = Run(() => SendAsync<StoreNetworkItemDto<Product>>(HttpMethod.Get,
            "api/products/by-barcode?q=" + Uri.EscapeDataString(barcode.Trim())));
        return dto.Item;
    }

    public static Product CreateProduct(ProductInput input) =>
        Run(() => SendAsync<StoreNetworkItemDto<Product>>(HttpMethod.Post, "api/products", input)).Item
        ?? throw new InvalidOperationException("Falha ao criar produto no servidor.");

    public static Product UpdateProduct(int id, ProductInput input) =>
        Run(() => SendAsync<StoreNetworkItemDto<Product>>(HttpMethod.Put, $"api/products/{id}", input)).Item
        ?? throw new InvalidOperationException("Falha ao atualizar produto no servidor.");

    public static void SoftDeleteProduct(int id) =>
        Run(() => SendAsync<StoreNetworkOkDto>(HttpMethod.Delete, $"api/products/{id}"));

    public static Product MergeProducts(int keepId, int absorbId) =>
        Run(() => SendAsync<StoreNetworkItemDto<Product>>(HttpMethod.Post, "api/products/merge", new
        {
            keepId,
            absorbId,
        })).Item
        ?? throw new InvalidOperationException("Falha ao unificar produtos no servidor.");

    public static StockAdjustResult AdjustStock(
        int productId, StockAdjustMode mode, double? quantity, double? newStock, string? notes,
        double? unitCost = null) =>
        Run(() => SendAsync<StockAdjustResult>(HttpMethod.Post, "api/stock/adjust", new
        {
            productId,
            mode = mode.ToString(),
            quantity,
            newStock,
            notes,
            unitCost,
        })) ?? throw new InvalidOperationException("Falha no ajuste de estoque.");

    public static StockAdjustResult AdjustFridgeStock(
        int productId, StockAdjustMode mode, double? quantity, double? newStock, string? notes) =>
        Run(() => SendAsync<StockAdjustResult>(HttpMethod.Post, "api/stock/adjust-fridge", new
        {
            productId,
            mode = mode.ToString(),
            quantity,
            newStock,
            notes,
        })) ?? throw new InvalidOperationException("Falha no ajuste da geladeira.");

    public static IReadOnlyList<Purchase> ListPurchases(
        string? search, string? status, string? dateFrom, string? dateTo)
    {
        var q = new StringBuilder("api/purchases?");
        q.Append("search=").Append(Uri.EscapeDataString(search ?? ""));
        if (!string.IsNullOrWhiteSpace(status))
            q.Append("&status=").Append(Uri.EscapeDataString(status));
        if (!string.IsNullOrWhiteSpace(dateFrom))
            q.Append("&dateFrom=").Append(Uri.EscapeDataString(dateFrom));
        if (!string.IsNullOrWhiteSpace(dateTo))
            q.Append("&dateTo=").Append(Uri.EscapeDataString(dateTo));
        var dto = Run(() => SendAsync<StoreNetworkListDto<Purchase>>(HttpMethod.Get, q.ToString()));
        return dto.Items ?? [];
    }

    public static Purchase? GetPurchase(int id) =>
        Run(() => SendAsync<StoreNetworkItemDto<Purchase>>(HttpMethod.Get, $"api/purchases/{id}")).Item;

    public static bool NfeKeyExists(string chave) =>
        Run(() => SendAsync<StoreNetworkExistsDto>(HttpMethod.Get,
            "api/purchases/nfe-exists?key=" + Uri.EscapeDataString(chave))).Exists;

    public static int CreatePurchase(PurchaseInput input, bool closeOnSave)
    {
        EnsurePurchaseSalePriceCapability(input);
        TestPurchaseSendCount++;
        var dto = Run(() => SendAsync<StoreNetworkIdDto>(HttpMethod.Post, "api/purchases",
            new { input, closeOnSave }));
        PurchaseSalePriceRules.EnsureHostAppliedSalePrices(input, closeOnSave, dto.SalePriceUpdates);
        return dto.Id;
    }

    public static void UpdatePurchase(int id, PurchaseInput input, bool closeOnSave)
    {
        EnsurePurchaseSalePriceCapability(input);
        TestPurchaseSendCount++;
        var dto = Run(() => SendAsync<StoreNetworkOkDto>(HttpMethod.Put, $"api/purchases/{id}",
            new { input, closeOnSave }));
        PurchaseSalePriceRules.EnsureHostAppliedSalePrices(input, closeOnSave, dto.SalePriceUpdates);
    }

    public static void DeletePurchase(int id) =>
        Run(() => SendAsync<StoreNetworkOkDto>(HttpMethod.Delete, $"api/purchases/{id}"));

    public static void CancelPurchase(int id) =>
        Run(() => SendAsync<StoreNetworkOkDto>(HttpMethod.Post, $"api/purchases/{id}/cancel"));

    public static void ReopenPurchase(int id) =>
        Run(() => SendAsync<StoreNetworkOkDto>(HttpMethod.Post, $"api/purchases/{id}/reopen"));

    public static IReadOnlyList<Person> ListPeople(string? search, string ativo, string tipo)
    {
        var q = $"api/people?search={Uri.EscapeDataString(search ?? "")}&ativo={Uri.EscapeDataString(ativo)}&tipo={Uri.EscapeDataString(tipo)}";
        var dto = Run(() => SendAsync<StoreNetworkListDto<Person>>(HttpMethod.Get, q));
        return dto.Items ?? [];
    }

    public static Person? GetPerson(int id) =>
        Run(() => SendAsync<StoreNetworkItemDto<Person>>(HttpMethod.Get, $"api/people/{id}")).Item;

    public static Person? FindPersonByDoc(string digits) =>
        Run(() => SendAsync<StoreNetworkItemDto<Person>>(HttpMethod.Get,
            "api/people/by-doc?q=" + Uri.EscapeDataString(digits))).Item;

    public static Person CreatePerson(PersonInput input, bool requireClienteRole = true) =>
        Run(() => SendAsync<StoreNetworkItemDto<Person>>(HttpMethod.Post, "api/people",
            new { input, requireClienteRole })).Item
        ?? throw new InvalidOperationException("Falha ao criar pessoa na loja.");

    public static Person UpdatePerson(int id, PersonInput input, bool requireClienteRole = true) =>
        Run(() => SendAsync<StoreNetworkItemDto<Person>>(HttpMethod.Put, $"api/people/{id}",
            new { input, requireClienteRole })).Item
        ?? throw new InvalidOperationException("Falha ao atualizar pessoa na loja.");

    public static IReadOnlyList<PayableTitleRow> ListPayableTitles(
        string situacao, int? supplierId, string? dateFromBr, string? dateToBr, int? purchaseId)
    {
        var q = new StringBuilder("api/payables/titles?situacao=" + Uri.EscapeDataString(situacao ?? "pendentes"));
        if (supplierId is int sid) q.Append("&supplierId=").Append(sid);
        if (!string.IsNullOrWhiteSpace(dateFromBr)) q.Append("&dateFrom=").Append(Uri.EscapeDataString(dateFromBr));
        if (!string.IsNullOrWhiteSpace(dateToBr)) q.Append("&dateTo=").Append(Uri.EscapeDataString(dateToBr));
        if (purchaseId is int pid) q.Append("&purchaseId=").Append(pid);
        var dto = Run(() => SendAsync<StoreNetworkListDto<PayableTitleRow>>(HttpMethod.Get, q.ToString()));
        return dto.Items ?? [];
    }

    public static IReadOnlyList<PayableInstallmentRow> ListPayableInstallments(
        string situacao, int? supplierId, string? dateFromBr, string? dateToBr, int? purchaseId)
    {
        var q = new StringBuilder("api/payables/installments?situacao=" + Uri.EscapeDataString(situacao ?? "pendentes"));
        if (supplierId is int sid) q.Append("&supplierId=").Append(sid);
        if (!string.IsNullOrWhiteSpace(dateFromBr)) q.Append("&dateFrom=").Append(Uri.EscapeDataString(dateFromBr));
        if (!string.IsNullOrWhiteSpace(dateToBr)) q.Append("&dateTo=").Append(Uri.EscapeDataString(dateToBr));
        if (purchaseId is int pid) q.Append("&purchaseId=").Append(pid);
        var dto = Run(() => SendAsync<StoreNetworkListDto<PayableInstallmentRow>>(HttpMethod.Get, q.ToString()));
        return dto.Items ?? [];
    }

    public static IReadOnlyList<PayableInstallmentDetail> ListPayableInstallmentsOfTitle(int titleId)
    {
        var dto = Run(() => SendAsync<StoreNetworkListDto<PayableInstallmentDetail>>(HttpMethod.Get,
            $"api/payables/titles/{titleId}/installments"));
        return dto.Items ?? [];
    }

    public static PayableInstallmentDetail? GetPayableInstallment(int installmentId) =>
        Run(() => SendAsync<StoreNetworkItemDto<PayableInstallmentDetail>>(HttpMethod.Get,
            $"api/payables/installments/{installmentId}")).Item;

    public static bool EnsurePayablesForPurchase(int purchaseId) =>
        Run(() => SendAsync<StoreNetworkEnsurePayablesDto>(HttpMethod.Post,
            $"api/payables/purchases/{purchaseId}/ensure")).Created;

    public static void PayPayableInstallment(int installmentId, PayablePayInput input) =>
        Run(() => SendAsync<StoreNetworkOkDto>(HttpMethod.Post,
            $"api/payables/installments/{installmentId}/pay", input));

    public static void ReversePayablePayment(int installmentId) =>
        Run(() => SendAsync<StoreNetworkOkDto>(HttpMethod.Post,
            $"api/payables/installments/{installmentId}/reverse"));

    public static IReadOnlyList<ContainerType> ListContainerTypes(bool onlyActive)
    {
        var q = "api/container-types?onlyActive=" + (onlyActive ? "1" : "0");
        var dto = Run(() => SendAsync<StoreNetworkListDto<ContainerType>>(HttpMethod.Get, q));
        return dto.Items ?? [];
    }

    public static ContainerType? GetContainerType(int id) =>
        Run(() => SendAsync<StoreNetworkItemDto<ContainerType>>(HttpMethod.Get,
            $"api/container-types/{id}")).Item;

    public static ContainerType CreateContainerType(ContainerTypeInput input) =>
        Run(() => SendAsync<StoreNetworkItemDto<ContainerType>>(HttpMethod.Post,
            "api/container-types", input)).Item
        ?? throw new InvalidOperationException("Falha ao criar tipo de vasilhame na loja.");

    public static ContainerType UpdateContainerType(int id, ContainerTypeInput input) =>
        Run(() => SendAsync<StoreNetworkItemDto<ContainerType>>(HttpMethod.Put,
            $"api/container-types/{id}", input)).Item
        ?? throw new InvalidOperationException("Falha ao atualizar tipo de vasilhame na loja.");

    public static VasilhameListResult ListVasilhame(
        bool somenteDevedor, bool somenteVencido, string? search, DateTime? from, DateTime? to)
    {
        var q = new StringBuilder("api/vasilhame?");
        q.Append("somenteDevedor=").Append(somenteDevedor ? "1" : "0");
        q.Append("&somenteVencido=").Append(somenteVencido ? "1" : "0");
        if (!string.IsNullOrWhiteSpace(search))
            q.Append("&search=").Append(Uri.EscapeDataString(search));
        if (from is DateTime df)
            q.Append("&from=").Append(Uri.EscapeDataString(df.ToString("yyyy-MM-dd")));
        if (to is DateTime dt)
            q.Append("&to=").Append(Uri.EscapeDataString(dt.ToString("yyyy-MM-dd")));
        return Run(() => SendAsync<VasilhameListResult>(HttpMethod.Get, q.ToString()))
               ?? new VasilhameListResult();
    }

    public static int CreateVasilhameMovement(
        string kind,
        int containerTypeId,
        double quantity,
        int? customerId,
        string? borrowerName,
        string? borrowerPhone,
        DateTime? dueDate,
        string? notes) =>
        Run(() => SendAsync<StoreNetworkIdDto>(HttpMethod.Post, "api/vasilhame/movements", new
        {
            kind,
            containerTypeId,
            quantity,
            customerId,
            borrowerName,
            borrowerPhone,
            dueDate = dueDate?.ToString("yyyy-MM-dd"),
            notes,
        })).Id;

    public static void DeleteVasilhameMovement(int id) =>
        Run(() => SendAsync<StoreNetworkOkDto>(HttpMethod.Delete, $"api/vasilhame/movements/{id}"));

    public static MovimentacaoResult MovimentacaoProdutos(DateTime from, DateTime to, string? paymentType, int limit) =>
        MovimentacaoGet("produtos", from, to, paymentType, limit);

    public static MovimentacaoResult MovimentacaoVendas(DateTime from, DateTime to, string? paymentType, int limit) =>
        MovimentacaoGet("vendas", from, to, paymentType, limit);

    public static MovimentacaoResult MovimentacaoCompras(DateTime from, DateTime to, int limit) =>
        MovimentacaoGet("compras", from, to, null, limit);

    private static MovimentacaoResult MovimentacaoGet(
        string kind, DateTime from, DateTime to, string? paymentType, int limit)
    {
        var q =
            "api/movimentacao?kind=" + Uri.EscapeDataString(kind) +
            "&from=" + Uri.EscapeDataString(from.ToString("yyyy-MM-dd")) +
            "&to=" + Uri.EscapeDataString(to.ToString("yyyy-MM-dd")) +
            "&limit=" + limit;
        if (!string.IsNullOrWhiteSpace(paymentType))
            q += "&paymentType=" + Uri.EscapeDataString(paymentType);
        return Run(() => SendAsync<MovimentacaoResult>(HttpMethod.Get, q))
               ?? new MovimentacaoResult();
    }

    public static PdvResumoDia GetPdvResumoDia(DateTime? sessionDate)
    {
        var q = sessionDate is DateTime d
            ? "api/pdv/resumo?date=" + Uri.EscapeDataString(d.ToString("yyyy-MM-dd"))
            : "api/pdv/resumo";
        return Run(() => SendAsync<PdvResumoDia>(HttpMethod.Get, q))
               ?? new PdvResumoDia();
    }

    public static IReadOnlyList<PdvSaleListRow> ListPdvSales(DateTime? sessionDate, bool includeCancelled)
    {
        var q = "api/pdv/sales?includeCancelled=" + (includeCancelled ? "1" : "0");
        if (sessionDate is DateTime d)
            q += "&date=" + Uri.EscapeDataString(d.ToString("yyyy-MM-dd"));
        var dto = Run(() => SendAsync<StoreNetworkListDto<PdvSaleListRow>>(HttpMethod.Get, q));
        return dto.Items ?? [];
    }

    public static PdvSaleDetail GetPdvSaleDetail(int saleId) =>
        Run(() => SendAsync<PdvSaleDetail>(HttpMethod.Get, $"api/pdv/sales/{saleId}"))
        ?? throw new InvalidOperationException("Venda não encontrada.");

    public static NegocioDashboard GetDashboard(DateTime? from, DateTime? to, string dateMode, string rdDateMode) =>
        Run(() => SendAsync<NegocioDashboard>(HttpMethod.Post, "api/dashboard", new
        {
            from = from?.ToString("yyyy-MM-dd"),
            to = to?.ToString("yyyy-MM-dd"),
            dateMode,
            rdDateMode,
        })) ?? new NegocioDashboard();

    public static StockReportResult StockReport(StockReportKind kind, DateTime? from, DateTime? to, int limit) =>
        Run(() => SendAsync<StockReportResult>(HttpMethod.Post, "api/stock/report", new
        {
            kind = kind.ToString(),
            from = from?.ToString("yyyy-MM-dd"),
            to = to?.ToString("yyyy-MM-dd"),
            limit,
        })) ?? new StockReportResult { Kind = kind };
}

public sealed class StoreNetworkStatusDto
{
    public bool Ok { get; set; }
    public string? Store { get; set; }
    public string? Error { get; set; }
    public string? Role { get; set; }
    public int ApiVersion { get; set; }
    public List<string>? AuthModes { get; set; }
    public List<string>? Features { get; set; }
}

public sealed class StoreNetworkPairDto
{
    public bool Ok { get; set; }
    public string? Store { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? CreatedAt { get; set; }
    public string? Error { get; set; }
}

public sealed class StoreNetworkDeviceStatusDto
{
    public bool Ok { get; set; }
    public bool Authorized { get; set; }
    public bool Revoked { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? CreatedAt { get; set; }
    public string? LastSeenAt { get; set; }
    public string? Error { get; set; }
}

public sealed class StoreNetworkSessionDto
{
    public bool Ok { get; set; }
    public string? Token { get; set; }
    public string? ExpiresAt { get; set; }
    public StoreNetworkRemoteUserDto? User { get; set; }
    public string? Error { get; set; }
}

public sealed class StoreNetworkRemoteUserDto
{
    public int Id { get; set; }
    public string? Login { get; set; }
    public string? Name { get; set; }
    public string? Role { get; set; }
    public UserPermissions? Permissions { get; set; }
}

public sealed class StoreNetworkOkDto
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    /// <summary>Quantos itens tiveram sale_price aplicado na mesma tx da compra. Null = host antigo.</summary>
    public int? SalePriceUpdates { get; set; }
}

public sealed class StoreNetworkIdDto
{
    public bool Ok { get; set; }
    public int Id { get; set; }
    public string? Error { get; set; }
    /// <summary>Quantos itens tiveram sale_price aplicado na mesma tx da compra. Null = host antigo.</summary>
    public int? SalePriceUpdates { get; set; }
}

public sealed class StoreNetworkExistsDto
{
    public bool Exists { get; set; }
}

public sealed class StoreNetworkEnsurePayablesDto
{
    public bool Ok { get; set; }
    public bool Created { get; set; }
}

public sealed class StoreNetworkListDto<T>
{
    public bool Ok { get; set; }
    public List<T>? Items { get; set; }
}

public sealed class StoreNetworkItemDto<T>
{
    public bool Ok { get; set; }
    public T? Item { get; set; }
}
