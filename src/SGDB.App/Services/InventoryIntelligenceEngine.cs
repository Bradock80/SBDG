using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Regras puras do giro físico 70C-B1/B1R. Sem I/O.
/// </summary>
public static class InventoryIntelligenceEngine
{
    /// <summary>Comparações de estoque/VMV (alinhado à precisão física do SGDB, ~4 casas).</summary>
    public const double Epsilon = 0.0001;

    /// <summary>
    /// Limiar de LowCoverage alinhado aos filtros futuros &lt;= 15.
    /// Não é recomendação de compra.
    /// </summary>
    public const double LowCoverageDaysThreshold = 15;

    public const int Window7 = 7;
    public const int Window30 = 30;
    public const int Window90 = 90;

    /// <summary>
    /// Fluxo diário separado: vendas brutas, devoluções e fato de saída válida.
    /// VMV usa MAX(0, Gross − Returns). LastValidSaleDate usa HasValidSaleOutflow.
    /// </summary>
    public readonly record struct DailyFlow(
        DateTime Date,
        double GrossSales,
        double PhysicalReturns,
        bool HasValidSaleOutflow);

    public readonly record struct LifeStartDecision(
        DateTime StartDate,
        string Source,
        string Justification,
        bool HasPhysicalAvailabilityEvidence);

    public static double NetPhysicalDemand(double grossSales, double physicalReturns)
    {
        var gross = IsFinite(grossSales) ? Math.Max(0, grossSales) : 0;
        var ret = IsFinite(physicalReturns) ? Math.Max(0, physicalReturns) : 0;
        return Math.Max(0, gross - ret);
    }

    /// <summary>
    /// FirstObservableAvailabilityDate: primeira evidência confiável de disponibilidade física.
    /// Não usa products.created_at no MIN quando existe entrada/venda.
    /// Cadastro prova existência no catálogo, não prateleira.
    /// </summary>
    public static LifeStartDecision ResolveLifeStart(
        DateTime today,
        DateTime? catalogCreated,
        DateTime? firstTrustedInbound,
        DateTime? firstValidSale)
    {
        today = today.Date;

        DateTime? Clamp(DateTime? candidate)
        {
            if (candidate is not DateTime d)
                return null;
            d = d.Date;
            return d > today ? today : d;
        }

        var inbound = Clamp(firstTrustedInbound);
        var sale = Clamp(firstValidSale);
        var catalog = Clamp(catalogCreated);

        DateTime? physical = null;
        if (inbound is DateTime inboundDate)
            physical = inboundDate;
        if (sale is DateTime saleDate && (physical is null || saleDate < physical.Value))
            physical = saleDate;

        if (physical is DateTime phys)
        {
            string source;
            if (inbound is DateTime i && sale is DateTime s)
                source = s < i ? "sales.session_date" : "trusted_inbound";
            else if (inbound is not null)
                source = "trusted_inbound";
            else
                source = "sales.session_date";

            return new LifeStartDecision(
                phys,
                source,
                "FirstObservableAvailabilityDate = MIN das evidências físicas "
                + "(compra fechada com gerar_estoque, movement de entrada confiável, "
                + "primeira venda válida). products.created_at não entra nesse MIN: "
                + "cadastro em janeiro não dilui VMV de disponibilidade em abril. "
                + "Venda é evidência tardia mínima quando não há entrada anterior. "
                + "Não usamos movement de saída/ajuste de baixa como início.",
                HasPhysicalAvailabilityEvidence: true);
        }

        if (catalog is DateTime cat)
        {
            return new LifeStartDecision(
                cat,
                "products.created_at",
                "Sem evidência física (compra fechada, entrada confiável ou venda). "
                + "Fallback: products.created_at. Não afirma disponibilidade de prateleira. "
                + "Filtros 30/60/90 de silêncio não se aplicam a cadastro isolado. "
                + "Limitação: estoque inicial gravado direto em products.stock sem movement "
                + "não é rastreável como data de entrada.",
                HasPhysicalAvailabilityEvidence: false);
        }

        return new LifeStartDecision(
            today,
            "today",
            "Sem cadastro datado e sem evidência física. Início = hoje (mínimo 1 dia).",
            HasPhysicalAvailabilityEvidence: false);
    }

    public static int HistoryDays(DateTime today, DateTime lifeStart)
    {
        var days = (today.Date - lifeStart.Date).Days + 1;
        return Math.Max(1, days);
    }

    public static (double Gross, double Returns) SumWindow(
        IReadOnlyList<DailyFlow> daily, DateTime today, int windowDays)
    {
        var from = today.Date.AddDays(-(windowDays - 1));
        var to = today.Date;
        double gross = 0;
        double ret = 0;
        foreach (var row in daily)
        {
            var d = row.Date.Date;
            if (d < from || d > to)
                continue;
            gross += row.GrossSales;
            ret += row.PhysicalReturns;
        }
        return (gross, ret);
    }

    public static double Vmv(double netQty, int historyDays, int windowDays)
    {
        var denom = Math.Max(1, Math.Min(windowDays, historyDays));
        var safeNet = Math.Max(0, netQty);
        return SafeRatio(safeNet, denom) ?? 0;
    }

    public static double? SafeRatio(double numerator, double denominator)
    {
        if (!IsFinite(numerator) || !IsFinite(denominator))
            return null;
        if (denominator <= Epsilon)
            return null;
        if (numerator < 0 && numerator > -Epsilon)
            numerator = 0;
        if (numerator < 0)
            return null;
        var r = numerator / denominator;
        return IsFinite(r) ? r : null;
    }

    public static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    public static (InventoryCoverageState State, double? CoverageDays) ClassifyCoverage(
        double totalStock, double vmv30)
    {
        if (!IsFinite(totalStock) || !IsFinite(vmv30) || vmv30 < 0)
            return (InventoryCoverageState.NoTurnover, null);

        if (totalStock < -Epsilon)
            return (InventoryCoverageState.NegativeStock, null);
        if (Math.Abs(totalStock) <= Epsilon)
            return (InventoryCoverageState.ZeroStock, null);
        if (vmv30 <= Epsilon)
            return (InventoryCoverageState.NoTurnover, null);

        var days = SafeRatio(totalStock, vmv30);
        if (days is null)
            return (InventoryCoverageState.NoTurnover, null);
        return (InventoryCoverageState.Calculable, days);
    }

    public static DateTime? LastValidSaleDate(IReadOnlyList<DailyFlow> daily)
    {
        DateTime? last = null;
        foreach (var row in daily)
        {
            if (!row.HasValidSaleOutflow)
                continue;
            var d = row.Date.Date;
            if (last is null || d > last.Value)
                last = d;
        }
        return last;
    }

    public static int? DaysWithoutSale(DateTime today, DateTime? lastValidSaleDate)
    {
        if (lastValidSaleDate is not DateTime d)
            return null;
        return Math.Max(0, (today.Date - d.Date).Days);
    }

    public static InventoryTurnoverSituation ClassifySituation(
        InventoryCoverageState coverage,
        DateTime? lastValidSaleDate,
        int historyDays,
        double? coverageDays)
    {
        if (coverage == InventoryCoverageState.NegativeStock)
            return InventoryTurnoverSituation.NegativeStock;
        if (coverage == InventoryCoverageState.ZeroStock)
            return InventoryTurnoverSituation.ZeroStock;
        if (lastValidSaleDate is null)
            return InventoryTurnoverSituation.NeverSold;
        if (historyDays < Window30)
            return InventoryTurnoverSituation.InsufficientHistory;
        if (coverage == InventoryCoverageState.NoTurnover)
            return InventoryTurnoverSituation.NoTurnover;
        if (coverageDays is double c && c <= LowCoverageDaysThreshold + Epsilon)
            return InventoryTurnoverSituation.LowCoverage;
        return InventoryTurnoverSituation.Normal;
    }

    public static ProductTurnoverRow BuildRow(
        int productId,
        string code,
        string name,
        double stock,
        double stockFridge,
        DateTime today,
        LifeStartDecision life,
        IReadOnlyList<DailyFlow> daily)
    {
        today = today.Date;
        var history = HistoryDays(today, life.StartDate);
        var (g7, r7) = SumWindow(daily, today, Window7);
        var (g30, r30) = SumWindow(daily, today, Window30);
        var (g90, r90) = SumWindow(daily, today, Window90);
        var vmv7 = Vmv(NetPhysicalDemand(g7, r7), history, Window7);
        var vmv30 = Vmv(NetPhysicalDemand(g30, r30), history, Window30);
        var vmv90 = Vmv(NetPhysicalDemand(g90, r90), history, Window90);

        if (!IsFinite(vmv7) || vmv7 < 0) vmv7 = 0;
        if (!IsFinite(vmv30) || vmv30 < 0) vmv30 = 0;
        if (!IsFinite(vmv90) || vmv90 < 0) vmv90 = 0;

        var totalStock = stock + stockFridge;
        if (!IsFinite(totalStock))
            totalStock = 0;

        var (covState, covDays) = ClassifyCoverage(totalStock, vmv30);
        var lastSale = LastValidSaleDate(daily);
        var daysWithout = DaysWithoutSale(today, lastSale);
        var situation = ClassifySituation(covState, lastSale, history, covDays);

        return new ProductTurnoverRow
        {
            ProductId = productId,
            Code = code ?? "",
            Name = name ?? "",
            Stock = stock,
            StockFridge = stockFridge,
            TotalStock = totalStock,
            Vmv7 = vmv7,
            Vmv30 = vmv30,
            Vmv90 = vmv90,
            CoverageDays = covDays,
            CoverageState = covState,
            LastValidSaleDate = lastSale,
            DaysWithoutSale = daysWithout,
            HistoryDays = history,
            IsHistoryInsufficient7 = history < Window7,
            IsHistoryInsufficient30 = history < Window30,
            IsHistoryInsufficient90 = history < Window90,
            HasPhysicalAvailabilityEvidence = life.HasPhysicalAvailabilityEvidence,
            Situation = situation,
        };
    }
}
