using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests.Infrastructure;

internal static class TestDataHelper
{
    public static void GrantPdvCancelPermission() => SetSessionRole("admin");

    public static void SetSessionRole(string role)
    {
        AppSession.SetUser(new User
        {
            Id = 1,
            Login = $"{role}_teste",
            Nome = $"Usuário {role}",
            Role = role,
            Permissions = UserPermissions.ForRole(role),
        });
    }

    /// <summary>
    /// Sessão com permissions_json customizado (Customized=true), preservando defaults da role
    /// e aplicando overrides via <paramref name="customize"/>.
    /// </summary>
    public static void SetSessionCustomPermissions(string role, Action<UserPermissions> customize)
    {
        var permissions = UserPermissions.ForRole(role);
        permissions.Customized = true;
        customize(permissions);
        AppSession.SetUser(new User
        {
            Id = 1,
            Login = $"{role}_custom_teste",
            Nome = $"Usuário {role} custom",
            Role = role,
            Permissions = permissions,
        });
    }

    public static int SeedSimpleProduct(
        double stock,
        double salePrice,
        double costPrice,
        string code = "T001",
        string name = "Produto Teste")
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $name, 'UN', $sale, $stock, $cost, 1, '{}'
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$sale", salePrice);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", costPrice);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static double GetProductStock(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(stock, 0) FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    public static double GetProductFridge(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(stock_fridge, 0) FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    public static void SetProductFridge(int productId, double fridge)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock_fridge = $fridge WHERE id = $id;";
        cmd.Parameters.AddWithValue("$fridge", fridge);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    public static int CountMovements(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movements WHERE product_id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static double SumLots(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(SUM(quantity), 0) FROM product_lots WHERE product_id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    public static ProductInput CatalogInputFrom(Product product, Action<ProductInput>? tweak = null)
    {
        var extra = ProductExtra.Parse(product.ExtraJson);
        var input = new ProductInput
        {
            Code = product.Code,
            Barcode = product.Barcode,
            Name = product.Name,
            GroupName = product.GroupName,
            Unit = product.Unit,
            CostPrice = product.CostPrice,
            SalePrice = product.SalePrice,
            MinStock = product.MinStock,
            Stock = product.Stock,
            StockFridge = product.StockFridge,
            StockFridgeMin = product.StockFridgeMin,
            Location = product.Location,
            Extra = extra,
            Active = product.Active,
        };
        tweak?.Invoke(input);
        return input;
    }

    public static PdvFinalizeResult FinalizeSimpleCashSale(
        int productId, double qty, double unitPrice, double cashReceived)
    {
        return PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = productId,
                    Code = "T001",
                    Name = "Produto Teste",
                    Unit = "UN",
                    Quantity = qty,
                    UnitPrice = unitPrice,
                    StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = cashReceived,
        });
    }
}
