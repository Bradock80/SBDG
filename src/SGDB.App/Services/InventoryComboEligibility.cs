using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Constantes e predicados 71A-B1. Sem I/O. Sem par, preço ou ranking.
/// </summary>
public static class InventoryComboEligibility
{
    public const int ExpectedQueryCount = 0;
    public const double Epsilon = InventoryIntelligenceEngine.Epsilon;

    public static bool HasPositiveQuantity(double? value) =>
        value is double number
        && InventoryIntelligenceEngine.IsFinite(number)
        && number > Epsilon;

    public static bool HasFactReason(
        InventoryCommercialFacts? facts,
        InventoryCommercialFactsReason reason)
    {
        var reasons = facts?.LimitationReasons;
        if (reasons is null)
            return false;
        foreach (var item in reasons)
        {
            if (item == reason)
                return true;
        }

        return false;
    }

    public static bool HasAttentionReason(
        InventoryAttentionResult? attention,
        InventoryAttentionReason reason)
    {
        if (attention is null)
            return false;
        if (attention.PrimaryReason == reason)
            return true;
        var secondary = attention.SecondaryReasons;
        if (secondary is null)
            return false;
        foreach (var item in secondary)
        {
            if (item == reason)
                return true;
        }

        return false;
    }
}
