using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Services;

namespace SGDB.Views;

public partial class RegisterWindow : Window
{
    public string? CreatedLogin { get; private set; }

    public RegisterWindow()
    {
        InitializeComponent();
        NomeBox.Focus();
    }

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Save_Click(sender, e);
    }

    private void LoginBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var login = (LoginBox.Text ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(login))
        {
            LoginHint.Text = "Letras minúsculas, números, ponto, _ ou -.";
            LoginHint.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            return;
        }

        if (UsersService.LoginExists(login))
        {
            LoginHint.Text = "Este usuário já está em uso.";
            LoginHint.Foreground = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
        }
        else
        {
            LoginHint.Text = "Usuário disponível.";
            LoginHint.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
        }
    }

    private void Password_Changed(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SenhaBox.Password) && string.IsNullOrEmpty(Senha2Box.Password))
        {
            PasswordHint.Text = "";
            return;
        }

        if (SenhaBox.Password.Length > 0 && SenhaBox.Password.Length < 4)
        {
            PasswordHint.Text = "Mínimo de 4 caracteres.";
            PasswordHint.Foreground = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
            return;
        }

        if (!string.IsNullOrEmpty(Senha2Box.Password) && SenhaBox.Password != Senha2Box.Password)
        {
            PasswordHint.Text = "As senhas não coincidem.";
            PasswordHint.Foreground = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
            return;
        }

        if (!string.IsNullOrEmpty(Senha2Box.Password) && SenhaBox.Password == Senha2Box.Password)
        {
            PasswordHint.Text = "Senhas conferem.";
            PasswordHint.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
            return;
        }

        PasswordHint.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            ApplicationLoginService.EnsureLocalUserAdministration();
            if (SenhaBox.Password != Senha2Box.Password)
                throw new UsersException("As senhas não coincidem.");

            UsersService.RegisterSelf(NomeBox.Text, LoginBox.Text, EmailBox.Text, SenhaBox.Password);
            CreatedLogin = (LoginBox.Text ?? "").Trim().ToLowerInvariant();

            MessageBox.Show(
                "Conta criada com sucesso.\n\n" +
                "Ela fica aguardando aprovação do administrador.\n" +
                "Depois de ativada em Usuários, você poderá entrar.",
                "Criar conta",
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
