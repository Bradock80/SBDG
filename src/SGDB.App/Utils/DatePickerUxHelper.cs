using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace SGDB.Utils;

/// <summary>
/// Remove o placeholder "Selecione uma data" / "Select a date" de todos os DatePicker.
/// </summary>
public static class DatePickerUxHelper
{
    private static bool _registered;

    public static void RegisterClearWatermark()
    {
        if (_registered)
            return;
        _registered = true;

        EventManager.RegisterClassHandler(
            typeof(DatePicker),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDatePickerLoaded));
    }

    private static void OnDatePickerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker picker)
            return;

        ClearWatermark(picker);
        // Template às vezes só fica pronto no próximo layout.
        picker.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => ClearWatermark(picker)));
    }

    public static void ClearWatermark(DatePicker picker)
    {
        picker.ApplyTemplate();
        var textBox = picker.Template?.FindName("PART_TextBox", picker) as DatePickerTextBox
                      ?? FindVisualChild<DatePickerTextBox>(picker);
        if (textBox is null)
            return;

        // .NET / WPF: o texto "Selecione uma data" fica no PART_Watermark do DatePickerTextBox.
        textBox.ApplyTemplate();
        if (textBox.Template?.FindName("PART_Watermark", textBox) is ContentControl watermark)
            watermark.Content = null;
        else if (FindVisualChildNamed(textBox, "PART_Watermark") is ContentControl found)
            found.Content = null;
    }

    private static DependencyObject? FindVisualChildNamed(DependencyObject parent, string name)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name)
                return child;
            var nested = FindVisualChildNamed(child, name);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;
            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
