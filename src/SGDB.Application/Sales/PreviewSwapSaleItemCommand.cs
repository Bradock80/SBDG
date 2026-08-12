namespace SGDB.Application.Sales;

/// <summary>
/// Entrada do preview de troca de item (sem gravação).
/// Espelha <c>PdvService.PreviewSwapSaleItem</c>.
/// </summary>
public sealed class PreviewSwapSaleItemCommand
{
    public int SaleId { get; init; }
    public int ItemId { get; init; }
    public int NewProductId { get; init; }
    public bool KeepLinePrice { get; init; }
    public double? NewQuantity { get; init; }

    /// <summary>
    /// Modalidade de cigarro: "AVULSO", "MACO"/"MAÇO", ou null (legado = MAÇO no App).
    /// Application não interpreta preço/fator — só transporta.
    /// </summary>
    public string? CigaretteMode { get; init; }

    /// <summary>Data da sessão de caixa; null = decisão do adapter (hoje).</summary>
    public DateTime? SessionDate { get; init; }
}
