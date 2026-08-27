using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;
using SGDB.Views;

namespace SGDB;

public partial class App : System.Windows.Application
{
    private static BitmapFrame? _appIcon;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        DatePickerUxHelper.RegisterClearWatermark();
        ApplicationLoginService.RemoteSessionInvalidated += OnRemoteSessionInvalidated;
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            RegisterGlobalWindowIcon();

            var isolatedDb = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(DatabaseService.DatabasePathEnvVar));
            var testFolder = (AppContext.BaseDirectory ?? "")
                .Contains("SGDB_TESTE", StringComparison.OrdinalIgnoreCase);

            // Isolado: não atalho, não atualizador (protege a instalação da loja).
            if (!isolatedDb && !testFolder)
            {
                if (AutoUpdateService.TryHandleApplyUpdateArgs(e.Args))
                    return;
                DesktopShortcutService.EnsureDesktopShortcut();
            }

            DatabaseService.Initialize();
            try
            {
                ProductService.BackfillMissingClassifications();
                ProductService.BackfillFixDoubleDividedUnitCosts();
                ProductService.BackfillFixCigarettePrices();
                ProductService.SanitizeAllCatalogNamesOnce();
                ProductService.NormalizePackUnitsToUnOnce();
                PayableService.NormalizeLegacyStatuses();
            }
            catch { /* não bloqueia abertura */ }

            User? user = null;
            string? typedPassword = null;

            if (ApplicationLoginService.ShouldRunInitialSetup())
            {
                var setup = new InitialSetupWindow();
                if (setup.ShowDialog() != true || setup.CreatedAdmin is null)
                {
                    Shutdown();
                    return;
                }
                user = setup.CreatedAdmin;
            }
            else
            {
                var login = new LoginWindow();
                if (login.ShowDialog() != true || login.AuthenticatedUser is null)
                {
                    Shutdown();
                    return;
                }
                user = login.AuthenticatedUser;
                typedPassword = login.TypedPassword;
            }

            if (!CompleteInteractiveLogin(user, typedPassword))
            {
                Shutdown();
                return;
            }

            ShowMainWindow(user);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            MessageBox.Show(
                $"Erro ao iniciar o SGDB:\n\n{ex.Message}\n\nDetalhes salvos em:\n{CrashLogPath()}",
                "SGDB — Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal bool CompleteInteractiveLogin(User user, string? typedPassword)
    {
        if (ApplicationLoginService.ShouldForceLocalPasswordChange(user, typedPassword))
        {
            var change = new PasswordChangeWindow(PasswordChangeMode.Forced, user) { Owner = null };
            if (change.ShowDialog() != true || !change.PasswordChanged)
            {
                MessageBox.Show(
                    "É necessário alterar a senha padrão para usar o sistema.",
                    "SGDB",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }

        AppSession.SetUser(user);
        AuditService.Log("login", "sessao", user.Id.ToString(), user.Login);
        return true;
    }

    internal void ShowMainWindow(User user)
    {
        var main = new MainWindow(user);
        MainWindow = main;
        main.Show();
        main.Activate();
        main.Topmost = true;
        main.Topmost = false;
        main.Focus();
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(DatabaseService.DatabasePathEnvVar)))
            _ = CheckForUpdatesInBackgroundAsync(main);
    }

    private bool _handlingRemoteExpiry;

    private void OnRemoteSessionInvalidated(StoreNetworkSessionExpiredException ex)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnRemoteSessionInvalidated(ex));
            return;
        }

        ReloginAfterRemoteSessionExpired(ex);
    }

    private void ReloginAfterRemoteSessionExpired(StoreNetworkSessionExpiredException ex)
    {
        if (_handlingRemoteExpiry)
            return;
        if (Windows.OfType<LoginWindow>().Any())
            return;

        _handlingRemoteExpiry = true;
        try
        {
            MessageBox.Show(
                ex.Message,
                "Rede Loja",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            var previous = MainWindow as MainWindow;
            var login = new LoginWindow();
            if (login.ShowDialog() == true && login.AuthenticatedUser is not null)
            {
                if (!CompleteInteractiveLogin(login.AuthenticatedUser, login.TypedPassword))
                {
                    Shutdown();
                    return;
                }

                ShowMainWindow(login.AuthenticatedUser);
                previous?.BeginLogoutClose();
                return;
            }

            Shutdown();
        }
        finally
        {
            _handlingRemoteExpiry = false;
        }
    }

    private static async Task CheckForUpdatesInBackgroundAsync(Window owner)
    {
        try
        {
            // Pequeno atraso para a janela principal aparecer primeiro
            await Task.Delay(1500).ConfigureAwait(false);
            await AutoUpdateService.CheckAndOfferUpdateAsync(owner).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
        }
    }

    /// <summary>Aplica o ícone do app em todas as janelas (título e barra de tarefas).</summary>
    private static void RegisterGlobalWindowIcon()
    {
        try
        {
            _appIcon = BitmapFrame.Create(
                new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
            System.Windows.EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyWindowLoaded));
        }
        catch { /* ícone é opcional */ }
    }

    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_appIcon is not null && sender is Window w && w.Icon is null)
            w.Icon = _appIcon;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is StoreNetworkSessionExpiredException)
        {
            e.Handled = true;
            return;
        }

        WriteCrashLog(e.Exception);
        MessageBox.Show(
            $"Erro inesperado:\n\n{e.Exception.Message}\n\n{e.Exception.GetType().Name}\n\nLog: {CrashLogPath()}",
            "SGDB — Erro",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog(ex);
    }

    private static string CrashLogPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SGDB",
            "crash.log");

    private static void WriteCrashLog(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath());
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(
                CrashLogPath(),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n\n");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
