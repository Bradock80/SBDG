using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motor puro 70D-B1: demanda/excesso SKU e FEFO de validade em memória.
/// Sem SQLite, UI, promoção, compra ou preço. Não altera VMV/cobertura 70C.
///
/// Dias civis: d = (expiry.Date − today.Date).Days. Sem +1 artificial.
/// 70I: vencido somente se expiry &lt; today. Hoje ainda é válido.
/// expiry == today → ExpiresToday: sem ProjectedSurplusAtExpiry (sem modelo intradiário).
/// </summary>
public static class InventoryProjectionEngine
{
    public const int MinHistoryDays = 30;

    /// <summary>Demanda projetada = VMV30 × dias. Não recalcula vendas. Não média 7/30/90.</summary>
    public static double? ProjectedDemand(double vmv30, int days)
    {
        if (days < 0)
            return null;
        if (!InventoryIntelligenceEngine.IsFinite(vmv30) || vmv30 < -InventoryIntelligenceEngine.Epsilon)
            return null;
        var vmv = Math.Max(0, vmv30);
        var demand = vmv * days;
        if (!InventoryIntelligenceEngine.IsFinite(demand) || demand < 0)
            return null;
        return demand;
    }

    /// <summary>
    /// max(0, totalStock − VMV30 × horizon) com travas SKU.
    /// CoverageBand.Normal não é excesso.
    /// </summary>
    public static double? ProjectedExcessQuantity(double totalStock, double vmv30, int horizonDays) =>
        Project(AllowedNumericRequest(totalStock, vmv30, horizonDays)).ProjectedExcessQuantity;

    public static InventoryProjectionResult Project(InventoryProjectionRequest request)
    {
        var today = request.Today.Date;
        var skuReason = EvaluateSkuBlockedReason(request);
        var expiryReason = EvaluateExpiryBlockedReason(request);
        var demand = ProjectedDemand(request.Vmv30, request.HorizonDays);

        var tracked = SumPositiveLots(request.Lots);
        var untracked = 0d;
        if (InventoryIntelligenceEngine.IsFinite(request.WarehouseStock)
            && request.WarehouseStock >= -InventoryIntelligenceEngine.Epsilon)
        {
            untracked = Math.Max(0, request.WarehouseStock - tracked);
        }

        var fridgeFlag = InventoryIntelligenceEngine.IsFinite(request.FridgeStock)
            && request.FridgeStock > InventoryIntelligenceEngine.Epsilon;

        var lots = ClassifyLots(request.Lots, today);
        if (expiryReason == InventoryExpiryProjectionBlockedReason.DuplicateLotId)
            lots = [];
        else if (expiryReason == InventoryExpiryProjectionBlockedReason.None)
            FillSurplus(lots, today, request.Vmv30);

        double? excess = null;
        if (skuReason == InventorySkuProjectionBlockedReason.None && demand is double d)
        {
            var stock = Math.Max(0, request.TotalStock);
            var raw = stock - d;
            if (InventoryIntelligenceEngine.IsFinite(raw))
                excess = Math.Max(0, raw);
        }

        return new InventoryProjectionResult
        {
            SkuBlockedReason = skuReason,
            ExpiryBlockedReason = expiryReason,
            HorizonDays = request.HorizonDays,
            ProjectedDemand = skuReason == InventorySkuProjectionBlockedReason.None ? demand : null,
            ProjectedExcessQuantity = excess,
            TrackedLotQuantity = tracked,
            UntrackedWarehouseQuantity = untracked,
            HasLotLocationLimitation = fridgeFlag,
            Lots = lots.Select(ToResult).ToList(),
        };
    }

    public static InventorySkuProjectionBlockedReason EvaluateSkuBlockedReason(InventoryProjectionRequest request)
    {
        if (!AreSkuScalarsFinite(request) || request.HorizonDays < 0 || request.HistoryDays < 0)
            return InventorySkuProjectionBlockedReason.InvalidInput;
        if (request.IsCompositionProduct)
            return InventorySkuProjectionBlockedReason.CompositionProduct;
        if (!request.HasPhysicalAvailabilityEvidence)
            return InventorySkuProjectionBlockedReason.NoPhysicalEvidence;
        if (request.HistoryDays < MinHistoryDays || request.IsHistoryInsufficient30)
            return InventorySkuProjectionBlockedReason.InsufficientHistory;
        if (request.WarehouseStock < -InventoryIntelligenceEngine.Epsilon
            || request.FridgeStock < -InventoryIntelligenceEngine.Epsilon)
            return InventorySkuProjectionBlockedReason.NegativeLocationStock;
        if (Math.Abs(request.TotalStock - (request.WarehouseStock + request.FridgeStock))
            > InventoryIntelligenceEngine.Epsilon)
            return InventorySkuProjectionBlockedReason.InconsistentStockTotals;
        if (request.TotalStock < -InventoryIntelligenceEngine.Epsilon)
            return InventorySkuProjectionBlockedReason.NegativeStock;
        if (request.Vmv30 <= InventoryIntelligenceEngine.Epsilon)
            return InventorySkuProjectionBlockedReason.NoObservableDemand;
        return InventorySkuProjectionBlockedReason.None;
    }

    public static InventoryExpiryProjectionBlockedReason EvaluateExpiryBlockedReason(InventoryProjectionRequest request)
    {
        if (!AreExpiryScalarsFinite(request) || request.HistoryDays < 0)
            return InventoryExpiryProjectionBlockedReason.InvalidInput;
        if (request.IsCompositionProduct)
            return InventoryExpiryProjectionBlockedReason.CompositionProduct;
        if (!request.HasPhysicalAvailabilityEvidence)
            return InventoryExpiryProjectionBlockedReason.NoPhysicalEvidence;
        if (request.HistoryDays < MinHistoryDays || request.IsHistoryInsufficient30)
            return InventoryExpiryProjectionBlockedReason.InsufficientHistory;
        if (request.WarehouseStock < -InventoryIntelligenceEngine.Epsilon)
            return InventoryExpiryProjectionBlockedReason.NegativeWarehouseStock;
        if (request.FridgeStock < -InventoryIntelligenceEngine.Epsilon)
            return InventoryExpiryProjectionBlockedReason.NegativeLocationStock;
        if (InventoryIntelligenceEngine.IsFinite(request.TotalStock)
            && Math.Abs(request.TotalStock - (request.WarehouseStock + request.FridgeStock))
                > InventoryIntelligenceEngine.Epsilon)
            return InventoryExpiryProjectionBlockedReason.InconsistentStockTotals;
        if (request.Vmv30 <= InventoryIntelligenceEngine.Epsilon)
            return InventoryExpiryProjectionBlockedReason.NoObservableDemand;
        if (HasDuplicateLotId(request.Lots))
            return InventoryExpiryProjectionBlockedReason.DuplicateLotId;
        if (HasInvalidLotQuantity(request.Lots))
            return InventoryExpiryProjectionBlockedReason.InvalidLotQuantity;
        var tracked = SumPositiveLots(request.Lots);
        if (InventoryIntelligenceEngine.IsFinite(request.WarehouseStock)
            && tracked > request.WarehouseStock + InventoryIntelligenceEngine.Epsilon)
            return InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse;
        return InventoryExpiryProjectionBlockedReason.None;
    }

    static InventoryProjectionRequest AllowedNumericRequest(double totalStock, double vmv30, int horizonDays) =>
        new()
        {
            Today = DateTime.Today,
            Vmv30 = vmv30,
            HistoryDays = MinHistoryDays,
            IsHistoryInsufficient30 = false,
            HasPhysicalAvailabilityEvidence = true,
            IsCompositionProduct = false,
            TotalStock = totalStock,
            WarehouseStock = totalStock,
            FridgeStock = 0,
            HorizonDays = horizonDays,
            Lots = [],
        };

    sealed class WorkingLot
    {
        public int LotId;
        public InventoryProjectionLotKind Kind;
        public double Quantity;
        public DateTime? ExpiryDate;
        public int? DaysUntilExpiry;
        public bool AlreadyExpired;
        public double Remaining;
        public double? UnitCost;
        public double? ProjectedSurplusAtExpiry;
        public double? ProjectedSurplusValue;
    }

    static InventoryProjectionLotResult ToResult(WorkingLot lot) =>
        new()
        {
            LotId = lot.LotId,
            Kind = lot.Kind,
            Quantity = lot.Quantity,
            ExpiryDate = lot.ExpiryDate,
            DaysUntilExpiry = lot.DaysUntilExpiry,
            AlreadyExpired = lot.AlreadyExpired,
            ProjectedSurplusAtExpiry = lot.ProjectedSurplusAtExpiry,
            ProjectedSurplusValue = lot.ProjectedSurplusValue,
        };

    static bool AreSkuScalarsFinite(InventoryProjectionRequest request) =>
        InventoryIntelligenceEngine.IsFinite(request.Vmv30)
        && InventoryIntelligenceEngine.IsFinite(request.TotalStock)
        && InventoryIntelligenceEngine.IsFinite(request.WarehouseStock)
        && InventoryIntelligenceEngine.IsFinite(request.FridgeStock);

    static bool AreExpiryScalarsFinite(InventoryProjectionRequest request) =>
        InventoryIntelligenceEngine.IsFinite(request.Vmv30)
        && InventoryIntelligenceEngine.IsFinite(request.WarehouseStock)
        && InventoryIntelligenceEngine.IsFinite(request.FridgeStock);

    static bool HasDuplicateLotId(IReadOnlyList<InventoryProjectionLotInput> lots)
    {
        var seen = new HashSet<int>();
        foreach (var lot in lots)
        {
            if (!seen.Add(lot.LotId))
                return true;
        }
        return false;
    }

    static bool HasInvalidLotQuantity(IReadOnlyList<InventoryProjectionLotInput> lots)
    {
        foreach (var lot in lots)
        {
            if (!InventoryIntelligenceEngine.IsFinite(lot.Quantity)
                || lot.Quantity < -InventoryIntelligenceEngine.Epsilon)
                return true;
        }
        return false;
    }

    static double SumPositiveLots(IReadOnlyList<InventoryProjectionLotInput> lots)
    {
        double sum = 0;
        foreach (var lot in lots)
        {
            if (!InventoryIntelligenceEngine.IsFinite(lot.Quantity)
                || lot.Quantity <= InventoryIntelligenceEngine.Epsilon)
                continue;
            sum += lot.Quantity;
        }
        return sum;
    }

    static List<WorkingLot> ClassifyLots(IReadOnlyList<InventoryProjectionLotInput> lots, DateTime today)
    {
        var ordered = lots
            .Select((lot, index) => (lot, index))
            .OrderBy(x => x.lot.ExpiryDate is null ? 1 : 0)
            .ThenBy(x => x.lot.ExpiryDate?.Date ?? DateTime.MaxValue)
            .ThenBy(x => x.lot.LotId)
            .ThenBy(x => x.index)
            .ToList();

        var results = new List<WorkingLot>(ordered.Count);
        foreach (var (lot, _) in ordered)
        {
            var qty = InventoryIntelligenceEngine.IsFinite(lot.Quantity)
                ? Math.Max(0, lot.Quantity)
                : 0;
            var expiry = lot.ExpiryDate?.Date;
            var work = new WorkingLot
            {
                LotId = lot.LotId,
                Quantity = qty,
                Remaining = qty,
                ExpiryDate = expiry,
                UnitCost = lot.UnitCost,
            };

            if (expiry is null)
            {
                work.Kind = InventoryProjectionLotKind.Undated;
                work.Remaining = 0;
            }
            else if (expiry.Value < today)
            {
                work.Kind = InventoryProjectionLotKind.AlreadyExpired;
                work.AlreadyExpired = true;
                work.Remaining = 0;
            }
            else if (expiry.Value == today)
            {
                work.Kind = InventoryProjectionLotKind.ExpiresToday;
                work.DaysUntilExpiry = 0;
                work.Remaining = 0;
            }
            else
            {
                work.Kind = InventoryProjectionLotKind.Dated;
                work.DaysUntilExpiry = (expiry.Value - today).Days;
            }

            results.Add(work);
        }

        return results;
    }

    static void FillSurplus(List<WorkingLot> lots, DateTime today, double vmv30)
    {
        var dated = lots
            .Where(l => l.Kind == InventoryProjectionLotKind.Dated
                && l.Quantity > InventoryIntelligenceEngine.Epsilon
                && l.ExpiryDate is not null)
            .OrderBy(l => l.ExpiryDate!.Value)
            .ThenBy(l => l.LotId)
            .ToList();

        var previous = today;
        foreach (var lot in dated)
        {
            var expiry = lot.ExpiryDate!.Value.Date;
            var intervalDays = Math.Max(0, (expiry - previous).Days);
            var demand = ProjectedDemand(vmv30, intervalDays) ?? 0;
            ConsumeDatedFefo(lots, demand);
            var leftover = lot.Remaining;
            if (!InventoryIntelligenceEngine.IsFinite(leftover) || leftover < 0)
                leftover = 0;
            lot.ProjectedSurplusAtExpiry = leftover;
            lot.ProjectedSurplusValue = SurplusValue(leftover, lot.UnitCost);
            lot.Remaining = 0;
            previous = expiry;
        }
    }

    static void ConsumeDatedFefo(List<WorkingLot> lots, double demand)
    {
        if (demand <= InventoryIntelligenceEngine.Epsilon)
            return;
        var order = lots
            .Where(l => l.Kind == InventoryProjectionLotKind.Dated
                && l.Remaining > InventoryIntelligenceEngine.Epsilon)
            .OrderBy(l => l.ExpiryDate)
            .ThenBy(l => l.LotId)
            .ToList();

        var left = demand;
        foreach (var lot in order)
        {
            if (left <= InventoryIntelligenceEngine.Epsilon)
                break;
            var take = Math.Min(lot.Remaining, left);
            lot.Remaining = Math.Max(0, lot.Remaining - take);
            left -= take;
        }
    }

    static double? SurplusValue(double surplus, double? unitCost)
    {
        if (unitCost is not double cost
            || !InventoryIntelligenceEngine.IsFinite(cost)
            || cost <= ValidityControlEngine.CostAvailableThreshold)
            return null;
        var value = surplus * cost;
        if (!InventoryIntelligenceEngine.IsFinite(value) || value < 0)
            return null;
        return value;
    }
}
