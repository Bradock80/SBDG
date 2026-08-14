using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PdvCupomConfirmWindow : Window
{
    private readonly PdvReceiptBuilder.ReceiptDocument _document;
    private readonly string _printerHint;

    public bool CancelSaleRequested { get; private set; }

    /// <summary>Usuário quer voltar às formas de pagamento (estorna a venda, mantém o carrinho).</summary>
    public bool BackToPaymentRequested { get; private set; }

    public PdvCupomConfirmWindow(
        int saleId,
        IReadOnlyList<PdvCartLine> items,
        IReadOnlyList<PdvPaymentPart> payments,
        double subtotal,
        double discount,
        double surcharge,
        double total,
        double cashReceived,
        double changeAmount)
    {
        InitializeComponent();

        var printer = AppSettingsService.GetPrinterSettings();
        _document = PdvReceiptBuilder.Build(
            saleId, items, payments, subtotal, discount, surcharge, total,
            cashReceived, changeAmount, AppSession.CurrentUser?.Nome);

        MessageText.Text = $"Venda #{saleId} concluída — R$ {ProductPriceHelper.FormatBr(total)}";
        if (changeAmount > 0)
            MessageText.Text += $"\nTroco: R$ {ProductPriceHelper.FormatBr(changeAmount)}";

        CupomText.Text = _document.Text;
        Loaded += (_, _) => FitCupomPreview();
        FitCupomPreview();

        _printerHint = string.IsNullOrWhiteSpace(printer.PrinterName)
            ? "Nenhuma impressora configurada — configure em Sistema → Impressoras"
            : $"{printer.PrinterName} · {_document.PaperWidthMm} mm · {printer.Copies} via(s)";
        HintText.Text = $"Prévia — {_printerHint} · S imprime · N não imprime · Esc volta";
    }

    private void CupomHost_SizeChanged(object sender, SizeChangedEventArgs e) { }

    /// <summary>
    /// Ajusta a largura do papel da prévia ao texto monoespaçado.
    /// Sem barra de rolagem: o Viewbox escala o cupom inteiro na área disponível.
    /// </summary>
    private void FitCupomPreview()
    {
        var fontSize = _document.PaperWidthMm <= 58 ? 15.0 : 17.0;
        CupomText.FontSize = fontSize;

        var typeface = new Typeface(
            new FontFamily("Consolas, Courier New"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        double dpi = 1.0;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
        catch { /* design-time / before load */ }

        double maxLineWidth = 0;
        foreach (var line in _document.Lines)
        {
            var sample = string.IsNullOrEmpty(line) ? " " : line;
            var ft = new FormattedText(
                sample,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                dpi);
            maxLineWidth = Math.Max(maxLineWidth, ft.WidthIncludingTrailingWhitespace);
        }

        if (maxLineWidth < 8)
            maxLineWidth = _document.CharsPerLine * fontSize * 0.62;

        var paperWidth = Math.Ceiling(maxLineWidth) + 48;
        CupomPaper.MinWidth = paperWidth;
        CupomPaper.Width = paperWidth;
        CupomPaper.ClearValue(HeightProperty);

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1100, Math.Max(820, workArea.Width * 0.55));
        Height = Math.Min(960, Math.Max(820, workArea.Height * 0.88));
        MinWidth = 720;
        MinHeight = 780;
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PeripheralService.PrintEscPosDocument(_document.Lines);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            var fallback = MessageBox.Show(
                $"{ex.Message}\n\nDeseja tentar pela impressão padrão do Windows?",
                "Impressão térmica",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (fallback != MessageBoxResult.Yes)
                return;

            try
            {
                PrintViaWindowsDialog();
                DialogResult = true;
                Close();
            }
            catch (Exception winEx)
            {
                MessageBox.Show($"Não foi possível imprimir o cupom.\n{winEx.Message}",
                    "Impressão", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void PrintViaWindowsDialog()
    {
        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true)
            throw new InvalidOperationException("Impressão cancelada.");

        var oldTransform = CupomPaper.LayoutTransform;
        var printableWidth = Math.Max(1, dialog.PrintableAreaWidth);
        var paperWidth = CupomPaper.ActualWidth > 0 ? CupomPaper.ActualWidth : CupomPaper.Width;
        var scale = Math.Min(1.0, printableWidth / paperWidth);
        CupomPaper.LayoutTransform = scale < 1
            ? new ScaleTransform(scale, scale)
            : null;
        CupomPaper.Measure(new Size(printableWidth, dialog.PrintableAreaHeight));
        CupomPaper.Arrange(new Rect(new Point(0, 0), CupomPaper.DesiredSize));
        dialog.PrintVisual(CupomPaper, "Cupom PDV SGDB");
        CupomPaper.LayoutTransform = oldTransform;
    }

    private void NoPrint_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CancelSale_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Voltar para as formas de pagamento?\n\n" +
                "• Os itens permanecem no carrinho\n" +
                "• Esta venda será estornada do caixa\n" +
                "• Se já pagou via PIX QR, o sistema tenta o estorno no Mercado Pago",
                "Alterar pagamento",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        BackToPaymentRequested = true;
        CancelSaleRequested = true;
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.S:
                Print_Click(sender, e);
                e.Handled = true;
                break;
            case Key.N:
                NoPrint_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Escape:
                CancelSale_Click(sender, e);
                e.Handled = true;
                break;
        }
    }
}
