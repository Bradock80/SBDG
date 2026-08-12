using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class BackupModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private BackupSettings _settings = new();
    private readonly List<string> _scheduleTimes = [];

    public BackupModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DbPathText.Text = $"Banco atual: {DatabaseService.DatabasePath}";
            AutoFolderHint.Text = $"Backups automáticos locais: {BackupService.DefaultBackupFolder}";
            LoadSettings();
            Focus();
        };
    }

    private void LoadSettings()
    {
        _settings = BackupSettingsService.Load();
        _scheduleTimes.Clear();
        _scheduleTimes.AddRange(_settings.ScheduleTimes);

        AutoEnabledBox.IsChecked = _settings.AutoEnabled;
        BackupOnCashCloseBox.IsChecked = _settings.BackupOnCashClose;
        BackupOnAppCloseBox.IsChecked = _settings.BackupOnAppClose;
        RetentionEnabledBox.IsChecked = _settings.RetentionEnabled;
        SetRetentionDays(_settings.RetentionDays > 0 ? _settings.RetentionDays : 30);
        CloudEnabledBox.IsChecked = _settings.CloudEnabled;
        CloudFolderBox.Text = _settings.CloudFolderPath;
        CloudSyncFolderRadio.IsChecked = _settings.CloudMode != "google_api";
        CloudGoogleApiRadio.IsChecked = _settings.CloudMode == "google_api";

        RenderScheduleChips();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        _settings = BackupSettingsService.Load();
        StatusText.Text = _settings.FooterStatusDisplay;
        StatusText.Foreground = _settings.LastBackupSuccess || string.IsNullOrWhiteSpace(_settings.LastBackupAt)
            ? new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34))
            : new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    }

    private void RenderScheduleChips()
    {
        ScheduleTimesPanel.Children.Clear();
        foreach (var time in _scheduleTimes.OrderBy(t => t, StringComparer.Ordinal))
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xF2, 0xFE)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x7D, 0xD3, 0xFC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(10, 4, 6, 4),
                Margin = new Thickness(0, 0, 8, 8),
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = time,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x03, 0x64, 0x8A)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });

            var remove = new Button
            {
                Content = "✖",
                Padding = new Thickness(4, 0, 4, 0),
                MinWidth = 24,
                Height = 22,
                FontSize = 10,
                Tag = time,
                Style = (Style)FindResource("SgdbOutlineButtonStyle"),
            };
            remove.Click += RemoveScheduleTime_Click;
            panel.Children.Add(remove);
            border.Child = panel;
            ScheduleTimesPanel.Children.Add(border);
        }
    }

    private void SetRetentionDays(int days)
    {
        days = Math.Clamp(days, 1, 365);
        var text = days.ToString();
        foreach (ComboBoxItem item in RetentionDaysBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), text, StringComparison.Ordinal))
            {
                RetentionDaysBox.SelectedItem = item;
                RetentionDaysBox.Text = text;
                return;
            }
        }

        RetentionDaysBox.SelectedItem = null;
        RetentionDaysBox.Text = text;
    }

    private int GetRetentionDays()
    {
        var raw = RetentionDaysBox.Text?.Trim() ?? "";
        if (RetentionDaysBox.SelectedItem is ComboBoxItem selected)
            raw = selected.Content?.ToString()?.Trim() ?? raw;

        if (!int.TryParse(raw, out var days) || days <= 0)
            return 30;
        return Math.Clamp(days, 1, 365);
    }

    private BackupSettings ReadSettingsFromUi()
    {
        var days = GetRetentionDays();

        return new BackupSettings
        {
            AutoEnabled = AutoEnabledBox.IsChecked == true,
            ScheduleTimes = BackupSettingsService.NormalizeTimes(_scheduleTimes),
            BackupOnCashClose = BackupOnCashCloseBox.IsChecked == true,
            BackupOnAppClose = BackupOnAppCloseBox.IsChecked == true,
            RetentionEnabled = RetentionEnabledBox.IsChecked == true,
            RetentionDays = days,
            CloudEnabled = CloudEnabledBox.IsChecked == true,
            CloudMode = CloudGoogleApiRadio.IsChecked == true ? "google_api" : "sync_folder",
            CloudFolderPath = CloudFolderBox.Text.Trim(),
            LastBackupAt = _settings.LastBackupAt,
            LastBackupTrigger = _settings.LastBackupTrigger,
            LastBackupSuccess = _settings.LastBackupSuccess,
            LastBackupPath = _settings.LastBackupPath,
            LastCloudSuccess = _settings.LastCloudSuccess,
            LastCloudAt = _settings.LastCloudAt,
            LastCloudPath = _settings.LastCloudPath,
            LastError = _settings.LastError,
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CloudEnabledBox.IsChecked == true
                && CloudSyncFolderRadio.IsChecked == true
                && string.IsNullOrWhiteSpace(CloudFolderBox.Text))
            {
                MessageBox.Show("Selecione a pasta sincronizada com a nuvem ou desative o backup na nuvem.",
                    "Backup na Nuvem", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _settings = ReadSettingsFromUi();
            BackupSettingsService.Save(_settings);
            BackupSchedulerService.Restart();
            RefreshStatus();
            MessageBox.Show("Configurações de backup salvas.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Salvar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "Salvar cópia de segurança",
                Filter = "Backup SGDB (*.zip)|*.zip",
                FileName = $"SGDB_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip",
                InitialDirectory = BackupService.DefaultBackupFolder,
            };
            if (dlg.ShowDialog() != true) return;

            var path = BackupService.CreateBackup(dlg.FileName, asZip: true);

            var settings = BackupSettingsService.Load();
            if (settings.CloudEnabled
                && settings.CloudMode == "sync_folder"
                && !string.IsNullOrWhiteSpace(settings.CloudFolderPath))
            {
                try
                {
                    var cloud = BackupService.CopyToCloudFolder(path, settings.CloudFolderPath.Trim());
                    BackupSettingsService.UpdateLastRun(s =>
                    {
                        s.LastCloudAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        s.LastCloudPath = cloud;
                        s.LastCloudSuccess = true;
                    });
                    MessageBox.Show($"Backup criado com sucesso:\n\nLocal: {path}\nNuvem: {cloud}",
                        "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception cloudEx)
                {
                    MessageBox.Show($"Backup local criado, mas falhou envio à nuvem:\n\n{path}\n\n{cloudEx.Message}",
                        "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show($"Backup criado com sucesso:\n\n{path}", "Backup",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            RefreshStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!AppSession.IsAdmin)
        {
            MessageBox.Show("Apenas administradores podem restaurar o banco.", "Restaurar",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            "Isso substitui TODOS os dados atuais pelo backup.\n\nContinuar?",
            "Restaurar dados",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var dlg = new OpenFileDialog
        {
            Title = "Selecionar backup",
            Filter = "Backup SGDB (*.zip;*.db)|*.zip;*.db|Todos|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            BackupService.RestoreBackup(dlg.FileName);
            MessageBox.Show(
                "Restauração concluída.\n\nFeche e abra o SGDB novamente para carregar os dados.",
                "Restaurar",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Restaurar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddScheduleTime_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new BackupScheduleTimeWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.SelectedTime))
            return;

        if (!_scheduleTimes.Contains(dlg.SelectedTime, StringComparer.Ordinal))
            _scheduleTimes.Add(dlg.SelectedTime);
        RenderScheduleChips();
    }

    private void RemoveScheduleTime_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string time)
            _scheduleTimes.RemoveAll(t => string.Equals(t, time, StringComparison.Ordinal));
        RenderScheduleChips();
    }

    private void PickCloudFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Selecione a pasta sincronizada com Google Drive, OneDrive ou Dropbox",
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(CloudFolderBox.Text) && Directory.Exists(CloudFolderBox.Text))
            dlg.InitialDirectory = CloudFolderBox.Text;

        if (dlg.ShowDialog() == true)
            CloudFolderBox.Text = dlg.FolderName;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
