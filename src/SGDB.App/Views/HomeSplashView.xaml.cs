using System.Windows.Controls;

namespace SGDB.Views;

public partial class HomeSplashView : UserControl
{
    public HomeSplashView()
    {
        InitializeComponent();
    }

    public string DepositoName
    {
        get => DepositoText.Text;
        set => DepositoText.Text = value;
    }
}
