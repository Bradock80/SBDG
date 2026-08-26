namespace SGDB.Domain.Purchases;

public enum PackFactorConfidence
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// Sugestão de fator de embalagem a partir do histórico de compras.
/// Nunca altera cadastro nem decide sozinha a quantidade da NF.
/// </summary>
public sealed record PackFactorSuggestionResult(
    double SuggestedFactor,
    PackFactorConfidence Confidence,
    string Evidence,
    bool RecommendReview)
{
    public static PackFactorSuggestionResult Empty { get; } =
        new(0, PackFactorConfidence.None, "", false);
}

/// <summary>
/// Serviço puro: histórico → sugestão de fator.
/// Fontes confiáveis (fator cadastrado ≥ 2, GTIN, qTrib) ficam na conversão XML;
/// histórico apenas alerta REVISAR.
/// </summary>
public static class PurchasePackFactorSuggestion
{
    static readonly double[] CommonPackSizes =
        [6, 8, 10, 12, 15, 16, 18, 20, 23, 24, 25, 30, 36, 40, 48, 50, 60];

    /// <summary>
    /// Analisa quantidades históricas já gravadas em purchase_items (unidades físicas).
    /// </summary>
    public static PackFactorSuggestionResult SuggestFromHistory(
        double currentCatalogFactor,
        IReadOnlyList<double> historicalQuantities)
    {
        if (currentCatalogFactor >= 2)
        {
            return new PackFactorSuggestionResult(
                Math.Round(currentCatalogFactor, 4),
                PackFactorConfidence.High,
                $"fator cadastrado {currentCatalogFactor:0.####}",
                RecommendReview: false);
        }

        if (historicalQuantities is null || historicalQuantities.Count == 0)
            return PackFactorSuggestionResult.Empty;

        var qtys = historicalQuantities
            .Where(q => q > 0.009 && double.IsFinite(q))
            .Select(q => Math.Round(q, 4))
            .ToList();
        if (qtys.Count < 2)
            return PackFactorSuggestionResult.Empty;

        var freq = qtys
            .GroupBy(q => q)
            .Select(g => (Qty: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.Qty)
            .ToList();

        var packish = freq
            .Where(x => CommonPackSizes.Any(c => Math.Abs(c - x.Qty) < 0.01))
            .ToList();

        if (packish.Count == 0)
            return PackFactorSuggestionResult.Empty;

        var best = packish[0];
        var share = best.Count / (double)qtys.Count;
        var confidence = PackFactorConfidence.Low;
        if (best.Count >= 4 && share >= 0.6)
            confidence = PackFactorConfidence.High;
        else if (best.Count >= 3 && share >= 0.5)
            confidence = PackFactorConfidence.High;
        else if (best.Count >= 2 && share >= 0.4)
            confidence = PackFactorConfidence.Medium;
        else if (best.Count >= 2)
            confidence = PackFactorConfidence.Low;
        else
            return PackFactorSuggestionResult.Empty;

        var evidence = $"{best.Count}/{qtys.Count} compras com qty={best.Qty:0.####}";
        return new PackFactorSuggestionResult(
            best.Qty,
            confidence,
            evidence,
            RecommendReview: confidence >= PackFactorConfidence.Medium);
    }

    /// <summary>
    /// True quando a linha ainda parece comercial (não expandida) e o histórico sugere fator.
    /// </summary>
    public static bool ShouldFlagPackReview(
        double nfQuantity,
        double physicalQuantity,
        PackFactorSuggestionResult suggestion)
    {
        if (!suggestion.RecommendReview || suggestion.SuggestedFactor < 2)
            return false;
        if (nfQuantity <= 0.009)
            return false;
        // Ainda não convertido: qty física ≈ qty comercial.
        if (physicalQuantity > nfQuantity * 1.5 + 0.01)
            return false;
        // 1 CX comercial típica (ou poucas caixas).
        return nfQuantity + 0.01 < suggestion.SuggestedFactor;
    }
}
