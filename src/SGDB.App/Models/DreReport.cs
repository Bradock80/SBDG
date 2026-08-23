using SGDB.Utils;

namespace SGDB.Models;

public sealed class DreLineRow
{
    public string Label { get; init; } = "";
    public string Sign { get; init; } = "";
    public double Amount { get; init; }
    public bool IsTotal { get; init; }
    public bool IsSubNote { get; init; }
    public string AmountDisplay => ProductPriceHelper.MoneyBr(Amount);
    public string Tone
    {
        get
        {
            if (IsSubNote) return "muted";
            if (IsTotal && Amount < -0.009) return "neg";
            if (IsTotal && Amount > 0.009) return "pos";
            return "normal";
        }
    }
}

public sealed class DreExpenseBreakdownRow
{
    public string Category { get; init; } = "";
    public double Amount { get; init; }
    public string AmountDisplay => ProductPriceHelper.MoneyBr(Amount);
}

public sealed class DreSimplificadoResult
{
    public DateTime DateFrom { get; init; }
    public DateTime DateTo { get; init; }

    public int QtdVendas { get; init; }
    public int QtdCanceladas { get; init; }

    /// <summary>Soma dos itens (antes de desconto de carrinho).</summary>
    public double ReceitaBruta { get; init; }

    /// <summary>Descontos concedidos nas vendas ativas (bruta − líquida, mínimo 0).</summary>
    public double Descontos { get; init; }

    /// <summary>Valor das vendas canceladas no período (não entra na receita líquida).</summary>
    public double Cancelamentos { get; init; }

    /// <summary>Descontos + cancelamentos (linha de dedução).</summary>
    public double DeducoesVendas { get; init; }

    /// <summary>SUM(sales.total) canceladas = 0.</summary>
    public double ReceitaLiquida { get; init; }

    public double Cmv { get; init; }
    public double CmvHistorico { get; init; }
    public double CmvEstimado { get; init; }
    public bool HasEstimatedLegacyCost { get; init; }
    public bool CmvUsesHistoricalSnapshot { get; init; }
    public bool ProfitIsEstimated { get; init; }
    public bool MarginIsEstimated { get; init; }
    public string? CmvReliabilityNote { get; init; }
    public double LucroBruto { get; init; }
    public double MargemBrutaPercent { get; init; }

    public double DespesasOperacionais { get; init; }
    public double LucroLiquido { get; init; }
    public double MargemLiquidaPercent { get; init; }

    public IReadOnlyList<DreExpenseBreakdownRow> DespesasPorCategoria { get; init; } = [];
    public IReadOnlyList<DreLineRow> CascadeLines { get; init; } = [];

    public string PeriodoDisplay =>
        $"{DateFrom:dd/MM/yyyy} a {DateTo:dd/MM/yyyy}";
}
