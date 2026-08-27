using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Regras da Central de Validades: faixas exclusivas da B2, ordenação, cards e alertas.
/// Não lê extra_json.data_validade. Não inventa lote.
/// </summary>
public static class ValidityControlEngine
{
    public const string MissingExpiryLabel = "Validade não informada";
    public const string UntrackedStockLabel = "Estoque sem validade/lote identificado";
    public const double CostAvailableThreshold = 0.009;

    public readonly record struct LotCostResolution(double? UsedCost, LotCostSource Source);

    /// <summary>
    /// Custo usado no Valor do lote: unit_cost gravado, senão médio atual, senão indisponível.
    /// Não afirma FIFO nem custo histórico exato.
    /// </summary>
    public static LotCostResolution ResolveLotCost(double recordedUnitCost, double productCostPrice)
    {
        if (recordedUnitCost > CostAvailableThreshold)
            return new(recordedUnitCost, LotCostSource.LotRecorded);
        if (productCostPrice > CostAvailableThreshold)
            return new(productCostPrice, LotCostSource.CurrentAverageEstimate);
        return new(null, LotCostSource.Unavailable);
    }

    public static double? ComputeLotValue(double quantity, double? usedCost)
    {
        if (usedCost is not double cost)
            return null;
        var safeQty = Math.Max(0, quantity);
        return ProductPriceHelper.RoundPrice(safeQty * cost);
    }

    /// <summary>
    /// Mapeamento 70B2 (somente faixas já existentes; sem VMV/giro/sobra):
    /// Expired → RemoveExpired;
    /// Today / Within7 → PrioritizeSale;
    /// Within15 / Within30 / Within60 / Within90 → Monitor;
    /// Ok → None;
    /// MissingExpiry / UntrackedStock / UninformedLot → ReviewData.
    /// ConsiderPromotion não é emitido aqui (70D+70F).
    /// Custo ausente não vira ReviewData se a faixa de validade já for útil.
    /// </summary>
    public static ValiditySuggestedAction ResolveSuggestedAction(
        ValidityControlRowKind kind,
        ProductExpiryStatusKind status,
        double quantity)
    {
        if (kind is ValidityControlRowKind.MissingExpiry
            or ValidityControlRowKind.UntrackedStock
            or ValidityControlRowKind.UninformedLot)
            return ValiditySuggestedAction.ReviewData;

        if (status == ProductExpiryStatusKind.Expired)
            return ValiditySuggestedAction.RemoveExpired;

        return status switch
        {
            ProductExpiryStatusKind.Today or ProductExpiryStatusKind.Within7
                => ValiditySuggestedAction.PrioritizeSale,
            ProductExpiryStatusKind.Within15 or ProductExpiryStatusKind.Within30
                or ProductExpiryStatusKind.Within60 or ProductExpiryStatusKind.Within90
                => ValiditySuggestedAction.Monitor,
            ProductExpiryStatusKind.Ok => ValiditySuggestedAction.None,
            _ => ValiditySuggestedAction.ReviewData,
        };
    }

    /// <summary>
    /// 0 = mais urgente. Segurança operacional antes de dinheiro.
    /// Rank 1 (ConsiderPromotion) fica reservado para 70D/70F; o 70B2 não o emite.
    /// </summary>
    public static int AttentionRankOf(ValiditySuggestedAction action) =>
        action switch
        {
            ValiditySuggestedAction.RemoveExpired => 0,
            ValiditySuggestedAction.ConsiderPromotion => 1,
            ValiditySuggestedAction.PrioritizeSale => 2,
            ValiditySuggestedAction.ReviewData => 3,
            ValiditySuggestedAction.Monitor => 4,
            _ => 5,
        };

    public static string FormatSuggestedActionReason(
        ValiditySuggestedAction action,
        ValidityControlRowKind kind,
        ProductExpiryStatusKind status,
        double quantity,
        int? daysRemaining,
        double? lotValue)
    {
        _ = lotValue;
        var qty = ProductLotListRow.FormatQty(Math.Max(0, quantity));
        return action switch
        {
            ValiditySuggestedAction.RemoveExpired =>
                $"Produto vencido com {qty} em estoque.",
            ValiditySuggestedAction.PrioritizeSale when status == ProductExpiryStatusKind.Today =>
                "Vence hoje. Priorizar saída.",
            ValiditySuggestedAction.PrioritizeSale =>
                $"Validade em {daysRemaining ?? 0} dias. Priorizar saída.",
            ValiditySuggestedAction.Monitor =>
                $"Validade em {daysRemaining ?? 0} dias. Acompanhar saída.",
            ValiditySuggestedAction.ReviewData when kind == ValidityControlRowKind.UntrackedStock =>
                "Estoque sem lote identificado.",
            ValiditySuggestedAction.ReviewData when kind == ValidityControlRowKind.MissingExpiry =>
                "Estoque sem validade identificada.",
            ValiditySuggestedAction.ReviewData =>
                "Lote sem data de validade.",
            _ => "",
        };
    }

    public static ProductExpiryStatusKind? BucketOf(ValidityControlFilterKind filter) =>
        filter switch
        {
            ValidityControlFilterKind.Expired => ProductExpiryStatusKind.Expired,
            ValidityControlFilterKind.Today => ProductExpiryStatusKind.Today,
            ValidityControlFilterKind.Days7 => ProductExpiryStatusKind.Within7,
            ValidityControlFilterKind.Days15 => ProductExpiryStatusKind.Within15,
            ValidityControlFilterKind.Days30 => ProductExpiryStatusKind.Within30,
            ValidityControlFilterKind.Days60 => ProductExpiryStatusKind.Within60,
            ValidityControlFilterKind.Days90 => ProductExpiryStatusKind.Within90,
            ValidityControlFilterKind.Uninformed => ProductExpiryStatusKind.Uninformed,
            _ => null,
        };

    public static string ToneFor(ProductExpiryStatusKind kind) =>
        kind switch
        {
            ProductExpiryStatusKind.Expired => "expired",
            ProductExpiryStatusKind.Today or ProductExpiryStatusKind.Within7 => "alert",
            ProductExpiryStatusKind.Within15 or ProductExpiryStatusKind.Within30 => "attention",
            ProductExpiryStatusKind.Within60 or ProductExpiryStatusKind.Within90 => "notice",
            ProductExpiryStatusKind.Uninformed => "info",
            _ => "ok",
        };

    public static bool MatchesFilter(ValidityControlRow row, ValidityControlFilterKind filter)
    {
        var bucket = BucketOf(filter);
        return bucket is null || row.Status.Kind == bucket;
    }

    public static bool MatchesSearch(
        ValidityControlRow row, string? search, string? group, string? brand)
    {
        if (!string.IsNullOrWhiteSpace(group)
            && !string.Equals(row.GroupName, group.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(brand)
            && !string.Equals(row.BrandName, brand.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(search))
            return true;
        var term = search.Trim();
        return row.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || row.ProductCode.Contains(term, StringComparison.OrdinalIgnoreCase)
            || row.LotDisplay.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ValidityControlRow> Apply(
        IEnumerable<ValidityControlRow> rows,
        ValidityControlFilterKind filter,
        string? search = null,
        string? group = null,
        string? brand = null)
    {
        return Sort(rows.Where(r => MatchesFilter(r, filter) && MatchesSearch(r, search, group, brand)));
    }

    public static IReadOnlyList<ValidityControlRow> Sort(IEnumerable<ValidityControlRow> rows) =>
        rows
            .OrderBy(r => r.AttentionRank)
            .ThenBy(r => Rank(r.Status.Kind))
            .ThenByDescending(r => r.LotValue.HasValue)
            .ThenByDescending(r => r.LotValue ?? 0)
            .ThenByDescending(r => r.Quantity)
            .ThenBy(r => r.DaysRemaining ?? int.MaxValue)
            .ThenBy(r => r.ProductId)
            .ThenBy(r => r.LotId ?? int.MaxValue)
            .ThenBy(r => r.LotDisplay, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static ValidityControlCards CountCards(IEnumerable<ValidityControlRow> rows)
    {
        var cards = new ValidityControlCards();
        foreach (var row in rows)
        {
            switch (row.Status.Kind)
            {
                case ProductExpiryStatusKind.Expired: cards.Expired++; break;
                case ProductExpiryStatusKind.Today: cards.Today++; break;
                case ProductExpiryStatusKind.Within7: cards.Days7++; break;
                case ProductExpiryStatusKind.Within15: cards.Days15++; break;
                case ProductExpiryStatusKind.Within30: cards.Days30++; break;
                case ProductExpiryStatusKind.Within60: cards.Days60++; break;
                case ProductExpiryStatusKind.Within90: cards.Days90++; break;
                case ProductExpiryStatusKind.Ok: cards.Ok++; break;
                default: cards.Uninformed++; break;
            }
        }
        return cards;
    }

    public static IReadOnlyList<ValidityControlRow> BuildRows(
        IEnumerable<ValidityControlProductInput> products, DateTime? today = null)
    {
        var day = (today ?? DateTime.Today).Date;
        var rows = new List<ValidityControlRow>();
        foreach (var product in products)
        {
            var activeLots = product.Lots
                .Where(l => l.Quantity > 0.0001)
                .ToList();
            var lotsQty = activeLots.Sum(l => l.Quantity);

            foreach (var lot in activeLots)
                rows.Add(FromLot(lot, product, day));

            if (!product.ExplicitExpiryControl)
                continue;

            var leftover = Math.Round(product.Stock - lotsQty, 4);
            if (leftover <= StockLotConsistencyService.Tolerance)
                continue;

            if (activeLots.Count == 0)
                rows.Add(AlertRow(product, leftover, ValidityControlRowKind.MissingExpiry, MissingExpiryLabel));
            else
                rows.Add(AlertRow(product, leftover, ValidityControlRowKind.UntrackedStock, UntrackedStockLabel));
        }

        return Sort(rows);
    }

    public static ValidityControlSnapshot Snapshot(
        IEnumerable<ValidityControlProductInput> products, DateTime? today = null)
    {
        var rows = BuildRows(products, today);
        return new ValidityControlSnapshot
        {
            Rows = rows,
            Cards = CountCards(rows),
        };
    }

    public static ValidityControlRow FromLot(
        ProductLot lot, ValidityControlProductInput product, DateTime today)
    {
        var status = ProductExpiryService.Classify(lot.ExpiryDate, today);
        var uninformed = status.Kind == ProductExpiryStatusKind.Uninformed;
        var kind = uninformed ? ValidityControlRowKind.UninformedLot : ValidityControlRowKind.Lot;
        var cost = ResolveLotCost(lot.UnitCost, product.CostPrice);
        var lotValue = ComputeLotValue(lot.Quantity, cost.UsedCost);
        var action = ResolveSuggestedAction(kind, status.Kind, lot.Quantity);
        return new ValidityControlRow
        {
            ProductId = product.ProductId,
            LotId = lot.Id == 0 ? null : lot.Id,
            ProductName = product.Name,
            ProductCode = product.Code,
            GroupName = product.GroupName,
            BrandName = product.BrandName,
            LotDisplay = string.IsNullOrWhiteSpace(lot.LotNumber) ? "—" : lot.LotNumber.Trim(),
            Quantity = lot.Quantity,
            ExpiryDate = lot.ExpiryDate,
            DaysRemaining = status.Days,
            Status = status,
            StatusDisplay = status.Label,
            UnitCost = lot.UnitCost,
            OriginDisplay = lot.PurchaseId is int id && id > 0 ? $"Compra #{id}" : "—",
            RowKind = kind,
            Tone = ToneFor(status.Kind),
            StockFridge = product.StockFridge,
            UsedCost = cost.UsedCost,
            CostSource = cost.Source,
            LotValue = lotValue,
            SuggestedAction = action,
            AttentionRank = AttentionRankOf(action),
            SuggestedActionReason = FormatSuggestedActionReason(
                action, kind, status.Kind, lot.Quantity, status.Days, lotValue),
        };
    }

    public static string FormatHomeSummary(ValidityControlCards cards)
    {
        var parts = new List<string>();
        if (cards.Expired > 0)
            parts.Add($"{cards.Expired} vencido{(cards.Expired == 1 ? "" : "s")}");
        if (cards.Today > 0)
            parts.Add($"{cards.Today} hoje");
        if (cards.Days7 > 0)
            parts.Add($"{cards.Days7} até 7 dias");
        var until30 = cards.Days15 + cards.Days30;
        if (until30 > 0)
            parts.Add($"{until30} até 30 dias");
        if (parts.Count == 0)
            return "";
        return "Validades: " + string.Join(" • ", parts);
    }

    public static bool ShouldShowHomeAlert(ValidityControlCards cards) =>
        cards.Expired + cards.Today + cards.Days7 + cards.Days15 + cards.Days30 > 0;

    static int Rank(ProductExpiryStatusKind kind) =>
        kind switch
        {
            ProductExpiryStatusKind.Expired => 0,
            ProductExpiryStatusKind.Today => 1,
            ProductExpiryStatusKind.Within7 => 2,
            ProductExpiryStatusKind.Within15 => 3,
            ProductExpiryStatusKind.Within30 => 4,
            ProductExpiryStatusKind.Within60 => 5,
            ProductExpiryStatusKind.Within90 => 6,
            ProductExpiryStatusKind.Ok => 7,
            _ => 8,
        };

    static ValidityControlRow AlertRow(
        ValidityControlProductInput product,
        double qty,
        ValidityControlRowKind kind,
        string label)
    {
        var cost = ResolveLotCost(recordedUnitCost: 0, product.CostPrice);
        var lotValue = ComputeLotValue(qty, cost.UsedCost);
        var action = ResolveSuggestedAction(kind, ProductExpiryStatusKind.Uninformed, qty);
        return new()
        {
            ProductId = product.ProductId,
            ProductName = product.Name,
            ProductCode = product.Code,
            GroupName = product.GroupName,
            BrandName = product.BrandName,
            LotDisplay = "—",
            Quantity = qty,
            Status = ProductExpiryStatus.Uninformed,
            StatusDisplay = label,
            RowKind = kind,
            Tone = ToneFor(ProductExpiryStatusKind.Uninformed),
            StockFridge = product.StockFridge,
            UsedCost = cost.UsedCost,
            CostSource = cost.Source,
            LotValue = lotValue,
            SuggestedAction = action,
            AttentionRank = AttentionRankOf(action),
            SuggestedActionReason = FormatSuggestedActionReason(
                action, kind, ProductExpiryStatusKind.Uninformed, qty, daysRemaining: null, lotValue),
        };
    }
}
