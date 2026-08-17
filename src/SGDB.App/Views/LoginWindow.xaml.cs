using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class LoginWindow : Window
{
    public User? AuthenticatedUser { get; private set; }

    /// <summary>Senha digitada no login bem-sucedido (para detectar senha de fábrica).</summary>
    public string? TypedPassword { get; private set; }

    private bool _passwordVisible;
    private bool _loggingIn;

    public LoginWindow()
    {
        InitializeComponent();
        DepositoLabel.Text = FormatDepositoName(AppSettingsService.GetNomeDeposito());
        ApplyNetworkModeChrome();
        LoadRememberedCredentials();
        if (string.IsNullOrWhiteSpace(LoginBox.Text))
            LoginBox.Focus();
        else if (string.IsNullOrEmpty(PasswordBox.Password))
            PasswordBox.Focus();
        else
            EnterButton.Focus();
    }

    private void ApplyNetworkModeChrome()
    {
        if (!ApplicationLoginService.IsRemoteLogin)
        {
            NetworkModeText.Visibility = Visibility.Collapsed;
            ConfigureNetworkBtn.Visibility = Visibility.Collapsed;
            RememberCheck.Content = "Lembrar usuário e senha";
            FirstAccessBtn.Visibility = SetupService.NeedsInitialSetup()
                ? Visibility.Visible
                : Visibility.Collapsed;
            FirstAccessSep.Visibility = FirstAccessBtn.Visibility;
            LocalAccountLinks.Visibility = Visibility.Visible;
            return;
        }

        var host = StoreNetworkMode.GetClientHost();
        var store = FormatDepositoName(AppSettingsService.GetNomeDeposito());
        NetworkModeText.Text = string.IsNullOrWhiteSpace(host)
            ? "Entrar na loja"
            : $"Conectado à Rede Loja • {host}";
        if (!string.IsNullOrWhiteSpace(store) && store != "Meu Depósito")
            NetworkModeText.Text += $" — {store}";
        NetworkModeText.Visibility = Visibility.Visible;

        RememberCheck.Content = "Lembrar usuário";
        LocalAccountLinks.Visibility = Visibility.Collapsed;
        ConfigureNetworkBtn.Visibility = Visibility.Visible;
    }

    private void LoadRememberedCredentials()
    {
        var saved = LoginRememberService.TryLoad();
        if (saved is null)
            return;

        LoginBox.Text = saved.Value.Login;
        RememberCheck.IsChecked = true;
        if (ApplicationLoginService.CanRememberPassword)
            PasswordBox.Password = saved.Value.Password;
    }

    private static string FormatDepositoName(string? name)
    {
        var raw = string.IsNullOrWhiteSpace(name) ? "Meu Depósito" : name.Trim();
        return Regex.Replace(raw, @"\bDEPOSITO\b", "DEPÓSITO", RegexOptions.IgnoreCase);
    }

    private void Enter_Click(object sender, RoutedEventArgs e) => DoLogin();

    private void LoginBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_passwordVisible)
            PasswordVisibleBox.Focus();
        else
            PasswordBox.Focus();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        DoLogin();
    }

    private void PasswordVisibleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        DoLogin();
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        if (_passwordVisible)
        {
            PasswordVisibleBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordVisibleBox.Visibility = Visibility.Visible;
            TogglePasswordBtn.ToolTip = "Ocultar senha";
            TogglePasswordIcon.Data = (Geometry)FindResource("EyeOffGeometry");
            PasswordVisibleBox.CaretIndex = PasswordVisibleBox.Text.Length;
            PasswordVisibleBox.Focus();
        }
        else
        {
            PasswordBox.Password = PasswordVisibleBox.Text;
            PasswordVisibleBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            TogglePasswordBtn.ToolTip = "Mostrar senha";
            TogglePasswordIcon.Data = (Geometry)FindResource("EyeOpenGeometry");
            PasswordBox.Focus();
        }
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        if (ApplicationLoginService.IsRemoteLogin)
        {
            MessageBox.Show(
                ApplicationLoginService.RegisterOnServerMessage,
                "Rede Loja",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new RegisterWindow { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.CreatedLogin))
        {
            LoginBox.Text = dlg.CreatedLogin;
            PasswordBox.Clear();
            PasswordVisibleBox.Clear();
            PasswordBox.Focus();
            ErrorText.Text =
                "Conta criada. Aguarde o administrador ativar em Usuários.";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void ForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        if (ApplicationLoginService.IsRemoteLogin)
        {
            MessageBox.Show(
                ApplicationLoginService.PasswordChangeUnavailableMessage,
                "Rede Loja",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!SetupService.HasRecoveryCode())
        {
            MessageBox.Show(
                "Ainda não há código de recuperação neste computador.\n\n" +
                "Peça a um administrador para redefinir a senha em:\n" +
                "Menu → Usuários → 🔑 Redefinir senha.\n\n" +
                "Se você lembrar a senha atual, entre e altere-a — um código será gerado.",
                "Esqueceu a senha?",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new PasswordChangeWindow(PasswordChangeMode.Recovery, prefilledLogin: LoginBox.Text)
        {
            Owner = this,
        };
        if (dlg.ShowDialog() == true)
        {
            MessageBox.Show("Senha redefinida. Entre com a nova senha.", "SGDB",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void FirstAccess_Click(object sender, RoutedEventArgs e)
    {
        if (ApplicationLoginService.IsRemoteLogin)
        {
            MessageBox.Show(
                ApplicationLoginService.RegisterOnServerMessage,
                "Rede Loja",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!SetupService.NeedsInitialSetup())
        {
            MessageBox.Show(
                "Este computador já possui usuários cadastrados.\n" +
                "Use o login normal ou peça ao administrador para criar sua conta em Usuários.",
                "Primeiro acesso",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var setup = new InitialSetupWindow { Owner = this };
        if (setup.ShowDialog() == true && setup.CreatedAdmin is not null)
        {
            AuthenticatedUser = setup.CreatedAdmin;
            TypedPassword = null;
            DialogResult = true;
            Close();
        }
    }

    private void ConfigureNetwork_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new StoreNetworkWindow { Owner = this };
        dlg.ShowDialog();
        ApplyNetworkModeChrome();
    }

    private string CurrentPassword =>
        _passwordVisible ? PasswordVisibleBox.Text : PasswordBox.Password;

    private void DoLogin()
    {
        if (_loggingIn)
            return;

        ErrorText.Visibility = Visibility.Collapsed;
        _loggingIn = true;
        EnterButton.IsEnabled = false;
        EnterButton.Content = "Entrando…";

        try
        {
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            var password = CurrentPassword;
            try
            {
                var result = ApplicationLoginService.TryLogin(LoginBox.Text, password);
                if (!result.Ok || result.User is null)
                {
                    ErrorText.Text = result.Error ?? "Usuário ou senha inválidos.";
                    ErrorText.Visibility = Visibility.Visible;
                    if (result.CanOpenStoreNetworkSettings)
                        ConfigureNetworkBtn.Visibility = Visibility.Visible;
                    if (_passwordVisible)
                    {
                        PasswordVisibleBox.Clear();
                        PasswordVisibleBox.Focus();
                    }
                    else
                    {
                        PasswordBox.Clear();
                        PasswordBox.Focus();
                    }
                    return;
                }

                AuthenticatedUser = result.User;
                TypedPassword = result.TypedPassword;

                if (RememberCheck.IsChecked == true)
                {
                    if (ApplicationLoginService.CanRememberPassword)
                        LoginRememberService.Save(LoginBox.Text, password);
                    else
                        LoginRememberService.SaveLoginOnly(LoginBox.Text);
                }
                else
                    LoginRememberService.Clear();

                DialogResult = true;
                Close();
            }
            catch (AuthPendingException ex)
            {
                ErrorText.Text = ex.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Erro ao entrar: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _loggingIn = false;
            if (IsLoaded)
            {
                EnterButton.IsEnabled = true;
                EnterButton.Content = "Entrar";
            }
        }
    }
}
