using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SGDB.Utils;

/// <summary>
/// UX de digitação rápida: seleciona tudo ao focar e Enter avança como Tab.
/// </summary>
public static class InputUxHelper
{
    public static void Attach(UIElement root, params TextBox[] skipEnterAsTab)
    {
        var skip = new HashSet<TextBox>(skipEnterAsTab);

        root.AddHandler(UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((_, e) =>
            {
                if (e.NewFocus is not TextBox tb || tb.IsReadOnly)
                    return;
                // Texto interno do ComboBox editável — nunca SelectAll (some a 1ª letra ao digitar a 2ª).
                if (IsComboEditableTextBox(tb))
                    return;

                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (tb.IsKeyboardFocusWithin && !tb.IsReadOnly && !IsComboEditableTextBox(tb))
                        tb.SelectAll();
                });
            }), true);

        root.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((_, e) =>
            {
                if (FindTextBox(e.OriginalSource as DependencyObject) is not TextBox tb || tb.IsReadOnly)
                    return;
                if (IsComboEditableTextBox(tb))
                    return;

                if (!tb.IsKeyboardFocusWithin)
                {
                    tb.Focus();
                    e.Handled = true;
                }
            }), true);

        root.AddHandler(UIElement.PreviewKeyDownEvent,
            new KeyEventHandler((_, e) =>
            {
                if (e.Key != Key.Enter)
                    return;
                if (Keyboard.Modifiers != ModifierKeys.None)
                    return;
                if (e.OriginalSource is not TextBox tb || tb.IsReadOnly || tb.AcceptsReturn)
                    return;
                if (skip.Contains(tb))
                    return;
                if (IsComboEditableTextBox(tb))
                    return;

                e.Handled = true;
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }), true);
    }

    private static bool IsComboEditableTextBox(TextBox tb) =>
        tb.Name == "PART_EditableTextBox" || FindParentComboBox(tb) is not null;

    private static ComboBox? FindParentComboBox(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ComboBox cb)
                return cb;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static TextBox? FindTextBox(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBox tb)
                return tb;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}
