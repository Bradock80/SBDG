using System.Windows;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class InitialSetupWindow : Window
{
    public User? CreatedAdmin { get; private set; }
    public string? RecoveryCodeShown { get; private set; }

    public InitialSetupWindow()
    {
        InitializeComponent();
        FantasiaBox.Focus();
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            if (SenhaBox.Password != Senha2Box.Password)
                throw new InvalidOperationException("As senhas não coincidem.");

            var company = new CompanyProfile
            {
                NomeFantasia = FantasiaBox.Text.Trim(),
                RazaoSocial = RazaoBox.Text.Trim(),
                Cnpj = CnpjBox.Text.Trim(),
                Telefone = TelefoneBox.Text.Trim(),
                Cidade = CidadeBox.Text.Trim(),
                Uf = UfBox.Text.Trim().ToUpperInvariant(),
            };

            var recovery = SetupService.CompleteInitialSetup(
                company,
                AdminLoginBox.Text,
                AdminNomeBox.Text,
                SenhaBox.Password);

            RecoveryCodeShown = recovery;
            CreatedAdmin = AuthService.TryLogin(AdminLoginBox.Text.Trim(), SenhaBox.Password)
                ?? throw new InvalidOperationException("Admin criado, mas o login automático falhou. Entre pela tela de login.");

            MessageBox.Show(
                "Configuração concluída!\n\n" +
                "Guarde este código de recuperação de senha em local seguro:\n\n" +
                $"    {recovery}\n\n" +
                "Ele será pedido se você esquecer a senha. Não será mostrado de novo.",
                "Código de recuperação",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
