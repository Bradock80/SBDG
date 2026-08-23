using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 61B/61D — Compra / lotes / estorno.
/// Os testes que documentavam o bug FEFO no cancelamento foram substituídos
/// pela expectativa correta da 61D (estorno exato da origem).
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseLotCancelCharacterizationTests
{
    private static readonly DateTime ExpiryNear = DateTime.Today.AddDays(30);
    private static readonly DateTime ExpiryFar = DateTime.Today.AddDays(200);

    [Fact]
    public void PurchaseWithoutLot_AddsGlobalAndMovement_CancelRestoresGlobal()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(100, 5, 2, "PWL", "SEM LOTE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SEM LOTE", qty: 20, lot: null, expiry: null);

        Assert.Equal(120, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, SumLots(productId));
        Assert.Equal(1, CountMovements(productId, "entrada_compra", purchaseId));

        PurchaseService.Cancel(purchaseId);

        Assert.Equal(100, TestDataHelper.GetProductStock(productId));
        Assert.Equal(1, CountMovements(productId, "estorno_compra", purchaseId));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
    }

    [Fact]
    public void PurchaseWithLot_CreatesProductLotAndMovement()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "PBL", "COM LOTE");

        CreateClosedPurchase(supplierId, productId, "COM LOTE", qty: 20, lot: "B", expiry: ExpiryFar);

        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal(20, GetLotQty(productId, "B"));
        Assert.Equal(1, CountLotRows(productId));
    }

    [Fact]
    public void Receive_CurrentBehavior_MergesSameLotAcrossPurchases_AndOverwritesPurchaseId()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MRG", "MERGE LOTE");

        var p1 = CreateClosedPurchase(supplierId, productId, "MERGE LOTE", 10, "ABC", ExpiryFar, number: "NF-1");
        var p2 = CreateClosedPurchase(supplierId, productId, "MERGE LOTE", 20, "ABC", ExpiryFar, number: "NF-2");

        Assert.Equal(30, TestDataHelper.GetProductStock(productId));
        Assert.Equal(1, CountLotRows(productId));
        Assert.Equal(30, GetLotQty(productId, "ABC"));
        // COALESCE(novo, antigo): purchase_id fica da segunda compra — não representa origem completa.
        Assert.Equal(p2, GetLotPurchaseId(productId, "ABC"));
        Assert.NotEqual(p1, p2);
    }

    [Fact]
    public void CancelPurchase_WithTrackedOrigin_DeductsOnlyOriginalLot()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        // Global alinhado ao lote A (Receive não altera products.stock).
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2, "FEFO", "FEFO ESTORNO");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 10,
            LotNumber = "A",
            ExpiryDate = ExpiryNear,
        });

        var purchaseB = CreateClosedPurchase(supplierId, productId, "FEFO ESTORNO", 20, "B", ExpiryFar, number: "NF-B");
        Assert.Equal(30, TestDataHelper.GetProductStock(productId));
        Assert.Equal(10, GetLotQty(productId, "A"));
        Assert.Equal(20, GetLotQty(productId, "B"));

        PurchaseService.Cancel(purchaseB);

        // 61D: estorno exato do lote B. A permanece 10 (bug 61B era A=0, B=10).
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(10, GetLotQty(productId, "A"));
        Assert.Equal(0, GetLotQty(productId, "B"));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseB));
    }

    [Fact]
    public void CancelPurchase_AfterSaleConsumesLotA_PreservesRemainingA_AndClearsB()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "61b-sale");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2, "VS", "VENDA MEIO");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 10,
            LotNumber = "A",
            ExpiryDate = ExpiryNear,
        });

        var purchaseB = CreateClosedPurchase(supplierId, productId, "VENDA MEIO", 20, "B", ExpiryFar, number: "NF-VS");
        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 5, unitPrice: 5, cashReceived: 25);
        Assert.Equal(5, GetLotQty(productId, "A"));
        Assert.Equal(20, GetLotQty(productId, "B"));
        Assert.Equal(25, TestDataHelper.GetProductStock(productId));

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseB));
        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage, ex.Message);

        Assert.Equal(25, TestDataHelper.GetProductStock(productId));
        Assert.Equal(5, GetLotQty(productId, "A"));
        Assert.Equal(20, GetLotQty(productId, "B"));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseB));
    }

    [Fact]
    public void CancelPurchase_WhenOwnLotPartiallySold_BlocksEntireCancel()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 80, notes: "61b-partial");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "PAR", "PARCIAL");

        var purchaseB = CreateClosedPurchase(supplierId, productId, "PARCIAL", 20, "B", ExpiryFar, number: "NF-P");
        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 8, unitPrice: 5, cashReceived: 40);
        Assert.Equal(12, TestDataHelper.GetProductStock(productId));
        Assert.Equal(12, GetLotQty(productId, "B"));

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseB));
        Assert.Contains("já foi vendida ou movimentada", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(12, TestDataHelper.GetProductStock(productId));
        Assert.Equal(12, GetLotQty(productId, "B"));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseB));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseB));
    }

    [Fact]
    public void CancelPurchase_WhenGlobalStockInsufficient_BlocksAndKeepsStock()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(100, 5, 2, "NEG", "NEG STOCK");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "NEG STOCK", 20, lot: null, expiry: null);
        Assert.Equal(120, TestDataHelper.GetProductStock(productId));
        SetStockDirect(productId, 7);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("estoque negativo", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(7, TestDataHelper.GetProductStock(productId));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void CancelPurchase_WithoutLotOrigin_DoesNotDeductUnrelatedLots()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "INS", "LOTES INSUF");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "LOTES INSUF", 20, lot: null, expiry: null);
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        // Receive não altera global — lote X não pertence a esta compra.
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 8,
            LotNumber = "X",
            ExpiryDate = ExpiryNear,
        });
        Assert.Equal(8, SumLots(productId));

        PurchaseService.Cancel(purchaseId);

        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(8, SumLots(productId)); // 61D: sem FEFO no estorno; X permanece
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
    }

    [Fact]
    public void Purchase_ExpiryWithoutLotNumber_CreatesLot_AndCancelUsesExactOrigin()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "EXP", "SO VALIDADE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SO VALIDADE", 15, lot: "", expiry: ExpiryFar);
        Assert.Equal(15, TestDataHelper.GetProductStock(productId));
        Assert.Equal(1, CountLotRows(productId));
        Assert.Equal(15, GetLotQty(productId, ""));

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, SumLots(productId));
    }

    [Fact]
    public void Purchase_LotWithoutExpiry_Created_AndFefoOrdersAfterDatedLots()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "61b-null-exp");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "NEX", "SEM VAL");

        CreateClosedPurchase(supplierId, productId, "SEM VAL", 10, "SEMVAL", expiry: null, number: "NF-NV");
        CreateClosedPurchase(supplierId, productId, "SEM VAL", 10, "COMVAL", ExpiryNear, number: "NF-CV");
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));

        // Venda FEFO: validade primeiro; sem validade por último.
        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 5, unitPrice: 5, cashReceived: 25);
        Assert.Equal(5, GetLotQty(productId, "COMVAL"));
        Assert.Equal(10, GetLotQty(productId, "SEMVAL"));
    }

    [Fact]
    public void CancelPurchase_Twice_DoesNotDoubleReverseStock()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(50, 5, 2, "DUP", "DUPLO CANCEL");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "DUPLO CANCEL", 10, lot: null, expiry: null);
        Assert.Equal(60, TestDataHelper.GetProductStock(productId));

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(50, TestDataHelper.GetProductStock(productId));
        var movAfterFirst = CountMovements(productId, "estorno_compra", purchaseId);

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(50, TestDataHelper.GetProductStock(productId));
        Assert.Equal(movAfterFirst, CountMovements(productId, "estorno_compra", purchaseId));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
    }

    [Fact]
    public void CancelOpenPurchase_DoesNotReverseStock()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(40, 5, 2, "OPN", "ABERTA");

        var purchaseId = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-OPEN",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = productId,
                    ProductName = "ABERTA",
                    Quantity = 12,
                    UnitPrice = 2,
                },
            ],
        }, closeOnSave: false);

        Assert.Equal("aberta", GetPurchaseStatus(purchaseId));
        Assert.Equal(40, TestDataHelper.GetProductStock(productId));

        PurchaseService.Cancel(purchaseId);

        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
        Assert.Equal(40, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void PurchaseItems_CurrentBehavior_DoNotPersistLotOrExpiry()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "ITM", "ITEM SEM LOTE COL");

        CreateClosedPurchase(supplierId, productId, "ITEM SEM LOTE COL", 5, "L1", ExpiryFar);

        var cols = GetPurchaseItemColumns();
        Assert.DoesNotContain("lot_number", cols);
        Assert.DoesNotContain("expiry_date", cols);
        Assert.DoesNotContain("product_lot_id", cols);

        // Lote existe em product_lots, mas purchase_items só tem qty/preço.
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id, quantity, unit_price FROM purchase_items LIMIT 1;";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(productId, reader.GetInt32(0));
        Assert.Equal(5, reader.GetDouble(1));
    }

    [Fact]
    public void PurchaseMovement_LinksRefTypePurchaseAndOperation()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2, "MOV", "MOV REF");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "MOV REF", 7, lot: null, expiry: null);
        var entrada = GetLatestMovement(productId, "entrada_compra", purchaseId);
        Assert.Equal(10, entrada.Before);
        Assert.Equal(17, entrada.After);
        Assert.Equal(7, entrada.Qty);
        Assert.Equal("purchase", entrada.RefType);
        Assert.Equal(purchaseId, entrada.RefId);

        PurchaseService.Cancel(purchaseId);
        var estorno = GetLatestMovement(productId, "estorno_compra", purchaseId);
        Assert.Equal(17, estorno.Before);
        Assert.Equal(10, estorno.After);
        Assert.Equal("saida", estorno.Type);
    }

    [Fact]
    public void Purchase_CigarettePhysicalQty_IsUsedAsProvidedByCaller()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var cigId = SeedCigarette(stock: 0, fator: 20);
        // PurchaseService não converte: qty 40 física → stock +40.
        CreateClosedPurchase(supplierId, cigId, "CIGARRO 61B", qty: 40, lot: null, expiry: null, number: "NF-CIG");
        Assert.Equal(40, TestDataHelper.GetProductStock(cigId));
    }

    [Fact]
    public void Payable_ClosedPurchase_CreatesTitle_CancelRemovesWhenUnpaid()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "PAY", "PAGAR");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "PAGAR", 10, lot: null, expiry: null, number: "NF-PAY");
        var titles = PayableService.ListTitlesLocal(purchaseId: purchaseId);
        Assert.NotEmpty(titles);

        PurchaseService.Cancel(purchaseId);
        Assert.Empty(PayableService.ListTitlesLocal(purchaseId: purchaseId));
    }

    [Fact]
    public void Payable_PaidInstallment_BlocksCancel()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "PAID", "PAGO");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "PAGO", 10, lot: null, expiry: null, unitPrice: 5, number: "NF-PAID");
        var inst = Assert.Single(PayableService.ListInstallmentsLocal(purchaseId: purchaseId));
        PayableService.PayInstallment(inst.Id, new PayablePayInput
        {
            PaidAmount = inst.Amount,
            PaidDate = DateTime.Today.ToString("dd/MM/yyyy"),
            PaymentType = "Dinheiro",
        });

        var ex = Assert.Throws<PayableException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("parcela paga", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
    }

    // --- helpers ---

    private static int CreateClosedPurchase(
        int supplierId,
        int productId,
        string name,
        double qty,
        string? lot,
        DateTime? expiry,
        string number = "NF-61B",
        double unitPrice = 2)
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
                    UnitPrice = unitPrice,
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
            VALUES ('fornecedor', 'juridica', 'FORN 61B', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCigarette(double stock, double fator)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            PrecoAvulso = 1.5,
            PrecoAtacado = 28.5,
            QtdAtacado = fator,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIG61B', 'CIGARRO 61B', 'CIGARROS', 'UN', 28.5, $stock, 20, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string GetPurchaseStatus(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static double SumLots(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(SUM(quantity),0) FROM product_lots WHERE product_id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static double GetLotQty(int productId, string lotNumber)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(quantity),0) FROM product_lots
            WHERE product_id = $id AND IFNULL(lot_number,'') = $lot;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.Parameters.AddWithValue("$lot", lotNumber);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static int CountLotRows(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM product_lots WHERE product_id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int? GetLotPurchaseId(int productId, string lotNumber)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT purchase_id FROM product_lots
            WHERE product_id = $id AND IFNULL(lot_number,'') = $lot
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.Parameters.AddWithValue("$lot", lotNumber);
        var o = cmd.ExecuteScalar();
        return o is null or DBNull ? null : Convert.ToInt32(o);
    }

    private static int CountMovements(int productId, string operation, int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE product_id = $pid
              AND IFNULL(operation,'') = $op
              AND IFNULL(ref_type,'') = 'purchase'
              AND IFNULL(ref_id,0) = $rid;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$op", operation);
        cmd.Parameters.AddWithValue("$rid", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static MovRow GetLatestMovement(int productId, string operation, int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT movement_type, quantity, IFNULL(stock_before,0), IFNULL(stock_after,0),
                   IFNULL(ref_type,''), IFNULL(ref_id,0)
            FROM movements
            WHERE product_id = $pid
              AND IFNULL(operation,'') = $op
              AND IFNULL(ref_type,'') = 'purchase'
              AND IFNULL(ref_id,0) = $rid
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$op", operation);
        cmd.Parameters.AddWithValue("$rid", purchaseId);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        return new MovRow(
            reader.GetString(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetString(4),
            reader.GetInt32(5));
    }

    private static void SetStockDirect(int productId, double stock)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock = $stock WHERE id = $id;";
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static HashSet<string> GetPurchaseItemColumns()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(purchase_items);";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(1));
        return set;
    }

    private sealed record MovRow(
        string Type, double Qty, double Before, double After, string RefType, int RefId);
}
