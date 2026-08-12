using System.Windows;
using System.Windows.Input;
using SGDB.Models;

namespace SGDB.Views;

public partial class ReceiptPreviewExpandWindow : Window
{
    public event EventHandler? PrintTestRequested;

    public ReceiptPreviewExpandWindow(ReceiptPreviewData data, Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        RefreshPreview(data);
    }

    public void RefreshPreview(ReceiptPreviewData data)
    {
        PreviewControl.Tag = data;
        PreviewControl.Compact = false;
        PreviewControl.Apply(data);
    }

    private void PrintTest_Click(object sender, RoutedEventArgs e) =>
        PrintTestRequested?.Invoke(this, EventArgs.Empty);

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
