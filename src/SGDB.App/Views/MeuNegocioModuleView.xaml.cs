using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Views;

public partial class MeuNegocioModuleView : UserControl
{
    public event EventHandler? CloseRequested;

    private string _tab = "visao";
    private bool _loading;
    private DateTime _dateFrom = DateTime.Today.AddDays(-29);
    private DateTime _dateTo = DateTime.Today;

    public MeuNegocioModuleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Focus();
            Reload();
        };
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// DataGrid e ScrollViewer internos engolem a roda do mouse; no topo o scroll pra cima
    /// não sobe pro painel. Forçamos o scroll vertical do conteúdo da página.
    /// </summary>
    private void ContentScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Shift)
            return;

        ContentScroll.ScrollToVerticalOffset(ContentScroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;

        _tab = btn.Name switch
        {
            nameof(TabMargem) => "margem",
            nameof(TabRecebimentos) => "recebimentos",
            nameof(TabMensal) => "mensal",
            nameof(TabReceitasDespesas) => "receitas-despesas",
            nameof(TabTaxas) => "taxas",
            _ => "visao",
        };

        UpdateTabUi();
    }

    private void UpdateTabUi()
    {
        TabVisao.Tag = _tab == "visao" ? "active" : "";
        TabMargem.Tag = _tab == "margem" ? "active" : "";
        TabRecebimentos.Tag = _tab == "recebimentos" ? "active" : "";
        TabMensal.Tag = _tab == "mensal" ? "active" : "";
        TabReceitasDespesas.Tag = _tab == "receitas-despesas" ? "active" : "";
        TabTaxas.Tag = _tab == "taxas" ? "active" : "";

        PanelVisao.Visibility = _tab == "visao" ? Visibility.Visible : Visibility.Collapsed;
        PanelMargem.Visibility = _tab == "margem" ? Visibility.Visible : Visibility.Collapsed;
        PanelRecebimentos.Visibility = _tab == "recebimentos" ? Visibility.Visible : Visibility.Collapsed;
        PanelMensal.Visibility = _tab == "mensal" ? Visibility.Visible : Visibility.Collapsed;
        PanelReceitasDespesas.Visibility = _tab == "receitas-despesas" ? Visibility.Visible : Visibility.Collapsed;
        PanelTaxas.Visibility = _tab == "taxas" ? Visibility.Visible : Visibility.Collapsed;

        var isRd = _tab == "receitas-despesas";
        FilterVendas.Visibility = isRd ? Visibility.Collapsed : Visibility.Visible;
        FilterRd.Visibility = isRd ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Atualizar_Click(object sender, RoutedEventArgs e) => Reload();

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsLoaded)
            Reload();
    }

    private void RdMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsLoaded)
            Reload();
    }

    private void SelecionarPeriodo_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dlg = new PeriodPickerWindow(_dateFrom, _dateTo) { Owner = owner };
        if (dlg.ShowDialog() == true && dlg.Applied)
        {
            _dateFrom = dlg.DateFrom;
            _dateTo = dlg.DateTo;
            Reload();
        }
    }

    private void Reload()
    {
        var mode = ModeCreated.IsChecked == true ? "created" : "session";
        var rdMode = RdModeEmission.IsChecked == true ? "emission" : "due";

        try
        {
            _loading = true;
            var data = BusinessDashboardService.GetDashboard(_dateFrom, _dateTo, mode, rdMode);
            Apply(data);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Meu Negócio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Apply(NegocioDashboard d)
    {
        _dateFrom = d.DateFrom;
        _dateTo = d.DateTo;
        PeriodText.Text = $"{d.DateFrom:dd/MM/yyyy} até {d.DateTo:dd/MM/yyyy}";

        KpiFaturamento.Text = $"R$ {d.Faturamento:N2}";
        KpiPedidos.Text = d.QtdPedidos.ToString("N0");
        KpiTicket.Text = $"R$ {d.TicketMedio:N2}";
        KpiDescontos.Text = $"R$ {d.Recebimentos.TotalDescontos:N2}";
        KpiServicos.Text = $"R$ {d.Recebimentos.TotalServicos:N2}";
        KpiCancelados.Text = d.QtdCancelados.ToString("N0");
        KpiCmv.Text = $"R$ {d.Cmv:N2}";
        if (d.CmvUsesHistoricalSnapshot && d.HasEstimatedLegacyCost)
        {
            KpiCmvNote.Text = d.CmvReliabilityNote ?? HistoricalSaleCostRules.EstimatedLegacyPeriodNote;
            KpiCmvNote.Visibility = Visibility.Visible;
        }
        else
        {
            KpiCmvNote.Text = "";
            KpiCmvNote.Visibility = Visibility.Collapsed;
        }
        KpiItens.Text = d.ItensVendidos % 1 == 0
            ? d.ItensVendidos.ToString("N0")
            : d.ItensVendidos.ToString("N2");
        KpiMediaItens.Text = d.MediaItensPedido.ToString("N2");
        KpiClientes.Text = d.ClientesAtendidos.ToString("N0");
        KpiDespesas.Text = $"R$ {d.Despesas:N2}";
        KpiSaldo.Text = $"R$ {d.SaldoPeriodo:N2}";
        KpiTaxas.Text = $"R$ {d.TaxasLucro.TotalTaxas:N2}";

        var lucroBrutoReal = d.Faturamento - d.Cmv - d.Despesas - d.TaxasLucro.TotalTaxas;
        KpiLucroBruto.Text = $"R$ {lucroBrutoReal:N2}";
        KpiLucroBruto.Foreground = lucroBrutoReal < -0.009
            ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
            : new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));

        var chartVm = d.DailyChart.Select(p => new DailyBarVm
        {
            Label = p.Label,
            Total = p.Total,
            CaixaBarHeight = Math.Max(0, 140 * p.HeightRatio * p.CaixaRatio),
            FiadoBarHeight = Math.Max(0, 140 * p.HeightRatio * p.FiadoRatio),
            TooltipCaixa = $"Caixa (PDV): R$ {p.Caixa:N2}",
            TooltipFiado = $"Fiado: R$ {p.Fiado:N2}",
            TooltipTotal = $"Total: R$ {p.Total:N2}",
        }).ToList();

        var hasSales = chartVm.Any(x => x.Total > 0.009);
        ChartEmpty.Visibility = hasSales ? Visibility.Collapsed : Visibility.Visible;
        DailyChartItems.ItemsSource = hasSales ? chartVm : null;

        var topVm = d.TopVendidos.Select(t => new TopRowVm
        {
            Posicao = t.Posicao,
            Name = string.IsNullOrWhiteSpace(t.Name) ? t.Code : t.Name,
            QtyDisplay = t.Qty % 1 == 0 ? t.Qty.ToString("N0") : t.Qty.ToString("N2"),
            BarWidth = Math.Max(2, 180 * t.BarRatio),
        }).ToList();
        var hasTop = topVm.Count > 0;
        TopEmpty.Visibility = hasTop ? Visibility.Collapsed : Visibility.Visible;
        TopList.ItemsSource = hasTop ? topVm : null;

        ApplyVendasInsight(d.VendasInsight);

        MnFat.Text = $"R$ {d.MensalFaturamento:N2}";
        MnCusto.Text = $"R$ {d.MensalCusto:N2}";
        MnLucro.Text = $"R$ {d.MensalLucro:N2}";
        MnLucro.Foreground = d.MensalLucro < -0.009
            ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
            : new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
        var mensalBars = ToTripleBars(d.MensalRows);
        var hasMensal = mensalBars.Any(x => x.HasData);
        MensalChartEmpty.Visibility = hasMensal ? Visibility.Collapsed : Visibility.Visible;
        MensalChart.ItemsSource = hasMensal ? mensalBars : null;
        MensalGrid.ItemsSource = d.MensalRows;

        MgStatus.Text = d.StatusLabel;
        MgMediaCat.Text = $"{d.MediaCatalogo:N1}%";
        MgMediaVend.Text = $"{d.MargemVendasPeriodo:N1}%";
        MgMediaHist.Text = $"{d.MargemVendasHistorico:N1}%";
        MgPeriodoHint.Text = d.QtdVendasPeriodo > 0
            ? $"{d.QtdVendasPeriodo} venda(s) no período selecionado"
            : "Sem vendas no período — veja o histórico ao lado";
        MgHistoricoHint.Text = d.QtdVendasHistorico > 0
            ? $"{d.QtdVendasHistorico} venda(s) de {d.HistoricoFromBr} até {d.HistoricoToBr}"
            : "Nenhuma venda registrada ainda";
        MgCriticos.Text = d.FaixaCritico.ToString("N0");
        MgTotalProdutos.Text = $"de {d.TotalComPreco} com preço de venda";

        MargemStatusCard.Background = d.StatusKey switch
        {
            "critico" => new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
            "atencao" => new SolidColorBrush(Color.FromRgb(0xFF, 0xED, 0xD5)),
            _ => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
        };
        MargemStatusCard.BorderBrush = d.StatusKey switch
        {
            "critico" => new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA)),
            "atencao" => new SolidColorBrush(Color.FromRgb(0xFE, 0xD7, 0xAA)),
            _ => new SolidColorBrush(Color.FromRgb(0xBB, 0xF7, 0xD0)),
        };

        var gaugeMax = Math.Max(40.0, Math.Ceiling((Math.Max(d.MediaCatalogo, 22) + 8) / 10.0) * 10.0);
        if (gaugeMax > 100) gaugeMax = 100;
        GaugeZoneCritico.Width = new GridLength(15, GridUnitType.Star);
        GaugeZoneAtencao.Width = new GridLength(3, GridUnitType.Star);
        GaugeZoneSaudavel.Width = new GridLength(4, GridUnitType.Star);
        GaugeZoneExcelente.Width = new GridLength(Math.Max(0.5, gaugeMax - 22), GridUnitType.Star);
        GaugeLabel0.Text = "0%";
        GaugeLabel1.Text = $"{gaugeMax * 0.2:0}%";
        GaugeLabel2.Text = $"{gaugeMax * 0.4:0}%";
        GaugeLabel3.Text = $"{gaugeMax * 0.6:0}%";
        GaugeLabel4.Text = $"{gaugeMax * 0.8:0}%";
        GaugeLabelMax.Text = $"{gaugeMax:0}%";

        MargemGaugeCanvas.SizeChanged -= MargemGaugeCanvas_SizeChanged;
        MargemGaugeCanvas.SizeChanged += MargemGaugeCanvas_SizeChanged;
        _gaugeMedia = d.MediaCatalogo;
        _gaugeMax = gaugeMax;
        UpdateMargemGaugeMarker();
        MargemGaugeMarker.ToolTip = $"Média catálogo: {d.MediaCatalogo:N1}% (eixo até {gaugeMax:0}%)";

        MargemBenchmarkList.ItemsSource = d.MargemBenchmarks.Select(b => new MargemBarVm
        {
            Label = b.Label,
            ValueDisplay = b.ValueDisplay,
            BarWidth = Math.Max(2, 220 * b.BarRatio),
            ColorBrush = BrushFromHex(b.Color),
        }).ToList();

        BindPieSection(
            d.MargemFaixasPie,
            MargemPie,
            MargemPieLegend,
            MargemPieEmpty,
            MargemPieBody,
            asMoney: false);

        var gruposVm = d.MargemGrupos.Select(g => new MargemBarVm
        {
            Label = g.Label,
            LabelCurto = g.Label.Length > 8 ? g.Label[..8] + "…" : g.Label,
            ValueDisplay = g.MarginDisplay,
            BarHeight = Math.Max(2, 160 * Math.Clamp(g.MarginPercent / 50.0, 0, 1)),
            BarWidth = 0,
            ColorBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Tooltip = $"{g.Label}: {g.MarginPercent:N1}% ({g.Qty} produto(s))",
        }).ToList();
        var hasGrupos = gruposVm.Count > 0;
        MargemGruposEmpty.Visibility = hasGrupos ? Visibility.Collapsed : Visibility.Visible;
        MargemGruposList.ItemsSource = hasGrupos ? gruposVm : null;
        MargemGruposLabels.ItemsSource = hasGrupos ? gruposVm : null;

        var hasCriticos = d.Abaixo15.Count > 0;
        MargemCriticosEmpty.Visibility = hasCriticos ? Visibility.Collapsed : Visibility.Visible;
        MargemGrid.Visibility = hasCriticos ? Visibility.Visible : Visibility.Collapsed;
        MargemGrid.ItemsSource = hasCriticos ? d.Abaixo15 : null;

        ApplyRecebimentos(d.Recebimentos);
        ApplyReceitasDespesas(d.ReceitasDespesas);
        ApplyTaxas(d.TaxasLucro);
    }

    private double _gaugeMedia;
    private double _gaugeMax = 50;

    private void MargemGaugeCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateMargemGaugeMarker();

    private void UpdateMargemGaugeMarker()
    {
        if (MargemGaugeCanvas.ActualWidth <= 1)
            return;
        var pct = Math.Clamp(_gaugeMedia, 0, _gaugeMax) / _gaugeMax;
        var x = pct * MargemGaugeCanvas.ActualWidth - MargemGaugeMarker.Width / 2;
        Canvas.SetLeft(MargemGaugeMarker, Math.Max(0, x));
    }

    private void ApplyVendasInsight(NegocioVendasInsight i)
    {
        InsightPanel.Visibility = i.TemDados ? Visibility.Visible : Visibility.Collapsed;
        if (!i.TemDados)
            return;

        InsightMelhorDow.Text = i.MelhorDiaSemana;
        InsightMelhorDowValor.Text = $"Soma no período: R$ {i.MelhorDiaSemanaTotal:N2}";
        InsightPeriodoMes.Text = i.PeriodoMesMelhor;
        InsightPeriodoDetalhe.Text =
            $"Início R$ {i.InicioMesTotal:N2} · Meio R$ {i.MeioMesTotal:N2} · Final R$ {i.FimMesTotal:N2}";
        InsightMelhorData.Text = i.MelhorDiaData;
        InsightMelhorDataValor.Text = $"R$ {i.MelhorDiaDataTotal:N2}";
        InsightRanking.Text = string.IsNullOrWhiteSpace(i.RankingDiasResumo)
            ? ""
            : $"Ranking no período: {i.RankingDiasResumo}";

        // Ordem fixa Dom→Sáb para o mini gráfico
        var order = new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
        var byName = i.DiasSemana.ToDictionary(x => x.Dia, x => x);
        var shortNames = new[] { "Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb" };
        InsightDowList.ItemsSource = order.Select((name, idx) =>
        {
            byName.TryGetValue(name, out var row);
            var total = row?.Total ?? 0;
            var ratio = row?.BarRatio ?? 0;
            var melhor = row?.IsMelhor == true;
            return new DowBarVm
            {
                DiaCurto = shortNames[idx],
                BarHeight = Math.Max(2, 56 * ratio),
                BarBrush = melhor
                    ? new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8))
                    : new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)),
                LabelBrush = melhor
                    ? new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F))
                    : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Tooltip = $"{name}: R$ {total:N2}",
            };
        }).ToList();
    }

    private void ApplyRecebimentos(NegocioRecebimentosData r)
    {
        RecFat.Text = $"R$ {r.Faturamento:N2}";
        RecServicos.Text = r.TotalServicos.ToString("N2");
        RecDescontos.Text = r.TotalDescontos.ToString("N2");

        var vsBars = ToCompareBars(r.VsPagar, "Receb.", "Pagar");
        var hasVs = vsBars.Any(x => x.BarA > 0.5 || x.BarB > 0.5);
        RecVsPagarEmpty.Visibility = hasVs ? Visibility.Collapsed : Visibility.Visible;
        RecVsPagarChart.ItemsSource = hasVs ? vsBars : null;

        BindPieSection(r.BandeiraSlices, RecBandeiraPie, RecBandeiraList, RecBandeiraEmpty, RecBandeiraBody);
        BindPieSection(r.FormaSlices, RecFormaPie, RecFormaList, RecFormaEmpty, RecFormaBody);
    }

    private static void BindPieSection(
        IReadOnlyList<NegocioSliceRow> slices,
        ItemsControl pie,
        ItemsControl legend,
        TextBlock empty,
        FrameworkElement body,
        bool asMoney = true)
    {
        var pieVm = ToPieSlices(slices, asMoney);
        var has = pieVm.Count > 0;
        empty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        body.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        pie.ItemsSource = has ? pieVm : null;
        legend.ItemsSource = has ? ToSliceVm(slices, asMoney) : null;
    }

    private void ApplyReceitasDespesas(NegocioReceitasDespesasData rd)
    {
        RdReceitasTotal.Text = $"R$ {rd.ReceitasTotal:N2}";
        RdReceitasLine.Text = $"(+) Receitas  R$ {rd.Receitas:N2}";
        RdTransferLine.Text = $"(+) Transferência de Crédito  R$ {rd.TransferenciaCredito:N2}";
        RdDespesasTotal.Text = $"R$ {rd.Despesas:N2}";
        RdDespesasPrevisto.Text = $"R$ {rd.DespesasPrevisto:N2} (Previsto)";
        RdSaldo.Text = $"R$ {rd.Saldo:N2}";
        RdSaldo.Foreground = rd.Saldo < -0.009
            ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F));

        var mensalBars = ToCompareBars(rd.MensalReceitasDespesas, "Receitas", "Despesas");
        var hasMensal = mensalBars.Any(x => x.BarA > 0.5 || x.BarB > 0.5);
        RdMensalEmpty.Visibility = hasMensal ? Visibility.Collapsed : Visibility.Visible;
        RdMensalChart.ItemsSource = hasMensal ? mensalBars : null;

        var prevBars = ToCompareBars(rd.MensalPrevistoRealizado, "Previstas", "Realizadas");
        var hasPrev = prevBars.Any(x => x.BarA > 0.5 || x.BarB > 0.5);
        RdPrevRealEmpty.Visibility = hasPrev ? Visibility.Collapsed : Visibility.Visible;
        RdPrevRealChart.ItemsSource = hasPrev ? prevBars : null;

        BindPieSection(rd.DespesasCategoria, RdCategoriaPie, RdCategoriaList, RdCategoriaEmpty, RdCategoriaBody);
    }

    private void ApplyTaxas(NegocioTaxasLucroData t)
    {
        TxTotalTaxas.Text = $"R$ {t.TotalTaxas:N2}";
        TxTotalComTaxa.Text = $"R$ {t.TotalComTaxa:N2}";
        TxLiquido.Text = $"R$ {t.LiquidoAposTaxas:N2}";
        TxPctRecebido.Text = $"{t.PctSobreRecebido:N1}% do recebido (R$ {t.TotalRecebido:N2})";
        TxResumoChart.ItemsSource = ToCompareBars(t.ResumoBarras, "Vendido", "Taxa");
        TxFormaList.ItemsSource = ToSliceVm(t.TaxasPorForma);
        TxDetalheGrid.ItemsSource = t.Detalhe;
    }

    private static List<CompareBarVm> ToCompareBars(
        IReadOnlyList<NegocioMonthCompareRow> rows, string labelA, string labelB) =>
        rows.Select(r => new CompareBarVm
        {
            Label = r.Mes,
            BarA = Math.Max(0, 120 * r.HeightRatioA),
            BarB = Math.Max(0, 120 * r.HeightRatioB),
            Tooltip = $"{labelA}: R$ {r.SerieA:N2} · {labelB}: R$ {r.SerieB:N2}",
        }).ToList();

    private static List<TripleBarVm> ToTripleBars(IReadOnlyList<NegocioMensalRow> rows)
    {
        var max = Math.Max(0.01, rows.Count == 0
            ? 0.01
            : rows.SelectMany(r => new[] { r.Faturamento, r.Custo, Math.Abs(r.Lucro) }).Max());

        return rows.Select(r => new TripleBarVm
        {
            Label = r.Mes,
            BarFat = Math.Max(0, 140 * r.Faturamento / max),
            BarCusto = Math.Max(0, 140 * r.Custo / max),
            BarLucro = Math.Max(0, 140 * Math.Abs(r.Lucro) / max),
            LucroBrush = r.Lucro < -0.009
                ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
                : new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            HasData = r.Faturamento > 0.009 || r.Custo > 0.009 || Math.Abs(r.Lucro) > 0.009,
            Tooltip =
                $"Faturamento: R$ {r.Faturamento:N2} · Custo: R$ {r.Custo:N2} · Lucro: R$ {r.Lucro:N2}" +
                (r.PagFornecedor > 0.009 ? $" · Pag. forn.: R$ {r.PagFornecedor:N2}" : ""),
        }).ToList();
    }

    private static List<SliceVm> ToSliceVm(IReadOnlyList<NegocioSliceRow> rows, bool asMoney = true) =>
        rows.Select(r => new SliceVm
        {
            Label = r.Label,
            TotalDisplay = asMoney ? $"R$ {r.Total:N2}" : $"{r.Total:N0}",
            PctDisplay = $"{r.Pct:N1}%",
            BarWidth = Math.Max(2, 160 * r.BarRatio),
            ColorBrush = BrushFromHex(r.Color),
        }).ToList();

    private static List<PieSliceVm> ToPieSlices(IReadOnlyList<NegocioSliceRow> rows, bool asMoney = true)
    {
        const double cx = 100, cy = 100, r = 90;
        var valid = rows.Where(x => x.Total > 0.009).ToList();
        var total = valid.Sum(x => x.Total);
        if (total <= 0.009)
            return [];

        // Começa no topo (−90°), sentido horário — igual à gestão
        var angle = -90.0;
        var list = new List<PieSliceVm>();
        foreach (var row in valid)
        {
            var sweep = row.Total / total * 360.0;
            if (sweep < 0.01)
                continue;
            var totalTxt = asMoney ? $"R$ {row.Total:N2}" : row.Total.ToString("N0");
            list.Add(new PieSliceVm
            {
                Geometry = MakePieSliceGeometry(cx, cy, r, angle, sweep),
                ColorBrush = BrushFromHex(row.Color),
                Tooltip = $"{row.Label}: {totalTxt} ({row.Pct:N1}%)",
            });
            angle += sweep;
        }
        return list;
    }

    private static Geometry MakePieSliceGeometry(double cx, double cy, double radius, double startDeg, double sweepDeg)
    {
        if (sweepDeg >= 359.9)
        {
            var full = new EllipseGeometry(new Point(cx, cy), radius, radius);
            full.Freeze();
            return full;
        }

        static Point Polar(double cX, double cY, double rad, double deg)
        {
            var radAng = deg * Math.PI / 180.0;
            return new Point(cX + rad * Math.Cos(radAng), cY + rad * Math.Sin(radAng));
        }

        var start = Polar(cx, cy, radius, startDeg);
        var end = Polar(cx, cy, radius, startDeg + sweepDeg);
        var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
        fig.Segments.Add(new LineSegment(start, true));
        fig.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            sweepDeg > 180,
            SweepDirection.Clockwise,
            true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private static Brush BrushFromHex(string hex)
    {
        try
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        }
    }

    private void MeuNegocioModuleView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            Reload();
            e.Handled = true;
        }
    }

    private sealed class DailyBarVm
    {
        public string Label { get; init; } = "";
        public double Total { get; init; }
        public double CaixaBarHeight { get; init; }
        public double FiadoBarHeight { get; init; }
        public string TooltipCaixa { get; init; } = "";
        public string TooltipFiado { get; init; } = "";
        public string TooltipTotal { get; init; } = "";
    }

    private sealed class TopRowVm
    {
        public int Posicao { get; init; }
        public string Name { get; init; } = "";
        public string QtyDisplay { get; init; } = "";
        public double BarWidth { get; init; }
    }

    private sealed class DowBarVm
    {
        public string DiaCurto { get; init; } = "";
        public double BarHeight { get; init; }
        public Brush BarBrush { get; init; } = Brushes.SteelBlue;
        public Brush LabelBrush { get; init; } = Brushes.Gray;
        public string Tooltip { get; init; } = "";
    }

    private sealed class CompareBarVm
    {
        public string Label { get; init; } = "";
        public double BarA { get; init; }
        public double BarB { get; init; }
        public string Tooltip { get; init; } = "";
    }

    private sealed class TripleBarVm
    {
        public string Label { get; init; } = "";
        public double BarFat { get; init; }
        public double BarCusto { get; init; }
        public double BarLucro { get; init; }
        public Brush LucroBrush { get; init; } = Brushes.Green;
        public bool HasData { get; init; }
        public string Tooltip { get; init; } = "";
    }

    private sealed class MargemBarVm
    {
        public string Label { get; init; } = "";
        public string LabelCurto { get; init; } = "";
        public string ValueDisplay { get; init; } = "";
        public double BarWidth { get; init; }
        public double BarHeight { get; init; }
        public Brush ColorBrush { get; init; } = Brushes.SteelBlue;
        public string Tooltip { get; init; } = "";
    }

    private sealed class SliceVm
    {
        public string Label { get; init; } = "";
        public string TotalDisplay { get; init; } = "";
        public string PctDisplay { get; init; } = "";
        public double BarWidth { get; init; }
        public Brush ColorBrush { get; init; } = Brushes.SteelBlue;
    }

    private sealed class PieSliceVm
    {
        public Geometry Geometry { get; init; } = Geometry.Empty;
        public Brush ColorBrush { get; init; } = Brushes.SteelBlue;
        public string Tooltip { get; init; } = "";
    }
}
