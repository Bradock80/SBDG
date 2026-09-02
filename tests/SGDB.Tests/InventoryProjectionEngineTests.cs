using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 70D-B1-R2 — motor puro de projeção (SKU e validade separados).
/// Sem SQLite. Sem deposito.db. Sem UI.
///
/// Dias civis: d = (expiry.Date − today.Date).Days. Sem +1.
/// 70I: vencido só se expiry &lt; today. expiry == today = ExpiresToday, sem sobra numérica.
/// </summary>
public class InventoryProjectionEngineTests
{
    private static readonly DateTime Today = new(2026, 8, 30);

    private static InventoryProjectionRequest Allowed(
        double totalStock,
        double vmv30,
        int horizonDays = 30,
        double? warehouse = null,
        double fridge = 0,
        int historyDays = 30,
        bool evidence = true,
        bool composition = false,
        bool insufficient30 = false,
        params InventoryProjectionLotInput[] lots) =>
        new()
        {
            Today = Today,
            Vmv30 = vmv30,
            HistoryDays = historyDays,
            IsHistoryInsufficient30 = insufficient30,
            HasPhysicalAvailabilityEvidence = evidence,
            IsCompositionProduct = composition,
            TotalStock = totalStock,
            WarehouseStock = warehouse ?? totalStock,
            FridgeStock = fridge,
            HorizonDays = horizonDays,
            Lots = lots,
        };

    private static InventoryProjectionLotInput Lot(
        int id, double qty, int? daysUntilExpiry, double? cost = null) =>
        new()
        {
            LotId = id,
            Quantity = qty,
            ExpiryDate = daysUntilExpiry is int d ? Today.AddDays(d) : null,
            UnitCost = cost,
        };

    private static InventoryProjectionLotResult RequireLot(InventoryProjectionResult result, int id) =>
        result.Lots.Single(l => l.LotId == id);

    [Fact]
    public void A_stock_100_vmv_10_horizon_30_excess_is_zero()
    {
        var result = InventoryProjectionEngine.Project(Allowed(100, 10, 30));
        Assert.True(result.CanProjectSku);
        Assert.Equal(300, result.ProjectedDemand!.Value, 4);
        Assert.Equal(0, result.ProjectedExcessQuantity!.Value, 4);
    }

    [Fact]
    public void B_stock_100_vmv_1_horizon_30_excess_is_70()
    {
        var result = InventoryProjectionEngine.Project(Allowed(100, 1, 30));
        Assert.Equal(70, result.ProjectedExcessQuantity!.Value, 4);
    }

    [Theory]
    [InlineData(30, 70)]
    [InlineData(60, 40)]
    [InlineData(90, 10)]
    public void Excess_horizons_30_60_90(int horizon, double expectedExcess)
    {
        var result = InventoryProjectionEngine.Project(Allowed(100, 1, horizon));
        Assert.True(result.CanProjectSku);
        Assert.Equal(expectedExcess, result.ProjectedExcessQuantity!.Value, 4);
    }

    [Fact]
    public void Helper_ProjectedExcessQuantity_matches_max_zero_stock_minus_demand()
    {
        Assert.Equal(0, InventoryProjectionEngine.ProjectedExcessQuantity(100, 10, 30)!.Value, 4);
        Assert.Equal(70, InventoryProjectionEngine.ProjectedExcessQuantity(100, 1, 30)!.Value, 4);
        Assert.Equal(40, InventoryProjectionEngine.ProjectedExcessQuantity(100, 1, 60)!.Value, 4);
        Assert.Equal(10, InventoryProjectionEngine.ProjectedExcessQuantity(100, 1, 90)!.Value, 4);
    }

    [Fact]
    public void Normal_coverage_is_not_automatic_excess()
    {
        var result = InventoryProjectionEngine.Project(Allowed(20, 1, 30));
        Assert.Equal(0, result.ProjectedExcessQuantity!.Value, 4);
    }

    [Fact]
    public void C_lot_50_expires_in_10_days_vmv_1_surplus_40()
    {
        var result = InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, 10)));
        Assert.True(result.CanProjectExpiry);
        var lot = RequireLot(result, 1);
        Assert.Equal(10, lot.DaysUntilExpiry);
        Assert.Equal(InventoryProjectionLotKind.Dated, lot.Kind);
        Assert.Equal(40, lot.ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void D_lot_50_expires_in_10_days_vmv_10_surplus_0()
    {
        Assert.Equal(0, RequireLot(
            InventoryProjectionEngine.Project(Allowed(50, 10, 30, lots: Lot(1, 50, 10))), 1)
            .ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void E_two_lots_fefo_first_surplus_10_second_0()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            80, 2, 30, lots: [Lot(1, 30, 10), Lot(2, 50, 40)]));
        Assert.Equal(10, RequireLot(result, 1).ProjectedSurplusAtExpiry!.Value, 4);
        Assert.Equal(0, RequireLot(result, 2).ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void F_vmv_zero_blocks_sku_and_expiry()
    {
        var result = InventoryProjectionEngine.Project(Allowed(100, 0, 30));
        Assert.Equal(InventorySkuProjectionBlockedReason.NoObservableDemand, result.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.NoObservableDemand, result.ExpiryBlockedReason);
        Assert.Null(result.ProjectedExcessQuantity);
        Assert.Null(result.ProjectedDemand);
    }

    [Fact]
    public void G_history_29_blocked_30_allowed()
    {
        var blocked = InventoryProjectionEngine.Project(Allowed(100, 1, 30, historyDays: 29));
        Assert.Equal(InventorySkuProjectionBlockedReason.InsufficientHistory, blocked.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.InsufficientHistory, blocked.ExpiryBlockedReason);

        var allowed = InventoryProjectionEngine.Project(Allowed(100, 1, 30, historyDays: 30));
        Assert.True(allowed.CanProjectSku);
        Assert.Equal(70, allowed.ProjectedExcessQuantity!.Value, 4);
    }

    [Fact]
    public void H_no_physical_evidence_blocks_sku_and_expiry()
    {
        var result = InventoryProjectionEngine.Project(Allowed(100, 1, 30, evidence: false));
        Assert.Equal(InventorySkuProjectionBlockedReason.NoPhysicalEvidence, result.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.NoPhysicalEvidence, result.ExpiryBlockedReason);
    }

    [Fact]
    public void I_composition_blocks_sku_and_expiry()
    {
        var result = InventoryProjectionEngine.Project(Allowed(100, 1, 30, composition: true));
        Assert.Equal(InventorySkuProjectionBlockedReason.CompositionProduct, result.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.CompositionProduct, result.ExpiryBlockedReason);
    }

    [Fact]
    public void J_negative_total_with_negative_warehouse_is_location_block()
    {
        var result = InventoryProjectionEngine.Project(Allowed(-5, 1, 30));
        Assert.Equal(InventorySkuProjectionBlockedReason.NegativeLocationStock, result.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.NegativeWarehouseStock, result.ExpiryBlockedReason);
    }

    [Fact]
    public void K_undated_lot_has_no_surplus_at_expiry()
    {
        var lot = RequireLot(
            InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, null))), 1);
        Assert.Equal(InventoryProjectionLotKind.Undated, lot.Kind);
        Assert.Null(lot.ProjectedSurplusAtExpiry);
    }

    [Fact]
    public void L_expired_lot_is_already_expired_not_projection()
    {
        var lot = RequireLot(
            InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, -1))), 1);
        Assert.True(lot.AlreadyExpired);
        Assert.Equal(InventoryProjectionLotKind.AlreadyExpired, lot.Kind);
        Assert.Null(lot.ProjectedSurplusAtExpiry);
    }

    [Fact]
    public void Lots_over_warehouse_blocks_expiry_sku_still_calculable()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            50, 1, 30, warehouse: 50,
            lots: [Lot(1, 40, 1), Lot(2, 40, 90)]));
        Assert.True(result.CanProjectSku);
        Assert.Equal(20, result.ProjectedExcessQuantity!.Value, 4);
        Assert.Equal(
            InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse,
            result.ExpiryBlockedReason);
        Assert.Null(RequireLot(result, 1).ProjectedSurplusAtExpiry);
        Assert.Null(RequireLot(result, 2).ProjectedSurplusAtExpiry);
    }

    [Fact]
    public void N_lots_below_warehouse_untracked_and_expiry_calculable()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            100, 1, 30, warehouse: 100, lots: Lot(1, 30, 10)));
        Assert.True(result.CanProjectExpiry);
        Assert.Equal(30, result.TrackedLotQuantity, 4);
        Assert.Equal(70, result.UntrackedWarehouseQuantity, 4);
        Assert.Equal(20, RequireLot(result, 1).ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void O_fridge_sets_location_limitation_fact()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            totalStock: 80, vmv30: 1, warehouse: 50, fridge: 30, lots: Lot(1, 50, 10)));
        Assert.True(result.HasLotLocationLimitation);
        Assert.True(result.CanProjectSku);
        Assert.True(result.CanProjectExpiry);
        Assert.Equal(50, result.ProjectedExcessQuantity!.Value, 4);
        Assert.Equal(40, RequireLot(result, 1).ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void O_zero_fridge_does_not_set_limitation_flag()
    {
        var result = InventoryProjectionEngine.Project(Allowed(50, 1, 30, fridge: 0, lots: Lot(1, 50, 10)));
        Assert.False(result.HasLotLocationLimitation);
    }

    [Fact]
    public void P_known_cost_surplus_value()
    {
        var lot = RequireLot(
            InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, 10, cost: 2.5))), 1);
        Assert.Equal(100, lot.ProjectedSurplusValue!.Value, 4);
    }

    [Fact]
    public void Q_missing_zero_threshold_negative_nan_inf_cost_null_value_does_not_block()
    {
        foreach (double? cost in new double?[] { null, 0, 0.009, -1, double.NaN, double.PositiveInfinity })
        {
            var result = InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, 10, cost)));
            Assert.True(result.CanProjectSku);
            Assert.True(result.CanProjectExpiry);
            Assert.Equal(40, RequireLot(result, 1).ProjectedSurplusAtExpiry!.Value, 4);
            Assert.Null(RequireLot(result, 1).ProjectedSurplusValue);
        }
    }

    [Fact]
    public void R_nan_infinity_sku_invalid_without_lots()
    {
        Assert.Equal(
            InventorySkuProjectionBlockedReason.InvalidInput,
            InventoryProjectionEngine.Project(Allowed(100, double.NaN)).SkuBlockedReason);
        Assert.Equal(
            InventorySkuProjectionBlockedReason.InvalidInput,
            InventoryProjectionEngine.Project(Allowed(double.PositiveInfinity, 1)).SkuBlockedReason);
        Assert.Null(InventoryProjectionEngine.ProjectedDemand(double.NaN, 30));
        Assert.Null(InventoryProjectionEngine.ProjectedDemand(1, -1));
        Assert.Null(InventoryProjectionEngine.ProjectedExcessQuantity(double.NaN, 1, 30));
    }

    [Fact]
    public void Expiry_today_is_valid_without_numeric_surplus()
    {
        var lot = RequireLot(
            InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, 0))), 1);
        Assert.Equal(InventoryProjectionLotKind.ExpiresToday, lot.Kind);
        Assert.False(lot.AlreadyExpired);
        Assert.Equal(0, lot.DaysUntilExpiry);
        Assert.Null(lot.ProjectedSurplusAtExpiry);
        Assert.Null(lot.ProjectedSurplusValue);
    }

    [Fact]
    public void Expiry_tomorrow_is_one_civil_day()
    {
        var lot = RequireLot(
            InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, 1))), 1);
        Assert.Equal(InventoryProjectionLotKind.Dated, lot.Kind);
        Assert.Equal(1, lot.DaysUntilExpiry);
        Assert.Equal(49, lot.ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void Civil_expiry_yesterday_already_expired()
    {
        var lot = RequireLot(
            InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, -1))), 1);
        Assert.True(lot.AlreadyExpired);
        Assert.Null(lot.ProjectedSurplusAtExpiry);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 99)]
    public void Horizon_zero_and_one(int horizon, double expectedExcess)
    {
        var result = InventoryProjectionEngine.Project(Allowed(100, 1, horizon));
        Assert.Equal(expectedExcess, result.ProjectedExcessQuantity!.Value, 4);
    }

    [Fact]
    public void Vmv_at_epsilon_is_no_observable_demand()
    {
        var result = InventoryProjectionEngine.Project(Allowed(100, InventoryIntelligenceEngine.Epsilon, 30));
        Assert.Equal(InventorySkuProjectionBlockedReason.NoObservableDemand, result.SkuBlockedReason);
    }

    [Fact]
    public void Vmv_just_above_epsilon_projects()
    {
        var result = InventoryProjectionEngine.Project(
            Allowed(100, InventoryIntelligenceEngine.Epsilon + 0.0001, 30));
        Assert.True(result.CanProjectSku);
    }

    [Fact]
    public void Warehouse_negative_fridge_positive_blocks_sku_and_expiry()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            totalStock: 5, vmv30: 1, warehouse: -5, fridge: 10, lots: Lot(1, 10, 10)));
        Assert.Equal(InventorySkuProjectionBlockedReason.NegativeLocationStock, result.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.NegativeWarehouseStock, result.ExpiryBlockedReason);
        Assert.Null(result.ProjectedExcessQuantity);
        Assert.Null(RequireLot(result, 1).ProjectedSurplusAtExpiry);
    }

    [Fact]
    public void Incoherent_total_blocks_sku()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            totalStock: 100, vmv30: 1, warehouse: 10, fridge: 5));
        Assert.Equal(InventorySkuProjectionBlockedReason.InconsistentStockTotals, result.SkuBlockedReason);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.InconsistentStockTotals, result.ExpiryBlockedReason);
        Assert.Null(result.ProjectedExcessQuantity);
    }

    [Fact]
    public void Duplicate_lot_id_blocks_expiry_sku_independent()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            40, 1, 30, lots: [Lot(7, 20, 10), Lot(7, 20, 10)]));
        Assert.True(result.CanProjectSku);
        Assert.Equal(10, result.ProjectedExcessQuantity!.Value, 4);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.DuplicateLotId, result.ExpiryBlockedReason);
        Assert.Empty(result.Lots);
    }

    [Fact]
    public void Negative_lot_quantity_blocks_expiry_not_sku()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            50, 1, 30, lots: [Lot(1, -10, 10), Lot(2, 50, 10)]));
        Assert.True(result.CanProjectSku);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.InvalidLotQuantity, result.ExpiryBlockedReason);
        Assert.Null(RequireLot(result, 2).ProjectedSurplusAtExpiry);
    }

    [Fact]
    public void Nan_inf_lot_quantity_blocks_expiry_not_sku()
    {
        var nan = InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, double.NaN, 10)));
        Assert.True(nan.CanProjectSku);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.InvalidLotQuantity, nan.ExpiryBlockedReason);

        var inf = InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, double.PositiveInfinity, 10)));
        Assert.True(inf.CanProjectSku);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.InvalidLotQuantity, inf.ExpiryBlockedReason);
    }

    [Fact]
    public void Invalid_expiry_text_blocks_expiry_not_sku_and_does_not_emit_undated()
    {
        var lot = new InventoryProjectionLotInput
        {
            LotId = 1,
            Quantity = 40,
            ExpiryDate = null,
            HasInvalidExpiryText = true,
            UnitCost = 5,
        };
        var result = InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: lot));
        Assert.True(result.CanProjectSku);
        Assert.Equal(20, result.ProjectedExcessQuantity!.Value, 4);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.InvalidExpiryDate, result.ExpiryBlockedReason);
        Assert.Empty(result.Lots);
    }

    [Fact]
    public void Same_expiry_demand_consumed_once()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            40, 3, 30, lots: [Lot(1, 20, 10), Lot(2, 20, 10)]));
        Assert.Equal(0, RequireLot(result, 1).ProjectedSurplusAtExpiry!.Value, 4);
        Assert.Equal(10, RequireLot(result, 2).ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void Three_lots_demand_not_reused()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            40, 1, 30, lots: [Lot(1, 10, 5), Lot(2, 10, 10), Lot(3, 20, 20)]));
        Assert.Equal(5, RequireLot(result, 1).ProjectedSurplusAtExpiry!.Value, 4);
        Assert.Equal(5, RequireLot(result, 2).ProjectedSurplusAtExpiry!.Value, 4);
        Assert.Equal(10, RequireLot(result, 3).ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void Input_order_does_not_change_result()
    {
        var a = Lot(1, 30, 10);
        var b = Lot(2, 50, 40);
        var forward = InventoryProjectionEngine.Project(Allowed(80, 2, 30, lots: [a, b]));
        var reverse = InventoryProjectionEngine.Project(Allowed(80, 2, 30, lots: [b, a]));
        Assert.Equal(
            RequireLot(forward, 1).ProjectedSurplusAtExpiry,
            RequireLot(reverse, 1).ProjectedSurplusAtExpiry);
        Assert.Equal(
            RequireLot(forward, 2).ProjectedSurplusAtExpiry,
            RequireLot(reverse, 2).ProjectedSurplusAtExpiry);
    }

    [Fact]
    public void Higher_vmv_does_not_increase_lot_surplus()
    {
        var low = InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, 10)));
        var high = InventoryProjectionEngine.Project(Allowed(50, 10, 30, lots: Lot(1, 50, 10)));
        Assert.True(high.Lots.Single().ProjectedSurplusAtExpiry
            <= low.Lots.Single().ProjectedSurplusAtExpiry + 0.0001);
    }

    [Fact]
    public void Outputs_never_nan_or_infinity_when_projectable()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            80, 2, 30, lots: [Lot(1, 30, 10), Lot(2, 50, 40)]));
        Assert.True(InventoryIntelligenceEngine.IsFinite(result.ProjectedDemand!.Value));
        Assert.True(result.ProjectedDemand >= 0);
        Assert.True(result.ProjectedExcessQuantity >= 0);
        foreach (var lot in result.Lots)
        {
            if (lot.ProjectedSurplusAtExpiry is double s)
            {
                Assert.True(InventoryIntelligenceEngine.IsFinite(s));
                Assert.True(s >= 0);
                Assert.True(s <= lot.Quantity + 0.0001);
            }
            if (lot.ProjectedSurplusValue is double v)
            {
                Assert.True(InventoryIntelligenceEngine.IsFinite(v));
                Assert.True(v >= 0);
            }
        }
    }

    [Fact]
    public void Undated_does_not_steal_demand_from_dated_lots()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            80, 2, 30, lots: [Lot(1, 30, 10), Lot(9, 50, null)]));
        Assert.Equal(10, RequireLot(result, 1).ProjectedSurplusAtExpiry!.Value, 4);
        Assert.Null(RequireLot(result, 9).ProjectedSurplusAtExpiry);
    }

    [Fact]
    public void Expires_today_does_not_consume_later_interval()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            80, 1, 30, lots: [Lot(1, 20, 0), Lot(2, 50, 10)]));
        Assert.Equal(InventoryProjectionLotKind.ExpiresToday, RequireLot(result, 1).Kind);
        Assert.Null(RequireLot(result, 1).ProjectedSurplusAtExpiry);
        Assert.Equal(40, RequireLot(result, 2).ProjectedSurplusAtExpiry!.Value, 4);
    }

    [Fact]
    public void Cost_just_above_threshold_yields_value()
    {
        var lot = RequireLot(
            InventoryProjectionEngine.Project(Allowed(50, 1, 30, lots: Lot(1, 50, 10, 0.0090001))), 1);
        Assert.NotNull(lot.ProjectedSurplusValue);
    }
}
