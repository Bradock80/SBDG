using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 42A — Avulso/Maço no Deck (service + settle). UI do diálogo coberta indiretamente
/// via ResolveManualSale/ResolveCigaretteSale + AddProduct (mesmo contrato da View).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class OpenTabCigaretteSaleModeTests
{
    private static void BeginStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    [Fact]
    public void Add_Avulso()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans();
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);

        var item = AddResolved(tabId, scan);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(1.50, item.UnitPrice);
        Assert.Equal(1, item.StockUnitsPerSale);
        Assert.Contains("AVULSO", item.ProductName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Add_Maco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans();
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);

        var item = AddResolved(tabId, scan);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(28.50, item.UnitPrice);
        Assert.Equal(20, item.StockUnitsPerSale);
        Assert.Contains("MAÇO", item.ProductName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoisAvulsos_MergePreservaPreco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans();
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);

        AddResolved(tabId, scan);
        AddResolved(tabId, scan);

        var items = OpenTabService.Get(tabId).Items;
        Assert.Single(items);
        Assert.Equal(2, items[0].Quantity);
        Assert.Equal(1.50, items[0].UnitPrice);
        Assert.Equal(3.00, items[0].Subtotal);
        Assert.Equal(1, items[0].StockUnitsPerSale);
    }

    [Fact]
    public void DoisMacos_MergeStockFator40()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans();
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);

        AddResolved(tabId, scan);
        AddResolved(tabId, scan);

        var items = OpenTabService.Get(tabId).Items;
        Assert.Single(items);
        Assert.Equal(2, items[0].Quantity);
        Assert.Equal(28.50, items[0].UnitPrice);
        Assert.Equal(20, items[0].StockUnitsPerSale);
        Assert.Equal(40, items[0].Quantity * items[0].StockUnitsPerSale);
    }

    [Fact]
    public void AvulsoEMaco_DuasLinhas()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans();

        AddResolved(tabId, PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso));
        AddResolved(tabId, PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco));

        var items = OpenTabService.Get(tabId).Items;
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => Math.Abs(i.StockUnitsPerSale - 1) < 0.001 && Math.Abs(i.UnitPrice - 1.50) < 0.001);
        Assert.Contains(items, i => Math.Abs(i.StockUnitsPerSale - 20) < 0.001 && Math.Abs(i.UnitPrice - 28.50) < 0.001);
    }

    [Fact]
    public void Qty_Avulso5_E_Maco2()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans();
        var avulso = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);
        var maco = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);

        var a = AddResolved(tabId, avulso);
        OpenTabService.SetItemQuantity(a.Id, 5);
        a = OpenTabService.Get(tabId).Items.Single(i => i.Id == a.Id);
        Assert.Equal(5, a.Quantity);
        Assert.Equal(1.50, a.UnitPrice);
        Assert.Equal(1, a.StockUnitsPerSale);
        Assert.Equal(7.50, a.Subtotal);

        var m = AddResolved(tabId, maco);
        OpenTabService.SetItemQuantity(m.Id, 2);
        m = OpenTabService.Get(tabId).Items.Single(i => i.Id == m.Id);
        Assert.Equal(2, m.Quantity);
        Assert.Equal(28.50, m.UnitPrice);
        Assert.Equal(20, m.StockUnitsPerSale);
        Assert.Equal(57.00, m.Subtotal);
        Assert.Equal(40, m.Quantity * m.StockUnitsPerSale);
    }

    [Fact]
    public void CigarroSemPrecoAvulso_EntraComoMaco()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans(precoAvulso: 0);
        Assert.False(PdvCartHelper.NeedsCigaretteModeChoice(product));

        // Espelha a View: sem diálogo → ResolveCigaretteSale MAÇO (não AddProduct cru).
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);
        var item = AddResolved(tabId, scan);

        Assert.Equal(28.50, item.UnitPrice);
        Assert.Equal(20, item.StockUnitsPerSale);
        Assert.Equal(1, item.Quantity);
    }

    [Fact]
    public void ProdutoComum_Preservado()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var pid = TestDataHelper.SeedSimpleProduct(stock: 50, salePrice: 4, costPrice: 2, code: "AGUA1", name: "Agua 500ml");
        var tabId = OpenTabService.Create("Mesa Comum");
        var item = OpenTabService.AddProduct(tabId, pid, 2);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(4, item.UnitPrice);
        Assert.Equal(1, item.StockUnitsPerSale);
        Assert.Equal("Agua 500ml", item.ProductName);
    }

    [Fact]
    public void RemoveAntesSettle_NaoAlteraStock()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans(stock: 100);
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);
        var item = AddResolved(tabId, scan);
        Assert.Equal(100, TestDataHelper.GetProductStock(product.Id));

        OpenTabService.RemoveItem(item.Id);
        Assert.Equal(100, TestDataHelper.GetProductStock(product.Id));
        Assert.Empty(OpenTabService.Get(tabId).Items);
    }

    [Fact]
    public void Settle_MacoETresAvulsos_Fisico23_ECancel()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(50, "deck-cig");

        var (tabId, product) = SeedDeckRothmans(stock: 100);
        var avulso = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);
        var maco = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Maco);

        AddResolved(tabId, maco);
        AddResolved(tabId, avulso, qty: 3);

        var lines = OpenTabService.ToCartLines(tabId).ToList();
        Assert.Equal(23, lines.Sum(l => l.StockQuantity));

        var result = OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
        {
            Items = lines,
            PaymentType = "Dinheiro",
            CashReceived = 28.50 + 4.50,
        });

        Assert.Equal(77, TestDataHelper.GetProductStock(product.Id));

        var stocks = GetSaleItemStockQtys(result.SaleId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { 3.0, 20.0 }, stocks);

        PdvService.CancelSale(result.SaleId);
        Assert.Equal(100, TestDataHelper.GetProductStock(product.Id));
    }

    [Fact]
    public void Client_AddProduct_Bloqueado()
    {
        using var _ = TempDatabase.Create();
        BeginStandalone();
        var (tabId, product) = SeedDeckRothmans();
        var scan = PdvService.ResolveCigaretteSale(product, PdvCigaretteSaleMode.Avulso);

        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        try
        {
            Assert.Throws<StoreNetworkClientBlockedException>(() => AddResolved(tabId, scan));
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    private static OpenTabItemRow AddResolved(int tabId, PdvScanResult scan, double qty = 1) =>
        OpenTabService.AddProduct(
            tabId,
            scan.Product.Id,
            qty * scan.Quantity,
            scan.UnitPrice,
            scan.StockUnitsPerSale,
            PdvCartHelper.LineDisplayName(scan.Product, scan.ModeLabel));

    private static (int TabId, Product Product) SeedDeckRothmans(
        double stock = 200, double precoAvulso = 1.50)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = 20,
            QtdAtacado = 20,
            PrecoAtacado = 28.50,
            PrecoAvulso = precoAvulso,
        }.ToJson();

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'ROTHD', 'Rothmans Blue', 'Cigarros', 'UN', 28.5, $stock, 24, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        var tabId = OpenTabService.Create("Mesa Cigarro");
        return (tabId, LoadProduct(id));
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
