using System.Globalization;

namespace SGDB.Services;

/// <summary>
/// Parse puro de validade/quantidade da 70D-B2. Sem CurrentCulture. Sem I/O.
/// </summary>
public static class InventoryProjectionLotParser
{
    public enum ExpiryKind
    {
        Missing = 0,
        ValidIso,
        Invalid,
    }

    public readonly record struct ExpiryParseResult(ExpiryKind Kind, DateTime? Date);

    /// <summary>
    /// NULL/vazio/espaços → Missing (Undated). yyyy-MM-dd invariante → ValidIso.
    /// Qualquer outro texto não vazio → Invalid (não vira Undated).
    /// </summary>
    public static ExpiryParseResult ParseExpiry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new(ExpiryKind.Missing, null);

        var s = raw.Trim();
        if (DateTime.TryParseExact(
                s,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
            return new(ExpiryKind.ValidIso, dt.Date);

        return new(ExpiryKind.Invalid, null);
    }

    /// <summary>
    /// Lê número SQLite sem formatar como texto de cultura. Falha → NaN (B1 bloqueia validade).
    /// </summary>
    public static double ReadSqliteNumber(object? value)
    {
        if (value is null or DBNull)
            return double.NaN;

        switch (value)
        {
            case double d:
                return d;
            case float f:
                return f;
            case decimal m:
                return (double)m;
            case long l:
                return l;
            case int i:
                return i;
            case short s:
                return s;
            case byte b:
                return b;
            case string text:
                if (double.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                    return parsed;
                return double.NaN;
            default:
                try
                {
                    return Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    return double.NaN;
                }
        }
    }
}
