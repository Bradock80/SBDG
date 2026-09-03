using SGDB.Services;

namespace SGDB.Models;

/// <summary>
/// Linha 70G-B3 para futura UI. Sem WPF. Sem recálculo B1/B2.
/// Action é a informação principal; Status permanece no domínio.
/// </summary>
public sealed class InventoryPurchaseGuidancePresentationRow
{
    public int ProductId { get; init; }

    public InventoryPurchaseGuidanceStatus Status { get; init; } =
        InventoryPurchaseGuidanceStatus.NotApplicable;

    public InventoryPurchaseGuidanceAction Action { get; init; }
    public string ActionLabel { get; init; } = "";

    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
    public string ConfidenceLabel { get; init; } = "";

    public InventoryPurchaseGuidanceReason PrimaryReason { get; init; }
    public string PrimaryReasonLabel { get; init; } = "";
    public string ShortExplanation { get; init; } = "";
    public string DetailExplanation { get; init; } = "";

    public IReadOnlyList<InventoryPurchaseGuidanceReason> SecondaryReasons { get; init; } = [];
    public IReadOnlyList<string> SecondaryReasonLabels { get; init; } = [];

    public string TotalStockDisplay { get; init; } = InventoryProjectionPresentation.EmDash;
    public string Vmv30Display { get; init; } = InventoryProjectionPresentation.EmDash;
    public string Vmv30Text { get; init; } = InventoryProjectionPresentation.EmDash;
    public string CoverageDisplay { get; init; } = "";

    public string ExcessQuantityDisplay { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ExcessQuantityText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ExpirySurplusDisplay { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ExpirySurplusText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ValidityLabel { get; init; } = InventoryProjectionPresentation.EmDash;

    public string ConsiderLimitationNote { get; init; } = "";
    public string DisclaimerText { get; init; } = "";

    public bool IsConsiderReplenishment { get; init; }
    public bool IsDoNotReplenishNow { get; init; }
    public bool IsMonitor { get; init; }
    public bool IsReviewData { get; init; }
    public bool IsNotApplicable { get; init; }
    public bool IsJoinMissing { get; init; }
}

/// <summary>Presentation em lote. Ordem = snapshot 70G-B2 (Intelligence.Rows).</summary>
public sealed class InventoryPurchaseGuidancePresentationSnapshot
{
    public int QueryCount { get; init; }
    public IReadOnlyList<InventoryPurchaseGuidancePresentationRow> Rows { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryPurchaseGuidancePresentationRow> ByProductId { get; init; } =
        new Dictionary<int, InventoryPurchaseGuidancePresentationRow>();
}

/// <summary>
/// Rótulos PT-BR 70G-B3. Sem I/O, WPF, SQL ou recálculo B1/B2.
/// Reusa formatação 70C/70D e vocabulário de confiança 70E.
/// </summary>
public static class InventoryPurchaseGuidancePresentation
{
    public const int ExpectedQueryCount = 0;

    public const int ExpectedPipelineQueryCount =
        InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount;

    public const string EmDash = InventoryProjectionPresentation.EmDash;

    public const string ModuleTitle = "Reposição Inteligente";
    public const string ToolbarTitle = "Reposição";

    public const string ActionConsiderReplenishment = "Considerar reposição";
    public const string ActionDoNotReplenishNow = "Não repor agora";
    public const string ActionMonitor = "Acompanhar";
    public const string ActionReviewData = "Revisar dados";
    public const string ActionNone = "Não aplicável";

    public const string CardConsiderReplenishment = ActionConsiderReplenishment;
    public const string CardDoNotReplenishNow = ActionDoNotReplenishNow;
    public const string CardMonitor = ActionMonitor;
    public const string CardReviewData = ActionReviewData;

    public const string CoverageNotCalculable = "Não calculável";
    public const string SituationUnavailable = "Situação não disponível";

    public const string ReasonOutOfStockWithObservedDemand = "Sem estoque com giro observado";
    public const string ReasonCriticalCoverage = "Cobertura crítica";
    public const string ReasonLowCoverage = "Cobertura baixa";
    public const string ReasonProjectedExcess30 = "Excesso projetado";
    public const string ReasonProjectedExpirySurplus = "Sobra projetada antes da validade";
    public const string ReasonIdleStock = "Estoque parado";
    public const string ReasonNoObservableDemand = "Sem giro observado no período";
    public const string ReasonInsufficientHistory = "Histórico ainda insuficiente";
    public const string ReasonNoPhysicalEvidence = "Sem evidência física suficiente";
    public const string ReasonStructuralDataIssue = "Inconsistência nos dados";
    public const string ReasonLocationLimitation = "Limitação na leitura por local";
    public const string ReasonCompositionProduct = "Produto composto";
    public const string ReasonExpired = "Produto vencido";
    public const string ReasonExpiresToday = "Vence hoje";
    public const string ReasonNone = "Situação de acompanhamento";

    public const string ValidityExpired = "Vencido";
    public const string ValidityExpiresToday = "Vence hoje";
    public const string ValidityExpiryRisk = "Risco antes da validade";

    public const string ConsiderLimitationNote =
        "Orientação baseada no estoque e giro observados. "
        + "Prazo do fornecedor e mercadoria em trânsito ainda não fazem parte desta análise.";

    public const string GuidanceDisclaimer =
        "A Reposição Inteligente é um apoio à decisão. "
        + "O SGDB não cria pedidos nem altera o estoque automaticamente.";

    public const string MissingAnalysis = "Orientação de reposição indisponível.";

    public static InventoryPurchaseGuidancePresentationRow MissingRow(int productId = 0) =>
        new()
        {
            ProductId = productId,
            Status = InventoryPurchaseGuidanceStatus.ReviewData,
            Action = InventoryPurchaseGuidanceAction.ReviewData,
            ActionLabel = ActionReviewData,
            Confidence = InventoryAttentionConfidence.Unavailable,
            ConfidenceLabel = InventoryAttentionPresentation.ConfidenceUnavailable,
            PrimaryReason = InventoryPurchaseGuidanceReason.StructuralDataIssue,
            PrimaryReasonLabel = MissingAnalysis,
            ShortExplanation = MissingAnalysis,
            DetailExplanation = MissingAnalysis,
            SecondaryReasons = [],
            SecondaryReasonLabels = [],
            TotalStockDisplay = EmDash,
            Vmv30Display = EmDash,
            Vmv30Text = EmDash,
            CoverageDisplay = CoverageNotCalculable,
            ExcessQuantityDisplay = EmDash,
            ExcessQuantityText = EmDash,
            ExpirySurplusDisplay = EmDash,
            ExpirySurplusText = EmDash,
            ValidityLabel = EmDash,
            DisclaimerText = GuidanceDisclaimer,
            IsReviewData = true,
            IsJoinMissing = true,
        };

    public static InventoryPurchaseGuidancePresentationRow ResolveForDetail(
        InventoryPurchaseGuidancePresentationSnapshot? snapshot,
        int productId)
    {
        if (snapshot?.ByProductId is { Count: > 0 } map
            && map.TryGetValue(productId, out var row)
            && row is not null)
            return row;

        return MissingRow(productId);
    }

    public static InventoryPurchaseGuidancePresentationSnapshot Apply(
        InventoryPurchaseGuidanceSnapshot? snapshot,
        InventoryIntelligenceSnapshot? intelligence = null,
        InventoryProjectionSnapshot? projection = null)
    {
        snapshot ??= new InventoryPurchaseGuidanceSnapshot();
        var results = snapshot.Results ?? [];
        var turnoverById = IndexTurnover(intelligence);
        var projectedById = projection?.ByProductId
            ?? (IReadOnlyDictionary<int, InventoryProjectedProduct>)
                new Dictionary<int, InventoryProjectedProduct>();

        var rows = new List<InventoryPurchaseGuidancePresentationRow>(results.Count);
        var map = new Dictionary<int, InventoryPurchaseGuidancePresentationRow>(results.Count);
        foreach (var result in results)
        {
            turnoverById.TryGetValue(result.ProductId, out var turnover);
            projectedById.TryGetValue(result.ProductId, out var projected);
            var row = FromResult(result, turnover, projected);
            rows.Add(row);
            map.TryAdd(row.ProductId, row);
        }

        return new InventoryPurchaseGuidancePresentationSnapshot
        {
            QueryCount = snapshot.QueryCount,
            Rows = rows,
            ByProductId = map,
        };
    }

    public static InventoryPurchaseGuidancePresentationRow FromResult(
        InventoryPurchaseGuidanceResult? result,
        ProductTurnoverRow? turnover = null,
        InventoryProjectedProduct? projected = null)
    {
        if (result is null)
            return MissingRow();

        var secondary = result.SecondaryReasons ?? [];
        var secondaryLabels = new List<string>(secondary.Count);
        foreach (var reason in secondary)
        {
            if (reason == result.PrimaryReason || reason == InventoryPurchaseGuidanceReason.None)
                continue;
            secondaryLabels.Add(ReasonLabel(reason));
        }

        var coverageBand = turnover is not null && turnover.ProductId == result.ProductId
            ? turnover.CoverageBand
            : (InventoryCoverageBand?)null;
        var shortText = ShortExplanationOf(result.PrimaryReason, result.Action, result.Confidence, coverageBand);
        var consider = result.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment;
        var detail = consider ? $"{shortText} {ConsiderLimitationNote}" : shortText;

        var stock = TurnoverStock(turnover, result.ProductId);
        var vmv = TurnoverVmv(turnover, result.ProductId);
        var coverage = TurnoverCoverage(turnover, result.ProductId);
        var excess = ProjectedExcess(projected, result.ProductId);
        var surplus = ProjectedSurplus(projected, result.ProductId);

        var excessDisplay = InventoryProjectionPresentation.FormatCalculatedQty(excess);
        var surplusDisplay = InventoryProjectionPresentation.FormatCalculatedQty(surplus);
        var hasExcessReason = HasReason(result, InventoryPurchaseGuidanceReason.ProjectedExcess30);
        var hasSurplusReason = HasReason(result, InventoryPurchaseGuidanceReason.ProjectedExpirySurplus);
        detail = AppendFact(detail, hasExcessReason ? ExcessText(excessDisplay) : EmDash);
        detail = AppendFact(detail, hasSurplusReason ? SurplusText(surplusDisplay) : EmDash);

        return new InventoryPurchaseGuidancePresentationRow
        {
            ProductId = result.ProductId,
            Status = result.Status,
            Action = result.Action,
            ActionLabel = ActionLabel(result.Action),
            Confidence = result.Confidence,
            ConfidenceLabel = InventoryAttentionPresentation.ConfidenceLabel(result.Confidence),
            PrimaryReason = result.PrimaryReason,
            PrimaryReasonLabel = ReasonLabel(result.PrimaryReason),
            ShortExplanation = shortText,
            DetailExplanation = detail.Trim(),
            SecondaryReasons = secondary,
            SecondaryReasonLabels = secondaryLabels,
            TotalStockDisplay = FormatStock(stock),
            Vmv30Display = FormatVmvDisplay(vmv),
            Vmv30Text = FormatVmvText(vmv),
            CoverageDisplay = FormatCoverage(coverage),
            ExcessQuantityDisplay = excessDisplay,
            ExcessQuantityText = hasExcessReason ? ExcessText(excessDisplay) : EmDash,
            ExpirySurplusDisplay = surplusDisplay,
            ExpirySurplusText = hasSurplusReason ? SurplusText(surplusDisplay) : EmDash,
            ValidityLabel = ValidityLabelOf(result.PrimaryReason),
            ConsiderLimitationNote = consider ? ConsiderLimitationNote : "",
            DisclaimerText = GuidanceDisclaimer,
            IsConsiderReplenishment = consider,
            IsDoNotReplenishNow = result.Action == InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            IsMonitor = result.Action == InventoryPurchaseGuidanceAction.Monitor,
            IsReviewData = result.Action == InventoryPurchaseGuidanceAction.ReviewData
                || result.Status == InventoryPurchaseGuidanceStatus.ReviewData,
            IsNotApplicable = result.Status == InventoryPurchaseGuidanceStatus.NotApplicable
                || result.Action == InventoryPurchaseGuidanceAction.None,
        };
    }

    public static string ActionLabel(InventoryPurchaseGuidanceAction action) =>
        action switch
        {
            InventoryPurchaseGuidanceAction.ConsiderReplenishment => ActionConsiderReplenishment,
            InventoryPurchaseGuidanceAction.DoNotReplenishNow => ActionDoNotReplenishNow,
            InventoryPurchaseGuidanceAction.Monitor => ActionMonitor,
            InventoryPurchaseGuidanceAction.ReviewData => ActionReviewData,
            InventoryPurchaseGuidanceAction.None => ActionNone,
            _ => SituationUnavailable,
        };

    public static string ReasonLabel(InventoryPurchaseGuidanceReason reason) =>
        reason switch
        {
            InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand =>
                ReasonOutOfStockWithObservedDemand,
            InventoryPurchaseGuidanceReason.CriticalCoverage => ReasonCriticalCoverage,
            InventoryPurchaseGuidanceReason.LowCoverage => ReasonLowCoverage,
            InventoryPurchaseGuidanceReason.ProjectedExcess30 => ReasonProjectedExcess30,
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus => ReasonProjectedExpirySurplus,
            InventoryPurchaseGuidanceReason.IdleStock => ReasonIdleStock,
            InventoryPurchaseGuidanceReason.NoObservableDemand => ReasonNoObservableDemand,
            InventoryPurchaseGuidanceReason.InsufficientHistory => ReasonInsufficientHistory,
            InventoryPurchaseGuidanceReason.NoPhysicalEvidence => ReasonNoPhysicalEvidence,
            InventoryPurchaseGuidanceReason.StructuralDataIssue => ReasonStructuralDataIssue,
            InventoryPurchaseGuidanceReason.LocationLimitation => ReasonLocationLimitation,
            InventoryPurchaseGuidanceReason.CompositionProduct => ReasonCompositionProduct,
            InventoryPurchaseGuidanceReason.Expired => ReasonExpired,
            InventoryPurchaseGuidanceReason.ExpiresToday => ReasonExpiresToday,
            InventoryPurchaseGuidanceReason.None => ReasonNone,
            _ => SituationUnavailable,
        };

    public static string ShortExplanation(
        InventoryPurchaseGuidanceReason reason,
        InventoryPurchaseGuidanceAction action = InventoryPurchaseGuidanceAction.Monitor,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Limited,
        InventoryCoverageBand? coverageBand = null) =>
        ShortExplanationOf(reason, action, confidence, coverageBand);

    public static string DetailExplanation(
        InventoryPurchaseGuidanceReason reason,
        InventoryPurchaseGuidanceAction action = InventoryPurchaseGuidanceAction.Monitor,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Limited,
        InventoryCoverageBand? coverageBand = null)
    {
        var shortText = ShortExplanationOf(reason, action, confidence, coverageBand);
        if (action == InventoryPurchaseGuidanceAction.ConsiderReplenishment)
            return $"{shortText} {ConsiderLimitationNote}";
        return shortText;
    }

    static string ShortExplanationOf(
        InventoryPurchaseGuidanceReason reason,
        InventoryPurchaseGuidanceAction action,
        InventoryAttentionConfidence confidence,
        InventoryCoverageBand? coverageBand) =>
        reason switch
        {
            InventoryPurchaseGuidanceReason.OutOfStockWithObservedDemand =>
                "O produto está sem estoque e apresentou giro no período analisado. Vale considerar a reposição.",
            InventoryPurchaseGuidanceReason.CriticalCoverage =>
                "O estoque atual cobre poucos dias do giro observado. Vale considerar a reposição.",
            InventoryPurchaseGuidanceReason.LowCoverage =>
                "A cobertura de estoque está baixa em relação ao giro observado. Vale considerar a reposição.",
            InventoryPurchaseGuidanceReason.ProjectedExcess30 =>
                "Há estoque acima da demanda projetada para o horizonte analisado. Uma nova reposição não se justifica agora.",
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus =>
                "Parte do estoque pode permanecer sem giro até a validade. Uma nova reposição não se justifica agora.",
            InventoryPurchaseGuidanceReason.IdleStock =>
                "O produto possui estoque e está sem giro por período prolongado. Uma nova reposição não se justifica agora.",
            InventoryPurchaseGuidanceReason.Expired =>
                "Há produto vencido identificado. Não é indicado aumentar esse estoque agora.",
            InventoryPurchaseGuidanceReason.ExpiresToday =>
                "Há produto com vencimento hoje. Não é indicado aumentar esse estoque agora.",
            InventoryPurchaseGuidanceReason.NoObservableDemand =>
                "Não há giro observado suficiente no período para justificar uma decisão de reposição agora. Acompanhe o produto.",
            InventoryPurchaseGuidanceReason.InsufficientHistory =>
                "O histórico disponível ainda é insuficiente para uma orientação mais conclusiva. Continue acompanhando.",
            InventoryPurchaseGuidanceReason.LocationLimitation =>
                "A leitura de validade não representa todos os locais de estoque. Acompanhe o produto antes de decidir pela reposição.",
            InventoryPurchaseGuidanceReason.NoPhysicalEvidence =>
                "Não há evidência física suficiente para orientar a reposição. Revise os dados do produto.",
            InventoryPurchaseGuidanceReason.StructuralDataIssue =>
                "Existem inconsistências nos dados que impedem uma orientação confiável de reposição.",
            InventoryPurchaseGuidanceReason.CompositionProduct =>
                "Este item é composto por outros produtos. A reposição deve ser analisada nos componentes.",
            InventoryPurchaseGuidanceReason.None =>
                NoneExplanation(action, confidence, coverageBand),
            _ => SituationUnavailable,
        };

    static string NoneExplanation(
        InventoryPurchaseGuidanceAction action,
        InventoryAttentionConfidence confidence,
        InventoryCoverageBand? coverageBand)
    {
        if (coverageBand == InventoryCoverageBand.Attention
            || (coverageBand is null
                && action == InventoryPurchaseGuidanceAction.Monitor
                && confidence == InventoryAttentionConfidence.Limited))
            return "A cobertura atual pede acompanhamento, mas os dados ainda não justificam uma reposição.";

        return "O estoque está em situação de acompanhamento, sem indicação atual de reposição.";
    }

    static string ValidityLabelOf(InventoryPurchaseGuidanceReason reason) =>
        reason switch
        {
            InventoryPurchaseGuidanceReason.Expired => ValidityExpired,
            InventoryPurchaseGuidanceReason.ExpiresToday => ValidityExpiresToday,
            InventoryPurchaseGuidanceReason.ProjectedExpirySurplus => ValidityExpiryRisk,
            _ => EmDash,
        };

    static Dictionary<int, ProductTurnoverRow> IndexTurnover(InventoryIntelligenceSnapshot? intelligence)
    {
        var map = new Dictionary<int, ProductTurnoverRow>();
        foreach (var row in intelligence?.Rows ?? [])
            map.TryAdd(row.ProductId, row);
        return map;
    }

    static double? TurnoverStock(ProductTurnoverRow? turnover, int productId) =>
        turnover is not null && turnover.ProductId == productId ? turnover.TotalStock : null;

    static double? TurnoverVmv(ProductTurnoverRow? turnover, int productId) =>
        turnover is not null && turnover.ProductId == productId ? turnover.Vmv30 : null;

    static double? TurnoverCoverage(ProductTurnoverRow? turnover, int productId) =>
        turnover is not null && turnover.ProductId == productId ? turnover.CoverageDays : null;

    static double? ProjectedExcess(InventoryProjectedProduct? projected, int productId)
    {
        if (projected is null || projected.ProductId != productId)
            return null;
        var projection = projected.Projection;
        if (projection is null || !projection.CanProjectSku)
            return null;
        return projection.ProjectedExcessQuantity;
    }

    static double? ProjectedSurplus(InventoryProjectedProduct? projected, int productId)
    {
        if (projected is null || projected.ProductId != productId)
            return null;
        return InventoryProjectionPresentation.SumExpirySurplusQuantity(projected.Projection?.Lots);
    }

    static bool HasReason(
        InventoryPurchaseGuidanceResult result,
        InventoryPurchaseGuidanceReason reason)
    {
        if (result.PrimaryReason == reason)
            return true;
        foreach (var item in result.SecondaryReasons ?? [])
        {
            if (item == reason)
                return true;
        }

        return false;
    }

    static string FormatStock(double? qty)
    {
        if (qty is not double value || !double.IsFinite(value))
            return EmDash;
        return InventoryIntelligencePresentation.FormatQty(value);
    }

    static string FormatVmvDisplay(double? vmv)
    {
        if (vmv is not double value)
            return EmDash;
        if (!double.IsFinite(value))
            return EmDash;
        return InventoryIntelligencePresentation.FormatVmv30(value);
    }

    static string FormatVmvText(double? vmv)
    {
        var display = FormatVmvDisplay(vmv);
        if (display == EmDash)
            return EmDash;
        return $"Giro médio: {display} un./dia";
    }

    static string FormatCoverage(double? days)
    {
        if (days is not double value || !double.IsFinite(value))
            return CoverageNotCalculable;
        var number = InventoryIntelligencePresentation.FormatCoverageDays(value);
        if (number == InventoryIntelligencePresentation.EmDash)
            return CoverageNotCalculable;
        return $"{number} dias";
    }

    static string ExcessText(string display) =>
        display == EmDash ? EmDash : $"Excesso projetado: {display} un.";

    static string SurplusText(string display) =>
        display == EmDash ? EmDash : $"Sobra projetada até a validade: {display} un.";

    static string AppendFact(string detail, string fact)
    {
        if (string.IsNullOrWhiteSpace(fact) || fact == EmDash)
            return detail;
        return $"{detail} {fact}";
    }
}
