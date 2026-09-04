using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69E-B2 — leitura de CMV da linha de venda.
/// Snapshot (<c>cost_at_sale</c>) é histórico confiável; NULL é legado estimado.
/// Não grava nem faz backfill.
/// </summary>
public readonly record struct SaleLineCmv(
    double UnitCost,
    double TotalCost,
    bool IsHistorical,
    bool IsEstimated);

public sealed class PeriodSaleCmv
{
    public double Total { get; init; }
    public double Historical { get; init; }
    public double EstimatedLegacy { get; init; }
    public bool HasEstimatedLegacyCost { get; init; }
    public bool HasHistoricalCost { get; init; }
    public bool ProfitIsEstimated => HasEstimatedLegacyCost;
    public bool MarginIsEstimated => HasEstimatedLegacyCost;
    public string? ReliabilityNote => HasEstimatedLegacyCost
        ? HistoricalSaleCostRules.EstimatedLegacyPeriodNote
        : null;
}

/// <summary>
/// 71B-B2 — mesmos custos de <see cref="PeriodSaleCmv"/> + contagem de origem por linha.
/// Não altera a fórmula de <see cref="HistoricalSaleCostRules.ResolveLine"/>.
/// </summary>
public sealed class PeriodSaleCmvBreakdown
{
    public PeriodSaleCmv Period { get; init; } = new();
    public int SaleItemCount { get; init; }
    public int HistoricalCostItemCount { get; init; }
    public int EstimatedLegacyCostItemCount { get; init; }
    public int UnavailableCostItemCount { get; init; }
}

public static class HistoricalSaleCostRules
{
    public const string EstimatedLegacyPeriodNote =
        "Parte do CMV deste período é estimada porque existem vendas anteriores ao histórico de custos.";

    /// <summary>
    /// Host novo preenche este flag. Ausente (host antigo) → não presumir CMV histórico.
    /// </summary>
    public const bool ReportsUseHistoricalSnapshot = true;

    public static SaleLineCmv ResolveLine(
        double quantity,
        double? costAtSale,
        double catalogCost,
        double unitSalePrice,
        string? productName,
        string? group,
        ProductExtra? extra)
    {
        if (costAtSale is { } snap && double.IsFinite(snap))
        {
            var unit = ProductPriceHelper.RoundPrice(snap);
            return new SaleLineCmv(unit, quantity * unit, IsHistorical: true, IsEstimated: false);
        }

        var unitEst = ProductPriceHelper.UnitCostForSoldLine(
            catalogCost, unitSalePrice, extra, productName, group);
        return new SaleLineCmv(unitEst, quantity * unitEst, IsHistorical: false, IsEstimated: true);
    }

    public static PeriodSaleCmv Sum(IEnumerable<SaleLineCmv> lines)
    {
        double hist = 0, est = 0;
        var hadEst = false;
        var hadHist = false;
        foreach (var line in lines)
        {
            if (line.IsHistorical)
            {
                hist += line.TotalCost;
                hadHist = true;
            }
            else
            {
                est += line.TotalCost;
                hadEst = true;
            }
        }

        return new PeriodSaleCmv
        {
            Historical = ProductPriceHelper.RoundPrice(hist),
            EstimatedLegacy = ProductPriceHelper.RoundPrice(est),
            Total = ProductPriceHelper.RoundPrice(hist + est),
            HasEstimatedLegacyCost = hadEst,
            HasHistoricalCost = hadHist,
        };
    }

    public static PeriodSaleCmv SumNonCancelledBySession(
        SqliteConnection conn, string fromStr, string toStr) =>
        SumNonCancelledBySessionWithBreakdown(conn, fromStr, toStr).Period;

    /// <summary>
    /// Mesma consulta e fórmula de CMV do período; adiciona contagens de origem.
    /// Linha com quantity/custo não finito entra em Unavailable e não no Total do período.
    /// </summary>
    public static PeriodSaleCmvBreakdown SumNonCancelledBySessionWithBreakdown(
        SqliteConnection conn, string fromStr, string toStr)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.quantity, si.unit_price, si.cost_at_sale,
                   IFNULL(p.cost_price,0),
                   IFNULL(si.product_name,''), IFNULL(p.extra_json,''), IFNULL(p.group_name,'')
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE IFNULL(s.cancelled,0) = 0
              AND s.session_date >= $from AND s.session_date <= $to;
            """;
        cmd.Parameters.AddWithValue("$from", fromStr);
        cmd.Parameters.AddWithValue("$to", toStr);
        return SumBreakdownFromReader(cmd);
    }

    public static PeriodSaleCmv SumAllNonCancelled(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.quantity, si.unit_price, si.cost_at_sale,
                   IFNULL(p.cost_price,0),
                   IFNULL(si.product_name,''), IFNULL(p.extra_json,''), IFNULL(p.group_name,'')
            FROM sale_items si
            JOIN sales s ON s.id = si.sale_id
            LEFT JOIN products p ON p.id = si.product_id
            WHERE IFNULL(s.cancelled,0) = 0;
            """;
        return SumFromReader(cmd);
    }

    public static double? ReadCostAtSale(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        var value = reader.GetDouble(ordinal);
        return double.IsFinite(value) ? value : null;
    }

    private static PeriodSaleCmv SumFromReader(SqliteCommand cmd) =>
        SumBreakdownFromReader(cmd).Period;

    private static PeriodSaleCmvBreakdown SumBreakdownFromReader(SqliteCommand cmd)
    {
        var usable = new List<SaleLineCmv>();
        var saleItemCount = 0;
        var historicalCount = 0;
        var estimatedCount = 0;
        var unavailableCount = 0;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            saleItemCount++;
            var qty = reader.GetDouble(0);
            var unitSale = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
            var costAtSale = ReadCostAtSale(reader, 2);
            var catalogCost = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
            var name = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var extra = ProductExtra.Parse(reader.IsDBNull(5) ? null : reader.GetString(5));
            var group = reader.IsDBNull(6) ? "" : reader.GetString(6);

            if (!double.IsFinite(qty) || !double.IsFinite(unitSale) || !double.IsFinite(catalogCost))
            {
                unavailableCount++;
                continue;
            }

            var line = ResolveLine(qty, costAtSale, catalogCost, unitSale, name, group, extra);
            if (!double.IsFinite(line.UnitCost) || !double.IsFinite(line.TotalCost))
            {
                unavailableCount++;
                continue;
            }

            usable.Add(line);
            if (line.IsHistorical)
                historicalCount++;
            else
                estimatedCount++;
        }

        return new PeriodSaleCmvBreakdown
        {
            Period = Sum(usable),
            SaleItemCount = saleItemCount,
            HistoricalCostItemCount = historicalCount,
            EstimatedLegacyCostItemCount = estimatedCount,
            UnavailableCostItemCount = unavailableCount,
        };
    }
}
