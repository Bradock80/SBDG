namespace SGDB.Models;

/// <summary>
/// Linha 70F-B5D para futura UI. Sem WPF. Sem recálculo B4/B5B.
/// </summary>
public sealed class InventoryPromotionSuggestionPresentationRow
{
    public int ProductId { get; init; }

    public InventoryPromotionSuggestionStatus Status { get; init; } =
        InventoryPromotionSuggestionStatus.NotApplicable;
    public string StatusLabel { get; init; } = "";

    public InventoryPromotionSuggestionAction Action { get; init; }
    public string ActionLabel { get; init; } = "";

    public InventoryCommercialScenarioThesis Thesis { get; init; }
    public string ThesisLabel { get; init; } = InventoryProjectionPresentation.EmDash;

    public InventoryPromotionSuggestionObjective Objective { get; init; }
    public string ObjectiveLabel { get; init; } = InventoryProjectionPresentation.EmDash;

    public InventoryAttentionConfidence Confidence { get; init; } =
        InventoryAttentionConfidence.Unavailable;
    public string ConfidenceLabel { get; init; } = "";

    public InventoryAttentionPriority? AttentionPriority { get; init; }
    public string PriorityLabel { get; init; } = InventoryProjectionPresentation.EmDash;

    public InventoryPromotionSuggestionReason PrimaryReason { get; init; }
    public string PrimaryReasonLabel { get; init; } = "";
    public string Explanation { get; init; } = "";

    public IReadOnlyList<InventoryPromotionSuggestionReason> SecondaryReasons { get; init; } = [];
    public IReadOnlyList<string> SecondaryReasonLabels { get; init; } = [];

    public IReadOnlyList<InventoryPromotionSuggestionWarning> Warnings { get; init; } = [];
    public IReadOnlyList<string> WarningLabels { get; init; } = [];

    public string AttentionQuantityLabel { get; init; } = "";
    public string AttentionQuantityText { get; init; } = InventoryProjectionPresentation.EmDash;
    public InventoryCommercialAttentionQuantitySource AttentionQuantitySource { get; init; }
    public string AttentionQuantitySourceLabel { get; init; } = InventoryProjectionPresentation.EmDash;

    public IReadOnlyList<InventoryCommercialScenarioOptionPresentation> ScenarioOptions { get; init; } = [];

    public string DisclaimerText { get; init; } = "";

    public bool IsSuggested { get; init; }
    public bool IsReviewData { get; init; }
    public bool IsExpired { get; init; }
    public bool IsJoinMissing { get; init; }
}

/// <summary>Presentation em lote. Ordem = snapshot B5C (Intelligence.Rows).</summary>
public sealed class InventoryPromotionSuggestionPresentationSnapshot
{
    public int QueryCount { get; init; }
    public IReadOnlyList<InventoryPromotionSuggestionPresentationRow> Rows { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryPromotionSuggestionPresentationRow> ByProductId { get; init; } =
        new Dictionary<int, InventoryPromotionSuggestionPresentationRow>();
}

/// <summary>
/// Rótulos PT-BR 70F-B5D. Sem I/O, WPF, SQL ou recálculo B4/B5B.
/// Reusa formatação 70C/70D/B4D (moeda, quantidade, —, cenário) e vocabulário 70E.
/// </summary>
public static class InventoryPromotionSuggestionPresentation
{
    public const int ExpectedQueryCount = 0;

    public const string EmDash = InventoryProjectionPresentation.EmDash;

    public const string StatusSuggested = "Sugestão disponível";
    public const string StatusMonitorOnly = "Acompanhar";
    public const string StatusReviewData = "Revisar dados";
    public const string StatusNotApplicable = "Sem sugestão de promoção";
    public const string StatusPolicyMissing = "Margem mínima não configurada";
    public const string StatusPolicyInvalid = "Margem mínima inválida";
    public const string StatusFinancialUnavailable = "Análise financeira indisponível";
    public const string StatusExpired = "Produto vencido";

    public const string ActionConsiderPromotion = "Considerar promoção";
    public const string ActionPrioritizeExposure = "Priorizar saída / exposição";
    public const string ActionMonitor = "Monitorar";
    public const string ActionReviewData = "Revisar dados";
    public const string ActionRemoveExpired = "Retirar / conferir";
    public const string ActionNone = "Nenhuma ação promocional";

    public const string ObjectiveReduceExpiry =
        "Reduzir a sobra projetada até a validade";
    public const string ObjectiveReduceExcess30 =
        "Reduzir o excesso projetado em 30 dias";
    public const string ObjectiveIncreaseAttention =
        "Aumentar a atenção comercial ao produto";
    public const string ObjectiveMonitorTurnover =
        "Acompanhar o giro do produto";
    public const string ObjectiveReviewInformation =
        "Conferir as informações do produto";
    public const string ObjectiveRemoveExpired =
        "Retirar o produto vencido da disponibilidade de venda";

    public const string AttentionQuantityCaption = "Quantidade em atenção";
    public const string QuantitySourceExpiry = "Projeção até a validade";
    public const string QuantitySourceExcess30 = "Projeção de excesso em 30 dias";

    public const string SuggestedDisclaimer =
        "Esta é uma simulação para apoio à decisão. O SGDB não altera preços nem ativa promoções automaticamente.";
    public const string ShortDisclaimer =
        "O SGDB não altera preços automaticamente.";
    public const string ExpiredDisclaimer =
        "Produto vencido não recebe incentivo de venda. O SGDB não altera preços automaticamente.";

    public const string WarningMinimumMarginAllowsAtCost =
        "A política comercial configurada permite que as simulações cheguem até o custo do produto (margem mínima de 0%).";
    public const string WarningWholesalePricingMayDiffer =
        "Este produto possui configuração de atacado. O preço efetivo no PDV pode ser diferente do preço de catálogo usado nesta simulação.";

    public const string MissingAnalysis = "Análise comercial indisponível.";

    public static InventoryPromotionSuggestionPresentationRow MissingRow(int productId = 0) =>
        new()
        {
            ProductId = productId,
            StatusLabel = MissingAnalysis,
            ActionLabel = ActionNone,
            ThesisLabel = EmDash,
            ObjectiveLabel = EmDash,
            Confidence = InventoryAttentionConfidence.Unavailable,
            ConfidenceLabel = InventoryAttentionPresentation.ConfidenceUnavailable,
            PriorityLabel = EmDash,
            PrimaryReasonLabel = MissingAnalysis,
            Explanation = MissingAnalysis,
            SecondaryReasons = [],
            SecondaryReasonLabels = [],
            Warnings = [],
            WarningLabels = [],
            AttentionQuantityLabel = AttentionQuantityCaption,
            AttentionQuantityText = EmDash,
            AttentionQuantitySourceLabel = EmDash,
            ScenarioOptions = [],
            DisclaimerText = ShortDisclaimer,
            IsJoinMissing = true,
        };

    public static InventoryPromotionSuggestionPresentationRow ResolveForDetail(
        InventoryPromotionSuggestionPresentationSnapshot? snapshot,
        int productId)
    {
        if (snapshot?.ByProductId is { Count: > 0 } map
            && map.TryGetValue(productId, out var row)
            && row is not null)
            return row;

        return MissingRow(productId);
    }

    public static InventoryPromotionSuggestionPresentationSnapshot Apply(
        InventoryPromotionSuggestionSnapshot? snapshot)
    {
        snapshot ??= new InventoryPromotionSuggestionSnapshot();
        var rows = snapshot.Rows ?? [];
        var presented = new List<InventoryPromotionSuggestionPresentationRow>(rows.Count);
        var map = new Dictionary<int, InventoryPromotionSuggestionPresentationRow>(rows.Count);
        foreach (var row in rows)
        {
            var item = FromRow(row);
            presented.Add(item);
            map.TryAdd(item.ProductId, item);
        }

        return new InventoryPromotionSuggestionPresentationSnapshot
        {
            QueryCount = snapshot.QueryCount,
            Rows = presented,
            ByProductId = map,
        };
    }

    public static InventoryPromotionSuggestionPresentationRow FromRow(
        InventoryPromotionSuggestionRow? row)
    {
        row ??= new InventoryPromotionSuggestionRow();
        var presented = FromResult(row.Suggestion);
        return CloneWithProductId(presented, row.ProductId);
    }

    public static InventoryPromotionSuggestionPresentationRow FromResult(
        InventoryPromotionSuggestionResult? result)
    {
        result ??= new InventoryPromotionSuggestionResult();
        var suggested = result.Status == InventoryPromotionSuggestionStatus.Suggested;
        var expired = result.Status == InventoryPromotionSuggestionStatus.Expired;
        var secondary = result.SecondaryReasons ?? [];
        var secondaryLabels = new List<string>(secondary.Count);
        foreach (var reason in secondary)
        {
            if (reason == result.PrimaryReason || reason == InventoryPromotionSuggestionReason.None)
                continue;
            secondaryLabels.Add(ReasonLabel(reason));
        }

        var warnings = result.Warnings ?? [];
        var warningLabels = new List<string>(warnings.Count);
        foreach (var warning in warnings)
            warningLabels.Add(WarningLabel(warning));

        return new InventoryPromotionSuggestionPresentationRow
        {
            ProductId = result.ProductId,
            Status = result.Status,
            StatusLabel = StatusLabel(result.Status),
            Action = result.Action,
            ActionLabel = ActionLabel(result.Action),
            Thesis = result.Thesis,
            ThesisLabel = ThesisLabel(result.Thesis),
            Objective = result.Objective,
            ObjectiveLabel = ObjectiveLabel(result.Objective),
            Confidence = result.Confidence,
            ConfidenceLabel = InventoryAttentionPresentation.ConfidenceLabel(result.Confidence),
            AttentionPriority = result.AttentionPriority,
            PriorityLabel = PriorityLabel(result.AttentionPriority),
            PrimaryReason = result.PrimaryReason,
            PrimaryReasonLabel = ReasonLabel(result.PrimaryReason),
            Explanation = ReasonExplanation(result.PrimaryReason),
            SecondaryReasons = secondary,
            SecondaryReasonLabels = secondaryLabels,
            Warnings = warnings,
            WarningLabels = warningLabels,
            AttentionQuantityLabel = AttentionQuantityCaption,
            AttentionQuantityText = FormatQuantity(result.AttentionQuantity),
            AttentionQuantitySource = result.AttentionQuantitySource,
            AttentionQuantitySourceLabel = QuantitySourceLabel(result.AttentionQuantitySource),
            ScenarioOptions = PresentScenarios(result, suggested),
            DisclaimerText = DisclaimerOf(result.Status),
            IsSuggested = suggested,
            IsReviewData = result.Status == InventoryPromotionSuggestionStatus.ReviewData,
            IsExpired = expired,
        };
    }

    public static string StatusLabel(InventoryPromotionSuggestionStatus status) =>
        status switch
        {
            InventoryPromotionSuggestionStatus.Suggested => StatusSuggested,
            InventoryPromotionSuggestionStatus.MonitorOnly => StatusMonitorOnly,
            InventoryPromotionSuggestionStatus.ReviewData => StatusReviewData,
            InventoryPromotionSuggestionStatus.NotApplicable => StatusNotApplicable,
            InventoryPromotionSuggestionStatus.PolicyMissing => StatusPolicyMissing,
            InventoryPromotionSuggestionStatus.PolicyInvalid => StatusPolicyInvalid,
            InventoryPromotionSuggestionStatus.FinancialDataUnavailable => StatusFinancialUnavailable,
            InventoryPromotionSuggestionStatus.Expired => StatusExpired,
            _ => "Situação não classificada",
        };

    public static string ActionLabel(InventoryPromotionSuggestionAction action) =>
        action switch
        {
            InventoryPromotionSuggestionAction.ConsiderPromotion => ActionConsiderPromotion,
            InventoryPromotionSuggestionAction.PrioritizeExposure => ActionPrioritizeExposure,
            InventoryPromotionSuggestionAction.Monitor => ActionMonitor,
            InventoryPromotionSuggestionAction.ReviewData => ActionReviewData,
            InventoryPromotionSuggestionAction.RemoveExpired => ActionRemoveExpired,
            InventoryPromotionSuggestionAction.None => ActionNone,
            _ => "Ação não classificada",
        };

    public static string ObjectiveLabel(InventoryPromotionSuggestionObjective objective) =>
        objective switch
        {
            InventoryPromotionSuggestionObjective.ReduceProjectedExpirySurplus => ObjectiveReduceExpiry,
            InventoryPromotionSuggestionObjective.ReduceProjectedExcess30 => ObjectiveReduceExcess30,
            InventoryPromotionSuggestionObjective.IncreaseCommercialAttention => ObjectiveIncreaseAttention,
            InventoryPromotionSuggestionObjective.MonitorTurnover => ObjectiveMonitorTurnover,
            InventoryPromotionSuggestionObjective.ReviewInformation => ObjectiveReviewInformation,
            InventoryPromotionSuggestionObjective.RemoveExpired => ObjectiveRemoveExpired,
            InventoryPromotionSuggestionObjective.None => EmDash,
            _ => "Objetivo não classificado",
        };

    public static string ThesisLabel(InventoryCommercialScenarioThesis thesis) =>
        InventoryCommercialScenarioPresentation.ThesisLabel(thesis);

    public static string PriorityLabel(InventoryAttentionPriority? priority) =>
        priority is InventoryAttentionPriority value
            ? InventoryAttentionPresentation.PriorityLabel(value)
            : EmDash;

    public static string QuantitySourceLabel(InventoryCommercialAttentionQuantitySource source) =>
        source switch
        {
            InventoryCommercialAttentionQuantitySource.ExpirySurplus => QuantitySourceExpiry,
            InventoryCommercialAttentionQuantitySource.ProjectedExcess30 => QuantitySourceExcess30,
            InventoryCommercialAttentionQuantitySource.None => EmDash,
            _ => "Origem não classificada",
        };

    public static string WarningLabel(InventoryPromotionSuggestionWarning warning) =>
        warning switch
        {
            InventoryPromotionSuggestionWarning.MinimumMarginPolicyAllowsAtCost =>
                WarningMinimumMarginAllowsAtCost,
            InventoryPromotionSuggestionWarning.WholesalePricingMayDiffer =>
                WarningWholesalePricingMayDiffer,
            _ => "Aviso não classificado",
        };

    public static string ReasonLabel(InventoryPromotionSuggestionReason reason) =>
        reason switch
        {
            InventoryPromotionSuggestionReason.None => EmDash,
            InventoryPromotionSuggestionReason.InvalidInput => "Dados inválidos",
            InventoryPromotionSuggestionReason.ScenarioMissing => "Cenário comercial ausente",
            InventoryPromotionSuggestionReason.DuplicateScenario => "Conflito de cenários comerciais",
            InventoryPromotionSuggestionReason.Expired => "Produto vencido",
            InventoryPromotionSuggestionReason.LocationLimitation => "Limitação de localização",
            InventoryPromotionSuggestionReason.ReviewData => "Revisar dados",
            InventoryPromotionSuggestionReason.LimitedConfidence => "Análise limitada",
            InventoryPromotionSuggestionReason.UnavailableConfidence => "Análise indisponível",
            InventoryPromotionSuggestionReason.ExpiresToday => "Vence hoje",
            InventoryPromotionSuggestionReason.NearExpiryWithoutSurplus => "Validade próxima",
            InventoryPromotionSuggestionReason.DatedWithoutSurplusInWindow => "Validade a acompanhar",
            InventoryPromotionSuggestionReason.IdleOnly => "Produto parado",
            InventoryPromotionSuggestionReason.HighCoverageOnly => "Cobertura elevada",
            InventoryPromotionSuggestionReason.PolicyMissing => "Margem mínima não configurada",
            InventoryPromotionSuggestionReason.PolicyInvalid => "Margem mínima inválida",
            InventoryPromotionSuggestionReason.MissingProduct => "Produto não encontrado",
            InventoryPromotionSuggestionReason.UnknownCost => "Custo atual desconhecido",
            InventoryPromotionSuggestionReason.InvalidCost => "Custo atual inválido",
            InventoryPromotionSuggestionReason.NotSellable => "Produto não vendável",
            InventoryPromotionSuggestionReason.CompositionProduct => "Produto composto",
            InventoryPromotionSuggestionReason.AmbiguousSaleUnit => "Unidade comercial ambígua",
            InventoryPromotionSuggestionReason.FinancialDataUnavailable => "Dados financeiros indisponíveis",
            InventoryPromotionSuggestionReason.NotApplicable => "Sem indicação promocional",
            InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus =>
                InventoryCommercialScenarioPresentation.ThesisExpirySurplus,
            InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess =>
                InventoryCommercialScenarioPresentation.ThesisExcess30,
            _ => "Motivo não classificado",
        };

    public static string ReasonExplanation(InventoryPromotionSuggestionReason reason) =>
        reason switch
        {
            InventoryPromotionSuggestionReason.None =>
                "Não há motivo promocional adicional para este produto.",
            InventoryPromotionSuggestionReason.InvalidInput =>
                "Há dados inconsistentes que impedem uma orientação promocional confiável. Confira o cadastro.",
            InventoryPromotionSuggestionReason.ScenarioMissing =>
                "A análise comercial deste produto não pôde ser composta. Trate como revisão estrutural da análise, não como diagnóstico de quantidade em estoque.",
            InventoryPromotionSuggestionReason.DuplicateScenario =>
                "Há mais de um cenário comercial para o mesmo produto. A orientação foi suspensa para não escolher um valor.",
            InventoryPromotionSuggestionReason.Expired =>
                "Produto vencido não recebe sugestão de promoção e deve ser retirado/conferido conforme as regras de validade.",
            InventoryPromotionSuggestionReason.LocationLimitation =>
                "Há limitação de localização (por exemplo geladeira) que impede uma conclusão promocional segura.",
            InventoryPromotionSuggestionReason.ReviewData =>
                "Os dados deste produto precisam ser conferidos antes de qualquer orientação promocional.",
            InventoryPromotionSuggestionReason.LimitedConfidence =>
                "Os dados disponíveis não sustentam uma sugestão numérica de promoção.",
            InventoryPromotionSuggestionReason.UnavailableConfidence =>
                "A análise não está disponível. Sem sugestão numérica de promoção.",
            InventoryPromotionSuggestionReason.ExpiresToday =>
                "O produto ainda está dentro da validade hoje, mas a primeira versão da análise prioriza saída/exposição sem sugerir redução de preço.",
            InventoryPromotionSuggestionReason.NearExpiryWithoutSurplus =>
                "Há validade em até 7 dias, sem sobra projetada. Priorize a saída. Esta versão não sugere redução de preço.",
            InventoryPromotionSuggestionReason.DatedWithoutSurplusInWindow =>
                "Há validade entre 8 e 30 dias, sem sobra projetada. Acompanhe a saída. Esta versão não sugere redução de preço.",
            InventoryPromotionSuggestionReason.IdleOnly =>
                "Baixo giro isoladamente não é suficiente para sugerir promoção.",
            InventoryPromotionSuggestionReason.HighCoverageOnly =>
                "Cobertura elevada isoladamente é sinal para acompanhamento, não para redução de preço.",
            InventoryPromotionSuggestionReason.PolicyMissing =>
                "Configure a margem mínima em Sistema → Política comercial para permitir análise financeira dos cenários.",
            InventoryPromotionSuggestionReason.PolicyInvalid =>
                "A margem mínima configurada é inválida. Corrija a política comercial em Sistema → Política comercial. Sem isso, não há simulação de cenário.",
            InventoryPromotionSuggestionReason.MissingProduct =>
                "O produto não foi encontrado no catálogo. Sem dados para orientar promoção.",
            InventoryPromotionSuggestionReason.UnknownCost =>
                "O custo médio atual é insuficiente para análise financeira. Custo desconhecido não é zero.",
            InventoryPromotionSuggestionReason.InvalidCost =>
                "O custo médio atual é inválido e não permite análise financeira dos cenários.",
            InventoryPromotionSuggestionReason.NotSellable =>
                "O produto está marcado como não vendável. Não há sugestão de promoção.",
            InventoryPromotionSuggestionReason.CompositionProduct =>
                "Produto composto. Esta análise não calcula promoção de kit nem de composição.",
            InventoryPromotionSuggestionReason.AmbiguousSaleUnit =>
                "A unidade comercial é ambígua. O SGDB não divide custo nem infere unidades por maço para orientar promoção.",
            InventoryPromotionSuggestionReason.FinancialDataUnavailable =>
                "Preço, custo ou margem necessários não estão disponíveis. Valores desconhecidos não são tratados como zero.",
            InventoryPromotionSuggestionReason.NotApplicable =>
                "Não há indicação promocional para este produto nesta análise.",
            InventoryPromotionSuggestionReason.SuggestedBecauseExpirySurplus =>
                "A projeção indica quantidade que pode permanecer em estoque até a data de validade considerada.",
            InventoryPromotionSuggestionReason.SuggestedBecauseProjectedExcess =>
                "A projeção indica estoque acima da demanda estimada para os próximos 30 dias.",
            _ => "Há uma situação comercial que não pôde ser descrita.",
        };

    static IReadOnlyList<InventoryCommercialScenarioOptionPresentation> PresentScenarios(
        InventoryPromotionSuggestionResult result,
        bool suggested)
    {
        if (!suggested)
            return [];

        var source = result.Scenarios ?? [];
        if (source.Count == 0)
            return [];

        return InventoryCommercialScenarioPresentation.FromResult(new InventoryCommercialScenarioResult
        {
            Status = InventoryCommercialScenarioStatus.Available,
            Scenarios = source,
        }).Scenarios;
    }

    static string DisclaimerOf(InventoryPromotionSuggestionStatus status) =>
        status switch
        {
            InventoryPromotionSuggestionStatus.Suggested => SuggestedDisclaimer,
            InventoryPromotionSuggestionStatus.Expired => ExpiredDisclaimer,
            _ => ShortDisclaimer,
        };

    static string FormatQuantity(double? quantity)
    {
        if (quantity is not double value || !double.IsFinite(value))
            return EmDash;
        return InventoryProjectionPresentation.FormatQty(value);
    }

    static InventoryPromotionSuggestionPresentationRow CloneWithProductId(
        InventoryPromotionSuggestionPresentationRow source,
        int productId) =>
        new()
        {
            ProductId = productId,
            Status = source.Status,
            StatusLabel = source.StatusLabel,
            Action = source.Action,
            ActionLabel = source.ActionLabel,
            Thesis = source.Thesis,
            ThesisLabel = source.ThesisLabel,
            Objective = source.Objective,
            ObjectiveLabel = source.ObjectiveLabel,
            Confidence = source.Confidence,
            ConfidenceLabel = source.ConfidenceLabel,
            AttentionPriority = source.AttentionPriority,
            PriorityLabel = source.PriorityLabel,
            PrimaryReason = source.PrimaryReason,
            PrimaryReasonLabel = source.PrimaryReasonLabel,
            Explanation = source.Explanation,
            SecondaryReasons = source.SecondaryReasons,
            SecondaryReasonLabels = source.SecondaryReasonLabels,
            Warnings = source.Warnings,
            WarningLabels = source.WarningLabels,
            AttentionQuantityLabel = source.AttentionQuantityLabel,
            AttentionQuantityText = source.AttentionQuantityText,
            AttentionQuantitySource = source.AttentionQuantitySource,
            AttentionQuantitySourceLabel = source.AttentionQuantitySourceLabel,
            ScenarioOptions = source.ScenarioOptions,
            DisclaimerText = source.DisclaimerText,
            IsSuggested = source.IsSuggested,
            IsReviewData = source.IsReviewData,
            IsExpired = source.IsExpired,
            IsJoinMissing = source.IsJoinMissing,
        };
}
