namespace SGDB.Domain.Common;

/// <summary>
/// Política interna do SGDB para valores monetários calculados:
/// duas casas decimais com <see cref="MidpointRounding.AwayFromZero"/>.
/// </summary>
/// <remarks>
/// Não use para quantidades físicas (estoque, peso, fatores).
/// Não é afirmação de obrigação legal/fiscal.
/// </remarks>
public static class MonetaryRounding
{
    public static double Round(double value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Teto em centavos para piso financeiro 70F-B3.
    /// Não substitui <see cref="Round"/> (AwayFromZero), que pode ficar abaixo da margem mínima.
    /// </summary>
    public static decimal CeilingToCents(decimal value)
    {
        if (value <= 0m)
            return 0m;
        return decimal.Ceiling(value * 100m) / 100m;
    }
}
