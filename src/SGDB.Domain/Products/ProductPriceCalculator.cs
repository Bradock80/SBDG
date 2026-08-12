namespace SGDB.Domain.Products;

using SGDB.Domain.Common;

/// <summary>
/// Núcleo puro de preço/custo. Comportamento idêntico ao núcleo monetário
/// que antes vivia em <c>ProductPriceHelper</c> (App).
/// </summary>
public static class ProductPriceCalculator
{
    /// <summary>
    /// Custo médio ponderado na entrada de estoque:
    /// (estoque_antes × custo_antes + qtd_entrada × custo_entrada) / (estoque_antes + qtd_entrada).
    /// Estoque zerado/negativo → usa o custo da entrada (pode ser 0 = brinde).
    /// </summary>
    public static double WeightedAverageCost(
        double stockBefore, double costBefore, double qtyIn, double costIn)
    {
        if (qtyIn <= 0.0000001)
            return RoundPrice(Math.Max(0, costBefore));

        var before = Math.Max(0, stockBefore);
        var incoming = Math.Max(0, costIn);
        if (before <= 0.0000001)
            return RoundPrice(incoming);

        var totalQty = before + qtyIn;
        if (totalQty <= 0.0000001)
            return RoundPrice(incoming);

        return RoundPrice((before * Math.Max(0, costBefore) + qtyIn * incoming) / totalQty);
    }

    /// <summary>
    /// Remove uma entrada do custo médio (estorno de compra/entrada):
    /// (estoque×custo − qtd×custoEntrada) / (estoque−qtd).
    /// Se sobrar estoque ≤ 0, zera o custo.
    /// </summary>
    public static double RemoveFromWeightedAverage(
        double stockNow, double costNow, double qtyOut, double costOut)
    {
        if (qtyOut <= 0.0000001)
            return RoundPrice(Math.Max(0, costNow));

        var stock = Math.Max(0, stockNow);
        var remaining = stock - qtyOut;
        if (remaining <= 0.0000001)
            return 0;

        var removedValue = qtyOut * Math.Max(0, costOut);
        var keptValue = stock * Math.Max(0, costNow) - removedValue;
        if (keptValue < 0)
            keptValue = 0;
        return RoundPrice(keptValue / remaining);
    }

    /// <summary>
    /// Facade da política monetária comum (<see cref="MonetaryRounding.Round"/>).
    /// Mantida para compatibilidade com consumidores existentes.
    /// </summary>
    /// <remarks>Política interna do SGDB — não é afirmação de obrigação legal.</remarks>
    public static double RoundPrice(double value) =>
        MonetaryRounding.Round(value);

    public static double MarginOnSale(double cost, double sale)
    {
        if (sale <= 0)
            return 0;
        return RoundPrice((sale - cost) / sale * 100.0);
    }

    public static double SaleFromCostAndMargin(double cost, double marginPercent) =>
        marginPercent >= 100 ? 0 : RoundPrice(cost / (1.0 - marginPercent / 100.0));

    public static double CostFromPurchaseAndPercent(double purchase, double costsPercent) =>
        RoundPrice(purchase * (1.0 + costsPercent / 100.0));

    /// <summary>Custo do maço/fardo a partir do custo unitário da NF.</summary>
    public static double PackCostFromUnit(double unitCost, double packFactor) =>
        packFactor >= 2 ? RoundPrice(unitCost * packFactor) : RoundPrice(unitCost);

    /// <summary>
    /// Quantidade física de estoque a partir da quantidade comercial no PDV.
    /// Se <paramref name="stockUnitsPerSale"/> &gt; 1 (ex.: maço = 20 cigarros), multiplica;
    /// caso contrário devolve a quantidade comercial sem arredondar.
    /// </summary>
    /// <remarks>
    /// Arredondamento de quantidade física: 4 casas (não é política monetária).
    /// </remarks>
    public static double StockQuantityForSale(double displayQty, double stockUnitsPerSale) =>
        stockUnitsPerSale > 1.0001
            ? Math.Round(displayQty * stockUnitsPerSale, 4)
            : displayQty;

    /// <summary>
    /// Interpreta o preço atacado cadastrado:
    /// unitário quando ≤ preço de venda (+ tolerância 0,009);
    /// total do lote quando &gt; venda e <paramref name="qtdLote"/> ≥ 2 (divide pelo lote).
    /// </summary>
    public static double WholesaleUnitPrice(double salePrice, double precoAtacado, double qtdLote)
    {
        if (precoAtacado <= 0)
            return salePrice;
        if (precoAtacado <= salePrice + 0.009)
            return RoundPrice(precoAtacado);
        if (qtdLote >= 2)
            return RoundPrice(precoAtacado / qtdLote);
        return RoundPrice(precoAtacado);
    }
}
