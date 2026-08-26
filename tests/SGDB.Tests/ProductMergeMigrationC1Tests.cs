using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Domain.Products;
using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69T-C1 — validação estrutural da migração product_barcodes e do merge,
/// somente em bancos isolados em .tmp/69T-C1 (nunca AppData/deposito.db).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class ProductMergeMigrationC1Tests
{
    private const string EanA = "7891000001001";
    private const string PackA = "7891000001023";
    private const string EanB = "7891000002002";
    private const string PackB = "7891000002023";
    private const string EanC = "7891000003003";

    private static string StageDir()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".tmp", "69T-C1"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FreshDb(string name)
    {
        var path = Path.Combine(StageDir(), name);
        foreach (var extra in new[] { "", "-wal", "-shm", "-journal" })
        {
            var p = path + extra;
            if (File.Exists(p))
                File.Delete(p);
        }
        return path;
    }

    private static void Begin(string dbName)
    {
        ProductService.TestBeforeApplyMergeCost = null;
        ProductService.TestAfterRemapProductIds = null;
        DatabaseService.Initialize(FreshDb(dbName));
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
    }

    [Fact]
    public void Migracao_Pre69TB_BackfillUnidadePackEVazio_Idempotente()
    {
        var path = FreshDb("migracao-pre-69tb.db");
        DatabaseService.Initialize(path);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");

        InsertLegacyProduct("A", "PROD A UNIT", "GERAL", "1111111111111", null, 1, cost: 1, sale: 2);
        InsertLegacyProduct("B", "PROD B PACK", "GERAL", "2222222222222", "3333333333333", 12, cost: 2, sale: 4);
        InsertLegacyProduct("C", "PROD C SEM EAN", "GERAL", null, null, 1, cost: 1, sale: 2);
        InsertLegacyProduct("D1", "PROD D1", "GERAL", "4444444444444", null, 1, cost: 1, sale: 2);
        InsertLegacyProduct("D2", "PROD D2", "GERAL", "5555555555555", null, 1, cost: 1, sale: 2);
        InsertLegacyProduct("E", "Rothmans Blue", "Cigarros", "8111111111111", "8222222222222", 20, cost: 10, sale: 12);

        DropProductBarcodesTable();
        Assert.False(TableExists("product_barcodes"));

        DatabaseService.Initialize(path);
        Assert.True(TableExists("product_barcodes"));

        var rows = ListBarcodeRows();
        Assert.DoesNotContain(rows, r => string.IsNullOrWhiteSpace(r.Barcode));
        Assert.Equal(rows.Count, rows.Select(r => r.Barcode).Distinct().Count());

        Assert.Contains(rows, r => r.Code == "A" && r.Barcode == "1111111111111" && r.Kind == ProductBarcodeKinds.Unit && r.PackFactor == 1);
        Assert.Contains(rows, r => r.Code == "B" && r.Barcode == "2222222222222" && r.Kind == ProductBarcodeKinds.Unit);
        Assert.Contains(rows, r => r.Code == "B" && r.Barcode == "3333333333333" && r.Kind == ProductBarcodeKinds.Pack && r.PackFactor == 12);
        Assert.DoesNotContain(rows, r => r.Code == "C");
        Assert.Contains(rows, r => r.Code == "D1" && r.Barcode == "4444444444444");
        Assert.Contains(rows, r => r.Code == "D2" && r.Barcode == "5555555555555");
        Assert.Contains(rows, r => r.Code == "E" && r.Barcode == "8111111111111" && r.Kind == ProductBarcodeKinds.Unit);
        Assert.Contains(rows, r => r.Code == "E" && r.Barcode == "8222222222222" && r.Kind == ProductBarcodeKinds.Pack && r.PackFactor == 20);

        var before = SnapshotBarcodes();
        DatabaseService.Initialize(path);
        var after = SnapshotBarcodes();
        Assert.Equal(before, after);
    }

    [Fact]
    public void Merge_CasoPrincipal_EstoqueCustoAliasesNfPdvPack()
    {
        Begin("merge-principal.db");
        var keep = Seed("K", "ORIGINAL GARRAFA VIDRO 300ML", 152, 2.72, 4.50, EanA, PackA, 23);
        var absorb = Seed("X", "ORIGINAL 300ML DUPLICADA", 20, 3.00, 4.90, EanB, PackB, 23);

        var expectedCost = ProductPriceHelper.WeightedAverageCost(152, 2.72, 20, 3.00);
        Assert.Equal(2.75, expectedCost, 2);

        var merged = ProductService.MergeProducts(keep, absorb);
        Assert.Equal(172, merged.Stock, 4);
        Assert.Equal(0, merged.StockFridge, 4);
        Assert.Equal(expectedCost, merged.CostPrice, 2);
        Assert.True(merged.Active);

        var abs = ProductService.GetById(absorb)!;
        Assert.False(abs.Active);
        Assert.Equal(0, abs.Stock, 4);
        Assert.Equal(0, abs.StockFridge, 4);

        Assert.Equal(keep, ProductService.FindByBarcodeOrPack(EanA)!.Id);
        Assert.Equal(keep, ProductService.FindByBarcodeOrPack(PackA)!.Id);
        Assert.Equal(keep, ProductService.FindByBarcodeOrPack(EanB)!.Id);
        Assert.Equal(keep, ProductService.FindByBarcodeOrPack(PackB)!.Id);
        Assert.Null(ProductService.GetById(absorb)!.Barcode);

        using var conn = DatabaseService.OpenConnection();
        Assert.Equal(ProductBarcodeKinds.Unit, ProductBarcodeService.FindKind(conn, null, keep, EanA));
        Assert.Equal(ProductBarcodeKinds.Pack, ProductBarcodeService.FindKind(conn, null, keep, PackA));
        Assert.Equal(ProductBarcodeKinds.Alias, ProductBarcodeService.FindKind(conn, null, keep, EanB));
        Assert.Equal(ProductBarcodeKinds.Pack, ProductBarcodeService.FindKind(conn, null, keep, PackB));
        Assert.Equal(23, PackFactor(conn, keep, PackA));
        Assert.Equal(23, PackFactor(conn, keep, PackB));
        var extra = ProductExtra.Parse(merged.ExtraJson);
        Assert.Equal(PackA, TextNorm.NormalizeBarcode(extra.BarcodeEmbalagem));

        var nf = NfeXmlImportService.ResolveExistingProduct(new NfeImportItem
        {
            Barcode = EanB,
            Name = "PRODUTO INEXISTENTE XYZ 999",
        });
        Assert.NotNull(nf);
        Assert.Equal(keep, nf!.Id);
        Assert.False(nf.Id == absorb);

        Assert.True(PdvBarcodeTerm.LooksLike(EanB));
        var pdv = PdvService.ResolveExactBarcode(EanB);
        Assert.NotNull(pdv);
        Assert.Equal(keep, pdv!.Product.Id);
        Assert.False(pdv.IsPackSale);
        Assert.Equal(1, pdv.Quantity);

        var pdvPackB = PdvService.ResolveExactBarcode(PackB);
        Assert.NotNull(pdvPackB);
        Assert.Equal(keep, pdvPackB!.Product.Id);
        Assert.True(pdvPackB.IsPackSale);

        var pdvPackA = PdvService.ResolveExactBarcode(PackA);
        Assert.NotNull(pdvPackA);
        Assert.Equal(keep, pdvPackA!.Product.Id);
        Assert.True(pdvPackA.IsPackSale);
    }

    [Fact]
    public void Merge_ConflitoComTerceiro_BloqueiaERollbackCompleto()
    {
        Begin("merge-conflito.db");
        var keep = Seed("KA", "KEEP A", 10, 2, 4, EanA, PackA, 12);
        var absorb = Seed("KB", "ABSORB B", 5, 3, 5, EanB, PackB, 12);
        var third = Seed("KC", "TERCEIRO C", 7, 1.5, 3, EanC);
        using (var conn = DatabaseService.OpenConnection())
            ProductBarcodeService.Upsert(conn, null, third, EanB, ProductBarcodeKinds.Alias, 1, "c1-conflict");

        var before = SnapshotProducts(keep, absorb, third);
        var beforeBc = SnapshotBarcodes();

        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keep, absorb));
        Assert.Contains("já está em outro produto", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(before, SnapshotProducts(keep, absorb, third));
        Assert.Equal(beforeBc, SnapshotBarcodes());
        Assert.True(ProductService.GetById(keep)!.Active);
        Assert.True(ProductService.GetById(absorb)!.Active);
        Assert.True(ProductService.GetById(third)!.Active);
    }

    [Fact]
    public void Merge_DepositoMaisGeladeira_MediaFisica()
    {
        Begin("merge-geladeira.db");
        var keep = Seed("G1", "KEEP FRIDGE", 100, 2.00, 5, EanA, fridge: 20);
        var absorb = Seed("G2", "ABS FRIDGE", 30, 4.00, 6, EanB, fridge: 10);
        var expected = ProductMergeRules.WeightedPhysicalAverage(100, 20, 2.00, 30, 10, 4.00, false, 1);

        var m = ProductService.MergeProducts(keep, absorb);
        Assert.Equal(130, m.Stock, 4);
        Assert.Equal(30, m.StockFridge, 4);
        Assert.Equal(expected, m.CostPrice, 2);
        Assert.Equal(2.50, m.CostPrice, 2);

        var abs = ProductService.GetById(absorb)!;
        Assert.False(abs.Active);
        Assert.Equal(0, abs.Stock, 4);
        Assert.Equal(0, abs.StockFridge, 4);
    }

    [Fact]
    public void Merge_HistoricoLotesMovimentosInventarioRemapeados()
    {
        Begin("merge-historico.db");
        CashService.OpenSession(50, "c1-hist");
        var supplier = SeedSupplier();
        var keep = Seed("H1", "KEEP LOT", 0, 2, 8, EanA);
        var absorb = Seed("H2", "ABS LOT", 0, 2, 8, EanB);
        var purchaseId = CreateClosed(supplier, absorb, 10, 3, "NF-C1", "L-C1", DateTime.Today.AddDays(80));
        var sale = TestDataHelper.FinalizeSimpleCashSale(absorb, 1, 8, 8);
        var tab = OpenTabService.Create("Comanda C1");
        OpenTabService.AddProduct(tab, absorb, 1);

        using (var conn = DatabaseService.OpenConnection())
        {
            Exec(conn, """
                INSERT INTO movements (product_id, movement_type, quantity, unit_price, notes)
                VALUES ($p, 'ajuste', 2, 2, 'c1-hist');
                """, ("$p", absorb));
            Exec(conn, """
                INSERT INTO inventory_sessions (status, notes, closed_at)
                VALUES ('fechada', 'c1', datetime('now','localtime'));
                """);
            var sessionId = Convert.ToInt32(Scalar(conn, "SELECT id FROM inventory_sessions ORDER BY id DESC LIMIT 1;"));
            Exec(conn, """
                INSERT INTO inventory_items (session_id, product_id, theoretical_qty, counted_qty)
                VALUES ($s, $p, 4, 4);
                """, ("$s", sessionId), ("$p", absorb));
        }

        ProductService.MergeProducts(keep, absorb);

        using var after = DatabaseService.OpenConnection();
        Assert.Equal(keep, Convert.ToInt32(Scalar(after, "SELECT product_id FROM purchase_items WHERE purchase_id=$p;", ("$p", purchaseId))));
        Assert.Equal(keep, Convert.ToInt32(Scalar(after, "SELECT product_id FROM purchase_item_lots WHERE purchase_id=$p;", ("$p", purchaseId))));
        Assert.Equal(keep, Convert.ToInt32(Scalar(after, "SELECT product_id FROM sale_items WHERE sale_id=$p;", ("$p", sale.SaleId))));
        Assert.Equal(keep, Convert.ToInt32(Scalar(after, "SELECT product_id FROM movements WHERE notes='c1-hist';")));
        Assert.Equal(keep, Convert.ToInt32(Scalar(after, "SELECT product_id FROM product_lots WHERE lot_number='L-C1';")));
        Assert.Equal(keep, Convert.ToInt32(Scalar(after, "SELECT product_id FROM inventory_items LIMIT 1;")));
        Assert.Equal(keep, Assert.Single(OpenTabService.Get(tab).Items).ProductId);
        Assert.Equal(0, Convert.ToInt32(Scalar(after, "SELECT COUNT(*) FROM purchase_items WHERE product_id=$p;", ("$p", absorb))));
    }

    [Fact]
    public void Merge_Kits_PreservaOuBloqueia()
    {
        Begin("merge-kits.db");
        var child = Seed("CH", "FILHO KIT", 0, 1, 2, "9000000000001");

        var keepA = Seed("KA", "KIT KEEP VAZIO", 0, 1, 2, "9111111111111");
        var absA = Seed("AA", "KIT ABS COMP", 0, 1, 2, "9222222222222");
        SetComposition(absA, child);
        var mergedA = ProductService.MergeProducts(keepA, absA);
        Assert.NotEmpty(ProductExtra.Parse(mergedA.ExtraJson).ComposicaoItens);
        Assert.Equal(child, ProductExtra.Parse(mergedA.ExtraJson).ComposicaoItens[0].ProductId);

        var keepB = Seed("KB", "KIT KEEP COMP", 0, 1, 2, "9333333333333");
        var absB = Seed("AB", "KIT ABS VAZIO", 0, 1, 2, "9444444444444");
        SetComposition(keepB, child);
        var mergedB = ProductService.MergeProducts(keepB, absB);
        Assert.Equal(child, ProductExtra.Parse(mergedB.ExtraJson).ComposicaoItens[0].ProductId);

        var keepC = Seed("KC", "KIT KEEP AMBOS", 0, 1, 2, "9555555555555");
        var absC = Seed("AC", "KIT ABS AMBOS", 0, 1, 2, "9666666666666");
        SetComposition(keepC, child);
        SetComposition(absC, child);
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keepC, absC));
        Assert.Equal(ProductMergeRules.ConflictingCompositionMessage, ex.Message);
        Assert.True(ProductService.GetById(absC)!.Active);
        Assert.NotEmpty(ProductExtra.Parse(ProductService.GetById(keepC)!.ExtraJson).ComposicaoItens);
        Assert.NotEmpty(ProductExtra.Parse(ProductService.GetById(absC)!.ExtraJson).ComposicaoItens);
    }

    [Fact]
    public void Merge_Cigarro_FatorCompativelPreservaEan_IncompativelBloqueia()
    {
        Begin("merge-cigarro.db");
        var keepOk = SeedCig("C1", 200, 10, 20, "8111111111111", "8211111111111");
        var absOk = SeedCig("C2", 100, 12, 20, "8122222222222", "8222222222222");
        var merged = ProductService.MergeProducts(keepOk, absOk);
        Assert.Equal(300, merged.Stock, 4);
        Assert.Equal(keepOk, ProductService.FindByBarcodeOrPack("8122222222222")!.Id);
        Assert.Equal(keepOk, ProductService.FindByBarcodeOrPack("8222222222222")!.Id);
        using (var conn = DatabaseService.OpenConnection())
        {
            Assert.Equal(ProductBarcodeKinds.Alias, ProductBarcodeService.FindKind(conn, null, keepOk, "8122222222222"));
            Assert.Equal(ProductBarcodeKinds.Pack, ProductBarcodeService.FindKind(conn, null, keepOk, "8222222222222"));
            Assert.Equal(20, PackFactor(conn, keepOk, "8222222222222"));
        }
        var pdvUnit = PdvService.ResolveExactBarcode("8122222222222", PdvCigaretteSaleMode.Avulso);
        Assert.NotNull(pdvUnit);
        Assert.Equal(keepOk, pdvUnit!.Product.Id);
        Assert.False(pdvUnit.IsPackSale);
        var pdvPack = PdvService.ResolveExactBarcode("8222222222222", PdvCigaretteSaleMode.Maco);
        Assert.NotNull(pdvPack);
        Assert.Equal(keepOk, pdvPack!.Product.Id);
        Assert.True(pdvPack.IsPackSale);

        var keepBad = SeedCig("C3", 200, 10, 20, "8133333333333", "8233333333333");
        var absBad = SeedCig("C4", 100, 12, 10, "8144444444444", "8244444444444");
        var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keepBad, absBad));
        Assert.Equal(ProductMergeRules.DifferentCigaretteFactorMessage, ex.Message);
        Assert.True(ProductService.GetById(absBad)!.Active);
        Assert.Equal(absBad, ProductService.FindByBarcodeOrPack("8144444444444")!.Id);
    }

    [Fact]
    public void Merge_Audit_ContemKeepAbsorbEstoqueCustoAliases()
    {
        Begin("merge-audit.db");
        var keep = Seed("AU", "AUD KEEP", 152, 2.72, 4, EanA, PackA, 23);
        var absorb = Seed("AB", "AUD ABS", 20, 3.00, 5, EanB, PackB, 23);
        ProductService.MergeProducts(keep, absorb);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT details FROM audit_log
            WHERE action = 'unificar' AND entity = 'produto'
            ORDER BY id DESC LIMIT 1;
            """;
        var details = cmd.ExecuteScalar()?.ToString() ?? "";
        Assert.True(AuditPayloadBuilder.TryParse(details, out var doc));
        Assert.Equal(keep, doc.Payload.GetProperty("keep_id").GetInt32());
        Assert.Equal(absorb, doc.Payload.GetProperty("absorb_id").GetInt32());
        Assert.Equal(152, doc.Payload.GetProperty("stock_keep_before").GetDouble(), 4);
        Assert.Equal(20, doc.Payload.GetProperty("stock_absorb_before").GetDouble(), 4);
        Assert.Equal(172, doc.Payload.GetProperty("stock_after").GetDouble(), 4);
        Assert.Equal(2.72, doc.Payload.GetProperty("cost_keep_before").GetDouble(), 2);
        Assert.Equal(3.00, doc.Payload.GetProperty("cost_absorb_before").GetDouble(), 2);
        Assert.Equal(2.75, doc.Payload.GetProperty("cost_after").GetDouble(), 2);
        Assert.True(doc.Payload.TryGetProperty("aliases_moved", out var aliases));
        Assert.Contains(EanB, aliases.ToString(), StringComparison.Ordinal);
        Assert.Contains(PackB, aliases.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Capability_V3_HostAntigoNaoExecutaMerge()
    {
        Begin("merge-capability.db");
        var keep = Seed("CK", "KEEP CAP", 10, 2, 4, EanA);
        var absorb = Seed("CA", "ABS CAP", 5, 3, 5, EanB);
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        StoreNetworkClient.TestStatusFeatures =
        [
            "session", "pairing",
            PurchaseSalePriceRules.AtomicFeature,
            PurchaseAverageCostRules.AtomicFeature,
            PurchaseCancelCostRules.AtomicFeature,
        ];
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ProductService.MergeProducts(keep, absorb));
            Assert.Equal(ProductMergeRules.HostNeedsUpgradeBeforeMergeMessage, ex.Message);
            Assert.Equal(0, StoreNetworkClient.TestMergeSendCount);
        }
        finally
        {
            StoreNetworkClient.ResetPurchaseSalePriceTestHooks();
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
        Assert.True(ProductService.GetById(absorb)!.Active);
        Assert.Equal(10, ProductService.GetById(keep)!.Stock, 4);
        Assert.Contains(ProductMergeRules.AtomicFeature, StoreNetworkHost.AdvertisedFeatures);
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
        SetStock(id, stock, fridge, cost);
        return id;
    }

    private static int SeedCig(string code, double stock, double cost, double fator, string barcode, string pack)
    {
        var extra = new ProductExtra
        {
            BarcodeEmbalagem = pack,
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAvulso = 1.5,
            PrecoAtacado = 10,
            PrecoCompra = cost,
        };
        var id = ProductService.Create(new ProductInput
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
        SetStock(id, stock, 0, cost);
        return id;
    }

    private static void SetStock(int id, double stock, double fridge, double cost)
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

    private static void InsertLegacyProduct(
        string code, string name, string group, string? barcode, string? pack, double fator, double cost, double sale)
    {
        var extra = new ProductExtra
        {
            BarcodeEmbalagem = pack,
            FatorEmbalagem = fator,
            QtdAtacado = fator > 1 ? fator : 0,
            PrecoCompra = cost,
        }.ToJson();
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (code, barcode, name, group_name, unit, cost_price, sale_price, stock, stock_fridge, extra_json, active)
            VALUES ($code, $bc, $name, $g, 'UN', $cost, $sale, 0, 0, $extra, 1);
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$bc", (object?)barcode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$g", group);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$sale", sale);
        cmd.Parameters.AddWithValue("$extra", extra);
        cmd.ExecuteNonQuery();
    }

    private static void DropProductBarcodesTable()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DROP INDEX IF EXISTS idx_product_barcodes_barcode;
            DROP INDEX IF EXISTS idx_product_barcodes_product;
            DROP INDEX IF EXISTS idx_product_barcodes_active;
            DROP TABLE IF EXISTS product_barcodes;
            """;
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(string name)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static List<(string Code, string Barcode, string Kind, double PackFactor)> ListBarcodeRows()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(p.code,''), pb.barcode, pb.kind, pb.pack_factor
            FROM product_barcodes pb
            JOIN products p ON p.id = pb.product_id
            ORDER BY p.code, pb.barcode;
            """;
        using var r = cmd.ExecuteReader();
        var rows = new List<(string, string, string, double)>();
        while (r.Read())
            rows.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetDouble(3)));
        return rows;
    }

    private static string SnapshotBarcodes()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT product_id, barcode, kind, ROUND(pack_factor,4), active
            FROM product_barcodes
            ORDER BY barcode, product_id;
            """;
        using var r = cmd.ExecuteReader();
        var parts = new List<string>();
        while (r.Read())
            parts.Add($"{r.GetInt32(0)}|{r.GetString(1)}|{r.GetString(2)}|{r.GetDouble(3)}|{r.GetInt32(4)}");
        return string.Join(";", parts);
    }

    private static string SnapshotProducts(params int[] ids)
    {
        using var conn = DatabaseService.OpenConnection();
        var parts = new List<string>();
        foreach (var id in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, IFNULL(barcode,''), stock, stock_fridge, cost_price, sale_price, active
                FROM products WHERE id=$id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            parts.Add($"{r.GetInt32(0)}|{r.GetString(1)}|{r.GetDouble(2):0.####}|{r.GetDouble(3):0.####}|{r.GetDouble(4):0.####}|{r.GetDouble(5):0.####}|{r.GetInt32(6)}");
        }
        return string.Join(";", parts);
    }

    private static double PackFactor(SqliteConnection conn, int productId, string barcode)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT pack_factor FROM product_barcodes
            WHERE product_id=$p AND barcode=$b AND active=1 LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.Parameters.AddWithValue("$b", barcode);
        return Convert.ToDouble(cmd.ExecuteScalar());
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
            VALUES ('fornecedor', 'juridica', 'FORN C1', 1, '{"ativo":true,"fornecedores":true}');
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

    private static void Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection conn, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteScalar();
    }
}
