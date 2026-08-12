using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SGDB.Models;
using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Views;

public partial class BankAccountsModuleView : UserControl
{
    public event EventHandler? CloseRequested;
    private bool _suppress;

    public BankAccountsModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _suppress = true;
            StatusBox.ItemsSource = new[] { "Todas", "Pendente", "Conferido", "Divergente" };
            StatusBox.SelectedIndex = 0;
            PayBox.ItemsSource = new[] { "Todas as formas" };
            PayBox.SelectedIndex = 0;
            OperatorBox.ItemsSource = new[] { "Todas operadoras" };
            OperatorBox.SelectedIndex = 0;
            DateFromBox.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateToBox.SelectedDate = DateTime.Today;
            _suppress = false;
            ReloadAccounts();
            Focus();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ReloadAccounts()
    {
        _suppress = true;
        var list = BankService.ListAccounts(onlyActive: false).ToList();
        AccountBox.ItemsSource = list;
        AccountBox.SelectedIndex = list.Count > 0 ? 0 : -1;
        _suppress = false;
        ReloadFilterCombos();
        LoadMovements();
    }

    private int? SelectedAccountId =>
        AccountBox.SelectedItem is BankAccountRow a ? a.Id : null;

    private string SelectedStatus =>
        (StatusBox.SelectedItem as string)?.ToLowerInvariant() switch
        {
            "pendente" => "pendente",
            "conferido" => "conferido",
            "divergente" => "divergente",
            _ => "todas",
        };

    private string? SelectedPay =>
        PayBox.SelectedItem as string is { } p &&
        !p.Equals("Todas as formas", StringComparison.OrdinalIgnoreCase)
            ? p : null;

    private string? SelectedOperator =>
        OperatorBox.SelectedItem as string is { } o &&
        !o.Equals("Todas operadoras", StringComparison.OrdinalIgnoreCase)
            ? o : null;

    private void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || !IsLoaded) return;
        ReloadFilterCombos();
        LoadMovements();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || !IsLoaded) return;
        LoadMovements();
    }

    private void Buscar_Click(object sender, RoutedEventArgs e) => LoadMovements();

    private void SoPendentes_Click(object sender, RoutedEventArgs e)
    {
        _suppress = true;
        StatusBox.SelectedItem = "Pendente";
        _suppress = false;
        LoadMovements();
    }

    private void PeriodQuick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var today = DateTime.Today;
        DateTime from, to;
        switch (tag)
        {
            case "ontem":
                from = to = today.AddDays(-1);
                break;
            case "semana":
            {
                var diff = ((int)today.DayOfWeek + 6) % 7; // segunda = início
                from = today.AddDays(-diff);
                to = today;
                break;
            }
            case "mes":
                from = new DateTime(today.Year, today.Month, 1);
                to = today;
                break;
            default:
                from = to = today;
                break;
        }
        DateFromBox.SelectedDate = from;
        DateToBox.SelectedDate = to;
        LoadMovements();
    }

    private void ReloadFilterCombos()
    {
        _suppress = true;
        var paySel = PayBox.SelectedItem as string;
        var opSel = OperatorBox.SelectedItem as string;

        var pays = new List<string> { "Todas as formas" };
        pays.AddRange(PaymentMethodsService.List()
            .Where(m => m.Active && m.ApiLabel is not ("Dinheiro" or "Fiado"))
            .Select(m => m.ApiLabel));
        if (SelectedAccountId is int accId)
        {
            foreach (var p in BankService.ListPaymentTypesUsed(accId))
            {
                if (!pays.Contains(p, StringComparer.OrdinalIgnoreCase))
                    pays.Add(p);
            }
        }
        PayBox.ItemsSource = pays;
        PayBox.SelectedItem = pays.Contains(paySel ?? "") ? paySel : pays[0];

        var ops = new List<string> { "Todas operadoras" };
        ops.AddRange(BankService.CommonOperators);
        if (SelectedAccountId is int aid)
        {
            foreach (var o in BankService.ListOperatorsUsed(aid))
            {
                if (!ops.Contains(o, StringComparer.OrdinalIgnoreCase))
                    ops.Add(o);
            }
        }
        OperatorBox.ItemsSource = ops;
        OperatorBox.SelectedItem = ops.Contains(opSel ?? "") ? opSel : ops[0];
        _suppress = false;
    }

    private void LoadMovements()
    {
        if (SelectedAccountId is not int accId)
        {
            MovGrid.ItemsSource = null;
            SaldoText.Text = "";
            SetCards(0, 0, 0, 0, 0, 0);
            return;
        }

        try
        {
            var acc = BankService.GetAccount(accId);
            SaldoText.Text = string.IsNullOrWhiteSpace(acc.DefaultOperator)
                ? $"Saldo: {acc.BalanceDisplay}"
                : $"Saldo: {acc.BalanceDisplay} · {acc.DefaultOperator}";

            var result = BankService.ListMovements(
                accId,
                DateFromBox.SelectedDate,
                DateToBox.SelectedDate,
                SelectedStatus,
                SelectedPay,
                SelectedOperator);
            MovGrid.ItemsSource = result.Rows;
            SetCards(result.TotalIn, result.TotalOut, result.TotalFees, result.PeriodBalance,
                result.Pendentes, result.Conferidos);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetCards(double inn, double outt, double fees, double saldo, int pend, int conf)
    {
        CardInText.Text = $"R$ {ProductPriceHelper.FormatBr(inn)}";
        CardOutText.Text = $"R$ {ProductPriceHelper.FormatBr(outt)}";
        CardFeeText.Text = $"R$ {ProductPriceHelper.FormatBr(fees)}";
        CardSaldoText.Text = $"R$ {ProductPriceHelper.FormatBr(saldo)}";
        CardPendText.Text = pend > 0 ? $"{pend} pendente(s)" : "";
        CardConfText.Text = conf > 0 ? $"{conf} conferido(s)" : "";
    }

    private void NovaConta_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new BankAccountEditWindow(null) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            ReloadAccounts();
    }

    private void EditarConta_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccountId is not int id)
        {
            MessageBox.Show("Selecione uma conta.", "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new BankAccountEditWindow(id) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            ReloadAccounts();
    }

    private void NovoLancamento_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccountId is not int id)
        {
            MessageBox.Show("Selecione uma conta.", "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new BankMovementEditWindow(id) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            ReloadFilterCombos();
            LoadMovements();
        }
    }

    private void Conferir_Click(object sender, RoutedEventArgs e) => SetStatus("conferido");
    private void Divergente_Click(object sender, RoutedEventArgs e) => SetStatus("divergente");

    private void SetStatus(string status)
    {
        if (MovGrid.SelectedItem is not BankMovementRow row)
        {
            MessageBox.Show("Selecione um lançamento.", "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            BankService.SetReconciliation(row.Id, status,
                status == "conferido" ? DateTime.Today : null);
            LoadMovements();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ConferirTodos_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccountId is not int id)
        {
            MessageBox.Show("Selecione uma conta.", "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            "Conferir TODOS os lançamentos pendentes do período/filtros atuais?\n\nUse quando o dia bateu certinho com o banco.",
            "Conferir todos",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var n = BankService.ConferirTodos(
                id,
                DateFromBox.SelectedDate,
                DateToBox.SelectedDate,
                SelectedPay,
                SelectedOperator);
            MessageBox.Show(
                n == 0 ? "Nenhum pendente no filtro atual." : $"{n} lançamento(s) marcado(s) como conferido(s).",
                "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadMovements();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Importar_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccountId is not int id)
        {
            MessageBox.Show("Selecione uma conta.", "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (DateFromBox.SelectedDate is not DateTime from || DateToBox.SelectedDate is not DateTime to)
        {
            MessageBox.Show("Informe o período De/até para importar.", "Contas Bancárias",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            var n = BankService.ImportExpectedFromSales(id, from, to);
            MessageBox.Show(
                n == 0
                    ? "Nenhum crédito novo para importar (Pix/cartão já lançados ou período sem vendas)."
                    : $"{n} crédito(s) previsto(s) importado(s).\nConfira taxas e marque como Conferido (F7).",
                "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            ReloadFilterCombos();
            LoadMovements();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportarOfx_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccountId is not int id)
        {
            MessageBox.Show("Selecione uma conta.", "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Importar extrato OFX",
            Filter = "Extrato OFX (*.ofx;*.qfx;*.ofc)|*.ofx;*.qfx;*.ofc|Todos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var r = BankService.ImportOfx(id, dlg.FileName);
            MessageBox.Show(
                $"Extrato processado ({r.TotalInFile} no arquivo):\n" +
                $"• {r.Matched} cruzado(s) e conferido(s)\n" +
                $"• {r.Created} lançamento(s) novo(s)\n" +
                $"• {r.Skipped} ignorado(s) (já importados ou zerados)",
                "Extrato OFX", MessageBoxButton.OK, MessageBoxImage.Information);
            ReloadFilterCombos();
            LoadMovements();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Extrato OFX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Excluir_Click(object sender, RoutedEventArgs e)
    {
        if (MovGrid.SelectedItem is not BankMovementRow row)
        {
            MessageBox.Show("Selecione um lançamento.", "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show("Excluir este lançamento?", "Contas Bancárias",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            BankService.DeleteMovement(row.Id);
            LoadMovements();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Contas Bancárias", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        else if (e.Key == Key.F2) { NovoLancamento_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F5) { Importar_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F6) { ImportarOfx_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F7) { Conferir_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F8) { Divergente_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.F9) { ConferirTodos_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Delete) { Excluir_Click(sender, e); e.Handled = true; }
    }
}
