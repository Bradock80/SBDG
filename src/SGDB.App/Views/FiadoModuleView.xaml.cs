using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class FiadoModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    public FiadoModuleView(bool embeddedInReports = false)
    {
        InitializeComponent();
        if (embeddedInReports)
        {
            CloseTabButton.Visibility = Visibility.Collapsed;
            TitleBar.Visibility = Visibility.Collapsed;
            TitleText.Text = "Contas em Fiado";
        }
        Loaded += (_, _) =>
        {
            ApplyPermissionUi();
            Focus();
            LoadData();
        };
    }

    private void ApplyPermissionUi()
    {
        var canReceive = AccessControl.Can("FiadoReceber");
        var canDelete = AccessControl.Can("FiadoExcluir");
        BtnReceber.Visibility = canReceive ? Visibility.Visible : Visibility.Collapsed;
        BtnExcluirFiado.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
        BtnDescartarOrfaos.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadData()
    {
        try
        {
            UpdateCashBanner();
            var somenteSaldo = SomenteSaldo.IsChecked == true;
            var result = FiadoService.ListContas(somenteSaldo, SearchBox.Text);
            FiadoGrid.ItemsSource = result.Rows;
            MetaText.Text =
                $"{result.Registros} cliente(s) · Total em aberto (saldo): R$ {result.TotalSaldo:N2}";
            UpdateSelectedSummary();

            var hasOrphan = result.Rows.Any(r => r.Orphan) || FiadoService.HasOrphanSales();
            OrphanWrap.Visibility = hasOrphan ? Visibility.Visible : Visibility.Collapsed;
            if (hasOrphan)
            {
                OrphanPersonBox.ItemsSource = PersonService.List(null, "ativos", "clientes");
                if (OrphanPersonBox.Items.Count > 0 && OrphanPersonBox.SelectedIndex < 0)
                    OrphanPersonBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fiado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateCashBanner()
    {
        var open = CashService.IsOperational();
        if (open)
        {
            CashBanner.Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5));
            CashBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0xE7, 0xB7));
            CashBanner.BorderThickness = new Thickness(1);
            CashBannerText.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
            CashBannerText.Text = "🟢 Caixa Aberto: os recebimentos entrarão automaticamente no caixa do dia.";
        }
        else
        {
            CashBanner.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
            CashBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
            CashBanner.BorderThickness = new Thickness(1);
            CashBannerText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
            CashBannerText.Text = "🔴 Caixa fechado: abra em Caixa (F2) para registrar recebimentos.";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private FiadoContaRow? SelectedRow => FiadoGrid.SelectedItem as FiadoContaRow;

    private void FiadoGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedSummary();

    private void UpdateSelectedSummary()
    {
        if (SelectedRow is { } row)
        {
            SelectedSummaryText.Text = row.SummaryTooltip;
            SelectedSummaryCard.Visibility = Visibility.Visible;
        }
        else
        {
            SelectedSummaryText.Text = "";
            SelectedSummaryCard.Visibility = Visibility.Collapsed;
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            LoadData();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadData();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchPlaceholder.Visibility = Visibility.Visible;
        SomenteSaldo.IsChecked = true;
        LoadData();
    }

    private void FiadoGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedRow is null)
            return;
        OpenDetail(SelectedRow);
    }

    private void Detalhe_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
        {
            MessageBox.Show("Selecione um cliente na lista.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenDetail(SelectedRow);
    }

    private void Receber_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("FiadoReceber", "receber fiado", Window.GetWindow(this)))
            return;
        if (SelectedRow is null)
        {
            MessageBox.Show("Selecione um cliente na lista.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenReceber(SelectedRow);
    }

    private void OpenDetail(FiadoContaRow row)
    {
        if (row.Orphan || row.CustomerId <= 0)
        {
            MessageBox.Show("Vincule as vendas sem cliente antes de abrir o detalhe.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new FiadoDetailWindow(row.CustomerId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            LoadData();
    }

    private void OpenReceber(FiadoContaRow row)
    {
        if (row.Orphan || row.CustomerId <= 0)
        {
            MessageBox.Show("Vincule as vendas sem cliente antes de receber.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (row.Balance <= 0.005)
        {
            MessageBox.Show("Este cliente não possui saldo em aberto.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!CashService.IsOperational())
        {
            MessageBox.Show("Abra o caixa antes de registrar o recebimento.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new FiadoReceberWindow(row.CustomerId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            LoadData();
    }

    private void VincularOrfaos_Click(object sender, RoutedEventArgs e)
    {
        if (OrphanPersonBox.SelectedItem is not Person person)
        {
            MessageBox.Show("Selecione o cliente na lista ao lado.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedOrphan = SelectedRow is { Orphan: true } row ? row : null;
        var orphanPartyKey = selectedOrphan?.OrphanPartyKey;
        var qtd = selectedOrphan?.SalesCount ?? 0;
        var saldo = selectedOrphan?.Balance ?? 0;
        var quem = selectedOrphan?.CustomerName ?? "todas as vendas sem cliente";

        if (selectedOrphan is null)
        {
            var any = FiadoGrid.ItemsSource is System.Collections.IEnumerable rows
                ? rows.OfType<FiadoContaRow>().Where(r => r.Orphan).ToList()
                : [];
            qtd = any.Sum(r => r.SalesCount);
            saldo = any.Sum(r => r.Balance);
        }

        var confirm = MessageBox.Show(
            selectedOrphan is null
                ? $"Isso vai colocar TODAS as vendas sem cliente ({qtd} venda(s), cerca de R$ {saldo:N2}) " +
                  $"na conta de:\n\n{person.Name}\n\nContinuar?"
                : $"Vincular as vendas de:\n\n{quem}\n({qtd} venda(s), R$ {saldo:N2})\n\n" +
                  $"para o cadastro:\n\n{person.Name}\n\nContinuar?",
            "Vincular vendas sem cliente",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var n = FiadoService.LinkOrphanSales(person.Id, orphanPartyKey);
            MessageBox.Show(
                $"{n} venda(s) agora estão na conta de {person.Name}.",
                "Fiado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Não foi possível vincular as vendas.\n\n" + ex.Message,
                "Fiado",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DescartarOrfaos_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("FiadoExcluir", "excluir ou limpar fiado", Window.GetWindow(this)))
            return;
        var selectedOrphan = SelectedRow is { Orphan: true } row ? row : null;
        var orphanPartyKey = selectedOrphan?.OrphanPartyKey;
        var qtd = selectedOrphan?.SalesCount ?? 0;
        var saldo = selectedOrphan?.Balance ?? 0;
        var quem = selectedOrphan?.CustomerName ?? "TODAS as vendas sem cliente";

        if (selectedOrphan is null)
        {
            var any = FiadoGrid.ItemsSource is System.Collections.IEnumerable rows
                ? rows.OfType<FiadoContaRow>().Where(r => r.Orphan).ToList()
                : [];
            qtd = any.Sum(r => r.SalesCount);
            saldo = any.Sum(r => r.Balance);
        }

        var confirm = MessageBox.Show(
            "Isso NÃO lança o fiado para nenhum cliente da lista.\n\n" +
            $"Cancelar: {quem}\n({qtd} venda(s), cerca de R$ {saldo:N2})\n\n" +
            "Somem da lista de fiado. Estoque antigo NÃO volta.\n\n" +
            (selectedOrphan is null
                ? "Dica: selecione uma linha laranja na grade para cancelar só aquele nome.\n\n"
                : "") +
            "Confirmar cancelamento?",
            "Cancelar vendas sem cliente",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var n = FiadoService.DiscardOrphanSales(orphanPartyKey);
            MessageBox.Show(
                $"{n} venda(s) fiado cancelada(s).",
                "Fiado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Não foi possível cancelar.\n\n" + ex.Message,
                "Fiado",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ExcluirFiado_Click(object sender, RoutedEventArgs e)
    {
        if (!AccessControl.Ensure("FiadoExcluir", "excluir ou limpar fiado", Window.GetWindow(this)))
            return;
        if (SelectedRow is null)
        {
            MessageBox.Show("Selecione o cliente na lista.", "Fiado",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedRow.Orphan || SelectedRow.CustomerId <= 0)
        {
            DescartarOrfaos_Click(sender, e);
            return;
        }

        var row = SelectedRow;
        var confirm = MessageBox.Show(
            "Excluir TODO o fiado deste cliente?\n\n" +
            $"{row.CustomerName}\n" +
            $"{row.SalesCount} venda(s) · Saldo R$ {row.Balance:N2}\n\n" +
            "• Cancela as vendas fiado\n" +
            "• Devolve o estoque\n" +
            "• Estorna recebimentos (se houver)\n" +
            "• Some da lista de fiado\n\n" +
            "O cadastro do cliente em Clientes NÃO é apagado.\n\n" +
            "Confirmar exclusão?",
            "Excluir fiado",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var (sales, payments, total) = FiadoService.ClearCustomerFiado(row.CustomerId);
            MessageBox.Show(
                $"Fiado excluído.\n\n" +
                $"{sales} venda(s) cancelada(s)\n" +
                $"{payments} recebimento(s) estornado(s)\n" +
                $"Total limpo: R$ {total:N2}",
                "Fiado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fiado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void FiadoModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            Detalhe_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F7 && BtnReceber.Visibility == Visibility.Visible)
        {
            Receber_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            LoadData();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && BtnExcluirFiado.Visibility == Visibility.Visible)
        {
            ExcluirFiado_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && SelectedRow is not null
                 && !ReferenceEquals(Keyboard.FocusedElement, SearchBox))
        {
            OpenDetail(SelectedRow);
            e.Handled = true;
        }
    }
}
