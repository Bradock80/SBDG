using System.Windows;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Views;

/// <summary>Escolha Avulso/Maço no PDV — sem banco, sem carrinho.</summary>
public partial class PdvCigaretteModeWindow : Window
{
    public string? SelectedMode { get; private set; }

    public PdvCigaretteModeWindow(string productName, double precoAvulso, double precoMaco)
    {
        InitializeComponent();
        ProductNameText.Text = productName;
        AvulsoPriceText.Text = ProductPriceHelper.MoneyBr(precoAvulso);
        MacoPriceText.Text = ProductPriceHelper.MoneyBr(precoMaco);
        Loaded += (_, _) => Focus();
    }

    private void Avulso_Click(object sender, RoutedEventArgs e) => Accept(PdvCigaretteSaleMode.Avulso);

    private void Maco_Click(object sender, RoutedEventArgs e) => Accept(PdvCigaretteSaleMode.Maco);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = null;
        DialogResult = false;
        Close();
    }

    private void Accept(string mode)
    {
        SelectedMode = mode;
        DialogResult = true;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A)
        {
            Accept(PdvCigaretteSaleMode.Avulso);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.M)
        {
            Accept(PdvCigaretteSaleMode.Maco);
            e.Handled = true;
        }
    }
}
