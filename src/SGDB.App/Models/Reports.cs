using SGDB.Utils;

namespace SGDB.Models;

public class MaisVendidoRow
{
    public int Posicao { get; init; }
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string GroupName { get; init; } = "";
    public double Qty { get; init; }
    public double Total { get; init; }
    public string QtyDisplay => Qty.ToString("N3");
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    public string GroupDisplay => string.IsNullOrWhiteSpace(GroupName) ? "—" : GroupName;
}

public class MaisVendidosResult
{
    public IReadOnlyList<MaisVendidoRow> Rows { get; init; } = [];
    public int Registros { get; init; }
    public double TotalQty { get; init; }
    public double TotalValor { get; init; }
    public DateTime DateFrom { get; init; }
    public DateTime DateTo { get; init; }
}

public class CaixaHistoricoRow
{
    public int Id { get; init; }
    public int SessionId { get; init; }
    public string OpenedAtBr { get; init; } = "";
    public string ClosedAtBr { get; init; } = "";
    public double SaldoInicial { get; init; }
    public double SaldoFinal { get; init; }
    public double? SaldoInformado { get; init; }
    /// <summary>Saldo informado − saldo previsto (null se turno ainda aberto / sem contagem).</summary>
    public double? DifferenceAmount { get; init; }
    public string OperatorName { get; init; } = "";
    public string Observacao { get; init; } = "";
    public bool IsOpen { get; init; }
    public string StatusLabel => IsOpen ? "Aberto" : "Fechado";
    public string StatusKey => IsOpen ? "open" : "closed";
    public string SaldoInicialDisplay => ProductPriceHelper.MoneyBr(SaldoInicial);
    public string SaldoFinalDisplay => ProductPriceHelper.MoneyBr(SaldoFinal);
    public string SaldoInformadoDisplay => ProductPriceHelper.MoneyBrOrDash(SaldoInformado);
    public string OperatorDisplay => string.IsNullOrWhiteSpace(OperatorName) ? "—" : OperatorName;
    public string DifferenceDisplay => DifferenceAmount is double d
        ? ProductPriceHelper.MoneyBr(d)
        : "—";
    /// <summary>none | zero | positive | negative — para cor na grade.</summary>
    public string DifferenceTone
    {
        get
        {
            if (DifferenceAmount is not double d)
                return "none";
            if (Math.Abs(d) < 0.009)
                return "zero";
            return d > 0 ? "positive" : "negative";
        }
    }
    public string ObsDisplay => string.IsNullOrWhiteSpace(Observacao) ? "—" : Observacao;
}

public class CaixaHistoricoListResult
{
    public IReadOnlyList<CaixaHistoricoRow> Rows { get; init; } = [];
    public int Registros { get; init; }
    public string Modo { get; init; } = "recentes";
    public int Limit { get; init; }
}

public class CaixaHistoricoDetail
{
    public int Id { get; init; }
    public int SessionId { get; init; }
    public bool IsOpen { get; init; }
    public string StatusLabel => IsOpen ? "Aberto" : "Fechado";
    public double SaldoInicial { get; init; }
    public double EntradasCaixa { get; init; }
    public double SaidasCaixa { get; init; }
    public double SaldoFinal { get; init; }
    public double SaldoFinalGaveta { get; init; }
    public double? SaldoInformado { get; init; }
    public double? DifferenceAmount { get; init; }
    public string OperatorName { get; init; } = "";
    public Dictionary<string, double> EntradasPorForma { get; init; } = new();
    public string OpeningObs { get; init; } = "";
    public string OpenedAtBr { get; init; } = "";
    public string OpenedTimeBr { get; init; } = "";
    public string ClosedAtBr { get; init; } = "";
    public List<CashMovementRow> Rows { get; init; } = [];
}

public class CurvaAbcRow
{
    public int Posicao { get; init; }
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string GroupName { get; init; } = "";
    public double Qty { get; init; }
    public double Total { get; init; }
    public double ParticipacaoPercent { get; init; }
    public double AcumuladoPercent { get; init; }
    public string Classe { get; init; } = "C";
    public double Stock { get; init; }
    public double CostPrice { get; init; }
    public double DaysOfStock { get; init; }
    public double CapitalParado { get; init; }

    public string QtyDisplay => Qty.ToString("N3");
    public string TotalDisplay => ProductPriceHelper.MoneyBr(Total);
    public string ParticipacaoDisplay => $"{ParticipacaoPercent:N1}%";
    public string AcumuladoDisplay => $"{AcumuladoPercent:N1}%";
    public string GroupDisplay => string.IsNullOrWhiteSpace(GroupName) ? "—" : GroupName;
    public string StockDisplay => Stock.ToString("N3");
    public string DaysDisplay => DaysOfStock <= 0 ? "—" : DaysOfStock.ToString("N1");
    public string CapitalDisplay => ProductPriceHelper.MoneyBr(CapitalParado);
}

public class CurvaAbcResult
{
    public IReadOnlyList<CurvaAbcRow> Rows { get; init; } = [];
    public int Registros { get; init; }
    public double TotalValor { get; init; }
    public int CountA { get; init; }
    public int CountB { get; init; }
    public int CountC { get; init; }
    public DateTime DateFrom { get; init; }
    public DateTime DateTo { get; init; }
}

public class EstoqueMinimoRow
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public double Stock { get; init; }
    public double MinStock { get; init; }
    public double SugestaoCompra { get; init; }
    public string StockDisplay => Stock.ToString("N3");
    public string MinStockDisplay => MinStock.ToString("N0");
    public string SugestaoDisplay => SugestaoCompra.ToString("N0");
}

public class EstoqueMinimoResult
{
    public IReadOnlyList<EstoqueMinimoRow> Rows { get; init; } = [];
    public int Registros { get; init; }
}

public class PrevisaoRecebimentoRow
{
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public double Balance { get; init; }
    public DateTime? LastSale { get; init; }
    public DateTime DueEstimated { get; init; }
    public bool IsOverdue { get; init; }
    public string LastSaleDisplay => LastSale is DateTime d ? d.ToString("dd/MM/yyyy") : "—";
    public string DueDisplay => DueEstimated.ToString("dd/MM/yyyy");
    public string BalanceDisplay => ProductPriceHelper.MoneyBr(Balance);
    public string StatusLabel => IsOverdue ? "Vencido" : "No prazo";
    public string StatusKey => IsOverdue ? "overdue" : "ok";
}

public class PrevisaoRecebimentoResult
{
    public IReadOnlyList<PrevisaoRecebimentoRow> Rows { get; init; } = [];
    public int Registros { get; init; }
    public double TotalProjetado { get; init; }
    public double TotalVencido { get; init; }
    public int HorizontDays { get; init; }
}

public class FechamentoConsolidadoResult
{
    public DateTime DateFrom { get; init; }
    public DateTime DateTo { get; init; }
    public int QtdVendas { get; init; }
    public double TotalFaturado { get; init; }
    public double TotalAVista { get; init; }
    public double TotalFiado { get; init; }
    public double TotalRecebidoFiado { get; init; }
    public double Cmv { get; init; }
    public double CmvHistorico { get; init; }
    public double CmvEstimado { get; init; }
    public bool HasEstimatedLegacyCost { get; init; }
    public bool CmvUsesHistoricalSnapshot { get; init; }
    public bool ProfitIsEstimated { get; init; }
    public bool MarginIsEstimated { get; init; }
    public string? CmvReliabilityNote { get; init; }
    public double LucroEstimado { get; init; }
    public double MargemPercent { get; init; }
    public Dictionary<string, double> PorForma { get; init; } = new();
}
