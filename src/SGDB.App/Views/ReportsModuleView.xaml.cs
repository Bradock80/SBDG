using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SGDB.Application.Sales;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class ReportsModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private string _view = "menu";
    private FiadoModuleView? _fiadoView;
    private DreReportModuleView? _dreView;
    private StockIoReportModuleView? _estoqueIoView;

    public ReportsModuleView(string? initialView = null)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            ShowView(string.IsNullOrWhiteSpace(initialView) ? "menu" : initialView!);
        };
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        var view = btn.Name switch
        {
            nameof(BtnMaisVendidos) => "mais_vendidos",
            nameof(BtnVendasPdv) => "vendas_pdv",
            nameof(BtnFiado) => "fiado_contas",
            nameof(BtnEstoqueMinimo) => "estoque_minimo",
            nameof(BtnCurvaAbc) => "curva_abc",
            nameof(BtnPrevisaoFiado) => "previsao_fiado",
            nameof(BtnFechamento) => "fechamento",
            nameof(BtnDre) => "dre",
            nameof(BtnEstoqueIo) => "estoque_io",
            nameof(BtnCaixaHistorico) => "caixa_historico",
            _ => "menu",
        };
        ShowView(view);
    }

    private void ClearNavTags()
    {
        BtnMaisVendidos.Tag = "";
        BtnVendasPdv.Tag = "";
        BtnFiado.Tag = "";
        BtnEstoqueMinimo.Tag = "";
        BtnCurvaAbc.Tag = "";
        BtnPrevisaoFiado.Tag = "";
        BtnFechamento.Tag = "";
        BtnDre.Tag = "";
        BtnEstoqueIo.Tag = "";
        BtnCaixaHistorico.Tag = "";
    }

    private void HideAllPanels()
    {
        PanelMenu.Visibility = Visibility.Collapsed;
        PanelMaisVendidos.Visibility = Visibility.Collapsed;
        PanelVendasPdv.Visibility = Visibility.Collapsed;
        PanelFiado.Visibility = Visibility.Collapsed;
        PanelEstoqueMinimo.Visibility = Visibility.Collapsed;
        PanelCurvaAbc.Visibility = Visibility.Collapsed;
        PanelPrevisao.Visibility = Visibility.Collapsed;
        PanelFechamento.Visibility = Visibility.Collapsed;
        PanelCaixaLista.Visibility = Visibility.Collapsed;
        PanelCaixaDetalhe.Visibility = Visibility.Collapsed;
        DreHost.Visibility = Visibility.Collapsed;
        EstoqueIoHost.Visibility = Visibility.Collapsed;
    }

    private void ShowView(string view)
    {
        _view = view switch
        {
            "itens_vendidos" => "vendas_pdv",
            _ => view,
        };

        HideAllPanels();
        ClearNavTags();

        switch (_view)
        {
            case "mais_vendidos":
                TitleText.Text = "Relatório — Mais Vendidos";
                BtnMaisVendidos.Tag = "active";
                PanelMaisVendidos.Visibility = Visibility.Visible;
                EnsureMvDates();
                LoadMaisVendidos();
                break;
            case "vendas_pdv":
                TitleText.Text = "Relatório — Histórico de vendas";
                BtnVendasPdv.Tag = "active";
                PanelVendasPdv.Visibility = Visibility.Visible;
                EnsureVpDates();
                LoadVendasPdv();
                break;
            case "fiado_contas":
                TitleText.Text = "Relatório — Contas em Fiado";
                BtnFiado.Tag = "active";
                PanelFiado.Visibility = Visibility.Visible;
                EnsureFiado();
                break;
            case "estoque_minimo":
                TitleText.Text = "Relatório — Alerta de Reposição";
                BtnEstoqueMinimo.Tag = "active";
                PanelEstoqueMinimo.Visibility = Visibility.Visible;
                LoadEstoqueMinimo();
                break;
            case "curva_abc":
                TitleText.Text = "Relatório — Giro de Estoque (ABC)";
                BtnCurvaAbc.Tag = "active";
                PanelCurvaAbc.Visibility = Visibility.Visible;
                EnsureAbcDates();
                LoadCurvaAbc();
                break;
            case "previsao_fiado":
                TitleText.Text = "Relatório — Previsão de Recebimento";
                BtnPrevisaoFiado.Tag = "active";
                PanelPrevisao.Visibility = Visibility.Visible;
                LoadPrevisao();
                break;
            case "fechamento":
                TitleText.Text = "Relatório — Fechamento Consolidado";
                BtnFechamento.Tag = "active";
                PanelFechamento.Visibility = Visibility.Visible;
                EnsureFcDates();
                LoadFechamento();
                break;
            case "dre":
                TitleText.Text = "Relatório — DRE Simplificado";
                BtnDre.Tag = "active";
                EnsureDre();
                DreHost.Visibility = Visibility.Visible;
                break;
            case "estoque_io":
                TitleText.Text = "Relatório — Entradas e Saídas do Estoque";
                BtnEstoqueIo.Tag = "active";
                EnsureEstoqueIo();
                EstoqueIoHost.Visibility = Visibility.Visible;
                break;
            case "caixa_historico":
                TitleText.Text = "Relatório — Histórico do Caixa";
                BtnCaixaHistorico.Tag = "active";
                PanelCaixaLista.Visibility = Visibility.Visible;
                EnsureCxDates();
                LoadCaixaLista();
                break;
            default:
                _view = "menu";
                TitleText.Text = "Relatório";
                PanelMenu.Visibility = Visibility.Visible;
                break;
        }
    }

    private void EnsureFiado()
    {
        if (_fiadoView is not null)
            return;
        _fiadoView = new FiadoModuleView(embeddedInReports: true);
        FiadoHost.Content = _fiadoView;
    }

    private void EnsureDre()
    {
        if (_dreView is not null)
            return;
        _dreView = new DreReportModuleView();
        _dreView.CloseRequested += (_, _) => ShowView("menu");
        DreHost.Content = _dreView;
    }

    private void EnsureEstoqueIo()
    {
        if (_estoqueIoView is not null)
            return;
        _estoqueIoView = new StockIoReportModuleView();
        _estoqueIoView.CloseRequested += (_, _) => ShowView("menu");
        EstoqueIoHost.Content = _estoqueIoView;
    }

    // ——— Estoque mínimo ———

    private void EmAtualizar_Click(object sender, RoutedEventArgs e) => LoadEstoqueMinimo();

    private void LoadEstoqueMinimo()
    {
        try
        {
            var result = ReportsService.ListEstoqueMinimo();
            EmGrid.ItemsSource = result.Rows;
            EmMeta.Text = result.Registros == 0
                ? "Nenhum produto abaixo do estoque mínimo."
                : $"{result.Registros} produto(s) precisam de reposição · Sugestão total: {result.Rows.Sum(r => r.SugestaoCompra):N0} un.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Estoque Mínimo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ——— Curva ABC ———

    private void EnsureAbcDates()
    {
        if (AbcDateFrom.SelectedDate is null)
            AbcDateFrom.SetDate(DateTime.Today.AddDays(-30));
        if (AbcDateTo.SelectedDate is null)
            AbcDateTo.SetDate(DateTime.Today);
    }

    private void Abc30_Click(object sender, RoutedEventArgs e)
    {
        AbcDateFrom.SetDate(DateTime.Today.AddDays(-30));
        AbcDateTo.SetDate(DateTime.Today);
        LoadCurvaAbc();
    }

    private void AbcPesquisar_Click(object sender, RoutedEventArgs e) => LoadCurvaAbc();

    private void LoadCurvaAbc()
    {
        if (!AbcDateFrom.TryGetDate(out var from) || !AbcDateTo.TryGetDate(out var to))
        {
            MessageBox.Show("Selecione as datas De e Até.", "Curva ABC",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var result = ReportsService.ListCurvaAbc(from, to);
            AbcGrid.ItemsSource = result.Rows;
            AbcCountAText.Text = result.CountA.ToString();
            AbcCountBText.Text = result.CountB.ToString();
            AbcCountCText.Text = result.CountC.ToString();
            AbcMeta.Text = $"{result.Registros} produto(s) · Fat. R$ {result.TotalValor:N2}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Curva ABC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ——— Previsão fiados ———

    private void PrevHorizonte_Changed(object sender, RoutedEventArgs e)
    {
        if (PrevGrid is null)
            return;
        if (IsLoaded)
            LoadPrevisao();
    }

    private void PrevAtualizar_Click(object sender, RoutedEventArgs e) => LoadPrevisao();

    private int GetPrevHorizonDays() =>
        Prev7.IsChecked == true ? 7 : Prev15.IsChecked == true ? 15 : 30;

    private void LoadPrevisao()
    {
        try
        {
            var result = ReportsService.ListPrevisaoRecebimento(GetPrevHorizonDays());
            PrevGrid.ItemsSource = result.Rows;
            PrevMeta.Text =
                $"{result.Registros} cliente(s) · Projetado (até {result.HorizontDays}d): R$ {result.TotalProjetado:N2} · " +
                $"Já vencido: R$ {result.TotalVencido:N2}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Previsão de Recebimento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ——— Fechamento ———

    private void EnsureFcDates()
    {
        if (FcDateFrom.SelectedDate is null)
            FcDateFrom.SetDate(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        if (FcDateTo.SelectedDate is null)
            FcDateTo.SetDate(DateTime.Today);
    }

    private void FcMes_Click(object sender, RoutedEventArgs e)
    {
        FcDateFrom.SetDate(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        FcDateTo.SetDate(DateTime.Today);
        LoadFechamento();
    }

    private void FcPesquisar_Click(object sender, RoutedEventArgs e) => LoadFechamento();

    private void LoadFechamento()
    {
        if (!FcDateFrom.TryGetDate(out var from) || !FcDateTo.TryGetDate(out var to))
        {
            MessageBox.Show("Selecione as datas De e Até.", "Fechamento",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var r = ReportsService.GetFechamentoConsolidado(from, to);
            FcPeriodoText.Text = $"Período {r.DateFrom:dd/MM/yyyy} a {r.DateTo:dd/MM/yyyy}";
            FcFaturado.Text = $"R$ {r.TotalFaturado:N2}";
            FcAVista.Text = $"R$ {r.TotalAVista:N2}";
            FcFiado.Text = $"R$ {r.TotalFiado:N2}";
            FcRecebidoFiado.Text = $"R$ {r.TotalRecebidoFiado:N2}";
            FcCmv.Text = $"R$ {r.Cmv:N2}";
            FcLucro.Text = $"R$ {r.LucroEstimado:N2}  ({r.MargemPercent:N1}%)";
            FcMeta.Text = $"{r.QtdVendas} venda(s) no período · Lucro = faturamento − CMV.";
            if (r.HasEstimatedLegacyCost && !string.IsNullOrWhiteSpace(r.CmvReliabilityNote))
                FcMeta.Text += " " + r.CmvReliabilityNote;
            FcFormaGrid.ItemsSource = r.PorForma
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new { kv.Key, kv.Value })
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fechamento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ——— Mais Vendidos ———

    private void EnsureMvDates()
    {
        if (MvDateFrom.SelectedDate is null)
            MvDateFrom.SetDate(DateTime.Today);
        if (MvDateTo.SelectedDate is null)
            MvDateTo.SetDate(DateTime.Today);
    }

    private void MvHoje_Click(object sender, RoutedEventArgs e)
    {
        MvDateFrom.SetDate(DateTime.Today);
        MvDateTo.SetDate(DateTime.Today);
        LoadMaisVendidos();
    }

    private void MvPesquisar_Click(object sender, RoutedEventArgs e) => LoadMaisVendidos();

    private void LoadMaisVendidos()
    {
        if (!MvDateFrom.TryGetDate(out var from) || !MvDateTo.TryGetDate(out var to))
        {
            MessageBox.Show("Selecione as datas De e Até.", "Mais Vendidos",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var result = ReportsService.ListMaisVendidos(from, to);
            MvGrid.ItemsSource = result.Rows;
            MvMeta.Text =
                $"{result.Registros} produto(s) · Qtd total {result.TotalQty:N3} · R$ {result.TotalValor:N2}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Mais Vendidos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ——— Vendas PDV ———

    private void EnsureVpDates()
    {
        if (VpDateFrom.SelectedDate is null)
            VpDateFrom.SetDate(DateTime.Today.AddDays(-1));
        if (VpDateTo.SelectedDate is null)
            VpDateTo.SetDate(DateTime.Today);
    }

    private void VpHoje_Click(object sender, RoutedEventArgs e)
    {
        VpDateFrom.SetDate(DateTime.Today);
        VpDateTo.SetDate(DateTime.Today);
        LoadVendasPdv();
    }

    private void VpOntem_Click(object sender, RoutedEventArgs e)
    {
        var t = DateTime.Today.AddDays(-1);
        VpDateFrom.SetDate(t);
        VpDateTo.SetDate(t);
        LoadVendasPdv();
    }

    private void VpPesquisar_Click(object sender, RoutedEventArgs e) => LoadVendasPdv();

    private void LoadVendasPdv()
    {
        if (!VpDateFrom.TryGetDate(out var from) || !VpDateTo.TryGetDate(out var to))
        {
            MessageBox.Show("Selecione as datas De e Até.", "Vendas PDV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var open = CashService.IsOperational();
            VpCashHint.Text = open
                ? "Caixa aberto. Del / duplo clique cancela venda de hoje (caixa aberto)."
                : "Caixa fechado. Consulta funciona; cancelar venda só com caixa aberto (hoje).";

            var rows = ReportsService.ListVendasPdv(from, to, includeCancelled: true);
            VpGrid.ItemsSource = rows;
            var ativas = rows.Where(r => !r.Cancelled).ToList();
            VpMeta.Text =
                $"{rows.Count} venda(s) · Ativas: R$ {ativas.Sum(r => r.Total):N2} · Período {from:dd/MM/yyyy} a {to:dd/MM/yyyy}";
            ClearVpDetail();
            if (rows.Count > 0)
                VpGrid.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vendas PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void VpGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Opacity = e.Row.Item is PdvSaleListRow { Cancelled: true } ? 0.55 : 1;
    }

    private void VpGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VpGrid.SelectedItem is not PdvSaleListRow row)
        {
            ClearVpDetail();
            return;
        }

        try
        {
            var detail = PdvQueryService.GetSaleDetail(row.Id);
            VpDetailTitle.Text = detail.Cancelled
                ? $"Venda #{detail.Id} — CANCELADA"
                : $"Venda #{detail.Id}";
            VpDetailInfo.Text =
                $"{detail.CreatedAtBr} · {(string.IsNullOrWhiteSpace(detail.CustomerName) ? "—" : detail.CustomerName)} · {detail.PaymentLabel} · R$ {detail.Total:N2}";
            VpItemsGrid.ItemsSource = detail.Items;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vendas PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
            ClearVpDetail();
        }
    }

    private void ClearVpDetail()
    {
        VpDetailTitle.Text = "Selecione uma venda";
        VpDetailInfo.Text = "";
        VpItemsGrid.ItemsSource = null;
    }

    private void VpGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => TryCancelSelectedSale();

    private void TryCancelSelectedSale()
    {
        if (VpGrid.SelectedItem is not PdvSaleListRow row)
            return;
        if (row.Cancelled)
            return;
        if (!CashService.IsOperational())
        {
            MessageBox.Show("Caixa fechado — abra o caixa para cancelar vendas.", "Vendas PDV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!DateTime.TryParse(row.SessionDate, out var saleDay) || saleDay.Date != DateTime.Today)
        {
            MessageBox.Show("Só é possível cancelar vendas do dia de hoje.", "Vendas PDV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                $"Cancelar venda #{row.Id}?\nEstoque e caixa serão estornados.",
                "Vendas PDV",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            ApplicationServices.CancelSale.Execute(new CancelSaleCommand
            {
                SaleId = row.Id,
            });
            PixSaleReverseService.ShowOperatorAlert(Window.GetWindow(this));
            LoadVendasPdv();
        }
        catch (Exception ex) when (ex is PdvException or CashOperationException)
        {
            MessageBox.Show(ex.Message, "Vendas PDV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ——— Caixa histórico ———

    private void EnsureCxDates()
    {
        if (CxDateFrom.SelectedDate is null)
            CxDateFrom.SetDate(DateTime.Today.AddDays(-30));
        if (CxDateTo.SelectedDate is null)
            CxDateTo.SetDate(DateTime.Today);
    }

    private void CxFiltro_Changed(object sender, RoutedEventArgs e)
    {
        if (CxPeriodoFields is null)
            return;
        CxPeriodoFields.IsEnabled = CxPeriodo.IsChecked == true;
    }

    private void CxAtualizar_Click(object sender, RoutedEventArgs e) => LoadCaixaLista();

    private void LoadCaixaLista()
    {
        var modo = CxPeriodo.IsChecked == true ? "periodo" : "recentes";
        if (!int.TryParse(CxLimit.Text.Trim(), out var limit))
            limit = 30;

        DateTime? from = null, to = null;
        if (modo == "periodo")
        {
            if (!CxDateFrom.TryGetDate(out var f) || !CxDateTo.TryGetDate(out var t))
            {
                MessageBox.Show("Selecione as datas De e Até.", "Histórico do Caixa",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            from = f;
            to = t;
        }

        try
        {
            var result = CashService.ListCaixaHistorico(limit, from, to, modo);
            CxGrid.ItemsSource = result.Rows;
            CxMeta.Text = $"{result.Registros} turno(s) · modo {result.Modo} · limite {result.Limit}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Histórico do Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CxGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CxGrid.SelectedItem is not CaixaHistoricoRow row)
            return;
        OpenCaixaDetail(row.Id);
    }

    private void CxDetalhes_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int id })
            OpenCaixaDetail(id);
        else if (sender is Button { Tag: not null } btn && int.TryParse(btn.Tag.ToString(), out var parsed))
            OpenCaixaDetail(parsed);
    }

    private void CxImprimir_Click(object sender, RoutedEventArgs e)
    {
        int? id = null;
        if (sender is Button { Tag: int i })
            id = i;
        else if (sender is Button { Tag: not null } btn && int.TryParse(btn.Tag.ToString(), out var parsed))
            id = parsed;

        if (id is not int aberturaId)
            return;

        try
        {
            var detail = CashService.GetCaixaHistoricoDetail(aberturaId);
            if (detail is null)
            {
                MessageBox.Show("Turno não encontrado.", "Imprimir resumo",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PrintCaixaResumo(detail);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Imprimir resumo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void PrintCaixaResumo(CaixaHistoricoDetail detail)
    {
        var formas = detail.EntradasPorForma.Count == 0
            ? "—"
            : string.Join("  ·  ", detail.EntradasPorForma.Select(kv => $"{kv.Key}: R$ {kv.Value:N2}"));

        var diffText = detail.DifferenceAmount is double d
            ? $"R$ {d:N2}"
            : "—";
        var informado = detail.SaldoInformado is double s
            ? $"R$ {s:N2}"
            : "—";
        var operador = string.IsNullOrWhiteSpace(detail.OperatorName) ? "—" : detail.OperatorName;

        var paper = new Border
        {
            Background = System.Windows.Media.Brushes.White,
            Padding = new Thickness(28),
            Width = 520,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Resumo de Caixa",
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                    new TextBlock
                    {
                        Text = $"Turno #{detail.Id} — {detail.StatusLabel}",
                        FontSize = 13,
                        Foreground = System.Windows.Media.Brushes.DimGray,
                        Margin = new Thickness(0, 0, 0, 16),
                    },
                    MakePrintLine("Operador", operador),
                    MakePrintLine("Aberto em", $"{detail.OpenedAtBr} {detail.OpenedTimeBr}"),
                    MakePrintLine("Fechado em", string.IsNullOrEmpty(detail.ClosedAtBr) ? "—" : detail.ClosedAtBr),
                    MakePrintLine("Saldo inicial", $"R$ {detail.SaldoInicial:N2}"),
                    MakePrintLine("Entradas", $"R$ {detail.EntradasCaixa:N2}"),
                    MakePrintLine("Saídas", $"R$ {detail.SaidasCaixa:N2}"),
                    MakePrintLine("Saldo final previsto", $"R$ {detail.SaldoFinalGaveta:N2}"),
                    MakePrintLine("Saldo final informado", informado),
                    MakePrintLine("Diferença / Quebra", diffText),
                    MakePrintLine("Por forma", formas),
                    MakePrintLine("Observação",
                        string.IsNullOrWhiteSpace(detail.OpeningObs) ? "—" : detail.OpeningObs),
                },
            },
        };

        paper.Measure(new Size(520, double.PositiveInfinity));
        paper.Arrange(new Rect(0, 0, 520, paper.DesiredSize.Height));
        paper.UpdateLayout();

        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true)
            return;
        dlg.PrintVisual(paper, $"Resumo caixa #{detail.Id}");
    }

    private static TextBlock MakePrintLine(string label, string value) =>
        new()
        {
            Text = $"{label}: {value}",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };

    private void OpenCaixaDetail(int aberturaId)
    {
        try
        {
            var detail = CashService.GetCaixaHistoricoDetail(aberturaId);
            if (detail is null)
            {
                MessageBox.Show("Turno não encontrado.", "Histórico do Caixa",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            PanelCaixaLista.Visibility = Visibility.Collapsed;
            PanelCaixaDetalhe.Visibility = Visibility.Visible;
            TitleText.Text = "Relatório — Histórico do Caixa (detalhe)";

            CxDetailTitle.Text = $"Turno #{detail.Id} — {detail.StatusLabel}";
            var formas = detail.EntradasPorForma.Count == 0
                ? "—"
                : string.Join(" · ", detail.EntradasPorForma.Select(kv => $"{kv.Key}: R$ {kv.Value:N2}"));
            var operador = string.IsNullOrWhiteSpace(detail.OperatorName) ? "—" : detail.OperatorName;
            var diff = detail.DifferenceAmount is double d ? $"R$ {d:N2}" : "—";
            var informado = detail.SaldoInformado is double s ? $"R$ {s:N2}" : "—";
            CxDetailResumo.Text =
                $"Operador: {operador}" +
                $"\nAberto: {detail.OpenedAtBr} {detail.OpenedTimeBr}" +
                (string.IsNullOrEmpty(detail.ClosedAtBr) ? "" : $" · Fechado: {detail.ClosedAtBr}") +
                $"\nSaldo inicial: R$ {detail.SaldoInicial:N2} · Entradas: R$ {detail.EntradasCaixa:N2} · Saídas: R$ {detail.SaidasCaixa:N2}" +
                $"\nSaldo final: R$ {detail.SaldoFinal:N2} · Gaveta: R$ {detail.SaldoFinalGaveta:N2}" +
                $" · Informado: {informado} · Diferença: {diff}" +
                $"\nPor forma: {formas}" +
                (string.IsNullOrWhiteSpace(detail.OpeningObs) ? "" : $"\nObs.: {detail.OpeningObs}");

            CxDetailGrid.ItemsSource = detail.Rows;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Histórico do Caixa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CxVoltar_Click(object sender, RoutedEventArgs e)
    {
        PanelCaixaDetalhe.Visibility = Visibility.Collapsed;
        PanelCaixaLista.Visibility = Visibility.Visible;
        TitleText.Text = "Relatório — Histórico do Caixa";
    }

    // ——— Keys / helpers ———

    private void ReportsModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (PanelCaixaDetalhe.Visibility == Visibility.Visible)
            {
                CxVoltar_Click(sender, e);
                e.Handled = true;
                return;
            }
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (_view == "vendas_pdv" && (e.Key == Key.Delete || e.Key == Key.F4))
        {
            TryCancelSelectedSale();
            e.Handled = true;
        }

        if (e.Key == Key.F5)
        {
            switch (_view)
            {
                case "mais_vendidos": LoadMaisVendidos(); break;
                case "vendas_pdv": LoadVendasPdv(); break;
                case "estoque_minimo": LoadEstoqueMinimo(); break;
                case "curva_abc": LoadCurvaAbc(); break;
                case "previsao_fiado": LoadPrevisao(); break;
                case "fechamento": LoadFechamento(); break;
                case "caixa_historico":
                    if (PanelCaixaLista.Visibility == Visibility.Visible)
                        LoadCaixaLista();
                    break;
            }
            e.Handled = true;
        }
    }
}
