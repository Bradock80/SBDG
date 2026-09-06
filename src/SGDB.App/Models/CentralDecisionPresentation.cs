using SGDB.Domain.Commercial;

namespace SGDB.Models;

/// <summary>Item executivo Agir agora. Sem WPF. Ordem = B2.</summary>
public sealed class CentralDecisionActNowPresentation
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductTitle { get; init; } = "";
    public CommercialGoalActionType ActionType { get; init; }
    public string WhatText { get; init; } = "";
    public string WhyText { get; init; } = "";
    public string CareText { get; init; } = "";
    public InventoryAttentionConfidence Confidence { get; init; }
    public string ConfidenceText { get; init; } = "";
    public string GrossProfitText { get; init; } = CommercialGoalPresentation.EmDash;
    public string GrossMarginText { get; init; } = CommercialGoalPresentation.EmDash;
    public string CostQualityText { get; init; } = "";
    public bool HasPromotionSuggestion { get; init; }
    public string PromotionText { get; init; } = "";
    public bool HasComboSuggestion { get; init; }
    public string ComboText { get; init; } = "";
    public string ReplenishmentText { get; init; } = "";
    public string QuantityText { get; init; } = "";
    public InventorySmartArea Area { get; init; }
    public string AreaText { get; init; } = "";
    public CommercialGoalPresentationTone Tone { get; init; }
}

/// <summary>Item informativo Preservar. Não é ação primária.</summary>
public sealed class CentralDecisionPreservePresentation
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductTitle { get; init; } = "";
    public string GrossProfitText { get; init; } = CommercialGoalPresentation.EmDash;
    public string GrossMarginText { get; init; } = CommercialGoalPresentation.EmDash;
    public string CoverageText { get; init; } = "";
    public string CostQualityText { get; init; } = "";
    public CommercialGoalPresentationTone Tone { get; init; } =
        CommercialGoalPresentationTone.Positive;
}

/// <summary>Faixa compacta da Meta. Sem projeção, ritmo ou forecast.</summary>
public sealed class CentralDecisionGoalStripPresentation
{
    public required CommercialGoalMetricPresentation Goal { get; init; }
    public required CommercialGoalMetricPresentation Realized { get; init; }
    public required CommercialGoalMetricPresentation Remaining { get; init; }
    public required CommercialGoalMetricPresentation Status { get; init; }
    public string InventoryOnlyNote { get; init; } = "";
    public string EstimatedBadge { get; init; } = "";
    public bool ShowEstimatedBadge => EstimatedBadge.Length > 0;
}

/// <summary>Snapshot PT-BR 71C-B3. Consome B2. Sem I/O.</summary>
public sealed class CentralDecisionPresentationSnapshot
{
    public CentralDecisionState State { get; init; }
    public CommercialCompetence Competence { get; init; }
    public DateOnly ReferenceDate { get; init; }
    public int QueryCount { get; init; }

    public string Title { get; init; } = CentralDecisionPresentation.Title;
    public string Subtitle { get; init; } = CentralDecisionPresentation.Subtitle;
    public string Headline { get; init; } = "";
    public string SupportingText { get; init; } = "";
    public string EmptyText { get; init; } = "";
    public string ActNowTitle { get; init; } = CentralDecisionPresentation.ActNowTitle;
    public string PreserveTitle { get; init; } = CentralDecisionPresentation.PreserveTitle;
    public string PreserveSubtitle { get; init; } = CentralDecisionPresentation.PreserveSubtitle;

    public required CentralDecisionGoalStripPresentation GoalStrip { get; init; }
    public IReadOnlyList<CentralDecisionActNowPresentation> ActNow { get; init; } = [];
    public IReadOnlyList<CentralDecisionPreservePresentation> Preserve { get; init; } = [];
    public IReadOnlyList<CommercialGoalLimitationPresentation> Limitations { get; init; } = [];
}

/// <summary>
/// Apresentação executiva 71C-B3. Traduz B2. Sem ranking, SQL, I/O ou WPF.
/// </summary>
public static class CentralDecisionPresentation
{
    public const int ExpectedQueryCount = 0;

    public const string Title = "Central de Decisão";
    public const string Subtitle =
        "Veja o que merece atenção agora, por quê e quais produtos convém preservar.";

    public const string ActNowTitle = "O que fazer hoje";
    public const string PreserveTitle = "Preservar";
    public const string PreserveSubtitle =
        "Produtos com boa contribuição histórica e estoque em condição adequada, "
        + "sem necessidade atual de acelerar a saída.";

    public const string HeadlineOperational = "Orientações com base nos dados disponíveis.";
    public const string HeadlineLimited =
        "Há orientações disponíveis, com limitações que o operador deve considerar.";
    public const string HeadlineEmpty =
        "Nenhuma ação prioritária foi identificada neste momento.";
    public const string HeadlineUnavailable =
        "Não foi possível montar a Central de Decisão com segurança.";
    public const string HeadlineFuture =
        "A Central de Decisão operacional não é calculada para competências futuras.";

    public const string InventoryOnlyNote =
        "As orientações abaixo seguem o estoque; não há perseguição numérica de uma meta válida neste estado.";

    public const string WhatReviewData = "Corrigir dados";
    public const string WhatReviewNegativeStock = "Corrigir estoque negativo";
    public const string WhatReviewLotDivergence = "Corrigir divergência de lote";
    public const string WhatRemoveExpired = "Retirar produto vencido";
    public const string WhatExpiry = "Priorizar validade";
    public const string WhatExcess = "Suspender compra";
    public const string WhatIdle = "Suspender compra";
    public const string WhatProtect = "Comprar agora";
    public const string WhatMonitor = "Acompanhar";

    public const string WhyReviewData =
        "Os dados precisam ser corrigidos antes de uma decisão comercial.";
    public const string WhyRemoveExpired = "Há produto vencido para retirada.";
    public const string WhyExpiry = "Há risco de sobra antes da validade.";
    public const string WhyExpiresToday = "O produto vence hoje.";
    public const string WhyExcess = "Há excesso projetado para os próximos 30 dias.";
    public const string WhyIdle = "O produto apresenta baixo giro.";
    public const string WhyProtect = "O estoque disponível exige proteção.";
    public const string WhyMonitor = "Acompanhe este produto.";

    public const string CareReviewData =
        "A decisão comercial deve aguardar a correção dos dados.";
    public const string CareProtect =
        "Não acelerar a saída enquanto houver indicação de reposição.";
    public const string CareInsufficient =
        "Não há dados suficientes para uma decisão comercial segura.";
    public const string CareLocation = "Há limitação na localização do estoque.";
    public const string CareNoEvidence = "Falta evidência física do estoque.";
    public const string CareStructural = "Há conflito ou inconsistência nos dados.";
    public const string CareEstimated = "Os valores de lucro usam custo estimado.";
    public const string CareDoNotReplenish = "Não repor agora.";
    public const string CareConsiderReplenish = "Considerar reposição";

    public const string PromotionText = "Promoção sugerida disponível";
    public const string ComboText = "Existe oportunidade de combo";

    public const string LimitationContributionTitle = "Contribuição histórica limitada";
    public const string LimitationContributionBody =
        "O lucro realizado por produto não está disponível com segurança nesta competência.";
    public const string LimitationIntelligenceTitle = "Inteligência de estoque indisponível";
    public const string LimitationIntelligenceBody =
        "Não foi possível usar a leitura de estoque com segurança.";
    public const string LimitationPromotionTitle = "Promoção limitada";
    public const string LimitationPromotionBody =
        "A leitura de promoção não está completa nesta montagem.";
    public const string LimitationComboTitle = "Combo limitado";
    public const string LimitationComboBody =
        "A leitura de combo não está completa nesta montagem.";

    public static CentralDecisionPresentationSnapshot Apply(CentralDecisionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var actNow = new CentralDecisionActNowPresentation[snapshot.ActNow.Count];
        for (var i = 0; i < snapshot.ActNow.Count; i++)
            actNow[i] = PresentActNow(snapshot.ActNow[i]);

        var preserve = new CentralDecisionPreservePresentation[snapshot.Preserve.Count];
        for (var i = 0; i < snapshot.Preserve.Count; i++)
            preserve[i] = PresentPreserve(snapshot.Preserve[i]);

        var (headline, supporting, empty) = PresentState(snapshot.State);
        return new CentralDecisionPresentationSnapshot
        {
            State = snapshot.State,
            Competence = snapshot.Competence,
            ReferenceDate = snapshot.ReferenceDate,
            QueryCount = ExpectedQueryCount,
            Headline = headline,
            SupportingText = supporting,
            EmptyText = empty,
            GoalStrip = PresentGoalStrip(snapshot),
            ActNow = actNow,
            Preserve = preserve,
            Limitations = PresentLimitations(snapshot),
        };
    }

    static (string Headline, string Supporting, string Empty) PresentState(CentralDecisionState state) =>
        state switch
        {
            CentralDecisionState.Limited => (HeadlineLimited, HeadlineLimited, ""),
            CentralDecisionState.Empty => (HeadlineEmpty, HeadlineEmpty, HeadlineEmpty),
            CentralDecisionState.Unavailable => (HeadlineUnavailable, HeadlineUnavailable, HeadlineUnavailable),
            CentralDecisionState.Future => (HeadlineFuture, HeadlineFuture, HeadlineFuture),
            _ => (HeadlineOperational, Subtitle, ""),
        };

    static CentralDecisionGoalStripPresentation PresentGoalStrip(CentralDecisionSnapshot snapshot)
    {
        var goal = snapshot.Goal;
        var unavailable = snapshot.ProgressSkipReason.HasFlag(
            CommercialGoalProgressSkipReason.GrossProfitUnavailable)
            || snapshot.FinancialQuality == CommercialGoalCostQuality.Unavailable
            || goal?.GrossProfitAvailable == false;
        var invalid = snapshot.ProgressSkipReason.HasFlag(
            CommercialGoalProgressSkipReason.InvalidGoalConfiguration)
            || snapshot.GoalStatus == CommercialGoalStatus.InvalidGoal;
        var noGoal = !invalid
            && (snapshot.GoalStatus == CommercialGoalStatus.NoGoal
                || goal is { HasValidGoal: false }
                || goal is null);
        var estimated = snapshot.FinancialQuality == CommercialGoalCostQuality.EstimatedLegacy
            || goal?.HasLimitation(CommercialGoalLimitation.LegacyCostEstimate) == true;

        var goalText = invalid
            ? CommercialGoalPresentation.GoalInvalid
            : noGoal
                ? CommercialGoalPresentation.GoalNotConfigured
                : CommercialGoalPresentation.FormatMoney(snapshot.GoalAmount);

        var realizedText = unavailable
            ? CommercialGoalPresentation.EmDash
            : CommercialGoalPresentation.FormatMoney(snapshot.RealizedGrossProfit);
        var remainingText = unavailable || invalid || noGoal
            ? CommercialGoalPresentation.EmDash
            : CommercialGoalPresentation.FormatMoney(snapshot.RemainingAmount);

        var statusText = PresentStatus(snapshot.GoalStatus, invalid, noGoal, unavailable);
        var inventoryOnly = snapshot.PlanMode == CommercialGoalActionPlanMode.InventoryOnly
            || noGoal
            || invalid
            || unavailable;

        return new CentralDecisionGoalStripPresentation
        {
            Goal = Metric("goal", CommercialGoalPresentation.CardGoal, goalText, !noGoal && !invalid),
            Realized = Metric(
                "realized",
                CommercialGoalPresentation.CardRealized,
                realizedText,
                !unavailable,
                estimated ? CommercialGoalPresentation.EstimatedBadge : ""),
            Remaining = Metric(
                "remaining",
                CommercialGoalPresentation.CardRemaining,
                remainingText,
                !unavailable && !invalid && !noGoal),
            Status = Metric(
                "status",
                CommercialGoalPresentation.CardStatus,
                statusText,
                true,
                tone: StatusTone(snapshot.GoalStatus, invalid, unavailable)),
            InventoryOnlyNote = inventoryOnly ? InventoryOnlyNote : "",
            EstimatedBadge = estimated ? CommercialGoalPresentation.EstimatedBadge : "",
        };
    }

    static string PresentStatus(
        CommercialGoalStatus? status,
        bool invalid,
        bool noGoal,
        bool unavailable)
    {
        if (unavailable && status is null)
            return CommercialGoalPresentation.HeadlineUnavailable;
        if (invalid)
            return CommercialGoalPresentation.StatusInvalidGoal;
        if (noGoal)
            return CommercialGoalPresentation.StatusNoGoal;
        return status switch
        {
            CommercialGoalStatus.Achieved => CommercialGoalPresentation.StatusAchieved,
            CommercialGoalStatus.AbovePace => CommercialGoalPresentation.StatusAbovePace,
            CommercialGoalStatus.OnPace => CommercialGoalPresentation.StatusOnPace,
            CommercialGoalStatus.BelowPace => CommercialGoalPresentation.StatusBelowPace,
            CommercialGoalStatus.NotStarted => CommercialGoalPresentation.StatusNotStarted,
            CommercialGoalStatus.InvalidGoal => CommercialGoalPresentation.StatusInvalidGoal,
            CommercialGoalStatus.NoGoal => CommercialGoalPresentation.StatusNoGoal,
            _ => CommercialGoalPresentation.EmDash,
        };
    }

    static CommercialGoalPresentationTone StatusTone(
        CommercialGoalStatus? status,
        bool invalid,
        bool unavailable)
    {
        if (unavailable)
            return CommercialGoalPresentationTone.Unavailable;
        if (invalid)
            return CommercialGoalPresentationTone.Warning;
        return status switch
        {
            CommercialGoalStatus.Achieved or CommercialGoalStatus.AbovePace =>
                CommercialGoalPresentationTone.Positive,
            CommercialGoalStatus.BelowPace => CommercialGoalPresentationTone.Attention,
            CommercialGoalStatus.InvalidGoal => CommercialGoalPresentationTone.Warning,
            _ => CommercialGoalPresentationTone.Neutral,
        };
    }

    static CentralDecisionActNowPresentation PresentActNow(CentralDecisionActNowItem item)
    {
        var action = item.Action;
        var suppressCommercial = action.ActionType is CommercialGoalActionType.ReviewData
            or CommercialGoalActionType.RemoveExpired
            or CommercialGoalActionType.ProtectAvailability;
        var showPromotion = !suppressCommercial && action.HasPromotionSuggestion;
        var showCombo = !suppressCommercial && action.HasComboSuggestion;
        var (gpText, marginText, qualityText) = PresentContribution(item.Contribution);

        return new CentralDecisionActNowPresentation
        {
            ProductId = action.ProductId,
            ProductCode = action.ProductCode,
            ProductName = action.ProductName,
            ProductTitle = ProductTitle(action.ProductCode, action.ProductName, action.ProductId),
            ActionType = action.ActionType,
            WhatText = WhatText(action),
            WhyText = WhyText(action),
            CareText = CareText(action),
            Confidence = action.Confidence,
            ConfidenceText = ConfidenceTextOf(action, item.Contribution),
            GrossProfitText = gpText,
            GrossMarginText = marginText,
            CostQualityText = qualityText,
            HasPromotionSuggestion = showPromotion,
            PromotionText = showPromotion ? PromotionText : "",
            HasComboSuggestion = showCombo,
            ComboText = showCombo ? ComboText : "",
            ReplenishmentText = ReplenishmentText(action),
            QuantityText = QuantityOf(action),
            Area = AreaOf(action.ActionType),
            AreaText = InventorySmartPresentation.AreaLabel(AreaOf(action.ActionType)),
            Tone = ActNowTone(action.ActionType),
        };
    }

    static CentralDecisionPreservePresentation PresentPreserve(CentralDecisionPreserveItem item)
    {
        var (gpText, marginText, qualityText) = PresentContribution(item.Contribution);
        return new CentralDecisionPreservePresentation
        {
            ProductId = item.ProductId,
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            ProductTitle = ProductTitle(item.ProductCode, item.ProductName, item.ProductId),
            GrossProfitText = gpText,
            GrossMarginText = marginText,
            CoverageText = CoverageText(item.CoverageBand),
            CostQualityText = qualityText,
            Tone = CommercialGoalPresentationTone.Positive,
        };
    }

    static (string Gp, string Margin, string Quality) PresentContribution(
        CommercialGoalProductContributionRow? row)
    {
        if (row is null)
            return (CommercialGoalPresentation.EmDash, CommercialGoalPresentation.EmDash, "");

        var gp = row.GrossProfit.HasValue
            ? CommercialGoalPresentation.FormatMoney(row.GrossProfit)
            : CommercialGoalPresentation.EmDash;
        var margin = row.GrossMarginPercent is { } percent
            ? percent.ToString("N2", Utils.ProductPriceHelper.Br) + "%"
            : CommercialGoalPresentation.EmDash;
        var quality = row.CostQuality switch
        {
            CommercialGoalCostQuality.EstimatedLegacy =>
                CommercialGoalProductContributionPresentation.QualityEstimated,
            CommercialGoalCostQuality.Unavailable =>
                CommercialGoalProductContributionPresentation.QualityUnavailable,
            _ => CommercialGoalProductContributionPresentation.QualityExact,
        };
        return (gp, margin, quality);
    }

    static string WhatText(CommercialGoalActionType type) =>
        type switch
        {
            CommercialGoalActionType.ReviewData => WhatReviewData,
            CommercialGoalActionType.RemoveExpired => WhatRemoveExpired,
            CommercialGoalActionType.PrioritizeExpiryRisk => WhatExpiry,
            CommercialGoalActionType.PrioritizeExcess => WhatExcess,
            CommercialGoalActionType.PrioritizeIdle => WhatIdle,
            CommercialGoalActionType.ProtectAvailability => WhatProtect,
            _ => WhatMonitor,
        };

    static string WhatText(CommercialGoalActionItem action)
    {
        if (action.ActionType == CommercialGoalActionType.ProtectAvailability
            && action.RecommendedQuantity is double qty)
        {
            return InventorySmartPresentation.PackagingText(qty, action.PackCount, action.PackFactor);
        }

        if (action.ActionType == CommercialGoalActionType.ReviewData)
        {
            return action.AttentionReason switch
            {
                InventoryAttentionReason.NegativeStock
                    or InventoryAttentionReason.NegativeLocationStock
                    or InventoryAttentionReason.NegativeWarehouseStock =>
                    WhatReviewNegativeStock,
                InventoryAttentionReason.TrackedQuantityExceedsWarehouse
                    or InventoryAttentionReason.InconsistentStockTotals =>
                    WhatReviewLotDivergence,
                _ => WhatReviewData,
            };
        }

        return WhatText(action.ActionType);
    }

    static InventorySmartArea AreaOf(CommercialGoalActionType type) =>
        type switch
        {
            CommercialGoalActionType.RemoveExpired
                or CommercialGoalActionType.PrioritizeExpiryRisk => InventorySmartArea.Urgent,
            CommercialGoalActionType.ProtectAvailability => InventorySmartArea.Buy,
            CommercialGoalActionType.PrioritizeExcess
                or CommercialGoalActionType.PrioritizeIdle => InventorySmartArea.SellFaster,
            CommercialGoalActionType.ReviewData => InventorySmartArea.FixData,
            _ => InventorySmartArea.Preserve,
        };

    static string QuantityOf(CommercialGoalActionItem action)
    {
        if (action.RecommendedQuantity is double qty)
            return InventorySmartPresentation.PackagingText(qty, action.PackCount, action.PackFactor);
        if (action.NearestDatedDaysUntilExpiry is int days)
            return days <= 0 ? "vence hoje" : $"vence em {days} dia(s)";
        return "";
    }

    static string WhyText(CommercialGoalActionItem action) =>
        action.AttentionReason switch
        {
            InventoryAttentionReason.ExpiresToday => WhyExpiresToday,
            InventoryAttentionReason.Expired => WhyRemoveExpired,
            InventoryAttentionReason.SurplusAtExpiry
                or InventoryAttentionReason.NearExpiryWithoutSurplus => WhyExpiry,
            InventoryAttentionReason.ProjectedExcess30 => WhyExcess,
            InventoryAttentionReason.Idle => WhyIdle,
            _ => action.ActionType switch
            {
                CommercialGoalActionType.ReviewData => WhyReviewDataOf(action),
                CommercialGoalActionType.RemoveExpired => WhyRemoveExpired,
                CommercialGoalActionType.PrioritizeExpiryRisk => WhyExpiry,
                CommercialGoalActionType.PrioritizeExcess => WhyExcess,
                CommercialGoalActionType.PrioritizeIdle => WhyIdle,
                CommercialGoalActionType.ProtectAvailability => WhyProtect,
                _ => WhyMonitor,
            },
        };

    static string WhyReviewDataOf(CommercialGoalActionItem action) =>
        action.AttentionReason switch
        {
            InventoryAttentionReason.NegativeStock
                or InventoryAttentionReason.NegativeLocationStock
                or InventoryAttentionReason.NegativeWarehouseStock =>
                "O estoque está negativo e precisa ser conferido.",
            InventoryAttentionReason.TrackedQuantityExceedsWarehouse
                or InventoryAttentionReason.InconsistentStockTotals =>
                "O saldo dos lotes diverge do saldo do produto.",
            InventoryAttentionReason.InvalidLotQuantity =>
                "Há lote sem quantidade confiável.",
            _ => WhyReviewData,
        };

    static string ConfidenceTextOf(
        CommercialGoalActionItem action,
        CommercialGoalProductContributionRow? contribution)
    {
        if (action.Confidence == InventoryAttentionConfidence.Unavailable
            && contribution?.GrossProfit is not null)
        {
            return InventoryAttentionPresentation.ConfidenceLimited;
        }

        return InventoryAttentionPresentation.ConfidenceLabel(action.Confidence);
    }

    static string CareText(CommercialGoalActionItem action)
    {
        var parts = new List<string>(4);
        if (action.ActionType == CommercialGoalActionType.ReviewData)
            parts.Add(CareReviewData);
        if (action.ActionType == CommercialGoalActionType.ProtectAvailability
            || action.PurchaseGuidanceAction == InventoryPurchaseGuidanceAction.ConsiderReplenishment)
        {
            parts.Add(CareProtect);
        }

        if (action.PurchaseGuidanceAction == InventoryPurchaseGuidanceAction.DoNotReplenishNow)
            parts.Add(CareDoNotReplenish);

        if (action.Limitations.HasFlag(CommercialGoalActionLimitation.LocationLimitation))
            parts.Add(CareLocation);
        if (action.Limitations.HasFlag(CommercialGoalActionLimitation.InsufficientHistory)
            || (action.Confidence == InventoryAttentionConfidence.Unavailable
                && !action.Limitations.HasFlag(CommercialGoalActionLimitation.FinancialUnavailable)
                && action.ActionType == CommercialGoalActionType.ReviewData))
        {
            parts.Add(CareInsufficient);
        }

        if (action.Limitations.HasFlag(CommercialGoalActionLimitation.NoPhysicalEvidence))
            parts.Add(CareNoEvidence);
        if (action.Limitations.HasFlag(CommercialGoalActionLimitation.StructuralDataIssue))
            parts.Add(CareStructural);
        if (action.Limitations.HasFlag(CommercialGoalActionLimitation.LegacyCostEstimate))
            parts.Add(CareEstimated);

        return string.Join(" ", parts);
    }

    static string ReplenishmentText(CommercialGoalActionItem action)
    {
        if (action.ActionType == CommercialGoalActionType.ProtectAvailability
            || action.PurchaseGuidanceAction == InventoryPurchaseGuidanceAction.ConsiderReplenishment)
        {
            return CareConsiderReplenish;
        }

        return "";
    }

    static CommercialGoalPresentationTone ActNowTone(CommercialGoalActionType type) =>
        type switch
        {
            CommercialGoalActionType.ReviewData => CommercialGoalPresentationTone.Unavailable,
            CommercialGoalActionType.RemoveExpired
                or CommercialGoalActionType.PrioritizeExpiryRisk =>
                CommercialGoalPresentationTone.Warning,
            CommercialGoalActionType.PrioritizeExcess
                or CommercialGoalActionType.PrioritizeIdle
                or CommercialGoalActionType.ProtectAvailability =>
                CommercialGoalPresentationTone.Attention,
            _ => CommercialGoalPresentationTone.Neutral,
        };

    static string CoverageText(InventoryCoverageBand band) =>
        band switch
        {
            InventoryCoverageBand.Normal => "Cobertura normal",
            InventoryCoverageBand.Critical => "Cobertura crítica",
            InventoryCoverageBand.Low => "Cobertura baixa",
            InventoryCoverageBand.Attention => "Atenção à cobertura",
            InventoryCoverageBand.Zero => "Sem estoque",
            InventoryCoverageBand.Negative => "Estoque negativo — conferir",
            _ => "Cobertura não calculável",
        };

    static string ProductTitle(string code, string name, int id)
    {
        if (code.Length > 0 && name.Length > 0)
            return code + " — " + name;
        if (name.Length > 0)
            return name;
        if (code.Length > 0)
            return code;
        return "#" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    static IReadOnlyList<CommercialGoalLimitationPresentation> PresentLimitations(
        CentralDecisionSnapshot snapshot)
    {
        var list = new List<CommercialGoalLimitationPresentation>(6);
        Add(list, snapshot.HasLimitation(CentralDecisionLimitation.IntelligenceUnavailable),
            "intelligence", LimitationIntelligenceTitle, LimitationIntelligenceBody, true);
        Add(list, snapshot.HasLimitation(CentralDecisionLimitation.ContributionUnavailable),
            "contribution", LimitationContributionTitle, LimitationContributionBody, false);
        Add(list, snapshot.HasLimitation(CentralDecisionLimitation.PromotionSourceUnavailable),
            "promotion", LimitationPromotionTitle, LimitationPromotionBody, false);
        Add(list, snapshot.HasLimitation(CentralDecisionLimitation.ComboSourceUnavailable),
            "combo", LimitationComboTitle, LimitationComboBody, false);

        var plan = snapshot.ActionPlan;
        if (plan is not null)
        {
            Add(list, plan.HasLimitation(CommercialGoalActionLimitation.FinancialUnavailable),
                "financial", CommercialGoalPresentation.HeadlineUnavailable,
                CommercialGoalActionPlanPresentation.SupportingUnavailable, true);
            Add(list, plan.HasLimitation(CommercialGoalActionLimitation.LegacyCostEstimate)
                    || snapshot.FinancialQuality == CommercialGoalCostQuality.EstimatedLegacy,
                "legacy", CommercialGoalPresentation.EstimatedBadge,
                CommercialGoalPresentation.EstimatedExplanation, false);
            Add(list, plan.HasLimitation(CommercialGoalActionLimitation.LocationLimitation),
                "location", CareLocation, CareLocation, false);
            Add(list, plan.HasLimitation(CommercialGoalActionLimitation.InsufficientHistory),
                "history", CareInsufficient, CareInsufficient, false);
            Add(list, plan.HasLimitation(CommercialGoalActionLimitation.NoPhysicalEvidence),
                "evidence", CareNoEvidence, CareNoEvidence, false);
            Add(list, plan.HasLimitation(CommercialGoalActionLimitation.StructuralDataIssue),
                "structural", CareStructural, CareStructural, false);
        }

        return list;
    }

    static void Add(
        List<CommercialGoalLimitationPresentation> list,
        bool include,
        string key,
        string title,
        string body,
        bool prominent)
    {
        if (!include)
            return;
        list.Add(new CommercialGoalLimitationPresentation
        {
            Key = key,
            Title = title,
            Body = body,
            IsProminent = prominent,
        });
    }

    static CommercialGoalMetricPresentation Metric(
        string key,
        string title,
        string value,
        bool available,
        string supporting = "",
        CommercialGoalPresentationTone tone = CommercialGoalPresentationTone.Neutral) =>
        new()
        {
            Key = key,
            Title = title,
            ValueText = value,
            SupportingText = supporting,
            IsAvailable = available,
            Tone = tone,
        };
}
