namespace SGDB.Models;

/// <summary>
/// Modalidade de venda de cigarro no PDV (mesmo ProductId / estoque físico).
/// Strings locais — sem enum amplo — alinhadas a <see cref="PdvScanResult.ModeLabel"/>.
/// </summary>
public static class PdvCigaretteSaleMode
{
    public const string Avulso = "AVULSO";
    public const string Maco = "MAÇO";

    public static bool IsAvulso(string? mode) =>
        string.Equals(mode?.Trim(), Avulso, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode?.Trim(), "Avulso", StringComparison.OrdinalIgnoreCase);

    /// <summary>Null/vazio = MAÇO (comportamento histórico do PDV).</summary>
    public static bool IsMaco(string? mode) =>
        string.IsNullOrWhiteSpace(mode)
        || string.Equals(mode.Trim(), Maco, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode.Trim(), "MACO", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode.Trim(), "Maco", StringComparison.OrdinalIgnoreCase);
}
