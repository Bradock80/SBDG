using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Resolução de venda do Companion (sem UI). Host chama; testes cobrem a lógica.
/// </summary>
public static class DeckCompanionSaleHelper
{
    /// <summary>
    /// Payload quando term/scan resolve cigarro com PrecoAvulso e ainda sem mode.
    /// </summary>
    public sealed class ModeRequiredInfo
    {
        public int ProductId { get; init; }
        public string Name { get; init; } = "";
        public double Qty { get; init; }
        public double PrecoAvulso { get; init; }
        public double PrecoMaco { get; init; }
        public bool AllowsAvulso { get; init; } = true;
    }

    public static object MapProductForApi(Product p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var extra = ProductExtra.Parse(p.ExtraJson);
        var isCig = ProductClassificationHelper.IsCigarette(p.Name, p.GroupName);
        var allowsAvulso = isCig && PdvService.AllowsCigaretteAvulso(extra);
        var precoAvulso = allowsAvulso ? ProductPriceHelper.RoundPrice(extra.PrecoAvulso) : 0.0;
        var precoMaco = ResolvePackPrice(p, extra, isCig);
        var fator = isCig
            ? (extra.FatorEmbalagem >= 2 ? extra.FatorEmbalagem
                : (extra.QtdAtacado >= 2 ? extra.QtdAtacado : 0))
            : 0.0;

        return new
        {
            id = p.Id,
            code = p.Code,
            barcode = p.Barcode,
            name = p.Name,
            unit = p.Unit,
            price = isCig ? precoMaco : p.SalePrice,
            priceDisplay = ProductPriceHelper.MoneyBr(isCig ? precoMaco : p.SalePrice),
            allowsAvulso,
            precoAvulso,
            precoMaco,
            fatorEmbalagem = fator > 0 ? fator : (double?)null,
        };
    }

    /// <summary>
    /// Term/scan: se cigarro com PrecoAvulso e ainda sem mode → modeRequired.
    /// Caso contrário null → Host deve seguir AddFromScan (comum, CX, cigarro sem avulso).
    /// </summary>
    public static ModeRequiredInfo? TryGetModeRequiredForTerm(string term, double qty)
    {
        if (string.IsNullOrWhiteSpace(term))
            return null;

        var scan = PdvService.ResolveScan(term);
        if (scan?.Product is null)
            return null;

        var product = scan.Product;
        if (!ProductClassificationHelper.IsCigarette(product.Name, product.GroupName))
            return null;

        var extra = ProductExtra.Parse(product.ExtraJson);
        if (!PdvService.AllowsCigaretteAvulso(extra))
            return null;

        var precoAvulso = ProductPriceHelper.RoundPrice(extra.PrecoAvulso);
        var precoMaco = ResolvePackPrice(product, extra, isCig: true);
        var q = qty > 0 ? qty : 1;

        return new ModeRequiredInfo
        {
            ProductId = product.Id,
            Name = product.Name,
            Qty = q,
            PrecoAvulso = precoAvulso,
            PrecoMaco = precoMaco,
            AllowsAvulso = true,
        };
    }

    /// <summary>
    /// Adiciona item por productId. Mode opcional: cigarro sem mode → MAÇO.
    /// UnitPrice/StockUnitsPerSale do cliente são ignorados (não há parâmetros).
    /// Produto comum com mode: mode é ignorado.
    /// </summary>
    public static OpenTabItemRow AddByProductId(
        int tabId, int productId, double quantity, string? mode)
    {
        if (quantity <= 0)
            throw new OpenTabException("Quantidade inválida.");

        var product = ProductService.GetByIdLocal(productId)
            ?? throw new OpenTabException("Produto não encontrado.");
        if (!product.Active)
            throw new OpenTabException($"Produto inativo: {product.Name}");

        if (!ProductClassificationHelper.IsCigarette(product.Name, product.GroupName))
            return OpenTabService.AddProduct(tabId, productId, quantity);

        if (!string.IsNullOrWhiteSpace(mode)
            && !PdvCigaretteSaleMode.IsAvulso(mode)
            && !PdvCigaretteSaleMode.IsMaco(mode))
            throw new OpenTabException("Modalidade inválida. Use AVULSO ou MACO.");

        var effective = string.IsNullOrWhiteSpace(mode) || PdvCigaretteSaleMode.IsMaco(mode)
            ? PdvCigaretteSaleMode.Maco
            : PdvCigaretteSaleMode.Avulso;

        try
        {
            var resolved = PdvService.ResolveCigaretteSale(product, effective);
            return OpenTabService.AddProduct(
                tabId,
                resolved.Product.Id,
                quantity * resolved.Quantity,
                resolved.UnitPrice,
                resolved.StockUnitsPerSale,
                PdvCartHelper.LineDisplayName(resolved.Product, resolved.ModeLabel));
        }
        catch (InvalidOperationException ex)
        {
            throw new OpenTabException(ex.Message);
        }
    }

    private static double ResolvePackPrice(Product p, ProductExtra extra, bool isCig)
    {
        if (!isCig)
            return p.SalePrice;
        try
        {
            return PdvService.ResolveCigaretteSale(p, PdvCigaretteSaleMode.Maco).UnitPrice;
        }
        catch
        {
            return extra.PrecoAtacado > 0 ? ProductPriceHelper.RoundPrice(extra.PrecoAtacado) : p.SalePrice;
        }
    }
}
