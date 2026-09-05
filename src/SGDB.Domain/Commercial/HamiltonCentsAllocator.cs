namespace SGDB.Domain.Commercial;

/// <summary>Parcela de Hamilton. Peso ≥ 0. Desempate TieKey1, depois TieKey2.</summary>
public readonly record struct HamiltonShare(long Weight, int TieKey1, int TieKey2);

/// <summary>
/// Rateio de centavos pelo método de Hamilton (maior resto). 0 SQL.
/// Desempate determinístico: TieKey1, depois TieKey2 — nunca a ordem da lista.
/// </summary>
public static class HamiltonCentsAllocator
{
    public static int[] Allocate(int totalCents, IReadOnlyList<HamiltonShare> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);
        var n = shares.Count;
        var result = new int[n];
        if (n == 0)
        {
            if (totalCents != 0)
                throw new ArgumentException("Não há destinatários para centavos não zero.", nameof(shares));
            return result;
        }

        long weightSum = 0;
        for (var i = 0; i < n; i++)
        {
            if (shares[i].Weight < 0)
                throw new ArgumentOutOfRangeException(nameof(shares), "Peso negativo.");
            weightSum += shares[i].Weight;
        }

        if (weightSum == 0)
        {
            if (totalCents == 0)
                return result;
            var winner = 0;
            for (var i = 1; i < n; i++)
            {
                if (IsStrictlyBefore(shares[i], shares[winner]))
                    winner = i;
            }

            result[winner] = totalCents;
            return result;
        }

        var rank = new RemainderRank[n];
        long assigned = 0;
        for (var i = 0; i < n; i++)
        {
            var w = shares[i].Weight;
            if (w == 0)
            {
                rank[i] = new RemainderRank(i, 0, shares[i].TieKey1, shares[i].TieKey2, HasWeight: false);
                continue;
            }

            var num = (long)totalCents * w;
            var q = num / weightSum;
            result[i] = checked((int)q);
            assigned += q;
            rank[i] = new RemainderRank(
                i,
                AbsRemainder(num, weightSum),
                shares[i].TieKey1,
                shares[i].TieKey2,
                HasWeight: true);
        }

        var leftover = totalCents - checked((int)assigned);
        if (leftover == 0)
            return result;

        Array.Sort(rank, CompareRemainderDescThenTies);
        var step = leftover > 0 ? 1 : -1;
        var need = leftover > 0 ? leftover : -leftover;
        var given = 0;
        for (var k = 0; k < n && given < need; k++)
        {
            if (!rank[k].HasWeight)
                continue;
            result[rank[k].Index] += step;
            given++;
        }

        return result;
    }

    static long AbsRemainder(long numerator, long denominator)
    {
        var rem = numerator % denominator;
        return rem < 0 ? -rem : rem;
    }

    static bool IsStrictlyBefore(HamiltonShare a, HamiltonShare b)
    {
        if (a.TieKey1 != b.TieKey1)
            return a.TieKey1 < b.TieKey1;
        if (a.TieKey2 != b.TieKey2)
            return a.TieKey2 < b.TieKey2;
        return false;
    }

    static int CompareRemainderDescThenTies(RemainderRank a, RemainderRank b)
    {
        var weightCmp = b.HasWeight.CompareTo(a.HasWeight);
        if (weightCmp != 0)
            return weightCmp;
        var rem = b.Remainder.CompareTo(a.Remainder);
        if (rem != 0)
            return rem;
        var t1 = a.TieKey1.CompareTo(b.TieKey1);
        if (t1 != 0)
            return t1;
        var t2 = a.TieKey2.CompareTo(b.TieKey2);
        if (t2 != 0)
            return t2;
        return a.Index.CompareTo(b.Index);
    }

    readonly record struct RemainderRank(
        int Index,
        long Remainder,
        int TieKey1,
        int TieKey2,
        bool HasWeight);
}
