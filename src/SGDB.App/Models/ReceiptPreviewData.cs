namespace SGDB.Models;

public sealed class ReceiptPreviewData
{
    public int PaperWidthMm { get; init; } = 80;
    public int CharsPerLine { get; init; } = 42;
    public string Header { get; init; } = "";
    public string Meta { get; init; } = "";
    public string Body { get; init; } = "";
    public string Footer { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string QrHint { get; init; } = "[ QR CODE NFC-e ]";
    public string CutHint { get; init; } = "";
    public string WidthLabel { get; init; } = "Bobina 80 mm";
    public string SerratePattern { get; init; } = "";

    /// <summary>Largura física simulada do papel em pixels (96 DPI).</summary>
    public double PaperWidthPx => PaperWidthMm <= 58 ? 219 : 302;

    public double PaperPaddingPx => 12;

    public double BodyFontSize => PaperWidthMm <= 58 ? 10 : 11;
    public double HeaderFontSize => PaperWidthMm <= 58 ? 11 : 12;
    public double MetaFontSize => PaperWidthMm <= 58 ? 9 : 10;
    public double BarcodeFontSize => PaperWidthMm <= 58 ? 14 : 18;
}
