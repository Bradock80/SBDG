namespace SGDB.Models;

/// <summary>
/// Linha 70E-B3 para o operador. Sem WPF. Sem recálculo 70C/70D.
/// Contrato B4: prioridade + motivo na grade; detalhe fora. Sem várias colunas novas.
/// </summary>
public sealed class InventoryAttentionPresentationRow
{
    public int ProductId { get; init; }

    public InventoryAttentionPriority Priority { get; init; }
    public string PriorityDisplay { get; init; } = "";

    public InventoryAttentionFamily Family { get; init; }
    public string FamilyDisplay { get; init; } = "";

    public InventoryAttentionReason PrimaryReason { get; init; }
    public string PrimaryReasonDisplay { get; init; } = "";

    public IReadOnlyList<InventoryAttentionReason> SecondaryReasons { get; init; } = [];
    public IReadOnlyList<string> SecondaryReasonDisplays { get; init; } = [];

    public InventoryOperatorAction Action { get; init; }
    public string ActionDisplay { get; init; } = "";

    public InventoryAttentionConfidence Confidence { get; init; }
    public string ConfidenceDisplay { get; init; } = "";

    public string Explanation { get; init; } = "";

    public string ProjectedExcess30Display { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ProjectedExpirySurplusDisplay { get; init; } = InventoryProjectionPresentation.EmDash;
    public string ProjectedExpirySurplusValueDisplay { get; init; } = InventoryProjectionPresentation.EmDash;
    public string SurplusValueQualityDisplay { get; init; } = InventoryProjectionPresentation.CostUnavailableLabel;

    /// <summary>True só quando não há atenção e a análise está disponível.</summary>
    public bool IsAllClear { get; init; }
}

/// <summary>Presentation em lote. Ordem = snapshot 70E (Intelligence.Rows). Sem reordenar.</summary>
public sealed class InventoryAttentionPresentationSnapshot
{
    public DateTime Today { get; init; }
    public int QueryCount { get; init; }
    public IReadOnlyList<InventoryAttentionPresentationRow> Rows { get; init; } = [];
}

/// <summary>
/// Rótulos PT-BR da priorização 70E. Sem I/O, UI, compra ou preço.
/// Reusa formatação 70C/70D (quantidade, —, qualidade de custo).
/// </summary>
public static class InventoryAttentionPresentation
{
    public const string PriorityCritical = "Crítica";
    public const string PriorityHigh = "Alta";
    public const string PriorityMedium = "Média";
    public const string PriorityLow = "Baixa";
    public const string PriorityNormal = "Normal";

    public const string FamilyData = "Conferência de dados";
    public const string FamilyExpiry = "Validade";
    public const string FamilyExcess = "Excesso de estoque";
    public const string FamilyTurnover = "Giro";
    public const string FamilyNormal = "Normal";

    public const string ActionNoneClear = "Nenhuma ação imediata";
    public const string ActionNoneUnavailable = "Sem recomendação";
    public const string ActionEvaluateExcess = "Avaliar excesso";

    public const string ConfidenceReliable = "Análise disponível";
    public const string ConfidenceLimited = "Análise com limitações";
    public const string ConfidenceUnavailable = "Análise indisponível";

    public static InventoryAttentionPresentationSnapshot Apply(
        InventoryAttentionSnapshot? snapshot,
        InventoryProjectionPresentationSnapshot? presented = null)
    {
        snapshot ??= new InventoryAttentionSnapshot();
        presented ??= new InventoryProjectionPresentationSnapshot();
        var lookup = presented.ByProductId ?? new Dictionary<int, InventoryProjectedProductPresentation>();
        var rows = new List<InventoryAttentionPresentationRow>(snapshot.Results.Count);
        foreach (var result in snapshot.Results ?? [])
        {
            lookup.TryGetValue(result.ProductId, out var product);
            rows.Add(FromResult(result, product));
        }

        return new InventoryAttentionPresentationSnapshot
        {
            Today = snapshot.Today,
            QueryCount = snapshot.QueryCount,
            Rows = rows,
        };
    }

    public static InventoryAttentionPresentationRow FromResult(
        InventoryAttentionResult? result,
        InventoryProjectedProductPresentation? presented = null)
    {
        result ??= new InventoryAttentionResult();
        var secondary = result.SecondaryReasons ?? [];
        var secondaryDisplays = new List<string>(secondary.Count);
        foreach (var reason in secondary)
            secondaryDisplays.Add(ReasonLabel(reason));

        var allClear = result.PrimaryReason == InventoryAttentionReason.None
            && result.Confidence == InventoryAttentionConfidence.Reliable;

        return new InventoryAttentionPresentationRow
        {
            ProductId = result.ProductId,
            Priority = result.Priority,
            PriorityDisplay = PriorityLabel(result.Priority),
            Family = result.Family,
            FamilyDisplay = FamilyLabel(result.Family),
            PrimaryReason = result.PrimaryReason,
            PrimaryReasonDisplay = ReasonLabel(result.PrimaryReason),
            SecondaryReasons = secondary,
            SecondaryReasonDisplays = secondaryDisplays,
            Action = result.Action,
            ActionDisplay = ActionLabel(result.Action, result.Confidence),
            Confidence = result.Confidence,
            ConfidenceDisplay = ConfidenceLabel(result.Confidence),
            Explanation = ExplanationOf(result.PrimaryReason, allClear),
            ProjectedExcess30Display = InventoryProjectionPresentation.FormatCalculatedQty(
                result.ProjectedExcessQuantity),
            ProjectedExpirySurplusDisplay = InventoryProjectionPresentation.FormatCalculatedQty(
                result.ProjectedExpirySurplusQuantity),
            ProjectedExpirySurplusValueDisplay = FormatExpirySurplusValue(result, presented),
            SurplusValueQualityDisplay = presented?.SurplusValueQualityDisplay
                ?? InventoryProjectionPresentation.SurplusValueQualityLabel(result.SurplusValueQuality),
            IsAllClear = allClear,
        };
    }

    public static string PriorityLabel(InventoryAttentionPriority priority) =>
        priority switch
        {
            InventoryAttentionPriority.Critical => PriorityCritical,
            InventoryAttentionPriority.High => PriorityHigh,
            InventoryAttentionPriority.Medium => PriorityMedium,
            InventoryAttentionPriority.Low => PriorityLow,
            InventoryAttentionPriority.Normal => PriorityNormal,
            _ => "Prioridade não classificada",
        };

    public static string FamilyLabel(InventoryAttentionFamily family) =>
        family switch
        {
            InventoryAttentionFamily.DataQuality => FamilyData,
            InventoryAttentionFamily.Expiry => FamilyExpiry,
            InventoryAttentionFamily.Excess => FamilyExcess,
            InventoryAttentionFamily.Turnover => FamilyTurnover,
            InventoryAttentionFamily.Normal => FamilyNormal,
            _ => "Situação não classificada",
        };

    public static string ActionLabel(
        InventoryOperatorAction action,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable) =>
        action switch
        {
            InventoryOperatorAction.ReviewData => ValidityControlUi.ActionLabel(ValiditySuggestedAction.ReviewData),
            InventoryOperatorAction.RemoveExpired => ValidityControlUi.ActionLabel(ValiditySuggestedAction.RemoveExpired),
            InventoryOperatorAction.PrioritizeSale => ValidityControlUi.ActionLabel(ValiditySuggestedAction.PrioritizeSale),
            InventoryOperatorAction.Monitor => ValidityControlUi.ActionLabel(ValiditySuggestedAction.Monitor),
            InventoryOperatorAction.EvaluateExcess => ActionEvaluateExcess,
            InventoryOperatorAction.None when confidence == InventoryAttentionConfidence.Unavailable =>
                ActionNoneUnavailable,
            InventoryOperatorAction.None => ActionNoneClear,
            _ => "Ação não classificada",
        };

    public static string ConfidenceLabel(InventoryAttentionConfidence confidence) =>
        confidence switch
        {
            InventoryAttentionConfidence.Reliable => ConfidenceReliable,
            InventoryAttentionConfidence.Limited => ConfidenceLimited,
            InventoryAttentionConfidence.Unavailable => ConfidenceUnavailable,
            _ => "Confiança não classificada",
        };

    public static string ReasonLabel(InventoryAttentionReason reason) =>
        reason switch
        {
            InventoryAttentionReason.None => "Sem atenção",
            InventoryAttentionReason.InvalidInput => "Dados inválidos",
            InventoryAttentionReason.NegativeStock => "Estoque total negativo",
            InventoryAttentionReason.NegativeLocationStock => "Depósito ou geladeira negativo",
            InventoryAttentionReason.NegativeWarehouseStock => "Depósito negativo",
            InventoryAttentionReason.InconsistentStockTotals => "Total inconsistente",
            InventoryAttentionReason.TrackedQuantityExceedsWarehouse => "Lotes excedem o depósito",
            InventoryAttentionReason.DuplicateLotId => "Lotes duplicados",
            InventoryAttentionReason.InvalidLotQuantity => "Quantidade de lote inválida",
            InventoryAttentionReason.InvalidExpiryDate => "Validade inválida",
            InventoryAttentionReason.Expired => "Produto vencido",
            InventoryAttentionReason.ExpiresToday => "Vence hoje",
            InventoryAttentionReason.SurplusAtExpiry => "Sobra projetada até a validade",
            InventoryAttentionReason.NearExpiryWithoutSurplus => "Validade próxima",
            InventoryAttentionReason.DatedWithoutSurplusInWindow => "Validade a acompanhar",
            InventoryAttentionReason.ProjectedExcess30 => "Sobra projetada em 30 dias",
            InventoryAttentionReason.Idle => "Produto parado",
            InventoryAttentionReason.Undated => "Sem validade informada",
            InventoryAttentionReason.NoLot => "Sem lote identificado",
            InventoryAttentionReason.InsufficientHistory => "Histórico insuficiente",
            InventoryAttentionReason.NoPhysicalEvidence => "Sem histórico de estoque",
            InventoryAttentionReason.CompositionProduct => "Produto composto",
            InventoryAttentionReason.NoObservableDemand => "Sem giro observável",
            InventoryAttentionReason.ProjectionMissing => "Projeção indisponível",
            InventoryAttentionReason.DuplicateProjection => "Projeção inconsistente",
            _ => "Atenção não classificada",
        };

    public static string ReasonExplanation(InventoryAttentionReason reason) =>
        ExplanationOf(reason, allClear: reason == InventoryAttentionReason.None);

    static string ExplanationOf(InventoryAttentionReason reason, bool allClear) =>
        reason switch
        {
            InventoryAttentionReason.None when allClear =>
                "Não há atenção imediata neste produto.",
            InventoryAttentionReason.None =>
                "Não foi possível concluir uma recomendação para este produto.",
            InventoryAttentionReason.InvalidInput =>
                "Há números inconsistentes que impedem uma análise confiável. Confira o cadastro.",
            InventoryAttentionReason.NegativeStock =>
                "O estoque total está negativo. Confira o cadastro antes de qualquer ação.",
            InventoryAttentionReason.NegativeLocationStock =>
                "Há estoque negativo no depósito ou na geladeira. Confira as quantidades.",
            InventoryAttentionReason.NegativeWarehouseStock =>
                "O estoque do depósito está negativo. Confira as quantidades.",
            InventoryAttentionReason.InconsistentStockTotals =>
                "O estoque total é diferente da soma de depósito e geladeira. Confira o cadastro.",
            InventoryAttentionReason.TrackedQuantityExceedsWarehouse =>
                "A soma dos lotes é maior que o estoque do depósito. Confira os lotes.",
            InventoryAttentionReason.DuplicateLotId =>
                "Há lotes com identificador repetido. Confira o cadastro de lotes.",
            InventoryAttentionReason.InvalidLotQuantity =>
                "Há lote com quantidade negativa ou inválida. Confira os lotes.",
            InventoryAttentionReason.InvalidExpiryDate =>
                "Há validade cadastrada fora do formato esperado. Isso não é ausência de validade.",
            InventoryAttentionReason.Expired =>
                "Há produto vencido. Retire ou confira o estoque antes de qualquer outra ação.",
            InventoryAttentionReason.ExpiresToday =>
                "Há estoque que vence hoje. Priorize a saída. O produto ainda está válido hoje.",
            InventoryAttentionReason.SurplusAtExpiry =>
                "Pelo giro observado, há sobra projetada até a validade.",
            InventoryAttentionReason.NearExpiryWithoutSurplus =>
                "Há validade em até 7 dias, sem sobra projetada. Priorize a saída.",
            InventoryAttentionReason.DatedWithoutSurplusInWindow =>
                "Há validade entre 8 e 30 dias, sem sobra projetada. Acompanhe a saída.",
            InventoryAttentionReason.ProjectedExcess30 =>
                "O estoque atual é maior que a demanda projetada para os próximos 30 dias.",
            InventoryAttentionReason.Idle =>
                "O produto possui estoque e está sem venda observada há pelo menos 90 dias.",
            InventoryAttentionReason.Undated =>
                "Há lote sem data de validade. Confira o cadastro. Isso não significa que o produto não vence.",
            InventoryAttentionReason.NoLot =>
                "Há estoque do depósito sem lote identificado.",
            InventoryAttentionReason.InsufficientHistory =>
                "Ainda não há histórico suficiente para calcular uma projeção confiável.",
            InventoryAttentionReason.NoPhysicalEvidence =>
                "Não há evidência de entrada ou venda observável. Cadastro isolado não permite concluir o giro.",
            InventoryAttentionReason.CompositionProduct =>
                "Produto composto. O giro físico fica nos componentes.",
            InventoryAttentionReason.NoObservableDemand =>
                "Não há giro observável no período. Não é possível projetar demanda nem sobra.",
            InventoryAttentionReason.ProjectionMissing =>
                "A projeção deste produto não pôde ser composta. Atualize a análise e confira os dados.",
            InventoryAttentionReason.DuplicateProjection =>
                "Há mais de uma projeção para o mesmo produto. A análise foi suspensa para não escolher um valor.",
            _ => "Há uma atenção que não pôde ser descrita.",
        };

    static string FormatExpirySurplusValue(
        InventoryAttentionResult result,
        InventoryProjectedProductPresentation? presented)
    {
        if (presented is not null)
            return presented.SurplusValueDisplay;

        if (result.SurplusValueQuality == InventoryProjectionSurplusValueQuality.Unavailable)
            return InventoryProjectionPresentation.EmDash;

        return InventoryProjectionPresentation.EmDash;
    }
}
