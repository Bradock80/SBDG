namespace SGDB.Domain.Purchases;

public enum NfeCfopCostKind
{
    Normal = 0,
    Bonificacao,
    Remessa,
    UnknownOutbound,
}

/// <summary>
/// Classificação conservadora. Só CFOPs explícitos saem de Normal.
/// 59xx/69xx restantes → revisão, não custo pago automático.
/// </summary>
public static class NfeCfopCostClassifier
{
    public static NfeCfopCostKind Classify(string? cfop)
    {
        var digits = new string((cfop ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
            return NfeCfopCostKind.Normal;

        var code = digits.Length > 4 ? digits[^4..] : digits;
        return code switch
        {
            "5910" or "6910" => NfeCfopCostKind.Bonificacao,
            "5911" or "6911" => NfeCfopCostKind.Remessa,
            _ => code.StartsWith("59", StringComparison.Ordinal)
                 || code.StartsWith("69", StringComparison.Ordinal)
                ? NfeCfopCostKind.UnknownOutbound
                : NfeCfopCostKind.Normal,
        };
    }
}
