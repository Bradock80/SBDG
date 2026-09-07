using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SGDB.Views;

/// <summary>
/// Roteamento puro da roda em telas com ScrollViewer aninhado.
/// Delta WPF: positivo = para cima. Sem query nem ranking.
/// </summary>
public static class InventoryNestedWheelScroll
{
    public static InventoryComboWheelRoute Route(
        double innerOffset,
        double innerScrollable,
        double outerOffset,
        double outerScrollable,
        int delta)
    {
        if (TryApplyVertical(innerOffset, innerScrollable, delta, out var innerNext))
            return new InventoryComboWheelRoute(true, true, false, innerNext, outerOffset);
        if (TryApplyVertical(outerOffset, outerScrollable, delta, out var outerNext))
            return new InventoryComboWheelRoute(true, false, true, innerOffset, outerNext);
        return new InventoryComboWheelRoute(false, false, false, innerOffset, outerOffset);
    }

    public static bool TryApplyVertical(
        double verticalOffset,
        double scrollableHeight,
        int delta,
        out double nextOffset)
    {
        nextOffset = verticalOffset;
        if (delta == 0 || scrollableHeight <= 0 || !IsFinite(verticalOffset) || !IsFinite(scrollableHeight))
            return false;

        var candidate = verticalOffset - delta;
        if (candidate < 0)
            candidate = 0;
        else if (candidate > scrollableHeight)
            candidate = scrollableHeight;

        if (candidate == verticalOffset)
            return false;

        nextOffset = candidate;
        return true;
    }

    public static bool ShouldIgnore(MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers == ModifierKeys.Shift)
            return true;
        return IsUnderComboBox(e.OriginalSource);
    }

    public static bool TryHandle(MouseWheelEventArgs e, ScrollViewer? inner, ScrollViewer? outer)
    {
        if (ShouldIgnore(e))
            return false;

        var innerOff = inner?.VerticalOffset ?? 0;
        var innerMax = inner?.ScrollableHeight ?? 0;
        var outerOff = outer?.VerticalOffset ?? 0;
        var outerMax = outer?.ScrollableHeight ?? 0;
        var route = Route(innerOff, innerMax, outerOff, outerMax, e.Delta);
        if (!route.Handled)
            return false;

        if (route.MoveInner && inner is not null)
            inner.ScrollToVerticalOffset(route.InnerOffset);
        else if (route.MoveOuter && outer is not null)
            outer.ScrollToVerticalOffset(route.OuterOffset);

        e.Handled = true;
        return true;
    }

    public static bool TryHandleDataGrid(MouseWheelEventArgs e, DataGrid? grid, ScrollViewer? outer)
    {
        if (grid is null)
            return false;
        return TryHandle(e, FindScrollViewer(grid), outer);
    }

    public static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindScrollViewer(child);
            if (found is not null)
                return found;
        }

        return null;
    }

    public static bool IsUnderComboBox(object? source)
    {
        if (source is not DependencyObject current)
            return false;

        while (current is not null)
        {
            if (current is ComboBox or ComboBoxItem)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    public static bool IsInside(object? source, params DependencyObject?[] hosts)
    {
        if (source is not DependencyObject current)
            return false;
        while (current is not null)
        {
            foreach (var host in hosts)
            {
                if (host is not null && ReferenceEquals(current, host))
                    return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    public static bool TryHandleOutside(
        MouseWheelEventArgs e,
        ScrollViewer? outer,
        params DependencyObject?[] inners)
    {
        if (IsInside(e.OriginalSource, inners))
            return false;
        return TryHandle(e, outer, null);
    }

    static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
