using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69D-C2-B2 — merge com custo médio do estoque físico total e FKs completas.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class ProductMergeCostTests
{
    private static readonly DateTime ExpiryFar = DateTime.Today.AddDays(200);

    private static TempDatabase BeginDb()
    {
        ProductService.TestBeforeApplyMergeCost = null;
        ProductService.TestAfterRemapProductIds = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void Formula_DepositoMaisGeladeira_567()
    {
        var cost = ProductMergeRules.WeightedPhysicalAverage(
            keepWarehouse: 0, keepFridge: 20, keepCost: 5,
            absorbWarehouse: 10, absorbFridge: 0, absorbCost: 7,
            cigarette: false, packFactor: 1);
        Assert.Equal(5.67, cost);
    }

    [Fact]
    public void DepositoMaisGeladeira_IndependeDaOrdem()
    {
        using var _ = BeginDb();
        var a = Seed("A1", "AGUA A", stock: 0, fridge: 20, cost: 5, sale: 8);
        var b = Seed("B1", "AGUA B", stock: 10, fridge: 0, cost: 7, sale: 9);

        var keepA = ProductService.MergeProducts(a, b);
        Assert.Equal(5.67, keepA.CostPrice);
        Assert.Equal(10, keepA.Stock);
        Assert.Equal(20, keepA.StockFridge);
    }

    [Fact]
    public void OrdemInversa_MesmoCusto()
    {
        using var _ = BeginDb();
        var a = Seed("A2", "AGUA A", stock: 0, fridge: 20, cost: 5, sale: 8);
        var b = Seed("B2", "AGUA B", stock: 10, fridge: 0, cost: 7, sale: 9);

        var keepB = ProductService.MergeProducts(b, a);
        Assert.Equal(5.67, keepB.CostPrice);
        Assert.Equal(10, keepB.Stock);
        Assert.Equal(20, keepB.StockFridge);
        Assert.Equal(9, keepB.SalePrice);
    }

    [Fact]
    public void SomenteGeladeira_567()
    {
        using var _ = BeginDb();
        var a = Seed("G1", "GELA A", stock: 0, fridge: 20, cost: 5);
        var b = Seed("G2", "GELA B", stock: 0, fridge: 10, cost: 7);
        Assert.Equal(5.67, ProductService.MergeProducts(a, b).CostPrice);
    }

    [Fact]
    public void Misto_Custo6()
    {
        using var _ = BeginDb();
        var a = Seed("M1", "MISTO A", stock: 10, fridge: 10, cost: 5);
        var b = Seed("M2", "MISTO B", stock: 5, fridge: 15, cost: 7);
        Assert.Equal(6, ProductService.MergeProducts(a, b).CostPrice);
    }

    [Fact]
    public void ZeroKeep_UsaCustoAbsorb()
    {
        using var _ = BeginDb();
        var a = Seed("Z1", "ZERO A", stock: 0, fridge: 0, cost: 5);
        var b = Seed("Z2", "ZERO B", stock: 10, fridge: 0, cost: 7);
        Assert.Equal(7, ProductService.MergeProducts(a, b).CostPrice);
        Assert.Equal(7, ProductService.MergeProducts(
            Seed("Z3", "ZERO C", stock: 10, fridge: 0, cost: 7),
            Seed("Z4", "ZERO D", stock: 0, fridge: 0, cost: 5)).CostPrice);
    }

    [Fact]
    public void AmbosZero_PreservaCustoKeep()
    {
        using var _ = BeginDb();
        var a = Seed("ZZ1", "VAZIO A", stock: 0, fridge: 0, cost: 5);
        var b = Seed("ZZ2", "VAZIO B", stock: 0, fridge: 0, cost: 7);
        Assert.Equal(5, ProductService.MergeProducts(a, b).CostPrice);
        Assert.False(ProductService.GetById(b)!.Active);
    }

    [Fact]
    public void EstoqueNegativo_Bloqueia()
    {
        using var _ = BeginDb();
        var a = Seed("N1", "NEG A", stock: -2, fridge: 0, cost: 5);
        var b = Seed("N2", "NEG B", stock: 10, fridge: 0, cost: 7);
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(a, b));
        Assert.Equal(ProductMergeRules.NegativeStockMessage, ex.Message);
        Assert.True(ProductService.GetById(b)!.Active);
        Assert.Equal(10, TestDataHelper.GetProductStock(b));
    }

    [Fact]
    public void Cigarro_MesmoFator_MediaPorMacos()
    {
        using var _ = BeginDb();
        var a = SeedCig("CA", 200, 10, 20);
        var b = SeedCig("CB", 100, 12, 20);
        var merged = ProductService.MergeProducts(a, b);
        Assert.Equal(10.67, merged.CostPrice);
        Assert.Equal(300, merged.Stock);
        var extra = ProductExtra.Parse(merged.ExtraJson);
        Assert.Equal(1.50, extra.PrecoAvulso);
        Assert.Equal(10, extra.PrecoAtacado);
        var product = ProductService.GetById(a)!;
        Assert.Equal(10, PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Maco).UnitPrice);
        Assert.Equal(1.50, PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Avulso).UnitPrice);
    }

    [Fact]
    public void Cigarro_FatoresDiferentes_Bloqueia()
    {
        using var _ = BeginDb();
        var a = SeedCig("FA", 200, 10, 20);
        var b = SeedCig("FB", 100, 12, 10);
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(a, b));
        Assert.Equal(ProductMergeRules.DifferentCigaretteFactorMessage, ex.Message);
        Assert.True(ProductService.GetById(a)!.Active);
        Assert.True(ProductService.GetById(b)!.Active);
    }

    [Fact]
    public void NormalMaisCigarro_Bloqueia()
    {
        using var _ = BeginDb();
        var agua = Seed("AG", "AGUA 500", stock: 10, fridge: 0, cost: 5);
        var cig = SeedCig("CG", 200, 10, 20);
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(agua, cig));
        Assert.Equal(ProductMergeRules.NormalAndCigaretteMessage, ex.Message);
    }

    [Fact]
    public void Cigarro_FatorAusente_TrataComo20_SeKeepTambem20()
    {
        using var _ = BeginDb();
        var keep = SeedCig("CF20", 200, 10, 20);
        var absorb = SeedCig("CF0", 100, 12, 0);
        Assert.Equal(10.67, ProductService.MergeProducts(keep, absorb).CostPrice);
    }

    [Fact]
    public void Cigarro_FatorAusenteVsFator10_Bloqueia()
    {
        using var _ = BeginDb();
        var keep = SeedCig("CF10", 200, 10, 10);
        var absorb = SeedCig("CFX", 100, 12, 0);
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keep, absorb));
        Assert.Equal(ProductMergeRules.DifferentCigaretteFactorMessage, ex.Message);
        Assert.True(ProductService.GetById(keep)!.Active);
        Assert.True(ProductService.GetById(absorb)!.Active);
    }

    [Fact]
    public void Cigarro_GeladeiraEntraNaMedia()
    {
        using var _ = BeginDb();
        var a = SeedCig("CG1", 0, 10, 20);
        TestDataHelper.SetProductFridge(a, 200);
        var b = SeedCig("CG2", 100, 12, 20);
        Assert.Equal(10.67, ProductService.MergeProducts(a, b).CostPrice);
    }

    [Fact]
    public void PrecoCompra_UltimaCompraDoKeepVence()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var keep = Seed("PK2", "KEEP DEPOIS", stock: 0, fridge: 0, cost: 5);
        var absorb = Seed("PA2", "ABS ANTES", stock: 0, fridge: 0, cost: 5);
        CreateClosed(supplier, absorb, "ABS ANTES", 10, 7, "NF-A2");
        CreateClosed(supplier, keep, "KEEP DEPOIS", 10, 9, "NF-K3");
        Assert.Equal(9, PrecoCompra(ProductService.MergeProducts(keep, absorb)));
    }

    [Fact]
    public void PrecoCompra_UltimaCompraDoAbsorbVence()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var keep = Seed("PK", "KEEP COMPRA", stock: 0, fridge: 0, cost: 5);
        var absorb = Seed("PA", "ABS COMPRA", stock: 0, fridge: 0, cost: 5);
        CreateClosed(supplier, keep, "KEEP COMPRA", 10, 7, "NF-K");
        CreateClosed(supplier, absorb, "ABS COMPRA", 10, 9, "NF-A");

        var merged = ProductService.MergeProducts(keep, absorb);
        Assert.Equal(9, PrecoCompra(merged));
    }

    [Fact]
    public void PrecoCompra_CompraCanceladaNaoConta()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var keep = Seed("CK", "KEEP CANC", stock: 10, fridge: 0, cost: 5);
        var absorb = Seed("CA2", "ABS CANC", stock: 0, fridge: 0, cost: 5);
        CreateClosed(supplier, keep, "KEEP CANC", 5, 7, "NF-K2");
        var cancelled = CreateClosed(supplier, absorb, "ABS CANC", 5, 11, "NF-AC");
        PurchaseService.Cancel(cancelled);

        Assert.Equal(7, PrecoCompra(ProductService.MergeProducts(keep, absorb)));
    }

    [Fact]
    public void PrecoCompra_SemCompra_Zero()
    {
        using var _ = BeginDb();
        var keep = Seed("SK", "SEM COMPRA K", stock: 0, fridge: 0, cost: 5);
        var absorb = Seed("SA", "SEM COMPRA A", stock: 0, fridge: 0, cost: 7);
        Assert.Equal(0, PrecoCompra(ProductService.MergeProducts(keep, absorb)));
    }

    [Fact]
    public void PurchaseItemLots_RemapeiaParaKeep()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var keep = Seed("LK", "LOTE KEEP", stock: 0, fridge: 0, cost: 2);
        var absorb = Seed("LA", "LOTE ABS", stock: 0, fridge: 0, cost: 2);
        var purchaseId = CreateClosed(supplier, absorb, "LOTE ABS", 10, 3, "NF-L", "B", ExpiryFar);

        ProductService.MergeProducts(keep, absorb);

        Assert.Equal(keep, LotProductId(purchaseId));
        Assert.Equal(keep, PurchaseItemProductId(purchaseId));
        Assert.Equal(10, LotQty(keep, "B"));
        Assert.Equal(0, LotQty(absorb, "B"));
    }

    [Fact]
    public void CancelPosMerge_BloqueiaPorMovementEstrutural()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var keep = Seed("MK", "MERGE KEEP", stock: 0, fridge: 0, cost: 2);
        var absorb = Seed("MA", "MERGE ABS", stock: 0, fridge: 0, cost: 2);
        var purchaseId = CreateClosed(supplier, absorb, "MERGE ABS", 10, 3, "NF-M", "B", ExpiryFar);

        ProductService.MergeProducts(keep, absorb);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage, ex.Message);
        Assert.Equal("fechada", PurchaseStatus(purchaseId));
        Assert.Equal(keep, LotProductId(purchaseId));
        Assert.Equal(1, CountOp(keep, ProductMergeRules.MergeOperation));
    }

    [Fact]
    public void SaleItemsEOpenTab_RemapeiamProductId_PreservamNome()
    {
        using var _ = BeginDb();
        CashService.OpenSession(50, "merge-sale");
        var keep = Seed("OK", "KEEP TAB", stock: 20, fridge: 0, cost: 2, sale: 8);
        var absorb = Seed("OA", "ABS TAB", stock: 20, fridge: 0, cost: 2, sale: 8);
        var sale = TestDataHelper.FinalizeSimpleCashSale(absorb, 1, 8, 8);
        var tab = OpenTabService.Create("Comanda merge");
        OpenTabService.AddProduct(tab, absorb, 1);

        ProductService.MergeProducts(keep, absorb);

        Assert.Equal(keep, SaleItemProductId(sale.SaleId));
        Assert.Equal("ABS TAB", SaleItemName(sale.SaleId));
        Assert.NotEqual("KEEP TAB", SaleItemName(sale.SaleId));
        var line = Assert.Single(OpenTabService.Get(tab).Items);
        Assert.Equal(keep, line.ProductId);
        Assert.Equal("ABS TAB", line.ProductName);
    }

    [Fact]
    public void FalhaAntesDoCusto_Rollback()
    {
        using var _ = BeginDb();
        var keep = Seed("RBK", "RB KEEP", stock: 10, fridge: 0, cost: 5);
        var absorb = Seed("RBA", "RB ABS", stock: 10, fridge: 0, cost: 7);
        try
        {
            ProductService.TestBeforeApplyMergeCost = () =>
                throw new InvalidOperationException("falha controlada antes do custo");
            var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keep, absorb));
            Assert.Contains("falha controlada antes do custo", ex.Message);
        }
        finally
        {
            ProductService.TestBeforeApplyMergeCost = null;
        }

        Assert.True(ProductService.GetById(absorb)!.Active);
        Assert.Equal(10, TestDataHelper.GetProductStock(keep));
        Assert.Equal(10, TestDataHelper.GetProductStock(absorb));
        Assert.Equal(5, ProductService.GetById(keep)!.CostPrice);
    }

    [Fact]
    public void FalhaDepoisDoRemap_RollbackLotesEFks()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var keep = Seed("RK", "REM KEEP", stock: 0, fridge: 0, cost: 2);
        var absorb = Seed("RA", "REM ABS", stock: 0, fridge: 0, cost: 2);
        var purchaseId = CreateClosed(supplier, absorb, "REM ABS", 8, 3, "NF-R", "R1", ExpiryFar);
        try
        {
            ProductService.TestAfterRemapProductIds = () =>
                throw new InvalidOperationException("falha controlada depois do remap");
            var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keep, absorb));
            Assert.Contains("falha controlada depois do remap", ex.Message);
        }
        finally
        {
            ProductService.TestAfterRemapProductIds = null;
        }

        Assert.True(ProductService.GetById(absorb)!.Active);
        Assert.Equal(absorb, LotProductId(purchaseId));
        Assert.Equal(8, LotQty(absorb, "R1"));
        Assert.Equal(0, CountOp(keep, ProductMergeRules.MergeOperation));
    }

    [Fact]
    public void InventarioAberto_Bloqueia()
    {
        using var _ = BeginDb();
        var keep = Seed("IK", "INV KEEP", stock: 10, fridge: 0, cost: 5);
        var absorb = Seed("IA", "INV ABS", stock: 5, fridge: 0, cost: 7);
        InventoryService.CreateSession();
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keep, absorb));
        Assert.Equal(ProductMergeRules.OpenInventoryMessage, ex.Message);
        Assert.True(ProductService.GetById(absorb)!.Active);
    }

    [Fact]
    public void AuditEMovement_ContemBeforeAfterSemSegredo()
    {
        using var _ = BeginDb();
        var keep = Seed("AUK", "AUD KEEP", stock: 0, fridge: 20, cost: 5);
        var absorb = Seed("AUA", "AUD ABS", stock: 10, fridge: 0, cost: 7);

        ProductService.MergeProducts(keep, absorb);

        Assert.Equal(1, CountOp(keep, ProductMergeRules.MergeOperation));
        using var conn = DatabaseService.OpenConnection();
        using var mov = conn.CreateCommand();
        mov.CommandText = """
            SELECT notes, IFNULL(ref_type,''), IFNULL(ref_id,0)
            FROM movements WHERE product_id = $id AND operation = $op LIMIT 1;
            """;
        mov.Parameters.AddWithValue("$id", keep);
        mov.Parameters.AddWithValue("$op", ProductMergeRules.MergeOperation);
        using (var r = mov.ExecuteReader())
        {
            Assert.True(r.Read());
            var notes = r.GetString(0);
            Assert.Contains("\"keep\":", notes);
            Assert.Contains("\"absorb\":", notes);
            Assert.Contains("\"cost\":", notes);
            Assert.Equal(ProductMergeRules.MergeRefType, r.GetString(1));
            Assert.Equal(absorb, r.GetInt32(2));
        }

        using var aud = conn.CreateCommand();
        aud.CommandText = """
            SELECT user_login, details FROM audit_log
            WHERE action = 'unificar' AND entity = 'produto'
            ORDER BY id DESC LIMIT 1;
            """;
        using var ar = aud.ExecuteReader();
        Assert.True(ar.Read());
        var login = ar.IsDBNull(0) ? "" : ar.GetString(0);
        var details = ar.IsDBNull(1) ? "" : ar.GetString(1);
        Assert.Contains("admin_teste", login);
        Assert.DoesNotContain("token", details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PIN", details, StringComparison.OrdinalIgnoreCase);
        Assert.True(AuditPayloadBuilder.TryParse(details, out var doc));
        Assert.Equal("unificar_produto", doc.Payload.GetProperty("op").GetString());
        Assert.Equal(keep, doc.Payload.GetProperty("keep_id").GetInt32());
        Assert.Equal(absorb, doc.Payload.GetProperty("absorb_id").GetInt32());
        Assert.Equal(5, doc.Payload.GetProperty("cost_keep_before").GetDouble());
        Assert.Equal(7, doc.Payload.GetProperty("cost_absorb_before").GetDouble());
        Assert.Equal(5.67, doc.Payload.GetProperty("cost_after").GetDouble());
        Assert.Equal("merge_produtos", doc.Payload.GetProperty("source").GetString());
    }

    private static int Seed(string code, string name, double stock, double fridge, double cost, double sale = 8)
    {
        var id = TestDataHelper.SeedSimpleProduct(stock, sale, cost, code, name);
        if (fridge > 0)
            TestDataHelper.SetProductFridge(id, fridge);
        return id;
    }

    private static int SeedCig(string code, double stock, double cost, double fator)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAvulso = 1.50,
            PrecoAtacado = 10,
            PrecoCompra = cost,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, 'Rothmans Blue', 'Cigarros', 'UN', 10, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CreateClosed(
        int supplierId, int productId, string name, double qty, double unit, string number,
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
                    ProductName = name,
                    Quantity = qty,
                    UnitPrice = unit,
                    SalePrice = 8,
                    LotNumber = lot,
                    ExpiryDate = expiry,
                },
            ],
        }, closeOnSave: true);
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN MERGE', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double PrecoCompra(Product product) =>
        ProductExtra.Parse(product.ExtraJson).PrecoCompra;

    private static int LotProductId(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM purchase_item_lots WHERE purchase_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int PurchaseItemProductId(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM purchase_items WHERE purchase_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double LotQty(int productId, string lot)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(quantity),0) FROM product_lots
            WHERE product_id = $id AND IFNULL(lot_number,'') = $lot;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.Parameters.AddWithValue("$lot", lot);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static int CountOp(int productId, string op)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM movements WHERE product_id = $id AND operation = $op;";
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.Parameters.AddWithValue("$op", op);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string PurchaseStatus(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static int SaleItemProductId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string SaleItemName(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_name FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
