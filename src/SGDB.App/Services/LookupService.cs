using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using SGDB.Utils;

namespace SGDB.Services;

public sealed class CepLookupResult
{
    public string Cep { get; init; } = "";
    public string? Address { get; init; }
    public string? Neighborhood { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Complement { get; init; }
}

public sealed class CnpjLookupResult
{
    public string CpfCnpj { get; init; } = "";
    public string? Name { get; init; }
    public string? TradeName { get; init; }
    public string? Cep { get; init; }
    public string? Address { get; init; }
    public string? AddressNumber { get; init; }
    public string? Complement { get; init; }
    public string? Neighborhood { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
}

public static class LookupService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SGDB-Nativo/1.0");
        return client;
    }

    public static async Task<CepLookupResult> LookupCepAsync(string cep, CancellationToken ct = default)
    {
        var digits = TextNorm.DigitsOnly(cep, 8);
        if (digits is null || digits.Length != 8)
            throw new InvalidOperationException("CEP deve ter 8 dígitos (ex.: 27130-130).");

        try
        {
            using var response = await Http.GetAsync($"https://viacep.com.br/ws/{digits}/json/", ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("CEP não encontrado.");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("erro", out var erro) && erro.ValueKind == JsonValueKind.True)
                throw new InvalidOperationException("CEP não encontrado.");

            var complement = GetString(root, "complemento");
            return new CepLookupResult
            {
                Cep = FormatCep(digits),
                Address = GetString(root, "logradouro"),
                Neighborhood = GetString(root, "bairro"),
                City = GetString(root, "localidade"),
                State = TextNorm.UpperState(GetString(root, "uf")),
                Complement = string.IsNullOrWhiteSpace(complement) ? null : complement,
            };
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Sem conexão com a internet.");
        }
    }

    public static async Task<CnpjLookupResult> LookupCnpjAsync(string cnpj, CancellationToken ct = default)
    {
        var digits = TextNorm.DigitsOnly(cnpj, 14);
        if (digits is null || digits.Length != 14)
            throw new InvalidOperationException("CNPJ deve ter 14 dígitos (formato 00.000.000/0000-00).");

        try
        {
            return await LookupCnpjBrasilApiAsync(digits, ct);
        }
        catch (InvalidOperationException)
        {
            return await LookupCnpjReceitaWsAsync(digits, ct);
        }
    }

    private static async Task<CnpjLookupResult> LookupCnpjBrasilApiAsync(string digits, CancellationToken ct)
    {
        using var response = await Http.GetAsync($"https://brasilapi.com.br/api/cnpj/v1/{digits}", ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("CNPJ não encontrado.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var tipoLog = GetString(root, "descricao_tipo_de_logradouro");
        var logradouro = GetString(root, "logradouro");
        var address = !string.IsNullOrWhiteSpace(tipoLog) && !string.IsNullOrWhiteSpace(logradouro)
            ? $"{tipoLog} {logradouro}".Trim()
            : logradouro;

        var phones = new List<string>();
        foreach (var key in new[] { "ddd_telefone_1", "ddd_telefone_2" })
        {
            var raw = GetString(root, key);
            if (!string.IsNullOrWhiteSpace(raw))
                phones.Add(OnlyPhoneDigits(raw));
        }

        var cepDigits = TextNorm.DigitsOnly(GetString(root, "cep"), 8);
        return new CnpjLookupResult
        {
            CpfCnpj = FormatCnpj(digits),
            Name = TextNorm.UpperStr(GetString(root, "razao_social")),
            TradeName = TextNorm.UpperStr(GetString(root, "nome_fantasia")),
            Cep = cepDigits is { Length: 8 } ? FormatCep(cepDigits) : null,
            Address = TextNorm.UpperStr(address),
            AddressNumber = TextNorm.UpperStr(GetString(root, "numero")),
            Complement = TextNorm.UpperStr(GetString(root, "complemento")),
            Neighborhood = TextNorm.UpperStr(GetString(root, "bairro")),
            City = TextNorm.UpperStr(GetString(root, "municipio")),
            State = TextNorm.UpperState(GetString(root, "uf")),
            Email = string.IsNullOrWhiteSpace(GetString(root, "email"))
                ? null
                : GetString(root, "email")!.Trim().ToLowerInvariant(),
            Phone = phones.FirstOrDefault(),
        };
    }

    private static async Task<CnpjLookupResult> LookupCnpjReceitaWsAsync(string digits, CancellationToken ct)
    {
        using var response = await Http.GetAsync($"https://www.receitaws.com.br/v1/cnpj/{digits}", ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("CNPJ não encontrado.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        if (string.Equals(GetString(root, "status"), "ERROR", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(GetString(root, "message") ?? "CNPJ não encontrado.");

        var cepDigits = TextNorm.DigitsOnly(GetString(root, "cep"), 8);
        var phone = GetString(root, "telefone");
        return new CnpjLookupResult
        {
            CpfCnpj = FormatCnpj(digits),
            Name = TextNorm.UpperStr(GetString(root, "nome")),
            TradeName = TextNorm.UpperStr(GetString(root, "fantasia")),
            Cep = cepDigits is { Length: 8 } ? FormatCep(cepDigits) : GetString(root, "cep"),
            Address = TextNorm.UpperStr(GetString(root, "logradouro")),
            AddressNumber = TextNorm.UpperStr(GetString(root, "numero")),
            Complement = TextNorm.UpperStr(GetString(root, "complemento")),
            Neighborhood = TextNorm.UpperStr(GetString(root, "bairro")),
            City = TextNorm.UpperStr(GetString(root, "municipio")),
            State = TextNorm.UpperState(GetString(root, "uf")),
            Email = string.IsNullOrWhiteSpace(GetString(root, "email"))
                ? null
                : GetString(root, "email")!.Trim().ToLowerInvariant(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : OnlyPhoneDigits(phone),
        };
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()?.Trim()
            : null;

    private static string OnlyPhoneDigits(string value) =>
        Regex.Replace(value, @"\D", "");

    public static string FormatCep(string digits) =>
        digits.Length == 8 ? $"{digits[..5]}-{digits[5..]}" : digits;

    public static string FormatCnpj(string digits) =>
        digits.Length == 14
            ? $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}"
            : digits;

    /// <summary>Formata telefone BR: (24) 99999-9999 ou (24) 9999-9999.</summary>
    public static string FormatPhone(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
        if (digits.Length > 11)
            digits = digits[..11];
        return digits.Length switch
        {
            11 => $"({digits[..2]}) {digits[2..7]}-{digits[7..]}",
            10 => $"({digits[..2]}) {digits[2..6]}-{digits[6..]}",
            >= 6 => $"({digits[..2]}) {digits[2..]}",
            >= 3 => $"({digits[..2]}) {digits[2..]}",
            2 => $"({digits}",
            _ => digits,
        };
    }

    /// <summary>Formata CNPJ enquanto digita (só dígitos → máscara).</summary>
    public static string FormatCnpjTyping(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
        if (digits.Length > 14)
            digits = digits[..14];
        if (digits.Length <= 2) return digits;
        if (digits.Length <= 5) return $"{digits[..2]}.{digits[2..]}";
        if (digits.Length <= 8) return $"{digits[..2]}.{digits[2..5]}.{digits[5..]}";
        if (digits.Length <= 12) return $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..]}";
        return FormatCnpj(digits);
    }

    /// <summary>Formata CEP enquanto digita.</summary>
    public static string FormatCepTyping(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
        if (digits.Length > 8)
            digits = digits[..8];
        return digits.Length > 5 ? $"{digits[..5]}-{digits[5..]}" : digits;
    }
}
