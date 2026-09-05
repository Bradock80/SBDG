namespace SGDB.Domain.Commercial;

/// <summary>Linha de venda para rateio do ajuste de cabeçalho. 0 SQL.</summary>
public readonly record struct CommercialGoalHeaderAdjustmentLine(
    int SaleItemId,
    int ProductId,
    decimal Subtotal);

/// <summary>
/// Rateio V1: Adjustment = sales.total − Σ subtotal, proporcional ao subtotal positivo.
/// Hamilton / maior resto. Desempate sale_item_id, depois product_id.
/// </summary>
public static class CommercialGoalHeaderAdjustmentAllocator
{
    public const int OwnQueryCount = 0;

    public static int[] AllocateAttributedCents(
        decimal saleTotal,
        IReadOnlyList<CommercialGoalHeaderAdjustmentLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var n = lines.Count;
        if (n == 0)
            throw new ArgumentException("Venda com itens exige ao menos uma linha.", nameof(lines));

        var baseCents = new int[n];
        var shares = new HamiltonShare[n];
        var baseSum = 0;
        for (var i = 0; i < n; i++)
        {
            var cents = CommercialGoalCents.ToCents(lines[i].Subtotal);
            baseCents[i] = cents;
            baseSum += cents;
            var weight = cents > 0 ? cents : 0;
            shares[i] = new HamiltonShare(weight, lines[i].SaleItemId, lines[i].ProductId);
        }

        var saleCents = CommercialGoalCents.ToCents(saleTotal);
        var adjustment = saleCents - baseSum;
        var adjShares = HamiltonCentsAllocator.Allocate(adjustment, shares);

        var attributed = new int[n];
        var check = 0;
        for (var i = 0; i < n; i++)
        {
            attributed[i] = baseCents[i] + adjShares[i];
            check += attributed[i];
        }

        if (check != saleCents)
            throw new InvalidOperationException("Rateio do cabeçalho não fechou com sales.total.");

        return attributed;
    }
}
