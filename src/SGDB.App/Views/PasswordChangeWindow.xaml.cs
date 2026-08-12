using System.Windows;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public enum PasswordChangeMode
{
    Forced,
    Recovery,
    AdminReset,
}

public partial class PasswordChangeWindow : Window
{
    private readonly PasswordChangeMode _mode;
    private readonly User? _user;
    private readonly int? _targetUserId;

    public bool PasswordChanged { get; private set; }

    public PasswordChangeWindow(PasswordChangeMode mode, User? user = null, string? prefilledLogin = null)
        : this(mode, user, prefilledLogin, targetUserId: null)
    {
    }

    public PasswordChangeWindow(
        PasswordChangeMode mode,
        User? user,
        string? prefilledLogin,
        int? targetUserId,
        string? targetNome = null)
    {
        InitializeComponent();
        _mode = mode;
        _user = user;
        _targetUserId = targetUserId;

        if (mode == PasswordChangeMode.Forced)
        {
            TitleText.Text = "Redefinir senha";
            SubtitleText.Text =
                "Você está usando a senha padrão de fábrica. Defina uma senha pessoal para continuar.";
            LoginBox.Text = user?.Login ?? SetupService.FactoryLogin;
            LoginBox.IsReadOnly = true;
            RecoveryLabel.Visibility = Visibility.Collapsed;
            RecoveryBox.Visibility = Visibility.Collapsed;
            SenhaBox.Focus();
        }
        else if (mode == PasswordChangeMode.AdminReset)
        {
            TitleText.Text = "Redefinir senha (admin)";
            var nome = string.IsNullOrWhiteSpace(targetNome) ? "" : $" ({targetNome.Trim()})";
            SubtitleText.Text =
                $"Defina uma nova senha para o usuário {prefilledLogin}{nome}.";
            LoginBox.Text = prefilledLogin ?? "";
            LoginBox.IsReadOnly = true;
            RecoveryLabel.Visibility = Visibility.Collapsed;
            RecoveryBox.Visibility = Visibility.Collapsed;
            SenhaBox.Focus();
        }
        else
        {
            TitleText.Text = "Esqueceu a senha?";
            SubtitleText.Text =
                "Informe o login, o código de recuperação (gerado na configuração inicial) e a nova senha.\n" +
                "Sem o código, peça a um administrador para redefinir em Usuários.";
            LoginBox.Text = prefilledLogin ?? "";
            RecoveryLabel.Visibility = Visibility.Visible;
            RecoveryBox.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(LoginBox.Text))
                LoginBox.Focus();
            else
                RecoveryBox.Focus();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            if (SenhaBox.Password != Senha2Box.Password)
                throw new InvalidOperationException("As senhas não coincidem.");
            if (SenhaBox.Password.Length < 4)
                throw new InvalidOperationException("A senha deve ter pelo menos 4 caracteres.");

            if (_mode == PasswordChangeMode.Forced)
            {
                if (_user is null)
                    throw new InvalidOperationException("Usuário inválido.");
                if (SetupService.IsFactoryDefaultPassword(_user.Login, SenhaBox.Password))
                    throw new InvalidOperationException("Escolha uma senha diferente da padrão (admin).");

                UsersService.Save(
                    _user.Id,
                    _user.Login,
                    _user.Nome,
                    _user.Role,
                    true,
                    SenhaBox.Password);

                if (!SetupService.HasRecoveryCode())
                {
                    var code = SetupService.RegenerateRecoveryCode();
                    MessageBox.Show(
                        "Senha alterada.\n\n" +
                        "Guarde este código de recuperação (não será mostrado de novo):\n\n" +
                        $"    {code}",
                        "Código de recuperação",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else if (_mode == PasswordChangeMode.AdminReset)
            {
                if (_targetUserId is not int uid)
                    throw new InvalidOperationException("Usuário inválido.");
                UsersService.ResetPasswordByAdmin(uid, SenhaBox.Password);
            }
            else
            {
                if (!SetupService.TryResetPasswordWithRecovery(
                        LoginBox.Text, RecoveryBox.Text, SenhaBox.Password, out var err))
                    throw new InvalidOperationException(err);
            }

            PasswordChanged = true;
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
