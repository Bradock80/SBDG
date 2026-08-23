namespace SGDB.Models;

using SGDB.Utils;

public class NegocioKpiCard
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Accent { get; init; } = "normal"; // normal | hero | danger | info | success
}

public class NegocioChartPoint
{
    public string Label { get; init; } = "";
    public double Caixa { get; init; }
    public double Fiado { get; init; }
    public double Total => Caixa + Fiado;
    public double HeightRatio { get; set; }
    public double CaixaRatio { get; set; }
    public double FiadoRatio { get; set; }
}

public class NegocioTopRow
{
    public int Posicao { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public double Qty { get; init; }
    public double Total { get; init; }
    public double BarRatio { get; set; }
    public string QtyDisplay => Qty.ToString("N3");
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
}

public class NegocioMensalRow
{
    public string Mes { get; init; } = "";
    public double Faturamento { get; init; }
    public double Custo { get; init; }
    public double Lucro { get; init; }
    public double PagFornecedor { get; init; }
    public string FaturamentoDisplay => ProductPriceHelper.MoneyBr(Faturamento);
    public string CustoDisplay => ProductPriceHelper.MoneyBr(Custo);
    public string LucroDisplay => ProductPriceHelper.MoneyBr(Lucro);
    public string PagFornecedorDisplay => PagFornecedor > 0.009 ? ProductPriceHelper.MoneyBr(PagFornecedor) : "—";
    public bool LucroNegativo => Lucro < -0.009;
}

public class NegocioMargemCriticoRow
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string GroupName { get; init; } = "";
    public double CostPrice { get; init; }
    public double SalePrice { get; init; }
    public double MarginPercent { get; init; }
    /// <summary>Custo muito acima da venda — provável erro de unidade (fardo vs unidade).</summary>
    public bool IsCadastroSuspeito { get; init; }
    public string CostDisplay => ProductPriceHelper.MoneyBr(CostPrice);
    public string SaleDisplay => ProductPriceHelper.MoneyBr(SalePrice);
    public string MarginDisplay => IsCadastroSuspeito
        ? $"⚠ {MarginPercent:N1}%"
        : $"{MarginPercent:N1}%";
}

public class NegocioMargemGrupoRow
{
    public string Label { get; init; } = "";
    public double MarginPercent { get; init; }
    public int Qty { get; init; }
    public double BarRatio { get; set; }
    public string MarginDisplay => $"{MarginPercent:N1}%";
}

public class NegocioMargemBenchmarkRow
{
    public string Label { get; init; } = "";
    public double Value { get; init; }
    public string Color { get; init; } = "#2563eb";
    public double BarRatio { get; set; }
    public string ValueDisplay => $"{Value:N1}%";
}

public class NegocioSliceRow
{
    public string Label { get; init; } = "";
    public double Total { get; init; }
    public double Pct { get; init; }
    public string Color { get; init; } = "#2563eb";
    public double BarRatio { get; set; }
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    public string PctDisplay => $"{Pct:N1}%";
}

public class NegocioMonthCompareRow
{
    public string Mes { get; init; } = "";
    public double SerieA { get; init; }
    public double SerieB { get; init; }
    public double HeightRatioA { get; set; }
    public double HeightRatioB { get; set; }
    public string SerieADisplay => ProductPriceHelper.MoneyBr(SerieA);
    public string SerieBDisplay => ProductPriceHelper.MoneyBr(SerieB);
}

public class NegocioRecebimentosData
{
    public double Faturamento { get; init; }
    public double TotalServicos { get; init; }
    public double TotalDescontos { get; init; }
    public IReadOnlyList<NegocioSliceRow> FormaSlices { get; init; } = [];
    public IReadOnlyList<NegocioSliceRow> BandeiraSlices { get; init; } = [];
    public IReadOnlyList<NegocioMonthCompareRow> VsPagar { get; init; } = [];
}

public class NegocioReceitasDespesasData
{
    public double Receitas { get; init; }
    public double TransferenciaCredito { get; init; }
    public double ReceitasTotal { get; init; }
    public double Despesas { get; init; }
    public double DespesasPrevisto { get; init; }
    public double DespesasCaixa { get; init; }
    public double Saldo { get; init; }
    public IReadOnlyList<NegocioSliceRow> DespesasCategoria { get; init; } = [];
    public IReadOnlyList<NegocioMonthCompareRow> MensalReceitasDespesas { get; init; } = [];
    public IReadOnlyList<NegocioMonthCompareRow> MensalPrevistoRealizado { get; init; } = [];
    public string RdDateMode { get; init; } = "due";
}

public class NegocioTaxaDetalheRow
{
    public string Forma { get; init; } = "";
    public double Vendido { get; init; }
    public double FeePercent { get; init; }
    public double TaxaValor { get; init; }
    public double Liquido { get; init; }
    public string VendidoDisplay => ProductPriceHelper.MoneyBr(Vendido);
    public string FeePercentDisplay => $"{FeePercent:N2}%";
    public string TaxaValorDisplay => ProductPriceHelper.MoneyBr(TaxaValor);
    public string LiquidoDisplay => ProductPriceHelper.MoneyBr(Liquido);
}

public class NegocioTaxasLucroData
{
    public double TotalTaxas { get; init; }
    public double TotalRecebido { get; init; }
    public double TotalComTaxa { get; init; }
    public double LiquidoAposTaxas { get; init; }
    public double PctSobreRecebido { get; init; }
    public IReadOnlyList<NegocioSliceRow> TaxasPorForma { get; init; } = [];
    public IReadOnlyList<NegocioTaxaDetalheRow> Detalhe { get; init; } = [];
    public IReadOnlyList<NegocioMonthCompareRow> ResumoBarras { get; init; } = [];
}

public class NegocioVendasInsight
{
    public string MelhorDiaSemana { get; init; } = "—";
    public double MelhorDiaSemanaTotal { get; init; }
    public string RankingDiasResumo { get; init; } = "";
    public IReadOnlyList<NegocioDiaSemanaRow> DiasSemana { get; init; } = [];

    public string PeriodoMesMelhor { get; init; } = "—"; // Início / Meio / Final
    public double InicioMesTotal { get; init; }
    public double MeioMesTotal { get; init; }
    public double FimMesTotal { get; init; }

    public string MelhorDiaData { get; init; } = "—";
    public double MelhorDiaDataTotal { get; init; }

    public bool TemDados { get; init; }
}

public class NegocioDiaSemanaRow
{
    public string Dia { get; init; } = "";
    public double Total { get; init; }
    public double BarRatio { get; set; }
    public bool IsMelhor { get; init; }
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
}

public class NegocioDashboard
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string DateMode { get; set; } = "session";

    public double Faturamento { get; set; }
    public int QtdPedidos { get; set; }
    public double TicketMedio { get; set; }
    public int QtdCancelados { get; set; }
    public double Cmv { get; set; }
    public double CmvHistorico { get; set; }
    public double CmvEstimado { get; set; }
    public bool HasEstimatedLegacyCost { get; set; }
    public bool CmvUsesHistoricalSnapshot { get; set; }
    public bool ProfitIsEstimated { get; set; }
    public bool MarginIsEstimated { get; set; }
    public string? CmvReliabilityNote { get; set; }
    public double ItensVendidos { get; set; }
    public double MediaItensPedido { get; set; }
    public int ClientesAtendidos { get; set; }
    public double Despesas { get; set; }
    public double RecebimentosFiado { get; set; }
    public double SaldoPeriodo { get; set; }
    public double LucroBruto { get; set; }
    public double MargemBrutaPercent { get; set; }

    public List<NegocioChartPoint> DailyChart { get; set; } = [];
    public List<NegocioTopRow> TopVendidos { get; set; } = [];
    public NegocioVendasInsight VendasInsight { get; set; } = new();
    public List<NegocioMensalRow> MensalRows { get; set; } = [];
    public double MensalFaturamento { get; set; }
    public double MensalCusto { get; set; }
    public double MensalLucro { get; set; }

    public double MediaCatalogo { get; set; }
    public double MargemVendasPeriodo { get; set; }
    public double MargemVendasHistorico { get; set; }
    public int QtdVendasPeriodo { get; set; }
    public int QtdVendasHistorico { get; set; }
    public string HistoricoFromBr { get; set; } = "—";
    public string HistoricoToBr { get; set; } = "—";
    public string StatusLabel { get; set; } = "—";
    public string StatusKey { get; set; } = "saudavel"; // saudavel | atencao | critico
    public int FaixaCritico { get; set; }
    public int FaixaAtencao { get; set; }
    public int FaixaSaudavel { get; set; }
    public int FaixaExcelente { get; set; }
    public int TotalComPreco { get; set; }
    public List<NegocioMargemCriticoRow> Abaixo15 { get; set; } = [];
    public List<NegocioMargemGrupoRow> MargemGrupos { get; set; } = [];
    public List<NegocioMargemBenchmarkRow> MargemBenchmarks { get; set; } = [];
    public List<NegocioSliceRow> MargemFaixasPie { get; set; } = [];

    public NegocioRecebimentosData Recebimentos { get; set; } = new();
    public NegocioReceitasDespesasData ReceitasDespesas { get; set; } = new();
    public NegocioTaxasLucroData TaxasLucro { get; set; } = new();
}
