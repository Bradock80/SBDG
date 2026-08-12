using System.Printing;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using SGDB.Models;

using SGDB.Services;



namespace SGDB.Views;



public partial class PrinterSettingsModuleView : UserControl

{

    public event EventHandler? CloseRequested;

    private int _copies = 1;

    private bool _suppress;

    private ReceiptPreviewExpandWindow? _expandWindow;



    public PrinterSettingsModuleView()

    {

        InitializeComponent();

        Loaded += (_, _) => { Load(); Focus(); };

    }



    private void Load()

    {

        var s = AppSettingsService.GetPrinterSettings();

        _suppress = true;

        RefreshPrinterList(s.PrinterName);

        Width80.IsChecked = s.PaperWidthMm != 58;

        Width58.IsChecked = s.PaperWidthMm == 58;

        _copies = Math.Clamp(s.Copies, 1, 5);

        CopiesText.Text = _copies.ToString();

        AutoCutBox.IsChecked = s.AutoCut;
        AutoPrintPreContaBox.IsChecked = s.AutoPrintDeckPreConta;

        FooterBox.Text = s.FooterText;

        _suppress = false;

        UpdatePrinterPlaceholder();

        UpdateStatus();

        UpdateFooterHint();

        UpdatePreview();

    }



    private void RefreshPrinterList(string? preferName = null)

    {

        preferName ??= PrinterBox.Text?.Trim();

        var names = new List<string>();

        try

        {

            var server = new LocalPrintServer();

            foreach (var q in server.GetPrintQueues())

                names.Add(q.Name);

        }

        catch { /* sem spooler */ }



        names.Sort(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(preferName) &&

            !names.Contains(preferName, StringComparer.OrdinalIgnoreCase))

            names.Insert(0, preferName);



        PrinterBox.ItemsSource = names;

        if (!string.IsNullOrWhiteSpace(preferName))

            PrinterBox.Text = preferName;

        else

            PrinterBox.Text = "";

    }



    private int SelectedWidthMm => Width58.IsChecked == true ? 58 : 80;



    private int CharsPerLine => ReceiptPreviewBuilder.CharsForWidth(SelectedWidthMm);



    private ReceiptPreviewData BuildPreviewData() =>

        ReceiptPreviewBuilder.Build(SelectedWidthMm, FooterBox.Text ?? "", AutoCutBox.IsChecked == true);



    private void UpdatePrinterPlaceholder() =>

        PrinterPlaceholder.Visibility = string.IsNullOrWhiteSpace(PrinterBox.Text)

            ? Visibility.Visible

            : Visibility.Collapsed;



    private void UpdateStatus()

    {

        var name = PrinterBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(name))

        {

            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

            StatusText.Text = "Nenhuma impressora selecionada";

            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

            return;

        }



        try

        {

            using var server = new LocalPrintServer();

            PrintQueue? queue = null;

            foreach (var q in server.GetPrintQueues())

            {

                if (string.Equals(q.Name, name, StringComparison.OrdinalIgnoreCase))

                {

                    queue = q;

                    break;

                }

            }



            if (queue is null)

            {

                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));

                StatusText.Text = "Impressora não encontrada na lista — confira o nome";

                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x34, 0x12));

                return;

            }



            queue.Refresh();

            var status = queue.QueueStatus;

            var offline = status.HasFlag(PrintQueueStatus.Offline)

                          || status.HasFlag(PrintQueueStatus.NotAvailable)

                          || status.HasFlag(PrintQueueStatus.Error)

                          || status.HasFlag(PrintQueueStatus.PaperOut);



            if (offline)

            {

                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

                var reason = status.HasFlag(PrintQueueStatus.PaperOut) ? "Sem papel"

                    : status.HasFlag(PrintQueueStatus.Offline) ? "Desconectada / Offline"

                    : "Com erro";

                StatusText.Text = $"{reason} — {queue.Name}";

                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

            }

            else

            {

                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));

                StatusText.Text = $"Conectada — {queue.Name}";

                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));

            }

        }

        catch

        {

            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));

            StatusText.Text = "Não foi possível consultar o status";

            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x34, 0x12));

        }

    }



    private void UpdateFooterHint()

    {

        var cols = CharsPerLine;

        var over = ReceiptPreviewBuilder.CountLongFooterLines(FooterBox.Text ?? "", cols);

        FooterHint.Text = over > 0

            ? $"Limite: {cols} caracteres/linha · {over} linha(s) longas serão quebradas"

            : $"Limite recomendado: {cols} caracteres por linha (quebra por palavra)";

        FooterHint.Foreground = over > 0

            ? new SolidColorBrush(Color.FromRgb(0xC2, 0x41, 0x0C))

            : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

    }



    private void UpdatePreview()

    {

        var data = BuildPreviewData();

        PreviewWidthLabel.Text = data.WidthLabel;

        SidePreview.Tag = data;

        SidePreview.Apply(data);



        if (_expandWindow is { IsVisible: true })

            _expandWindow.RefreshPreview(data);

    }



    private void ExpandPreview_Click(object sender, RoutedEventArgs e)

    {

        if (_expandWindow is { IsVisible: true })

        {

            _expandWindow.Activate();

            return;

        }



        var owner = Window.GetWindow(this);

        _expandWindow = new ReceiptPreviewExpandWindow(BuildPreviewData(), owner);

        _expandWindow.PrintTestRequested += (_, _) => PrintTest_Click(sender, e);

        _expandWindow.Closed += (_, _) => _expandWindow = null;

        _expandWindow.Show();

    }



    private PrinterSettings ReadForm() => new()

    {

        PrinterName = PrinterBox.Text?.Trim() ?? "",

        PaperWidthMm = SelectedWidthMm,

        AutoCut = AutoCutBox.IsChecked == true,
        AutoPrintDeckPreConta = AutoPrintPreContaBox.IsChecked == true,

        FooterText = FooterBox.Text ?? "",

        Copies = _copies,

    };



    private void RefreshPrinters_Click(object sender, RoutedEventArgs e)

    {

        RefreshPrinterList();

        UpdatePrinterPlaceholder();

        UpdateStatus();

    }



    private void PrinterBox_Changed(object sender, SelectionChangedEventArgs e)

    {

        if (_suppress || !IsLoaded) return;

        Dispatcher.BeginInvoke(() =>

        {

            UpdatePrinterPlaceholder();

            UpdateStatus();

        });

    }



    private void PrinterBox_LostFocus(object sender, RoutedEventArgs e)

    {

        UpdatePrinterPlaceholder();

        UpdateStatus();

    }



    private void Preview_Changed(object sender, RoutedEventArgs e)

    {

        if (_suppress || !IsLoaded) return;

        UpdateFooterHint();

        UpdatePreview();

    }



    private void FooterBox_TextChanged(object sender, TextChangedEventArgs e)

    {

        if (_suppress || !IsLoaded) return;

        UpdateFooterHint();

        UpdatePreview();

    }



    private void CopiesMinus_Click(object sender, RoutedEventArgs e)

    {

        _copies = Math.Max(1, _copies - 1);

        CopiesText.Text = _copies.ToString();

    }



    private void CopiesPlus_Click(object sender, RoutedEventArgs e)

    {

        _copies = Math.Min(5, _copies + 1);

        CopiesText.Text = _copies.ToString();

    }



    private void PrintTest_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            var s = ReadForm();

            if (string.IsNullOrWhiteSpace(s.PrinterName))

            {

                MessageBox.Show("Selecione uma impressora antes de imprimir o teste.",

                    "Impressoras", MessageBoxButton.OK, MessageBoxImage.Information);

                return;

            }

            PeripheralService.PrintTestPage(s.PrinterName, s.PaperWidthMm, s.FooterText, s.AutoCut);

            MessageBox.Show("Comando de teste enviado para a impressora.",

                "Impressoras", MessageBoxButton.OK, MessageBoxImage.Information);

        }

        catch (Exception ex)

        {

            MessageBox.Show(ex.Message, "Imprimir teste", MessageBoxButton.OK, MessageBoxImage.Warning);

        }

    }



    private void Save_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            var s = ReadForm();

            AppSettingsService.SavePrinterSettings(s);

            AuditService.Log("salvar", "impressora", null, s.PrinterName);

            MessageBox.Show("Configuração de impressora salva.", "Impressoras",

                MessageBoxButton.OK, MessageBoxImage.Information);

        }

        catch (Exception ex)

        {

            MessageBox.Show(ex.Message, "Impressoras", MessageBoxButton.OK, MessageBoxImage.Warning);

        }

    }



    private void Close_Click(object sender, RoutedEventArgs e) =>

        CloseRequested?.Invoke(this, EventArgs.Empty);



    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key == Key.F9) { Save_Click(sender, e); e.Handled = true; }

        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }

    }

}


