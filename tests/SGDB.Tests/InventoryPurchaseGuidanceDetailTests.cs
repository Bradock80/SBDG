using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70G-B5 — orientação de reposição no detalhe técnico existente.
/// Sem instanciar Window WPF, sem EXE, sem deposito.db da loja.
/// </summary>
public class InventoryPurchaseGuidanceDetailTests
{
    [Theory]
    [InlineData(InventoryPurchaseGuidanceAction.ConsiderReplenishment, InventoryPurchaseGuidanceReason.LowCoverage)]
    [InlineData(InventoryPurchaseGuidanceAction.DoNotReplenishNow, InventoryPurchaseGuidanceReason.ProjectedExcess30)]
    [InlineData(InventoryPurchaseGuidanceAction.Monitor, InventoryPurchaseGuidanceReason.None)]
    [InlineData(InventoryPurchaseGuidanceAction.ReviewData, InventoryPurchaseGuidanceReason.NoPhysicalEvidence)]
    [InlineData(InventoryPurchaseGuidanceAction.None, InventoryPurchaseGuidanceReason.CompositionProduct)]
    public void Paridade_Action_igual_B4_e_detalhe(
        InventoryPurchaseGuidanceAction action,
        InventoryPurchaseGuidanceReason reason)
    {
        var pair = Pair(action, reason);
        Assert.Equal(pair.Grid.Action, pair.Detail.PurchaseGuidance.Action);
        Assert.Equal(pair.Grid.ActionLabel, pair.Detail.PurchaseGuidance.ActionLabel);
    }

    [Fact]
    public void Paridade_Reason_Confidence_Explanation()
    {
        var pair = Pair(
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryPurchaseGuidanceReason.ProjectedExcess30);
        Assert.Equal(pair.Grid.Guidance.PrimaryReason, pair.Detail.PurchaseGuidance.PrimaryReason);
        Assert.Equal(pair.Grid.PrimaryReasonLabel, pair.Detail.PurchaseGuidance.PrimaryReasonLabel);
        Assert.Equal(pair.Grid.Guidance.Confidence, pair.Detail.PurchaseGuidance.Confidence);
        Assert.Equal(pair.Grid.ConfidenceLabel, pair.Detail.PurchaseGuidance.ConfidenceLabel);
        Assert.Equal(pair.Grid.DetailExplanation, pair.Detail.PurchaseGuidance.DetailExplanation);
        Assert.Same(pair.Presented, pair.Detail.PurchaseGuidance);
    }

    [Fact]
    public void Secao_existe_com_titulo()
    {
        var xaml = ReadWindowXaml();
        var commercial = xaml.IndexOf("x:Name=\"CommercialActionSection\"", StringComparison.Ordinal);
        var section = xaml.IndexOf("x:Name=\"ReplenishmentSection\"", StringComparison.Ordinal);
        var scroll = xaml.IndexOf("<ScrollViewer", StringComparison.Ordinal);
        var scrollEnd = xaml.IndexOf("</ScrollViewer>", StringComparison.Ordinal);
        Assert.True(commercial > 0 && section > commercial);
        Assert.True(scroll > 0 && section > scroll && section < scrollEnd);
        Assert.Contains("InventoryPurchaseGuidanceDetailUi.Heading", xaml, StringComparison.Ordinal);
        Assert.Equal("ORIENTAÇÃO DE REPOSIÇÃO", InventoryPurchaseGuidanceDetailUi.Heading);
        Assert.Contains("MinWidth=\"820\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Guidance_presente_quando_snapshot_tem_produto()
    {
        var pair = Pair(
            InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryPurchaseGuidanceReason.CriticalCoverage);
        Assert.False(pair.Detail.PurchaseGuidance.IsJoinMissing);
        Assert.Equal(1, pair.Detail.PurchaseGuidance.ProductId);
        Assert.Equal("Considerar reposição", pair.Detail.PurchaseGuidance.ActionLabel);
    }

    [Fact]
    public void Missing_guidance_usa_fallback_B3()
    {
        var snap = Snapshot70C(1);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var detail = InventoryProjectionDetail.TryCreate(snap, presented, 1);
        Assert.NotNull(detail);
        Assert.True(detail!.PurchaseGuidance.IsJoinMissing);
        Assert.Equal(InventoryPurchaseGuidanceAction.ReviewData, detail.PurchaseGuidance.Action);
        Assert.Equal(InventoryPurchaseGuidancePresentation.ActionReviewData, detail.PurchaseGuidance.ActionLabel);
        Assert.Equal(InventoryPurchaseGuidancePresentation.MissingAnalysis, detail.PurchaseGuidance.DetailExplanation);
        Assert.Equal(InventoryAttentionPresentation.ConfidenceUnavailable, detail.PurchaseGuidance.ConfidenceLabel);
    }

    [Fact]
    public void Null_snapshot_e_null_presentation_nao_lancam()
    {
        var snap = Snapshot70C(1);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var fromNullSnap = InventoryProjectionDetail.TryCreate(
            snap, presented, 1, guidance: null);
        Assert.True(fromNullSnap!.PurchaseGuidance.IsJoinMissing);

        var empty = InventoryProjectionDetail.TryCreate(
            snap, presented, 1,
            guidance: new InventoryPurchaseGuidancePresentationSnapshot());
        Assert.True(empty!.PurchaseGuidance.IsJoinMissing);

        Assert.Equal(
            InventoryPurchaseGuidancePresentation.MissingAnalysis,
            InventoryPurchaseGuidancePresentation.ResolveForDetail(null, 1).DetailExplanation);
    }

    [Fact]
    public void ProductId_inexistente_retorna_null()
    {
        var snap = Snapshot70C(1);
        var presented = InventoryProjectionPresentation.Apply(snap);
        var guidance = SnapGuidance(Present(
            InventoryPurchaseGuidanceAction.Monitor,
            InventoryPurchaseGuidanceReason.None));
        Assert.Null(InventoryProjectionDetail.TryCreate(snap, presented, 99, guidance: guidance));
        Assert.Null(InventoryProjectionDetail.TryCreate(snap, presented, 0, guidance: guidance));
        Assert.Null(InventoryProjectionDetail.TryCreate(null, presented, 1, guidance: guidance));
    }

    [Fact]
    public void Consider_mostra_nota_canonica()
    {
        var pair = Pair(
            InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            InventoryPurchaseGuidanceReason.LowCoverage);
        Assert.True(InventoryPurchaseGuidanceDetailUi.ShowConsiderNote(pair.Detail.PurchaseGuidance));
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.ConsiderLimitationNote,
            pair.Detail.PurchaseGuidance.ConsiderLimitationNote);
        Assert.Contains(
            InventoryPurchaseGuidancePresentation.ConsiderLimitationNote,
            pair.Detail.PurchaseGuidance.DetailExplanation,
            StringComparison.Ordinal);
        Assert.Contains("ReplenishmentConsiderNote", ReadWindowXaml(), StringComparison.Ordinal);
        Assert.Contains("ShowConsiderNote", ReadWindowCs(), StringComparison.Ordinal);
    }

    [Fact]
    public void DoNot_nao_contem_nunca()
    {
        var blob = Blob(Pair(
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryPurchaseGuidanceReason.ProjectedExcess30).Detail.PurchaseGuidance);
        Assert.Contains("agora", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nunca", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("não comprar mais", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("produto ruim", blob, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Idle_nao_contem_nao_vende()
    {
        var blob = Blob(Pair(
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryPurchaseGuidanceReason.IdleStock).Detail.PurchaseGuidance);
        Assert.Equal("Não repor agora", Pair(
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryPurchaseGuidanceReason.IdleStock).Detail.PurchaseGuidance.ActionLabel);
        Assert.DoesNotContain("não vende", blob, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("produto sem saída", blob, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agora", blob, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Review_e_NA_visiveis()
    {
        var review = Pair(
            InventoryPurchaseGuidanceAction.ReviewData,
            InventoryPurchaseGuidanceReason.NoPhysicalEvidence).Detail.PurchaseGuidance;
        Assert.Equal("Revisar dados", review.ActionLabel);
        Assert.False(string.IsNullOrWhiteSpace(review.DetailExplanation));
        Assert.Contains("Revise os dados", review.DetailExplanation, StringComparison.OrdinalIgnoreCase);

        var na = Pair(
            InventoryPurchaseGuidanceAction.None,
            InventoryPurchaseGuidanceReason.CompositionProduct).Detail.PurchaseGuidance;
        Assert.Equal("Não aplicável", na.ActionLabel);
        Assert.Equal("Produto composto", na.PrimaryReasonLabel);
        Assert.Contains("componentes", na.DetailExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("None", na.ActionLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("None", na.PrimaryReasonLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Secondary_visivel_quando_existe_e_oculto_quando_vazio()
    {
        var with = Present(
            InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            InventoryPurchaseGuidanceReason.ProjectedExcess30,
            [InventoryPurchaseGuidanceReason.ProjectedExpirySurplus]);
        Assert.True(InventoryPurchaseGuidanceDetailUi.ShowSecondary(with));
        Assert.Equal(["Sobra projetada antes da validade"], with.SecondaryReasonLabels);

        var without = Present(
            InventoryPurchaseGuidanceAction.Monitor,
            InventoryPurchaseGuidanceReason.None);
        Assert.False(InventoryPurchaseGuidanceDetailUi.ShowSecondary(without));
        Assert.Empty(without.SecondaryReasonLabels);
        Assert.Contains("ShowSecondary", ReadWindowCs(), StringComparison.Ordinal);
        Assert.Contains("ReplenishmentSecondaryPanel", ReadWindowXaml(), StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", ReadWindowXaml(), StringComparison.Ordinal);
    }

    [Fact]
    public void None_nao_aparece_literalmente()
    {
        foreach (var action in Enum.GetValues<InventoryPurchaseGuidanceAction>())
        {
            var label = InventoryPurchaseGuidancePresentation.ActionLabel(action);
            Assert.DoesNotContain("None", label, StringComparison.Ordinal);
        }

        var xaml = ReadWindowXaml();
        var cs = ReadWindowCs();
        Assert.DoesNotContain("None", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Action.None", cs, StringComparison.Ordinal);
    }

    [Fact]
    public void Detalhe_nao_recalcula_Action()
    {
        var window = ReadWindowCs();
        var ui = ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidanceDetailUi.cs");
        var detail = ReadSource("src", "SGDB.App", "Models", "InventoryProjectionDetail.cs");
        foreach (var text in new[] { window, ui, detail })
        {
            Assert.DoesNotContain("InventoryPurchaseGuidanceEngine", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CoverageBand.Low", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CoverageBand.Critical", text, StringComparison.Ordinal);
            Assert.DoesNotContain("IsIdle", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ProjectedExcess", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HasExpiredLot", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PurchaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SupplierService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SuggestedQuantity", text, StringComparison.Ordinal);
            Assert.DoesNotContain("min_stock", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PurchaseScore", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT ", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("ResolveForDetail(guidance, productId)", detail, StringComparison.Ordinal);
        Assert.Contains("guidance.ActionLabel", window, StringComparison.Ordinal);
        Assert.Contains("DetailExplanation", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("ShortExplanation", ReadWindowCs(), StringComparison.Ordinal);
    }

    [Fact]
    public void Abrir_detalhe_mais_zero_query()
    {
        Assert.Equal(0, InventoryPurchaseGuidanceDetailUi.ExpectedQueryCount);
        Assert.Equal(0, InventoryPurchaseGuidanceComposer.ExpectedQueryCount);
        Assert.Equal(0, InventoryPurchaseGuidancePresentation.ExpectedQueryCount);
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
        Assert.Equal(
            9,
            InventoryIntelligenceService.ExpectedQueryCount
            + InventoryProjectionService.ExpectedLotsQueryCount
            + InventoryCommercialFactsService.ExpectedQueryCount
            + InventoryCommercialMarginSettingsService.ExpectedLoadQueryCount
            + InventoryPurchaseGuidanceComposer.ExpectedQueryCount
            + InventoryPurchaseGuidancePresentation.ExpectedQueryCount
            + InventoryPurchaseGuidanceUi.ExpectedQueryCount
            + InventoryPurchaseGuidanceDetailUi.ExpectedQueryCount);

        var eiOpen = MethodBody(ReadEiCs(), "private void OpenProjectionDetail_Click");
        var b4Open = MethodBody(ReadB4Cs(), "private void OpenProjectionDetail()");
        foreach (var open in new[] { eiOpen, b4Open })
        {
            Assert.Contains("InventoryProjectionDetail.TryCreate", open, StringComparison.Ordinal);
            Assert.Contains("_guidancePresented", open, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryProjectionService.Load", open, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryPurchaseGuidanceComposer", open, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryPurchaseGuidanceEngine", open, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryPurchaseGuidancePresentation.Apply", open, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryCommercialFactsService", open, StringComparison.Ordinal);
        }

        Assert.Equal(1, CountOccurrences(ReadEiCs(), "InventoryProjectionService.Load("));
        Assert.Equal(1, CountOccurrences(ReadB4Cs(), "InventoryProjectionService.Load("));
        Assert.Contains("InventoryPurchaseGuidanceComposer.Compose(snapshot)", ReadEiCs(), StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_O1_ByProductId()
    {
        var resolve = MethodBody(
            ReadSource("src", "SGDB.App", "Models", "InventoryPurchaseGuidancePresentation.cs"),
            "public static InventoryPurchaseGuidancePresentationRow ResolveForDetail");
        Assert.Contains("TryGetValue", resolve, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefault", resolve, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach", resolve, StringComparison.Ordinal);
        var tryCreate = MethodBody(
            ReadSource("src", "SGDB.App", "Models", "InventoryProjectionDetail.cs"),
            "public static InventoryProjectionDetail? TryCreate");
        Assert.Contains("ResolveForDetail(guidance, productId)", tryCreate, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_disclaimer_geral_no_detalhe()
    {
        var xaml = ReadWindowXaml();
        var start = xaml.IndexOf("x:Name=\"ReplenishmentSection\"", StringComparison.Ordinal);
        var section = xaml[start..];
        Assert.DoesNotContain("GuidanceDisclaimer", section, StringComparison.Ordinal);
        Assert.Contains("ReplenishmentConsiderNote", section, StringComparison.Ordinal);
        Assert.Contains("ConsiderLimitationNote", ReadWindowCs(), StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_fatos_numericos_duplicados()
    {
        var xaml = ReadWindowXaml();
        var start = xaml.IndexOf("x:Name=\"ReplenishmentSection\"", StringComparison.Ordinal);
        var section = xaml[start..xaml.IndexOf("</Border>", start, StringComparison.Ordinal)];
        Assert.DoesNotContain("TotalStockDisplay", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Vmv30Display", section, StringComparison.Ordinal);
        Assert.DoesNotContain("CoverageDisplay", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ExcessQuantityDisplay", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Quantidade sugerida", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Fornecedor", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Comprar", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_nao_usa_servico()
    {
        var cs = ReadWindowCs();
        Assert.DoesNotContain("using SGDB.Services", cs, StringComparison.Ordinal);
        Assert.Contains("BindPurchaseGuidance", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPromotionSuggestionEngine", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryCommercialScenarioEngine", cs, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(InventoryPurchaseGuidanceReason.ProjectedExcess30)]
    [InlineData(InventoryPurchaseGuidanceReason.ProjectedExpirySurplus)]
    [InlineData(InventoryPurchaseGuidanceReason.IdleStock)]
    [InlineData(InventoryPurchaseGuidanceReason.Expired)]
    [InlineData(InventoryPurchaseGuidanceReason.ExpiresToday)]
    public void Semantica_DoNot_via_engine(InventoryPurchaseGuidanceReason reason)
    {
        var result = reason switch
        {
            InventoryPurchaseGuidanceReason.ProjectedExcess30 =>
                Eval(In(projectedExcessQuantity: 8)),
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus =>
                Eval(In(projectedExpirySurplus: 5)),
            InventoryPurchaseGuidanceReason.IdleStock =>
                Eval(In(isIdle: true, vmv30: 0, coverageDays: null)),
            InventoryPurchaseGuidanceReason.Expired =>
                Eval(In(hasExpiredLot: true)),
            _ => Eval(In(hasExpiresTodayLot: true)),
        };
        var detail = DetailFromEngine(result);
        Assert.Equal(InventoryPurchaseGuidanceAction.DoNotReplenishNow, detail.PurchaseGuidance.Action);
        Assert.Equal(reason, detail.PurchaseGuidance.PrimaryReason);
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.FromResult(result).ActionLabel,
            detail.PurchaseGuidance.ActionLabel);
        Assert.Contains("agora", detail.PurchaseGuidance.ActionLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Semantica_zero_giro_Critical_Low_via_engine()
    {
        var zero = DetailFromEngine(Eval(In(
            stock: 0, stockFridge: 0, vmv30: 2,
            coverageBand: InventoryCoverageBand.Zero,
            coverageDays: null,
            isZeroStockWithTurnover: true)));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, zero.PurchaseGuidance.Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand, zero.PurchaseGuidance.PrimaryReason);

        var critical = DetailFromEngine(Eval(In(
            coverageBand: InventoryCoverageBand.Critical, coverageDays: 2, vmv30: 2)));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, critical.PurchaseGuidance.Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.CriticalCoverage, critical.PurchaseGuidance.PrimaryReason);

        var low = DetailFromEngine(Eval(In(
            coverageBand: InventoryCoverageBand.Low, coverageDays: 6, vmv30: 2)));
        Assert.Equal(InventoryPurchaseGuidanceAction.ConsiderReplenishment, low.PurchaseGuidance.Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.LowCoverage, low.PurchaseGuidance.PrimaryReason);
    }

    [Fact]
    public void Semantica_VMV0_acompanha_e_NoEvidence_revisa()
    {
        var monitor = DetailFromEngine(Eval(In(
            vmv30: 0, coverageBand: InventoryCoverageBand.Normal, coverageDays: 20)));
        Assert.Equal(InventoryPurchaseGuidanceAction.Monitor, monitor.PurchaseGuidance.Action);

        var review = DetailFromEngine(Eval(In(hasPhysicalAvailabilityEvidence: false)));
        Assert.Equal(InventoryPurchaseGuidanceAction.ReviewData, review.PurchaseGuidance.Action);
        Assert.Equal(InventoryPurchaseGuidanceReason.NoPhysicalEvidence, review.PurchaseGuidance.PrimaryReason);
    }

    [Fact]
    public void ReviewData_fallback_seguro()
    {
        var missing = InventoryPurchaseGuidancePresentation.MissingRow(8);
        Assert.Equal(InventoryPurchaseGuidanceAction.ReviewData, missing.Action);
        Assert.True(missing.IsJoinMissing);
        Assert.DoesNotContain("Exception", missing.DetailExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            InventoryPurchaseGuidancePresentation.ActionReviewData,
            InventoryPurchaseGuidanceDetailUi.Row(null).ActionLabel);
    }

    [Fact]
    public void QueryCount_ui_e_zero() =>
        Assert.Equal(0, InventoryPurchaseGuidanceDetailUi.ExpectedQueryCount);

    static (InventoryPurchaseGuidanceGridRow Grid, InventoryProjectionDetail Detail, InventoryPurchaseGuidancePresentationRow Presented)
        Pair(InventoryPurchaseGuidanceAction action, InventoryPurchaseGuidanceReason reason)
    {
        var presented = Present(action, reason);
        var turnover = new ProductTurnoverRow
        {
            ProductId = 1,
            Name = "Alfa",
            Code = "C-1",
            CoverageBand = InventoryCoverageBand.Normal,
            TotalStock = 10,
            Vmv30 = 1,
        };
        var grid = InventoryPurchaseGuidanceUi.ToGridRow(presented, turnover);
        var detail = DetailFromPresented(presented, turnover);
        return (grid, detail, presented);
    }

    static InventoryProjectionDetail DetailFromEngine(InventoryPurchaseGuidanceResult result)
    {
        var presented = InventoryPurchaseGuidancePresentation.FromResult(result);
        return DetailFromPresented(presented, new ProductTurnoverRow
        {
            ProductId = presented.ProductId == 0 ? 7 : presented.ProductId,
            Name = "P",
            TotalStock = 10,
            Stock = 10,
        });
    }

    static InventoryProjectionDetail DetailFromPresented(
        InventoryPurchaseGuidancePresentationRow presented,
        ProductTurnoverRow turnover)
    {
        var id = presented.ProductId == 0 ? turnover.ProductId : presented.ProductId;
        var snap = Snapshot70C(id, turnover);
        var projection = InventoryProjectionPresentation.Apply(snap);
        return InventoryProjectionDetail.TryCreate(snap, projection, id, guidance: SnapGuidance(presented))!;
    }

    static InventoryPurchaseGuidancePresentationSnapshot SnapGuidance(
        InventoryPurchaseGuidancePresentationRow presented) =>
        new()
        {
            Rows = [presented],
            ByProductId = new Dictionary<int, InventoryPurchaseGuidancePresentationRow>
            {
                [presented.ProductId] = presented,
            },
        };

    static InventoryPurchaseGuidancePresentationRow Present(
        InventoryPurchaseGuidanceAction action,
        InventoryPurchaseGuidanceReason reason,
        IReadOnlyList<InventoryPurchaseGuidanceReason>? secondary = null)
    {
        var status = action switch
        {
            InventoryPurchaseGuidanceAction.ConsiderReplenishment
                or InventoryPurchaseGuidanceAction.DoNotReplenishNow =>
                InventoryPurchaseGuidanceStatus.GuidanceAvailable,
            InventoryPurchaseGuidanceAction.Monitor => InventoryPurchaseGuidanceStatus.Monitor,
            InventoryPurchaseGuidanceAction.ReviewData => InventoryPurchaseGuidanceStatus.ReviewData,
            _ => InventoryPurchaseGuidanceStatus.NotApplicable,
        };
        return InventoryPurchaseGuidancePresentation.FromResult(new InventoryPurchaseGuidanceResult
        {
            ProductId = 1,
            Action = action,
            Status = status,
            PrimaryReason = reason,
            SecondaryReasons = secondary ?? [],
            Confidence = action == InventoryPurchaseGuidanceAction.ConsiderReplenishment
                ? InventoryAttentionConfidence.Limited
                : InventoryAttentionConfidence.Reliable,
        });
    }

    static InventoryProjectionSnapshot Snapshot70C(int id, ProductTurnoverRow? turnover = null) =>
        new()
        {
            Intelligence = new InventoryIntelligenceSnapshot
            {
                Rows = [turnover ?? new ProductTurnoverRow { ProductId = id, Name = "P", TotalStock = 10, Stock = 10 }],
            },
            ByProductId = new Dictionary<int, InventoryProjectedProduct>
            {
                [id] = new() { ProductId = id },
            },
        };

    static InventoryPurchaseGuidanceResult Eval(InventoryPurchaseGuidanceInput input) =>
        InventoryPurchaseGuidanceEngine.Evaluate(input);

    static InventoryPurchaseGuidanceInput In(
        double stock = 10,
        double stockFridge = 0,
        double vmv30 = 1,
        InventoryCoverageBand coverageBand = InventoryCoverageBand.Normal,
        double? coverageDays = 20,
        bool isIdle = false,
        bool isZeroStockWithTurnover = false,
        bool hasPhysicalAvailabilityEvidence = true,
        int historyDays = 120,
        bool isCompositionProduct = false,
        double? projectedExcessQuantity = 0,
        double? projectedExpirySurplus = 0,
        bool hasExpiredLot = false,
        bool hasExpiresTodayLot = false) =>
        new()
        {
            ProductId = 7,
            Stock = stock,
            StockFridge = stockFridge,
            TotalStock = stock + stockFridge,
            Vmv30 = vmv30,
            CoverageBand = coverageBand,
            CoverageDays = coverageDays,
            IsIdle = isIdle,
            IsZeroStockWithTurnover = isZeroStockWithTurnover,
            HasPhysicalAvailabilityEvidence = hasPhysicalAvailabilityEvidence,
            HistoryDays = historyDays,
            IsCompositionProduct = isCompositionProduct,
            CanProjectSku = true,
            ProjectedExcessQuantity = projectedExcessQuantity,
            ProjectedExpirySurplus = projectedExpirySurplus,
            HasExpiredLot = hasExpiredLot,
            HasExpiresTodayLot = hasExpiresTodayLot,
        };

    static string Blob(InventoryPurchaseGuidancePresentationRow row) =>
        string.Join(' ',
            row.ActionLabel,
            row.PrimaryReasonLabel,
            row.ConfidenceLabel,
            row.ShortExplanation,
            row.DetailExplanation,
            row.ConsiderLimitationNote);

    static string ReadWindowXaml() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml");

    static string ReadWindowCs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryProjectionDetailWindow.xaml.cs");

    static string ReadEiCs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryIntelligenceModuleView.xaml.cs");

    static string ReadB4Cs() =>
        ReadSource("src", "SGDB.App", "Views", "InventoryPurchaseGuidanceModuleView.xaml.cs");

    static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var brace = source.IndexOf('{', start);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        return source[start..];
    }

    static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var idx = text.IndexOf(token, start, StringComparison.Ordinal);
            if (idx < 0)
                return count;
            count++;
            start = idx + token.Length;
        }
    }

    static string ReadSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}

/// <summary>
/// 70G-B5 — pipeline isolado. Sem EXE, sem banco da loja.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryPurchaseGuidanceB5PipelineTests
{
    [Fact]
    public void Pipeline_9_e_detalhe_reusa_snapshot()
    {
        using var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B5GA", "Detalhe A");
        var saleBefore = ReadSale(id);

        var snapshot = InventoryProjectionService.Load();
        var presented = InventoryProjectionPresentation.Apply(snapshot);
        var attention = InventoryAttentionComposer.Build(snapshot);
        var attentionPresented = InventoryAttentionPresentation.Apply(attention, presented);
        var eligibility = InventoryCommercialEligibilityComposer.Build(snapshot, attention);
        var facts = InventoryCommercialFactsService.Load(
            InventoryCommercialEligibilityComposer.ProductIds(snapshot));
        var setting = InventoryCommercialMarginSettingsService.Load();
        var policy = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        var commercial = InventoryCommercialScenarioComposer.Compose(
            snapshot.Intelligence, snapshot, attention, eligibility, facts, policy);
        var commercialPresented = InventoryCommercialScenarioPresentation.Apply(commercial);
        var promotion = InventoryPromotionSuggestionComposer.Compose(snapshot.Intelligence, commercial);
        var promotionPresented = InventoryPromotionSuggestionPresentation.Apply(promotion);
        var guidance = InventoryPurchaseGuidanceComposer.Compose(snapshot);
        var guidancePresented = InventoryPurchaseGuidancePresentation.Apply(
            guidance, snapshot.Intelligence, snapshot);

        Assert.Equal(7, snapshot.QueryCount);
        Assert.Equal(1, facts.QueryCount);
        Assert.Equal(1, setting.QueryCount);
        Assert.Equal(0, guidance.QueryCount);
        Assert.Equal(
            9,
            snapshot.QueryCount + facts.QueryCount + setting.QueryCount + guidance.QueryCount);

        var grid = InventoryPurchaseGuidanceUi.Apply(
            guidancePresented, snapshot.Intelligence.Rows, InventoryPurchaseGuidanceUiFilter.Cleared())
            .First(r => r.ProductId == id);
        var detail = InventoryProjectionDetail.TryCreate(
            snapshot, presented, id, attentionPresented, commercialPresented,
            promotionPresented, guidancePresented);
        Assert.NotNull(detail);
        Assert.Same(guidancePresented.ByProductId[id], detail!.PurchaseGuidance);
        Assert.Equal(grid.Action, detail.PurchaseGuidance.Action);
        Assert.Equal(grid.ActionLabel, detail.PurchaseGuidance.ActionLabel);
        Assert.Equal(grid.PrimaryReasonLabel, detail.PurchaseGuidance.PrimaryReasonLabel);
        Assert.Equal(grid.DetailExplanation, detail.PurchaseGuidance.DetailExplanation);
        Assert.Equal(saleBefore, ReadSale(id));
    }

    static double ReadSale(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sale_price FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }
}
