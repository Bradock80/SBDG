using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Models;

/// <summary>
/// Cenário financeiro já formatado. Sem recálculo. Ordem = B3/B4.
/// </summary>
public sealed class InventoryComboScenarioPresentation
{
    public InventoryComboPairFinancialScenarioKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string PairPriceText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string GrossProfitText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string GrossMarginText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ReductionText { get; init; } = InventoryProjectionPresentation.EmDash;
}

/// <summary>
/// Uma sugestão B4 em PT-BR. Sem reordenar, sem decidir ranking.
/// </summary>
public sealed class InventoryComboSuggestionPresentationRow
{
    public int TargetProductId { get; init; }
    public int AnchorProductId { get; init; }
    public InventoryComboPairEvidence PairEvidence { get; init; }

    public string TargetTitle { get; init; } = "";
    public string TargetReasonText { get; init; } = "";
    public string AnchorTitle { get; init; } = "";
    public string AnchorReasonText { get; init; } = "";

    public string EvidenceText { get; init; } = "";
    public string EvidenceDetailText { get; init; } = "";
    public string EvidenceTone { get; init; } = "";

    public string CurrentPriceLabel { get; init; } = "";
    public string CurrentPriceText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string FloorPriceLabel { get; init; } = "";
    public string FloorPriceText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string FloorExplanation { get; init; } = "";

    public string ReferencePriceLabel { get; init; } = "";
    public string ReferencePriceText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ReferenceSubtitle { get; init; } = "";
    public string ReductionLabel { get; init; } = "";
    public string ReductionText { get; init; } = InventoryProjectionPresentation.EmDash;

    public string GrossProfitLabel { get; init; } = "";
    public string GrossProfitText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string GrossMarginLabel { get; init; } = "";
    public string GrossMarginText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ReferenceGrossProfitText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ReferenceGrossMarginText { get; init; } = InventoryProjectionPresentation.EmDash;

    public string TargetStockLabel { get; init; } = "";
    public string TargetStockText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string AnchorStockLabel { get; init; } = "";
    public string AnchorStockText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string AnchorCoverageLabel { get; init; } = "";
    public string AnchorCoverageText { get; init; } = InventoryProjectionPresentation.EmDash;

    public string ConfidenceText { get; init; } = "";
    public IReadOnlyList<string> LimitationsText { get; init; } = [];
    public IReadOnlyList<InventoryComboScenarioPresentation> Scenarios { get; init; } = [];
    public bool HasReferenceScenario { get; init; }
}

/// <summary>
/// Alvo elegível B5 com sugestões já formatadas. Lista vazia não é erro.
/// </summary>
public sealed class InventoryComboTargetPresentationGroup
{
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string TargetTitle { get; init; } = "";
    public ComboTargetEligibilityReason Reason { get; init; }
    public string TargetReasonText { get; init; } = "";
    public string ConfidenceText { get; init; } = "";
    public string TargetStockText { get; init; } = InventoryProjectionPresentation.EmDash;
    public int SuggestionCount { get; init; }
    public string SuggestionCountText { get; init; } = "";
    public string EmptyMessage { get; init; } = "";
    public IReadOnlyList<InventoryComboSuggestionPresentationRow> Suggestions { get; init; } = [];
}

/// <summary>
/// Snapshot PT-BR 71A-B6. QueryCount herdado de B5. Ordem = B5/B4.
/// </summary>
public sealed class InventoryComboPresentationSnapshot
{
    public int QueryCount { get; init; }
    public string EmptySnapshotMessage { get; init; } = "";
    public string DisclaimerText { get; init; } = "";
    public IReadOnlyList<InventoryComboTargetPresentationGroup> Targets { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryComboTargetPresentationGroup> ByProductId { get; init; } =
        new Dictionary<int, InventoryComboTargetPresentationGroup>();
}

/// <summary>
/// Rótulos PT-BR 71A-B6. Sem I/O, WPF, SQL ou recálculo B1–B5.
/// Reusa formatação 70C/70D/70F e vocabulário de confiança 70E.
/// </summary>
public static class InventoryComboPresentation
{
    public const int ExpectedQueryCount = 0;

    public const int ExpectedPipelineQueryCount =
        InventoryComboIntelligenceComposer.ExpectedPipelineQueryCount;

    public const string EmDash = InventoryProjectionPresentation.EmDash;

    public const string ModuleTitle = "Combo Inteligente";

    public const string TargetReasonExpirySurplus =
        InventoryPurchaseGuidancePresentation.ReasonProjectedExpirySurplus;
    public const string TargetReasonProjectedExcess = "Excesso projetado de estoque";
    public const string TargetReasonIdle = InventoryCommercialScenarioPresentation.ThesisIdle;

    public const string AnchorReasonHealthy = "Produto com giro e estoque saudável";

    public const string EvidenceObserved = "Compra conjunta observada";
    public const string EvidenceWeak = "Poucas compras conjuntas observadas";
    public const string EvidenceInsufficient = "Sem evidência suficiente de compra conjunta";
    public const string EvidenceUnavailable = "Evidência não utilizável nesta sugestão";

    public const string EvidenceInsufficientDetail =
        "Histórico insuficiente para medir a associação com segurança.";

    public const string CurrentPriceLabel = "Preços atuais";
    public const string FloorPriceLabel = "Piso conjunto";
    public const string FloorExplanation =
        "Menor valor calculado respeitando a margem mínima configurada.";
    public const string ReferencePriceLabel = "Preço conjunto de referência";
    public const string ReferenceSubtitle =
        "Redução concentrada no produto que precisa girar; a âncora permanece no preço atual.";
    public const string ReductionLabel = "Redução sobre os preços atuais";
    public const string GrossProfitLabel = "Lucro bruto por combinação vendida neste preço";
    public const string GrossMarginLabel = "Margem bruta";
    public const string TargetStockLabel = "Estoque do produto-alvo";
    public const string AnchorStockLabel = "Estoque da âncora";
    public const string AnchorCoverageLabel = "Cobertura da âncora";

    public const string LimitationWeak =
        "Há poucas compras conjuntas observadas.";
    public const string LimitationInsufficientHistory =
        "O histórico ainda é insuficiente para confirmar a associação.";
    public const string LimitationTargetLimited =
        "A análise do produto-alvo tem limitações.";
    public const string LimitationAnchorLimited =
        "A análise da âncora tem limitações.";
    public const string LimitationOtherData =
        "A cobertura da âncora não está disponível nesta análise.";

    public const string DisclaimerText =
        "O Combo Inteligente é um apoio à decisão. "
        + "O SGDB não cria promoções, não altera preços e não movimenta estoque automaticamente. "
        + "As combinações são baseadas no estoque, giro, margem e histórico disponíveis. "
        + "Elas não representam previsão de venda.";

    public const string EmptyTargetMessage =
        "Nenhuma combinação segura encontrada para este produto.";
    public const string EmptySnapshotMessage =
        "Nenhum produto com necessidade de giro foi identificado pelos critérios atuais.";

    public static InventoryComboPresentationSnapshot Apply(
        InventoryComboIntelligenceSnapshot? snapshot)
    {
        snapshot ??= new InventoryComboIntelligenceSnapshot();
        var source = snapshot.Targets ?? [];
        var titles = snapshot.ProductTitles
            ?? new Dictionary<int, InventoryComboProductTitle>();
        var groups = new List<InventoryComboTargetPresentationGroup>(source.Count);
        var map = new Dictionary<int, InventoryComboTargetPresentationGroup>(source.Count);

        foreach (var target in source)
        {
            if (target is null)
                continue;
            var presented = PresentTarget(target, titles);
            groups.Add(presented);
            map.TryAdd(presented.ProductId, presented);
        }

        return new InventoryComboPresentationSnapshot
        {
            QueryCount = snapshot.QueryCount,
            EmptySnapshotMessage = groups.Count == 0 ? EmptySnapshotMessage : "",
            DisclaimerText = DisclaimerText,
            Targets = groups,
            ByProductId = map,
        };
    }

    public static InventoryComboTargetPresentationGroup PresentTarget(
        InventoryComboTargetSuggestionGroup? target,
        IReadOnlyDictionary<int, InventoryComboProductTitle>? titles = null)
    {
        target ??= new InventoryComboTargetSuggestionGroup();
        titles ??= new Dictionary<int, InventoryComboProductTitle>();
        var suggestions = target.Suggestions ?? [];
        var rows = new List<InventoryComboSuggestionPresentationRow>(suggestions.Count);
        foreach (var suggestion in suggestions)
        {
            if (suggestion is null)
                continue;
            rows.Add(PresentSuggestion(suggestion, target, titles));
        }

        var count = rows.Count;
        var reason = target.Eligibility?.Reason ?? ComboTargetEligibilityReason.None;
        var confidence = target.Eligibility?.Confidence ?? InventoryAttentionConfidence.Unavailable;
        ResolveIdentity(target.ProductId, target.Code, target.Name, titles, out var code, out var name);
        return new InventoryComboTargetPresentationGroup
        {
            ProductId = target.ProductId,
            Code = code,
            Name = name,
            TargetTitle = ProductTitle(target.ProductId, target.Code, target.Name, titles),
            Reason = reason,
            TargetReasonText = TargetReasonText(reason),
            ConfidenceText = InventoryAttentionPresentation.ConfidenceLabel(confidence),
            TargetStockText = count > 0 ? rows[0].TargetStockText : EmDash,
            SuggestionCount = count,
            SuggestionCountText = SuggestionCountText(count),
            EmptyMessage = count == 0 ? EmptyTargetMessage : "",
            Suggestions = rows,
        };
    }

    public static InventoryComboSuggestionPresentationRow PresentSuggestion(
        InventoryComboSuggestion? suggestion,
        InventoryComboTargetSuggestionGroup? target = null,
        IReadOnlyDictionary<int, InventoryComboProductTitle>? titles = null)
    {
        suggestion ??= new InventoryComboSuggestion();
        titles ??= new Dictionary<int, InventoryComboProductTitle>();
        var current = FindScenario(suggestion.Scenarios, InventoryComboPairFinancialScenarioKind.CurrentPrices);
        var reference = FindScenario(
            suggestion.Scenarios, InventoryComboPairFinancialScenarioKind.TargetReductionReference);
        var scenarios = PresentScenarios(suggestion.Scenarios);

        return new InventoryComboSuggestionPresentationRow
        {
            TargetProductId = suggestion.TargetProductId,
            AnchorProductId = suggestion.AnchorProductId,
            PairEvidence = suggestion.PairEvidence,
            TargetTitle = ProductTitle(
                suggestion.TargetProductId, target?.Code, target?.Name, titles),
            TargetReasonText = TargetReasonText(suggestion.TargetReason),
            AnchorTitle = ProductTitle(suggestion.AnchorProductId, null, null, titles),
            AnchorReasonText = AnchorReasonText(suggestion.AnchorReason),
            EvidenceText = EvidenceText(suggestion.PairEvidence),
            EvidenceDetailText = EvidenceDetailText(suggestion),
            EvidenceTone = EvidenceTone(suggestion.PairEvidence),
            CurrentPriceLabel = CurrentPriceLabel,
            CurrentPriceText = FormatMoney(current?.PairPrice ?? suggestion.NormalPairPrice),
            FloorPriceLabel = FloorPriceLabel,
            FloorPriceText = FormatMoney(suggestion.PairFloorPrice),
            FloorExplanation = FloorExplanation,
            ReferencePriceLabel = reference is null ? "" : ReferencePriceLabel,
            ReferencePriceText = FormatMoney(reference?.PairPrice),
            ReferenceSubtitle = reference is null ? "" : ReferenceSubtitle,
            ReductionLabel = ReductionLabel,
            ReductionText = FormatMoney(reference?.ReductionFromCurrent),
            GrossProfitLabel = GrossProfitLabel,
            GrossProfitText = FormatMoney(current?.GrossProfit),
            GrossMarginLabel = GrossMarginLabel,
            GrossMarginText = FormatPercentFromFraction(current?.GrossMargin),
            ReferenceGrossProfitText = FormatMoney(reference?.GrossProfit),
            ReferenceGrossMarginText = FormatPercentFromFraction(reference?.GrossMargin),
            TargetStockLabel = TargetStockLabel,
            TargetStockText = FormatStock(suggestion.TargetStock),
            AnchorStockLabel = AnchorStockLabel,
            AnchorStockText = FormatStock(suggestion.AnchorStock),
            AnchorCoverageLabel = AnchorCoverageLabel,
            AnchorCoverageText = FormatCoverage(suggestion.AnchorCoverageDays),
            ConfidenceText = InventoryAttentionPresentation.ConfidenceLabel(suggestion.Confidence),
            LimitationsText = PresentLimitations(suggestion.Limitations),
            Scenarios = scenarios,
            HasReferenceScenario = reference is not null,
        };
    }

    public static string TargetReasonText(ComboTargetEligibilityReason reason) =>
        reason switch
        {
            ComboTargetEligibilityReason.ExpirySurplus => TargetReasonExpirySurplus,
            ComboTargetEligibilityReason.ProjectedExcess => TargetReasonProjectedExcess,
            ComboTargetEligibilityReason.Idle => TargetReasonIdle,
            _ => EmDash,
        };

    public static string AnchorReasonText(ComboAnchorEligibilityReason reason) =>
        reason switch
        {
            ComboAnchorEligibilityReason.HealthyNormalCoverage => AnchorReasonHealthy,
            _ => EmDash,
        };

    public static string EvidenceText(InventoryComboPairEvidence evidence) =>
        evidence switch
        {
            InventoryComboPairEvidence.Observed => EvidenceObserved,
            InventoryComboPairEvidence.Weak => EvidenceWeak,
            InventoryComboPairEvidence.InsufficientHistory => EvidenceInsufficient,
            _ => EvidenceUnavailable,
        };

    public static string EvidenceTone(InventoryComboPairEvidence evidence) =>
        evidence switch
        {
            InventoryComboPairEvidence.Observed => "observed",
            InventoryComboPairEvidence.Weak => "weak",
            InventoryComboPairEvidence.InsufficientHistory => "insufficient",
            _ => "unavailable",
        };

    public static string LimitationText(InventoryComboSuggestionLimitation limitation) =>
        limitation switch
        {
            InventoryComboSuggestionLimitation.WeakPairEvidence => LimitationWeak,
            InventoryComboSuggestionLimitation.InsufficientPairHistory => LimitationInsufficientHistory,
            InventoryComboSuggestionLimitation.TargetLimitedConfidence => LimitationTargetLimited,
            InventoryComboSuggestionLimitation.AnchorLimitedConfidence => LimitationAnchorLimited,
            InventoryComboSuggestionLimitation.OtherDataLimitation => LimitationOtherData,
            _ => EmDash,
        };

    public static string SuggestionCountText(int count)
    {
        if (count <= 0)
            return "Nenhuma combinação";
        if (count == 1)
            return "1 combinação";
        return $"{count.ToString("0", ProductPriceHelper.Br)} combinações";
    }

    public static string JointSalesText(int pairTransactions)
    {
        var n = pairTransactions < 0 ? 0 : pairTransactions;
        var unit = n == 1 ? "venda conjunta" : "vendas conjuntas";
        var days = InventoryComboPairEvidenceEngine.WindowDays.ToString("0", ProductPriceHelper.Br);
        return $"{n.ToString("N0", ProductPriceHelper.Br)} {unit} nos últimos {days} dias";
    }

    public static string AssociationShareText(double? confidenceTargetToAnchor)
    {
        var percent = FormatPercentFromFraction(confidenceTargetToAnchor);
        if (percent == EmDash)
            return "";
        return $"{percent} das vendas do produto-alvo também incluíram esta âncora";
    }

    static string EvidenceDetailText(InventoryComboSuggestion suggestion)
    {
        if (suggestion.PairEvidence == InventoryComboPairEvidence.InsufficientHistory)
            return EvidenceInsufficientDetail;

        if (suggestion.PairEvidence is InventoryComboPairEvidence.Observed
            or InventoryComboPairEvidence.Weak)
        {
            var sales = JointSalesText(suggestion.PairTransactions);
            var share = AssociationShareText(suggestion.ConfidenceTargetToAnchor);
            return string.IsNullOrWhiteSpace(share) ? sales : $"{sales}. {share}";
        }

        return EvidenceUnavailable;
    }

    static IReadOnlyList<InventoryComboScenarioPresentation> PresentScenarios(
        IReadOnlyList<InventoryComboPairFinancialScenario>? scenarios)
    {
        if (scenarios is null || scenarios.Count == 0)
            return [];

        var rows = new List<InventoryComboScenarioPresentation>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            var isReference = scenario.Kind
                == InventoryComboPairFinancialScenarioKind.TargetReductionReference;
            rows.Add(new InventoryComboScenarioPresentation
            {
                Kind = scenario.Kind,
                Title = isReference ? ReferencePriceLabel : CurrentPriceLabel,
                Subtitle = isReference ? ReferenceSubtitle : "",
                PairPriceText = FormatMoney(scenario.PairPrice),
                GrossProfitText = FormatMoney(scenario.GrossProfit),
                GrossMarginText = FormatPercentFromFraction(scenario.GrossMargin),
                ReductionText = isReference
                    ? FormatMoney(scenario.ReductionFromCurrent)
                    : EmDash,
            });
        }

        return rows;
    }

    static InventoryComboPairFinancialScenario? FindScenario(
        IReadOnlyList<InventoryComboPairFinancialScenario>? scenarios,
        InventoryComboPairFinancialScenarioKind kind)
    {
        if (scenarios is null)
            return null;
        foreach (var scenario in scenarios)
        {
            if (scenario.Kind == kind)
                return scenario;
        }

        return null;
    }

    static IReadOnlyList<string> PresentLimitations(
        IReadOnlyList<InventoryComboSuggestionLimitation>? limitations)
    {
        if (limitations is null || limitations.Count == 0)
            return [];

        var texts = new List<string>(limitations.Count);
        foreach (var item in limitations)
        {
            var text = LimitationText(item);
            if (text == EmDash || texts.Contains(text))
                continue;
            texts.Add(text);
        }

        return texts;
    }

    static void ResolveIdentity(
        int productId,
        string? code,
        string? name,
        IReadOnlyDictionary<int, InventoryComboProductTitle> titles,
        out string resolvedCode,
        out string resolvedName)
    {
        if (titles.TryGetValue(productId, out var title) && title is not null)
        {
            if (string.IsNullOrWhiteSpace(code))
                code = title.Code;
            if (string.IsNullOrWhiteSpace(name))
                name = title.Name;
        }

        resolvedCode = (code ?? "").Trim();
        resolvedName = (name ?? "").Trim();
    }

    static string ProductTitle(
        int productId,
        string? code,
        string? name,
        IReadOnlyDictionary<int, InventoryComboProductTitle> titles)
    {
        ResolveIdentity(productId, code, name, titles, out var resolvedCode, out var resolvedName);
        if (resolvedCode.Length == 0 && resolvedName.Length == 0)
            return EmDash;
        if (resolvedCode.Length == 0)
            return resolvedName;
        if (resolvedName.Length == 0)
            return resolvedCode;
        return $"{resolvedCode} — {resolvedName}";
    }

    static string FormatMoney(double? value) =>
        InventoryProjectionPresentation.FormatMoney(value);

    static string FormatStock(double value) =>
        InventoryIntelligenceEngine.IsFinite(value)
            ? InventoryIntelligencePresentation.FormatQty(value)
            : EmDash;

    static string FormatCoverage(double? days)
    {
        if (days is not double value || !InventoryIntelligenceEngine.IsFinite(value))
            return EmDash;
        var number = InventoryIntelligencePresentation.FormatCoverageDays(value);
        return number == InventoryIntelligencePresentation.EmDash ? EmDash : $"{number} dias";
    }

    static string FormatPercentFromFraction(double? value)
    {
        if (value is not double raw || !InventoryIntelligenceEngine.IsFinite(raw))
            return EmDash;

        var percent = raw * 100;
        var rounded = Math.Round(percent, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.0000001)
            return Math.Round(rounded).ToString("0", ProductPriceHelper.Br) + "%";
        return rounded.ToString("0.##", ProductPriceHelper.Br) + "%";
    }
}
