using System.IO;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70E-B3 — presentation pura. Sem WPF, SQL, Load ou regra comercial.
/// </summary>
public class InventoryAttentionPresentationTests
{
    static InventoryAttentionResult Result(
        InventoryAttentionReason reason,
        InventoryAttentionPriority priority,
        InventoryAttentionFamily family,
        InventoryOperatorAction action,
        InventoryAttentionConfidence confidence,
        int id = 1,
        double? excess30 = null,
        double? expirySurplus = null,
        InventoryProjectionSurplusValueQuality quality =
            InventoryProjectionSurplusValueQuality.Unavailable,
        IReadOnlyList<InventoryAttentionReason>? secondary = null) =>
        new()
        {
            ProductId = id,
            PrimaryReason = reason,
            Priority = priority,
            Family = family,
            Action = action,
            Confidence = confidence,
            ProjectedExcessQuantity = excess30,
            ProjectedExpirySurplusQuantity = expirySurplus,
            SurplusValueQuality = quality,
            SecondaryReasons = secondary ?? [],
        };

    static InventoryAttentionPresentationRow Present(InventoryAttentionResult result,
        InventoryProjectedProductPresentation? presented = null) =>
        InventoryAttentionPresentation.FromResult(result, presented);

    [Theory]
    [InlineData(InventoryAttentionPriority.Critical, "Crítica")]
    [InlineData(InventoryAttentionPriority.High, "Alta")]
    [InlineData(InventoryAttentionPriority.Medium, "Média")]
    [InlineData(InventoryAttentionPriority.Low, "Baixa")]
    [InlineData(InventoryAttentionPriority.Normal, "Normal")]
    public void Priority_labels(InventoryAttentionPriority priority, string expected) =>
        Assert.Equal(expected, InventoryAttentionPresentation.PriorityLabel(priority));

    [Theory]
    [InlineData(InventoryAttentionFamily.DataQuality, "Conferência de dados")]
    [InlineData(InventoryAttentionFamily.Expiry, "Validade")]
    [InlineData(InventoryAttentionFamily.Excess, "Excesso de estoque")]
    [InlineData(InventoryAttentionFamily.Turnover, "Giro")]
    [InlineData(InventoryAttentionFamily.Normal, "Normal")]
    public void Family_labels(InventoryAttentionFamily family, string expected) =>
        Assert.Equal(expected, InventoryAttentionPresentation.FamilyLabel(family));

    [Fact]
    public void Actions_reuse_70B2_labels()
    {
        Assert.Equal("Revisar dados", InventoryAttentionPresentation.ActionLabel(InventoryOperatorAction.ReviewData));
        Assert.Equal("Retirar / conferir", InventoryAttentionPresentation.ActionLabel(InventoryOperatorAction.RemoveExpired));
        Assert.Equal("Priorizar saída", InventoryAttentionPresentation.ActionLabel(InventoryOperatorAction.PrioritizeSale));
        Assert.Equal("Monitorar", InventoryAttentionPresentation.ActionLabel(InventoryOperatorAction.Monitor));
        Assert.Equal("Avaliar excesso", InventoryAttentionPresentation.ActionLabel(InventoryOperatorAction.EvaluateExcess));
        Assert.Equal("Nenhuma ação imediata", InventoryAttentionPresentation.ActionLabel(InventoryOperatorAction.None));
        Assert.Equal(
            "Sem recomendação",
            InventoryAttentionPresentation.ActionLabel(
                InventoryOperatorAction.None, InventoryAttentionConfidence.Unavailable));
        Assert.Equal(
            ValidityControlUi.ActionLabel(ValiditySuggestedAction.ReviewData),
            InventoryAttentionPresentation.ActionLabel(InventoryOperatorAction.ReviewData));
    }

    [Theory]
    [InlineData(InventoryAttentionConfidence.Reliable, "Análise disponível")]
    [InlineData(InventoryAttentionConfidence.Limited, "Análise com limitações")]
    [InlineData(InventoryAttentionConfidence.Unavailable, "Análise indisponível")]
    public void Confidence_labels(InventoryAttentionConfidence confidence, string expected) =>
        Assert.Equal(expected, InventoryAttentionPresentation.ConfidenceLabel(confidence));

    [Theory]
    [InlineData(InventoryAttentionReason.Expired)]
    [InlineData(InventoryAttentionReason.ExpiresToday)]
    [InlineData(InventoryAttentionReason.SurplusAtExpiry)]
    [InlineData(InventoryAttentionReason.NearExpiryWithoutSurplus)]
    [InlineData(InventoryAttentionReason.DatedWithoutSurplusInWindow)]
    [InlineData(InventoryAttentionReason.ProjectedExcess30)]
    [InlineData(InventoryAttentionReason.Idle)]
    [InlineData(InventoryAttentionReason.Undated)]
    [InlineData(InventoryAttentionReason.NoLot)]
    [InlineData(InventoryAttentionReason.InvalidExpiryDate)]
    [InlineData(InventoryAttentionReason.ProjectionMissing)]
    [InlineData(InventoryAttentionReason.DuplicateProjection)]
    [InlineData(InventoryAttentionReason.InsufficientHistory)]
    [InlineData(InventoryAttentionReason.NoPhysicalEvidence)]
    [InlineData(InventoryAttentionReason.CompositionProduct)]
    [InlineData(InventoryAttentionReason.NoObservableDemand)]
    [InlineData(InventoryAttentionReason.InvalidInput)]
    [InlineData(InventoryAttentionReason.NegativeStock)]
    [InlineData(InventoryAttentionReason.NegativeLocationStock)]
    [InlineData(InventoryAttentionReason.NegativeWarehouseStock)]
    [InlineData(InventoryAttentionReason.InconsistentStockTotals)]
    [InlineData(InventoryAttentionReason.TrackedQuantityExceedsWarehouse)]
    [InlineData(InventoryAttentionReason.DuplicateLotId)]
    [InlineData(InventoryAttentionReason.InvalidLotQuantity)]
    [InlineData(InventoryAttentionReason.None)]
    public void Every_reason_has_nonempty_label_and_explanation(InventoryAttentionReason reason)
    {
        var label = InventoryAttentionPresentation.ReasonLabel(reason);
        var explanation = InventoryAttentionPresentation.ReasonExplanation(reason);
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.False(string.IsNullOrWhiteSpace(explanation));
        Assert.DoesNotContain("DataQuality", label, StringComparison.Ordinal);
        Assert.DoesNotContain("blocked", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FEFO", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SKU", label, StringComparison.Ordinal);
        Assert.DoesNotContain(reason.ToString(), explanation.Replace(" ", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void Expired_texts()
    {
        var row = Present(Result(
            InventoryAttentionReason.Expired,
            InventoryAttentionPriority.Critical,
            InventoryAttentionFamily.Expiry,
            InventoryOperatorAction.RemoveExpired,
            InventoryAttentionConfidence.Reliable));
        Assert.Equal("Produto vencido", row.PrimaryReasonDisplay);
        Assert.Equal("Retirar / conferir", row.ActionDisplay);
        Assert.Equal("Crítica", row.PriorityDisplay);
        Assert.Equal("Validade", row.FamilyDisplay);
        Assert.Contains("vencido", row.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promoção", row.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpiresToday_and_surplus_and_near_and_window()
    {
        Assert.Equal("Vence hoje", InventoryAttentionPresentation.ReasonLabel(InventoryAttentionReason.ExpiresToday));
        Assert.Equal(
            "Sobra projetada até a validade",
            InventoryAttentionPresentation.ReasonLabel(InventoryAttentionReason.SurplusAtExpiry));
        Assert.Equal(
            "Validade próxima",
            InventoryAttentionPresentation.ReasonLabel(InventoryAttentionReason.NearExpiryWithoutSurplus));
        Assert.Equal(
            "Validade a acompanhar",
            InventoryAttentionPresentation.ReasonLabel(InventoryAttentionReason.DatedWithoutSurplusInWindow));
        Assert.Contains(
            "sobra projetada até a validade",
            InventoryAttentionPresentation.ReasonExplanation(InventoryAttentionReason.SurplusAtExpiry),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "vai vencer sobrando",
            InventoryAttentionPresentation.ReasonExplanation(InventoryAttentionReason.SurplusAtExpiry),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Excess_idle_undated_nolot()
    {
        Assert.Equal(
            "Sobra projetada em 30 dias",
            InventoryAttentionPresentation.ReasonLabel(InventoryAttentionReason.ProjectedExcess30));
        Assert.Equal("Produto parado", InventoryAttentionPresentation.ReasonLabel(InventoryAttentionReason.Idle));
        Assert.Equal("Sem validade informada", InventoryAttentionPresentation.ReasonLabel(InventoryAttentionReason.Undated));
        Assert.Equal("Sem lote identificado", InventoryAttentionPresentation.ReasonLabel(InventoryAttentionReason.NoLot));
        Assert.Contains(
            "90 dias",
            InventoryAttentionPresentation.ReasonExplanation(InventoryAttentionReason.Idle),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Idle_sem_excesso_mostra_Monitorar()
    {
        var row = Present(Result(
            InventoryAttentionReason.Idle,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Turnover,
            InventoryOperatorAction.Monitor,
            InventoryAttentionConfidence.Reliable));
        Assert.Equal("Giro", row.FamilyDisplay);
        Assert.Equal("Monitorar", row.ActionDisplay);
        Assert.Equal(InventoryOperatorAction.Monitor, row.Action);
        Assert.NotEqual("Avaliar excesso", row.ActionDisplay);
    }

    [Fact]
    public void Excesso_mostra_Avaliar_excesso()
    {
        var row = Present(Result(
            InventoryAttentionReason.ProjectedExcess30,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Excess,
            InventoryOperatorAction.EvaluateExcess,
            InventoryAttentionConfidence.Reliable));
        Assert.Equal("Avaliar excesso", row.ActionDisplay);
        Assert.Equal(InventoryOperatorAction.EvaluateExcess, row.Action);
    }

    [Fact]
    public void Idle_com_excesso_mostra_Avaliar_excesso()
    {
        var row = Present(Result(
            InventoryAttentionReason.ProjectedExcess30,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Excess,
            InventoryOperatorAction.EvaluateExcess,
            InventoryAttentionConfidence.Reliable,
            secondary: [InventoryAttentionReason.Idle]));
        Assert.Equal("Avaliar excesso", row.ActionDisplay);
        Assert.Equal(new[] { "Produto parado" }, row.SecondaryReasonDisplays);
        Assert.NotEqual("Monitorar", row.ActionDisplay);
    }

    [Fact]
    public void Presentation_nao_mascara_acao_do_motor()
    {
        var excessAsIdleReason = Present(Result(
            InventoryAttentionReason.Idle,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Turnover,
            InventoryOperatorAction.EvaluateExcess,
            InventoryAttentionConfidence.Reliable));
        Assert.Equal("Avaliar excesso", excessAsIdleReason.ActionDisplay);
    }

    [Fact]
    public void Missing_and_duplicate_are_operator_safe()
    {
        var missing = Present(Result(
            InventoryAttentionReason.ProjectionMissing,
            InventoryAttentionPriority.Low,
            InventoryAttentionFamily.DataQuality,
            InventoryOperatorAction.ReviewData,
            InventoryAttentionConfidence.Unavailable));
        Assert.Equal("Projeção indisponível", missing.PrimaryReasonDisplay);
        Assert.DoesNotContain("InventoryAttention", missing.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductId", missing.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("Composer", missing.Explanation, StringComparison.Ordinal);

        var dup = Present(Result(
            InventoryAttentionReason.DuplicateProjection,
            InventoryAttentionPriority.Critical,
            InventoryAttentionFamily.DataQuality,
            InventoryOperatorAction.ReviewData,
            InventoryAttentionConfidence.Unavailable));
        Assert.Equal("Projeção inconsistente", dup.PrimaryReasonDisplay);
        Assert.DoesNotContain("last-wins", dup.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dictionary", dup.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Normal_unavailable_nao_parece_tudo_certo()
    {
        var row = Present(Result(
            InventoryAttentionReason.InsufficientHistory,
            InventoryAttentionPriority.Normal,
            InventoryAttentionFamily.Normal,
            InventoryOperatorAction.None,
            InventoryAttentionConfidence.Unavailable));
        Assert.False(row.IsAllClear);
        Assert.Equal("Normal", row.PriorityDisplay);
        Assert.Equal("Análise indisponível", row.ConfidenceDisplay);
        Assert.Equal("Sem recomendação", row.ActionDisplay);
        Assert.NotEqual("Nenhuma ação imediata", row.ActionDisplay);
        Assert.Contains("histórico suficiente", row.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Não há atenção imediata", row.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Normal_reliable_e_tudo_certo()
    {
        var row = Present(Result(
            InventoryAttentionReason.None,
            InventoryAttentionPriority.Normal,
            InventoryAttentionFamily.Normal,
            InventoryOperatorAction.None,
            InventoryAttentionConfidence.Reliable));
        Assert.True(row.IsAllClear);
        Assert.Equal("Nenhuma ação imediata", row.ActionDisplay);
        Assert.Equal("Análise disponível", row.ConfidenceDisplay);
        Assert.Equal("Não há atenção imediata neste produto.", row.Explanation);
    }

    [Fact]
    public void Zero_nao_e_emdash()
    {
        var zero = Present(Result(
            InventoryAttentionReason.None,
            InventoryAttentionPriority.Normal,
            InventoryAttentionFamily.Normal,
            InventoryOperatorAction.None,
            InventoryAttentionConfidence.Reliable,
            excess30: 0,
            expirySurplus: 0));
        var missing = Present(Result(
            InventoryAttentionReason.ProjectionMissing,
            InventoryAttentionPriority.Low,
            InventoryAttentionFamily.DataQuality,
            InventoryOperatorAction.ReviewData,
            InventoryAttentionConfidence.Unavailable));
        Assert.Equal("0", zero.ProjectedExcess30Display);
        Assert.Equal("0", zero.ProjectedExpirySurplusDisplay);
        Assert.Equal("—", missing.ProjectedExcess30Display);
        Assert.Equal("—", missing.ProjectedExpirySurplusDisplay);
        Assert.NotEqual(zero.ProjectedExcess30Display, missing.ProjectedExcess30Display);
    }

    [Fact]
    public void Quantidade_usa_formato_SGDB()
    {
        var row = Present(Result(
            InventoryAttentionReason.ProjectedExcess30,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Excess,
            InventoryOperatorAction.EvaluateExcess,
            InventoryAttentionConfidence.Reliable,
            excess30: 10,
            expirySurplus: 30));
        Assert.Equal(ProductLotListRow.FormatQty(10), row.ProjectedExcess30Display);
        Assert.Equal(ProductLotListRow.FormatQty(30), row.ProjectedExpirySurplusDisplay);
        Assert.DoesNotContain("R$", row.ProjectedExcess30Display, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prejuízo", row.ProjectedExcess30Display, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sobra_30d_sem_valor_financeiro()
    {
        var row = Present(Result(
            InventoryAttentionReason.ProjectedExcess30,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Excess,
            InventoryOperatorAction.EvaluateExcess,
            InventoryAttentionConfidence.Reliable,
            excess30: 12.5));
        Assert.Equal(InventoryProjectionPresentation.FormatCalculatedQty(12.5), row.ProjectedExcess30Display);
        Assert.Equal("—", row.ProjectedExpirySurplusValueDisplay);
        Assert.DoesNotContain("R$", row.ProjectedExcess30Display, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(LotCostSource.LotRecorded, InventoryProjectionSurplusValueQuality.CompleteRecorded)]
    [InlineData(LotCostSource.CurrentAverageEstimate, InventoryProjectionSurplusValueQuality.CompleteWithEstimate)]
    public void Valor_reusa_70D(LotCostSource source, InventoryProjectionSurplusValueQuality quality)
    {
        var presented = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                SkuBlockedReason = InventorySkuProjectionBlockedReason.None,
                HorizonDays = 30,
                ProjectedDemand = 10,
                ProjectedExcessQuantity = 30,
                Lots =
                [
                    new InventoryProjectionLotResult
                    {
                        LotId = 1,
                        Kind = InventoryProjectionLotKind.Dated,
                        Quantity = 40,
                        DaysUntilExpiry = 10,
                        ProjectedSurplusAtExpiry = 30,
                        ProjectedSurplusValue = 90,
                    },
                ],
            },
            LotCosts =
            [
                new InventoryProjectedLotCost { LotId = 1, UsedCost = 3, CostSource = source },
            ],
        });
        var row = Present(Result(
            InventoryAttentionReason.SurplusAtExpiry,
            InventoryAttentionPriority.High,
            InventoryAttentionFamily.Expiry,
            InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionConfidence.Reliable,
            expirySurplus: 30,
            quality: quality), presented);
        Assert.Equal(presented.SurplusValueDisplay, row.ProjectedExpirySurplusValueDisplay);
        Assert.Equal(presented.SurplusValueQualityDisplay, row.SurplusValueQualityDisplay);
        Assert.Equal(
            InventoryProjectionPresentation.SurplusValueQualityLabel(quality),
            row.SurplusValueQualityDisplay);
        Assert.NotEqual("—", row.ProjectedExpirySurplusValueDisplay);
        if (quality == InventoryProjectionSurplusValueQuality.CompleteWithEstimate)
            Assert.Contains("*", row.ProjectedExpirySurplusValueDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("prejuízo", row.ProjectedExpirySurplusValueDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("perda", row.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valor_parcial_e_indisponivel()
    {
        var partial = InventoryProjectionPresentation.FromProduct(new InventoryProjectedProduct
        {
            ProductId = 1,
            Projection = new InventoryProjectionResult
            {
                SkuBlockedReason = InventorySkuProjectionBlockedReason.None,
                HorizonDays = 30,
                ProjectedDemand = 10,
                ProjectedExcessQuantity = 70,
                Lots =
                [
                    new InventoryProjectionLotResult
                    {
                        LotId = 1,
                        Kind = InventoryProjectionLotKind.Dated,
                        Quantity = 40,
                        DaysUntilExpiry = 10,
                        ProjectedSurplusAtExpiry = 30,
                        ProjectedSurplusValue = 60,
                    },
                    new InventoryProjectionLotResult
                    {
                        LotId = 2,
                        Kind = InventoryProjectionLotKind.Dated,
                        Quantity = 40,
                        DaysUntilExpiry = 10,
                        ProjectedSurplusAtExpiry = 40,
                    },
                ],
            },
            LotCosts =
            [
                new InventoryProjectedLotCost
                {
                    LotId = 1,
                    UsedCost = 2,
                    CostSource = LotCostSource.LotRecorded,
                },
                new InventoryProjectedLotCost { LotId = 2, CostSource = LotCostSource.Unavailable },
            ],
        });
        var partialRow = Present(Result(
            InventoryAttentionReason.SurplusAtExpiry,
            InventoryAttentionPriority.High,
            InventoryAttentionFamily.Expiry,
            InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionConfidence.Limited,
            expirySurplus: 70,
            quality: InventoryProjectionSurplusValueQuality.Partial), partial);
        Assert.Equal("Valor parcial", partialRow.SurplusValueQualityDisplay);
        Assert.Contains("parcial", partialRow.ProjectedExpirySurplusValueDisplay, StringComparison.OrdinalIgnoreCase);

        var none = Present(Result(
            InventoryAttentionReason.SurplusAtExpiry,
            InventoryAttentionPriority.High,
            InventoryAttentionFamily.Expiry,
            InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionConfidence.Limited,
            expirySurplus: 30));
        Assert.Equal("—", none.ProjectedExpirySurplusValueDisplay);
        Assert.Equal("Sem custo disponível", none.SurplusValueQualityDisplay);
    }

    [Fact]
    public void Secondary_reasons_preservam_ordem_sem_duplicata()
    {
        var row = Present(Result(
            InventoryAttentionReason.SurplusAtExpiry,
            InventoryAttentionPriority.High,
            InventoryAttentionFamily.Expiry,
            InventoryOperatorAction.PrioritizeSale,
            InventoryAttentionConfidence.Reliable,
            secondary:
            [
                InventoryAttentionReason.ProjectedExcess30,
                InventoryAttentionReason.Idle,
            ]));
        Assert.Equal(
            new[] { "Sobra projetada em 30 dias", "Produto parado" },
            row.SecondaryReasonDisplays);
        Assert.Equal(row.SecondaryReasonDisplays.Distinct().Count(), row.SecondaryReasonDisplays.Count);
        Assert.DoesNotContain(row.PrimaryReasonDisplay, row.SecondaryReasonDisplays);
    }

    [Fact]
    public void Apply_preserva_ordem_e_querycount()
    {
        var snapshot = new InventoryAttentionSnapshot
        {
            Today = new DateTime(2026, 9, 1),
            QueryCount = 7,
            Results =
            [
                Result(InventoryAttentionReason.Expired, InventoryAttentionPriority.Critical,
                    InventoryAttentionFamily.Expiry, InventoryOperatorAction.RemoveExpired,
                    InventoryAttentionConfidence.Reliable, id: 20),
                Result(InventoryAttentionReason.None, InventoryAttentionPriority.Normal,
                    InventoryAttentionFamily.Normal, InventoryOperatorAction.None,
                    InventoryAttentionConfidence.Reliable, id: 10),
            ],
        };
        var presented = InventoryAttentionPresentation.Apply(snapshot);
        Assert.Equal(7, presented.QueryCount);
        Assert.Equal(new[] { 20, 10 }, presented.Rows.Select(r => r.ProductId));
        Assert.Equal(snapshot.QueryCount, presented.QueryCount);
    }

    [Fact]
    public void Determinismo()
    {
        var result = Result(
            InventoryAttentionReason.ProjectedExcess30,
            InventoryAttentionPriority.Medium,
            InventoryAttentionFamily.Excess,
            InventoryOperatorAction.EvaluateExcess,
            InventoryAttentionConfidence.Reliable,
            excess30: 8,
            secondary: [InventoryAttentionReason.Idle]);
        var a = Present(result);
        var b = Present(result);
        Assert.Equal(a.PriorityDisplay, b.PriorityDisplay);
        Assert.Equal(a.Explanation, b.Explanation);
        Assert.Equal(a.SecondaryReasonDisplays, b.SecondaryReasonDisplays);
        Assert.Equal(a.ProjectedExcess30Display, b.ProjectedExcess30Display);
    }

    [Fact]
    public void Linguagem_segura_em_todos_os_texts()
    {
        foreach (var reason in Enum.GetValues<InventoryAttentionReason>())
        {
            var blob = string.Join(' ',
                InventoryAttentionPresentation.ReasonLabel(reason),
                InventoryAttentionPresentation.ReasonExplanation(reason));
            Assert.DoesNotContain("prejuízo", blob, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("perda garantida", blob, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Criar promoção", blob, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Dar desconto", blob, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Fazer combo", blob, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Não comprar", blob, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            "promoção",
            InventoryAttentionPresentation.ActionEvaluateExcess,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Null_snapshot_nao_lanca()
    {
        var presented = InventoryAttentionPresentation.Apply(null);
        Assert.Empty(presented.Rows);
        Assert.Equal(0, presented.QueryCount);
    }

    [Fact]
    public void Source_nao_tem_io_nem_comercial()
    {
        var path = FindSource("src", "SGDB.App", "Models", "InventoryAttentionPresentation.cs");
        Assert.True(File.Exists(path), path);
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("Sqlite", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Data", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Load(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreNetwork", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sale_price", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConsiderPromotion", source, StringComparison.Ordinal);
        Assert.DoesNotContain("prejuízo", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("promoção", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desconto", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("combo", source, StringComparison.OrdinalIgnoreCase);
    }

    static string FindSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return "";
    }
}
