using SGDB.Domain.Common;

namespace SGDB.Domain.Finance;

/// <summary>
/// Regras financeiras puras (taxas e acréscimos).
/// Valores monetários finais usam <see cref="MonetaryRounding.Round"/> (AwayFromZero).
/// </summary>
public static class FinancialCalculator
{
    /// <summary>
    /// Taxa sobre valor bruto: <c>Round(gross × % / 100 + fixo)</c>.
    /// Gross/%/fixo negativos são clampados para 0 (comportamento legado de CalcFeeAmount).
    /// </summary>
    public static double CalculateFeeAmount(double gross, double feePercent, double feeFixed = 0)
    {
        gross = Math.Max(0, gross);
        feePercent = Math.Max(0, feePercent);
        feeFixed = Math.Max(0, feeFixed);
        return MonetaryRounding.Round(gross * feePercent / 100.0 + feeFixed);
    }

    /// <summary>
    /// Acréscimo unitário de tabela de preço: <c>Round(fixo + base × % / 100)</c>.
    /// Sem clamps extras — mesmos operandos do legado CalcUnitSurcharge após o gate da tabela.
    /// </summary>
    public static double CalculateUnitSurcharge(
        double basePrice, double surchargePercent, double surchargeFixed) =>
        MonetaryRounding.Round(surchargeFixed + basePrice * surchargePercent / 100.0);
}
