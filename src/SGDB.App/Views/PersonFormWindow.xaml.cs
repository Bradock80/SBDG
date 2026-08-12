using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class PersonFormWindow : Window
{
    private static readonly (string Value, string Label)[] KindOptions =
    {
        ("fisica", "FÍSICA"),
        ("juridica", "JURÍDICA"),
    };

    private static readonly (string Value, string Label)[] ReceiptOptions =
    {
        ("", "—"),
        ("dinheiro", "Dinheiro"),
        ("pix", "Pix"),
        ("boleto", "Boleto"),
        ("cartao", "Cartão"),
    };

    private readonly int? _personId;

    public PersonFormWindow(int? personId)
    {
        _personId = personId;
        InitializeComponent();
        KindBox.ItemsSource = KindOptions.Select(k => k.Label).ToList();
        KindBox.SelectedIndex = 1;
        ReceiptBox.ItemsSource = ReceiptOptions.Select(r => r.Label).ToList();
        ReceiptBox.SelectedIndex = 0;

        if (personId is null)
        {
            TitleText.Text = "Cadastro de Pessoas — Novo";
            IdBox.Text = "(automático)";
        }
        else
            LoadPerson(personId.Value);
    }

    private bool IsJuridica => KindBox.SelectedIndex == 1;

    private void LoadPerson(int id)
    {
        var person = PersonService.GetById(id);
        if (person is null)
        {
            MessageBox.Show("Pessoa não encontrada.", "Clientes", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        TitleText.Text = $"Cadastro de Pessoas — {person.Name}";
        IdBox.Text = person.Id.ToString();
        DocBox.Text = FormatDocDisplay(person.CpfCnpj);
        RgBox.Text = person.RgIe ?? "";
        KindBox.SelectedIndex = person.PersonKind == "fisica" ? 0 : 1;
        NameBox.Text = person.Name;
        TradeBox.Text = person.TradeName ?? "";
        CepBox.Text = FormatCepDisplay(person.Cep);
        AddressBox.Text = person.Address ?? "";
        NumberBox.Text = person.AddressNumber ?? "";
        ComplementBox.Text = person.Complement ?? "";
        NeighborhoodBox.Text = person.Neighborhood ?? "";
        CityBox.Text = person.City ?? "";
        StateBox.Text = person.State ?? "";
        EmailBox.Text = person.Email ?? "";
        PhoneBox.Text = person.Phone ?? "";
        Cell1Box.Text = person.Cell1 ?? "";
        WhatsappBox.Text = person.Whatsapp ?? "";
        Phone2Box.Text = person.Phone2 ?? "";
        Cell2Box.Text = person.Cell2 ?? "";
        NotesBox.Text = person.Notes ?? "";

        var receiptIdx = Array.FindIndex(ReceiptOptions, r => r.Value == (person.ReceiptType ?? ""));
        ReceiptBox.SelectedIndex = receiptIdx >= 0 ? receiptIdx : 0;

        var roles = person.Roles;
        RoleAtivoBox.IsChecked = person.Active;
        RoleClientesBox.IsChecked = roles.Clientes;
        RoleFornecedoresBox.IsChecked = roles.Fornecedores;
        RoleFuncionariosBox.IsChecked = roles.Funcionarios;
        RoleCredenciadorasBox.IsChecked = roles.Credenciadoras;
        RoleParceirosBox.IsChecked = roles.Parceiros;
        RoleCcfBox.IsChecked = roles.CcfSpc;
        RoleEstrangeiroBox.IsChecked = roles.Estrangeiro;
        RoleMarketplacesBox.IsChecked = roles.Marketplaces;

        var unitExtra = person.FiadoUnitSurcharge;
        FiadoUnitSurchargeBox.IsChecked = unitExtra > 0.009;
        FiadoUnitSurchargeValBox.Text = ProductPriceHelper.FormatBr(unitExtra > 0.009 ? unitExtra : 0.50);
        FiadoUnitSurchargePanel.Visibility = FiadoUnitSurchargeBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void FiadoUnitSurchargeBox_Changed(object sender, RoutedEventArgs e)
    {
        var on = FiadoUnitSurchargeBox.IsChecked == true;
        FiadoUnitSurchargePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on && ProductPriceHelper.ParseBr(FiadoUnitSurchargeValBox.Text) < 0.009)
            FiadoUnitSurchargeValBox.Text = ProductPriceHelper.FormatBr(0.50);
    }

    private void FiadoUnitSurchargeVal_LostFocus(object sender, RoutedEventArgs e) =>
        FiadoUnitSurchargeValBox.Text =
            ProductPriceHelper.FormatBr(Math.Max(0, ProductPriceHelper.ParseBr(FiadoUnitSurchargeValBox.Text)));

    private static string FormatDocDisplay(string? value)
    {
        var digits = TextNorm.DigitsOnly(value, 14);
        if (digits is null)
            return value ?? "";
        return digits.Length == 14 ? LookupService.FormatCnpj(digits) : digits;
    }

    private static string FormatCepDisplay(string? value)
    {
        var digits = TextNorm.DigitsOnly(value, 8);
        if (digits is null)
            return value ?? "";
        return LookupService.FormatCep(digits);
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && btn.Tag is string tag)
            SelectTab(tag);
    }

    private void SelectTab(string tag)
    {
        TabDados.IsChecked = tag == "dados";
        TabAdicionais.IsChecked = tag == "adicionais";
        TabContatos.IsChecked = tag == "contatos";

        PanelDados.Visibility = tag == "dados" ? Visibility.Visible : Visibility.Collapsed;
        PanelAdicionais.Visibility = tag == "adicionais" ? Visibility.Visible : Visibility.Collapsed;
        PanelContatos.Visibility = tag == "contatos" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LookupCnpj_Click(object sender, RoutedEventArgs e) => await LookupCnpjAsync();

    private async void LookupCep_Click(object sender, RoutedEventArgs e) => await LookupCepAsync();

    private async Task LookupCepAsync()
    {
        var digits = TextNorm.DigitsOnly(CepBox.Text, 8);
        if (digits is null || digits.Length != 8)
        {
            SetStatus("Digite os 8 números do CEP e clique na lupa.", isError: true);
            CepBox.Focus();
            return;
        }

        BtnLookupCep.IsEnabled = false;
        SetStatus("Buscando CEP…");
        try
        {
            var data = await LookupService.LookupCepAsync(digits);
            CepBox.Text = data.Cep;
            if (!string.IsNullOrWhiteSpace(data.Address))
                AddressBox.Text = data.Address;
            if (!string.IsNullOrWhiteSpace(data.Neighborhood))
                NeighborhoodBox.Text = data.Neighborhood;
            if (!string.IsNullOrWhiteSpace(data.City))
                CityBox.Text = data.City;
            if (!string.IsNullOrWhiteSpace(data.State))
                StateBox.Text = data.State;
            if (!string.IsNullOrWhiteSpace(data.Complement) && string.IsNullOrWhiteSpace(ComplementBox.Text))
                ComplementBox.Text = data.Complement;

            SetStatus("Endereço preenchido.");
            NumberBox.Focus();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            BtnLookupCep.IsEnabled = true;
        }
    }

    private async Task LookupCnpjAsync()
    {
        if (!IsJuridica)
        {
            SetStatus("Busca automática só para Pessoa Jurídica.", isError: true);
            return;
        }

        var digits = TextNorm.DigitsOnly(DocBox.Text, 14);
        if (digits is null || digits.Length != 14)
        {
            SetStatus("Digite os 14 números do CNPJ e clique na lupa.", isError: true);
            DocBox.Focus();
            return;
        }

        BtnLookupCnpj.IsEnabled = false;
        SetStatus("Buscando CNPJ… (pode levar alguns segundos)");
        try
        {
            var data = await LookupService.LookupCnpjAsync(digits);
            DocBox.Text = data.CpfCnpj;
            if (!string.IsNullOrWhiteSpace(data.Name))
                NameBox.Text = data.Name;
            if (!string.IsNullOrWhiteSpace(data.TradeName))
                TradeBox.Text = data.TradeName;
            if (!string.IsNullOrWhiteSpace(data.Cep))
                CepBox.Text = data.Cep;
            if (!string.IsNullOrWhiteSpace(data.Address))
                AddressBox.Text = data.Address;
            if (!string.IsNullOrWhiteSpace(data.AddressNumber))
                NumberBox.Text = data.AddressNumber;
            if (!string.IsNullOrWhiteSpace(data.Complement))
                ComplementBox.Text = data.Complement;
            if (!string.IsNullOrWhiteSpace(data.Neighborhood))
                NeighborhoodBox.Text = data.Neighborhood;
            if (!string.IsNullOrWhiteSpace(data.City))
                CityBox.Text = data.City;
            if (!string.IsNullOrWhiteSpace(data.State))
                StateBox.Text = data.State;
            if (!string.IsNullOrWhiteSpace(data.Email))
                EmailBox.Text = data.Email;
            if (!string.IsNullOrWhiteSpace(data.Phone) && string.IsNullOrWhiteSpace(PhoneBox.Text))
                PhoneBox.Text = data.Phone;

            SetStatus("Dados da empresa preenchidos.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            BtnLookupCnpj.IsEnabled = true;
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28))
            : new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        if (!isError && string.IsNullOrWhiteSpace(message))
            StatusText.Foreground = Brushes.Black;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput();
            if (_personId is null)
                PersonService.Create(input);
            else
                PersonService.Update(_personId.Value, input);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Clientes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private PersonInput BuildInput()
    {
        var kind = KindBox.SelectedIndex == 0 ? "fisica" : "juridica";
        var receiptIdx = ReceiptBox.SelectedIndex;
        var receipt = receiptIdx >= 0 && receiptIdx < ReceiptOptions.Length
            ? ReceiptOptions[receiptIdx].Value
            : "";

        var roles = new PersonRoles
        {
            Ativo = RoleAtivoBox.IsChecked == true,
            Clientes = RoleClientesBox.IsChecked == true,
            Fornecedores = RoleFornecedoresBox.IsChecked == true,
            Funcionarios = RoleFuncionariosBox.IsChecked == true,
            Credenciadoras = RoleCredenciadorasBox.IsChecked == true,
            Parceiros = RoleParceirosBox.IsChecked == true,
            CcfSpc = RoleCcfBox.IsChecked == true,
            Estrangeiro = RoleEstrangeiroBox.IsChecked == true,
            Marketplaces = RoleMarketplacesBox.IsChecked == true,
        };

        return new PersonInput
        {
            PersonKind = kind,
            Name = NameBox.Text,
            TradeName = TradeBox.Text,
            CpfCnpj = DocBox.Text,
            RgIe = RgBox.Text,
            Cep = CepBox.Text,
            Address = AddressBox.Text,
            AddressNumber = NumberBox.Text,
            Complement = ComplementBox.Text,
            Neighborhood = NeighborhoodBox.Text,
            City = CityBox.Text,
            State = StateBox.Text,
            Email = EmailBox.Text,
            Phone = PhoneBox.Text,
            Cell1 = Cell1Box.Text,
            Whatsapp = WhatsappBox.Text,
            Phone2 = Phone2Box.Text,
            Cell2 = Cell2Box.Text,
            ReceiptType = receipt,
            Notes = NotesBox.Text,
            Roles = roles,
            Active = RoleAtivoBox.IsChecked == true,
            FiadoUnitSurcharge = FiadoUnitSurchargeBox.IsChecked == true
                ? Math.Max(0, ProductPriceHelper.ParseBr(FiadoUnitSurchargeValBox.Text))
                : 0,
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            Save_Click(sender, e);
            e.Handled = true;
        }
    }
}
