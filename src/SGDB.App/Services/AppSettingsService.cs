using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

public static class AppSettingsService
{
    public const string KeyNomeDeposito = "nome_deposito";
    public const string DefaultNomeDeposito = "Meu Depósito";

    public static string GetNomeDeposito()
    {
        var company = GetCompanyProfile();
        if (!string.IsNullOrWhiteSpace(company.NomeFantasia))
            return company.NomeFantasia.Trim();
        if (!string.IsNullOrWhiteSpace(company.RazaoSocial))
            return company.RazaoSocial.Trim();
        var value = GetSetting(KeyNomeDeposito);
        return string.IsNullOrWhiteSpace(value) ? DefaultNomeDeposito : value.Trim();
    }

    public static string? GetSetting(string key)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = $key LIMIT 1;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    public static void SetSetting(string key, string value)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value ?? "");
        cmd.ExecuteNonQuery();
    }

    public static CompanyProfile GetCompanyProfile()
    {
        return new CompanyProfile
        {
            RazaoSocial = GetSetting("company_razao") ?? "",
            NomeFantasia = GetSetting("company_fantasia") ?? GetSetting(KeyNomeDeposito) ?? "",
            Cnpj = GetSetting("company_cnpj") ?? "",
            Ie = GetSetting("company_ie") ?? "",
            Endereco = GetSetting("company_endereco") ?? "",
            Numero = GetSetting("company_numero") ?? "",
            Bairro = GetSetting("company_bairro") ?? "",
            Cidade = GetSetting("company_cidade") ?? "",
            Uf = GetSetting("company_uf") ?? "",
            Cep = GetSetting("company_cep") ?? "",
            Telefone = GetSetting("company_telefone") ?? "",
            Email = GetSetting("company_email") ?? "",
            PixKey = GetSetting("company_pix") ?? "",
            LogoPath = GetSetting("company_logo_path"),
        };
    }

    public static void SaveCompanyProfile(CompanyProfile p)
    {
        SetSetting("company_razao", p.RazaoSocial.Trim());
        SetSetting("company_fantasia", p.NomeFantasia.Trim());
        SetSetting(KeyNomeDeposito, string.IsNullOrWhiteSpace(p.NomeFantasia) ? p.RazaoSocial.Trim() : p.NomeFantasia.Trim());
        SetSetting("company_cnpj", p.Cnpj.Trim());
        SetSetting("company_ie", p.Ie.Trim());
        SetSetting("company_endereco", p.Endereco.Trim());
        SetSetting("company_numero", p.Numero.Trim());
        SetSetting("company_bairro", p.Bairro.Trim());
        SetSetting("company_cidade", p.Cidade.Trim());
        SetSetting("company_uf", p.Uf.Trim().ToUpperInvariant());
        SetSetting("company_cep", p.Cep.Trim());
        SetSetting("company_telefone", p.Telefone.Trim());
        SetSetting("company_email", p.Email.Trim());
        SetSetting("company_pix", p.PixKey.Trim());
        if (!string.IsNullOrWhiteSpace(p.LogoPath))
            SetSetting("company_logo_path", p.LogoPath!);
    }

    public static string BrandingDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SGDB", "branding");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SaveLogoFile(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new InvalidOperationException("Arquivo de logo não encontrado.");
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"))
            throw new InvalidOperationException("Use PNG, JPG, BMP ou GIF.");
        var dest = Path.Combine(BrandingDir, "logo" + (ext == ".jpeg" ? ".jpg" : ext));
        File.Copy(sourcePath, dest, overwrite: true);
        SetSetting("company_logo_path", dest);
        return dest;
    }

    public static void ClearLogo()
    {
        var path = GetSetting("company_logo_path");
        SetSetting("company_logo_path", "");
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch { /* ignore */ }
    }

    public static PrinterSettings GetPrinterSettings()
    {
        _ = int.TryParse(GetSetting("printer_width") ?? "80", out var width);
        if (width is not (58 or 80)) width = 80;
        _ = int.TryParse(GetSetting("printer_copies") ?? "1", out var copies);
        copies = Math.Clamp(copies, 1, 5);
        return new PrinterSettings
        {
            PrinterName = GetSetting("printer_name") ?? "",
            PaperWidthMm = width,
            AutoCut = (GetSetting("printer_auto_cut") ?? "1") != "0",
            AutoPrintDeckPreConta = (GetSetting("decks_auto_print_preconta") ?? "0") == "1",
            FooterText = GetSetting("printer_footer")
                ?? "Agradecemos a preferência!\nConferir o troco e os cascos no ato da compra.",
            Copies = copies,
        };
    }

    public static void SavePrinterSettings(PrinterSettings s)
    {
        SetSetting("printer_name", s.PrinterName.Trim());
        SetSetting("printer_width", s.PaperWidthMm is 58 ? "58" : "80");
        SetSetting("printer_auto_cut", s.AutoCut ? "1" : "0");
        SetSetting("decks_auto_print_preconta", s.AutoPrintDeckPreConta ? "1" : "0");
        SetSetting("printer_footer", s.FooterText ?? "");
        SetSetting("printer_copies", Math.Clamp(s.Copies, 1, 5).ToString());
    }

    public static PeripheralSettings GetPeripheralSettings()
    {
        _ = int.TryParse(GetSetting("scale_baud") ?? "9600", out var baud);
        if (baud <= 0) baud = 9600;
        return new PeripheralSettings
        {
            DrawerEnabled = (GetSetting("drawer_enabled") ?? "1") != "0",
            DrawerOpenOnCashSale = (GetSetting("drawer_on_cash") ?? "1") != "0",
            ScannerMode = GetSetting("scanner_mode") ?? "teclado",
            ScaleEnabled = (GetSetting("scale_enabled") ?? "0") == "1",
            ScalePort = GetSetting("scale_port") ?? "COM1",
            ScaleBaud = baud,
            ScaleProtocol = GetSetting("scale_protocol") ?? "toledo",
        };
    }

    public static void SavePeripheralSettings(PeripheralSettings s)
    {
        SetSetting("drawer_enabled", s.DrawerEnabled ? "1" : "0");
        SetSetting("drawer_on_cash", s.DrawerOpenOnCashSale ? "1" : "0");
        SetSetting("scanner_mode", string.IsNullOrWhiteSpace(s.ScannerMode) ? "teclado" : s.ScannerMode.Trim());
        SetSetting("scale_enabled", s.ScaleEnabled ? "1" : "0");
        SetSetting("scale_port", s.ScalePort.Trim());
        SetSetting("scale_baud", s.ScaleBaud.ToString());
        SetSetting("scale_protocol", string.IsNullOrWhiteSpace(s.ScaleProtocol) ? "toledo" : s.ScaleProtocol.Trim().ToLowerInvariant());
    }

    /// <summary>Linhas de cabeçalho para cupom / fiado / vasilhame.</summary>
    public static IReadOnlyList<string> BuildReceiptHeaderLines()
    {
        var c = GetCompanyProfile();
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.NomeFantasia)) lines.Add(c.NomeFantasia.Trim().ToUpperInvariant());
        else if (!string.IsNullOrWhiteSpace(c.RazaoSocial)) lines.Add(c.RazaoSocial.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(c.RazaoSocial) &&
            !string.Equals(c.RazaoSocial, c.NomeFantasia, StringComparison.OrdinalIgnoreCase))
            lines.Add(c.RazaoSocial.Trim());
        if (!string.IsNullOrWhiteSpace(c.Cnpj)) lines.Add($"CNPJ: {c.Cnpj}");
        if (!string.IsNullOrWhiteSpace(c.Ie)) lines.Add($"IE: {c.Ie}");
        var addr = c.AddressLine;
        if (!string.IsNullOrWhiteSpace(addr)) lines.Add(addr);
        if (!string.IsNullOrWhiteSpace(c.Telefone)) lines.Add($"Tel: {c.Telefone}");
        if (!string.IsNullOrWhiteSpace(c.PixKey)) lines.Add($"PIX: {c.PixKey}");
        return lines;
    }
}
