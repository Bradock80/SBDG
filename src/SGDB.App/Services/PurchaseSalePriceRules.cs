using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// ETAPA 69D-B — intenção e validação do preço de venda na compra.
/// A UI envia UpdateSalePrice; o service não infere edição comparando números.
/// </summary>
public static class PurchaseSalePriceRules
{
    public const string AtomicFeature = "purchase_sale_price_atomic";

    public const string InvalidSalePriceMessage =
        "Preço de venda inválido. Informe um valor finito maior que zero.";

    public const string HostNeedsUpgradeBeforeCloseMessage =
        "O PC da loja precisa ser atualizado antes de concluir uma compra que altera preços de venda.";

    public const string HostNeedsUpdateMessage =
        "O PC da loja precisa ser atualizado para gravar o preço de venda na compra.";

    public const string HostDidNotApplyMessage =
        "O preço de venda informado não foi gravado no cadastro da loja.";

    public static void RequireValidSalePrice(double sale)
    {
        if (!double.IsFinite(sale) || sale <= 0)
            throw new InvalidOperationException(InvalidSalePriceMessage);
    }

    public static double NormalizeSalePrice(double sale)
    {
        RequireValidSalePrice(sale);
        return ProductPriceHelper.RoundPrice(sale);
    }

    public static bool SameMoney(double a, double b) =>
        Math.Abs(ProductPriceHelper.RoundPrice(a) - ProductPriceHelper.RoundPrice(b)) < 0.001;

    public static int CountRequestedSaleUpdates(IEnumerable<PurchaseItemInput> items) =>
        items.Count(i => i.UpdateSalePrice);

    public static bool NeedsAtomicSalePriceCapability(PurchaseInput input) =>
        CountRequestedSaleUpdates(input.Items) > 0;

    public static bool SupportsAtomicSalePrice(IEnumerable<string>? features) =>
        features is not null
        && features.Any(f => string.Equals(f, AtomicFeature, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Client novo + host antigo: o host ignora SalePrice/UpdateSalePrice e não
    /// devolve salePriceUpdates. Não tratar isso como sucesso silencioso.
    /// </summary>
    public static void EnsureHostAppliedSalePrices(
        PurchaseInput input, bool closeOnSave, int? salePriceUpdates)
    {
        if (!closeOnSave)
            return;
        var requested = CountRequestedSaleUpdates(input.Items);
        if (requested == 0)
            return;
        if (salePriceUpdates is null)
            throw new InvalidOperationException(HostNeedsUpdateMessage);
        if (salePriceUpdates.Value != requested)
            throw new InvalidOperationException(HostDidNotApplyMessage);
    }
}
