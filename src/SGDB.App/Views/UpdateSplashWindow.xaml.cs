using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SGDB.Views;

public partial class UpdateSplashWindow : Window
{
    public UpdateSplashWindow()
    {
        InitializeComponent();
        LoadLogo();
    }

    private void LoadLogo()
    {
        try
        {
            var pack = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
            LogoImage.Source = pack;
            return;
        }
        catch { /* tenta arquivo ao lado do exe */ }

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                LogoImage.Source = bmp;
                return;
            }
        }
        catch { /* fallback texto */ }

        LogoImage.Visibility = Visibility.Collapsed;
        LogoFallback.Visibility = Visibility.Visible;
    }

    public void SetProgress(double percent, string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetProgress(percent, status));
            return;
        }

        var p = Math.Clamp(percent, 0, 100);
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = p;
        PercentText.Text = $"{p:0}%";
        if (!string.IsNullOrWhiteSpace(status))
            StatusText.Text = status;
    }

    public void SetIndeterminate(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetIndeterminate(status));
            return;
        }

        ProgressBar.IsIndeterminate = true;
        PercentText.Text = "";
        if (!string.IsNullOrWhiteSpace(status))
            StatusText.Text = status;
    }
}
