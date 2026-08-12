using System.Windows;
using SGDB.Services;

namespace SGDB.Views;

public partial class BackupScheduleTimeWindow : Window
{
    public string? SelectedTime { get; private set; }

    public BackupScheduleTimeWindow()
    {
        InitializeComponent();
        TimeBox.Text = "12:00";
        TimeBox.SelectAll();
        TimeBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!BackupSettingsService.TryNormalizeTime(TimeBox.Text, out var norm))
        {
            MessageBox.Show("Informe um horário válido (ex: 12:00 ou 22:30).", "Horário",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedTime = norm;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
