using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Central de Validades — listagem única a partir de product_lots.
/// extra_json.data_validade é ignorado.
/// </summary>
public static class ValidityControlService
{
    public const string Feature = "validity_control_read";
    public const string HostNeedsUpgradeMessage =
        "O PC da loja precisa ser atualizado para consultar o Controle de Validades.";

    public static ValidityControlSnapshot GetSnapshot(DateTime? today = null)
    {
        if (StoreNetworkMode.IsClient)
            return StoreNetworkClient.GetValidityControl();
        return GetSnapshotLocal(today);
    }

    public static ValidityControlSnapshot GetSnapshotLocal(DateTime? today = null)
    {
        var products = LoadProductsLocal();
        return ValidityControlEngine.Snapshot(products, today);
    }

    public static ValidityControlFilterKind FilterFromLegacyDays(int? days) =>
        days switch
        {
            7 => ValidityControlFilterKind.Days7,
            15 => ValidityControlFilterKind.Days15,
            30 => ValidityControlFilterKind.Days30,
            60 => ValidityControlFilterKind.Days60,
            90 => ValidityControlFilterKind.Days90,
            _ => ValidityControlFilterKind.All,
        };

    static IReadOnlyList<ValidityControlProductInput> LoadProductsLocal()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                p.id,
                IFNULL(p.code, ''),
                IFNULL(p.name, ''),
                IFNULL(p.group_name, ''),
                IFNULL(p.stock, 0),
                IFNULL(p.stock_fridge, 0),
                IFNULL(p.cost_price, 0),
                IFNULL(p.extra_json, ''),
                l.id,
                IFNULL(l.lot_number, ''),
                l.expiry_date,
                IFNULL(l.quantity, 0),
                l.purchase_id,
                IFNULL(l.unit_cost, 0)
            FROM products p
            LEFT JOIN product_lots l
                ON l.product_id = p.id AND l.quantity > 0.0001
            WHERE IFNULL(p.active, 1) = 1
            ORDER BY p.id, l.expiry_date;
            """;

        var map = new Dictionary<int, Builder>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var productId = reader.GetInt32(0);
            if (!map.TryGetValue(productId, out var builder))
            {
                var extraJson = reader.IsDBNull(7) ? "" : reader.GetString(7);
                var extra = ProductExtra.Parse(extraJson);
                builder = new Builder
                {
                    ProductId = productId,
                    Code = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    GroupName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    BrandName = (extra.Marca ?? "").Trim(),
                    Stock = reader.GetDouble(4),
                    StockFridge = reader.GetDouble(5),
                    CostPrice = reader.GetDouble(6),
                    ExplicitExpiryControl = extra.ControleValidade == true,
                };
                map[productId] = builder;
            }

            if (reader.IsDBNull(8))
                continue;

            builder.Lots.Add(new ProductLot
            {
                Id = reader.GetInt32(8),
                ProductId = productId,
                ProductCode = builder.Code,
                ProductName = builder.Name,
                LotNumber = reader.IsDBNull(9) ? "" : reader.GetString(9),
                ExpiryDateIso = reader.IsDBNull(10) ? null : reader.GetString(10),
                Quantity = reader.GetDouble(11),
                PurchaseId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                UnitCost = reader.GetDouble(13),
            });
        }

        return map.Values
            .Select(b => new ValidityControlProductInput
            {
                ProductId = b.ProductId,
                Code = b.Code,
                Name = b.Name,
                GroupName = b.GroupName,
                BrandName = b.BrandName,
                Stock = b.Stock,
                StockFridge = b.StockFridge,
                CostPrice = b.CostPrice,
                ExplicitExpiryControl = b.ExplicitExpiryControl,
                Lots = b.Lots,
            })
            .ToList();
    }

    sealed class Builder
    {
        public int ProductId;
        public string Code = "";
        public string Name = "";
        public string GroupName = "";
        public string BrandName = "";
        public double Stock;
        public double StockFridge;
        public double CostPrice;
        public bool ExplicitExpiryControl;
        public List<ProductLot> Lots { get; } = [];
    }
}
