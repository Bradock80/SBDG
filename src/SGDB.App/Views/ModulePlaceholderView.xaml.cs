using System.Windows;
using System.Windows.Controls;

namespace SGDB.Views;

public partial class ModulePlaceholderView : UserControl
{
    public event EventHandler? CloseRequested;

    public ModulePlaceholderView()
    {
        InitializeComponent();
    }

    public string ModuleTitle
    {
        get => ModuleTitleText.Text;
        set => ModuleTitleText.Text = value;
    }

    public string ModuleBody
    {
        get => ModuleBodyText.Text;
        set => ModuleBodyText.Text = value;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
