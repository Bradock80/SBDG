using System.IO;
using System.Text;
using SGDB.Domain.Commercial;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71B-B8C — apresentação PT-BR da contribuição por produto. Sem SQL, I/O ou recálculo.
/// </summary>
public class CommercialGoalProductContributionPresentationTests
{
    static readonly CommercialCompetence Sep2026 = CommercialCompetence.Create(2026, 9);

    static readonly string[] Forbidden =
    [
        "venda mais",
        "vender mais",
        "vai gerar",
        "irá gerar",
        "promova",
        "faça promoção",
        "para atingir a meta",
        "para bater a meta",
        "para fechar a meta",
        "deve vender",
        "venda agora",
        "produto recomendado",
        "produto campeão",
        "lucro líquido",
        "lucro incremental",
    ];

    [Fact]
    public void QueryCount_e_zero_e_titulo()
    {
        Assert.Equal(0, CommercialGoalProductContributionPresentation.ExpectedQueryCount);
        Assert.Equal("Contribuição para o lucro", CommercialGoalProductContributionPresentation.SectionTitle);
        var presented = Present(ExactSnapshot(Row(1, 100m, 60m, 40m, 40m, 1m)));
        Assert.Equal(0, presented.QueryCount);
    }

    [Fact]
    public void Apply_rejeita_nulo()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CommercialGoalProductContributionPresentation.Apply(null!));
    }

    [Fact]
    public void Exact_historico_discreto()
    {
        var presented = Present(ExactSnapshot(Row(1, 100m, 60m, 40m, 40m, 1m)));
        Assert.Equal(CommercialGoalProductContributionPresentationState.Historical, presented.State);
        Assert.Equal(CommercialGoalProductContributionPresentation.QualityExact, presented.Rows[0].CostQualityText);
        Assert.Equal(
            CommercialGoalProductContributionPresentation.QualityExactExplanation,
            presented.Rows[0].CostQualityExplanation);
        Assert.Equal(
            CommercialGoalProductContributionPresentation.HistoricalQualityText,
            presented.QualityText);
        Assert.False(presented.ShowEstimatedBadge);
        Assert.Equal(CommercialGoalPresentationTone.Neutral, presented.Rows[0].Tone);
    }

    [Fact]
    public void EstimatedLegacy_visivel()
    {
        var row = Row(1, 100m, 60m, 40m, 40m, 1m, CommercialGoalCostQuality.EstimatedLegacy);
        var presented = Present(Snapshot(
            CommercialGoalCostQuality.EstimatedLegacy, 100m, 60m, 40m, [row]));
        Assert.Equal(CommercialGoalProductContributionPresentationState.Estimated, presented.State);
        Assert.True(presented.ShowEstimatedBadge);
        Assert.Equal(CommercialGoalProductContributionPresentation.EstimatedBadge, presented.QualityText);
        Assert.Contains("estimada", presented.QualityExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CommercialGoalProductContributionPresentation.QualityEstimated, presented.Rows[0].CostQualityText);
        Assert.Equal(CommercialGoalPresentationTone.Attention, presented.Rows[0].Tone);
        Assert.NotEqual(CommercialGoalPresentation.EmDash, presented.Rows[0].GrossProfitText);
    }

    [Fact]
    public void Unavailable_nao_vira_zero()
    {
        var row = Row(1, 100m, 0m, gp: null, margin: null, share: null, CommercialGoalCostQuality.Unavailable);
        var presented = Present(Snapshot(
            CommercialGoalCostQuality.Unavailable, 100m, 0m, gp: null, [row], gpAvailable: false));
        Assert.Equal(CommercialGoalProductContributionPresentationState.ProfitUnavailable, presented.State);
        Assert.Equal(CommercialGoalProductContributionPresentation.HeadlineUnavailable, presented.Headline);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Rows[0].GrossProfitText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Rows[0].GrossMarginText);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Rows[0].GrossProfitShareText);
        Assert.DoesNotContain("R$ 0", presented.Rows[0].GrossProfitText, StringComparison.Ordinal);
        Assert.Equal(CommercialGoalProductContributionPresentation.QualityUnavailable, presented.Rows[0].CostQualityText);
        Assert.Equal("R$ 100,00", presented.Rows[0].RevenueText);
        Assert.Equal(CommercialGoalPresentationTone.Unavailable, presented.Rows[0].Tone);
        Assert.Empty(presented.TopContributors);
    }

    [Fact]
    public void MoneyBr_receita_cmv_gp()
    {
        var presented = Present(ExactSnapshot(Row(1, 1245.80m, 800.10m, 445.70m, 35.78m, 1m)));
        Assert.Equal("R$ 1.245,80", presented.Rows[0].RevenueText);
        Assert.Equal("R$ 800,10", presented.Rows[0].CogsText);
        Assert.Equal("R$ 445,70", presented.Rows[0].GrossProfitText);
    }

    [Fact]
    public void Gp_negativo_preservado()
    {
        var presented = Present(ExactSnapshot(Row(1, 10m, 45.20m, -35.20m, -352m, 1m)));
        Assert.Equal(CommercialGoalPresentation.FormatMoney(-35.20m), presented.Rows[0].GrossProfitText);
        Assert.Contains("-", presented.Rows[0].GrossProfitText, StringComparison.Ordinal);
        Assert.Contains(CommercialGoalProductContributionPresentation.IndicatorNegativeGp, presented.Rows[0].Indicators);
        Assert.Equal(CommercialGoalPresentationTone.Warning, presented.Rows[0].Tone);
        Assert.DoesNotContain("Pare de vender", string.Join(' ', presented.Rows[0].Indicators), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gp_zero()
    {
        var presented = Present(ExactSnapshot(Row(1, 10m, 10m, 0m, 0m, share: null)));
        Assert.Equal("R$ 0,00", presented.Rows[0].GrossProfitText);
        Assert.Contains(CommercialGoalProductContributionPresentation.IndicatorZeroGp, presented.Rows[0].Indicators);
        Assert.Equal(CommercialGoalPresentationTone.Neutral, presented.Rows[0].Tone);
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Rows[0].GrossProfitShareText);
    }

    [Fact]
    public void Margem_positiva_negativa_e_null()
    {
        var pos = Present(ExactSnapshot(Row(1, 100m, 60m, 40m, 40m, 1m)));
        Assert.Equal("40,00%", pos.Rows[0].GrossMarginText);

        var neg = Present(ExactSnapshot(Row(1, 10m, 15m, -5m, -50m, 1m)));
        Assert.Equal("-50,00%", neg.Rows[0].GrossMarginText);

        var undefined = Present(ExactSnapshot(Row(1, 0m, 8m, -8m, margin: null, share: null)));
        Assert.Equal(CommercialGoalPresentation.EmDash, undefined.Rows[0].GrossMarginText);
        Assert.NotEqual("0%", undefined.Rows[0].GrossMarginText);
        Assert.NotEqual("0,00%", undefined.Rows[0].GrossMarginText);
    }

    [Fact]
    public void Share_positivo_e_oculto_quando_total_zero()
    {
        var pos = Present(ExactSnapshot(Row(1, 100m, 60m, 40m, 40m, 0.54m)));
        Assert.Equal("54,00%", pos.Rows[0].GrossProfitShareText);

        var zero = Present(Snapshot(
            CommercialGoalCostQuality.Exact, 10m, 10m, 0m,
            [Row(1, 10m, 10m, 0m, 0m, share: null)]));
        Assert.Equal(CommercialGoalPresentation.EmDash, zero.Rows[0].GrossProfitShareText);
    }

    [Fact]
    public void Share_oculto_quando_gp_total_negativo()
    {
        var row = Row(1, 5m, 10m, -5m, -100m, 0.50m);
        var presented = Present(Snapshot(
            CommercialGoalCostQuality.Exact, 5m, 10m, -5m, [row]));
        Assert.Equal(CommercialGoalPresentation.EmDash, presented.Rows[0].GrossProfitShareText);
        Assert.Contains(
            presented.Limitations,
            l => l.Body == CommercialGoalProductContributionPresentation.ShareHiddenNegativeTotal);
        Assert.Equal(CommercialGoalPresentation.FormatMoney(-5m), presented.Rows[0].GrossProfitText);
    }

    [Fact]
    public void Receita_e_gp_nao_atribuidos()
    {
        var snap = Snapshot(
            CommercialGoalCostQuality.Exact, 12.50m, 0m, 12.50m, [],
            unattributedRevenue: 12.50m,
            unattributedGp: 12.50m,
            extraFlags: CommercialGoalProductContributionLimitation.HasUnattributedRevenue);
        var presented = Present(snap);
        Assert.True(presented.HasUnattributedRevenue);
        Assert.True(presented.HasUnattributedGrossProfit);
        Assert.Equal(CommercialGoalProductContributionPresentation.UnattributedRevenueTitle, presented.UnattributedRevenueTitle);
        Assert.Equal("R$ 12,50", presented.UnattributedRevenueText);
        Assert.Equal("R$ 12,50", presented.UnattributedGrossProfitText);
        Assert.Contains(presented.Limitations, l => l.Key == "unattributed");
        Assert.Equal(CommercialGoalProductContributionPresentationState.UnattributedOnly, presented.State);
        Assert.Equal(CommercialGoalProductContributionPresentation.EmptyUnattributed, presented.EmptyText);
    }

    [Fact]
    public void Exchanges_e_kit()
    {
        var row = Row(3, 20m, 6m, 14m, 70m, 1m);
        row = Clone(row, CommercialGoalProductContributionLimitation.HistoricalBomUnavailable);
        var snap = ExactSnapshot(
            CommercialGoalProductContributionLimitation.HistoricalBomUnavailable,
            row);
        var presented = Present(snap);
        Assert.Contains(presented.Limitations, l => l.Title == CommercialGoalProductContributionPresentation.LimitationExchangesTitle);
        Assert.Contains(presented.Limitations, l => l.Title == CommercialGoalProductContributionPresentation.LimitationKitTitle);
        Assert.Contains(presented.Rows[0].LimitationItems, l => l.Key == "kit");
        Assert.DoesNotContain("BOM", presented.Limitations[0].Title, StringComparison.Ordinal);
        Assert.DoesNotContain("BOM", string.Join(' ', presented.Limitations.Select(l => l.Title + l.Body)), StringComparison.Ordinal);
    }

    [Fact]
    public void Multiplas_limitacoes()
    {
        var presented = Present(Snapshot(
            CommercialGoalCostQuality.Exact, 20m, 0m, 20m, [],
            unattributedRevenue: 20m,
            unattributedGp: 20m,
            extraFlags: CommercialGoalProductContributionLimitation.HasUnattributedRevenue
                | CommercialGoalProductContributionLimitation.HistoricalBomUnavailable));
        Assert.True(presented.Limitations.Count >= 3);
    }

    [Fact]
    public void Rows_vazio()
    {
        var presented = Present(ExactSnapshot());
        Assert.True(presented.IsEmpty);
        Assert.Equal(CommercialGoalProductContributionPresentationState.Empty, presented.State);
        Assert.Equal(CommercialGoalProductContributionPresentation.HeadlineEmpty, presented.Headline);
        Assert.Equal(CommercialGoalProductContributionPresentation.EmptyNoRows, presented.EmptyText);
        Assert.Empty(presented.Rows);
    }

    [Fact]
    public void Ranking_preservado_e_top5_nao_reordena()
    {
        var lowMarginHighGp = Row(20, 5000m, 4000m, 1000m, 20m, 0.99m);
        var highMarginLowGp = Row(10, 20m, 10m, 10m, 50m, 0.01m);
        var presented = Present(ExactSnapshot(lowMarginHighGp, highMarginLowGp));
        Assert.Equal(20, presented.Rows[0].ProductId);
        Assert.Equal(10, presented.Rows[1].ProductId);
        Assert.Equal(1, presented.Rows[0].Rank);
        Assert.Equal(2, presented.Rows[1].Rank);
        Assert.Equal(20, presented.TopContributors[0].ProductId);
        Assert.Equal(10, presented.TopContributors[1].ProductId);
        Assert.True(presented.Rows[1].GrossMarginPercent > presented.Rows[0].GrossMarginPercent);
    }

    [Fact]
    public void Top5_preserva_ordem_e_maximo_cinco()
    {
        var rows = new CommercialGoalProductContributionRow[6];
        for (var i = 0; i < 6; i++)
            rows[i] = Row(i + 1, 100m - i, 10m, 90m - i, 10m, 0.1m);
        var presented = Present(ExactSnapshot(rows));
        Assert.Equal(6, presented.Rows.Count);
        Assert.Equal(5, presented.TopContributors.Count);
        for (var i = 0; i < 5; i++)
            Assert.Equal(i + 1, presented.TopContributors[i].ProductId);
        Assert.Equal(
            CommercialGoalProductContributionPresentation.TopContributorsTitle,
            presented.TopContributorsTitle);
    }

    [Fact]
    public void Linguagem_segura_e_sem_dependencia_de_inteligencia()
    {
        var src = File.ReadAllText(FindSource(
            "src", "SGDB.App", "Models", "CommercialGoalProductContributionPresentation.cs"));
        Assert.DoesNotContain("DatabaseService", src, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenConnection", src, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettings", src, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttention", src, StringComparison.Ordinal);
        Assert.DoesNotContain("PromotionSuggestion", src, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchaseGuidance", src, StringComparison.Ordinal);
        Assert.DoesNotContain("ComboIntelligence", src, StringComparison.Ordinal);
        Assert.DoesNotContain("70E", src, StringComparison.Ordinal);
        Assert.DoesNotContain("70F", src, StringComparison.Ordinal);
        Assert.DoesNotContain("70G", src, StringComparison.Ordinal);
        Assert.DoesNotContain("71A", src, StringComparison.Ordinal);

        var presented = Present(ExactSnapshot(
            Row(1, 100m, 60m, 40m, 40m, 1m, CommercialGoalCostQuality.EstimatedLegacy),
            Clone(Row(2, 10m, 20m, -10m, -100m, 0.2m), CommercialGoalProductContributionLimitation.HistoricalBomUnavailable)));
        var blob = Flatten(src) + Flatten(presented);
        foreach (var phrase in Forbidden)
            Assert.DoesNotContain(phrase, blob, StringComparison.OrdinalIgnoreCase);
    }

    static CommercialGoalProductContributionPresentationSnapshot Present(
        CommercialGoalProductContributionSnapshot snapshot) =>
        CommercialGoalProductContributionPresentation.Apply(snapshot);

    static CommercialGoalProductContributionSnapshot ExactSnapshot(
        params CommercialGoalProductContributionRow[] rows) =>
        ExactSnapshot(CommercialGoalProductContributionLimitation.None, rows);

    static CommercialGoalProductContributionSnapshot ExactSnapshot(
        CommercialGoalProductContributionLimitation extraFlags,
        params CommercialGoalProductContributionRow[] rows)
    {
        decimal rev = 0, cogs = 0, gp = 0;
        foreach (var row in rows)
        {
            rev += row.Revenue;
            cogs += row.Cogs;
            gp += row.GrossProfit ?? 0m;
        }

        return Snapshot(CommercialGoalCostQuality.Exact, rev, cogs, gp, rows, extraFlags: extraFlags);
    }

    static CommercialGoalProductContributionSnapshot Snapshot(
        CommercialGoalCostQuality quality,
        decimal revenue,
        decimal cogs,
        decimal? gp,
        IReadOnlyList<CommercialGoalProductContributionRow> rows,
        bool gpAvailable = true,
        decimal unattributedRevenue = 0m,
        decimal? unattributedGp = 0m,
        CommercialGoalProductContributionLimitation extraFlags = CommercialGoalProductContributionLimitation.None)
    {
        return new CommercialGoalProductContributionSnapshot
        {
            Competence = Sep2026,
            Revenue = revenue,
            UnattributedRevenue = unattributedRevenue,
            Cogs = cogs,
            UnattributedCogs = 0m,
            GrossProfit = gpAvailable ? gp : null,
            UnattributedGrossProfit = gpAvailable ? unattributedGp : null,
            CostQuality = quality,
            GrossProfitAvailable = gpAvailable,
            SaleCount = rows.Count,
            SaleItemCount = rows.Count,
            QueryCount = 1,
            Limitations = CommercialGoalProductContributionLimitation.ExchangesNotAdjusted | extraFlags,
            Rows = rows,
        };
    }

    static CommercialGoalProductContributionRow Row(
        int id,
        decimal revenue,
        decimal cogs,
        decimal? gp,
        decimal? margin,
        decimal? share,
        CommercialGoalCostQuality quality = CommercialGoalCostQuality.Exact) =>
        new()
        {
            ProductId = id,
            ProductCode = "P" + id,
            ProductName = "Prod " + id,
            Revenue = revenue,
            Cogs = cogs,
            GrossProfit = gp,
            GrossMarginPercent = margin,
            GrossProfitShare = share,
            CostQuality = quality,
        };

    static CommercialGoalProductContributionRow Clone(
        CommercialGoalProductContributionRow row,
        CommercialGoalProductContributionLimitation limitations) =>
        new()
        {
            ProductId = row.ProductId,
            ProductCode = row.ProductCode,
            ProductName = row.ProductName,
            Revenue = row.Revenue,
            Cogs = row.Cogs,
            GrossProfit = row.GrossProfit,
            GrossMarginPercent = row.GrossMarginPercent,
            GrossProfitShare = row.GrossProfitShare,
            CostQuality = row.CostQuality,
            Limitations = limitations,
        };

    static string Flatten(CommercialGoalProductContributionPresentationSnapshot presented)
    {
        var sb = new StringBuilder();
        sb.Append(presented.SectionTitle).Append(' ')
            .Append(presented.Headline).Append(' ')
            .Append(presented.SupportingText).Append(' ')
            .Append(presented.QualityText).Append(' ')
            .Append(presented.QualityExplanation).Append(' ')
            .Append(presented.EmptyText).Append(' ')
            .Append(presented.UnattributedRevenueTitle).Append(' ')
            .Append(presented.UnattributedRevenueExplanation).Append(' ')
            .Append(presented.UnattributedGrossProfitTitle).Append(' ')
            .Append(presented.UnattributedGrossProfitExplanation).Append(' ')
            .Append(presented.TopContributorsTitle);
        foreach (var lim in presented.Limitations)
            sb.Append(' ').Append(lim.Title).Append(' ').Append(lim.Body);
        foreach (var row in presented.Rows)
        {
            sb.Append(' ').Append(row.ProductTitle)
                .Append(' ').Append(row.CostQualityText)
                .Append(' ').Append(row.CostQualityExplanation)
                .Append(' ').Append(row.RevenueText)
                .Append(' ').Append(row.CogsText)
                .Append(' ').Append(row.GrossProfitText)
                .Append(' ').Append(row.GrossMarginText)
                .Append(' ').Append(row.GrossProfitShareText);
            foreach (var ind in row.Indicators)
                sb.Append(' ').Append(ind);
            foreach (var lim in row.LimitationItems)
                sb.Append(' ').Append(lim.Title).Append(' ').Append(lim.Body);
        }

        return sb.ToString();
    }

    static string Flatten(string src) => src;

    static string FindSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}
