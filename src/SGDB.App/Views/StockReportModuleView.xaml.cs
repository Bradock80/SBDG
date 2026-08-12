using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class StockReportModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly StockReportKind _kind;
    private readonly bool _isRanking;
    private readonly bool _isZero;

    private static readonly SolidColorBrush GoldBrush = Brush("#F59E0B");
    private static readonly SolidColorBrush GoldText = Brush("#78350F");
    private static readonly SolidColorBrush SilverBrush = Brush("#94A3B8");
    private static readonly SolidColorBrush SilverText = Brush("#1E293B");
    private static readonly SolidColorBrush BronzeBrush = Brush("#D97706");
    private static readonly SolidColorBrush BronzeText = Brush("#7C2D12");
    private static readonly SolidColorBrush NeutralBrush = Brush("#E2E8F0");
    private static readonly SolidColorBrush NeutralText = Brush("#475569");

    public StockReportModuleView(StockReportKind kind)
    {
        _kind = kind;
        _isRanking = kind is StockReportKind.MaisVendidos or StockReportKind.MenosVendidos
            or StockReportKind.MaisLucrativos or StockReportKind.MenosLucrativos;
        _isZero = kind == StockReportKind.ZeraNegativo;

        InitializeComponent();
        TitleText.Text = StockService.ReportTitle(kind);
        PeriodPanel.Visibility = _isRanking ? Visibility.Visible : Visibility.Collapsed;
        ZeroBtn.Visibility = _isZero ? Visibility.Visible : Visibility.Collapsed;

        if (_isRanking)
        {
            DateFrom.SelectedDate = DateTime.Today.AddDays(-30);
            DateTo.SelectedDate = DateTime.Today;
        }

        BuildColumns();
        Loaded += (_, _) =>
        {
            Focus();
            Refresh();
        };
    }

    private void BuildColumns()
    {
        ReportGrid.Columns.Clear();
        ReportGrid.Columns.Add(PosMedalColumn());

        if (_isRanking)
        {
            ReportGrid.Columns.Add(TextCol("Código", nameof(StockReportRow.Code), 90));
            ReportGrid.Columns.Add(TextCol("Produto", nameof(StockReportRow.Name), star: true));
            ReportGrid.Columns.Add(TextCol("Grupo", nameof(StockReportRow.GroupName), 130));
            ReportGrid.Columns.Add(NumCol("Qtd", nameof(StockReportRow.QtyDisplay), 90));
            ReportGrid.Columns.Add(NumCol("Total R$", nameof(StockReportRow.TotalDisplay), 110));
            if (_kind is StockReportKind.MaisLucrativos or StockReportKind.MenosLucrativos)
                ReportGrid.Columns.Add(NumCol("Lucro R$", nameof(StockReportRow.LucroDisplay), 110));
        }
        else
        {
            ReportGrid.Columns.Add(TextCol("Código", nameof(StockReportRow.Code), 90));
            ReportGrid.Columns.Add(TextCol("Produto", nameof(StockReportRow.Name), star: true));
            ReportGrid.Columns.Add(TextCol("Grupo", nameof(StockReportRow.GroupName), 120));
            ReportGrid.Columns.Add(NumCol("Estoque", nameof(StockReportRow.StockDisplay), 90));
            ReportGrid.Columns.Add(NumCol("Mín.", nameof(StockReportRow.MinStock), 60));
            ReportGrid.Columns.Add(NumCol("Valor R$", nameof(StockReportRow.StockValueDisplay), 100));
            if (_kind == StockReportKind.Validade7d)
            {
                ReportGrid.Columns.Add(TextCol("Validade", nameof(StockReportRow.DataValidade), 90));
                ReportGrid.Columns.Add(NumCol("Dias", nameof(StockReportRow.DiasValidade), 55));
            }
            ReportGrid.Columns.Add(TextCol("Local", nameof(StockReportRow.Location), 110));
        }
    }

    private DataGridTemplateColumn PosMedalColumn()
    {
        var col = new DataGridTemplateColumn
        {
            Header = "Pos.",
            Width = new DataGridLength(56),
            HeaderStyle = (Style)FindResource("CenterHeader"),
        };

        var factory = new FrameworkElementFactory(typeof(Grid));
        factory.SetValue(FrameworkElement.WidthProperty, 56.0);
        factory.SetValue(FrameworkElement.HeightProperty, 40.0);

        var ellipse = new FrameworkElementFactory(typeof(Ellipse));
        ellipse.SetValue(FrameworkElement.WidthProperty, 28.0);
        ellipse.SetValue(FrameworkElement.HeightProperty, 28.0);
        ellipse.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        ellipse.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        ellipse.SetBinding(Shape.FillProperty, new Binding(nameof(StockReportRow.Posicao))
        {
            Converter = PosMedalBrushConverter.Instance,
        });

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(StockReportRow.Posicao)));
        label.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        label.SetValue(TextBlock.FontSizeProperty, 11.0);
        label.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(StockReportRow.Posicao))
        {
            Converter = PosMedalTextConverter.Instance,
        });

        factory.AppendChild(ellipse);
        factory.AppendChild(label);
        col.CellTemplate = new DataTemplate { VisualTree = factory };
        return col;
    }

    private DataGridTextColumn TextCol(string header, string path, double width = 100, bool star = false)
    {
        var col = new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
            HeaderStyle = (Style)FindResource("TextHeader"),
        };
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(10, 0, 10, 0)));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        col.ElementStyle = style;
        return col;
    }

    private DataGridTextColumn NumCol(string header, string path, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = new DataGridLength(width),
            HeaderStyle = (Style)FindResource("NumHeader"),
            ElementStyle = (Style)FindResource("NumCell"),
        };
    }

    private void Refresh()
    {
        try
        {
            var result = StockService.ListReport(
                _isZero ? StockReportKind.Negativo : _kind,
                DateFrom.SelectedDate,
                DateTo.SelectedDate);

            ReportGrid.ItemsSource = result.Rows;
            BuildFooter(result);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, TitleText.Text, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BuildFooter(StockReportResult result)
    {
        FooterBadges.Children.Clear();
        SummaryText.Visibility = Visibility.Collapsed;

        if (_isRanking)
        {
            PeriodHint.Text = result.DateFrom is not null
                ? $"Período {result.DateFrom:dd/MM/yyyy} — {result.DateTo:dd/MM/yyyy}"
                : "";

            AddBadge("Produtos", result.Registros.ToString("N0"), boldValue: false);
            AddBadge("Qtd", result.TotalQty.ToString("N3"), boldValue: false);
            AddBadge("Total R$", result.TotalValor.ToString("N2"), boldValue: true);
            if (_kind is StockReportKind.MaisLucrativos or StockReportKind.MenosLucrativos || result.TotalLucro != 0)
                AddBadge("Lucro R$", result.TotalLucro.ToString("N2"), boldValue: true, accent: true);
        }
        else
        {
            PeriodHint.Text = "";
            AddBadge("Registros", result.Registros.ToString("N0"), boldValue: false);
            AddBadge("Estoque", result.TotalStock.ToString("N3"), boldValue: false);
            AddBadge("Valor R$", result.TotalValor.ToString("N2"), boldValue: true);
        }
    }

    private void AddBadge(string label, string value, bool boldValue, bool accent = false)
    {
        var border = new Border
        {
            Style = (Style)FindResource("FooterBadge"),
            Background = accent ? Brush("#ECFDF5") : Brush("#F1F5F9"),
            BorderBrush = accent ? Brush("#A7F3D0") : Brush("#E2E8F0"),
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = label + "  ",
            FontSize = 11,
            Foreground = Brush("#64748B"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = boldValue ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = accent ? Brush("#065F46") : Brush("#0F172A"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        border.Child = stack;
        FooterBadges.Children.Add(border);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Zero_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "Zerar todos os produtos com estoque negativo (entrada até 0)?",
                TitleText.Text, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var n = StockService.ZeroNegativeStock();
            MessageBox.Show($"{n} produto(s) ajustado(s).", TitleText.Text, MessageBoxButton.OK, MessageBoxImage.Information);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, TitleText.Text, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7) { Refresh(); e.Handled = true; }
        else if (e.Key == Key.F9 && _isZero) { Zero_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
        b.Freeze();
        return b;
    }

    private sealed class PosMedalBrushConverter : IValueConverter
    {
        public static readonly PosMedalBrushConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var pos = value is int i ? i : 0;
            return pos switch
            {
                1 => GoldBrush,
                2 => SilverBrush,
                3 => BronzeBrush,
                _ => NeutralBrush,
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class PosMedalTextConverter : IValueConverter
    {
        public static readonly PosMedalTextConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var pos = value is int i ? i : 0;
            return pos switch
            {
                1 => GoldText,
                2 => SilverText,
                3 => BronzeText,
                _ => NeutralText,
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => Binding.DoNothing;
    }
}
