using SGDB.Utils;

namespace SGDB.Models;

/// <summary>
/// Opção simulada 70F-B4D. Sem botão, Apply ou comando.
/// </summary>
public sealed class InventoryCommercialScenarioOptionPresentation
{
    public InventoryCommercialScenarioKind Kind { get; init; }
    public string KindLabel { get; init; } = "";
    public string SimulatedPriceText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ReductionAmountText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ReductionPercentText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ReductionSummaryText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string GrossMarginText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string Explanation { get; init; } = "";
}

/// <summary>
/// Presentation 70F-B4D de um produto. Sem recálculo comercial. Sem XAML.
/// </summary>
public sealed class InventoryCommercialScenarioPresentationRow
{
    public int ProductId { get; init; }
    public InventoryCommercialScenarioStatus Status { get; init; }
    public string StatusLabel { get; init; } = "";
    public InventoryCommercialScenarioThesis Thesis { get; init; }
    public string ThesisLabel { get; init; } = "";
    public InventoryCommercialScenarioReason PrimaryReason { get; init; }
    public string PrimaryReasonLabel { get; init; } = "";
    public string Explanation { get; init; } = "";
    public string ActionGuidance { get; init; } = "";
    public string SimulationDisclaimer { get; init; } = "";
    public string OperatorFooter { get; init; } = "";

    public string CurrentCatalogPriceLabel { get; init; } = "";
    public string CurrentCatalogPriceText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string CurrentGrossMarginLabel { get; init; } = "";
    public string CurrentGrossMarginText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string MinimumGrossMarginLabel { get; init; } = "";
    public string MinimumGrossMarginText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string FloorPriceLabel { get; init; } = "";
    public string FloorPriceText { get; init; } = InventoryProjectionPresentation.EmDash;
    public string FloorExplanation { get; init; } = "";
    public string FinancialRoomLabel { get; init; } = "";
    public string FinancialRoomText { get; init; } = InventoryProjectionPresentation.EmDash;

    public string AttentionQuantityLabel { get; init; } = "";
    public string AttentionQuantityText { get; init; } = InventoryProjectionPresentation.EmDash;

    public string ConfidenceDisplay { get; init; } = "";
    public IReadOnlyList<string> SecondaryReasonLabels { get; init; } = [];
    public IReadOnlyList<InventoryCommercialScenarioReason> SecondaryReasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<InventoryCommercialScenarioOptionPresentation> Scenarios { get; init; } = [];

    public bool IsScenarioAvailable { get; init; }
    public bool ShowFinancialAnalysis { get; init; }
    public bool ShowScenarioOptions { get; init; }
    public bool IsJoinMissing { get; init; }
}

/// <summary>Presentation em lote. Ordem = snapshot B4C (Intelligence.Rows).</summary>
public sealed class InventoryCommercialScenarioPresentationSnapshot
{
    public int QueryCount { get; init; }
    public IReadOnlyList<InventoryCommercialScenarioPresentationRow> Rows { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryCommercialScenarioPresentationRow> ByProductId { get; init; } =
        new Dictionary<int, InventoryCommercialScenarioPresentationRow>();
}

/// <summary>
/// Rótulos PT-BR 70F-B4D. Sem I/O, WPF, SQL ou recálculo B4B.
/// Reusa formatação 70C/70D (moeda, quantidade, —) e vocabulário do Estoque Inteligente.
/// </summary>
public static class InventoryCommercialScenarioPresentation
{
    public const int ExpectedQueryCount = 0;

    public const string EmDash = InventoryProjectionPresentation.EmDash;

    public const string StatusAvailable = "Cenários disponíveis";
    public const string StatusMonitorOnly = "Acompanhar";
    public const string StatusReviewData = "Revisar dados";
    public const string StatusNoRecommendation = "Sem cenário comercial";
    public const string StatusPolicyMissing = "Margem mínima não configurada";
    public const string StatusPolicyInvalid = "Margem mínima inválida";
    public const string StatusFinancialUnavailable = "Análise financeira indisponível";
    public const string StatusExpired = "Produto vencido";

    public const string ThesisExpirySurplus = "Sobra projetada até a validade";
    public const string ThesisExcess30 = "Excesso projetado em 30 dias";
    public const string ThesisIdle = "Produto parado";
    public const string ThesisHighCoverage = "Cobertura elevada";

    public const string KindLight = "Cenário leve";
    public const string KindModerate = "Cenário moderado";

    public const string CatalogPriceCaption = "Preço atual (catálogo)";
    public const string CurrentMarginCaption = "Margem atual";
    public const string MinimumMarginCaption = "Margem mínima";
    public const string FloorCaption = "Piso financeiro";
    public const string FloorExplanationText =
        "Menor preço de catálogo calculado para respeitar a margem mínima configurada.";
    public const string FloorLimitHint = "Limite calculado para respeitar a margem mínima.";
    public const string SimulatedPriceCaption = "Preço simulado";
    public const string MissingAnalysis = "Análise comercial indisponível.";
    public const string RoomCaption = "Espaço até o piso";
    public const string ReductionCaption = "Redução na simulação";
    public const string ScenarioMarginCaption = "Margem bruta no cenário";
    public const string AttentionExcessCaption = "Quantidade em atenção — projeção 30 dias";
    public const string AttentionExpiryCaption = "Quantidade em atenção até a validade";

    public const string SimulationDisclaimerText =
        "Os valores abaixo são simulações dentro da margem mínima configurada. O SGDB não altera preços automaticamente.";
    public const string OperatorFooterText =
        "Analise o cenário antes de alterar qualquer preço.";
    public const string ScenarioOptionExplanation =
        "Simulação de preço de catálogo dentro da margem mínima. Não é promoção nem preço de PDV.";

    public const string ExpiredExplanation = "Produto vencido — retirar/conferir.";
    public const string ExpiresTodayExplanation = "Vence hoje — priorizar saída.";
    public const string IdleExplanation = "Produto parado — avaliar exposição e giro.";
    public const string IdleGuidance = "Não há cenário de redução só porque o produto está parado.";
    public const string LimitedExplanation =
        "Análise com limitações — não foi calculado cenário de redução.";
    public const string PolicyMissingExplanation = "Margem mínima não configurada.";
    public const string PolicyMissingGuidance =
        "Configure a margem mínima em Sistema → Política comercial para habilitar simulações financeiras quando aplicável.";
    public const string PolicyInvalidExplanation = "Margem mínima inválida.";
    public const string PolicyInvalidGuidance =
        "A configuração da margem mínima precisa ser revisada em Sistema → Política comercial.";
    public const string NoRecommendationExplanation =
        "Não há cenário comercial calculado para este produto.";

    public static InventoryCommercialScenarioPresentationRow MissingRow(int productId = 0) =>
        new()
        {
            ProductId = productId,
            StatusLabel = MissingAnalysis,
            ThesisLabel = EmDash,
            PrimaryReasonLabel = MissingAnalysis,
            Explanation = MissingAnalysis,
            ActionGuidance = MissingAnalysis,
            CurrentCatalogPriceLabel = CatalogPriceCaption,
            CurrentCatalogPriceText = EmDash,
            CurrentGrossMarginLabel = CurrentMarginCaption,
            CurrentGrossMarginText = EmDash,
            MinimumGrossMarginLabel = MinimumMarginCaption,
            MinimumGrossMarginText = EmDash,
            FloorPriceLabel = FloorCaption,
            FloorPriceText = EmDash,
            FloorExplanation = FloorLimitHint,
            FinancialRoomLabel = RoomCaption,
            FinancialRoomText = EmDash,
            AttentionQuantityLabel = EmDash,
            AttentionQuantityText = EmDash,
            ConfidenceDisplay = InventoryAttentionPresentation.ConfidenceUnavailable,
            SecondaryReasonLabels = [],
            SecondaryReasons = [],
            Warnings = [],
            Scenarios = [],
            IsJoinMissing = true,
        };

    public static InventoryCommercialScenarioPresentationRow ResolveForDetail(
        InventoryCommercialScenarioPresentationSnapshot? snapshot,
        int productId)
    {
        if (snapshot?.ByProductId is { Count: > 0 } map
            && map.TryGetValue(productId, out var row)
            && row is not null)
            return row;

        return MissingRow(productId);
    }

    public static InventoryCommercialScenarioPresentationRow FromRow(
        InventoryCommercialScenarioRow? row)
    {
        row ??= new InventoryCommercialScenarioRow();
        var presented = FromResult(row.ScenarioResult);
        return CloneWithProductId(presented, row.ProductId);
    }

    public static InventoryCommercialScenarioPresentationSnapshot Apply(
        InventoryCommercialScenarioSnapshot? snapshot)
    {
        snapshot ??= new InventoryCommercialScenarioSnapshot();
        var rows = snapshot.Rows ?? [];
        var presented = new List<InventoryCommercialScenarioPresentationRow>(rows.Count);
        var map = new Dictionary<int, InventoryCommercialScenarioPresentationRow>(rows.Count);
        foreach (var row in rows)
        {
            var item = FromRow(row);
            presented.Add(item);
            map.TryAdd(item.ProductId, item);
        }

        return new InventoryCommercialScenarioPresentationSnapshot
        {
            QueryCount = snapshot.QueryCount,
            Rows = presented,
            ByProductId = map,
        };
    }

    public static InventoryCommercialScenarioPresentationRow FromResult(
        InventoryCommercialScenarioResult? result)
    {
        result ??= new InventoryCommercialScenarioResult();
        var secondary = result.SecondaryReasons ?? [];
        var secondaryLabels = new List<string>(secondary.Count);
        foreach (var reason in secondary)
        {
            if (reason == result.PrimaryReason)
                continue;
            secondaryLabels.Add(ReasonLabel(reason));
        }

        var options = BuildOptions(result);
        var available = result.Status == InventoryCommercialScenarioStatus.Available
            && options.Count > 0;
        var showFinancial = result.Status is not InventoryCommercialScenarioStatus.Expired
            and not InventoryCommercialScenarioStatus.ReviewData;

        return new InventoryCommercialScenarioPresentationRow
        {
            ProductId = result.ProductId,
            Status = result.Status,
            StatusLabel = StatusLabel(result.Status),
            Thesis = result.Thesis,
            ThesisLabel = ThesisLabel(result.Thesis),
            PrimaryReason = result.PrimaryReason,
            PrimaryReasonLabel = ReasonLabel(result.PrimaryReason),
            Explanation = ExplanationOf(result),
            ActionGuidance = GuidanceOf(result),
            SimulationDisclaimer = SimulationDisclaimerText,
            OperatorFooter = OperatorFooterText,
            CurrentCatalogPriceLabel = CatalogPriceCaption,
            CurrentCatalogPriceText = FormatMoney(result.CurrentCatalogPrice),
            CurrentGrossMarginLabel = CurrentMarginCaption,
            CurrentGrossMarginText = FormatPercent(result.CurrentGrossMarginPercent),
            MinimumGrossMarginLabel = MinimumMarginCaption,
            MinimumGrossMarginText = FormatPercent(result.MinimumGrossMarginPercent),
            FloorPriceLabel = FloorCaption,
            FloorPriceText = FormatMoney(result.MinimumAllowedCatalogPrice),
            FloorExplanation = FloorExplanationText,
            FinancialRoomLabel = RoomCaption,
            FinancialRoomText = FormatMoney(result.FinancialRoomAmount),
            AttentionQuantityLabel = AttentionLabel(result.AttentionQuantitySource),
            AttentionQuantityText = FormatQuantity(result.AttentionQuantity, result.AttentionQuantitySource),
            ConfidenceDisplay = InventoryAttentionPresentation.ConfidenceLabel(result.Confidence),
            SecondaryReasons = secondary,
            SecondaryReasonLabels = secondaryLabels,
            Warnings = BuildWarnings(result.PrimaryReason, secondary),
            Scenarios = options,
            IsScenarioAvailable = available,
            ShowFinancialAnalysis = showFinancial,
            ShowScenarioOptions = available,
        };
    }

    public static string StatusLabel(InventoryCommercialScenarioStatus status) =>
        status switch
        {
            InventoryCommercialScenarioStatus.Available => StatusAvailable,
            InventoryCommercialScenarioStatus.MonitorOnly => StatusMonitorOnly,
            InventoryCommercialScenarioStatus.ReviewData => StatusReviewData,
            InventoryCommercialScenarioStatus.NoRecommendation => StatusNoRecommendation,
            InventoryCommercialScenarioStatus.PolicyMissing => StatusPolicyMissing,
            InventoryCommercialScenarioStatus.PolicyInvalid => StatusPolicyInvalid,
            InventoryCommercialScenarioStatus.FinancialDataUnavailable => StatusFinancialUnavailable,
            InventoryCommercialScenarioStatus.Expired => StatusExpired,
            _ => "Situação não classificada",
        };

    public static string ThesisLabel(InventoryCommercialScenarioThesis thesis) =>
        thesis switch
        {
            InventoryCommercialScenarioThesis.ExpirySurplus => ThesisExpirySurplus,
            InventoryCommercialScenarioThesis.ProjectedExcess30 => ThesisExcess30,
            InventoryCommercialScenarioThesis.Idle => ThesisIdle,
            InventoryCommercialScenarioThesis.HighCoverage => ThesisHighCoverage,
            InventoryCommercialScenarioThesis.None => EmDash,
            _ => "Tese não classificada",
        };

    public static string KindLabel(InventoryCommercialScenarioKind kind) =>
        kind switch
        {
            InventoryCommercialScenarioKind.Light => KindLight,
            InventoryCommercialScenarioKind.Moderate => KindModerate,
            _ => "Cenário",
        };

    public static string ReasonLabel(InventoryCommercialScenarioReason reason) =>
        reason switch
        {
            InventoryCommercialScenarioReason.None => EmDash,
            InventoryCommercialScenarioReason.Expired => "Produto vencido",
            InventoryCommercialScenarioReason.InvalidInput => "Dados inválidos",
            InventoryCommercialScenarioReason.NegativeStock => "Estoque total negativo",
            InventoryCommercialScenarioReason.NegativeLocationStock => "Depósito ou geladeira negativo",
            InventoryCommercialScenarioReason.NegativeWarehouseStock => "Depósito negativo",
            InventoryCommercialScenarioReason.InconsistentStockTotals => "Total inconsistente",
            InventoryCommercialScenarioReason.TrackedQuantityExceedsWarehouse => "Lotes excedem o depósito",
            InventoryCommercialScenarioReason.DuplicateLotId => "Lotes duplicados",
            InventoryCommercialScenarioReason.InvalidLotQuantity => "Quantidade de lote inválida",
            InventoryCommercialScenarioReason.InvalidExpiryDate => "Validade inválida",
            InventoryCommercialScenarioReason.ProjectionMissing => "Projeção indisponível",
            InventoryCommercialScenarioReason.DuplicateProjection => "Projeção inconsistente",
            InventoryCommercialScenarioReason.LocationLimitation => "Limitação de localização",
            InventoryCommercialScenarioReason.InsufficientHistory => "Histórico insuficiente",
            InventoryCommercialScenarioReason.NoPhysicalEvidence => "Sem histórico de estoque",
            InventoryCommercialScenarioReason.Undated => "Sem validade informada",
            InventoryCommercialScenarioReason.NoLot => "Sem lote identificado",
            InventoryCommercialScenarioReason.ReviewData => "Revisar dados",
            InventoryCommercialScenarioReason.ExpiresToday => "Vence hoje",
            InventoryCommercialScenarioReason.NearExpiryWithoutSurplus => "Validade próxima",
            InventoryCommercialScenarioReason.DatedWithoutSurplusInWindow => "Validade a acompanhar",
            InventoryCommercialScenarioReason.HighCoverageMonitoring => "Cobertura elevada",
            InventoryCommercialScenarioReason.LimitedConfidence => "Análise com limitações",
            InventoryCommercialScenarioReason.UnavailableConfidence => "Análise indisponível",
            InventoryCommercialScenarioReason.Idle => "Produto parado",
            InventoryCommercialScenarioReason.PolicyMissing => "Margem mínima não configurada",
            InventoryCommercialScenarioReason.PolicyInvalid => "Margem mínima inválida",
            InventoryCommercialScenarioReason.MissingProduct => "Produto não encontrado",
            InventoryCommercialScenarioReason.UnknownCost => "Custo atual desconhecido",
            InventoryCommercialScenarioReason.InvalidCost => "Custo atual inválido",
            InventoryCommercialScenarioReason.UnusablePrice => "Preço de catálogo inutilizável",
            InventoryCommercialScenarioReason.InvalidPrice => "Preço de catálogo inválido",
            InventoryCommercialScenarioReason.NotSellable => "Produto não vendável",
            InventoryCommercialScenarioReason.CompositionProduct => "Produto composto",
            InventoryCommercialScenarioReason.AmbiguousSaleUnit => "Unidade comercial ambígua",
            InventoryCommercialScenarioReason.FloorUnavailable => "Piso financeiro indisponível",
            InventoryCommercialScenarioReason.PriceBelowFloor => "Preço abaixo do piso",
            InventoryCommercialScenarioReason.PriceAtFloor => "Preço no piso",
            InventoryCommercialScenarioReason.NoFinancialRoom => "Sem espaço financeiro",
            InventoryCommercialScenarioReason.ScenarioCollapsedByRounding => "Espaço insuficiente em centavos",
            InventoryCommercialScenarioReason.ExpirySurplus => ThesisExpirySurplus,
            InventoryCommercialScenarioReason.ProjectedExcess30 => ThesisExcess30,
            InventoryCommercialScenarioReason.NoRecommendation => StatusNoRecommendation,
            _ => "Motivo não classificado",
        };

    public static string ReasonExplanation(InventoryCommercialScenarioReason reason) =>
        reason switch
        {
            InventoryCommercialScenarioReason.None =>
                "Não há motivo comercial adicional para este produto.",
            InventoryCommercialScenarioReason.Expired => ExpiredExplanation,
            InventoryCommercialScenarioReason.InvalidInput =>
                "Há dados inconsistentes que impedem uma simulação confiável. Confira o cadastro.",
            InventoryCommercialScenarioReason.NegativeStock =>
                "O estoque total está negativo. Confira o cadastro antes de qualquer simulação.",
            InventoryCommercialScenarioReason.NegativeLocationStock =>
                "Há estoque negativo no depósito ou na geladeira. Confira as quantidades.",
            InventoryCommercialScenarioReason.NegativeWarehouseStock =>
                "O estoque do depósito está negativo. Confira as quantidades.",
            InventoryCommercialScenarioReason.InconsistentStockTotals =>
                "O estoque total é diferente da soma de depósito e geladeira. Confira o cadastro.",
            InventoryCommercialScenarioReason.TrackedQuantityExceedsWarehouse =>
                "A soma dos lotes é maior que o estoque do depósito. Confira os lotes.",
            InventoryCommercialScenarioReason.DuplicateLotId =>
                "Há lotes com identificador repetido. Confira o cadastro de lotes.",
            InventoryCommercialScenarioReason.InvalidLotQuantity =>
                "Há lote com quantidade negativa ou inválida. Confira os lotes.",
            InventoryCommercialScenarioReason.InvalidExpiryDate =>
                "Há validade cadastrada fora do formato esperado. Isso não é ausência de validade.",
            InventoryCommercialScenarioReason.ProjectionMissing =>
                "A projeção deste produto não pôde ser composta. Atualize a análise e confira os dados.",
            InventoryCommercialScenarioReason.DuplicateProjection =>
                "Há mais de uma projeção para o mesmo produto. A análise foi suspensa para não escolher um valor.",
            InventoryCommercialScenarioReason.LocationLimitation =>
                "Há limitação de localização (por exemplo geladeira) que impede uma conclusão comercial segura.",
            InventoryCommercialScenarioReason.InsufficientHistory =>
                "Ainda não há histórico suficiente para uma simulação comercial confiável.",
            InventoryCommercialScenarioReason.NoPhysicalEvidence =>
                "Não há evidência de entrada ou venda observável. Cadastro isolado não permite simulação.",
            InventoryCommercialScenarioReason.Undated =>
                "Há lote sem data de validade. Confira o cadastro. Isso não significa que o produto não vence.",
            InventoryCommercialScenarioReason.NoLot =>
                "Há estoque do depósito sem lote identificado.",
            InventoryCommercialScenarioReason.ReviewData =>
                "Os dados deste produto precisam ser conferidos antes de qualquer simulação comercial.",
            InventoryCommercialScenarioReason.ExpiresToday => ExpiresTodayExplanation,
            InventoryCommercialScenarioReason.NearExpiryWithoutSurplus =>
                "Há validade em até 7 dias, sem sobra projetada. Priorize a saída. Não há cenário de redução.",
            InventoryCommercialScenarioReason.DatedWithoutSurplusInWindow =>
                "Há validade entre 8 e 30 dias, sem sobra projetada. Acompanhe a saída. Não há cenário de redução.",
            InventoryCommercialScenarioReason.HighCoverageMonitoring =>
                "A cobertura está elevada e merece acompanhamento. Não há tese numérica de redução.",
            InventoryCommercialScenarioReason.LimitedConfidence => LimitedExplanation,
            InventoryCommercialScenarioReason.UnavailableConfidence =>
                "A análise não está disponível. Não foi calculado cenário de redução.",
            InventoryCommercialScenarioReason.Idle => IdleExplanation,
            InventoryCommercialScenarioReason.PolicyMissing => PolicyMissingExplanation,
            InventoryCommercialScenarioReason.PolicyInvalid => PolicyInvalidExplanation,
            InventoryCommercialScenarioReason.MissingProduct =>
                "O produto não foi encontrado no catálogo. Sem preço ou custo para simular.",
            InventoryCommercialScenarioReason.UnknownCost =>
                "O custo médio atual é insuficiente para simulação financeira. Zero não é custo conhecido.",
            InventoryCommercialScenarioReason.InvalidCost =>
                "O custo médio atual é inválido e não permite simulação financeira.",
            InventoryCommercialScenarioReason.UnusablePrice =>
                "O preço de catálogo não permite análise financeira.",
            InventoryCommercialScenarioReason.InvalidPrice =>
                "O preço de catálogo é inválido e não permite análise financeira.",
            InventoryCommercialScenarioReason.NotSellable =>
                "O produto está marcado como não vendável. Não há cenário comercial.",
            InventoryCommercialScenarioReason.CompositionProduct =>
                "Produto composto. O B4 não calcula cenário de kit nem de composição.",
            InventoryCommercialScenarioReason.AmbiguousSaleUnit =>
                "A unidade comercial é ambígua. O SGDB não divide custo nem infere unidades por maço.",
            InventoryCommercialScenarioReason.FloorUnavailable =>
                "O piso financeiro não pôde ser calculado. Sem simulação de redução.",
            InventoryCommercialScenarioReason.PriceBelowFloor =>
                "O preço atual de catálogo está abaixo do piso financeiro configurado.",
            InventoryCommercialScenarioReason.PriceAtFloor =>
                "O preço atual de catálogo já está no piso financeiro. Não há espaço para cenário.",
            InventoryCommercialScenarioReason.NoFinancialRoom =>
                "Não existe espaço financeiro para um cenário de redução.",
            InventoryCommercialScenarioReason.ScenarioCollapsedByRounding =>
                "O espaço financeiro é pequeno demais para produzir um cenário distinto em centavos.",
            InventoryCommercialScenarioReason.ExpirySurplus =>
                "Há quantidade projetada remanescente até a validade.",
            InventoryCommercialScenarioReason.ProjectedExcess30 =>
                "Há estoque projetado acima da demanda do horizonte de 30 dias.",
            InventoryCommercialScenarioReason.NoRecommendation => NoRecommendationExplanation,
            _ => "Há uma situação comercial que não pôde ser descrita.",
        };

    static IReadOnlyList<InventoryCommercialScenarioOptionPresentation> BuildOptions(
        InventoryCommercialScenarioResult result)
    {
        if (result.Status != InventoryCommercialScenarioStatus.Available)
            return [];

        var source = result.Scenarios ?? [];
        if (source.Count == 0)
            return [];

        var options = new List<InventoryCommercialScenarioOptionPresentation>(source.Count);
        foreach (var scenario in source)
        {
            var amount = FormatMoney(scenario.ReductionAmount);
            var percent = FormatPercent(scenario.ReductionPercent);
            options.Add(new InventoryCommercialScenarioOptionPresentation
            {
                Kind = scenario.Kind,
                KindLabel = KindLabel(scenario.Kind),
                SimulatedPriceText = FormatMoney(scenario.SimulatedCatalogPrice),
                ReductionAmountText = amount,
                ReductionPercentText = percent,
                ReductionSummaryText = $"{amount} ({percent})",
                GrossMarginText = FormatPercent(scenario.GrossMarginPercent),
                Explanation = ScenarioOptionExplanation,
            });
        }

        return options;
    }

    static IReadOnlyList<string> BuildWarnings(
        InventoryCommercialScenarioReason primary,
        IReadOnlyList<InventoryCommercialScenarioReason> secondary)
    {
        var warnings = new List<string>();
        AddWarning(warnings, primary);
        foreach (var reason in secondary)
        {
            if (reason == primary)
                continue;
            AddWarning(warnings, reason);
        }

        return warnings;
    }

    static void AddWarning(List<string> warnings, InventoryCommercialScenarioReason reason)
    {
        if (!IsWarning(reason))
            return;
        var text = ReasonExplanation(reason);
        if (warnings.Contains(text))
            return;
        warnings.Add(text);
    }

    static bool IsWarning(InventoryCommercialScenarioReason reason) =>
        reason is InventoryCommercialScenarioReason.LimitedConfidence
            or InventoryCommercialScenarioReason.UnavailableConfidence
            or InventoryCommercialScenarioReason.LocationLimitation
            or InventoryCommercialScenarioReason.UnknownCost
            or InventoryCommercialScenarioReason.InvalidCost
            or InventoryCommercialScenarioReason.UnusablePrice
            or InventoryCommercialScenarioReason.InvalidPrice
            or InventoryCommercialScenarioReason.PolicyMissing
            or InventoryCommercialScenarioReason.PolicyInvalid
            or InventoryCommercialScenarioReason.PriceBelowFloor
            or InventoryCommercialScenarioReason.CompositionProduct
            or InventoryCommercialScenarioReason.AmbiguousSaleUnit
            or InventoryCommercialScenarioReason.InsufficientHistory
            or InventoryCommercialScenarioReason.Undated
            or InventoryCommercialScenarioReason.NoLot
            or InventoryCommercialScenarioReason.FloorUnavailable
            or InventoryCommercialScenarioReason.ScenarioCollapsedByRounding
            or InventoryCommercialScenarioReason.MissingProduct
            or InventoryCommercialScenarioReason.NotSellable
            or InventoryCommercialScenarioReason.ProjectionMissing
            or InventoryCommercialScenarioReason.DuplicateProjection
            or InventoryCommercialScenarioReason.InvalidInput
            or InventoryCommercialScenarioReason.NegativeStock
            or InventoryCommercialScenarioReason.NegativeLocationStock
            or InventoryCommercialScenarioReason.NegativeWarehouseStock
            or InventoryCommercialScenarioReason.InconsistentStockTotals;

    static string ExplanationOf(InventoryCommercialScenarioResult result) =>
        result.Status switch
        {
            InventoryCommercialScenarioStatus.Expired => ExpiredExplanation,
            InventoryCommercialScenarioStatus.PolicyMissing => PolicyMissingExplanation,
            InventoryCommercialScenarioStatus.PolicyInvalid => PolicyInvalidExplanation,
            InventoryCommercialScenarioStatus.NoRecommendation => NoRecommendationExplanation,
            _ when result.PrimaryReason == InventoryCommercialScenarioReason.ExpiresToday =>
                ExpiresTodayExplanation,
            _ when result.PrimaryReason == InventoryCommercialScenarioReason.LimitedConfidence
                || result.Confidence == InventoryAttentionConfidence.Limited =>
                LimitedExplanation,
            _ when result.Thesis == InventoryCommercialScenarioThesis.Idle
                || result.PrimaryReason == InventoryCommercialScenarioReason.Idle =>
                IdleExplanation,
            _ => ReasonExplanation(result.PrimaryReason),
        };

    static string GuidanceOf(InventoryCommercialScenarioResult result)
    {
        if (result.Status == InventoryCommercialScenarioStatus.Available)
            return OperatorFooterText;
        if (result.Status == InventoryCommercialScenarioStatus.Expired)
            return "Retire ou confira o estoque. Não há simulação de preço.";
        if (result.PrimaryReason == InventoryCommercialScenarioReason.ExpiresToday)
            return "Priorize a saída. O SGDB não calcula redução automática.";
        if (result.Thesis == InventoryCommercialScenarioThesis.Idle
            || result.PrimaryReason == InventoryCommercialScenarioReason.Idle)
            return IdleGuidance;
        if (result.Status == InventoryCommercialScenarioStatus.PolicyMissing)
            return PolicyMissingGuidance;
        if (result.Status == InventoryCommercialScenarioStatus.PolicyInvalid)
            return PolicyInvalidGuidance;
        if (result.PrimaryReason == InventoryCommercialScenarioReason.LimitedConfidence
            || result.Confidence == InventoryAttentionConfidence.Limited)
            return LimitedExplanation;
        if (result.Status == InventoryCommercialScenarioStatus.ReviewData)
            return "Confira os dados antes de qualquer simulação comercial.";
        if (result.Status == InventoryCommercialScenarioStatus.FinancialDataUnavailable)
            return "Não há simulação financeira enquanto os dados necessários estiverem indisponíveis.";
        if (result.Status == InventoryCommercialScenarioStatus.NoRecommendation)
            return NoRecommendationExplanation;
        if (result.PrimaryReason is InventoryCommercialScenarioReason.PriceAtFloor
            or InventoryCommercialScenarioReason.NoFinancialRoom)
            return "O preço atual de catálogo não deixa espaço para um cenário de redução.";
        if (result.PrimaryReason == InventoryCommercialScenarioReason.PriceBelowFloor)
            return "O preço atual de catálogo está abaixo do piso financeiro.";
        if (result.PrimaryReason == InventoryCommercialScenarioReason.ScenarioCollapsedByRounding)
            return ReasonExplanation(InventoryCommercialScenarioReason.ScenarioCollapsedByRounding);
        if (result.Thesis == InventoryCommercialScenarioThesis.HighCoverage
            || result.PrimaryReason == InventoryCommercialScenarioReason.HighCoverageMonitoring)
            return "A cobertura merece acompanhamento. Não há cenário de redução.";
        return OperatorFooterText;
    }

    static string AttentionLabel(InventoryCommercialAttentionQuantitySource source) =>
        source switch
        {
            InventoryCommercialAttentionQuantitySource.ProjectedExcess30 => AttentionExcessCaption,
            InventoryCommercialAttentionQuantitySource.ExpirySurplus => AttentionExpiryCaption,
            _ => EmDash,
        };

    static string FormatQuantity(
        double? quantity,
        InventoryCommercialAttentionQuantitySource source)
    {
        if (source == InventoryCommercialAttentionQuantitySource.None)
            return EmDash;
        return InventoryProjectionPresentation.FormatCalculatedQty(quantity);
    }

    static string FormatMoney(double? value) =>
        InventoryProjectionPresentation.FormatMoney(value);

    static string FormatPercent(double? value)
    {
        if (value is not double raw || !double.IsFinite(raw))
            return EmDash;

        var rounded = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.0000001)
            return Math.Round(rounded).ToString("0", ProductPriceHelper.Br) + "%";
        return rounded.ToString("0.##", ProductPriceHelper.Br) + "%";
    }

    static InventoryCommercialScenarioPresentationRow CloneWithProductId(
        InventoryCommercialScenarioPresentationRow source,
        int productId) =>
        new()
        {
            ProductId = productId,
            Status = source.Status,
            StatusLabel = source.StatusLabel,
            Thesis = source.Thesis,
            ThesisLabel = source.ThesisLabel,
            PrimaryReason = source.PrimaryReason,
            PrimaryReasonLabel = source.PrimaryReasonLabel,
            Explanation = source.Explanation,
            ActionGuidance = source.ActionGuidance,
            SimulationDisclaimer = source.SimulationDisclaimer,
            OperatorFooter = source.OperatorFooter,
            CurrentCatalogPriceLabel = source.CurrentCatalogPriceLabel,
            CurrentCatalogPriceText = source.CurrentCatalogPriceText,
            CurrentGrossMarginLabel = source.CurrentGrossMarginLabel,
            CurrentGrossMarginText = source.CurrentGrossMarginText,
            MinimumGrossMarginLabel = source.MinimumGrossMarginLabel,
            MinimumGrossMarginText = source.MinimumGrossMarginText,
            FloorPriceLabel = source.FloorPriceLabel,
            FloorPriceText = source.FloorPriceText,
            FloorExplanation = source.FloorExplanation,
            FinancialRoomLabel = source.FinancialRoomLabel,
            FinancialRoomText = source.FinancialRoomText,
            AttentionQuantityLabel = source.AttentionQuantityLabel,
            AttentionQuantityText = source.AttentionQuantityText,
            ConfidenceDisplay = source.ConfidenceDisplay,
            SecondaryReasonLabels = source.SecondaryReasonLabels,
            SecondaryReasons = source.SecondaryReasons,
            Warnings = source.Warnings,
            Scenarios = source.Scenarios,
            IsScenarioAvailable = source.IsScenarioAvailable,
            ShowFinancialAnalysis = source.ShowFinancialAnalysis,
            ShowScenarioOptions = source.ShowScenarioOptions,
            IsJoinMissing = source.IsJoinMissing,
        };
}
