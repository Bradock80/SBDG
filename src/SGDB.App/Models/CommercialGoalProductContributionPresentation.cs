using SGDB.Domain.Commercial;
using SGDB.Utils;

namespace SGDB.Models;

/// <summary>Estado semântico da seção de contribuição. Sem cor/WPF.</summary>
public enum CommercialGoalProductContributionPresentationState
{
    Historical = 0,
    Estimated,
    ProfitUnavailable,
    Empty,
    UnattributedOnly,
}

/// <summary>Item PT-BR de contribuição histórica por produto. Sem WPF.</summary>
public sealed class CommercialGoalProductContributionItemPresentation
{
    public int Rank { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductTitle { get; init; } = "";
    public decimal Revenue { get; init; }
    public decimal Cogs { get; init; }
    public decimal? GrossProfit { get; init; }
    public decimal? GrossMarginPercent { get; init; }
    public decimal? GrossProfitShare { get; init; }
    public string RevenueText { get; init; } = CommercialGoalPresentation.EmDash;
    public string CogsText { get; init; } = CommercialGoalPresentation.EmDash;
    public string GrossProfitText { get; init; } = CommercialGoalPresentation.EmDash;
    public string GrossMarginText { get; init; } = CommercialGoalPresentation.EmDash;
    public string GrossProfitShareText { get; init; } = CommercialGoalPresentation.EmDash;
    public string CostQualityText { get; init; } = "";
    public string CostQualityExplanation { get; init; } = "";
    public CommercialGoalCostQuality CostQuality { get; init; }
    public CommercialGoalPresentationTone Tone { get; init; }
    public CommercialGoalProductContributionLimitation Limitations { get; init; }
    public IReadOnlyList<string> Indicators { get; init; } = [];
    public IReadOnlyList<CommercialGoalLimitationPresentation> LimitationItems { get; init; } = [];

    public bool HasLimitation(CommercialGoalProductContributionLimitation limitation) =>
        Limitations.HasFlag(limitation);
}

/// <summary>Snapshot PT-BR 71B-B8C. Consome B8B. Sem I/O.</summary>
public sealed class CommercialGoalProductContributionPresentationSnapshot
{
    public CommercialCompetence Competence { get; init; }
    public int QueryCount { get; init; }
    public string SectionTitle { get; init; } = CommercialGoalProductContributionPresentation.SectionTitle;
    public string Headline { get; init; } = "";
    public string SupportingText { get; init; } = "";
    public CommercialGoalProductContributionPresentationState State { get; init; }
    public string QualityText { get; init; } = "";
    public string QualityExplanation { get; init; } = "";
    public bool ShowEstimatedBadge { get; init; }
    public string EmptyText { get; init; } = "";
    public bool IsEmpty { get; init; }

    public string UnattributedRevenueTitle { get; init; } = "";
    public string UnattributedRevenueText { get; init; } = "";
    public string UnattributedRevenueExplanation { get; init; } = "";
    public bool HasUnattributedRevenue { get; init; }

    public string UnattributedGrossProfitTitle { get; init; } = "";
    public string UnattributedGrossProfitText { get; init; } = "";
    public string UnattributedGrossProfitExplanation { get; init; } = "";
    public bool HasUnattributedGrossProfit { get; init; }

    public string TopContributorsTitle { get; init; } =
        CommercialGoalProductContributionPresentation.TopContributorsTitle;

    public IReadOnlyList<CommercialGoalProductContributionItemPresentation> Rows { get; init; } = [];
    public IReadOnlyList<CommercialGoalProductContributionItemPresentation> TopContributors { get; init; } = [];
    public IReadOnlyList<CommercialGoalLimitationPresentation> Limitations { get; init; } = [];
}

/// <summary>
/// Apresentação PT-BR 71B-B8C. Histórico realizado. Sem SQL, I/O, recálculo ou causalidade.
/// </summary>
public static class CommercialGoalProductContributionPresentation
{
    public const int ExpectedQueryCount = 0;
    public const int TopContributorCount = 5;

    public const string SectionTitle = "Contribuição para o lucro";
    public const string DefaultSupporting =
        "Veja quais produtos mais contribuíram para o lucro bruto realizado na competência.";
    public const string TopContributorsTitle = "Principais contribuições realizadas";

    public const string HeadlineHistorical = "Contribuição para o lucro bruto realizado";
    public const string HeadlineEmpty = "Sem contribuição por produto nesta competência";
    public const string HeadlineUnattributedOnly =
        "Há receita na competência, mas ela não possui produtos associados para compor o ranking.";
    public const string HeadlineUnavailable = "Contribuição de lucro indisponível";

    public const string EmptyNoRows =
        "Não há vendas de produtos disponíveis para compor esta visão.";
    public const string EmptyUnattributed =
        "Há receita na competência, mas ela não possui produtos associados para compor o ranking.";
    public const string SupportingUnavailable =
        "A receita por produto está disponível, mas faltam dados de custo para calcular com segurança a contribuição de lucro bruto.";

    public const string EstimatedBadge = "Valores estimados";
    public const string EstimatedExplanation =
        "Parte dos custos históricos foi estimada porque nem todas as vendas possuem custo registrado no momento da operação.";
    public const string HistoricalQualityText = "Custos históricos disponíveis";
    public const string HistoricalQualityExplanation =
        "Os custos utilizados foram registrados no momento das vendas.";

    public const string QualityExact = "Histórico";
    public const string QualityEstimated = "Estimado";
    public const string QualityUnavailable = "Indisponível";
    public const string QualityExactExplanation =
        "Os custos utilizados foram registrados no momento das vendas.";
    public const string QualityEstimatedExplanation =
        "Parte dos custos históricos não estava registrada na venda e foi estimada com base no custo cadastrado disponível.";
    public const string QualityUnavailableExplanation =
        "Não há dados de custo suficientes para calcular com segurança o lucro bruto deste produto/período.";

    public const string RevenueLabel = "Receita atribuída";
    public const string RevenueExplanation =
        "Parcela da receita líquida da competência atribuída a este produto.";
    public const string CogsLabel = "CMV";
    public const string CogsExplanation =
        "Custo das mercadorias vendidas associado às vendas deste produto.";
    public const string GrossProfitLabel = "Lucro bruto";
    public const string GrossProfitExplanation =
        "Receita atribuída menos o custo das mercadorias vendidas.";
    public const string MarginLabel = "Margem bruta";
    public const string MarginExplanation =
        "Percentual de lucro bruto sobre a receita atribuída ao produto.";
    public const string ShareLabel = "Participação no lucro bruto realizado";
    public const string ShareExplanation =
        "Participação deste produto no lucro bruto realizado da competência.";
    public const string ShareHiddenNegativeTotal =
        "A participação percentual não é exibida porque o lucro bruto total da competência é negativo.";

    public const string IndicatorNegativeGp = "Lucro bruto negativo";
    public const string IndicatorNegativeGpExplanation =
        "Neste período, o custo associado às vendas deste produto superou a receita atribuída.";
    public const string IndicatorZeroGp =
        "Sem contribuição positiva de lucro bruto nesta competência.";

    public const string UnattributedRevenueTitle = "Receita não atribuída a produtos";
    public const string UnattributedRevenueExplanation =
        "Há receita registrada na competência sem itens de produto associados. Ela permanece incluída no total da Meta Comercial, mas não pode ser atribuída com segurança a um produto.";
    public const string UnattributedGpTitle = "Lucro bruto não atribuído";
    public const string UnattributedGpExplanation =
        "Parcela do lucro bruto realizado que não possui produto associado no registro da venda.";

    public const string LimitationExchangesTitle = "Trocas e devoluções ainda não ajustam estes valores.";
    public const string LimitationExchangesBody =
        "Esta visão segue a mesma regra atual da Meta Comercial e da DRE. Trocas e devoluções não são descontadas separadamente nesta versão.";
    public const string LimitationKitTitle = "Kit exibido no produto vendido";
    public const string LimitationKitBody =
        "Para kits/composições, a contribuição permanece associada ao produto vendido. A composição histórica dos componentes não é reconstruída.";
    public const string LimitationUnattributedTitle = "Há receita sem produto associado";
    public const string LimitationUnattributedBody =
        "Parte da receita da competência não possui item de produto associado e por isso não aparece no ranking por produto.";
    public const string LimitationShareNegativeTitle = "Participação percentual oculta";

    public static CommercialGoalProductContributionPresentationSnapshot Apply(
        CommercialGoalProductContributionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var hideShare = ShouldHideShare(snapshot);
        var rows = new CommercialGoalProductContributionItemPresentation[snapshot.Rows.Count];
        for (var i = 0; i < snapshot.Rows.Count; i++)
            rows[i] = PresentRow(snapshot.Rows[i], i + 1, hideShare, snapshot.GrossProfitAvailable);

        var top = TakeTopCalculable(rows);
        var state = ResolveState(snapshot);
        var (headline, supporting, empty) = PresentCopy(state);
        var (qualityText, qualityExplanation, estimatedBadge) = PresentPeriodQuality(snapshot, state);
        var limitations = PresentLimitations(snapshot, hideShare);

        var hasUnattrRev = snapshot.UnattributedRevenue != 0m;
        var hasUnattrGp = snapshot.GrossProfitAvailable
            && snapshot.UnattributedGrossProfit is { } ugp
            && ugp != 0m;

        return new CommercialGoalProductContributionPresentationSnapshot
        {
            Competence = snapshot.Competence,
            QueryCount = ExpectedQueryCount,
            SectionTitle = SectionTitle,
            Headline = headline,
            SupportingText = supporting,
            State = state,
            QualityText = qualityText,
            QualityExplanation = qualityExplanation,
            ShowEstimatedBadge = estimatedBadge,
            EmptyText = empty,
            IsEmpty = rows.Length == 0,
            UnattributedRevenueTitle = hasUnattrRev ? UnattributedRevenueTitle : "",
            UnattributedRevenueText = hasUnattrRev
                ? CommercialGoalPresentation.FormatMoney(snapshot.UnattributedRevenue)
                : "",
            UnattributedRevenueExplanation = hasUnattrRev ? UnattributedRevenueExplanation : "",
            HasUnattributedRevenue = hasUnattrRev,
            UnattributedGrossProfitTitle = hasUnattrGp ? UnattributedGpTitle : "",
            UnattributedGrossProfitText = hasUnattrGp
                ? CommercialGoalPresentation.FormatMoney(snapshot.UnattributedGrossProfit)
                : "",
            UnattributedGrossProfitExplanation = hasUnattrGp ? UnattributedGpExplanation : "",
            HasUnattributedGrossProfit = hasUnattrGp,
            TopContributorsTitle = TopContributorsTitle,
            Rows = rows,
            TopContributors = top,
            Limitations = limitations,
        };
    }

    static bool ShouldHideShare(CommercialGoalProductContributionSnapshot snapshot)
    {
        if (!snapshot.GrossProfitAvailable || snapshot.GrossProfit is not decimal gp)
            return true;
        return gp <= 0m;
    }

    static CommercialGoalProductContributionPresentationState ResolveState(
        CommercialGoalProductContributionSnapshot snapshot)
    {
        var hasRows = snapshot.Rows.Count > 0;
        if (!hasRows && snapshot.UnattributedRevenue != 0m)
            return CommercialGoalProductContributionPresentationState.UnattributedOnly;
        if (!hasRows)
            return CommercialGoalProductContributionPresentationState.Empty;
        if (!snapshot.GrossProfitAvailable)
            return CommercialGoalProductContributionPresentationState.ProfitUnavailable;
        if (snapshot.CostQuality == CommercialGoalCostQuality.EstimatedLegacy)
            return CommercialGoalProductContributionPresentationState.Estimated;
        return CommercialGoalProductContributionPresentationState.Historical;
    }

    static (string Headline, string Supporting, string Empty) PresentCopy(
        CommercialGoalProductContributionPresentationState state) =>
        state switch
        {
            CommercialGoalProductContributionPresentationState.Empty =>
                (HeadlineEmpty, DefaultSupporting, EmptyNoRows),
            CommercialGoalProductContributionPresentationState.UnattributedOnly =>
                (HeadlineUnattributedOnly, DefaultSupporting, EmptyUnattributed),
            CommercialGoalProductContributionPresentationState.ProfitUnavailable =>
                (HeadlineUnavailable, SupportingUnavailable, ""),
            _ => (HeadlineHistorical, DefaultSupporting, ""),
        };

    static (string Text, string Explanation, bool EstimatedBadge) PresentPeriodQuality(
        CommercialGoalProductContributionSnapshot snapshot,
        CommercialGoalProductContributionPresentationState state)
    {
        if (state is CommercialGoalProductContributionPresentationState.Empty
            or CommercialGoalProductContributionPresentationState.UnattributedOnly)
        {
            return ("", "", false);
        }

        if (!snapshot.GrossProfitAvailable
            || snapshot.CostQuality == CommercialGoalCostQuality.Unavailable)
        {
            return (QualityUnavailable, QualityUnavailableExplanation, false);
        }

        if (snapshot.CostQuality == CommercialGoalCostQuality.EstimatedLegacy)
            return (EstimatedBadge, EstimatedExplanation, true);

        return (HistoricalQualityText, HistoricalQualityExplanation, false);
    }

    static CommercialGoalProductContributionItemPresentation PresentRow(
        CommercialGoalProductContributionRow row,
        int rank,
        bool hideShare,
        bool periodGpAvailable)
    {
        var gpPublished = periodGpAvailable && row.GrossProfit.HasValue;
        var shareText = hideShare || !gpPublished
            ? CommercialGoalPresentation.EmDash
            : CommercialGoalPresentation.FormatPercent(row.GrossProfitShare);
        var gpText = gpPublished
            ? CommercialGoalPresentation.FormatMoney(row.GrossProfit)
            : CommercialGoalPresentation.EmDash;
        var marginText = gpPublished
            ? FormatMarginPercent(row.GrossMarginPercent)
            : CommercialGoalPresentation.EmDash;

        var (qualityText, qualityExplanation, qualityTone) = PresentQuality(row.CostQuality);
        var tone = ResolveRowTone(row, gpPublished, qualityTone);
        var indicators = PresentIndicators(row, gpPublished);
        var limitationItems = PresentRowLimitations(row);

        return new CommercialGoalProductContributionItemPresentation
        {
            Rank = rank,
            ProductId = row.ProductId,
            ProductCode = row.ProductCode,
            ProductName = row.ProductName,
            ProductTitle = ProductTitle(row),
            Revenue = row.Revenue,
            Cogs = row.Cogs,
            GrossProfit = row.GrossProfit,
            GrossMarginPercent = row.GrossMarginPercent,
            GrossProfitShare = row.GrossProfitShare,
            RevenueText = CommercialGoalPresentation.FormatMoney(row.Revenue),
            CogsText = CommercialGoalPresentation.FormatMoney(row.Cogs),
            GrossProfitText = gpText,
            GrossMarginText = marginText,
            GrossProfitShareText = shareText,
            CostQualityText = qualityText,
            CostQualityExplanation = qualityExplanation,
            CostQuality = row.CostQuality,
            Tone = tone,
            Limitations = row.Limitations,
            Indicators = indicators,
            LimitationItems = limitationItems,
        };
    }

    static string FormatMarginPercent(decimal? percent)
    {
        if (percent is not decimal value)
            return CommercialGoalPresentation.EmDash;
        return value.ToString("N2", ProductPriceHelper.Br) + "%";
    }

    static (string Text, string Explanation, CommercialGoalPresentationTone Tone) PresentQuality(
        CommercialGoalCostQuality quality) =>
        quality switch
        {
            CommercialGoalCostQuality.EstimatedLegacy =>
                (QualityEstimated, QualityEstimatedExplanation, CommercialGoalPresentationTone.Attention),
            CommercialGoalCostQuality.Unavailable =>
                (QualityUnavailable, QualityUnavailableExplanation, CommercialGoalPresentationTone.Unavailable),
            _ => (QualityExact, QualityExactExplanation, CommercialGoalPresentationTone.Neutral),
        };

    static CommercialGoalPresentationTone ResolveRowTone(
        CommercialGoalProductContributionRow row,
        bool gpPublished,
        CommercialGoalPresentationTone qualityTone)
    {
        if (row.CostQuality == CommercialGoalCostQuality.Unavailable || !gpPublished)
            return CommercialGoalPresentationTone.Unavailable;
        if (row.GrossProfit is { } gp && gp < 0m)
            return CommercialGoalPresentationTone.Warning;
        if (row.CostQuality == CommercialGoalCostQuality.EstimatedLegacy)
            return CommercialGoalPresentationTone.Attention;
        if (row.GrossProfit == 0m)
            return CommercialGoalPresentationTone.Neutral;
        return qualityTone;
    }

    static IReadOnlyList<string> PresentIndicators(
        CommercialGoalProductContributionRow row,
        bool gpPublished)
    {
        if (!gpPublished)
            return [];
        if (row.GrossProfit is { } gp && gp < 0m)
            return [IndicatorNegativeGp];
        if (row.GrossProfit == 0m)
            return [IndicatorZeroGp];
        return [];
    }

    static IReadOnlyList<CommercialGoalLimitationPresentation> PresentRowLimitations(
        CommercialGoalProductContributionRow row)
    {
        var list = new List<CommercialGoalLimitationPresentation>(2);
        if (row.HasLimitation(CommercialGoalProductContributionLimitation.HistoricalBomUnavailable))
        {
            list.Add(new CommercialGoalLimitationPresentation
            {
                Key = "kit",
                Title = LimitationKitTitle,
                Body = LimitationKitBody,
                IsProminent = false,
            });
        }

        return list;
    }

    static string ProductTitle(CommercialGoalProductContributionRow row)
    {
        if (row.ProductCode.Length > 0 && row.ProductName.Length > 0)
            return row.ProductCode + " — " + row.ProductName;
        if (row.ProductName.Length > 0)
            return row.ProductName;
        if (row.ProductCode.Length > 0)
            return row.ProductCode;
        return "#" + row.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    static IReadOnlyList<CommercialGoalProductContributionItemPresentation> TakeTopCalculable(
        CommercialGoalProductContributionItemPresentation[] rows)
    {
        var list = new List<CommercialGoalProductContributionItemPresentation>(TopContributorCount);
        for (var i = 0; i < rows.Length && list.Count < TopContributorCount; i++)
        {
            if (rows[i].GrossProfit.HasValue)
                list.Add(rows[i]);
        }

        return list;
    }

    static IReadOnlyList<CommercialGoalLimitationPresentation> PresentLimitations(
        CommercialGoalProductContributionSnapshot snapshot,
        bool hideShare)
    {
        var list = new List<CommercialGoalLimitationPresentation>(4);
        if (snapshot.HasLimitation(CommercialGoalProductContributionLimitation.ExchangesNotAdjusted))
        {
            list.Add(new CommercialGoalLimitationPresentation
            {
                Key = "exchanges",
                Title = LimitationExchangesTitle,
                Body = LimitationExchangesBody,
                IsProminent = false,
            });
        }

        if (snapshot.HasLimitation(CommercialGoalProductContributionLimitation.HasUnattributedRevenue)
            || snapshot.UnattributedRevenue != 0m)
        {
            list.Add(new CommercialGoalLimitationPresentation
            {
                Key = "unattributed",
                Title = LimitationUnattributedTitle,
                Body = LimitationUnattributedBody,
                IsProminent = true,
            });
        }

        if (snapshot.HasLimitation(CommercialGoalProductContributionLimitation.HistoricalBomUnavailable))
        {
            list.Add(new CommercialGoalLimitationPresentation
            {
                Key = "kit",
                Title = LimitationKitTitle,
                Body = LimitationKitBody,
                IsProminent = false,
            });
        }

        if (hideShare
            && snapshot.GrossProfitAvailable
            && snapshot.GrossProfit is { } gp
            && gp < 0m)
        {
            list.Add(new CommercialGoalLimitationPresentation
            {
                Key = "share-negative",
                Title = LimitationShareNegativeTitle,
                Body = ShareHiddenNegativeTotal,
                IsProminent = false,
            });
        }

        return list;
    }
}
