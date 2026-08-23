namespace SGDB.Domain.Finance;

/// <summary>
/// Classificação de um movimento de caixa para resumo/KPI versus detalhe/auditoria.
/// O saldo da gaveta não é filtrado aqui — a apresentação operacional é.
/// </summary>
public readonly record struct CashMovementReportFlags(
    bool IncludeInPdvSalesKpi,
    bool IncludeInOperationalInflows,
    bool IncludeInOperationalOutflows,
    bool IncludeInBalance,
    bool IncludeInDetail,
    bool IncludeInFormaBreakdown,
    string? DetailBadge)
{
    /// <summary>Comportamento legado: tudo entra em KPI e saldo.</summary>
    public static CashMovementReportFlags AllOperational { get; } = new(
        IncludeInPdvSalesKpi: true,
        IncludeInOperationalInflows: true,
        IncludeInOperationalOutflows: true,
        IncludeInBalance: true,
        IncludeInDetail: true,
        IncludeInFormaBreakdown: true,
        DetailBadge: null);
}

/// <summary>
/// ETAPA 69H — regras centrais de apresentação do caixa.
/// Fail-safe: só omite de KPI operacional o par venda cancelada + troca
/// quando a neutralização integral for comprovada. Troca parcial permanece visível.
/// </summary>
public static class CashMovementReportingRules
{
    public const string BadgeCancelledSale = "Venda cancelada";
    public const string BadgeLinkedExchange = "Troca vinculada";

    public const string RefTypeSale = "sale";
    public const string RefTypeSaleExchange = "sale_exchange";
    public const string KindVenda = "venda";

    /// <summary>
    /// Tolerância absoluta (centavos) e relativa para valores enormes (incidente 25338),
    /// onde o ULP de double é maior que R$ 0,01.
    /// </summary>
    public static bool AmountsNeutralize(double a, double b)
    {
        var diff = Math.Abs(a - b);
        if (diff <= 0.05)
            return true;
        var scale = Math.Max(Math.Abs(a), Math.Abs(b));
        if (scale < 1)
            return diff <= 0.05;
        return diff <= Math.Max(0.05, scale * 1e-9);
    }

    /// <summary>
    /// Prova simples de troca/devolução integral que zera o caixa da venda cancelada.
    /// Se houver itens novos (<paramref name="exchangeNewTotal"/> &gt; 0) ou o líquido
    /// não zerar: fail-safe, não omite.
    /// </summary>
    public static bool TryProveIntegralNeutralization(
        bool saleCancelled,
        double saleCashIn,
        double exchangeCashIn,
        double exchangeCashOut,
        double exchangeNewTotal)
    {
        if (!saleCancelled)
            return false;
        if (exchangeNewTotal > 0.009)
            return false;
        if (saleCashIn <= 0.009 || exchangeCashOut <= 0.009)
            return false;
        var net = saleCashIn + exchangeCashIn - exchangeCashOut;
        return AmountsNeutralize(net, 0);
    }

    public static CashMovementReportFlags Classify(
        string kind,
        string? refType,
        int refId,
        bool affectsBalance,
        IReadOnlySet<int> cancelledSaleIds,
        IReadOnlySet<int> neutralizedSaleIds,
        IReadOnlySet<int> neutralizedExchangeIds)
    {
        cancelledSaleIds ??= new HashSet<int>();
        neutralizedSaleIds ??= new HashSet<int>();
        neutralizedExchangeIds ??= new HashSet<int>();

        var kindNorm = (kind ?? "").Trim().ToLowerInvariant();
        var refNorm = (refType ?? "").Trim().ToLowerInvariant();
        var isSaleRef = refNorm == RefTypeSale && refId > 0;
        var isExchangeRef = refNorm == RefTypeSaleExchange && refId > 0;
        var isCancelledSale = isSaleRef && cancelledSaleIds.Contains(refId);
        var isNeutralizedSale = isSaleRef && neutralizedSaleIds.Contains(refId);
        var isNeutralizedExchange = isExchangeRef && neutralizedExchangeIds.Contains(refId);
        var omitOperational = isNeutralizedSale || isNeutralizedExchange;

        string? badge = null;
        if (isCancelledSale)
            badge = BadgeCancelledSale;
        else if (isNeutralizedExchange)
            badge = BadgeLinkedExchange;

        var isVenda = kindNorm == KindVenda;
        return new CashMovementReportFlags(
            IncludeInPdvSalesKpi: isVenda && affectsBalance && !isCancelledSale,
            IncludeInOperationalInflows: !omitOperational,
            IncludeInOperationalOutflows: !omitOperational,
            IncludeInBalance: true,
            IncludeInDetail: true,
            IncludeInFormaBreakdown: !omitOperational,
            DetailBadge: badge);
    }
}
