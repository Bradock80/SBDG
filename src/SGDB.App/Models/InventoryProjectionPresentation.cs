using SGDB.Utils;

namespace SGDB.Models;

/// <summary>Sobra projetada em 30 dias. Sem faixa comercial "Atenção".</summary>
public enum InventoryProjectionExcessStatus
{
    Unavailable = 0,
    NoExcess,
    ProjectedExcess,
}

/// <summary>
/// Resumo de validade/risco por produto. Não apaga alertas adicionais.
/// Fatos observáveis de lote vencem "projeção indisponível".
/// </summary>
public enum InventoryProjectionValidityStatus
{
    InvalidExpiry = 0,
    Expired,
    ExpiresToday,
    SurplusAtExpiry,
    Dated,
    Undated,
    NoLot,
    ProjectionUnavailable,
}

/// <summary>
/// Qualidade do valor estimado da sobra até a validade.
/// Nunca afirma prejuízo ou perda.
/// </summary>
public enum InventoryProjectionSurplusValueQuality
{
    Unavailable = 0,
    CompleteRecorded,
    CompleteWithEstimate,
    Partial,
}

/// <summary>Lote pronto para o detalhe B5. Sem I/O.</summary>
public sealed class InventoryProjectedLotPresentation
{
    public int LotId { get; init; }
    public InventoryProjectionLotKind Kind { get; init; }
    public string KindDisplay { get; init; } = "";
    public double Quantity { get; init; }
    public string QuantityDisplay { get; init; } = "";
    public DateTime? ExpiryDate { get; init; }
    public string ExpiryDisplay { get; init; } = "";
    public int? DaysUntilExpiry { get; init; }
    public string DaysUntilExpiryDisplay { get; init; } = "";
    public bool AlreadyExpired { get; init; }
    public double? ProjectedSurplusAtExpiry { get; init; }
    public string SurplusAtExpiryDisplay { get; init; } = "";
    public double? ProjectedSurplusValue { get; init; }
    public string SurplusValueDisplay { get; init; } = "";
    public LotCostSource CostSource { get; init; }
    public string CostSourceDisplay { get; init; } = "";
}

/// <summary>
/// Produto pronto para grade B4 e detalhe B5. Formatação, não regra de estoque.
/// </summary>
public sealed class InventoryProjectedProductPresentation
{
    public int ProductId { get; init; }
    public string Name { get; init; } = "";
    public string Code { get; init; } = "";

    public InventoryProjectionExcessStatus ExcessStatus { get; init; }
    public string ExcessStatusDisplay { get; init; } = "";
    public double? ProjectedExcessQuantity { get; init; }
    public string Surplus30Display { get; init; } = "";

    public InventoryProjectionValidityStatus ValidityStatus { get; init; }
    public string ValidityRiskDisplay { get; init; } = "";

    public int HorizonDays { get; init; }
    public double? ProjectedDemand { get; init; }
    public string ProjectedDemandDisplay { get; init; } = "";
    public string DemandCaption { get; init; } = "";

    public double? ProjectedExpirySurplusQuantity { get; init; }
    public string ExpirySurplusDisplay { get; init; } = "";

    public double? ProjectedExpirySurplusValue { get; init; }
    public InventoryProjectionSurplusValueQuality SurplusValueQuality { get; init; }
    public string SurplusValueDisplay { get; init; } = "";
    public string SurplusValueQualityDisplay { get; init; } = "";
    public string SurplusValueCaption { get; init; } = "";

    public double TrackedLotQuantity { get; init; }
    public string TrackedLotQuantityDisplay { get; init; } = "";
    public double UntrackedWarehouseQuantity { get; init; }
    public string UntrackedWarehouseQuantityDisplay { get; init; } = "";
    public bool HasUntrackedWarehouse { get; init; }
    public string UntrackedWarehouseAlert { get; init; } = "";

    public bool HasLotLocationLimitation { get; init; }
    public string FridgeLimitationAlert { get; init; } = "";

    public InventorySkuProjectionBlockedReason SkuBlockedReason { get; init; }
    public string SkuBlockedShortText { get; init; } = "";
    public string SkuBlockedExplanation { get; init; } = "";
    public InventoryExpiryProjectionBlockedReason ExpiryBlockedReason { get; init; }
    public string ExpiryBlockedShortText { get; init; } = "";
    public string ExpiryBlockedExplanation { get; init; } = "";

    public IReadOnlyList<string> Alerts { get; init; } = [];
    public IReadOnlyList<InventoryProjectedLotPresentation> Lots { get; init; } = [];
}

/// <summary>Saída em memória de <see cref="InventoryProjectionPresentation.Apply"/>.</summary>
public sealed class InventoryProjectionPresentationSnapshot
{
    public DateTime Today { get; init; }
    public IReadOnlyList<InventoryProjectedProductPresentation> Products { get; init; } = [];
    public IReadOnlyDictionary<int, InventoryProjectedProductPresentation> ByProductId { get; init; } =
        new Dictionary<int, InventoryProjectedProductPresentation>();
}

/// <summary>
/// Apresentação pura 70D-B3. Sem banco, UI, relógio de sistema, consulta ou N+1.
/// Não altera VMV, cobertura, motor B1/B2 nem 70C.
/// </summary>
public static class InventoryProjectionPresentation
{
    public const string EmDash = "—";
    public const double Epsilon = 0.0001;

    public const string ExcessUnavailableLabel = "Projeção indisponível";
    public const string ExcessNoneLabel = "Sem sobra 30d";
    public const string ExcessProjectedLabel = "Sobra projetada 30d";

    public const string ValidityInvalidLabel = "Validade cadastrada inválida";
    public const string ValidityExpiredLabel = "Vencido";
    public const string ValidityExpiresTodayLabel = "Vence hoje";
    public const string ValiditySurplusLabel = "Sobra até a validade";
    public const string ValidityDatedLabel = "Com validade";
    public const string ValidityUndatedLabel = "Sem validade informada";
    public const string ValidityNoLotLabel = "Sem lote identificado";
    public const string ValidityUnavailableLabel = "Projeção indisponível";

    public const string SurplusValueCaption = "Valor estimado da sobra";
    public const string SurplusValuePartialLabel = "Valor parcial";
    public const string SurplusValueEstimatedMarker = "*";
    public const string CostLotRecordedLabel = "Custo do lote";
    public const string CostAverageEstimateLabel = "Estimado pelo custo médio atual";
    public const string CostUnavailableLabel = "Sem custo disponível";
    public const string FridgeLimitationText =
        "Projeção por lote não distingue depósito e geladeira.";
    public const string DemandNotGuaranteed =
        "Estimativa com base no VMV 30. Não é venda garantida.";

    public static InventoryProjectionPresentationSnapshot Apply(InventoryProjectionSnapshot snapshot)
    {
        snapshot ??= new InventoryProjectionSnapshot();
        var products = new List<InventoryProjectedProductPresentation>();
        var map = new Dictionary<int, InventoryProjectedProductPresentation>();

        if (snapshot.Intelligence.Rows.Count > 0)
        {
            foreach (var row in snapshot.Intelligence.Rows)
            {
                snapshot.ByProductId.TryGetValue(row.ProductId, out var projected);
                var presented = FromProduct(projected ?? new InventoryProjectedProduct { ProductId = row.ProductId }, row);
                products.Add(presented);
                map[presented.ProductId] = presented;
            }
        }
        else
        {
            foreach (var projected in snapshot.ByProductId.Values)
            {
                var presented = FromProduct(projected);
                products.Add(presented);
                map[presented.ProductId] = presented;
            }
        }

        return new InventoryProjectionPresentationSnapshot
        {
            Today = snapshot.Today,
            Products = products,
            ByProductId = map,
        };
    }

    public static InventoryProjectedProductPresentation FromProduct(
        InventoryProjectedProduct product,
        ProductTurnoverRow? turnover = null)
    {
        product ??= new InventoryProjectedProduct();
        var projection = product.Projection ?? new InventoryProjectionResult();
        var costs = IndexCostsByLotId(product.LotCosts);
        var excess = ClassifyExcess(projection);
        var validity = ClassifyValidity(projection);
        var expiryQty = SumExpirySurplusQuantity(projection.Lots);
        var (expiryValue, valueQuality) = ResolveExpirySurplusValue(projection.Lots, costs);
        var lots = PresentLots(projection.Lots, costs);
        var untracked = projection.UntrackedWarehouseQuantity;
        var hasUntracked = IsPositive(untracked);
        var untrackedAlert = hasUntracked
            ? $"{FormatQty(untracked)} un. do depósito sem lote identificado"
            : "";
        var fridgeAlert = projection.HasLotLocationLimitation ? FridgeLimitationText : "";
        var skuText = SkuBlockedText(projection.SkuBlockedReason);
        var expiryText = ExpiryBlockedText(projection.ExpiryBlockedReason);
        var horizon = Math.Max(0, projection.HorizonDays);

        return new InventoryProjectedProductPresentation
        {
            ProductId = product.ProductId != 0 ? product.ProductId : turnover?.ProductId ?? 0,
            Name = turnover?.Name ?? "",
            Code = turnover?.Code ?? "",
            ExcessStatus = excess,
            ExcessStatusDisplay = ExcessStatusLabel(excess),
            ProjectedExcessQuantity = excess == InventoryProjectionExcessStatus.Unavailable
                ? null
                : projection.ProjectedExcessQuantity,
            Surplus30Display = FormatCalculatedQty(
                excess != InventoryProjectionExcessStatus.Unavailable
                    ? projection.ProjectedExcessQuantity
                    : null),
            ValidityStatus = validity,
            ValidityRiskDisplay = ValidityStatusLabel(validity),
            HorizonDays = horizon,
            ProjectedDemand = projection.CanProjectSku ? projection.ProjectedDemand : null,
            ProjectedDemandDisplay = FormatCalculatedQty(
                projection.CanProjectSku ? projection.ProjectedDemand : null),
            DemandCaption = DemandCaptionFor(horizon),
            ProjectedExpirySurplusQuantity = expiryQty,
            ExpirySurplusDisplay = FormatCalculatedQty(expiryQty),
            ProjectedExpirySurplusValue = expiryValue,
            SurplusValueQuality = valueQuality,
            SurplusValueDisplay = FormatSurplusValue(expiryValue, valueQuality),
            SurplusValueQualityDisplay = SurplusValueQualityLabel(valueQuality),
            SurplusValueCaption = SurplusValueCaption,
            TrackedLotQuantity = projection.TrackedLotQuantity,
            TrackedLotQuantityDisplay = FormatQty(projection.TrackedLotQuantity),
            UntrackedWarehouseQuantity = untracked,
            UntrackedWarehouseQuantityDisplay = FormatQty(untracked),
            HasUntrackedWarehouse = hasUntracked,
            UntrackedWarehouseAlert = untrackedAlert,
            HasLotLocationLimitation = projection.HasLotLocationLimitation,
            FridgeLimitationAlert = fridgeAlert,
            SkuBlockedReason = projection.SkuBlockedReason,
            SkuBlockedShortText = skuText.ShortText,
            SkuBlockedExplanation = skuText.Explanation,
            ExpiryBlockedReason = projection.ExpiryBlockedReason,
            ExpiryBlockedShortText = expiryText.ShortText,
            ExpiryBlockedExplanation = expiryText.Explanation,
            Alerts = BuildAlerts(
                fridgeAlert,
                untrackedAlert,
                skuText.ShortText,
                expiryText.ShortText,
                validity),
            Lots = lots,
        };
    }

    public static InventoryProjectionExcessStatus ClassifyExcess(InventoryProjectionResult projection)
    {
        projection ??= new InventoryProjectionResult();
        if (!projection.CanProjectSku
            || projection.ProjectedExcessQuantity is not double qty
            || !double.IsFinite(qty))
            return InventoryProjectionExcessStatus.Unavailable;
        return qty > Epsilon
            ? InventoryProjectionExcessStatus.ProjectedExcess
            : InventoryProjectionExcessStatus.NoExcess;
    }

    public static InventoryProjectionValidityStatus ClassifyValidity(InventoryProjectionResult projection)
    {
        projection ??= new InventoryProjectionResult();
        if (projection.ExpiryBlockedReason == InventoryExpiryProjectionBlockedReason.InvalidExpiryDate)
            return InventoryProjectionValidityStatus.InvalidExpiry;

        var lots = projection.Lots ?? [];
        var hasExpired = false;
        var hasExpiresToday = false;
        var hasDated = false;
        var hasUndated = false;
        foreach (var lot in lots)
        {
            if (lot.Kind == InventoryProjectionLotKind.AlreadyExpired || lot.AlreadyExpired)
                hasExpired = true;
            else if (lot.Kind == InventoryProjectionLotKind.ExpiresToday)
                hasExpiresToday = true;
            else if (lot.Kind == InventoryProjectionLotKind.Dated)
                hasDated = true;
            else if (lot.Kind == InventoryProjectionLotKind.Undated)
                hasUndated = true;
        }

        if (hasExpired)
            return InventoryProjectionValidityStatus.Expired;
        if (hasExpiresToday)
            return InventoryProjectionValidityStatus.ExpiresToday;

        var surplus = SumExpirySurplusQuantity(lots);
        if (surplus is double s && s > Epsilon)
            return InventoryProjectionValidityStatus.SurplusAtExpiry;
        if (hasDated)
            return InventoryProjectionValidityStatus.Dated;
        if (hasUndated)
            return InventoryProjectionValidityStatus.Undated;

        if (lots.Count == 0 && projection.TrackedLotQuantity <= Epsilon)
            return InventoryProjectionValidityStatus.NoLot;

        return InventoryProjectionValidityStatus.ProjectionUnavailable;
    }

    public static string ExcessStatusLabel(InventoryProjectionExcessStatus status) =>
        status switch
        {
            InventoryProjectionExcessStatus.NoExcess => ExcessNoneLabel,
            InventoryProjectionExcessStatus.ProjectedExcess => ExcessProjectedLabel,
            _ => ExcessUnavailableLabel,
        };

    public static string ValidityStatusLabel(InventoryProjectionValidityStatus status) =>
        status switch
        {
            InventoryProjectionValidityStatus.InvalidExpiry => ValidityInvalidLabel,
            InventoryProjectionValidityStatus.Expired => ValidityExpiredLabel,
            InventoryProjectionValidityStatus.ExpiresToday => ValidityExpiresTodayLabel,
            InventoryProjectionValidityStatus.SurplusAtExpiry => ValiditySurplusLabel,
            InventoryProjectionValidityStatus.Dated => ValidityDatedLabel,
            InventoryProjectionValidityStatus.Undated => ValidityUndatedLabel,
            InventoryProjectionValidityStatus.NoLot => ValidityNoLotLabel,
            _ => ValidityUnavailableLabel,
        };

    public static string LotKindLabel(InventoryProjectionLotKind kind) =>
        kind switch
        {
            InventoryProjectionLotKind.AlreadyExpired => ValidityExpiredLabel,
            InventoryProjectionLotKind.ExpiresToday => ValidityExpiresTodayLabel,
            InventoryProjectionLotKind.Dated => ValidityDatedLabel,
            InventoryProjectionLotKind.Undated => ValidityUndatedLabel,
            _ => ValidityUnavailableLabel,
        };

    public static string CostSourceLabel(LotCostSource source) =>
        source switch
        {
            LotCostSource.LotRecorded => CostLotRecordedLabel,
            LotCostSource.CurrentAverageEstimate => CostAverageEstimateLabel,
            _ => CostUnavailableLabel,
        };

    public static string SurplusValueQualityLabel(InventoryProjectionSurplusValueQuality quality) =>
        quality switch
        {
            InventoryProjectionSurplusValueQuality.CompleteRecorded => CostLotRecordedLabel,
            InventoryProjectionSurplusValueQuality.CompleteWithEstimate => CostAverageEstimateLabel,
            InventoryProjectionSurplusValueQuality.Partial => SurplusValuePartialLabel,
            _ => CostUnavailableLabel,
        };

    public static (string ShortText, string Explanation) SkuBlockedText(
        InventorySkuProjectionBlockedReason reason) =>
        reason switch
        {
            InventorySkuProjectionBlockedReason.None => ("", ""),
            InventorySkuProjectionBlockedReason.InvalidInput =>
                ("Dados inválidos",
                    "Números ou horizonte inconsistentes para calcular a projeção."),
            InventorySkuProjectionBlockedReason.CompositionProduct =>
                ("Produto composto",
                    "Produto composto. O giro físico fica nos componentes."),
            InventorySkuProjectionBlockedReason.NoPhysicalEvidence =>
                ("Sem histórico confiável",
                    "Sem histórico confiável de estoque (cadastro sem entrada ou venda observável)."),
            InventorySkuProjectionBlockedReason.InsufficientHistory =>
                ("Histórico insuficiente",
                    "Histórico inferior a 30 dias. A projeção não é calculada."),
            InventorySkuProjectionBlockedReason.NegativeStock =>
                ("Estoque inconsistente",
                    "Estoque total negativo. Confira o cadastro antes de projetar."),
            InventorySkuProjectionBlockedReason.NegativeLocationStock =>
                ("Estoque inconsistente",
                    "Depósito ou geladeira com estoque negativo."),
            InventorySkuProjectionBlockedReason.InconsistentStockTotals =>
                ("Estoque inconsistente",
                    "Total diferente da soma de depósito e geladeira."),
            InventorySkuProjectionBlockedReason.NoObservableDemand =>
                ("Sem giro observável",
                    "Sem giro observável no período. Não é possível projetar demanda nem sobra."),
            _ => ("Projeção indisponível", "A projeção de demanda não pôde ser calculada."),
        };

    public static (string ShortText, string Explanation) ExpiryBlockedText(
        InventoryExpiryProjectionBlockedReason reason) =>
        reason switch
        {
            InventoryExpiryProjectionBlockedReason.None => ("", ""),
            InventoryExpiryProjectionBlockedReason.InvalidInput =>
                ("Dados inválidos",
                    "Números inconsistentes para calcular a projeção por validade."),
            InventoryExpiryProjectionBlockedReason.CompositionProduct =>
                ("Produto composto",
                    "Produto composto. O giro físico fica nos componentes."),
            InventoryExpiryProjectionBlockedReason.NoPhysicalEvidence =>
                ("Sem histórico confiável",
                    "Sem histórico confiável de estoque (cadastro sem entrada ou venda observável)."),
            InventoryExpiryProjectionBlockedReason.InsufficientHistory =>
                ("Histórico insuficiente",
                    "Histórico inferior a 30 dias. A projeção de validade não é calculada."),
            InventoryExpiryProjectionBlockedReason.NoObservableDemand =>
                ("Sem giro observável",
                    "Sem giro observável no período. Não é possível projetar sobra até a validade."),
            InventoryExpiryProjectionBlockedReason.NegativeWarehouseStock =>
                ("Estoque inconsistente",
                    "Estoque do depósito negativo."),
            InventoryExpiryProjectionBlockedReason.NegativeLocationStock =>
                ("Estoque inconsistente",
                    "Geladeira com estoque negativo."),
            InventoryExpiryProjectionBlockedReason.InconsistentStockTotals =>
                ("Estoque inconsistente",
                    "Total diferente da soma de depósito e geladeira."),
            InventoryExpiryProjectionBlockedReason.DuplicateLotId =>
                ("Lotes duplicados",
                    "Há lotes com identificador repetido. O detalhe por lote não é exibido."),
            InventoryExpiryProjectionBlockedReason.InvalidLotQuantity =>
                ("Quantidade de lote inválida",
                    "Há lote com quantidade negativa ou inválida."),
            InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse =>
                ("Lotes excedem o depósito",
                    "A soma dos lotes é maior que o estoque do depósito."),
            InventoryExpiryProjectionBlockedReason.InvalidExpiryDate =>
                ("Validade cadastrada inválida",
                    "Há validade cadastrada com texto fora do formato aaaa-MM-dd. Não é ausência de validade."),
            _ => ("Projeção indisponível", "A projeção de validade não pôde ser calculada."),
        };

    public static string DemandCaptionFor(int horizonDays) =>
        $"Demanda projetada em {Math.Max(0, horizonDays)} dias";

    public static string FormatQty(double qty) => ProductLotListRow.FormatQty(qty);

    public static string FormatCalculatedQty(double? qty)
    {
        if (qty is not double value || !double.IsFinite(value))
            return EmDash;
        return FormatQty(value);
    }

    public static string FormatDate(DateTime? date) =>
        date is DateTime d ? d.ToString("dd/MM/yyyy", ProductPriceHelper.Br) : EmDash;

    public static string FormatDays(int? days) =>
        days is int value ? value.ToString("N0", ProductPriceHelper.Br) : EmDash;

    public static string FormatMoney(double? value) =>
        value is double v && double.IsFinite(v) ? ProductPriceHelper.MoneyBr(v) : EmDash;

    public static double? SumExpirySurplusQuantity(IReadOnlyList<InventoryProjectionLotResult>? lots)
    {
        if (lots is null || lots.Count == 0)
            return null;

        double sum = 0;
        var any = false;
        foreach (var lot in lots)
        {
            if (lot.ProjectedSurplusAtExpiry is not double qty
                || !double.IsFinite(qty)
                || qty < 0)
                continue;
            sum += qty;
            any = true;
        }

        return any ? sum : null;
    }

    static (double? Value, InventoryProjectionSurplusValueQuality Quality) ResolveExpirySurplusValue(
        IReadOnlyList<InventoryProjectionLotResult>? lots,
        IReadOnlyDictionary<int, InventoryProjectedLotCost> costs)
    {
        var surplusLots = new List<InventoryProjectionLotResult>();
        foreach (var lot in lots ?? [])
        {
            if (lot.ProjectedSurplusAtExpiry is double qty
                && double.IsFinite(qty)
                && qty > Epsilon)
                surplusLots.Add(lot);
        }

        if (surplusLots.Count == 0)
        {
            var rolled = SumExpirySurplusQuantity(lots);
            if (rolled is double zero && zero <= Epsilon)
                return (0, InventoryProjectionSurplusValueQuality.CompleteRecorded);
            return (null, InventoryProjectionSurplusValueQuality.Unavailable);
        }

        double sum = 0;
        var valued = 0;
        var missing = 0;
        var estimates = 0;

        foreach (var lot in surplusLots)
        {
            costs.TryGetValue(lot.LotId, out var cost);
            var source = cost?.CostSource ?? LotCostSource.Unavailable;
            if (source == LotCostSource.Unavailable
                || lot.ProjectedSurplusValue is not double value
                || !double.IsFinite(value)
                || value < 0)
            {
                missing++;
                continue;
            }

            sum += value;
            valued++;
            if (source == LotCostSource.CurrentAverageEstimate)
                estimates++;
        }

        if (valued == 0)
            return (null, InventoryProjectionSurplusValueQuality.Unavailable);
        if (missing > 0)
            return (sum, InventoryProjectionSurplusValueQuality.Partial);
        if (estimates > 0)
            return (sum, InventoryProjectionSurplusValueQuality.CompleteWithEstimate);
        return (sum, InventoryProjectionSurplusValueQuality.CompleteRecorded);
    }

    static string FormatSurplusValue(double? value, InventoryProjectionSurplusValueQuality quality)
    {
        if (quality == InventoryProjectionSurplusValueQuality.Unavailable
            || value is not double amount
            || !double.IsFinite(amount))
            return EmDash;

        var money = ProductPriceHelper.MoneyBr(amount);
        return quality switch
        {
            InventoryProjectionSurplusValueQuality.CompleteWithEstimate => money + SurplusValueEstimatedMarker,
            InventoryProjectionSurplusValueQuality.Partial => $"{money} (parcial)",
            _ => money,
        };
    }

    static IReadOnlyList<InventoryProjectedLotPresentation> PresentLots(
        IReadOnlyList<InventoryProjectionLotResult>? lots,
        IReadOnlyDictionary<int, InventoryProjectedLotCost> costs)
    {
        if (lots is null || lots.Count == 0)
            return [];

        var list = new List<InventoryProjectedLotPresentation>(lots.Count);
        foreach (var lot in lots)
        {
            costs.TryGetValue(lot.LotId, out var cost);
            var source = cost?.CostSource ?? LotCostSource.Unavailable;
            var surplusDisplay = FormatCalculatedQty(lot.ProjectedSurplusAtExpiry);
            var valueDisplay = lot.ProjectedSurplusValue is double value && double.IsFinite(value)
                ? source == LotCostSource.CurrentAverageEstimate
                    ? ProductPriceHelper.MoneyBr(value) + SurplusValueEstimatedMarker
                    : ProductPriceHelper.MoneyBr(value)
                : EmDash;

            list.Add(new InventoryProjectedLotPresentation
            {
                LotId = lot.LotId,
                Kind = lot.Kind,
                KindDisplay = LotKindLabel(lot.Kind),
                Quantity = lot.Quantity,
                QuantityDisplay = FormatQty(lot.Quantity),
                ExpiryDate = lot.ExpiryDate,
                ExpiryDisplay = FormatDate(lot.ExpiryDate),
                DaysUntilExpiry = lot.DaysUntilExpiry,
                DaysUntilExpiryDisplay = FormatDays(lot.DaysUntilExpiry),
                AlreadyExpired = lot.AlreadyExpired,
                ProjectedSurplusAtExpiry = lot.ProjectedSurplusAtExpiry,
                SurplusAtExpiryDisplay = surplusDisplay,
                ProjectedSurplusValue = lot.ProjectedSurplusValue,
                SurplusValueDisplay = valueDisplay,
                CostSource = source,
                CostSourceDisplay = CostSourceLabel(source),
            });
        }

        return list;
    }

    static IReadOnlyList<string> BuildAlerts(
        string fridgeAlert,
        string untrackedAlert,
        string skuShort,
        string expiryShort,
        InventoryProjectionValidityStatus validity)
    {
        var alerts = new List<string>();
        if (!string.IsNullOrEmpty(fridgeAlert))
            alerts.Add(fridgeAlert);
        if (!string.IsNullOrEmpty(untrackedAlert))
            alerts.Add(untrackedAlert);
        if (!string.IsNullOrEmpty(skuShort))
            alerts.Add(skuShort);
        if (!string.IsNullOrEmpty(expiryShort)
            && expiryShort != ValidityStatusLabel(validity)
            && expiryShort != skuShort)
            alerts.Add(expiryShort);
        return alerts;
    }

    static Dictionary<int, InventoryProjectedLotCost> IndexCostsByLotId(
        IReadOnlyList<InventoryProjectedLotCost>? costs)
    {
        var map = new Dictionary<int, InventoryProjectedLotCost>();
        if (costs is null)
            return map;

        foreach (var cost in costs)
        {
            if (!map.ContainsKey(cost.LotId))
                map[cost.LotId] = cost;
        }

        return map;
    }

    static bool IsPositive(double value) =>
        double.IsFinite(value) && value > Epsilon;
}
