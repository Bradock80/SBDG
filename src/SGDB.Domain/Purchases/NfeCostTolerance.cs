namespace SGDB.Domain.Purchases;

/// <summary>
/// Tolerância única da conciliação de custo da NF-e.
/// R$ 0,05 cobre arredondamento de centavo; 0,5% cobre conversão
/// comercial→físico (dúzia/caixa) sem aceitar erro grosseiro.
/// </summary>
public static class NfeCostTolerance
{
    public const double AbsoluteReais = 0.05;
    public const double Relative = 0.005;

    public static double AllowedDelta(double reference) =>
        Math.Max(AbsoluteReais, Math.Abs(reference) * Relative);

    public static bool NearlyEqual(double a, double b, double? reference = null)
    {
        var basis = reference ?? Math.Max(Math.Abs(a), Math.Abs(b));
        if (basis < AbsoluteReais)
            return Math.Abs(a - b) <= AbsoluteReais;
        return Math.Abs(a - b) <= AllowedDelta(basis);
    }
}
