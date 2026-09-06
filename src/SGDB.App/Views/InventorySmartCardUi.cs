using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;

namespace SGDB.Views;

/// <summary>Cartão compacto da visão simples. Sem consulta. Clique abre detalhes já carregados.</summary>
public static class InventorySmartCardUi
{
    public static Border Create(InventorySmartCard card, RoutedEventHandler? detailsClick)
    {
        var bg = card.Tone switch
        {
            "alert" => "#FEF2F2",
            "attention" => "#FFFBEB",
            "notice" => "#F8FAFC",
            "positive" => "#F0FDF4",
            _ => "#FFFFFF",
        };
        var border = card.Tone switch
        {
            "alert" => "#FECACA",
            "attention" => "#FDE68A",
            "positive" => "#BBF7D0",
            _ => "#E2E8F0",
        };

        var root = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = Brush(bg),
            BorderBrush = Brush(border),
            Tag = card.ProductId,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = card.ProductTitle,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1E293B"),
            TextWrapping = TextWrapping.Wrap,
        });
        left.Children.Add(new TextBlock
        {
            Text = card.ActionText,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#0F172A"),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(card.QuantityOrDeadlineText)
            && card.QuantityOrDeadlineText != card.ActionText)
        {
            left.Children.Add(new TextBlock
            {
                Text = card.QuantityOrDeadlineText,
                FontSize = 12,
                Foreground = Brush("#334155"),
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        left.Children.Add(new TextBlock
        {
            Text = card.ReasonText,
            FontSize = 12,
            Foreground = Brush("#475569"),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        if (!string.IsNullOrWhiteSpace(card.FixLocationText))
        {
            left.Children.Add(new TextBlock
            {
                Text = "Onde corrigir: " + card.FixLocationText,
                FontSize = 11,
                Foreground = Brush("#64748B"),
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(new TextBlock
        {
            Text = card.UrgencyText,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#64748B"),
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        var details = new Button
        {
            Content = InventorySmartPresentation.DetailsButton,
            Height = 28,
            Margin = new Thickness(12, 8, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Tag = card.ProductId,
            Cursor = Cursors.Hand,
        };
        if (detailsClick is not null)
            details.Click += detailsClick;
        right.Children.Add(details);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        root.Child = grid;
        return root;
    }

    public static StackPanel CreateViewToggle(
        InventorySmartViewMode mode,
        RoutedEventHandler simpleClick,
        RoutedEventHandler detailedClick)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
        panel.Children.Add(Toggle(InventorySmartPresentation.SimpleLabel, mode == InventorySmartViewMode.Simple, simpleClick));
        panel.Children.Add(Toggle(InventorySmartPresentation.DetailedLabel, mode == InventorySmartViewMode.Detailed, detailedClick));
        return panel;
    }

    static Button Toggle(string label, bool selected, RoutedEventHandler click)
    {
        var btn = new Button
        {
            Content = label,
            Height = 32,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            Background = Brush(selected ? "#E0F2FE" : "#F8FAFC"),
            BorderBrush = Brush(selected ? "#7DD3FC" : "#E2E8F0"),
            Cursor = Cursors.Hand,
        };
        btn.Click += click;
        return btn;
    }

    static Brush Brush(string hex) =>
        (Brush)new BrushConverter().ConvertFromString(hex)!;
}
