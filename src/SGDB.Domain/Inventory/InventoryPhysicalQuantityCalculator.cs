namespace SGDB.Domain.Inventory;

/// <summary>
/// Conversão pura maços + avulsos ↔ unidades físicas para inventário.
/// Não depende de UI, SQLite ou serviços de estoque.
/// </summary>
public static class InventoryPhysicalQuantityCalculator
{
    /// <summary>Limite superior de quantidade física aceita na conversão.</summary>
    public const long MaxPhysicalQuantity = 1_000_000_000L;

    public readonly record struct PackLooseSplit(long Packs, long Loose);

    public static bool IsValidFactor(int factor) => factor >= 2;

    /// <summary>
    /// Converte fator de embalagem (cadastro) em inteiro válido para maços/avulsos.
    /// Retorna false se &lt; 2 ou não for praticamente inteiro.
    /// </summary>
    public static bool TryResolveFactor(double fatorEmbalagem, out int factor)
    {
        factor = 0;
        if (fatorEmbalagem < 2)
            return false;

        var rounded = (int)Math.Round(fatorEmbalagem, MidpointRounding.AwayFromZero);
        if (rounded < 2)
            return false;
        if (Math.Abs(fatorEmbalagem - rounded) > 0.0001)
            return false;

        factor = rounded;
        return true;
    }

    /// <summary>
    /// Normaliza avulsos ≥ fator em maços extras.
    /// Ex.: 1 maço + 25 avulsos (fator 20) → 2 maços + 5 avulsos.
    /// </summary>
    public static PackLooseSplit Normalize(long packs, long loose, int factor)
    {
        EnsureNonNegative(packs, loose);
        EnsureFactor(factor);

        if (loose >= factor)
        {
            packs += loose / factor;
            loose %= factor;
        }

        EnsurePhysicalWithinLimit(packs, loose, factor);
        return new PackLooseSplit(packs, loose);
    }

    /// <summary>
    /// Calcula unidades físicas: packs × factor + loose (após normalização).
    /// </summary>
    public static double Calculate(long packs, long loose, int factor)
    {
        var normalized = Normalize(packs, loose, factor);
        return ToPhysical(normalized.Packs, normalized.Loose, factor);
    }

    /// <summary>
    /// Decompõe quantidade física inteira em maços + avulsos.
    /// Ex.: 2182 / 20 → 109 maços + 2 avulsos.
    /// </summary>
    public static PackLooseSplit SplitPhysicalQuantity(double physicalTotal, int factor)
    {
        EnsureFactor(factor);
        if (physicalTotal < -0.0001)
            throw new ArgumentOutOfRangeException(nameof(physicalTotal), "Quantidade física não pode ser negativa.");

        if (!IsWholeNumber(physicalTotal))
            throw new ArgumentException(
                "Quantidade física com decimal não pode ser decomposta em maços/avulsos.",
                nameof(physicalTotal));

        var total = (long)Math.Round(physicalTotal, MidpointRounding.AwayFromZero);
        if (total > MaxPhysicalQuantity)
            throw new ArgumentOutOfRangeException(nameof(physicalTotal),
                $"Quantidade física excede o limite de {MaxPhysicalQuantity:N0}.");

        var packs = total / factor;
        var loose = total % factor;
        return new PackLooseSplit(packs, loose);
    }

    /// <summary>True se o total físico é inteiro (apto a maços/avulsos).</summary>
    public static bool IsWholeNumber(double value)
        => Math.Abs(value - Math.Round(value, MidpointRounding.AwayFromZero)) < 0.0001;

    private static double ToPhysical(long packs, long loose, int factor)
    {
        EnsurePhysicalWithinLimit(packs, loose, factor);
        return packs * (long)factor + loose;
    }

    private static void EnsureNonNegative(long packs, long loose)
    {
        if (packs < 0)
            throw new ArgumentOutOfRangeException(nameof(packs), "Maços não pode ser negativo.");
        if (loose < 0)
            throw new ArgumentOutOfRangeException(nameof(loose), "Avulsos não pode ser negativo.");
    }

    private static void EnsureFactor(int factor)
    {
        if (!IsValidFactor(factor))
            throw new ArgumentOutOfRangeException(nameof(factor), "Fator de embalagem deve ser >= 2.");
    }

    private static void EnsurePhysicalWithinLimit(long packs, long loose, int factor)
    {
        if (packs > MaxPhysicalQuantity / factor)
            throw new ArgumentOutOfRangeException(nameof(packs),
                $"Quantidade física excede o limite de {MaxPhysicalQuantity:N0}.");

        var total = packs * (long)factor + loose;
        if (total > MaxPhysicalQuantity)
            throw new ArgumentOutOfRangeException(nameof(loose),
                $"Quantidade física excede o limite de {MaxPhysicalQuantity:N0}.");
    }
}
