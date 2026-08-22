using SGDB.Domain.Products;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69D-C1 — custo médio ponderado da compra (depósito + geladeira),
/// na mesma transação. preco_compra permanece o último custo da NF.
/// </summary>
public static class PurchaseAverageCostRules
{
    public const string AtomicFeature = "purchase_average_cost_atomic";

    public const string HostNeedsUpgradeBeforeCloseMessage =
        "O PC da loja precisa ser atualizado antes de concluir uma compra que atualiza o custo médio.";

    public const string HostNeedsUpdateMessage =
        "O PC da loja precisa ser atualizado para gravar o custo médio na compra.";

    public const string HostDidNotApplyMessage =
        "O custo médio informado não foi gravado no cadastro da loja.";

    public const string InvalidQuantityMessage =
        "Quantidade ou custo inválido para o custo médio.";

    public static string NegativeStockMessage(string productName) =>
        $"Estoque total negativo no produto \"{productName}\". Ajuste o estoque antes de lançar a compra para recalcular o custo médio.";

    public static bool NeedsAtomicAverageCostCapability(PurchaseInput input) =>
        input.UpdateAverageCost;

    public static bool SupportsAtomicAverageCost(IEnumerable<string>? features) =>
        features is not null
        && features.Any(f => string.Equals(f, AtomicFeature, StringComparison.OrdinalIgnoreCase));

    public static int CountAppliedProductUpdates(PurchaseInput input) =>
        input.UpdateAverageCost
            ? input.Items.Where(i => i.ProductId > 0).Select(i => i.ProductId).Distinct().Count()
            : 0;

    public static void EnsureHostAppliedAverageCosts(
        PurchaseInput input, bool closeOnSave, int? averageCostUpdates)
    {
        if (!closeOnSave)
            return;
        var requested = CountAppliedProductUpdates(input);
        if (requested == 0)
            return;
        if (averageCostUpdates is null)
            throw new InvalidOperationException(HostNeedsUpdateMessage);
        if (averageCostUpdates.Value != requested)
            throw new InvalidOperationException(HostDidNotApplyMessage);
    }

    public static double PhysicalStock(double warehouse, double fridge)
    {
        RequireFinite(warehouse, fridge);
        return warehouse + fridge;
    }

    public static void RequireUsableStockBefore(double stockBeforeTotal, string productName)
    {
        if (!double.IsFinite(stockBeforeTotal))
            throw new InvalidOperationException(InvalidQuantityMessage);
        if (stockBeforeTotal < -1e-4)
            throw new InvalidOperationException(NegativeStockMessage(productName));
    }

    /// <summary>
    /// Converte quantidade física da compra para a unidade da média
    /// (maços no cigarro; unidades nos demais) e o custo de catálogo da linha.
    /// </summary>
    public static void ToAverageUnits(
        string name,
        string? group,
        double packFactor,
        double physicalQty,
        double unitPrice,
        out double qtyForAvg,
        out double lineCost)
    {
        if (!double.IsFinite(physicalQty) || !double.IsFinite(unitPrice) || physicalQty <= 0)
            throw new InvalidOperationException(InvalidQuantityMessage);

        var factor = packFactor > 1 ? packFactor : 1;
        var isCigPack = ProductClassificationHelper.UsesPackPurchasePrice(name, group);
        var cigsPerPack = isCigPack
            ? ProductPriceHelper.ResolveCigarettesPerPack(name, factor)
            : factor;
        if (isCigPack && cigsPerPack >= 2)
            factor = cigsPerPack;

        var lineTotal = ProductPriceCalculator.RoundPrice(physicalQty * unitPrice);
        lineCost = ProductPriceHelper.ResolveCatalogCost(
            unitPrice, factor, name, group, lineTotal, physicalQty);

        qtyForAvg = isCigPack && cigsPerPack >= 2
            ? physicalQty / cigsPerPack
            : physicalQty;
    }

    public static double StockForAverage(
        string name,
        string? group,
        double packFactor,
        double warehouseBefore,
        double fridgeBefore)
    {
        var physical = PhysicalStock(warehouseBefore, fridgeBefore);
        RequireUsableStockBefore(physical, name);

        var factor = packFactor > 1 ? packFactor : 1;
        var isCigPack = ProductClassificationHelper.UsesPackPurchasePrice(name, group);
        var cigsPerPack = isCigPack
            ? ProductPriceHelper.ResolveCigarettesPerPack(name, factor)
            : factor;
        if (isCigPack && cigsPerPack >= 2)
            return physical / cigsPerPack;
        return physical;
    }

    /// <summary>
    /// Média ponderada agregando todas as linhas do mesmo produto (uma única média).
    /// Estoque anterior = depósito + geladeira, já na unidade da média.
    /// </summary>
    public static double WeightedAverageFromLines(
        double warehouseBefore,
        double fridgeBefore,
        double costBefore,
        string name,
        string? group,
        double packFactor,
        IReadOnlyList<(double Quantity, double UnitPrice)> lines)
    {
        if (lines.Count == 0)
            return ProductPriceCalculator.RoundPrice(Math.Max(0, costBefore));

        var stockBefore = StockForAverage(name, group, packFactor, warehouseBefore, fridgeBefore);
        double qtyIn = 0;
        double valueIn = 0;
        foreach (var line in lines)
        {
            ToAverageUnits(name, group, packFactor, line.Quantity, line.UnitPrice,
                out var qty, out var cost);
            qtyIn += qty;
            valueIn += qty * cost;
        }

        if (qtyIn <= 0.0000001)
            return ProductPriceCalculator.RoundPrice(Math.Max(0, costBefore));

        var costIn = valueIn / qtyIn;
        return ProductPriceHelper.WeightedAverageCost(stockBefore, costBefore, qtyIn, costIn);
    }

    public static double LastLineCatalogCost(
        string name,
        string? group,
        double packFactor,
        IReadOnlyList<(double Quantity, double UnitPrice)> lines)
    {
        if (lines.Count == 0)
            return 0;
        var last = lines[^1];
        ToAverageUnits(name, group, packFactor, last.Quantity, last.UnitPrice,
            out _, out var lineCost);
        return lineCost;
    }

    public static double RemovePurchaseFromAverage(
        double warehouseNow,
        double fridgeNow,
        double costNow,
        string name,
        string? group,
        double packFactor,
        IReadOnlyList<(double Quantity, double UnitPrice)> lines)
    {
        if (lines.Count == 0)
            return ProductPriceCalculator.RoundPrice(Math.Max(0, costNow));

        var stockNow = StockForAverage(name, group, packFactor, warehouseNow, fridgeNow);
        double qtyOut = 0;
        double valueOut = 0;
        foreach (var line in lines)
        {
            ToAverageUnits(name, group, packFactor, line.Quantity, line.UnitPrice,
                out var qty, out var cost);
            qtyOut += qty;
            valueOut += qty * cost;
        }

        if (qtyOut <= 0.0000001)
            return ProductPriceCalculator.RoundPrice(Math.Max(0, costNow));

        var costOut = valueOut / qtyOut;
        return ProductPriceHelper.RemoveFromWeightedAverage(stockNow, costNow, qtyOut, costOut);
    }

    private static void RequireFinite(params double[] values)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                throw new InvalidOperationException(InvalidQuantityMessage);
        }
    }
}
