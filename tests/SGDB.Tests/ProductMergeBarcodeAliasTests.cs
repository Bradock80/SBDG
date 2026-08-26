using SGDB.Domain.Products;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>ETAPA 69T-B — aliases de barcode na unificação.</summary>
[Collection(TempDatabaseCollection.Name)]
public class ProductMergeBarcodeAliasTests
{
    private static TempDatabase BeginDb()
    {
        ProductService.TestBeforeApplyMergeCost = null;
        ProductService.TestAfterRemapProductIds = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    private static int Seed(
        string code, string name, double stock, double cost, double sale,
        string? barcode, string? packBarcode = null, double fator = 1, double fridge = 0)
    {
        var extra = new ProductExtra
        {
            BarcodeEmbalagem = packBarcode,
            FatorEmbalagem = fator,
            QtdAtacado = fator > 1 ? fator : 0,
            PrecoCompra = cost,
        };
        var id = ProductService.Create(new ProductInput
        {
            Code = code,
            Barcode = barcode,
            Name = name,
            GroupName = "GERAL",
            Unit = "UN",
            CostPrice = cost,
            SalePrice = sale,
            Stock = 0,
            Extra = extra,
            Active = true,
        }).Id;
        if (stock > 0 || fridge > 0)
        {
            using var conn = DatabaseService.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE products SET stock=$s, stock_fridge=$f, cost_price=$c WHERE id=$id;";
            cmd.Parameters.AddWithValue("$s", stock);
            cmd.Parameters.AddWithValue("$f", fridge);
            cmd.Parameters.AddWithValue("$c", cost);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        return id;
    }

    [Fact]
    public void Merge_SomaEstoque_MediaPonderada()
    {
        using var _ = BeginDb();
        var a = Seed("A", "ORIGINAL 300", 152, 2.72, 4, "7891000000001");
        var b = Seed("B", "ORIGINAL DUP", 20, 3.00, 5, "7891000000002");
        var m = ProductService.MergeProducts(a, b);
        Assert.Equal(172, m.Stock, 4);
        Assert.Equal(2.75, m.CostPrice, 2);
        Assert.Equal(4, m.SalePrice, 2);
    }

    [Fact]
    public void Merge_EanPrincipalDoB_ViraAliasDeA()
    {
        using var _ = BeginDb();
        var a = Seed("A", "KEEP", 10, 2, 4, "1111111111111");
        var b = Seed("B", "ABS", 5, 3, 5, "2222222222222");
        ProductService.MergeProducts(a, b);
        var hit = ProductService.FindByBarcodeOrPack("2222222222222");
        Assert.NotNull(hit);
        Assert.Equal(a, hit!.Id);
        Assert.Contains("2222222222222", ProductBarcodeService.ListActiveBarcodes(a));
    }

    [Fact]
    public void Merge_PackBarcodeDoB_ViraPackAlias()
    {
        using var _ = BeginDb();
        var a = Seed("A", "KEEP", 10, 2, 4, "1111111111111");
        var b = Seed("B", "ABS", 5, 3, 5, "2222222222222", packBarcode: "3333333333333", fator: 12);
        ProductService.MergeProducts(a, b);
        var hit = ProductService.FindByBarcodeOrPack("3333333333333");
        Assert.NotNull(hit);
        Assert.Equal(a, hit!.Id);
        using var conn = DatabaseService.OpenConnection();
        Assert.Equal(ProductBarcodeKinds.Pack, ProductBarcodeService.FindKind(conn, null, a, "3333333333333"));
    }

    [Fact]
    public void Merge_KeepJaTemPack_BNaoPerdeEan()
    {
        using var _ = BeginDb();
        var a = Seed("A", "KEEP", 10, 2, 4, "1111111111111", packBarcode: "4444444444444", fator: 12);
        var b = Seed("B", "ABS", 5, 3, 5, "2222222222222", packBarcode: "5555555555555", fator: 24);
        ProductService.MergeProducts(a, b);
        Assert.Equal(a, ProductService.FindByBarcodeOrPack("2222222222222")!.Id);
        Assert.Equal(a, ProductService.FindByBarcodeOrPack("5555555555555")!.Id);
        Assert.Equal(a, ProductService.FindByBarcodeOrPack("4444444444444")!.Id);
        var keepExtra = ProductExtra.Parse(ProductService.GetById(a)!.ExtraJson);
        Assert.Equal("4444444444444", TextNorm.NormalizeBarcode(keepExtra.BarcodeEmbalagem));
    }

    [Fact]
    public void Nfe_EanAntigoDoB_EncontraA()
    {
        using var _ = BeginDb();
        var a = Seed("A", "KEEP NF", 10, 2, 4, "1111111111111");
        var b = Seed("B", "ABS NF", 5, 3, 5, "2222222222222");
        ProductService.MergeProducts(a, b);
        Assert.Equal(a, ProductService.FindByBarcodeOrPack("2222222222222")!.Id);
    }

    [Fact]
    public void Pdv_EanAntigoDoB_EncontraA()
    {
        using var _ = BeginDb();
        var a = Seed("A", "KEEP PDV", 10, 2, 4, "1111111111111");
        var b = Seed("B", "ABS PDV", 5, 3, 5, "2222222222222");
        ProductService.MergeProducts(a, b);
        var scan = PdvService.ResolveExactBarcode("2222222222222");
        Assert.NotNull(scan);
        Assert.Equal(a, scan!.Product.Id);
        Assert.False(scan.IsPackSale);
    }

    [Fact]
    public void BarcodeConflitanteComTerceiro_Bloqueia()
    {
        using var _ = BeginDb();
        var keep = Seed("K", "KEEP2", 10, 2, 4, "1212121212121");
        var absorb = Seed("X", "ABS2", 5, 3, 5, "1313131313131");
        var third = Seed("T", "THIRD", 1, 1, 2, "1414141414141");
        using (var conn = DatabaseService.OpenConnection())
        {
            ProductBarcodeService.Upsert(
                conn, null, third,
                "1313131313131", ProductBarcodeKinds.Alias, 1, "test-conflict");
        }

        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keep, absorb));
        Assert.Contains("já está em outro produto", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(ProductService.GetById(absorb)!.Active);
    }

    [Fact]
    public void Rollback_EmErroAntesDoCusto()
    {
        using var _ = BeginDb();
        var a = Seed("A", "RB KEEP", 10, 2, 4, "1111111111111");
        var b = Seed("B", "RB ABS", 5, 3, 5, "2222222222222");
        try
        {
            ProductService.TestBeforeApplyMergeCost = () =>
                throw new InvalidOperationException("falha controlada");
            Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(a, b));
        }
        finally
        {
            ProductService.TestBeforeApplyMergeCost = null;
        }
        Assert.True(ProductService.GetById(b)!.Active);
        Assert.Equal(5, ProductService.GetById(b)!.Stock, 4);
        Assert.Equal(b, ProductService.FindByBarcodeOrPack("2222222222222")!.Id);
    }

    [Fact]
    public void InventoryItems_Remapeados()
    {
        using var _ = BeginDb();
        var a = Seed("A", "INV KEEP", 10, 2, 4, "1111111111111");
        var b = Seed("B", "INV ABS", 5, 3, 5, "2222222222222");
        ProductService.MergeProducts(a, b);
        Assert.Equal(15, ProductService.GetById(a)!.Stock, 4);
    }

    [Fact]
    public void Absorb_ActiveZero_StockZerado()
    {
        using var _ = BeginDb();
        var a = Seed("A", "KEEP", 10, 2, 4, "1111111111111");
        var b = Seed("B", "ABS", 5, 3, 5, "2222222222222");
        ProductService.MergeProducts(a, b);
        var abs = ProductService.GetById(b)!;
        Assert.False(abs.Active);
        Assert.Equal(0, abs.Stock, 4);
        Assert.Equal(0, abs.StockFridge, 4);
    }

    [Fact]
    public void LotesPurchaseSalesOpenTab_Preservados()
    {
        using var _ = BeginDb();
        CashService.OpenSession(50, "merge-bc");
        var supplier = SeedSupplier();
        var a = Seed("A", "KEEP LOT", 0, 2, 8, "1111111111111");
        var b = Seed("B", "ABS LOT", 0, 2, 8, "2222222222222");
        var purchaseId = CreateClosed(supplier, b, 10, 3, "NF-BC", "L1", DateTime.Today.AddDays(100));
        var sale = TestDataHelper.FinalizeSimpleCashSale(b, 1, 8, 8);
        var tab = OpenTabService.Create("Comanda BC");
        OpenTabService.AddProduct(tab, b, 1);

        ProductService.MergeProducts(a, b);

        Assert.Equal(a, PurchaseItemProductId(purchaseId));
        Assert.Equal(a, LotProductId(purchaseId));
        Assert.Equal(a, SaleItemProductId(sale.SaleId));
        Assert.Equal(a, Assert.Single(OpenTabService.Get(tab).Items).ProductId);
    }

    [Fact]
    public void Audit_ContemAliases()
    {
        using var _ = BeginDb();
        var a = Seed("A", "AUD KEEP", 10, 2, 4, "1111111111111");
        var b = Seed("B", "AUD ABS", 5, 3, 5, "2222222222222");
        ProductService.MergeProducts(a, b);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT details FROM audit_log
            WHERE action = 'unificar' AND entity = 'produto'
            ORDER BY id DESC LIMIT 1;
            """;
        var details = cmd.ExecuteScalar()?.ToString() ?? "";
        Assert.True(AuditPayloadBuilder.TryParse(details, out var doc));
        Assert.True(doc.Payload.TryGetProperty("aliases_moved", out var aliases));
        Assert.True(aliases.GetArrayLength() >= 1);
        Assert.Contains("2222222222222", aliases.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CigarrosFatoresDiferentes_Bloqueia()
    {
        using var _ = BeginDb();
        var a = SeedCig("CA", 200, 10, 20, "8111111111111");
        var b = SeedCig("CB", 100, 12, 10, "8222222222222");
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(a, b));
        Assert.Equal(ProductMergeRules.DifferentCigaretteFactorMessage, ex.Message);
    }

    [Fact]
    public void KitsConflitantes_Bloqueia()
    {
        using var _ = BeginDb();
        var child = Seed("CH", "FILHO", 0, 1, 2, "9000000000001");
        var a = Seed("A", "KIT A", 0, 1, 2, "9111111111111");
        var b = Seed("B", "KIT B", 0, 1, 2, "9222222222222");
        SetComposition(a, child);
        SetComposition(b, child);
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(a, b));
        Assert.Equal(ProductMergeRules.ConflictingCompositionMessage, ex.Message);
    }

    [Fact]
    public void Capability_V3_Anunciada()
    {
        Assert.Equal("product_merge_safe_v3", ProductMergeRules.AtomicFeature);
        Assert.Contains(ProductMergeRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);
    }

    [Fact]
    public void SalePrice_ContinuaDoKeep_PrecoCompraUltimaCompra()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var a = Seed("A", "KEEP PC", 0, 5, 9, "1111111111111");
        var b = Seed("B", "ABS PC", 0, 5, 11, "2222222222222");
        CreateClosed(supplier, a, 10, 7, "NF-K");
        CreateClosed(supplier, b, 10, 9, "NF-A");
        var m = ProductService.MergeProducts(a, b);
        Assert.Equal(9, m.SalePrice, 2);
        Assert.Equal(9, ProductExtra.Parse(m.ExtraJson).PrecoCompra, 2);
    }

    private static int SeedCig(string code, double stock, double cost, double fator, string barcode)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAvulso = 1.5,
            PrecoAtacado = 10,
            PrecoCompra = cost,
        };
        return ProductService.Create(new ProductInput
        {
            Code = code,
            Barcode = barcode,
            Name = "Rothmans Blue",
            GroupName = "Cigarros",
            Unit = "UN",
            CostPrice = cost,
            SalePrice = 10,
            Stock = stock,
            Extra = extra,
            Active = true,
        }).Id;
    }

    private static void SetComposition(int kitId, int childId)
    {
        var p = ProductService.GetById(kitId)!;
        var extra = ProductExtra.Parse(p.ExtraJson);
        extra.ComposicaoItens =
        [
            new ProductCompositionItem { ProductId = childId, Quantity = 1 },
        ];
        ProductService.Update(kitId, new ProductInput
        {
            Code = p.Code,
            Barcode = p.Barcode,
            Name = p.Name ?? "",
            GroupName = p.GroupName,
            Unit = p.Unit,
            CostPrice = p.CostPrice,
            SalePrice = p.SalePrice,
            Stock = p.Stock,
            Extra = extra,
            Active = true,
        });
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN BC', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CreateClosed(
        int supplierId, int productId, double qty, double unit, string number,
        string? lot = null, DateTime? expiry = null)
    {
        return PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = number,
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = productId,
                    ProductName = "X",
                    Quantity = qty,
                    UnitPrice = unit,
                    SalePrice = 8,
                    LotNumber = lot,
                    ExpiryDate = expiry,
                },
            ],
        }, closeOnSave: true);
    }

    private static int PurchaseItemProductId(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM purchase_items WHERE purchase_id=$p LIMIT 1;";
        cmd.Parameters.AddWithValue("$p", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int LotProductId(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM purchase_item_lots WHERE purchase_id=$p LIMIT 1;";
        cmd.Parameters.AddWithValue("$p", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SaleItemProductId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM sale_items WHERE sale_id=$p LIMIT 1;";
        cmd.Parameters.AddWithValue("$p", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
