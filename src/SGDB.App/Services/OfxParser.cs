using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SGDB.Services;

public sealed class OfxTransaction
{
    public string FitId { get; init; } = "";
    public DateTime PostedDate { get; init; }
    public double Amount { get; init; }
    public string Memo { get; init; } = "";
    public string Type { get; init; } = "";
}

/// <summary>Parser leve de extrato OFX/QFX (tags SGML comuns dos bancos BR).</summary>
public static class OfxParser
{
    private static readonly Regex TrnBlock = new(
        @"<STMTTRN>(.*?)</STMTTRN>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagValue = new(
        @"<(?<tag>[A-Z0-9\.]+)>(?<val>[^\r\n<]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<OfxTransaction> ParseFile(string path)
    {
        var raw = File.ReadAllText(path, DetectEncoding(path));
        return Parse(raw);
    }

    public static IReadOnlyList<OfxTransaction> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        // Remove headers OFX (até o primeiro <OFX>)
        var idx = content.IndexOf("<OFX", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            content = content[idx..];

        var list = new List<OfxTransaction>();
        foreach (Match block in TrnBlock.Matches(content))
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match t in TagValue.Matches(block.Groups[1].Value))
            {
                var tag = t.Groups["tag"].Value.Trim();
                var val = t.Groups["val"].Value.Trim();
                if (!map.ContainsKey(tag))
                    map[tag] = val;
            }

            if (!map.TryGetValue("TRNAMT", out var amtRaw) ||
                !TryParseAmount(amtRaw, out var amount))
                continue;

            var dateRaw = map.GetValueOrDefault("DTPOSTED")
                          ?? map.GetValueOrDefault("DTUSER")
                          ?? "";
            if (!TryParseOfxDate(dateRaw, out var date))
                continue;

            var fitId = map.GetValueOrDefault("FITID") ?? "";
            if (string.IsNullOrWhiteSpace(fitId))
                fitId = $"{date:yyyyMMdd}|{amount:0.00}|{map.GetValueOrDefault("MEMO") ?? ""}";

            var memo = map.GetValueOrDefault("MEMO")
                       ?? map.GetValueOrDefault("NAME")
                       ?? map.GetValueOrDefault("PAYEE")
                       ?? "";

            list.Add(new OfxTransaction
            {
                FitId = fitId.Trim(),
                PostedDate = date.Date,
                Amount = amount,
                Memo = memo.Trim(),
                Type = map.GetValueOrDefault("TRNTYPE") ?? "",
            });
        }

        return list;
    }

    private static Encoding DetectEncoding(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[Math.Min(4096, (int)fs.Length)];
            _ = fs.Read(buf, 0, buf.Length);
            var head = Encoding.ASCII.GetString(buf);
            var m = Regex.Match(head, @"CHARSET\s*:\s*(\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var cs = m.Groups[1].Value.Trim().ToUpperInvariant();
                if (cs is "UTF-8" or "UTF8") return Encoding.UTF8;
                if (cs is "WINDOWS-1252" or "1252" or "ISO-8859-1")
                    return Encoding.GetEncoding(1252);
            }
        }
        catch
        {
            /* fallback */
        }

        return Encoding.GetEncoding(1252);
    }

    private static bool TryParseAmount(string raw, out double amount)
    {
        amount = 0;
        raw = (raw ?? "").Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(raw)) return false;

        // Bancos BR às vezes usam 1.234,56; OFX padrão usa 1234.56
        if (raw.Contains(',') && raw.Contains('.'))
        {
            // 1.234,56 → remove milhar
            raw = raw.Replace(".", "").Replace(',', '.');
        }
        else if (raw.Contains(',') && !raw.Contains('.'))
        {
            raw = raw.Replace(',', '.');
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out amount);
    }

    private static bool TryParseOfxDate(string raw, out DateTime date)
    {
        date = default;
        raw = (raw ?? "").Trim();
        if (raw.Length < 8) return false;
        // YYYYMMDD[HHMMSS][...]
        var digits = new string(raw.TakeWhile(char.IsDigit).ToArray());
        if (digits.Length < 8) return false;
        if (DateTime.TryParseExact(digits[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
            return true;
        return false;
    }
}
