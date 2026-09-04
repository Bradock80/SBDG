using SGDB.Domain.Commercial;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// 71B-B2 — leitura financeira mensal da Meta Comercial.
/// Receita = SUM(sales.total) cancelled=0 por session_date.
/// CMV = HistoricalSaleCostRules (snapshot + legado). Sem meta, UI ou sale_exchanges.
/// QueryCount = 2.
/// </summary>
public static class CommercialGoalFinancialService
{
    public const int ExpectedQueryCount = CommercialGoalFinancialSnapshot.ExpectedQueryCount;

    public const string ExchangeDoesNotAdjustPnlLimitation =
        "Devoluções e trocas (sale_exchanges) não reduzem faturamento, CMV nem lucro bruto "
        + "nesta V1 — a Meta segue a autoridade atual do DRE.";

    public const string UnavailableGrossProfitNote =
        "Lucro bruto indisponível: uma ou mais linhas de venda têm custo não utilizável.";

    public static CommercialGoalFinancialSnapshot Load(CommercialCompetence competence)
    {
        var fromStr = competence.StartDate.ToString("yyyy-MM-dd");
        var toStr = competence.EndDate.ToString("yyyy-MM-dd");

        using var conn = DatabaseService.OpenConnection();

        double revenue = 0;
        var saleCount = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT IFNULL(SUM(total),0), COUNT(*)
                FROM sales
                WHERE IFNULL(cancelled,0) = 0
                  AND session_date >= $from AND session_date <= $to;
                """;
            cmd.Parameters.AddWithValue("$from", fromStr);
            cmd.Parameters.AddWithValue("$to", toStr);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                revenue = RoundMoney(reader.GetDouble(0));
                saleCount = reader.GetInt32(1);
            }
        }

        var breakdown = HistoricalSaleCostRules.SumNonCancelledBySessionWithBreakdown(
            conn, fromStr, toStr);
        var period = breakdown.Period;

        var quality = ResolveQuality(breakdown);
        var revenueDec = ToMoney(revenue);
        var cogsDec = ToMoney(period.Total);
        var historicalCogs = ToMoney(period.Historical);
        var estimatedCogs = ToMoney(period.EstimatedLegacy);

        decimal? grossProfit = null;
        var grossAvailable = false;
        if (quality != CommercialGoalCostQuality.Unavailable)
        {
            grossProfit = revenueDec - cogsDec;
            grossAvailable = true;
        }

        return new CommercialGoalFinancialSnapshot
        {
            Competence = competence,
            NetCommercialRevenue = revenueDec,
            Cogs = cogsDec,
            GrossProfit = grossProfit,
            CostQuality = quality,
            SaleCount = saleCount,
            SaleItemCount = breakdown.SaleItemCount,
            HistoricalCostItemCount = breakdown.HistoricalCostItemCount,
            EstimatedLegacyCostItemCount = breakdown.EstimatedLegacyCostItemCount,
            UnavailableCostItemCount = breakdown.UnavailableCostItemCount,
            HistoricalCogs = historicalCogs,
            EstimatedLegacyCogs = estimatedCogs,
            ProfitIsEstimated = quality == CommercialGoalCostQuality.EstimatedLegacy,
            GrossProfitAvailable = grossAvailable,
            CostReliabilityNote = quality switch
            {
                CommercialGoalCostQuality.EstimatedLegacy =>
                    HistoricalSaleCostRules.EstimatedLegacyPeriodNote,
                CommercialGoalCostQuality.Unavailable => UnavailableGrossProfitNote,
                _ => null,
            },
            ExchangePnlLimitation = ExchangeDoesNotAdjustPnlLimitation,
        };
    }

    static CommercialGoalCostQuality ResolveQuality(PeriodSaleCmvBreakdown breakdown)
    {
        if (breakdown.UnavailableCostItemCount > 0)
            return CommercialGoalCostQuality.Unavailable;
        if (breakdown.EstimatedLegacyCostItemCount > 0)
            return CommercialGoalCostQuality.EstimatedLegacy;
        return CommercialGoalCostQuality.Exact;
    }

    static double RoundMoney(double value) => ProductPriceHelper.RoundPrice(value);

    static decimal ToMoney(double value) =>
        Convert.ToDecimal(RoundMoney(value));
}
