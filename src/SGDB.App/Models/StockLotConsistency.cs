namespace SGDB.Models;

/// <summary>
/// Linha somente leitura: consistência entre products.stock e soma de product_lots.
/// Não altera estoque nem lotes.
/// </summary>
public sealed class StockLotConsistencyRow
{
    /// <summary>Tolerância alinhada ao inventário (ABS(diff) &gt; 0.0009).</summary>
    public const double Tolerance = 0.0009;

    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string ProductName { get; init; } = "";
    public double GlobalStock { get; init; }
    public double LotsStock { get; init; }
    public double Difference { get; init; }
    public bool HasLots { get; init; }
    public bool? ExpiryControl { get; init; }

    public string GlobalDisplay => GlobalStock.ToString("N3");
    public string LotsDisplay => LotsStock.ToString("N3");
    public string DifferenceDisplay => Difference.ToString("N3");
    public string HasLotsDisplay => HasLots ? "Sim" : "Não";
    public string ExpiryControlDisplay => ExpiryControl switch
    {
        true => "Sim",
        false => "Não",
        null => "—",
    };

    /// <summary>OK | Global > Lotes | Lotes > Global</summary>
    public string Situation
    {
        get
        {
            if (Math.Abs(Difference) <= Tolerance)
                return "OK";
            return Difference > 0 ? "Global > Lotes" : "Lotes > Global";
        }
    }

    public string Tone => Situation switch
    {
        "Global > Lotes" => "global",
        "Lotes > Global" => "lots",
        _ => "ok",
    };
}

/// <summary>Opções de filtro do diagnóstico (somente leitura).</summary>
public sealed class StockLotConsistencyQuery
{
    /// <summary>Só linhas com |diferença| &gt; tolerância.</summary>
    public bool OnlyDivergent { get; init; } = true;

    /// <summary>
    /// Só produtos que já têm lote com quantidade OU ControleValidade=true.
    /// Evita falso alarme em produtos legados sem lotes.
    /// </summary>
    public bool OnlyWithLotsOrExpiryControl { get; init; } = true;

    public string? Search { get; init; }
}
