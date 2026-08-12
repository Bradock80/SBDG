using System.Windows;
using System.Windows.Input;
using SGDB.Utils;

namespace SGDB.Views;

public partial class CashSuprimentoWindow : Window
{
    public double Valor { get; private set; }
    public string? Observacao { get; private set; }

    public CashSuprimentoWindow()
    {
        InitializeComponent();
        InputUxHelper.Attach(this);
        Loaded += (_, _) => ValorBox.Focus();
        ValorBox.LostFocus += (_, _) => ValorBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(ValorBox.Text));
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySave())
            return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool TrySave()
    {
        Valor = ProductPriceHelper.ParseBr(ValorBox.Text);
        Observacao = ObsBox.Text.Trim();
        if (Valor <= 0)
        {
            MessageBox.Show("Informe um valor maior que zero.", "Suprimento", MessageBoxButton.OK, MessageBoxImage.Warning);
            ValorBox.Focus();
            return false;
        }
        return true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (TrySave())
            {
                DialogResult = true;
                Close();
            }
            e.Handled = true;
        }
    }
}
