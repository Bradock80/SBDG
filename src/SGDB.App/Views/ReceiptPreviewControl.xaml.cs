using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SGDB.Models;

namespace SGDB.Views;

public partial class ReceiptPreviewControl : UserControl
{
    public static readonly DependencyProperty CompactProperty =
        DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(ReceiptPreviewControl),
            new PropertyMetadata(true, OnCompactChanged));

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    private ReceiptPreviewData? _data;
    private static readonly Color PaperColor = Color.FromRgb(0xFD, 0xFC, 0xF7);

    public ReceiptPreviewControl()
    {
        InitializeComponent();
    }

    public void Apply(ReceiptPreviewData data)
    {
        _data = data;
        Tag = data;

        HeaderText.Text = data.Header;
        MetaText.Text = data.Meta;
        BodyText.Text = data.Body;
        FooterText.Text = data.Footer;
        BarcodeText.Text = data.Barcode;
        QrHintText.Text = data.QrHint;
        CutHintText.Text = data.CutHint;

        HeaderText.FontSize = data.HeaderFontSize;
        MetaText.FontSize = data.MetaFontSize;
        BodyText.FontSize = data.BodyFontSize;
        FooterText.FontSize = data.BodyFontSize;
        BarcodeText.FontSize = data.BarcodeFontSize;

        var textWidth = data.PaperWidthPx - (data.PaperPaddingPx * 2);
        HeaderText.MaxWidth = textWidth;
        MetaText.MaxWidth = textWidth;
        BodyText.MaxWidth = textWidth;
        FooterText.MaxWidth = textWidth;

        PaperHost.Width = data.PaperWidthPx;
        SerrateTopEdge.Width = data.PaperWidthPx;
        SerrateBottomEdge.Width = data.PaperWidthPx;

        ApplyCompactMode();
        ApplyContainerScale();
    }

    private void ApplyCompactMode()
    {
        DetailsPanel.Visibility = Compact ? Visibility.Collapsed : Visibility.Visible;
        FooterHighlight.Padding = Compact
            ? new Thickness(12, 8, 12, 10)
            : new Thickness(12, 10, 12, 10);

        var backdrop = Compact
            ? Color.FromRgb(0xEE, 0xF2, 0xF7)
            : Color.FromRgb(0x1A, 0x1F, 0x2E);
        SerrateTopEdge.Background = CreateSerrateBrush(backdrop, top: true);
        SerrateBottomEdge.Background = CreateSerrateBrush(backdrop, top: false);
    }

    private static DrawingBrush CreateSerrateBrush(Color backdrop, bool top)
    {
        var paper = new SolidColorBrush(PaperColor);
        var bg = new SolidColorBrush(backdrop);

        var ellipse = top
            ? new EllipseGeometry(new Point(6, 6), 6, 6)
            : new EllipseGeometry(new Point(6, 0), 6, 6);

        return new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 12, 6),
            ViewboxUnits = BrushMappingMode.Absolute,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, 12, 6))),
                    new GeometryDrawing(paper, null, ellipse),
                },
            },
        };
    }

    private void ApplyContainerScale()
    {
        if (!Compact)
        {
            Scaler.MaxWidth = double.PositiveInfinity;
            Scaler.MaxHeight = double.PositiveInfinity;
            return;
        }

        var available = Math.Max(0, ActualWidth - 2);
        if (available <= 0)
            available = 260;

        Scaler.MaxWidth = available;
        Scaler.MaxHeight = double.PositiveInfinity;
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Compact)
            ApplyContainerScale();
    }

    private static void OnCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ReceiptPreviewControl ctrl)
            return;

        ctrl.ApplyCompactMode();
        ctrl.ApplyContainerScale();
    }
}
