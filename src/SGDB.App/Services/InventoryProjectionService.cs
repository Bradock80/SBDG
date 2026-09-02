using Microsoft.Data.Sqlite;
using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Camada 70D-B2: compõe o snapshot 70C com uma query de lotes e o motor puro B1.
/// Não recalcula VMV. Sem UI, schema, RPC ou N+1.
/// </summary>
public static class InventoryProjectionService
{
    public const int DefaultHorizonDays = 30;
    public const int ExpectedLotsQueryCount = 1;
    public const int ExpectedQueryCount =
        InventoryIntelligenceService.ExpectedQueryCount + ExpectedLotsQueryCount;

    public static InventoryProjectionSnapshot Load(
        DateTime? today = null,
        int horizonDays = DefaultHorizonDays)
    {
        var intelligence = InventoryIntelligenceService.Load(today);
        var lotsByProduct = LoadLotsForActiveProducts();
        var day = intelligence.Today.Date;

        var map = new Dictionary<int, InventoryProjectedProduct>(intelligence.Rows.Count);
        foreach (var row in intelligence.Rows)
        {
            lotsByProduct.TryGetValue(row.ProductId, out var lots);
            lots ??= [];
            map[row.ProductId] = ProjectRow(row, lots, day, horizonDays);
        }

        return new InventoryProjectionSnapshot
        {
            Today = day,
            QueryCount = intelligence.QueryCount + ExpectedLotsQueryCount,
            Intelligence = intelligence,
            ByProductId = map.AsReadOnly(),
        };
    }

    static InventoryProjectedProduct ProjectRow(
        ProductTurnoverRow row,
        List<LoadedLot> lots,
        DateTime today,
        int horizonDays)
    {
        var inputs = new List<InventoryProjectionLotInput>(lots.Count);
        var costs = new List<InventoryProjectedLotCost>(lots.Count);
        var identities = new List<InventoryProjectedLotIdentity>(lots.Count);

        foreach (var lot in lots)
        {
            var parsed = InventoryProjectionLotParser.ParseExpiry(lot.ExpiryRaw);
            var cost = ValidityControlEngine.ResolveLotCost(lot.UnitCost, lot.ProductCostPrice);
            inputs.Add(new InventoryProjectionLotInput
            {
                LotId = lot.Id,
                Quantity = lot.Quantity,
                ExpiryDate = parsed.Kind == InventoryProjectionLotParser.ExpiryKind.ValidIso
                    ? parsed.Date
                    : null,
                HasInvalidExpiryText = parsed.Kind == InventoryProjectionLotParser.ExpiryKind.Invalid,
                UnitCost = cost.UsedCost,
            });
            costs.Add(new InventoryProjectedLotCost
            {
                LotId = lot.Id,
                UsedCost = cost.UsedCost,
                CostSource = cost.Source,
            });
            identities.Add(new InventoryProjectedLotIdentity
            {
                LotId = lot.Id,
                LotNumber = lot.LotNumber,
            });
        }

        var request = new InventoryProjectionRequest
        {
            Today = today,
            Vmv30 = row.Vmv30,
            HistoryDays = row.HistoryDays,
            IsHistoryInsufficient30 = row.IsHistoryInsufficient30,
            HasPhysicalAvailabilityEvidence = row.HasPhysicalAvailabilityEvidence,
            IsCompositionProduct = row.IsCompositionProduct,
            TotalStock = row.TotalStock,
            WarehouseStock = row.Stock,
            FridgeStock = row.StockFridge,
            HorizonDays = horizonDays,
            Lots = inputs,
        };

        return new InventoryProjectedProduct
        {
            ProductId = row.ProductId,
            Projection = InventoryProjectionEngine.Project(request),
            LotCosts = costs,
            LotIdentities = identities,
        };
    }

    static Dictionary<int, List<LoadedLot>> LoadLotsForActiveProducts()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                l.id,
                l.product_id,
                l.lot_number,
                l.quantity,
                l.expiry_date,
                l.unit_cost,
                IFNULL(p.cost_price, 0)
            FROM product_lots l
            INNER JOIN products p ON p.id = l.product_id
            WHERE IFNULL(p.active, 1) = 1;
            """;

        var map = new Dictionary<int, List<LoadedLot>>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var productId = reader.GetInt32(1);
            var lot = new LoadedLot(
                reader.GetInt32(0),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                InventoryProjectionLotParser.ReadSqliteNumber(reader.GetValue(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                InventoryProjectionLotParser.ReadSqliteNumber(reader.GetValue(5)),
                InventoryProjectionLotParser.ReadSqliteNumber(reader.GetValue(6)));

            if (!map.TryGetValue(productId, out var list))
            {
                list = [];
                map[productId] = list;
            }
            list.Add(lot);
        }

        return map;
    }

    readonly record struct LoadedLot(
        int Id,
        string? LotNumber,
        double Quantity,
        string? ExpiryRaw,
        double UnitCost,
        double ProductCostPrice);
}
