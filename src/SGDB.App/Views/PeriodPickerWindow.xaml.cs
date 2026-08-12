using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SGDB.Views;

public partial class PeriodPickerWindow : Window
{
    public DateTime DateFrom { get; private set; }
    public DateTime DateTo { get; private set; }
    public bool Applied { get; private set; }

    public PeriodPickerWindow(DateTime dateFrom, DateTime dateTo)
    {
        InitializeComponent();
        DateFrom = dateFrom.Date;
        DateTo = dateTo.Date;
        DateFromBox.SetDate(DateFrom);
        DateToBox.SetDate(DateTo);
    }

    private void Quick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;

        var today = DateTime.Today;
        DateTime from, to;
        switch (tag)
        {
            case "ontem":
                from = to = today.AddDays(-1);
                break;
            case "hoje":
                from = to = today;
                break;
            case "mes_ant":
            {
                var firstThis = new DateTime(today.Year, today.Month, 1);
                to = firstThis.AddDays(-1);
                from = new DateTime(to.Year, to.Month, 1);
                break;
            }
            case "mes":
                from = new DateTime(today.Year, today.Month, 1);
                to = today;
                break;
            case "7":
                from = today.AddDays(-6);
                to = today;
                break;
            case "90":
                from = today.AddDays(-89);
                to = today;
                break;
            default: // 30
                from = today.AddDays(-29);
                to = today;
                break;
        }

        ApplyAndClose(from, to);
    }

    private void Aplicar_Click(object sender, RoutedEventArgs e)
    {
        if (!DateFromBox.TryGetDate(out var from) || !DateToBox.TryGetDate(out var to))
        {
            MessageBox.Show("Selecione as datas De e Até.", "Selecionar período",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (from > to)
            (from, to) = (to, from);

        ApplyAndClose(from, to);
    }

    private void ApplyAndClose(DateTime from, DateTime to)
    {
        DateFrom = from.Date;
        DateTo = to.Date;
        Applied = true;
        DialogResult = true;
        Close();
    }

    private void Voltar_Click(object sender, RoutedEventArgs e)
    {
        Applied = false;
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Voltar_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Aplicar_Click(sender, e);
            e.Handled = true;
        }
    }
}
