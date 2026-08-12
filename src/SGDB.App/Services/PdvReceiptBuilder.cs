using System.Globalization;
using System.Text;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>Monta o texto do cupom não fiscal do PDV (preview + impressão térmica).</summary>
public static class PdvReceiptBuilder
{
    public sealed record ReceiptDocument(int CharsPerLine, int PaperWidthMm, IReadOnlyList<string> Lines)
    {
        public string Text => string.Join(Environment.NewLine, Lines);
    }

    public static ReceiptDocument Build(
        int saleId,
        IReadOnlyList<PdvCartLine> items,
        IReadOnlyList<PdvPaymentPart> payments,
        double subtotal,
        double discount,
        double surcharge,
        double total,
        double cashReceived,
        double changeAmount,
        string? operatorName = null)
    {
        var printer = AppSettingsService.GetPrinterSettings();
        var widthMm = printer.PaperWidthMm is 58 or 80 ? printer.PaperWidthMm : 80;
        var cols = ReceiptPreviewBuilder.CharsForWidth(widthMm);
        var lines = new List<string>();

        foreach (var h in AppSettingsService.BuildReceiptHeaderLines())
        {
            foreach (var wrapped in ReceiptPreviewBuilder.WrapWord(h, cols))
                lines.Add(Center(wrapped, cols));
        }

        if (lines.Count == 0)
            lines.Add(Center(AppSettingsService.GetNomeDeposito().ToUpperInvariant(), cols));

        lines.Add(Center("CUPOM NAO FISCAL", cols));
        lines.Add(Dash(cols));
        lines.Add(PadLine($"VENDA #{saleId}", DateTime.Now.ToString("dd/MM/yy HH:mm", CultureInfo.CurrentCulture), cols));
        if (!string.IsNullOrWhiteSpace(operatorName))
            lines.Add(Truncate($"Operador: {operatorName.Trim()}", cols));
        lines.Add(Dash(cols));
        lines.Add(PadLine("ITEM", "TOTAL", cols));
        lines.Add(Dash(cols));

        var idx = 1;
        foreach (var item in items)
        {
            var name = item.Name.Trim();
            foreach (var part in ReceiptPreviewBuilder.WrapWord($"{idx:00} {name}", cols))
                lines.Add(part);

            var qtyUnit = $"{item.Quantity:0.###} {item.Unit}".Trim();
            var detailLeft = $"{qtyUnit} x {item.UnitPrice:N2}";
            lines.Add(PadLine(detailLeft, item.Subtotal.ToString("N2", CultureInfo.CurrentCulture), cols));
            idx++;
        }

        lines.Add(Dash(cols));
        lines.Add(MoneyRow("SUBTOTAL", subtotal, cols));
        if (discount > 0.009)
            lines.Add(MoneyRow("DESCONTO", -discount, cols));
        if (surcharge > 0.009)
            lines.Add(MoneyRow("ACRESCIMO", surcharge, cols));
        lines.Add(MoneyRow("TOTAL", total, cols));
        lines.Add(Dash(cols));
        lines.Add("PAGAMENTO");

        if (payments.Count > 0)
        {
            foreach (var pay in payments)
                lines.Add(MoneyRow(pay.PaymentType.ToUpperInvariant(), pay.Amount, cols));
        }
        else
        {
            lines.Add(MoneyRow("DINHEIRO", total, cols));
        }

        if (cashReceived > 0.009)
            lines.Add(MoneyRow("RECEBIDO", cashReceived, cols));
        if (changeAmount > 0.009)
            lines.Add(MoneyRow("TROCO", changeAmount, cols));

        lines.Add(Dash(cols));

        var footer = printer.FooterText;
        var footerLines = ReceiptPreviewBuilder.WrapParagraph(footer ?? "", cols);
        if (footerLines.Count == 0)
            lines.Add(Center("OBRIGADO PELA PREFERENCIA", cols));
        else
        {
            foreach (var fl in footerLines)
                lines.Add(Center(fl, cols));
        }

        return new ReceiptDocument(cols, widthMm, lines);
    }

    private static string MoneyRow(string label, double value, int cols)
    {
        var right = "R$ " + Math.Abs(value).ToString("N2", CultureInfo.CurrentCulture);
        if (value < 0)
            right = "-" + right;
        return PadLine(label, right, cols);
    }

    private static string PadLine(string left, string right, int cols)
    {
        left = (left ?? "").TrimEnd();
        right = (right ?? "").Trim();
        var space = cols - left.Length - right.Length;
        if (space < 1)
        {
            var keep = Math.Max(1, cols - right.Length - 1);
            left = left.Length <= keep ? left : left[..keep];
            space = Math.Max(1, cols - left.Length - right.Length);
        }
        return left + new string(' ', space) + right;
    }

    private static string Center(string text, int cols)
    {
        text = (text ?? "").Trim();
        if (text.Length >= cols)
            return text[..cols];
        var pad = (cols - text.Length) / 2;
        return new string(' ', pad) + text;
    }

    private static string Truncate(string text, int cols) =>
        text.Length <= cols ? text : text[..cols];

    private static string Dash(int cols) => new('-', cols);
}
