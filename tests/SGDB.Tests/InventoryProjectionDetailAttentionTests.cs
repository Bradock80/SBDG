using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70E-B4C — detalhe usa presentation 70E já carregada. Sem WPF, SQL, Composer no lookup.
/// </summary>
public class InventoryProjectionDetailAttentionTests
{
    [Theory]
    [InlineData(InventoryAttentionPriority.Critical, "Crítica")]
    [InlineData(InventoryAttentionPriority.High, "Alta")]
    [InlineData(InventoryAttentionPriority.Medium, "Média")]
    [InlineData(InventoryAttentionPriority.Low, "Baixa")]
    [InlineData(InventoryAttentionPriority.Normal, "Normal")]
    public void Detail_shows_b3_priority_text(InventoryAttentionPriority priority, string expected)
    {
        var attention = InventoryAttentionPresentation.FromResult(new InventoryAttentionResult
        {
            ProductId = 1,
            Priority = priority,
            PrimaryReason = priority == InventoryAttentionPriority.Normal
                ? InventoryAttentionReason.None
                : InventoryAttentionReason.Idle,
            Family = InventoryAttentionFamily.Turnover,
            Action = InventoryOperatorAction.Monitor,
            Confidence = InventoryAttentionConfidence.Reliable,
        });
        var detail = DetailWith(attention);
        Assert.Equal(expected, detail.Attention.PriorityDisplay);
        Assert.Same(attention, detail.Attention);
        Assert.False(detail.Attention.IsJoinMissing);
    }

    [Fact]
    public void Normal_reliable_is_all_clear_not_missing()
    {
        var row = InventoryAttentionPresentation.FromResult(new InventoryAttentionResult
        {
            ProductId = 1,
            Priority = InventoryAttentionPriority.Normal,
            PrimaryReason = InventoryAttentionReason.None,
            Family = InventoryAttentionFamily.Normal,
            Action = InventoryOperatorAction.None,
            Confidence = InventoryAttentionConfidence.Reliable,
        });
        var detail = DetailWith(row);
        Assert.Equal("Normal", detail.Attention.PriorityDisplay);
        Assert.Equal("Sem atenção", detail.Attention.PrimaryReasonDisplay);
        Assert.Equal("Nenhuma ação imediata", detail.Attention.ActionDisplay);
        Assert.Equal("Análise disponível", detail.Attention.ConfidenceDisplay);
        Assert.True(detail.Attention.IsAllClear);
        Assert.False(detail.Attention.IsJoinMissing);
        Assert.Contains("Não há atenção imediata", detail.Attention.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Normal_unavailable_is_not_all_clear_and_not_join_missing()
    {
        var row = InventoryAttentionPresentation.FromResult(new InventoryAttentionResult
        {
            ProductId = 1,
            Priority = InventoryAttentionPriority.Normal,
            PrimaryReason = InventoryAttentionReason.None,
            Family = InventoryAttentionFamily.Normal,
            Action = InventoryOperatorAction.None,
            Confidence = InventoryAttentionConfidence.Unavailable,
        });
        var detail = DetailWith(row);
        Assert.Equal("Normal", detail.Attention.PriorityDisplay);
        Assert.Equal("Sem recomendação", detail.Attention.ActionDisplay);
        Assert.Equal("Análise indisponível", detail.Attention.ConfidenceDisplay);
        Assert.False(detail.Attention.IsAllClear);
        Assert.False(detail.Attention.IsJoinMissing);
        Assert.NotEqual(InventoryAttentionPresentation.MissingPriorityDisplay, detail.Attention.PriorityDisplay);
        Assert.Contains("Não foi possível concluir", detail.Attention.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Expired_remove_expired()
    {
        var detail = DetailWith(Present(
            InventoryAttentionPriority.Critical,
            InventoryAttentionReason.Expired,
            InventoryOperatorAction.RemoveExpired));
        Assert.Equal("Produto vencido", detail.Attention.PrimaryReasonDisplay);
        Assert.Equal("Retirar / conferir", detail.Attention.ActionDisplay);
        Assert.Equal("Crítica", detail.Attention.PriorityDisplay);
        Assert.Contains("vencido", detail.Attention.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promoção", detail.Attention.ActionDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Expires_today_prioritize_sale()
    {
        var detail = DetailWith(Present(
            InventoryAttentionPriority.High,
            InventoryAttentionReason.ExpiresToday,
            InventoryOperatorAction.PrioritizeSale));
        Assert.Equal("Vence hoje", detail.Attention.PrimaryReasonDisplay);
        Assert.Equal("Priorizar saída", detail.Attention.ActionDisplay);
        Assert.DoesNotContain("desconto", detail.Attention.ActionDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projected_excess_evaluate_excess_is_not_promotion()
    {
        var detail = DetailWith(Present(
            InventoryAttentionPriority.Medium,
            InventoryAttentionReason.ProjectedExcess30,
            InventoryOperatorAction.EvaluateExcess));
        Assert.Equal("Sobra projetada em 30 dias", detail.Attention.PrimaryReasonDisplay);
        Assert.Equal("Avaliar excesso", detail.Attention.ActionDisplay);
        Assert.DoesNotContain("Promover", detail.Attention.ActionDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("combo", detail.Attention.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preço", detail.Attention.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Idle_monitor()
    {
        var detail = DetailWith(Present(
            InventoryAttentionPriority.Medium,
            InventoryAttentionReason.Idle,
            InventoryOperatorAction.Monitor));
        Assert.Equal("Produto parado", detail.Attention.PrimaryReasonDisplay);
        Assert.Equal("Monitorar", detail.Attention.ActionDisplay);
        Assert.NotEqual("Avaliar excesso", detail.Attention.ActionDisplay);
    }

    [Fact]
    public void Idle_plus_excess_keeps_excess_primary_from_engine()
    {
        var today = new DateTime(2026, 9, 1);
        var row = Turnover(1, stock: 100, vmv30: 1, idle: true, history: 90);
        var projection = InventoryProjectionEngine.Project(new InventoryProjectionRequest
        {
            Today = today,
            Vmv30 = 1,
            HistoryDays = 90,
            HasPhysicalAvailabilityEvidence = true,
            TotalStock = 100,
            WarehouseStock = 100,
            HorizonDays = 30,
            Lots = [new InventoryProjectionLotInput { LotId = 1, Quantity = 100, ExpiryDate = today.AddDays(120) }],
        });
        var result = InventoryAttentionEngine.Evaluate(row, new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = projection,
        });
        Assert.Equal(InventoryAttentionReason.ProjectedExcess30, result.PrimaryReason);
        Assert.Equal(new[] { InventoryAttentionReason.Idle }, result.SecondaryReasons);

        var presented = InventoryAttentionPresentation.FromResult(result);
        var detail = DetailWith(presented);
        Assert.Equal("Sobra projetada em 30 dias", detail.Attention.PrimaryReasonDisplay);
        Assert.Equal("Avaliar excesso", detail.Attention.ActionDisplay);
        Assert.Equal(new[] { "Produto parado" }, detail.Attention.SecondaryReasonDisplays.ToArray());
        Assert.Same(presented.SecondaryReasonDisplays, detail.Attention.SecondaryReasonDisplays);
    }

    [Fact]
    public void Undated_and_no_lot_review_data()
    {
        var undated = DetailWith(Present(
            InventoryAttentionPriority.Low,
            InventoryAttentionReason.Undated,
            InventoryOperatorAction.ReviewData));
        Assert.Equal("Sem validade informada", undated.Attention.PrimaryReasonDisplay);
        Assert.Equal("Revisar dados", undated.Attention.ActionDisplay);

        var noLot = DetailWith(Present(
            InventoryAttentionPriority.Low,
            InventoryAttentionReason.NoLot,
            InventoryOperatorAction.ReviewData));
        Assert.Equal("Sem lote identificado", noLot.Attention.PrimaryReasonDisplay);
        Assert.Equal("Revisar dados", noLot.Attention.ActionDisplay);
    }

    [Fact]
    public void Projection_missing_and_duplicate()
    {
        var missing = DetailWith(Present(
            InventoryAttentionPriority.Low,
            InventoryAttentionReason.ProjectionMissing,
            InventoryOperatorAction.ReviewData,
            InventoryAttentionConfidence.Unavailable));
        Assert.Equal("Projeção indisponível", missing.Attention.PrimaryReasonDisplay);
        Assert.Equal("Análise indisponível", missing.Attention.ConfidenceDisplay);
        Assert.False(missing.Attention.IsJoinMissing);

        var dup = DetailWith(Present(
            InventoryAttentionPriority.Critical,
            InventoryAttentionReason.DuplicateProjection,
            InventoryOperatorAction.ReviewData,
            InventoryAttentionConfidence.Unavailable));
        Assert.Equal("Projeção inconsistente", dup.Attention.PrimaryReasonDisplay);
        Assert.Equal("Crítica", dup.Attention.PriorityDisplay);
    }

    [Fact]
    public void Secondary_reasons_preserve_engine_order_and_hide_when_empty()
    {
        var withSecondary = InventoryAttentionPresentation.FromResult(new InventoryAttentionResult
        {
            ProductId = 1,
            Priority = InventoryAttentionPriority.Medium,
            Family = InventoryAttentionFamily.Excess,
            PrimaryReason = InventoryAttentionReason.ProjectedExcess30,
            Action = InventoryOperatorAction.EvaluateExcess,
            Confidence = InventoryAttentionConfidence.Reliable,
            SecondaryReasons = [InventoryAttentionReason.Idle, InventoryAttentionReason.Undated],
        });
        var detail = DetailWith(withSecondary);
        Assert.Equal(
            new[] { "Produto parado", "Sem validade informada" },
            detail.Attention.SecondaryReasonDisplays.ToArray());

        var empty = DetailWith(Present(
            InventoryAttentionPriority.Critical,
            InventoryAttentionReason.Expired,
            InventoryOperatorAction.RemoveExpired));
        Assert.Empty(empty.Attention.SecondaryReasonDisplays);
    }

    [Theory]
    [InlineData(InventoryAttentionConfidence.Reliable, "Análise disponível")]
    [InlineData(InventoryAttentionConfidence.Limited, "Análise com limitações")]
    [InlineData(InventoryAttentionConfidence.Unavailable, "Análise indisponível")]
    public void Confidence_uses_b3_text_not_percent(InventoryAttentionConfidence confidence, string expected)
    {
        var detail = DetailWith(Present(
            InventoryAttentionPriority.Low,
            InventoryAttentionReason.InsufficientHistory,
            InventoryOperatorAction.ReviewData,
            confidence));
        Assert.Equal(expected, detail.Attention.ConfidenceDisplay);
        Assert.DoesNotContain("%", detail.Attention.ConfidenceDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("95", detail.Attention.ConfidenceDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_product_id_is_safe_unavailable_not_normal()
    {
        var row = Turnover(1);
        var snap = Snapshot(row);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var extra = Snap70E(Present(
            InventoryAttentionPriority.Critical,
            InventoryAttentionReason.Expired,
            InventoryOperatorAction.RemoveExpired,
            id: 99));

        var missing = InventoryProjectionDetail.TryCreate(snap, presented, 1, extra);
        Assert.NotNull(missing);
        Assert.True(missing!.Attention.IsJoinMissing);
        Assert.Equal("—", missing.Attention.PriorityDisplay);
        Assert.Equal("Análise indisponível", missing.Attention.PrimaryReasonDisplay);
        Assert.Equal("Sem recomendação", missing.Attention.ActionDisplay);
        Assert.Equal("Análise indisponível", missing.Attention.ConfidenceDisplay);
        Assert.Equal(InventoryAttentionPresentation.MissingExplanation, missing.Attention.Explanation);
        Assert.False(missing.Attention.IsAllClear);
        Assert.NotEqual("Normal", missing.Attention.PriorityDisplay);
        Assert.NotEqual("Sem atenção", missing.Attention.PrimaryReasonDisplay);
        Assert.NotEqual("Nenhuma ação imediata", missing.Attention.ActionDisplay);
        Assert.Empty(missing.Attention.SecondaryReasonDisplays);

        Assert.Null(InventoryProjectionDetail.TryCreate(snap, presented, 99, extra));
    }

    [Fact]
    public void Lookup_is_by_product_id_not_list_position()
    {
        var rows = new[] { Turnover(10, "Dez"), Turnover(20, "Vinte"), Turnover(30, "Trinta") };
        var snap = Snapshot(rows);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var attention = Snap70E(
            Present(InventoryAttentionPriority.High, InventoryAttentionReason.ExpiresToday,
                InventoryOperatorAction.PrioritizeSale, id: 30),
            Present(InventoryAttentionPriority.Critical, InventoryAttentionReason.Expired,
                InventoryOperatorAction.RemoveExpired, id: 10));

        var first = InventoryProjectionDetail.TryCreate(snap, presented, 10, attention)!;
        var last = InventoryProjectionDetail.TryCreate(snap, presented, 30, attention)!;
        var middle = InventoryProjectionDetail.TryCreate(snap, presented, 20, attention)!;

        Assert.Equal("Crítica", first.Attention.PriorityDisplay);
        Assert.Equal("Produto vencido", first.Attention.PrimaryReasonDisplay);
        Assert.Equal("Alta", last.Attention.PriorityDisplay);
        Assert.Equal("Vence hoje", last.Attention.PrimaryReasonDisplay);
        Assert.True(middle.Attention.IsJoinMissing);
        Assert.Same(attention.ByProductId[10], first.Attention);
        Assert.Same(attention.ByProductId[30], last.Attention);
    }

    [Fact]
    public void TryCreate_without_attention_uses_missing_row()
    {
        var row = Turnover(1);
        var snap = Snapshot(row);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var detail = InventoryProjectionDetail.TryCreate(snap, presented, 1);
        Assert.NotNull(detail);
        Assert.True(detail!.Attention.IsJoinMissing);
        Assert.Equal("—", detail.Attention.PriorityDisplay);
    }

    [Fact]
    public void Resolve_and_try_create_have_no_engine_or_query()
    {
        var detailSrc = File.ReadAllText(FindSource("src", "SGDB.App", "Models", "InventoryProjectionDetail.cs"));
        Assert.Contains("ResolveForDetail(attention, productId)", detailSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionComposer", detailSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionEngine", detailSrc, StringComparison.Ordinal);
        Assert.DoesNotContain(".Load(", detailSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", detailSrc, StringComparison.OrdinalIgnoreCase);

        var resolveSrc = File.ReadAllText(FindSource("src", "SGDB.App", "Models", "InventoryAttentionPresentation.cs"));
        Assert.Contains("ByProductId", resolveSrc, StringComparison.Ordinal);
        Assert.Contains("TryGetValue(productId", resolveSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionEngine", resolveSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryAttentionComposer", resolveSrc, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_row_factory_is_centralized()
    {
        var row = InventoryAttentionPresentation.MissingRow(7);
        Assert.Equal(7, row.ProductId);
        Assert.True(row.IsJoinMissing);
        Assert.Equal(InventoryAttentionPresentation.MissingPriorityDisplay, row.PriorityDisplay);
        Assert.Equal(InventoryAttentionPresentation.ActionNoneUnavailable, row.ActionDisplay);
        Assert.Same(
            InventoryAttentionPresentation.MissingRow(1).Explanation,
            InventoryAttentionPresentation.ResolveForDetail(null, 1).Explanation);
    }

    private static InventoryProjectionDetail DetailWith(InventoryAttentionPresentationRow attention)
    {
        var row = Turnover(attention.ProductId);
        var snap = Snapshot(row);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var detail = InventoryProjectionDetail.TryCreate(snap, presented, row.ProductId, Snap70E(attention));
        Assert.NotNull(detail);
        return detail!;
    }

    private static InventoryAttentionPresentationRow Present(
        InventoryAttentionPriority priority,
        InventoryAttentionReason reason,
        InventoryOperatorAction action,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable,
        int id = 1) =>
        InventoryAttentionPresentation.FromResult(new InventoryAttentionResult
        {
            ProductId = id,
            Priority = priority,
            PrimaryReason = reason,
            Family = reason switch
            {
                InventoryAttentionReason.None => InventoryAttentionFamily.Normal,
                InventoryAttentionReason.Expired or InventoryAttentionReason.ExpiresToday =>
                    InventoryAttentionFamily.Expiry,
                InventoryAttentionReason.ProjectedExcess30 => InventoryAttentionFamily.Excess,
                InventoryAttentionReason.Idle => InventoryAttentionFamily.Turnover,
                _ => InventoryAttentionFamily.DataQuality,
            },
            Action = action,
            Confidence = confidence,
        });

    private static ProductTurnoverRow Turnover(
        int id = 1,
        string name = "Leite",
        double stock = 40,
        double vmv30 = 1,
        bool idle = false,
        int history = 45) =>
        new()
        {
            ProductId = id,
            Name = name,
            Code = "C" + id,
            Stock = stock,
            StockFridge = 0,
            TotalStock = stock,
            Vmv30 = vmv30,
            CoverageDays = 40,
            CoverageBand = InventoryCoverageBand.Normal,
            HistoryDays = history,
            HasPhysicalAvailabilityEvidence = true,
            IsIdle = idle,
            IsHistoryInsufficient30 = history < 30,
        };

    private static InventoryProjectionSnapshot Snapshot(params ProductTurnoverRow[] rows)
    {
        var list = rows.Length == 0 ? [Turnover()] : rows;
        var map = new Dictionary<int, InventoryProjectedProduct>();
        foreach (var row in list)
        {
            map[row.ProductId] = new InventoryProjectedProduct
            {
                ProductId = row.ProductId,
                Projection = new InventoryProjectionResult
                {
                    HorizonDays = 30,
                    ProjectedDemand = 30,
                    ProjectedExcessQuantity = 0,
                    Lots = [],
                },
            };
        }

        return new InventoryProjectionSnapshot
        {
            Today = new DateTime(2026, 9, 1),
            QueryCount = 7,
            Intelligence = new InventoryIntelligenceSnapshot
            {
                Today = new DateTime(2026, 9, 1),
                QueryCount = 6,
                Rows = list,
            },
            ByProductId = map,
        };
    }

    private static InventoryAttentionPresentationSnapshot Snap70E(
        params InventoryAttentionPresentationRow[] rows)
    {
        var map = new Dictionary<int, InventoryAttentionPresentationRow>();
        foreach (var row in rows)
            map.TryAdd(row.ProductId, row);
        return new InventoryAttentionPresentationSnapshot { Rows = rows, ByProductId = map };
    }

    private static string FindSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return "";
    }
}
