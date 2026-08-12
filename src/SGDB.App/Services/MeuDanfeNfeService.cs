using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SGDB.Services;

/// <summary>
/// Baixa o XML da NF-e pela chave de acesso via API oficial do Meu Danfe (Api-Key).
/// </summary>
public static class MeuDanfeNfeService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    public static bool IsConfigured() => MeuDanfeCredentials.HasApiKey();

    /// <summary>
    /// Consulta a NF-e e devolve o XML completo (nfeProc / NFe).
    /// </summary>
    public static async Task<string> FetchXmlByChaveAsync(string chave44, CancellationToken ct = default)
    {
        var digits = new string((chave44 ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length != 44)
            throw new InvalidOperationException("A chave de acesso deve ter 44 dígitos.");

        var apiKey = MeuDanfeCredentials.TryLoadApiKey()
            ?? throw new InvalidOperationException(
                "Api-Key do Meu Danfe não configurada.\n\n" +
                "Em Dados da Empresa, cole a Api-Key (área do cliente → API / Integração).");

        await AddChaveAsync(digits, apiKey, ct).ConfigureAwait(false);
        return await GetXmlAsync(digits, apiKey, ct).ConfigureAwait(false);
    }

    private static async Task AddChaveAsync(string chave, string apiKey, CancellationToken ct)
    {
        // Mesmo endpoint usado por integrações oficiais: PUT /v2/fd/add/{chave}
        using var req = new HttpRequestMessage(HttpMethod.Put, $"https://api.meudanfe.com.br/v2/fd/add/{chave}");
        req.Headers.TryAddWithoutValidation("Api-Key", apiKey);

        using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if ((int)res.StatusCode == 401 || (int)res.StatusCode == 403)
            throw new InvalidOperationException(
                "Api-Key do Meu Danfe inválida ou sem permissão.\n\n" +
                "Gere uma nova em web.meudanfe.com.br → API / Integração.");

        if ((int)res.StatusCode == 429)
            throw new InvalidOperationException(
                "Muitas consultas em pouco tempo no Meu Danfe. Aguarde um momento e tente de novo.");

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Falha ao consultar a NF-e no Meu Danfe (HTTP {(int)res.StatusCode}).\n{TrimBody(body)}");

        // Alguns retornos são assíncronos (WAITING / SEARCHING).
        if (TryReadStatus(body, out var status) &&
            status is "WAITING" or "SEARCHING")
        {
            await WaitUntilReadyAsync(chave, apiKey, ct).ConfigureAwait(false);
        }
        else if (TryReadStatus(body, out status) && status == "NOT_FOUND")
        {
            throw new InvalidOperationException("Chave de acesso não encontrada no Meu Danfe / SEFAZ.");
        }
    }

    private static async Task WaitUntilReadyAsync(string chave, string apiKey, CancellationToken ct)
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(1500, ct).ConfigureAwait(false);
            using var req = new HttpRequestMessage(HttpMethod.Put, $"https://api.meudanfe.com.br/v2/fd/add/{chave}");
            req.Headers.TryAddWithoutValidation("Api-Key", apiKey);
            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) continue;
            if (!TryReadStatus(body, out var status)) return;
            if (status == "OK") return;
            if (status == "NOT_FOUND")
                throw new InvalidOperationException("Chave de acesso não encontrada no Meu Danfe / SEFAZ.");
        }
    }

    private static async Task<string> GetXmlAsync(string chave, string apiKey, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(1200, ct).ConfigureAwait(false);

            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.meudanfe.com.br/v2/fd/get/xml/{chave}");
            req.Headers.TryAddWithoutValidation("Api-Key", apiKey);

            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                last = new InvalidOperationException(
                    $"Não foi possível baixar o XML (HTTP {(int)res.StatusCode}).\n{TrimBody(body)}");
                continue;
            }

            var xml = ExtractXml(body);
            if (!string.IsNullOrWhiteSpace(xml) &&
                (xml.Contains("<NFe", StringComparison.OrdinalIgnoreCase) ||
                 xml.Contains("<nfeProc", StringComparison.OrdinalIgnoreCase) ||
                 xml.Contains("infNFe", StringComparison.OrdinalIgnoreCase)))
                return xml;

            last = new InvalidOperationException("Resposta do Meu Danfe sem XML válido.");
        }

        throw last ?? new InvalidOperationException("Timeout ao baixar o XML da NF-e.");
    }

    private static string? ExtractXml(string body)
    {
        body = body?.Trim() ?? "";
        if (body.StartsWith('<'))
            return body;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.String)
                    return data.GetString();
            }
            if (doc.RootElement.TryGetProperty("xml", out var xml))
            {
                if (xml.ValueKind == JsonValueKind.String)
                    return xml.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static bool TryReadStatus(string body, out string status)
    {
        status = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String)
            {
                status = (s.GetString() ?? "").Trim().ToUpperInvariant();
                return status.Length > 0;
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }

    private static string TrimBody(string? body)
    {
        body = (body ?? "").Trim();
        if (body.Length > 280)
            body = body[..280] + "…";
        return body;
    }
}
