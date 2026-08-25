using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SGDB.Views;

public partial class HomeSplashView : UserControl
{
    public event EventHandler? ValidityAlertClicked;

    public HomeSplashView()
    {
        InitializeComponent();
    }

    public string DepositoName
    {
        get => DepositoText.Text;
        set => DepositoText.Text = value;
    }

    public void SetValidityAlert(string? text, bool highlightExpired)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ValidityAlertBar.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }

        ValidityAlertText.Text = text + "  —  clique para abrir o Controle de Validades";
        ValidityAlertBar.Background = highlightExpired
            ? (Brush)new BrushConverter().ConvertFromString("#FEE2E2")!
            : (Brush)new BrushConverter().ConvertFromString("#FEF3C7")!;
        ValidityAlertBar.BorderBrush = highlightExpired
            ? (Brush)new BrushConverter().ConvertFromString("#F87171")!
            : (Brush)new BrushConverter().ConvertFromString("#F59E0B")!;
        ValidityAlertText.Foreground = highlightExpired
            ? (Brush)new BrushConverter().ConvertFromString("#991B1B")!
            : (Brush)new BrushConverter().ConvertFromString("#92400E")!;
        ValidityAlertBar.Visibility = System.Windows.Visibility.Visible;
    }

    private void ValidityAlertBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        ValidityAlertClicked?.Invoke(this, EventArgs.Empty);
}
