using System.Text.RegularExpressions;

namespace SGDB.Utils;

public static class TextNorm
{
    public static string? UpperStr(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim().ToUpperInvariant();
    }

    public static string? NormalizeBarcode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? null : digits;
    }

    /// <summary>
    /// Código da caixa/fardo só é válido se for diferente do código da unidade.
    /// </summary>
    public static string? DistinctPackBarcode(string? packBarcode, string? unitBarcode)
    {
        var pack = NormalizeBarcode(packBarcode);
        if (pack is null)
            return null;
        var unit = NormalizeBarcode(unitBarcode);
        if (unit is null)
            return pack;
        if (pack == unit || pack.TrimStart('0') == unit.TrimStart('0'))
            return null;
        return pack;
    }

    public static string? ReferenciaFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var n = name.Trim().ToUpperInvariant();
        var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Regex.Replace(w, @"[^A-Z0-9]", ""))
            .Where(w => w.Length > 0)
            .ToList();

        if (words.Count == 0)
            return null;

        if (words.Count == 1)
            return words[0][..Math.Min(words[0].Length, 40)];

        var reference = "";
        foreach (var w in words)
        {
            if (char.IsLetterOrDigit(w[0]))
                reference += w[0];
            foreach (var ch in w[1..])
            {
                if (char.IsDigit(ch))
                    reference += ch;
            }
        }

        if (reference.Length < 2)
            reference = Regex.Replace(n, @"[^A-Z0-9]", "")[..Math.Min(12, Regex.Replace(n, @"[^A-Z0-9]", "").Length)];

        return string.IsNullOrEmpty(reference) ? null : reference[..Math.Min(reference.Length, 40)];
    }

    public static string? DigitsOnly(string? value, int? maxLen = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits))
            return null;
        if (maxLen is not null && digits.Length > maxLen.Value)
            digits = digits[..maxLen.Value];
        return digits;
    }

    public static string? UpperState(string? value)
    {
        var s = UpperStr(value);
        if (s is null)
            return null;
        return s.Length > 2 ? s[..2] : s;
    }
}
