using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class CashDepositModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    public CashDepositModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DateBox.SelectedDate ??= DateBrHelper.TodayBrDate();
            ValorBox.LostFocus += (_, _) =>
                ValorBox.Text = ProductPriceHelper.FormatBr(ProductPriceHelper.ParseBr(ValorBox.Text));
            Focus();
            Reload();
            ValorBox.Focus();
            ValorBox.SelectAll();
        };
    }

    private CashDepositRow? Selected => DepositsGrid.SelectedItem as CashDepositRow;

    private void Reload()
    {
        var onlyPending = FilterPendentes.IsChecked == true;
        var onlyDepositados = FilterDepositados.IsChecked == true;
        IReadOnlyList<CashDepositRow> rows;
        if (onlyPending)
            rows = CashService.ListDepositAwaits(onlyPending: true);
        else
        {
            rows = CashService.ListDepositAwaits(onlyPending: false);
            if (onlyDepositados)
                rows = rows.Where(r => r.Status is "depositado" or "divergente").ToList();
        }

        DepositsGrid.ItemsSource = rows;
        var aguardandoTotal = rows.Where(r => r.Status == "pendente").Sum(r => r.Amount);
        MetaText.Text = rows.Count == 0
            ? "Nenhum lançamento neste filtro. Informe o dia e o valor acima e clique em Lançar."
            : $"{rows.Count} lançamento(s) · Aguardando depósito: R$ {aguardandoTotal:N2}";
    }

    private void Add_Click(object sender, RoutedEventArgs e) => TryAdd();

    private void TryAdd()
    {
        var date = DateBox.SelectedDate?.Date ?? DateBrHelper.TodayBrDate();
        var amount = ProductPriceHelper.ParseBr(ValorBox.Text);
        try
        {
            CashService.AddDepositAwait(date, amount);
            ValorBox.Text = ProductPriceHelper.FormatBr(0);
            FilterPendentes.IsChecked = true;
            Reload();
            ValorBox.Focus();
            ValorBox.SelectAll();
        }
        catch (CashOperationException ex)
        {
            MessageBox.Show(ex.Message, "Conferência de Depósitos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null)
        {
            MessageBox.Show("Selecione um lançamento aguardando.", "Conferência de Depósitos",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Selected.Status != "pendente")
        {
            MessageBox.Show("Só é possível excluir lançamentos aguardando.", "Conferência de Depósitos",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ask = MessageBox.Show(
            $"Excluir aguardando de {Selected.DepositDateBr} — {Selected.AmountDisplay}?",
            "Conferência de Depósitos",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
            return;

        try
        {
            CashService.DeleteDepositAwait(Selected.Id);
            Reload();
        }
        catch (CashOperationException ex)
        {
            MessageBox.Show(ex.Message, "Conferência de Depósitos",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            Reload();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Confirm_Click(object sender, RoutedEventArgs e) => OpenConfirm();

    private void DepositsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenConfirm();

    private void OpenConfirm()
    {
        if (Selected is null)
        {
            MessageBox.Show("Selecione o lançamento na lista.", "Conferência de Depósitos",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var owner = Window.GetWindow(this);
        var dlg = new CashDepositConfirmWindow(Selected) { Owner = owner };
        if (dlg.ShowDialog() == true)
            Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.F1 or Key.F2 or Key.F5)
        {
            if (e.Key == Key.F1)
                Reload();
            else if (e.Key == Key.F2)
                TryAdd();
            else
                OpenConfirm();
            e.Handled = true;
        }
    }
}
