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
}
