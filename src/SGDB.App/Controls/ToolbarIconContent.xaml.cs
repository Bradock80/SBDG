using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SGDB.Controls;

public partial class ToolbarIconContent : UserControl
{
    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(nameof(IconKey), typeof(string), typeof(ToolbarIconContent),
            new PropertyMetadata("", OnVisualChanged));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ToolbarIconContent),
            new PropertyMetadata("", OnVisualChanged));

    public string IconKey
    {
        get => (string)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public ToolbarIconContent()
    {
        InitializeComponent();
        ApplyVisual();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToolbarIconContent control)
            control.ApplyVisual();
    }

    private void ApplyVisual()
    {
        LabelText.Text = Label;

        if (string.IsNullOrWhiteSpace(IconKey))
        {
            IconImage.Source = null;
            return;
        }

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Toolbar", $"{IconKey}.png");
        if (!File.Exists(path))
        {
            IconImage.Source = null;
            return;
        }

        IconImage.Source = new BitmapImage(new Uri(path, UriKind.Absolute));
    }
}
