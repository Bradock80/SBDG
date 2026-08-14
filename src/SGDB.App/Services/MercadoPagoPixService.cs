using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SGDB.Services;

public sealed class MercadoPagoPixCharge
{
    public long PaymentId { get; init; }
    public string Status { get; init; } = "";
    public string StatusDetail { get; init; } = "";
    public string QrCode { get; init; } = "";
    public string? QrCodeBase64 { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public static class MercadoPagoPixService
{
    public static IMercadoPagoPixGateway Gateway { get; set; } = new HttpMercadoPagoPixGateway();

    public static Task<MercadoPagoPixCharge> CreatePixAsync(
        double amount,
        string description,
        string? payerEmail = null,
        CancellationToken ct = default) =>
        Gateway.CreatePixAsync(
            amount, description, Guid.NewGuid().ToString("N"), payerEmail, ct);

    public static Task<MercadoPagoPixCharge> GetPaymentAsync(long paymentId, CancellationToken ct = default) =>
        Gateway.GetPaymentAsync(paymentId, ct);

    public static Task CancelPaymentAsync(long paymentId, CancellationToken ct = default) =>
        Gateway.CancelPaymentAsync(paymentId, ct);

    public static Task RefundPaymentAsync(long paymentId, CancellationToken ct = default) =>
        Gateway.RefundPaymentAsync(paymentId, Guid.NewGuid().ToString("N"), ct);

    private sealed class HttpMercadoPagoPixGateway : IMercadoPagoPixGateway
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return c;
        }

        public async Task<MercadoPagoPixCharge> CreatePixAsync(
            double amount,
            string description,
            string idempotencyKey,
            string? payerEmail,
            CancellationToken ct)
        {
            var token = MercadoPagoCredentials.TryLoadAccessToken()
                ?? throw new InvalidOperationException("Access Token do Mercado Pago não configurado.");

            amount = Math.Round(Math.Max(0.01, amount), 2, MidpointRounding.AwayFromZero);
            var email = NormalizeEmail(payerEmail);
            var body = new Dictionary<string, object?>
            {
                ["transaction_amount"] = amount,
                ["description"] = string.IsNullOrWhiteSpace(description) ? "Venda PDV" : description.Trim(),
                ["payment_method_id"] = "pix",
                ["payer"] = new Dictionary<string, object?>
                {
                    ["email"] = email,
                },
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.mercadopago.com/v1/payments");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("X-Idempotency-Key",
                string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(FormatApiError(resp.StatusCode, json));

            return ParseCharge(json);
        }

        public async Task<MercadoPagoPixCharge> GetPaymentAsync(long paymentId, CancellationToken ct)
        {
            var token = MercadoPagoCredentials.TryLoadAccessToken()
                ?? throw new InvalidOperationException("Access Token do Mercado Pago não configurado.");

            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.mercadopago.com/v1/payments/{paymentId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(FormatApiError(resp.StatusCode, json));

            return ParseCharge(json);
        }

        public async Task CancelPaymentAsync(long paymentId, CancellationToken ct)
        {
            var token = MercadoPagoCredentials.TryLoadAccessToken()
                ?? throw new InvalidOperationException("Access Token do Mercado Pago não configurado.");
            if (paymentId <= 0)
                throw new InvalidOperationException("payment_id inválido.");

            var body = """{"status":"cancelled"}""";
            using var req = new HttpRequestMessage(HttpMethod.Put, $"https://api.mercadopago.com/v1/payments/{paymentId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(FormatApiError(resp.StatusCode, json));
        }

        public async Task RefundPaymentAsync(long paymentId, string idempotencyKey, CancellationToken ct)
        {
            var token = MercadoPagoCredentials.TryLoadAccessToken()
                ?? throw new InvalidOperationException("Access Token do Mercado Pago não configurado.");
            if (paymentId <= 0)
                throw new InvalidOperationException("payment_id inválido.");

            using var req = new HttpRequestMessage(
                HttpMethod.Post, $"https://api.mercadopago.com/v1/payments/{paymentId}/refunds");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("X-Idempotency-Key",
                string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey);
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(FormatApiError(resp.StatusCode, json));
        }
    }

    internal static MercadoPagoPixCharge ParseCharge(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0L;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
        var detail = root.TryGetProperty("status_detail", out var sd) ? sd.GetString() ?? "" : "";

        string qr = "";
        string? qrB64 = null;
        DateTime? exp = null;

        if (root.TryGetProperty("point_of_interaction", out var poi) &&
            poi.TryGetProperty("transaction_data", out var td))
        {
            if (td.TryGetProperty("qr_code", out var qrEl))
                qr = qrEl.GetString() ?? "";
            if (td.TryGetProperty("qr_code_base64", out var b64El))
                qrB64 = b64El.GetString();
        }

        if (root.TryGetProperty("date_of_expiration", out var expEl) &&
            expEl.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(expEl.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var expDt))
        {
            exp = expDt.ToLocalTime();
        }

        return new MercadoPagoPixCharge
        {
            PaymentId = id,
            Status = status,
            StatusDetail = detail,
            QrCode = qr,
            QrCodeBase64 = qrB64,
            ExpiresAt = exp,
        };
    }

    private static string NormalizeEmail(string? email)
    {
        email = (email ?? "").Trim();
        if (email.Contains('@') && email.Length >= 5)
            return email;
        var company = AppSettingsService.GetCompanyProfile().Email?.Trim();
        if (!string.IsNullOrEmpty(company) && company.Contains('@'))
            return company;
        return "cliente@email.com";
    }

    private static string FormatApiError(System.Net.HttpStatusCode code, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg))
                return $"Mercado Pago ({(int)code}): {msg.GetString()}";
            if (root.TryGetProperty("cause", out var cause) && cause.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cause.EnumerateArray())
                {
                    if (c.TryGetProperty("description", out var d))
                        return $"Mercado Pago ({(int)code}): {d.GetString()}";
                }
            }
        }
        catch
        {
            // fall through
        }

        var shortJson = json.Length > 240 ? json[..240] + "…" : json;
        return $"Mercado Pago ({(int)code}): {shortJson}";
    }
}
