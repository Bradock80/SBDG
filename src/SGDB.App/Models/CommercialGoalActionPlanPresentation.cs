using SGDB.Domain.Commercial;

namespace SGDB.Models;

/// <summary>
/// Item PT-BR do plano de atenção comercial. Sem WPF.
/// </summary>
public sealed class CommercialGoalActionPlanItemPresentation
{
    public int ProductId { get; init; }
    public CommercialGoalActionType ActionType { get; init; }
    public InventoryAttentionPriority Priority { get; init; }
    public InventoryAttentionConfidence Confidence { get; init; }
    public string PriorityText { get; init; } = "";
    public string ProductTitle { get; init; } = "";
    public string ReasonText { get; init; } = "";
    public string ComplementsText { get; init; } = "";
    public bool HasComplements => ComplementsText.Length > 0;
    public string ConfidenceText { get; init; } = "";
    public bool HasPromotionSuggestion { get; init; }
    public bool HasComboSuggestion { get; init; }
    public InventoryPurchaseGuidanceAction PurchaseGuidanceAction { get; init; }
    public CommercialGoalPresentationTone Tone { get; init; }
}

/// <summary>
/// Snapshot PT-BR 71B-B7. Consome o plano composto. Sem I/O.
/// </summary>
public sealed class CommercialGoalActionPlanPresentationSnapshot
{
    public CommercialCompetence Competence { get; init; }
    public DateOnly ReferenceDate { get; init; }
    public CommercialGoalActionPlanMode Mode { get; init; }
    public CommercialGoalStatus? GoalStatus { get; init; }
    public int QueryCount { get; init; }
    public int CandidateCount { get; init; }

    public string SectionTitle { get; init; } = CommercialGoalActionPlanPresentation.SectionTitle;
    public string Headline { get; init; } = "";
    public string SupportingText { get; init; } = "";
    public string EmptyText { get; init; } = "";
    public bool IsEmpty { get; init; }
    public bool IsFutureCompetence { get; init; }

    public IReadOnlyList<CommercialGoalActionPlanItemPresentation> Items { get; init; } = [];
    public IReadOnlyList<CommercialGoalLimitationPresentation> Limitations { get; init; } = [];
}

/// <summary>
/// Apresentação PT-BR 71B-B7. Orientação qualitativa. Sem causalidade de demanda.
/// </summary>
public static class CommercialGoalActionPlanPresentation
{
    public const int ExpectedQueryCount = 0;

    public const string SectionTitle = "Prioridades comerciais";
    public const string CountOne = "1 ponto merece atenção";
    public const string EmptyMessage =
        "Nenhuma prioridade comercial relevante foi identificada com os dados disponíveis.";

    public const string HeadlineOperational = "Prioridades comerciais do mês";
    public const string HeadlineBelowPace = "Meta abaixo do ritmo — avalie oportunidades seguras";
    public const string HeadlineOnPace = "Meta no ritmo — equilibre giro e proteção do estoque";
    public const string HeadlineAbovePace = "Meta acima do ritmo — preserve margem e cuide do estoque";
    public const string HeadlineAchieved = "Meta atingida — preserve margem e cuide do estoque";
    public const string HeadlineNoGoal = "Sem meta — prioridades baseadas no estoque";
    public const string HeadlineInvalidGoal = "Meta inválida — prioridades baseadas no estoque";
    public const string HeadlineUnavailable = "Lucro indisponível — prioridades baseadas no estoque";
    public const string HeadlineEstimated = "Lucro estimado — prioridades qualitativas do estoque";
    public const string HeadlineFuture =
        "Mês ainda não iniciado — o plano operacional começa com a competência";

    public const string SupportingFuture =
        "A orientação abaixo não representa ações para um mês que ainda não começou.";
    public const string SupportingNoGoal =
        "Estas prioridades não representam perseguição de uma meta financeira.";
    public const string SupportingInvalid =
        "A meta configurada não é usada numericamente nestas prioridades.";
    public const string SupportingUnavailable =
        "As prioridades de estoque não se relacionam quantitativamente à meta.";
    public const string SupportingEstimated =
        "As ações não estimam recuperação financeira da meta.";

    public const string ReasonReviewData = "Priorize a conferência dos dados deste produto.";
    public const string ReasonRemoveExpired = "Há produto vencido para retirada.";
    public const string ReasonExpiry = "Há risco ou sobra antes da validade.";
    public const string ReasonExcess = "Há excesso projetado.";
    public const string ReasonIdle = "Produto parado com estoque disponível.";
    public const string ReasonProtect = "Reposição merece atenção para preservar disponibilidade.";
    public const string ReasonMonitor = "Acompanhe este produto.";

    public const string ComplementPromotion = "Há cenário de promoção para avaliação.";
    public const string ComplementCombo = "Há combinação comercial para avaliação.";
    public const string ComplementDoNotReplenish = "Não repor agora.";
    public const string ComplementReplenish = "Reposição merece atenção.";

    public static CommercialGoalActionPlanPresentationSnapshot Empty { get; } = new()
    {
        Headline = HeadlineOperational,
        EmptyText = EmptyMessage,
        IsEmpty = true,
    };

    public static CommercialGoalActionPlanPresentationSnapshot Apply(
        CommercialGoalActionPlanSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var items = new CommercialGoalActionPlanItemPresentation[snapshot.Items.Count];
        for (var i = 0; i < snapshot.Items.Count; i++)
            items[i] = PresentItem(snapshot.Items[i]);

        var isFuture = snapshot.Mode == CommercialGoalActionPlanMode.FutureCompetence;
        var isEmpty = items.Length == 0;
        var (headline, supporting) = PresentHeadline(snapshot, isEmpty);
        return new CommercialGoalActionPlanPresentationSnapshot
        {
            Competence = snapshot.Competence,
            ReferenceDate = snapshot.ReferenceDate,
            Mode = snapshot.Mode,
            GoalStatus = snapshot.GoalStatus,
            QueryCount = snapshot.QueryCount,
            CandidateCount = snapshot.CandidateCount,
            SectionTitle = SectionTitle,
            Headline = headline,
            SupportingText = supporting,
            EmptyText = isEmpty && !isFuture ? EmptyMessage : "",
            IsEmpty = isEmpty,
            IsFutureCompetence = isFuture,
            Items = items,
            Limitations = PresentLimitations(snapshot),
        };
    }

    static (string Headline, string Supporting) PresentHeadline(
        CommercialGoalActionPlanSnapshot snapshot,
        bool isEmpty)
    {
        if (snapshot.Mode == CommercialGoalActionPlanMode.FutureCompetence)
            return (HeadlineFuture, SupportingFuture);

        var headline = snapshot.Mode == CommercialGoalActionPlanMode.InventoryOnly
            ? InventoryHeadline(snapshot)
            : OperationalHeadline(snapshot);

        if (isEmpty)
            return (headline, EmptyMessage);

        var count = CountText(snapshot.Items.Count);
        var supporting = InventorySupporting(snapshot);
        if (supporting.Length == 0)
            return (headline, count);
        return (headline, count + " " + supporting);
    }

    static string OperationalHeadline(CommercialGoalActionPlanSnapshot snapshot) =>
        snapshot.GoalStatus switch
        {
            CommercialGoalStatus.BelowPace => HeadlineBelowPace,
            CommercialGoalStatus.OnPace => HeadlineOnPace,
            CommercialGoalStatus.AbovePace => HeadlineAbovePace,
            CommercialGoalStatus.Achieved => HeadlineAchieved,
            _ => HeadlineOperational,
        };

    static string InventoryHeadline(CommercialGoalActionPlanSnapshot snapshot)
    {
        if (snapshot.HasLimitation(CommercialGoalActionLimitation.FinancialUnavailable)
            || snapshot.FinancialQuality == CommercialGoalCostQuality.Unavailable
            || snapshot.ProgressSkipReason.HasFlag(CommercialGoalProgressSkipReason.GrossProfitUnavailable))
        {
            return HeadlineUnavailable;
        }

        if (snapshot.ProgressSkipReason.HasFlag(CommercialGoalProgressSkipReason.InvalidGoalConfiguration)
            || snapshot.GoalStatus == CommercialGoalStatus.InvalidGoal)
        {
            return HeadlineInvalidGoal;
        }

        if (!snapshot.HasValidGoal || snapshot.GoalStatus is null or CommercialGoalStatus.NoGoal)
            return HeadlineNoGoal;

        if (snapshot.HasLimitation(CommercialGoalActionLimitation.LegacyCostEstimate))
            return HeadlineEstimated;

        return HeadlineOperational;
    }

    static string InventorySupporting(CommercialGoalActionPlanSnapshot snapshot)
    {
        if (snapshot.Mode != CommercialGoalActionPlanMode.InventoryOnly)
        {
            if (snapshot.HasLimitation(CommercialGoalActionLimitation.LegacyCostEstimate))
                return SupportingEstimated;
            return "";
        }

        if (snapshot.HasLimitation(CommercialGoalActionLimitation.FinancialUnavailable)
            || snapshot.FinancialQuality == CommercialGoalCostQuality.Unavailable
            || snapshot.ProgressSkipReason.HasFlag(CommercialGoalProgressSkipReason.GrossProfitUnavailable))
        {
            return SupportingUnavailable;
        }

        if (snapshot.ProgressSkipReason.HasFlag(CommercialGoalProgressSkipReason.InvalidGoalConfiguration)
            || snapshot.GoalStatus == CommercialGoalStatus.InvalidGoal)
        {
            return SupportingInvalid;
        }
        if (snapshot.HasLimitation(CommercialGoalActionLimitation.LegacyCostEstimate))
            return SupportingEstimated;
        return SupportingNoGoal;
    }

    static string CountText(int count)
    {
        if (count == 1)
            return CountOne;
        return $"{count} pontos merecem atenção";
    }

    static CommercialGoalActionPlanItemPresentation PresentItem(CommercialGoalActionItem item)
    {
        var tone = item.ActionType switch
        {
            CommercialGoalActionType.ReviewData or CommercialGoalActionType.RemoveExpired =>
                CommercialGoalPresentationTone.Warning,
            CommercialGoalActionType.PrioritizeExpiryRisk
                or CommercialGoalActionType.PrioritizeExcess
                or CommercialGoalActionType.PrioritizeIdle
                or CommercialGoalActionType.ProtectAvailability =>
                CommercialGoalPresentationTone.Attention,
            _ => CommercialGoalPresentationTone.Neutral,
        };

        return new CommercialGoalActionPlanItemPresentation
        {
            ProductId = item.ProductId,
            ActionType = item.ActionType,
            Priority = item.Priority,
            Confidence = item.Confidence,
            PriorityText = InventoryAttentionPresentation.PriorityLabel(item.Priority),
            ProductTitle = ProductTitle(item),
            ReasonText = ReasonText(item.ActionType),
            ComplementsText = ComplementsText(item),
            ConfidenceText = InventoryAttentionPresentation.ConfidenceLabel(item.Confidence),
            HasPromotionSuggestion = item.HasPromotionSuggestion,
            HasComboSuggestion = item.HasComboSuggestion,
            PurchaseGuidanceAction = item.PurchaseGuidanceAction,
            Tone = tone,
        };
    }

    static string ProductTitle(CommercialGoalActionItem item)
    {
        if (item.ProductCode.Length > 0 && item.ProductName.Length > 0)
            return item.ProductCode + " — " + item.ProductName;
        if (item.ProductName.Length > 0)
            return item.ProductName;
        if (item.ProductCode.Length > 0)
            return item.ProductCode;
        return "#" + item.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    static string ReasonText(CommercialGoalActionType actionType) =>
        actionType switch
        {
            CommercialGoalActionType.ReviewData => ReasonReviewData,
            CommercialGoalActionType.RemoveExpired => ReasonRemoveExpired,
            CommercialGoalActionType.PrioritizeExpiryRisk => ReasonExpiry,
            CommercialGoalActionType.PrioritizeExcess => ReasonExcess,
            CommercialGoalActionType.PrioritizeIdle => ReasonIdle,
            CommercialGoalActionType.ProtectAvailability => ReasonProtect,
            _ => ReasonMonitor,
        };

    static string ComplementsText(CommercialGoalActionItem item)
    {
        var parts = new List<string>(3);
        if (item.HasPromotionSuggestion)
            parts.Add(ComplementPromotion);
        if (item.HasComboSuggestion)
            parts.Add(ComplementCombo);
        if (item.ActionType != CommercialGoalActionType.ProtectAvailability)
        {
            if (item.PurchaseGuidanceAction == InventoryPurchaseGuidanceAction.DoNotReplenishNow)
                parts.Add(ComplementDoNotReplenish);
            else if (item.PurchaseGuidanceAction == InventoryPurchaseGuidanceAction.ConsiderReplenishment)
                parts.Add(ComplementReplenish);
        }

        return string.Join(" ", parts);
    }

    static IReadOnlyList<CommercialGoalLimitationPresentation> PresentLimitations(
        CommercialGoalActionPlanSnapshot snapshot)
    {
        var list = new List<CommercialGoalLimitationPresentation>(3);
        if (snapshot.HasLimitation(CommercialGoalActionLimitation.FinancialUnavailable))
        {
            list.Add(new CommercialGoalLimitationPresentation
            {
                Key = "financial-unavailable",
                Title = HeadlineUnavailable,
                Body = SupportingUnavailable,
                IsProminent = true,
            });
        }

        if (snapshot.HasLimitation(CommercialGoalActionLimitation.LegacyCostEstimate))
        {
            list.Add(new CommercialGoalLimitationPresentation
            {
                Key = "legacy",
                Title = CommercialGoalPresentation.EstimatedBadge,
                Body = SupportingEstimated,
                IsProminent = false,
            });
        }

        return list;
    }
}
