using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SGDB.Models;

namespace SGDB.Services;

public sealed record ScaleReadResult(bool Success, double WeightKg, string Message)
{
    public static ScaleReadResult Ok(double kg) =>
        new(true, kg, $"Peso lido: {kg:N3} kg");

    public static ScaleReadResult Fail(string message) =>
        new(false, 0, message);
}

/// <summary>Gaveta de dinheiro, impressão de teste e leitura de balança serial.</summary>
public static partial class PeripheralService
{
    public static void TryOpenCashDrawer() => _ = TryOpenCashDrawerWithResult();

    public static (bool Ok, string Message) TryOpenCashDrawerWithResult()
    {
        var peri = AppSettingsService.GetPeripheralSettings();
        if (!peri.DrawerEnabled)
            return (false, "Gaveta desativada — marque a opção no card de gaveta.");

        var printer = AppSettingsService.GetPrinterSettings();
        if (string.IsNullOrWhiteSpace(printer.PrinterName))
            return (false, "Configure a impressora em Sistema → Impressoras antes de testar.");

        try
        {
            var kick = new byte[] { 0x1B, 0x70, 0x00, 0x19, 0xFA };
            RawPrinterHelper.SendBytes(printer.PrinterName, kick);
            return (true, "Comando enviado para a impressora!");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static IReadOnlyList<string> GetAvailableComPorts()
    {
        try
        {
            return SerialPort.GetPortNames()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static ScaleReadResult TryReadScaleWeight(
        string? port = null,
        int? baud = null,
        string? protocol = null)
    {
        var settings = AppSettingsService.GetPeripheralSettings();
        port = string.IsNullOrWhiteSpace(port) ? settings.ScalePort : port.Trim();
        baud ??= settings.ScaleBaud;
        protocol = string.IsNullOrWhiteSpace(protocol) ? settings.ScaleProtocol : protocol.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(port))
            return ScaleReadResult.Fail("Informe a porta COM da balança.");

        try
        {
            var baudRate = (baud ?? 0) > 0 ? baud!.Value : 9600;
            using var serial = new SerialPort(port, baudRate, Parity.None, 8, StopBits.One)
            {
                Encoding = Encoding.ASCII,
                ReadTimeout = 2500,
                WriteTimeout = 1500,
                NewLine = "\r\n",
            };

            serial.Open();
            serial.DiscardInBuffer();

            var raw = ReadScaleRaw(serial, protocol);
            if (string.IsNullOrWhiteSpace(raw))
                return ScaleReadResult.Fail("Nenhuma resposta da balança — confira cabo, porta e protocolo.");

            if (!TryParseWeightKg(raw, out var kg))
                return ScaleReadResult.Fail($"Resposta recebida, mas peso não identificado: \"{TrimForDisplay(raw)}\"");

            return ScaleReadResult.Ok(kg);
        }
        catch (UnauthorizedAccessException)
        {
            return ScaleReadResult.Fail($"Porta {port} em uso por outro programa.");
        }
        catch (IOException ex)
        {
            return ScaleReadResult.Fail(ex.Message);
        }
        catch (TimeoutException)
        {
            return ScaleReadResult.Fail("Tempo esgotado aguardando resposta da balança.");
        }
        catch (Exception ex)
        {
            return ScaleReadResult.Fail(ex.Message);
        }
    }

    private static string ReadScaleRaw(SerialPort serial, string protocol)
    {
        return protocol switch
        {
            "filizola" => PollScale(serial, new byte[] { 0x05 }),
            "urano" => PollScale(serial, Encoding.ASCII.GetBytes("?\r\n")),
            "elgin" => PollScale(serial, Encoding.ASCII.GetBytes("W\r\n")),
            "nts" or "generico" or "generic" => ReadPassive(serial, 2200),
            _ => PollScale(serial, new byte[] { 0x05 }), // Toledo (padrão)
        };
    }

    private static string PollScale(SerialPort serial, byte[] command)
    {
        serial.Write(command, 0, command.Length);
        Thread.Sleep(120);
        return ReadPassive(serial, 2200);
    }

    private static string ReadPassive(SerialPort serial, int maxWaitMs)
    {
        var sb = new StringBuilder();
        var deadline = Environment.TickCount64 + maxWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                var chunk = serial.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    sb.Append(chunk);
                    if (TryParseWeightKg(sb.ToString(), out _))
                        break;
                }
            }
            catch (TimeoutException)
            {
                break;
            }

            Thread.Sleep(80);
        }

        if (sb.Length == 0)
        {
            try
            {
                var line = serial.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    sb.Append(line);
            }
            catch
            {
                // ignora — retorna vazio
            }
        }

        return sb.ToString();
    }

    private static bool TryParseWeightKg(string raw, out double kg)
    {
        kg = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        foreach (Match m in WeightPattern().Matches(raw))
        {
            var token = m.Value.Replace(',', '.');
            if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                kg = val;
                return true;
            }
        }

        return false;
    }

    private static string TrimForDisplay(string raw)
    {
        var t = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return t.Length <= 48 ? t : t[..48] + "…";
    }

    [GeneratedRegex(@"\d+[.,]\d+|\d+")]
    private static partial Regex WeightPattern();

    public static void TryOpenCashDrawerAfterCashSale(IEnumerable<PdvPaymentPart>? parts)
    {
        var peri = AppSettingsService.GetPeripheralSettings();
        if (!peri.DrawerEnabled || !peri.DrawerOpenOnCashSale)
            return;

        var hasCash = parts?.Any(p =>
            string.Equals(p.PaymentType, "Dinheiro", StringComparison.OrdinalIgnoreCase)) == true;
        if (!hasCash)
            return;

        TryOpenCashDrawer();
    }

    public static void PrintTestPage(
        string? printerName = null,
        int paperWidthMm = 80,
        string? footer = null,
        bool autoCut = true)
    {
        var settings = AppSettingsService.GetPrinterSettings();
        var name = string.IsNullOrWhiteSpace(printerName) ? settings.PrinterName : printerName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Selecione uma impressora.");

        var width = paperWidthMm is 58 or 80 ? paperWidthMm : settings.PaperWidthMm;
        var cols = width <= 58 ? 32 : 42;
        var footerText = footer ?? settings.FooterText;

        var lines = new List<string>();
        lines.AddRange(AppSettingsService.BuildReceiptHeaderLines());
        if (lines.Count == 0)
            lines.Add(AppSettingsService.GetNomeDeposito().ToUpperInvariant());
        lines.Add(new string('-', cols));
        lines.Add(CenterText("*** TESTE DE IMPRESSAO ***", cols));
        lines.Add(CenterText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), cols));
        lines.Add(new string('-', cols));
        lines.Add(PadLine("ITEM EXEMPLO", "R$ 10,00", cols));
        lines.Add(PadLine("TOTAL", "R$ 10,00", cols));
        lines.Add(new string('-', cols));
        foreach (var fl in (footerText ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(fl))
                lines.Add(fl.Trim());
        }
        lines.Add("");
        lines.Add(CenterText("SGDB — teste OK", cols));

        RawPrinterHelper.SendBytes(name, BuildEscPos(lines, autoCut));
    }

    /// <summary>Imprime linhas de texto na impressora térmica configurada (ESC/POS).</summary>
    public static void PrintReceiptLines(IEnumerable<string> bodyLines, bool autoCut = true)
    {
        var settings = AppSettingsService.GetPrinterSettings();
        if (string.IsNullOrWhiteSpace(settings.PrinterName))
            throw new InvalidOperationException("Configure a impressora em Sistema → Impressoras.");

        var width = settings.PaperWidthMm is 58 or 80 ? settings.PaperWidthMm : 80;
        var cols = width <= 58 ? 32 : 42;

        var lines = new List<string>();
        lines.AddRange(AppSettingsService.BuildReceiptHeaderLines());
        if (lines.Count == 0)
            lines.Add(AppSettingsService.GetNomeDeposito().ToUpperInvariant());
        lines.Add(new string('-', cols));
        foreach (var line in bodyLines)
            lines.Add(WrapLine(line ?? "", cols));
        lines.Add(new string('-', cols));
        foreach (var fl in (settings.FooterText ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(fl))
                lines.Add(WrapLine(fl.Trim(), cols));
        }

        PrintEscPosDocument(lines, autoCut);
    }

    /// <summary>Envia o documento completo (já formatado) para a impressora térmica ESC/POS.</summary>
    public static void PrintEscPosDocument(IEnumerable<string> lines, bool? autoCut = null, int? copies = null)
    {
        var settings = AppSettingsService.GetPrinterSettings();
        if (string.IsNullOrWhiteSpace(settings.PrinterName))
            throw new InvalidOperationException(
                "Nenhuma impressora térmica configurada.\n\nVá em Sistema → Impressoras & Cupom e selecione a impressora.");

        var cut = autoCut ?? settings.AutoCut;
        var reps = Math.Clamp(copies ?? settings.Copies, 1, 5);
        var payload = BuildEscPos(lines, cut);
        for (var i = 0; i < reps; i++)
            RawPrinterHelper.SendBytes(settings.PrinterName, payload);
    }

    private static string WrapLine(string text, int cols)
    {
        if (text.Length <= cols) return text;
        return text[..cols];
    }

    private static string CenterText(string text, int cols)
    {
        text = text.Trim();
        if (text.Length >= cols) return text[..cols];
        var pad = (cols - text.Length) / 2;
        return new string(' ', pad) + text;
    }

    private static string PadLine(string left, string right, int cols)
    {
        left = left.Trim();
        right = right.Trim();
        var space = cols - left.Length - right.Length;
        if (space < 1)
            return (left + " " + right);
        return left + new string(' ', space) + right;
    }

    private static byte[] BuildEscPos(IEnumerable<string> lines, bool autoCut)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x1B);
        ms.WriteByte(0x40);
        var enc = System.Text.Encoding.GetEncoding(850);
        foreach (var line in lines)
        {
            var bytes = enc.GetBytes(line);
            ms.Write(bytes, 0, bytes.Length);
            ms.WriteByte(0x0A);
        }
        ms.WriteByte(0x0A);
        ms.WriteByte(0x0A);
        if (autoCut)
        {
            ms.WriteByte(0x1D);
            ms.WriteByte(0x56);
            ms.WriteByte(0x00);
        }
        return ms.ToArray();
    }
}

internal static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private class DocInfoA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DocInfoA di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static void SendBytes(string printerName, byte[] data)
    {
        if (!OpenPrinter(printerName.Normalize(), out var hPrinter, IntPtr.Zero))
            throw new IOException("Não foi possível abrir a impressora.");

        try
        {
            var di = new DocInfoA { pDocName = "SGDB Drawer", pDataType = "RAW" };
            if (!StartDocPrinter(hPrinter, 1, di))
                throw new IOException("StartDocPrinter falhou.");
            try
            {
                if (!StartPagePrinter(hPrinter))
                    throw new IOException("StartPagePrinter falhou.");
                try
                {
                    var ptr = Marshal.AllocHGlobal(data.Length);
                    try
                    {
                        Marshal.Copy(data, 0, ptr, data.Length);
                        if (!WritePrinter(hPrinter, ptr, data.Length, out _))
                            throw new IOException("WritePrinter falhou.");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}
