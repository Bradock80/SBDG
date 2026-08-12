using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class PurchasesModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler<int>? OpenPayablesRequested;

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private string _statusFilter = "todas";

    public PurchasesModuleView()
    {
        InitializeComponent();
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            LoadPurchases();
        };
        Loaded += (_, _) =>
        {
            Focus();
            LoadPurchases();
        };
    }

    private void LoadPurchases()
    {
        var list = PurchaseService.List(SearchBox.Text, _statusFilter, DateFromBox.Text, DateToBox.Text);
        PurchasesGrid.ItemsSource = list;
        var total = PurchaseService.SumTotal(SearchBox.Text, _statusFilter, DateFromBox.Text, DateToBox.Text);
        TotalBarText.Text = total.ToString("N2");
    }

    private Purchase? SelectedPurchase => PurchasesGrid.SelectedItem as Purchase;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void Status_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        _statusFilter = StatusAberta.IsChecked == true ? "aberta"
            : StatusFechada.IsChecked == true ? "fechada"
            : StatusCancelada.IsChecked == true ? "cancelada"
            : "todas";
        LoadPurchases();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadPurchases();

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        DateFromBox.Clear();
        DateToBox.Clear();
        StatusTodas.IsChecked = true;
        LoadPurchases();
    }

    private void NewPurchase_Click(object sender, RoutedEventArgs e) => OpenForm(null, readOnly: false);

    private void EditPurchase_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPurchase is null)
        {
            MessageBox.Show("Selecione uma compra na lista.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedPurchase.Status == "fechada")
        {
            ReopenPurchase_Click(sender, e);
            return;
        }

        if (SelectedPurchase.Status != "aberta")
        {
            MessageBox.Show("Somente compras abertas podem ser alteradas. Use Visualizar (Alt+F7).",
                "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenForm(SelectedPurchase.Id, readOnly: false);
    }

    private void ReopenPurchase_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPurchase is null)
        {
            MessageBox.Show("Selecione uma compra fechada para reabrir.", "Compras",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedPurchase.Status != "fechada")
        {
            MessageBox.Show(
                SelectedPurchase.Status == "aberta"
                    ? "Esta compra já está aberta. Use Alterar (F3)."
                    : "Somente compras fechadas podem ser reabertas.",
                "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Reabrir a compra NF {SelectedPurchase.Number}?\n\n" +
            "• Estorna o estoque desta nota\n" +
            "• Desfaz a média de custo desta compra\n" +
            "• Remove títulos a pagar (se não houver parcela paga)\n" +
            "• Volta para status Aberta para você corrigir e finalizar de novo",
            "Reabrir compra",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var id = SelectedPurchase.Id;
            PurchaseService.Reopen(id);
            LoadPurchases();
            OpenForm(id, readOnly: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ViewPurchase_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPurchase is null)
        {
            MessageBox.Show("Selecione uma compra para visualizar.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenForm(SelectedPurchase.Id, readOnly: true);
    }

    private void PurchasesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedPurchase is null)
            return;

        if (SelectedPurchase.Status == "aberta")
            EditPurchase_Click(sender, e);
        else
            ViewPurchase_Click(sender, e);
    }

    private void DeletePurchase_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPurchase is null)
        {
            MessageBox.Show("Selecione uma compra para excluir.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedPurchase.Status == "fechada")
        {
            var cancel = MessageBox.Show(
                $"A compra NF {SelectedPurchase.Number} está fechada.\n\nDeseja cancelar (estorna estoque)?",
                "Cancelar compra",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (cancel != MessageBoxResult.Yes)
                return;

            try
            {
                PurchaseService.Cancel(SelectedPurchase.Id);
                LoadPurchases();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        if (SelectedPurchase.Status == "cancelada")
        {
            MessageBox.Show("Compra já está cancelada.", "Compras", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Excluir compra NF {SelectedPurchase.Number}?",
            "Excluir compra",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            PurchaseService.Delete(SelectedPurchase.Id);
            LoadPurchases();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compras", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Impressão da compra será implementada em breve.", "Compras",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenForm(int? purchaseId, bool readOnly)
    {
        try
        {
            var form = new PurchaseFormWindow(purchaseId, readOnly) { Owner = Window.GetWindow(this) };
            if (form.ShowDialog() == true)
            {
                LoadPurchases();
                if (form.OpenPayablesForPurchaseId is int pagarId)
                    OpenPayablesRequested?.Invoke(this, pagarId);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível abrir o cadastro de compra:\n\n{ex.Message}",
                "Compras",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void PurchasesModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7 && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            ViewPurchase_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F7)
        {
            ReopenPurchase_Click(sender, e);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.F2:
                NewPurchase_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F3:
                EditPurchase_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F4:
                Print_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F5:
                Refresh_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F6:
                SearchBox.Focus();
                e.Handled = true;
                break;
            case Key.F8:
                DeletePurchase_Click(sender, e);
                e.Handled = true;
                break;
            case Key.Enter when PurchasesGrid.SelectedItem is not null:
                if (SelectedPurchase?.Status == "aberta")
                    EditPurchase_Click(sender, e);
                else
                    ViewPurchase_Click(sender, e);
                e.Handled = true;
                break;
        }
    }
}
