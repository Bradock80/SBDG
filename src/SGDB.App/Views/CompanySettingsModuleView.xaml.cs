using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class CompanySettingsModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? CompanySaved;
    private string? _logoPath;
    private bool _suppressFormat;
    private bool _cnpjBusy;
    private bool _cepBusy;
    private string? _lastCnpjLookup;
    private string? _lastCepLookup;

    public CompanySettingsModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) => { Load(); Focus(); };
    }

    private void Load()
    {
        _suppressFormat = true;
        var c = AppSettingsService.GetCompanyProfile();
        FantasiaBox.Text = c.NomeFantasia;
        RazaoBox.Text = c.RazaoSocial;
        CnpjBox.Text = FormatStoredCnpj(c.Cnpj);
        IeBox.Text = c.Ie;
        EnderecoBox.Text = c.Endereco;
        NumeroBox.Text = c.Numero;
        BairroBox.Text = c.Bairro;
        CidadeBox.Text = c.Cidade;
        UfBox.Text = c.Uf;
        CepBox.Text = FormatStoredCep(c.Cep);
        TelBox.Text = LookupService.FormatPhone(c.Telefone);
        EmailBox.Text = c.Email;
        PixBox.Text = c.PixKey;
        _suppressFormat = false;

        MpPixEnabledBox.IsChecked = MercadoPagoCredentials.IsPixEnabled();
        MpTokenBox.Text = "";
        RefreshMpTokenHint();
        MeuDanfeApiKeyBox.Text = "";
        RefreshMeuDanfeHint();
        _logoPath = c.LogoPath;
        ShowLogo(_logoPath);
        SetStatus("");
    }

    private static string FormatStoredCnpj(string? value)
    {
        var digits = TextNorm.DigitsOnly(value, 14);
        return digits is { Length: 14 } ? LookupService.FormatCnpj(digits) : (value ?? "");
    }

    private static string FormatStoredCep(string? value)
    {
        var digits = TextNorm.DigitsOnly(value, 8);
        return digits is { Length: 8 } ? LookupService.FormatCep(digits) : (value ?? "");
    }

    private void SetStatus(string message, bool isError = false)
    {
        LookupStatus.Text = message;
        LookupStatus.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isError ? "#B91C1C" : "#64748B")!);
    }

    private void ApplyMaskedText(TextBox box, string formatted)
    {
        if (box.Text == formatted)
            return;
        _suppressFormat = true;
        var caretAtEnd = box.CaretIndex >= (box.Text?.Length ?? 0);
        box.Text = formatted;
        box.CaretIndex = caretAtEnd ? formatted.Length : Math.Min(box.CaretIndex, formatted.Length);
        _suppressFormat = false;
    }

    private void CnpjBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFormat) return;
        ApplyMaskedText(CnpjBox, LookupService.FormatCnpjTyping(CnpjBox.Text));

        var digits = TextNorm.DigitsOnly(CnpjBox.Text, 14);
        if (digits is { Length: 14 } && digits != _lastCnpjLookup && !_cnpjBusy)
            _ = LookupCnpjAsync(auto: true);
    }

    private void CnpjBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var digits = TextNorm.DigitsOnly(CnpjBox.Text, 14);
        if (digits is { Length: 14 })
            ApplyMaskedText(CnpjBox, LookupService.FormatCnpj(digits));
    }

    private void CepBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFormat) return;
        ApplyMaskedText(CepBox, LookupService.FormatCepTyping(CepBox.Text));

        var digits = TextNorm.DigitsOnly(CepBox.Text, 8);
        if (digits is { Length: 8 } && digits != _lastCepLookup && !_cepBusy)
            _ = LookupCepAsync(auto: true);
    }

    private void CepBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var digits = TextNorm.DigitsOnly(CepBox.Text, 8);
        if (digits is { Length: 8 })
            ApplyMaskedText(CepBox, LookupService.FormatCep(digits));
    }

    private void TelBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFormat) return;
        ApplyMaskedText(TelBox, LookupService.FormatPhone(TelBox.Text));
    }

    private void TelBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var digits = Regex.Replace(TelBox.Text ?? "", @"\D", "");
        if (digits.Length is 10 or 11)
            ApplyMaskedText(TelBox, LookupService.FormatPhone(digits));
    }

    private async void LookupCnpj_Click(object sender, RoutedEventArgs e) =>
        await LookupCnpjAsync(auto: false);

    private async void LookupCep_Click(object sender, RoutedEventArgs e) =>
        await LookupCepAsync(auto: false);

    private async Task LookupCnpjAsync(bool auto)
    {
        var digits = TextNorm.DigitsOnly(CnpjBox.Text, 14);
        if (digits is null || digits.Length != 14)
        {
            if (!auto)
                SetStatus("Digite os 14 números do CNPJ.", isError: true);
            return;
        }

        if (_cnpjBusy)
            return;

        _cnpjBusy = true;
        BtnLookupCnpj.IsEnabled = false;
        SetStatus("Buscando CNPJ…");
        try
        {
            var data = await LookupService.LookupCnpjAsync(digits);
            _lastCnpjLookup = digits;
            ApplyMaskedText(CnpjBox, data.CpfCnpj);

            if (!string.IsNullOrWhiteSpace(data.TradeName))
                FantasiaBox.Text = data.TradeName;
            else if (!string.IsNullOrWhiteSpace(data.Name) && string.IsNullOrWhiteSpace(FantasiaBox.Text))
                FantasiaBox.Text = data.Name;

            if (!string.IsNullOrWhiteSpace(data.Name))
                RazaoBox.Text = data.Name;
            if (!string.IsNullOrWhiteSpace(data.Cep))
            {
                ApplyMaskedText(CepBox, data.Cep);
                _lastCepLookup = TextNorm.DigitsOnly(data.Cep, 8);
            }
            if (!string.IsNullOrWhiteSpace(data.Address))
                EnderecoBox.Text = data.Address;
            if (!string.IsNullOrWhiteSpace(data.AddressNumber))
                NumeroBox.Text = data.AddressNumber;
            if (!string.IsNullOrWhiteSpace(data.Neighborhood))
                BairroBox.Text = data.Neighborhood;
            if (!string.IsNullOrWhiteSpace(data.City))
                CidadeBox.Text = data.City;
            if (!string.IsNullOrWhiteSpace(data.State))
                UfBox.Text = data.State;
            if (!string.IsNullOrWhiteSpace(data.Email))
                EmailBox.Text = data.Email;
            if (!string.IsNullOrWhiteSpace(data.Phone))
                ApplyMaskedText(TelBox, LookupService.FormatPhone(data.Phone));

            SetStatus("Dados da empresa preenchidos pelo CNPJ.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            _cnpjBusy = false;
            BtnLookupCnpj.IsEnabled = true;
        }
    }

    private async Task LookupCepAsync(bool auto)
    {
        var digits = TextNorm.DigitsOnly(CepBox.Text, 8);
        if (digits is null || digits.Length != 8)
        {
            if (!auto)
                SetStatus("Digite os 8 números do CEP.", isError: true);
            return;
        }

        if (_cepBusy)
            return;

        _cepBusy = true;
        BtnLookupCep.IsEnabled = false;
        SetStatus("Buscando CEP…");
        try
        {
            var data = await LookupService.LookupCepAsync(digits);
            _lastCepLookup = digits;
            ApplyMaskedText(CepBox, data.Cep);
            if (!string.IsNullOrWhiteSpace(data.Address))
                EnderecoBox.Text = data.Address;
            if (!string.IsNullOrWhiteSpace(data.Neighborhood))
                BairroBox.Text = data.Neighborhood;
            if (!string.IsNullOrWhiteSpace(data.City))
                CidadeBox.Text = data.City;
            if (!string.IsNullOrWhiteSpace(data.State))
                UfBox.Text = data.State;

            SetStatus("Endereço preenchido pelo CEP.");
            NumeroBox.Focus();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            _cepBusy = false;
            BtnLookupCep.IsEnabled = true;
        }
    }

    private void RefreshMpTokenHint()
    {
        if (MercadoPagoCredentials.HasToken())
        {
            MpTokenHint.Text = "Token salvo neste PC: " + MercadoPagoCredentials.MaskedTokenPreview();
            MpTokenHint.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#0F766E")!);
        }
        else
        {
            MpTokenHint.Text = "Token atual: (não configurado) — cole o Access Token e salve (F9)";
            MpTokenHint.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#B45309")!);
        }
    }

    private void ClearMpToken_Click(object sender, RoutedEventArgs e)
    {
        MercadoPagoCredentials.Clear();
        MpTokenBox.Text = "";
        MpPixEnabledBox.IsChecked = false;
        RefreshMpTokenHint();
        MessageBox.Show("Token removido.", "Mercado Pago", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshMeuDanfeHint()
    {
        if (MeuDanfeCredentials.HasApiKey())
        {
            MeuDanfeApiHint.Text = "Api-Key salva neste PC: " + MeuDanfeCredentials.MaskedApiKeyPreview();
            MeuDanfeApiHint.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#0F766E")!);
        }
        else
        {
            MeuDanfeApiHint.Text = "Api-Key atual: (não configurado) — cole e salve (F9)";
            MeuDanfeApiHint.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#B45309")!);
        }
    }

    private void ClearMeuDanfeKey_Click(object sender, RoutedEventArgs e)
    {
        MeuDanfeCredentials.Clear();
        MeuDanfeApiKeyBox.Text = "";
        RefreshMeuDanfeHint();
        MessageBox.Show("Api-Key removida.", "Meu Danfe", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowLogo(string? path)
    {
        LogoPreview.Source = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            LogoPreview.Source = bmp;
        }
        catch { /* ignore */ }
    }

    private void Logo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos|*.*",
            Title = "Logomarca do estabelecimento",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _logoPath = AppSettingsService.SaveLogoFile(dlg.FileName);
            ShowLogo(_logoPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Logo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearLogo_Click(object sender, RoutedEventArgs e)
    {
        AppSettingsService.ClearLogo();
        _logoPath = null;
        LogoPreview.Source = null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = new CompanyProfile
            {
                NomeFantasia = FantasiaBox.Text,
                RazaoSocial = RazaoBox.Text,
                Cnpj = CnpjBox.Text,
                Ie = IeBox.Text,
                Endereco = EnderecoBox.Text,
                Numero = NumeroBox.Text,
                Bairro = BairroBox.Text,
                Cidade = CidadeBox.Text,
                Uf = UfBox.Text,
                Cep = CepBox.Text,
                Telefone = TelBox.Text,
                Email = EmailBox.Text,
                PixKey = PixBox.Text,
                LogoPath = _logoPath,
            };
            if (string.IsNullOrWhiteSpace(p.NomeFantasia) && string.IsNullOrWhiteSpace(p.RazaoSocial))
                throw new InvalidOperationException("Informe o nome fantasia ou a razão social.");

            AppSettingsService.SaveCompanyProfile(p);

            var newToken = (MpTokenBox.Text ?? "").Trim();
            var tokenMsg = "";
            if (!string.IsNullOrEmpty(newToken))
            {
                if (!newToken.Contains("APP_USR", StringComparison.OrdinalIgnoreCase)
                    && !newToken.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "O Access Token parece inválido. Ele costuma começar com APP_USR- (produção).");
                }

                MercadoPagoCredentials.SaveAccessToken(newToken);
                MpTokenBox.Text = "";
                tokenMsg = "\n\nToken Mercado Pago salvo: " + MercadoPagoCredentials.MaskedTokenPreview();
            }

            var wantPix = MpPixEnabledBox.IsChecked == true;
            if (wantPix && !MercadoPagoCredentials.HasToken())
            {
                MpPixEnabledBox.IsChecked = false;
                throw new InvalidOperationException(
                    "Para ativar o QR PIX automático, cole o Access Token no campo e salve de novo.");
            }

            MercadoPagoCredentials.SetPixEnabled(wantPix && MercadoPagoCredentials.HasToken());
            RefreshMpTokenHint();
            MpPixEnabledBox.IsChecked = MercadoPagoCredentials.IsPixEnabled();

            var newMeuDanfe = (MeuDanfeApiKeyBox.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(newMeuDanfe))
            {
                if (newMeuDanfe.Length < 16)
                    throw new InvalidOperationException("A Api-Key do Meu Danfe parece incompleta.");
                MeuDanfeCredentials.SaveApiKey(newMeuDanfe);
                MeuDanfeApiKeyBox.Text = "";
                tokenMsg += "\n\nApi-Key Meu Danfe salva: " + MeuDanfeCredentials.MaskedApiKeyPreview();
            }
            RefreshMeuDanfeHint();

            AuditService.Log("salvar", "empresa", null, p.DisplayName);
            CompanySaved?.Invoke(this, EventArgs.Empty);
            MessageBox.Show("Dados da empresa salvos." + tokenMsg, "Empresa",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Empresa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9) { Save_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
