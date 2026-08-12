using System.Text;
using SGDB.Models;

namespace SGDB.Services;

public static class ReceiptPreviewBuilder
{
    public static ReceiptPreviewData Build(int paperWidthMm, string footerText, bool autoCut)
    {
        var cols = paperWidthMm <= 58 ? 32 : 42;
        var isNarrow = cols <= 32;
        var company = AppSettingsService.GetCompanyProfile();

        var name = !string.IsNullOrWhiteSpace(company.NomeFantasia)
            ? company.NomeFantasia.Trim().ToUpperInvariant()
            : !string.IsNullOrWhiteSpace(company.RazaoSocial)
                ? company.RazaoSocial.Trim().ToUpperInvariant()
                : AppSettingsService.GetNomeDeposito().ToUpperInvariant();

        var header = string.Join("\n", WrapWord(name, cols));

        var meta = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(company.Cnpj))
            meta.AppendLine(CenterLine($"CNPJ: {company.Cnpj.Trim()}", cols));
        if (!string.IsNullOrWhiteSpace(company.AddressLine))
        {
            foreach (var line in WrapWord(company.AddressLine, cols))
                meta.AppendLine(CenterLine(line, cols));
        }
        if (!string.IsNullOrWhiteSpace(company.Telefone))
            meta.AppendLine(CenterLine($"Tel: {company.Telefone.Trim()}", cols));
        meta.AppendLine(CenterLine(DateTime.Now.ToString("dd/MM/yyyy  HH:mm:ss"), cols));

        var body = new StringBuilder();
        body.AppendLine(new string('-', cols));
        body.AppendLine(Pad3("QTD", "DESCRICAO", "VALOR", cols));
        body.AppendLine(new string('-', cols));
        AppendItem(body, "2", "CERVEJA LATA 350ML", "9,80", cols);
        AppendItem(body, "1", "AGUA MINERAL 500ML", "3,50", cols);
        AppendItem(body, "1", "GELO 5KG", "12,00", cols);
        body.AppendLine(new string('-', cols));
        body.AppendLine(PadLine("SUBTOTAL", "R$ 25,30", cols));
        body.AppendLine(PadLine("DESCONTO", "R$ 0,00", cols));
        body.AppendLine(PadLine("TOTAL", "R$ 25,30", cols));
        body.AppendLine(new string('-', cols));
        body.AppendLine(PadLine("DINHEIRO", "R$ 30,00", cols));
        body.AppendLine(PadLine("TROCO", "R$ 4,70", cols));
        body.AppendLine(new string('-', cols));

        var footerLines = WrapParagraph(footerText ?? "", cols);
        var footer = footerLines.Count == 0
            ? "(sem texto de rodapé)"
            : string.Join("\n", footerLines);

        var serrateLen = isNarrow ? 18 : 26;

        return new ReceiptPreviewData
        {
            PaperWidthMm = paperWidthMm,
            CharsPerLine = cols,
            Header = header,
            Meta = meta.ToString().TrimEnd(),
            Body = body.ToString().TrimEnd(),
            Footer = footer,
            Barcode = isNarrow ? "|||| |||| | ||| |||| |" : "|||| |||| | ||| |||| | |||| |||",
            QrHint = isNarrow ? "[QR NFC-e]" : "[ QR CODE NFC-e ]",
            CutHint = autoCut ? "✂ corte automático" : "— sem corte automático",
            WidthLabel = isNarrow ? "Bobina 58 mm (estreita)" : "Bobina 80 mm (padrão)",
            SerratePattern = string.Concat(Enumerable.Repeat("▲▼", serrateLen / 2)),
        };
    }

    public static int CharsForWidth(int paperWidthMm) => paperWidthMm <= 58 ? 32 : 42;

    public static int CountLongFooterLines(string footer, int cols) =>
        WrapParagraph(footer ?? "", cols).Count(l => l.Length > cols);

    private static void AppendItem(StringBuilder sb, string qty, string desc, string value, int cols)
    {
        var val = "R$ " + value;
        var qtyPart = qty.PadLeft(3) + " ";
        var descMax = Math.Max(6, cols - qtyPart.Length - val.Length - 1);
        var d = desc.Length <= descMax ? desc : desc[..Math.Max(1, descMax - 1)] + ".";
        sb.AppendLine(PadLine(qtyPart + d, val, cols));
    }

    public static List<string> WrapWord(string text, int cols)
    {
        var result = new List<string>();
        text = (text ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return result;

        var words = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var line = new StringBuilder();
        foreach (var w in words)
        {
            if (w.Length > cols)
            {
                if (line.Length > 0)
                {
                    result.Add(line.ToString());
                    line.Clear();
                }
                for (var i = 0; i < w.Length; i += cols)
                    result.Add(w.Substring(i, Math.Min(cols, w.Length - i)));
                continue;
            }

            if (line.Length == 0)
                line.Append(w);
            else if (line.Length + 1 + w.Length <= cols)
                line.Append(' ').Append(w);
            else
            {
                result.Add(line.ToString());
                line.Clear();
                line.Append(w);
            }
        }
        if (line.Length > 0)
            result.Add(line.ToString());
        return result;
    }

    public static List<string> WrapParagraph(string text, int cols)
    {
        var result = new List<string>();
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            result.AddRange(WrapWord(raw.Trim(), cols));
        }
        return result;
    }

    private static string CenterLine(string s, int cols)
    {
        s = s.Trim();
        if (s.Length >= cols) return s[..cols];
        var pad = (cols - s.Length) / 2;
        return new string(' ', pad) + s;
    }

    private static string PadLine(string left, string right, int cols)
    {
        left = left.TrimEnd();
        right = right.Trim();
        var space = cols - left.Length - right.Length;
        if (space < 1)
        {
            var keep = Math.Max(1, cols - right.Length - 1);
            left = left.Length <= keep ? left : left[..keep];
            space = cols - left.Length - right.Length;
            if (space < 1) return (left + right)[..Math.Min(cols, left.Length + right.Length)];
        }
        return left + new string(' ', space) + right;
    }

    private static string Pad3(string a, string b, string c, int cols) =>
        PadLine(a.PadRight(4) + b, c, cols);
}
