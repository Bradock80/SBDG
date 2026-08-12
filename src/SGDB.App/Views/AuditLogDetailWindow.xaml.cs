using System.Windows;
using System.Windows.Input;
using SGDB.Models;

namespace SGDB.Views;

public partial class AuditLogDetailWindow : Window
{
    public AuditLogDetailWindow(AuditLogRow row, Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        TitleText.Text = row.ActionBadgeDisplay;
        SubtitleText.Text = $"{row.DateDisplay} · {row.UserName}";
        ContentPanel.Children.Add(AuditDetailViewBuilder.Build(row));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
