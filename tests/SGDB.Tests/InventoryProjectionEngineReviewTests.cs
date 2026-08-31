using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 70D-B1-R2 — invariantes da regra corrigida. Sem SQLite.
/// Não preserva comportamento antigo de cap silencioso nem sobra em expiry=today.
/// </summary>
public class InventoryProjectionEngineReviewTests
{
    private static readonly DateTime Today = new(2026, 8, 30);

    private static InventoryProjectionRequest Allowed(
        double totalStock,
        double vmv30,
        double? warehouse = null,
        double fridge = 0,
        params InventoryProjectionLotInput[] lots) =>
        new()
        {
            Today = Today,
            Vmv30 = vmv30,
            HistoryDays = 30,
            HasPhysicalAvailabilityEvidence = true,
            TotalStock = totalStock,
            WarehouseStock = warehouse ?? totalStock,
            FridgeStock = fridge,
            HorizonDays = 30,
            Lots = lots,
        };

    private static InventoryProjectionLotInput Lot(int id, double qty, int? days, double? cost = null) =>
        new()
        {
            LotId = id,
            Quantity = qty,
            ExpiryDate = days is int d ? Today.AddDays(d) : null,
            UnitCost = cost,
        };

    [Fact]
    public void Sku_excess_survives_lot_inconsistency()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            50, 1, warehouse: 50, lots: [Lot(1, 40, 1), Lot(2, 40, 90)]));
        Assert.True(result.CanProjectSku);
        Assert.False(result.CanProjectExpiry);
        Assert.Equal(
            InventoryExpiryProjectionBlockedReason.TrackedQuantityExceedsWarehouse,
            result.ExpiryBlockedReason);
        Assert.NotNull(result.ProjectedExcessQuantity);
    }

    [Fact]
    public void Duplicate_id_does_not_block_sku()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            40, 1, lots: [Lot(7, 10, 10), Lot(7, 10, 20)]));
        Assert.True(result.CanProjectSku);
        Assert.False(result.CanProjectExpiry);
        Assert.Equal(InventoryExpiryProjectionBlockedReason.DuplicateLotId, result.ExpiryBlockedReason);
    }

    [Fact]
    public void Invalid_expiry_text_does_not_block_sku()
    {
        var lot = new InventoryProjectionLotInput
        {
            LotId = 1,
            Quantity = 40,
            HasInvalidExpiryText = true,
        };
        var result = InventoryProjectionEngine.Project(Allowed(50, 1, lots: lot));
        Assert.True(result.CanProjectSku);
        Assert.False(result.CanProjectExpiry);
        Assert.Equal(
            InventoryExpiryProjectionBlockedReason.InvalidExpiryDate,
            result.ExpiryBlockedReason);
        Assert.Empty(result.Lots);
    }

    [Fact]
    public void Invalid_cost_never_blocks_quantity()
    {
        var result = InventoryProjectionEngine.Project(Allowed(
            50, 1, lots: Lot(1, 50, 10, double.NaN)));
        Assert.True(result.CanProjectSku);
        Assert.True(result.CanProjectExpiry);
        Assert.Equal(40, result.Lots.Single().ProjectedSurplusAtExpiry!.Value, 4);
        Assert.Null(result.Lots.Single().ProjectedSurplusValue);
    }

    [Fact]
    public void Untracked_does_not_reduce_dated_surplus()
    {
        var withGap = InventoryProjectionEngine.Project(Allowed(
            100, 2, warehouse: 100, lots: Lot(1, 60, 10)));
        Assert.Equal(40, withGap.Lots.Single().ProjectedSurplusAtExpiry!.Value, 4);
        Assert.Equal(40, withGap.UntrackedWarehouseQuantity, 4);
    }
}
