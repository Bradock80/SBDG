using SGDB.Domain.Common;

namespace SGDB.Domain.Commercial;

/// <summary>
/// Conversão determinística reais ↔ centavos. Mesma política AwayFromZero da Meta.
/// </summary>
public static class CommercialGoalCents
{
    public static int ToCents(decimal money)
    {
        var rounded = MonetaryRounding.RoundDecimal(money);
        return (int)decimal.Round(rounded * 100m, 0, MidpointRounding.AwayFromZero);
    }

    public static int ToCents(double money) =>
        ToCents(Convert.ToDecimal(MonetaryRounding.Round(money)));

    public static decimal FromCents(int cents) => cents / 100m;
}
