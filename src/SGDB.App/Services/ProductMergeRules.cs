using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69D-C2-B2 — unificação de produtos com custo médio do estoque físico total
/// (depósito + geladeira) e FKs completas, inclusive purchase_item_lots.
/// </summary>
public static class ProductMergeRules
{
    /// <summary>69T-B — merge seguro com aliases de barcode (v3).</summary>
    public const string AtomicFeature = "product_merge_safe_v3";

    public const string HostNeedsUpgradeBeforeMergeMessage =
        "O PC da loja precisa ser atualizado antes de unificar produtos.";

    public const string NegativeStockMessage =
        "Não é possível unificar: um dos produtos está com estoque total negativo. Ajuste o estoque antes.";

    public const string DifferentCigaretteFactorMessage =
        "Não é possível unificar estes cigarros porque possuem fatores de embalagem diferentes.";

    public const string NormalAndCigaretteMessage =
        "Não é possível unificar um produto comum com um cigarro: as unidades de custo são incompatíveis.";

    public const string OpenInventoryMessage =
        "Não é possível unificar produtos enquanto houver inventário em aberto para eles.";

    public const string ConflictingCompositionMessage =
        "Não é possível unificar: os dois produtos têm composição/kit. Remova ou unifique a composição manualmente.";

    public const string MergeOperation = "unificacao_produto";
    public const string MergeRefType = "product_merge";

    public static bool SupportsSafeMerge(IEnumerable<string>? features) =>
        features is not null
        && features.Any(f => string.Equals(f, AtomicFeature, StringComparison.OrdinalIgnoreCase));

    public static double ResolvePackFactor(string name, string? group, ProductExtra extra)
    {
        var stored = extra.FatorEmbalagem > 1 ? extra.FatorEmbalagem
            : extra.QtdAtacado > 1 ? extra.QtdAtacado : 1;
        if (!ProductClassificationHelper.UsesPackPurchasePrice(name, group))
            return stored > 1 ? stored : 1;
        return ProductPriceHelper.ResolveCigarettesPerPack(name, stored);
    }

    public static void ThrowIfIncompatibleUnits(
        string keepName, string? keepGroup, ProductExtra keepExtra,
        string absorbName, string? absorbGroup, ProductExtra absorbExtra)
    {
        var keepCig = ProductClassificationHelper.UsesPackPurchasePrice(keepName, keepGroup);
        var absorbCig = ProductClassificationHelper.UsesPackPurchasePrice(absorbName, absorbGroup);
        if (keepCig != absorbCig)
            throw new InvalidOperationException(NormalAndCigaretteMessage);
        if (!keepCig)
            return;

        var keepFactor = ResolvePackFactor(keepName, keepGroup, keepExtra);
        var absorbFactor = ResolvePackFactor(absorbName, absorbGroup, absorbExtra);
        if (Math.Abs(keepFactor - absorbFactor) > 0.001)
            throw new InvalidOperationException(DifferentCigaretteFactorMessage);
    }

    public static double WeightedPhysicalAverage(
        double keepWarehouse, double keepFridge, double keepCost,
        double absorbWarehouse, double absorbFridge, double absorbCost,
        bool cigarette, double packFactor)
    {
        var keepPhys = PurchaseAverageCostRules.PhysicalStock(keepWarehouse, keepFridge);
        var absorbPhys = PurchaseAverageCostRules.PhysicalStock(absorbWarehouse, absorbFridge);
        if (keepPhys < -1e-4 || absorbPhys < -1e-4)
            throw new InvalidOperationException(NegativeStockMessage);

        if (keepPhys + absorbPhys <= 1e-4)
        {
            if (keepCost > 0.009)
                return ProductPriceHelper.RoundPrice(keepCost);
            if (absorbCost > 0.009)
                return ProductPriceHelper.RoundPrice(absorbCost);
            return 0;
        }

        var usePacks = cigarette && packFactor >= 2;
        var keepQty = usePacks ? keepPhys / packFactor : keepPhys;
        var absorbQty = usePacks ? absorbPhys / packFactor : absorbPhys;
        return ProductPriceHelper.WeightedAverageCost(keepQty, keepCost, absorbQty, absorbCost);
    }

    public static bool HasOpenInventoryFor(
        SqliteConnection conn, SqliteTransaction tx, int keepId, int absorbId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT 1
            FROM inventory_items ii
            INNER JOIN inventory_sessions s ON s.id = ii.session_id
            WHERE s.status = 'aberta'
              AND ii.product_id IN ($keep, $absorb)
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$keep", keepId);
        cmd.Parameters.AddWithValue("$absorb", absorbId);
        return cmd.ExecuteScalar() is not null and not DBNull;
    }

    public static string MovementNotes(
        int keepId, int absorbId,
        double stockKeep, double fridgeKeep,
        double stockAbsorb, double fridgeAbsorb,
        double finalStock, double finalFridge,
        double costKeep, double costAbsorb, double costAfter,
        double precoCompraAfter)
    {
        return
            $"{{\"keep\":{keepId},\"absorb\":{absorbId}," +
            $"\"sk\":{Fmt(stockKeep)},\"fk\":{Fmt(fridgeKeep)}," +
            $"\"sa\":{Fmt(stockAbsorb)},\"fa\":{Fmt(fridgeAbsorb)}," +
            $"\"stock\":{Fmt(finalStock)},\"fridge\":{Fmt(finalFridge)}," +
            $"\"ck\":{Fmt(costKeep)},\"ca\":{Fmt(costAbsorb)},\"cost\":{Fmt(costAfter)}," +
            $"\"pc\":{Fmt(precoCompraAfter)}}}";
    }

    private static string Fmt(double value) =>
        value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
