using SGDB.Models;
using SGDB.Services;

namespace SGDB.Utils;

/// <summary>
/// Regras de preço e fusão de linhas do carrinho PDV (testáveis sem UI).
/// </summary>
public static class PdvCartHelper
{
    /// <summary>
    /// Preço unitário ao incluir/fundir linha. Avulso e maço de cigarro
    /// preservam o preço pendente ao aumentar qty; atacado comum permanece.
    /// </summary>
    public static double ResolveLineUnitPrice(
        Product product, double qty, double pendingUnitPrice, double stockUnitsPerSale = 1)
    {
        var isCig = ProductClassificationHelper.IsCigarette(product.Name, product.GroupName);
        var extra = ProductExtra.Parse(product.ExtraJson);

        // Maço cigarro: preço fixo por maço (qtd 1, 2, 3…)
        if (stockUnitsPerSale > 1.0001
            && isCig
            && pendingUnitPrice > 0)
            return pendingUnitPrice;

        // Avulso cigarro: StockUnitsPerSale=1 + PrecoAvulso cadastrado — não recalcular atacado/SalePrice
        if (stockUnitsPerSale <= 1.0001
            && isCig
            && PdvService.AllowsCigaretteAvulso(extra)
            && pendingUnitPrice > 0)
            return pendingUnitPrice;

        var packQty = extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem
            : (extra.QtdAtacado >= 2 ? extra.QtdAtacado : 0);

        // Bipou CX/fardo (refrigerante): preço unitário só na qtd exata do fardo
        if (pendingUnitPrice > 0 && packQty >= 2 && extra.PrecoAtacado > 0)
        {
            var packUnit = PdvService.WholesaleUnitPrice(product.SalePrice, extra.PrecoAtacado, packQty);
            if (Math.Abs(pendingUnitPrice - packUnit) < 0.009)
            {
                if (Math.Abs(qty - packQty) < 0.0001)
                    return packUnit;
                return PdvService.UnitPriceForQuantity(product, qty);
            }
        }

        if (pendingUnitPrice > 0 && Math.Abs(qty - 1) < 0.0001)
            return pendingUnitPrice;

        return PdvService.UnitPriceForQuantity(product, qty);
    }

    /// <summary>
    /// Inclui ou funde linha por ProductId + StockUnitsPerSale.
    /// </summary>
    public static PdvCartLine IncludeOrMerge(
        IList<PdvCartLine> cart,
        Product product,
        double qty,
        double pendingUnitPrice,
        double stockUnitsPerSale,
        ref int lineCounter,
        string? lineDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(cart);
        ArgumentNullException.ThrowIfNull(product);
        if (qty <= 0)
            throw new ArgumentOutOfRangeException(nameof(qty));

        var unitPrice = ResolveLineUnitPrice(product, qty, pendingUnitPrice, stockUnitsPerSale);
        var name = string.IsNullOrWhiteSpace(lineDisplayName) ? product.Name : lineDisplayName.Trim();

        var existing = cart.FirstOrDefault(c =>
            c.ProductId == product.Id
            && Math.Abs(c.StockUnitsPerSale - stockUnitsPerSale) < 0.0001);

        if (existing is not null)
        {
            var newQty = ProductPriceHelper.RoundPrice(existing.Quantity + qty);
            var mergedPrice = ResolveLineUnitPrice(product, newQty, pendingUnitPrice, stockUnitsPerSale);
            var idx = cart.IndexOf(existing);
            var merged = new PdvCartLine
            {
                LineNum = existing.LineNum,
                ProductId = existing.ProductId,
                Code = existing.Code,
                Name = existing.Name,
                Unit = existing.Unit,
                Quantity = newQty,
                UnitPrice = mergedPrice,
                StockUnitsPerSale = existing.StockUnitsPerSale,
            };
            cart[idx] = merged;
            return merged;
        }

        var line = new PdvCartLine
        {
            LineNum = ++lineCounter,
            ProductId = product.Id,
            Code = product.Code ?? "",
            Name = name,
            Unit = product.Unit,
            Quantity = qty,
            UnitPrice = unitPrice,
            StockUnitsPerSale = stockUnitsPerSale,
        };
        cart.Add(line);
        return line;
    }

    public static string LineDisplayName(Product product, string? modeLabel) =>
        string.IsNullOrWhiteSpace(modeLabel)
            ? product.Name
            : $"{product.Name} ({modeLabel})";

    public static bool NeedsCigaretteModeChoice(Product product)
    {
        if (!ProductClassificationHelper.IsCigarette(product.Name, product.GroupName))
            return false;
        var extra = ProductExtra.Parse(product.ExtraJson);
        return PdvService.AllowsCigaretteAvulso(extra);
    }
}
