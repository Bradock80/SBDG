using System.Globalization;
using System.Text.RegularExpressions;

namespace SGDB.Domain.Purchases;

/// <summary>
/// Parser conservador de "Preco Unitario Final" em infAdProd (padrão Ambev/CRBS).
/// Não interpreta texto livre fora deste padrão testado.
/// </summary>
public static class NfeInfAdProdFinalPriceParser
{
    static readonly Regex Pattern = new(
        @"Pre[cç]o\s+Unit[aá]rio\s+Final\s*:\s*([0-9]{1,6}(?:[.,][0-9]{1,6})?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? infAdProd, out double unitPrice)
    {
        unitPrice = 0;
        if (string.IsNullOrWhiteSpace(infAdProd))
            return false;

        var match = Pattern.Match(infAdProd);
        if (!match.Success)
            return false;

        return TryParseBr(match.Groups[1].Value, out unitPrice) && unitPrice > 0;
    }

    public static bool TryParseBr(string raw, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var t = raw.Trim();
        if (t.Contains(',') && t.Contains('.'))
            t = t.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        else if (t.Contains(','))
            t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value);
    }
}
