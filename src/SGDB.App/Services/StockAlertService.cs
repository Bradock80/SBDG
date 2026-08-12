using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Alertas de reposição: produtos abaixo do estoque mínimo,
/// priorizando cerveja/refri mais vendidos.
/// </summary>
public static class StockAlertService
{
    public sealed record AlertItem(
        int ProductId,
        string Code,
        string Name,
        string GroupName,
        double Stock,
        double MinStock,
        double SoldQty);

    public sealed record AlertSnapshot(
        IReadOnlyList<AlertItem> TopDrinks,
        int TotalBelowMin,
        int DrinkBelowMin);

    public static AlertSnapshot GetSnapshot(int take = 5)
    {
        try
        {
            var to = DateTime.Today;
            var from = to.AddDays(-30);
            var minReport = StockService.ListReport(StockReportKind.Minimo, limit: 500);
            var soldReport = StockService.ListReport(
                StockReportKind.MaisVendidos, from, to, limit: 300);

            var soldQty = new Dictionary<int, double>();
            foreach (var r in soldReport.Rows)
            {
                if (r.ProductId > 0)
                    soldQty[r.ProductId] = r.Qty;
            }

            var below = minReport.Rows
                .Where(r => r.ProductId > 0)
                .ToList();

            var drinks = below
                .Where(r => IsBeerOrSoda(r.GroupName, r.Name))
                .Select(r => new AlertItem(
                    r.ProductId,
                    r.Code,
                    r.Name,
                    r.GroupName,
                    r.Stock,
                    r.MinStock,
                    soldQty.TryGetValue(r.ProductId, out var q) ? q : 0))
                .OrderByDescending(a => a.SoldQty)
                .ThenByDescending(a => a.MinStock - a.Stock)
                .ThenBy(a => a.Name)
                .Take(Math.Clamp(take, 1, 10))
                .ToList();

            return new AlertSnapshot(
                drinks,
                below.Count,
                below.Count(r => IsBeerOrSoda(r.GroupName, r.Name)));
        }
        catch
        {
            return new AlertSnapshot([], 0, 0);
        }
    }

    public static bool IsBeerOrSoda(string? groupName, string? productName)
    {
        var g = (groupName ?? "").Trim().ToUpperInvariant();
        var n = (productName ?? "").Trim().ToUpperInvariant();

        if (g.Contains("CERVEJA") || g.Contains("REFRIGERANTE") || g.Contains("REFRI"))
            return true;

        // Nomes comuns quando o grupo está errado/vazio
        string[] keys =
        [
            "CERVEJA", "CHOPP", "HEINEKEN", "BRAHMA", "SKOL", "ANTARCTICA", "ORIGINAL",
            "SPATEN", "CORONA", "BUDWEISER", "STELLA", "AMSTEL", "ITAIPAVA", "EISENBAHN",
            "COCA", "GUARANA", "GUARANÁ", "FANTA", "SPRITE", "PEPSI", "SCHWEPPES",
            "TONICA", "TÔNICA", "SODA", "REFRI", "KUAT", "DOLLY", "H2OH",
        ];
        return keys.Any(k => n.Contains(k, StringComparison.Ordinal));
    }
}
