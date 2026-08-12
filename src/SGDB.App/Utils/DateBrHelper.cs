using System.Globalization;

namespace SGDB.Utils;

/// <summary>
/// Datas civis (dd/MM/yyyy) e timestamps do banco.
/// O Gestão grava created_at em UTC (naive); na tela converte para America/Sao_Paulo (UTC−3).
/// </summary>
public static class DateBrHelper
{
    /// <summary>Brasil sem horário de verão desde 2019.</summary>
    public static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    public static string TodayIso() => TodayBrDate().ToString("yyyy-MM-dd");

    public static string TodayBr() => TodayBrDate().ToString("dd/MM/yyyy");

    /// <summary>Data civil de hoje em Brasília (não depende do fuso do Windows).</summary>
    public static DateTime TodayBrDate() => UtcNowAsBrazil().Date;

    public static DateTime UtcNowAsBrazil() =>
        DateTime.UtcNow + BrazilOffset;

    /// <summary>Gravação no banco: UTC naive, igual ao datetime.utcnow do Gestão.</summary>
    public static string NowUtcIso() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);

    public static string FormatIso(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return "";
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToString("dd/MM/yyyy")
            : iso;
    }

    public static string? ToIso(string? br)
    {
        if (string.IsNullOrWhiteSpace(br))
            return null;
        if (!TryParseBr(br, out var dt))
            return null;
        return dt.ToString("yyyy-MM-dd");
    }

    public static bool TryParseBr(string? text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        // Digita só números: 14072026 → 14/07/2026
        var digits = new string(s.Where(char.IsDigit).ToArray());
        if (digits.Length == 8)
            s = $"{digits[..2]}/{digits[2..4]}/{digits[4..]}";
        else if (digits.Length == 6)
            s = $"{digits[..2]}/{digits[2..4]}/20{digits[4..]}";

        return DateTime.TryParseExact(
                   s,
                   ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy"],
                   CultureInfo.GetCultureInfo("pt-BR"),
                   DateTimeStyles.None,
                   out date)
               || DateTime.TryParse(s, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out date);
    }

    public static string AddDaysBr(string? br, int days)
    {
        var iso = ToIso(br) ?? TodayIso();
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return br ?? TodayBr();
        return dt.AddDays(days).ToString("dd/MM/yyyy");
    }

    public static string AddMonthsBr(string? br, int months)
    {
        var iso = ToIso(br) ?? TodayIso();
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return br ?? TodayBr();
        return dt.AddMonths(months).ToString("dd/MM/yyyy");
    }

    /// <summary>
    /// Interpreta o texto do banco como UTC naive e devolve horário de Brasília.
    /// </summary>
    public static DateTime ParseUtcToBrazil(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return UtcNowAsBrazil();

        var s = iso.Trim();
        // Aceita "2026-07-24 16:37:55.083375" e ISO com T / Z
        if (s.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            s = s[..^1];
        s = s.Replace('T', ' ');

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
            || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out utc)
            || DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out utc))
        {
            if (utc.Kind == DateTimeKind.Local)
                utc = utc.ToUniversalTime();
            else if (utc.Kind == DateTimeKind.Unspecified)
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return utc + BrazilOffset;
        }

        return UtcNowAsBrazil();
    }

    public static string FormatUtcToBrazil(string? iso, string format = "dd/MM/yyyy HH:mm")
    {
        if (string.IsNullOrWhiteSpace(iso))
            return "";
        return ParseUtcToBrazil(iso).ToString(format, CultureInfo.GetCultureInfo("pt-BR"));
    }

    public static string FormatUtcToBrazil(DateTime utcOrUnspecified, string format)
    {
        DateTime utc = utcOrUnspecified.Kind switch
        {
            DateTimeKind.Local => utcOrUnspecified.ToUniversalTime(),
            DateTimeKind.Utc => utcOrUnspecified,
            _ => DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc),
        };
        return (utc + BrazilOffset).ToString(format, CultureInfo.GetCultureInfo("pt-BR"));
    }
}
