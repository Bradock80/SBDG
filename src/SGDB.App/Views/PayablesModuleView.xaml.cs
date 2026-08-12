using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class PayablesModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private readonly int? _highlightPurchaseId;
    private string _situacao = "pendentes";
    private string _view = "parcelas";
    private bool _suppressFilter;

    public PayablesModuleView(int? highlightPurchaseId = null)
    {
        _highlightPurchaseId = highlightPurchaseId;
        InitializeComponent();
        Loaded += OnLoaded;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && IsLoaded)
                Dispatcher.BeginInvoke(LoadData, DispatcherPriority.Background);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSuppliers();
        _suppressFilter = true;
        try
        {
            DateFromBox.Clear();
            DateToBox.Clear();
            SupplierBox.SelectedIndex = 0;

            if (_highlightPurchaseId is int pid)
            {
                // Garante título/parcelas gravados antes de listar
                PayableService.EnsurePayablesForClosedPurchase(pid);

                BannerBorder.Visibility = Visibility.Visible;
                BannerText.Text =
                    $"Compra #{pid} finalizada. Confira abaixo os títulos/parcelas gerados. Duplo clique na linha para baixar.";
                SitTodas.IsChecked = true;
                _situacao = "todas";
            }
            else
            {
                BannerBorder.Visibility = Visibility.Collapsed;
                SitPendentes.IsChecked = true;
                _situacao = "pendentes";
            }
        }
        finally
        {
            _suppressFilter = false;
        }

        UpdateViewVisibility();
        // Depois do layout/filtros — evita lista vazia na primeira abertura
        Dispatcher.BeginInvoke(() =>
        {
            LoadData();
            Focus();
        }, DispatcherPriority.Loaded);
    }

    private void LoadSuppliers()
    {
        var list = new List<Person>
        {
            new() { Id = 0, Name = "Todos" },
        };
        list.AddRange(PersonService.List(null, "ativos", "fornecedores"));
        SupplierBox.ItemsSource = list;
        SupplierBox.SelectedIndex = 0;
    }

    private int? SelectedSupplierId =>
        SupplierBox.SelectedItem is Person p && p.Id > 0 ? p.Id : null;

    private int? ActivePurchaseId => _highlightPurchaseId;

    private void LoadData()
    {
        try
        {
            if (_view == "titulos")
            {
                var list = PayableService.ListTitles(_situacao, SelectedSupplierId,
                    DateFromBox.Text, DateToBox.Text, ActivePurchaseId);
                TitlesGrid.ItemsSource = list;
                TotalBarText.Text = list.Sum(t => t.TotalAmount).ToString("N2");
                PaidBarText.Text = list.Sum(t => t.PaidAmount).ToString("N2");
                CountBarText.Text = list.Count.ToString();
                if (_highlightPurchaseId is not null && list.Count > 0)
                    TitlesGrid.SelectedIndex = 0;
            }
            else
            {
                var list = PayableService.ListInstallments(_situacao, SelectedSupplierId,
                    DateFromBox.Text, DateToBox.Text, ActivePurchaseId);
                InstallmentsGrid.ItemsSource = list;
                TotalBarText.Text = list.Sum(i => i.Amount).ToString("N2");
                PaidBarText.Text = list.Sum(i => i.PaidAmount).ToString("N2");
                CountBarText.Text = list.Count.ToString();
                if (_highlightPurchaseId is not null && list.Count > 0)
                    InstallmentsGrid.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateViewVisibility()
    {
        var titles = _view == "titulos";
        TitlesGrid.Visibility = titles ? Visibility.Visible : Visibility.Collapsed;
        InstallmentsGrid.Visibility = titles ? Visibility.Collapsed : Visibility.Visible;
        PeriodLabel.Text = titles ? "Período | Emissão" : "Período | Vencimento";
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _suppressFilter)
            return;
        _situacao = SitPagas.IsChecked == true ? "pagas"
            : SitTodas.IsChecked == true ? "todas"
            : "pendentes";
        LoadData();
    }

    private void View_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        _view = ViewTitulos.IsChecked == true ? "titulos" : "parcelas";
        UpdateViewVisibility();
        LoadData();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadData();

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        SupplierBox.SelectedIndex = 0;
        DateFromBox.Clear();
        DateToBox.Clear();
        SitPendentes.IsChecked = true;
        _situacao = "pendentes";
        LoadData();
    }

    private void Novo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PayableNovoWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            LoadData();
    }

    private void Baixar_Click(object sender, RoutedEventArgs e) => OpenBaixa();

    private void Alterar_Click(object sender, RoutedEventArgs e) => OpenAlterar();

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenBaixa();

    private void OpenBaixa()
    {
        var installmentId = ResolveInstallmentIdForBaixa();
        if (installmentId is null)
            return;

        var dlg = new PayableBaixaWindow(installmentId.Value) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            LoadData();
    }

    private void OpenAlterar()
    {
        var installmentId = ResolveInstallmentIdForAlterar();
        if (installmentId is null)
            return;

        var dlg = new PayableAlterarWindow(installmentId.Value) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            LoadData();
    }

    private int? ResolveInstallmentIdForBaixa()
    {
        if (_view == "parcelas")
        {
            if (InstallmentsGrid.SelectedItem is not PayableInstallmentRow row)
            {
                MessageBox.Show("Selecione um título ou parcela na tabela (clique na linha).",
                    "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return row.Id;
        }

        if (TitlesGrid.SelectedItem is not PayableTitleRow title)
        {
            MessageBox.Show("Selecione um título ou parcela na tabela (clique na linha).",
                "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var insts = PayableService.ListInstallmentsOfTitle(title.Id);
        if (insts.Count == 0)
        {
            MessageBox.Show("Nenhuma parcela neste título.", "Contas a Pagar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var pending = insts.FirstOrDefault(i => i.Status != "pago");
        return (pending ?? insts[0]).Id;
    }

    private int? ResolveInstallmentIdForAlterar()
    {
        if (_view == "parcelas")
        {
            if (InstallmentsGrid.SelectedItem is not PayableInstallmentRow row)
            {
                MessageBox.Show("Selecione um título ou parcela na tabela (clique na linha).",
                    "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            if (row.Status == "pago")
            {
                MessageBox.Show("Parcela já paga. Estorne a baixa (F7) antes de alterar.",
                    "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return row.Id;
        }

        if (TitlesGrid.SelectedItem is not PayableTitleRow title)
        {
            MessageBox.Show("Selecione um título ou parcela na tabela (clique na linha).",
                "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var pending = PayableService.ListInstallmentsOfTitle(title.Id)
            .Where(i => i.Status != "pago")
            .ToList();
        if (pending.Count == 0)
        {
            MessageBox.Show("Nenhuma parcela pendente para alterar. Estorne a baixa se a parcela já foi paga.",
                "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        return pending[0].Id;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_view == "parcelas")
            {
                if (InstallmentsGrid.SelectedItem is not PayableInstallmentRow row)
                {
                    MessageBox.Show("Selecione um título ou parcela na tabela (clique na linha).",
                        "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var msg = row.PurchaseId is int pid
                    ? $"Excluir parcela {row.DisplayNumber}?\n\nVinculado à compra #{pid}. O título some de Contas a Pagar; para apagar a nota e estornar estoque, use Compras."
                    : $"Excluir parcela {row.DisplayNumber}?";
                if (MessageBox.Show(msg, "Excluir", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                PayableService.DeleteInstallment(row.Id);
            }
            else
            {
                if (TitlesGrid.SelectedItem is not PayableTitleRow title)
                {
                    MessageBox.Show("Selecione um título ou parcela na tabela (clique na linha).",
                        "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var msg = title.PurchaseId is int pid
                    ? $"Excluir título {title.Number}?\n\nVinculado à compra #{pid}. O título some de Contas a Pagar; para apagar a nota e estornar estoque, use Compras."
                    : $"Excluir título {title.Number}?";
                if (MessageBox.Show(msg, "Excluir", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                PayableService.DeleteTitle(title.Id);
            }

            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas a Pagar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var grid = _view == "titulos" ? TitlesGrid : InstallmentsGrid;
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true)
                return;
            dlg.PrintVisual(grid, "Contas a Pagar");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Imprimir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void PayablesModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F2:
                Novo_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F3:
                Alterar_Click(sender, e);
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
            case Key.F7:
                Baixar_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F8:
            case Key.Delete:
                Delete_Click(sender, e);
                e.Handled = true;
                break;
            case Key.F12:
                Refresh_Click(sender, e);
                e.Handled = true;
                break;
        }
    }
}
