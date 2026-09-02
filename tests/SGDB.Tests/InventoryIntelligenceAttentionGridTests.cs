using SGDB.Models;

namespace SGDB.Tests;

/// <summary>
/// 70E-B4B — join 70C+70D+70E na grade. Sem WPF, Load, SQL ou motor recalculado no filtro.
/// </summary>
public class InventoryIntelligenceAttentionGridTests
{
    [Fact]
    public void Join_is_by_product_id_not_list_position()
    {
        var giro = new[]
        {
            Giro(Turnover(10, "Dez")),
            Giro(Turnover(20, "Vinte")),
            Giro(Turnover(30, "Trinta")),
        };
        var projection = Snap70D(Proj(30, excess: 3), Proj(10, excess: 1), Proj(99, excess: 99));
        var attention = Snap70E(
            Attention(30, InventoryAttentionPriority.High, InventoryAttentionReason.ProjectedExcess30),
            Attention(10, InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired),
            Attention(99, InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired));

        var rows = InventoryIntelligenceProjectionPresentation.Combine(giro, projection, attention);

        Assert.Equal(new[] { 10, 20, 30 }, rows.Select(r => r.ProductId).ToArray());
        Assert.DoesNotContain(rows, r => r.ProductId == 99);
        Assert.Same(giro[0], rows[0].Intelligence);
        Assert.Same(giro[1], rows[1].Intelligence);
        Assert.Same(giro[2], rows[2].Intelligence);

        Assert.Equal("Crítica", rows[0].PriorityDisplay);
        Assert.Equal("Produto vencido", rows[0].PrimaryReasonDisplay);
        Assert.Equal(InventoryAttentionPriority.Critical, rows[0].Priority);
        Assert.Null(rows[1].Attention);
        Assert.Equal(InventoryAttentionPresentation.MissingPriorityDisplay, rows[1].PriorityDisplay);
        Assert.Equal(InventoryAttentionPresentation.MissingReasonDisplay, rows[1].PrimaryReasonDisplay);
        Assert.Equal("Alta", rows[2].PriorityDisplay);
        Assert.Equal("Sobra projetada em 30 dias", rows[2].PrimaryReasonDisplay);
    }

    [Fact]
    public void Extra_70e_does_not_create_ghost_product()
    {
        var rows = InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1, "Um"))],
            Snap70D(Proj(1)),
            Snap70E(
                Attention(1, InventoryAttentionPriority.Normal, InventoryAttentionReason.None),
                Attention(8, InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired),
                Attention(9, InventoryAttentionPriority.High, InventoryAttentionReason.ExpiresToday)));

        var row = Assert.Single(rows);
        Assert.Equal(1, row.ProductId);
        Assert.Equal("Normal", row.PriorityDisplay);
        Assert.DoesNotContain(rows, r => r.ProductId is 8 or 9);
    }

    [Fact]
    public void Missing_70e_is_not_reliable_normal()
    {
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1, "Sem 70E"))],
            Snap70D(Proj(1, excess: 0))));

        Assert.Null(row.Attention);
        Assert.Null(row.Priority);
        Assert.Null(row.PrimaryReason);
        Assert.Equal("—", row.PriorityDisplay);
        Assert.Equal("Análise indisponível", row.PrimaryReasonDisplay);
        Assert.NotEqual("Normal", row.PriorityDisplay);
        Assert.NotEqual("Sem atenção", row.PrimaryReasonDisplay);
        Assert.NotEqual(InventoryAttentionPriority.Normal, row.Priority);
        Assert.False(row.Attention?.IsAllClear == true);
        Assert.Equal(InventoryAttentionPresentation.MissingPrioritySortKey, row.PrioritySortKey);
        Assert.True(row.PrioritySortKey > (int)InventoryAttentionPriority.Normal);
    }

    [Fact]
    public void Composer_projection_missing_is_classified_not_join_gap()
    {
        var presented = InventoryAttentionPresentation.FromResult(new InventoryAttentionResult
        {
            ProductId = 1,
            Priority = InventoryAttentionPriority.Low,
            Family = InventoryAttentionFamily.DataQuality,
            PrimaryReason = InventoryAttentionReason.ProjectionMissing,
            Action = InventoryOperatorAction.ReviewData,
            Confidence = InventoryAttentionConfidence.Unavailable,
        });
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))],
            Snap70D(),
            Snap70E(presented)));

        Assert.NotNull(row.Attention);
        Assert.Equal("Baixa", row.PriorityDisplay);
        Assert.Equal("Projeção indisponível", row.PrimaryReasonDisplay);
        Assert.Equal(InventoryAttentionPriority.Low, row.Priority);
        Assert.False(row.Attention.IsAllClear);
        Assert.NotEqual(InventoryAttentionPresentation.MissingPriorityDisplay, row.PriorityDisplay);
    }

    [Theory]
    [InlineData(InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired, "Crítica", "Produto vencido")]
    [InlineData(InventoryAttentionPriority.High, InventoryAttentionReason.ExpiresToday, "Alta", "Vence hoje")]
    [InlineData(InventoryAttentionPriority.Medium, InventoryAttentionReason.ProjectedExcess30, "Média", "Sobra projetada em 30 dias")]
    [InlineData(InventoryAttentionPriority.Low, InventoryAttentionReason.Idle, "Baixa", "Produto parado")]
    [InlineData(InventoryAttentionPriority.Normal, InventoryAttentionReason.None, "Normal", "Sem atenção")]
    public void Priority_and_reason_use_b3_labels(
        InventoryAttentionPriority priority,
        InventoryAttentionReason reason,
        string priorityText,
        string reasonText)
    {
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))],
            Snap70D(Proj(1)),
            Snap70E(Attention(1, priority, reason))));

        Assert.Equal(priorityText, row.PriorityDisplay);
        Assert.Equal(reasonText, row.PrimaryReasonDisplay);
        Assert.Equal(priority, row.Priority);
        Assert.Equal(reason, row.PrimaryReason);
        Assert.Equal((int)priority, row.PrioritySortKey);
    }

    [Fact]
    public void Priority_sort_uses_ordinal_not_ptbr_text()
    {
        var giro = new[]
        {
            Giro(Turnover(1, "Alta")),
            Giro(Turnover(2, "Baixa")),
            Giro(Turnover(3, "Critica")),
            Giro(Turnover(4, "Media")),
            Giro(Turnover(5, "Normal")),
            Giro(Turnover(6, "Ausente")),
        };
        var attention = Snap70E(
            Attention(1, InventoryAttentionPriority.High, InventoryAttentionReason.ExpiresToday),
            Attention(2, InventoryAttentionPriority.Low, InventoryAttentionReason.Idle),
            Attention(3, InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired),
            Attention(4, InventoryAttentionPriority.Medium, InventoryAttentionReason.ProjectedExcess30),
            Attention(5, InventoryAttentionPriority.Normal, InventoryAttentionReason.None));

        var rows = InventoryIntelligenceProjectionPresentation.Combine(giro, Snap70D(), attention);
        var byText = rows.OrderBy(r => r.PriorityDisplay, StringComparer.Ordinal).Select(r => r.ProductId).ToArray();
        var byKey = rows.OrderBy(r => r.PrioritySortKey).Select(r => r.ProductId).ToArray();

        Assert.Equal(new[] { 3, 1, 4, 2, 5, 6 }, byKey);
        Assert.NotEqual(byText, byKey);
        Assert.Equal("Alta", rows[0].PriorityDisplay);
        Assert.True(StringComparer.Ordinal.Compare("Alta", "Crítica") < 0);
        Assert.True(rows.Single(r => r.ProductId == 3).PrioritySortKey
            < rows.Single(r => r.ProductId == 1).PrioritySortKey);
    }

    [Fact]
    public void Duplicate_attention_product_id_is_unavailable_not_last_wins()
    {
        var first = Attention(1, InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired);
        var second = Attention(1, InventoryAttentionPriority.Low, InventoryAttentionReason.Idle);
        var snapshot = new InventoryAttentionPresentationSnapshot
        {
            Rows = [first, second],
        };

        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))],
            Snap70D(Proj(1)),
            snapshot));

        Assert.Null(row.Attention);
        Assert.Equal("—", row.PriorityDisplay);
        Assert.Equal("Análise indisponível", row.PrimaryReasonDisplay);
        Assert.NotEqual("Crítica", row.PriorityDisplay);
        Assert.NotEqual("Baixa", row.PriorityDisplay);
    }

    [Fact]
    public void Existing_70c_filters_still_apply_and_keep_attention()
    {
        var rows = new[]
        {
            Turnover(2, "Beta", InventoryCoverageBand.Normal),
            Turnover(1, "Alfa", InventoryCoverageBand.Critical, coverageDays: 2, stock: 2, total: 2, vmv30: 1),
            Turnover(3, "Gama", InventoryCoverageBand.Critical, coverageDays: 1, stock: 1, total: 1, vmv30: 1),
        };
        var presented = Snap70D(Proj(1, excess: 4), Proj(2, excess: 9), Proj(3, excess: 1));
        var attention = Snap70E(
            Attention(1, InventoryAttentionPriority.High, InventoryAttentionReason.NearExpiryWithoutSurplus),
            Attention(2, InventoryAttentionPriority.Normal, InventoryAttentionReason.None),
            Attention(3, InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired));

        var filtered = InventoryIntelligenceProjectionPresentation.Apply(
            rows,
            new InventoryIntelligenceUiFilter { Card = InventoryIntelligenceCardKind.Critical },
            presented,
            attention);

        Assert.Equal(new[] { 1, 3 }, filtered.Select(r => r.ProductId).ToArray());
        Assert.Equal("Alta", filtered[0].PriorityDisplay);
        Assert.Equal("Crítica", filtered[1].PriorityDisplay);
        Assert.DoesNotContain(filtered, r => r.ProductId == 2);
    }

    [Fact]
    public void Cards_remain_70c_coverage_counts()
    {
        var rows = new[]
        {
            Turnover(1, "Critico", InventoryCoverageBand.Critical, coverageDays: 2, stock: 2, total: 2, vmv30: 1),
            Turnover(2, "Normal", InventoryCoverageBand.Normal),
        };
        var cards = InventoryIntelligencePresentation.CountCards(rows);
        Assert.Equal(new[]
        {
            "Todos",
            "Sem estoque",
            "Sem estoque + giro recente",
            "Crítica ≤ 3 dias",
            "Baixa 3–7 dias",
            "Parados 90+",
            "Conferir estoque",
        }, InventoryIntelligencePresentation.Cards.Select(c => c.Title).ToArray());
        Assert.Equal(2, cards.All);
        Assert.Equal(1, cards.Critical);
        Assert.Equal(0, cards.Idle);
    }

    [Fact]
    public void Grid_row_does_not_flatten_detail_only_fields()
    {
        var attention = Attention(1, InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired);
        var row = Assert.Single(InventoryIntelligenceProjectionPresentation.Combine(
            [Giro(Turnover(1))],
            Snap70D(Proj(1)),
            Snap70E(attention)));

        Assert.Equal(attention.ActionDisplay, row.Attention!.ActionDisplay);
        Assert.Equal(attention.Explanation, row.Attention.Explanation);
        Assert.Equal(attention.ConfidenceDisplay, row.Attention.ConfidenceDisplay);
        Assert.Equal(attention.FamilyDisplay, row.Attention.FamilyDisplay);

        var type = typeof(InventoryIntelligenceProjectionGridRow);
        Assert.Null(type.GetProperty("ActionDisplay"));
        Assert.Null(type.GetProperty("Explanation"));
        Assert.Null(type.GetProperty("ConfidenceDisplay"));
        Assert.Null(type.GetProperty("FamilyDisplay"));
        Assert.Null(type.GetProperty("SecondaryReasonDisplays"));
        Assert.NotNull(type.GetProperty("PriorityDisplay"));
        Assert.NotNull(type.GetProperty("PrimaryReasonDisplay"));
        Assert.NotNull(type.GetProperty("PrioritySortKey"));
    }

    private static ProductTurnoverRow Turnover(
        int id,
        string name = "Produto",
        InventoryCoverageBand band = InventoryCoverageBand.Normal,
        double? coverageDays = 20,
        double stock = 10,
        double total = 10,
        double vmv30 = 1) =>
        new()
        {
            ProductId = id,
            Name = name,
            Code = "P" + id,
            Stock = stock,
            StockFridge = 0,
            TotalStock = total,
            Vmv30 = vmv30,
            CoverageDays = coverageDays,
            CoverageBand = band,
            HistoryDays = 45,
            HasPhysicalAvailabilityEvidence = true,
        };

    private static InventoryIntelligenceGridRow Giro(ProductTurnoverRow row) =>
        InventoryIntelligencePresentation.ToGridRow(row);

    private static InventoryProjectedProductPresentation Proj(int id, double? excess = 0) =>
        InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = id,
            Projection = new InventoryProjectionResult
            {
                HorizonDays = 30,
                ProjectedDemand = 30,
                ProjectedExcessQuantity = excess,
                Lots = [],
            },
        });

    private static InventoryProjectionPresentationSnapshot Snap70D(
        params InventoryProjectedProductPresentation[] products)
    {
        var map = new Dictionary<int, InventoryProjectedProductPresentation>();
        foreach (var p in products)
        {
            if (!map.ContainsKey(p.ProductId))
                map[p.ProductId] = p;
        }

        return new InventoryProjectionPresentationSnapshot
        {
            Products = products,
            ByProductId = map,
        };
    }

    private static InventoryAttentionPresentationRow Attention(
        int id,
        InventoryAttentionPriority priority,
        InventoryAttentionReason reason) =>
        InventoryAttentionPresentation.FromResult(new InventoryAttentionResult
        {
            ProductId = id,
            Priority = priority,
            PrimaryReason = reason,
            Family = reason switch
            {
                InventoryAttentionReason.None => InventoryAttentionFamily.Normal,
                InventoryAttentionReason.Expired or InventoryAttentionReason.ExpiresToday
                    or InventoryAttentionReason.NearExpiryWithoutSurplus => InventoryAttentionFamily.Expiry,
                InventoryAttentionReason.ProjectedExcess30 => InventoryAttentionFamily.Excess,
                InventoryAttentionReason.Idle => InventoryAttentionFamily.Turnover,
                InventoryAttentionReason.ProjectionMissing => InventoryAttentionFamily.DataQuality,
                _ => InventoryAttentionFamily.Turnover,
            },
            Action = reason switch
            {
                InventoryAttentionReason.None => InventoryOperatorAction.None,
                InventoryAttentionReason.Expired => InventoryOperatorAction.RemoveExpired,
                InventoryAttentionReason.ExpiresToday or InventoryAttentionReason.NearExpiryWithoutSurplus =>
                    InventoryOperatorAction.PrioritizeSale,
                InventoryAttentionReason.ProjectedExcess30 => InventoryOperatorAction.EvaluateExcess,
                InventoryAttentionReason.Idle => InventoryOperatorAction.Monitor,
                _ => InventoryOperatorAction.ReviewData,
            },
            Confidence = InventoryAttentionConfidence.Reliable,
        });

    private static InventoryAttentionPresentationSnapshot Snap70E(
        params InventoryAttentionPresentationRow[] rows)
    {
        var map = new Dictionary<int, InventoryAttentionPresentationRow>();
        foreach (var row in rows)
            map.TryAdd(row.ProductId, row);

        return new InventoryAttentionPresentationSnapshot
        {
            Rows = rows,
            ByProductId = map,
        };
    }
}
