using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69E-B1 — custo unitário gerencial da linha no momento da venda.
/// <c>quantity × cost_at_sale = CMV da linha</c>. Não usa preço de venda para adivinhar unidade.
/// </summary>
public static class SaleCostSnapshotRules
{
    public const string InvalidSnapshotMessage =
        "Não foi possível gravar o custo histórico desta venda. A venda não foi concluída.";

    public static double ComputeForProduct(Product product, double quantity, double stockQty)
    {
        ArgumentNullException.ThrowIfNull(product);
        var extra = ProductExtra.Parse(product.ExtraJson);
        return ComputeLineUnitCost(
            quantity, stockQty, product.CostPrice, product.Name, product.GroupName, extra);
    }

    /// <summary>
    /// Custo unitário da linha comercial: CMV físico ÷ quantity.
    /// Cigarro: cost_price é do maço; estoque físico é em cigarros.
    /// Demais: cost_price é da unidade física.
    /// </summary>
    public static double ComputeLineUnitCost(
        double quantity,
        double stockQty,
        double catalogCost,
        string name,
        string? group,
        ProductExtra extra)
    {
        if (!double.IsFinite(quantity) || quantity <= 0.0000001)
            throw new InvalidOperationException(InvalidSnapshotMessage);
        if (!double.IsFinite(catalogCost) || catalogCost < -1e-9)
            throw new InvalidOperationException(InvalidSnapshotMessage);

        var cost = Math.Max(0, catalogCost);
        var physicalQty = stockQty > 0.0001 ? stockQty : quantity;
        if (!double.IsFinite(physicalQty) || physicalQty <= 0.0000001)
            throw new InvalidOperationException(InvalidSnapshotMessage);

        double physicalUnitCost;
        if (ProductClassificationHelper.UsesPackPurchasePrice(name, group))
        {
            var factor = ProductMergeRules.ResolvePackFactor(name, group, extra);
            if (!double.IsFinite(factor) || factor < 2)
                throw new InvalidOperationException(InvalidSnapshotMessage);
            physicalUnitCost = cost / factor;
        }
        else
        {
            physicalUnitCost = cost;
        }

        var cmv = physicalQty * physicalUnitCost;
        var unit = cmv / quantity;
        if (!double.IsFinite(unit) || unit < -1e-9)
            throw new InvalidOperationException(InvalidSnapshotMessage);
        return ProductPriceHelper.RoundPrice(Math.Max(0, unit));
    }
}
