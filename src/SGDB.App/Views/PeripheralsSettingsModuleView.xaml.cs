using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using SGDB.Models;

using SGDB.Services;



namespace SGDB.Views;



public partial class PeripheralsSettingsModuleView : UserControl

{

    public event EventHandler? CloseRequested;



    private static readonly (string Id, string Label)[] ScaleProtocols =

    [

        ("toledo", "Toledo"),

        ("filizola", "Filizola"),

        ("urano", "Urano"),

        ("elgin", "Elgin"),

        ("nts", "NTS / Genérico"),

    ];



    public PeripheralsSettingsModuleView()

    {

        InitializeComponent();

        Loaded += (_, _) => { Load(); Focus(); };

    }



    private void Load()

    {

        var s = AppSettingsService.GetPeripheralSettings();



        DrawerEnabledBox.IsChecked = s.DrawerEnabled;

        DrawerOnCashBox.IsChecked = s.DrawerOpenOnCashSale;

        ScaleEnabledBox.IsChecked = s.ScaleEnabled;



        ScaleProtocolBox.ItemsSource = ScaleProtocols;

        ScaleProtocolBox.DisplayMemberPath = "Label";

        ScaleProtocolBox.SelectedValuePath = "Id";

        ScaleProtocolBox.SelectedValue = NormalizeProtocol(s.ScaleProtocol);



        ScaleBaudBox.ItemsSource = new[] { "2400", "4800", "9600", "19200" };

        ScaleBaudBox.SelectedItem = s.ScaleBaud.ToString();

        if (ScaleBaudBox.SelectedItem is null)

            ScaleBaudBox.SelectedItem = "9600";



        RefreshComPorts(s.ScalePort);

        UpdateScaleFieldsState();

        UpdateAllStatus();

        UpdateScannerPlaceholder();

    }



    private static string NormalizeProtocol(string? protocol)

    {

        var p = (protocol ?? "toledo").Trim().ToLowerInvariant();

        return ScaleProtocols.Any(x => x.Id == p) ? p : "toledo";

    }



    private void RefreshComPorts(string? prefer = null)

    {

        prefer ??= ScalePortBox.Text?.Trim();

        var ports = PeripheralService.GetAvailableComPorts().ToList();

        if (!string.IsNullOrWhiteSpace(prefer) &&

            !ports.Contains(prefer, StringComparer.OrdinalIgnoreCase))

            ports.Insert(0, prefer);



        if (ports.Count == 0)

            ports.Add("COM1");



        ScalePortBox.ItemsSource = ports;

        ScalePortBox.Text = !string.IsNullOrWhiteSpace(prefer) ? prefer : ports[0];

    }



    private void UpdateScaleFieldsState()

    {

        var enabled = ScaleEnabledBox.IsChecked == true;

        ScaleFieldsPanel.IsEnabled = enabled;

        ScaleFieldsPanel.Opacity = enabled ? 1.0 : 0.55;

        ReadScaleBtn.IsEnabled = enabled;

    }



    private void UpdateAllStatus()
    {
        UpdateScannerStatus(active: false);
        UpdateDrawerStatus();
        UpdateScaleStatus();
    }

    private void UpdateScannerStatus(bool active)
    {
        if (active)
        {
            ScannerStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            ScannerStatusText.Text = "Ativo";
            ScannerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
        }
        else
        {
            ScannerStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            ScannerStatusText.Text = "Pronto para leitura";
            ScannerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        }
    }

    private void UpdateDrawerStatus()
    {
        var enabled = DrawerEnabledBox.IsChecked == true;
        if (enabled)
        {
            DrawerStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            DrawerStatusText.Text = "Habilitado";
            DrawerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        }
        else
        {
            DrawerStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            DrawerStatusText.Text = "Desabilitado";
            DrawerStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        }
    }



    private void UpdateScaleStatus()

    {

        if (ScaleEnabledBox.IsChecked != true)

        {

            ScaleStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

            ScaleStatusText.Text = "Inativo";

            ScaleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

            return;

        }



        var port = ScalePortBox.Text?.Trim() ?? "";

        var ports = PeripheralService.GetAvailableComPorts();

        if (ports.Any(p => string.Equals(p, port, StringComparison.OrdinalIgnoreCase)))

        {

            ScaleStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));

            ScaleStatusText.Text = "Pronto";

            ScaleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));

        }

        else

        {

            ScaleStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));

            ScaleStatusText.Text = "Porta não detectada";

            ScaleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x34, 0x12));

        }

    }



    private void UpdateScannerPlaceholder() =>

        ScannerPlaceholder.Visibility = string.IsNullOrWhiteSpace(ScannerTestBox.Text)

            ? Visibility.Visible

            : Visibility.Collapsed;



    private void ShowFeedback(TextBlock target, string message, bool success)

    {

        target.Text = message;

        target.Foreground = new SolidColorBrush(success

            ? Color.FromRgb(0x16, 0x65, 0x34)

            : Color.FromRgb(0x99, 0x1B, 0x1B));

        target.Visibility = Visibility.Visible;

    }



    private void TestScanner_Click(object sender, RoutedEventArgs e)

    {

        ScannerTestBox.Text = "";
        ScannerResultText.Text = "";
        UpdateScannerStatus(active: false);
        ScannerTestBox.Focus();

        UpdateScannerPlaceholder();

    }



    private void ScannerTestBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateScannerPlaceholder();

    private void ScannerTestBox_KeyDown(object sender, KeyEventArgs e)

    {

        if (e.Key != Key.Enter)

            return;



        var code = ScannerTestBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(code))

            return;



        ScannerResultText.Text = $"Código lido: {code} ({code.Length} caracteres)";
        ScannerResultText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
        UpdateScannerStatus(active: true);



        e.Handled = true;

    }



    private void TestDrawer_Click(object sender, RoutedEventArgs e)

    {

        var (ok, message) = PeripheralService.TryOpenCashDrawerWithResult();

        ShowFeedback(DrawerFeedbackText, message, ok);



        if (!ok && message.Contains("Configure a impressora", StringComparison.OrdinalIgnoreCase))

        {

            MessageBox.Show(message, "Gaveta", MessageBoxButton.OK, MessageBoxImage.Information);

        }

    }



    private void ReadScale_Click(object sender, RoutedEventArgs e)

    {

        ScaleWeightText.Text = "Lendo…";

        ScaleWeightText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));

        ScaleFeedbackText.Text = "";



        _ = int.TryParse(ScaleBaudBox.SelectedItem as string, out var baud);

        var protocol = ScaleProtocolBox.SelectedValue as string ?? "toledo";

        var port = ScalePortBox.Text?.Trim() ?? "COM1";



        var result = PeripheralService.TryReadScaleWeight(port, baud > 0 ? baud : 9600, protocol);

        if (result.Success)

        {

            ScaleWeightText.Text = $"Peso lido: {result.WeightKg:N3} kg";

            ScaleWeightText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));

            ScaleFeedbackText.Text = result.Message;

            ScaleFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));



            ScaleStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));

            ScaleStatusText.Text = "Conectado";

            ScaleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));

        }

        else

        {

            ScaleWeightText.Text = "Peso lido: —";

            ScaleWeightText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

            ScaleFeedbackText.Text = result.Message;

            ScaleFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));



            ScaleStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

            ScaleStatusText.Text = "Não encontrado";

            ScaleStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

        }

    }



    private void DrawerSettings_Changed(object sender, RoutedEventArgs e)

    {

        if (!IsLoaded) return;

        UpdateDrawerStatus();

    }



    private void ScaleSettings_Changed(object sender, RoutedEventArgs e)

    {

        if (!IsLoaded) return;

        UpdateScaleFieldsState();

        UpdateScaleStatus();

    }



    private PeripheralSettings ReadForm()

    {

        _ = int.TryParse(ScaleBaudBox.SelectedItem as string, out var baud);

        return new PeripheralSettings

        {

            DrawerEnabled = DrawerEnabledBox.IsChecked == true,

            DrawerOpenOnCashSale = DrawerOnCashBox.IsChecked == true,

            ScaleEnabled = ScaleEnabledBox.IsChecked == true,

            ScalePort = ScalePortBox.Text?.Trim() ?? "COM1",

            ScaleBaud = baud > 0 ? baud : 9600,

            ScaleProtocol = ScaleProtocolBox.SelectedValue as string ?? "toledo",

            ScannerMode = "teclado",

        };

    }



    private void Save_Click(object sender, RoutedEventArgs e)

    {

        try

        {

            var s = ReadForm();

            AppSettingsService.SavePeripheralSettings(s);

            AuditService.Log("salvar", "perifericos", null, null);

            MessageBox.Show("Periféricos salvos.", "Periféricos",

                MessageBoxButton.OK, MessageBoxImage.Information);

            UpdateAllStatus();

        }

        catch (Exception ex)

        {

            MessageBox.Show(ex.Message, "Periféricos", MessageBoxButton.OK, MessageBoxImage.Warning);

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


