using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 41B — Fusões do carrinho PDV (Avulso/Maço) e smoke Finalize/Cancel.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvCartCigaretteModeTests
{
    private static void BeginStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    [Fact]
    public void Cart_AvulsoEMaco_DuasLinhas()
    {
        var product = MakeRothmans();
        var cart = new List<PdvCartLine>();
        var counter = 0;

        var avulso = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);
        PdvCartHelper.IncludeOrMerge(cart, product, 1, avulso.UnitPrice, avulso.StockUnitsPerSale,
            ref counter, PdvCartHelper.LineDisplayName(product, avulso.ModeLabel));

        var maco = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);
        PdvCartHelper.IncludeOrMerge(cart, product, 1, maco.UnitPrice, maco.StockUnitsPerSale,
            ref counter, PdvCartHelper.LineDisplayName(product, maco.ModeLabel));

        Assert.Equal(2, cart.Count);
        Assert.Equal(1.50, cart.Single(c => c.StockUnitsPerSale < 1.1).UnitPrice);
        Assert.Equal(28.50, cart.Single(c => c.StockUnitsPerSale > 1.1).UnitPrice);
        Assert.Equal(30.00, ProductPriceHelper.RoundPrice(cart.Sum(c => c.Subtotal)));
        Assert.Equal(21, cart.Sum(c => c.StockQuantity));
    }

    [Fact]
    public void Cart_DoisAvulsos_PreservaPrecoAvulso()
    {
        var product = MakeRothmans();
        var cart = new List<PdvCartLine>();
        var counter = 0;
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);

        PdvCartHelper.IncludeOrMerge(cart, product, 1, scan.UnitPrice, scan.StockUnitsPerSale, ref counter);
        PdvCartHelper.IncludeOrMerge(cart, product, 1, scan.UnitPrice, scan.StockUnitsPerSale, ref counter);

        Assert.Single(cart);
        Assert.Equal(2, cart[0].Quantity);
        Assert.Equal(1.50, cart[0].UnitPrice);
        Assert.Equal(3.00, cart[0].Subtotal);
        Assert.Equal(2, cart[0].StockQuantity);
    }

    [Fact]
    public void Cart_DoisMacos_Stock40()
    {
        var product = MakeRothmans();
        var cart = new List<PdvCartLine>();
        var counter = 0;
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);

        PdvCartHelper.IncludeOrMerge(cart, product, 1, scan.UnitPrice, scan.StockUnitsPerSale, ref counter);
        PdvCartHelper.IncludeOrMerge(cart, product, 1, scan.UnitPrice, scan.StockUnitsPerSale, ref counter);

        Assert.Single(cart);
        Assert.Equal(2, cart[0].Quantity);
        Assert.Equal(28.50, cart[0].UnitPrice);
        Assert.Equal(57.00, cart[0].Subtotal);
        Assert.Equal(40, cart[0].StockQuantity);
    }

    [Fact]
    public void Cart_CincoAvulsos_StockQty5()
    {
        var product = MakeRothmans();
        var cart = new List<PdvCartLine>();
        var counter = 0;
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);
        PdvCartHelper.IncludeOrMerge(cart, product, 5, scan.UnitPrice, scan.StockUnitsPerSale, ref counter);

        Assert.Single(cart);
        Assert.Equal(5, cart[0].Quantity);
        Assert.Equal(1.50, cart[0].UnitPrice);
        Assert.Equal(7.50, cart[0].Subtotal);
        Assert.Equal(5, cart[0].StockQuantity);
    }

    [Fact]
    public void ResolveLineUnitPrice_ProdutoComum_AtacadoPreservado()
    {
        var extra = new ProductExtra { QtdAtacado = 3, PrecoAtacado = 2.50 }.ToJson();
        var product = new Product
        {
            Id = 1,
            Name = "Refrigerante Lata",
            GroupName = "Bebidas",
            SalePrice = 4.00,
            ExtraJson = extra,
            Unit = "UN",
        };

        Assert.Equal(4.00, PdvCartHelper.ResolveLineUnitPrice(product, 1, 4.00, 1));
        Assert.Equal(2.50, PdvCartHelper.ResolveLineUnitPrice(product, 3, 4.00, 1));
    }

    [Fact]
    public void NeedsCigaretteModeChoice_SoComPrecoAvulso()
    {
        Assert.True(PdvCartHelper.NeedsCigaretteModeChoice(MakeRothmans()));
        Assert.False(PdvCartHelper.NeedsCigaretteModeChoice(MakeRothmans(precoAvulso: 0)));
        Assert.False(PdvCartHelper.NeedsCigaretteModeChoice(new Product
        {
            Name = "Água 500ml",
            GroupName = "Bebidas",
            SalePrice = 2,
            ExtraJson = """{"preco_avulso":1.5}""",
            Unit = "UN",
        }));
    }

    [Fact]
    public void FinalizeCancel_AvulsoEMaco_EstoqueCompartilhado()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(50, "cart-mix");

        var id = SeedRothmansDb(stock: 100);
        var product = LoadProduct(id);
        var avulso = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);
        var maco = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);

        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = id,
                    Code = product.Code ?? "",
                    Name = PdvCartHelper.LineDisplayName(product, avulso.ModeLabel),
                    Unit = "UN",
                    Quantity = 1,
                    UnitPrice = avulso.UnitPrice,
                    StockUnitsPerSale = avulso.StockUnitsPerSale,
                },
                new PdvCartLine
                {
                    ProductId = id,
                    Code = product.Code ?? "",
                    Name = PdvCartHelper.LineDisplayName(product, maco.ModeLabel),
                    Unit = "UN",
                    Quantity = 1,
                    UnitPrice = maco.UnitPrice,
                    StockUnitsPerSale = maco.StockUnitsPerSale,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 30,
        });

        Assert.Equal(30.00, sale.Total);
        Assert.Equal(79, TestDataHelper.GetProductStock(id));

        var stocks = GetSaleItemStockQtys(sale.SaleId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 1.0, 20.0 }, stocks);

        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(100, TestDataHelper.GetProductStock(id));
    }

    private static Product MakeRothmans(double precoAvulso = 1.50) =>
        new()
        {
            Id = 42,
            Code = "ROTH1",
            Name = "Rothmans Blue",
            GroupName = "Cigarros",
            SalePrice = 28.50,
            Unit = "UN",
            ExtraJson = new ProductExtra
            {
                FatorEmbalagem = 20,
                QtdAtacado = 20,
                PrecoAtacado = 28.50,
                PrecoAvulso = precoAvulso,
            }.ToJson(),
        };

    private static int SeedRothmansDb(double stock)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = 20,
            QtdAtacado = 20,
            PrecoAtacado = 28.50,
            PrecoAvulso = 1.50,
        }.ToJson();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'ROTH1', 'Rothmans Blue', 'Cigarros', 'UN', 28.5, $stock, 24, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static Product LoadProduct(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, barcode, name, group_name, unit, cost_price, sale_price,
                   min_stock, stock, location, extra_json, active, created_at,
                   IFNULL(stock_fridge, 0), IFNULL(stock_fridge_min, 0)
            FROM products WHERE id = $id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        return new Product
        {
            Id = r.GetInt32(0),
            Code = r.IsDBNull(1) ? null : r.GetString(1),
            Barcode = r.IsDBNull(2) ? null : r.GetString(2),
            Name = r.GetString(3),
            GroupName = r.IsDBNull(4) ? null : r.GetString(4),
            Unit = r.GetString(5),
            CostPrice = r.GetDouble(6),
            SalePrice = r.GetDouble(7),
            MinStock = r.GetInt32(8),
            Stock = r.GetDouble(9),
            Location = r.IsDBNull(10) ? null : r.GetString(10),
            ExtraJson = r.IsDBNull(11) ? "{}" : r.GetString(11),
            Active = r.GetInt32(12) != 0,
            CreatedAt = r.IsDBNull(13) ? "" : r.GetString(13)!,
            StockFridge = r.GetDouble(14),
            StockFridgeMin = Convert.ToInt32(r.GetValue(15)),
        };
    }

    private static List<double> GetSaleItemStockQtys(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(stock_qty,0) FROM sale_items WHERE sale_id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        var list = new List<double>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(r.GetDouble(0));
        return list;
    }
}
