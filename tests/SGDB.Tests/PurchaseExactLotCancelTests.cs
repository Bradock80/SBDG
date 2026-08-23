using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 61D — Estorno exato dos lotes da compra (origem em purchase_item_lots).
/// Não usa FEFO no cancelamento rastreado. Compra legada (lot_origin_recorded=0) é bloqueada.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseExactLotCancelTests
{
    private static readonly DateTime ExpiryNear = DateTime.Today.AddDays(30);
    private static readonly DateTime ExpiryFar = DateTime.Today.AddDays(200);

    [Fact]
    public void Schema_Purchases_HasLotOriginRecorded_DefaultZeroUntilClose()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        using (var conn = DatabaseService.OpenConnection())
        {
            var cols = GetColumns(conn, "purchases");
            Assert.Contains("lot_origin_recorded", cols);
        }

        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MK", "MARCADOR");
        var openId = PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-OPEN-MK",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = productId,
                    ProductName = "MARCADOR",
                    Quantity = 3,
                    UnitPrice = 2,
                },
            ],
        }, closeOnSave: false);
        Assert.Equal(0, GetLotOriginRecorded(openId));

        var closedId = CreateClosedPurchase(supplierId, productId, "MARCADOR", 4, "L", ExpiryFar, number: "NF-CL-MK");
        Assert.Equal(1, GetLotOriginRecorded(closedId));
    }

    [Fact]
    public void Schema_BancoAntigoSemColuna_MigrationAdicionaLotOriginRecorded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SGDB.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "old.db");
        try
        {
            DatabaseService.Initialize(path);
            using (var conn = DatabaseService.OpenConnection())
            using (var drop = conn.CreateCommand())
            {
                drop.CommandText = "ALTER TABLE purchases DROP COLUMN lot_origin_recorded;";
                drop.ExecuteNonQuery();
                Assert.DoesNotContain("lot_origin_recorded", GetColumns(conn, "purchases"));
            }

            DatabaseService.Initialize(path);
            using var opened = DatabaseService.OpenConnection();
            Assert.Contains("lot_origin_recorded", GetColumns(opened, "purchases"));
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void CancelPurchase_WithLot_DeductsExactOrigin()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "EX", "EXATO");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "EXATO", 20, "B", ExpiryFar);
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal(20, GetLotQty(productId, "B"));

        PurchaseService.Cancel(purchaseId);

        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetLotQty(productId, "B"));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
        Assert.Equal(1, CountMovements(productId, "estorno_compra", purchaseId));
        var mov = GetLatestMovement(productId, "estorno_compra", purchaseId);
        Assert.Equal(20, mov.Qty);
        Assert.Equal(20, mov.Before);
        Assert.Equal(0, mov.After);
        Assert.Equal("purchase", mov.RefType);
        Assert.Equal(purchaseId, mov.RefId);
    }

    [Fact]
    public void CancelPurchase_TrackedOrigin_DeductsOnlyOriginalLot_PreservesOtherLot()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2, "CRIT", "CRITICO 61D");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 10,
            LotNumber = "A",
            ExpiryDate = ExpiryNear,
        });

        var purchaseB = CreateClosedPurchase(supplierId, productId, "CRITICO 61D", 20, "B", ExpiryFar, number: "NF-B");
        Assert.Equal(30, TestDataHelper.GetProductStock(productId));
        Assert.Equal(10, GetLotQty(productId, "A"));
        Assert.Equal(20, GetLotQty(productId, "B"));

        PurchaseService.Cancel(purchaseB);

        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(10, GetLotQty(productId, "A"));
        Assert.Equal(0, GetLotQty(productId, "B"));
    }

    [Fact]
    public void CancelPurchase_AfterFefoSaleOnA_PreservesRemainingA_ClearsB()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "61d-sale-a");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2, "SA", "SALE A");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 10,
            LotNumber = "A",
            ExpiryDate = ExpiryNear,
        });

        var purchaseB = CreateClosedPurchase(supplierId, productId, "SALE A", 20, "B", ExpiryFar, number: "NF-SA");
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
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseB));
    }

    [Fact]
    public void CancelPurchase_WhenOwnLotPartiallyConsumed_BlocksEntireOperation()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 80, notes: "61d-own");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "OWN", "OWN LOT");

        var purchaseB = CreateClosedPurchase(supplierId, productId, "OWN LOT", 20, "B", ExpiryFar, number: "NF-OWN");
        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 8, unitPrice: 5, cashReceived: 40);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseB));
        Assert.Contains("já foi vendida ou movimentada", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(12, TestDataHelper.GetProductStock(productId));
        Assert.Equal(12, GetLotQty(productId, "B"));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseB));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseB));
    }

    [Fact]
    public void TwoPurchasesSameLot_CancelSecond_LeavesFirstQuantity()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "ABC", "MESMO LOTE");

        var c1 = CreateClosedPurchase(supplierId, productId, "MESMO LOTE", 10, "ABC", ExpiryFar, number: "NF-C1");
        var c2 = CreateClosedPurchase(supplierId, productId, "MESMO LOTE", 20, "ABC", ExpiryFar, number: "NF-C2");
        Assert.Equal(30, GetLotQty(productId, "ABC"));

        PurchaseService.Cancel(c2);

        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(10, GetLotQty(productId, "ABC"));
        Assert.Equal("fechada", GetPurchaseStatus(c1));
        Assert.Equal("cancelada", GetPurchaseStatus(c2));
    }

    [Fact]
    public void TwoPurchasesSameLot_CancelFirstAfterSecond_GoesToZero()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "ABC2", "MESMO LOTE 2");

        var c1 = CreateClosedPurchase(supplierId, productId, "MESMO LOTE 2", 10, "ABC", ExpiryFar, number: "NF-C1b");
        var c2 = CreateClosedPurchase(supplierId, productId, "MESMO LOTE 2", 20, "ABC", ExpiryFar, number: "NF-C2b");

        PurchaseService.Cancel(c2);
        Assert.Equal(10, GetLotQty(productId, "ABC"));

        PurchaseService.Cancel(c1);
        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetLotQty(productId, "ABC"));
        Assert.Equal("cancelada", GetPurchaseStatus(c1));
    }

    [Fact]
    public void CancelPurchase_GlobalStockInsufficient_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "GLO", "GLOBAL");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "GLOBAL", 20, "B", ExpiryFar);
        SetStockDirect(productId, 7);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("estoque negativo", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(7, TestDataHelper.GetProductStock(productId));
        Assert.Equal(20, GetLotQty(productId, "B"));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void CancelPurchase_MissingLot_BlocksWithoutGuessing()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MISS", "LOTE SUMIU");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "LOTE SUMIU", 15, "Z", ExpiryFar);
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM product_lots WHERE product_id = $id;";
            cmd.Parameters.AddWithValue("$id", productId);
            cmd.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("não foi encontrado", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(15, TestDataHelper.GetProductStock(productId));
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void CancelPurchase_NullProductLotId_UniqueKeyFallbackSucceeds()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "FB", "FALLBACK");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "FALLBACK", 12, "FB1", ExpiryFar);
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE purchase_item_lots SET product_lot_id = NULL WHERE purchase_id = $id;";
            cmd.Parameters.AddWithValue("$id", purchaseId);
            cmd.ExecuteNonQuery();
        }

        PurchaseService.Cancel(purchaseId);

        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetLotQty(productId, "FB1"));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
    }

    [Fact]
    public void CancelPurchase_AmbiguousLotKey_Blocks()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "AMB", "AMBIGUO");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "AMBIGUO", 10, "DUP", ExpiryFar);
        using (var conn = DatabaseService.OpenConnection())
        using (var tx = conn.BeginTransaction())
        {
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = """
                    INSERT INTO product_lots (product_id, lot_number, expiry_date, quantity)
                    VALUES ($pid, 'DUP', $exp, 3);
                    """;
                ins.Parameters.AddWithValue("$pid", productId);
                ins.Parameters.AddWithValue("$exp", ExpiryFar.ToString("yyyy-MM-dd"));
                ins.ExecuteNonQuery();
            }
            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE purchase_item_lots SET product_lot_id = NULL WHERE purchase_id = $id;";
                upd.Parameters.AddWithValue("$id", purchaseId);
                upd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("não foi encontrado com segurança", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void CancelPurchase_ExpiryWithoutLotNumber_UsesExactOrigin()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "EXP", "SO VALIDADE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SO VALIDADE", 15, "", ExpiryFar);
        Assert.Equal(15, GetLotQty(productId, ""));

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetLotQty(productId, ""));
    }

    [Fact]
    public void CancelPurchase_LotWithoutExpiry_UsesExactOrigin()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "NEX", "SEM VAL");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SEM VAL", 9, "SEMVAL", expiry: null);
        Assert.Equal(9, GetLotQty(productId, "SEMVAL"));

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, GetLotQty(productId, "SEMVAL"));
    }

    [Fact]
    public void CancelPurchase_CigarettePhysicalQty_ReversesFortyUnits()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var cigId = SeedCigarette(stock: 0, fator: 20);

        var purchaseId = CreateClosedPurchase(supplierId, cigId, "CIGARRO 61D", 40, "CIGL", ExpiryFar, number: "NF-CIG");
        Assert.Equal(40, TestDataHelper.GetProductStock(cigId));
        Assert.Equal(40, GetLotQty(cigId, "CIGL"));

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(0, TestDataHelper.GetProductStock(cigId));
        Assert.Equal(0, GetLotQty(cigId, "CIGL"));
        var mov = GetLatestMovement(cigId, "estorno_compra", purchaseId);
        Assert.Equal(40, mov.Qty);
    }

    [Fact]
    public void CancelPurchase_WithoutLot_ReversesGlobalOnly()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(50, 5, 2, "NL", "SEM LOTE");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "SEM LOTE", 20, lot: null, expiry: null);
        Assert.Empty(PurchaseService.ListPurchaseItemLots(purchaseId));
        Assert.Equal(1, GetLotOriginRecorded(purchaseId));

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(50, TestDataHelper.GetProductStock(productId));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
    }

    [Fact]
    public void CancelPurchase_MixedItems_AllOrNothingSuccess()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var a = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MXA", "A LOTE");
        var b = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MXB", "B SEM");
        var c = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MXC", "C LOTE");

        var purchaseId = CreateMixedPurchase(supplierId, a, b, c);
        Assert.Equal(10, GetLotQty(a, "LA"));
        Assert.Equal(0, CountLotRows(b));
        Assert.Equal(10, GetLotQty(c, "LC"));

        PurchaseService.Cancel(purchaseId);

        Assert.Equal(0, TestDataHelper.GetProductStock(a));
        Assert.Equal(0, TestDataHelper.GetProductStock(b));
        Assert.Equal(0, TestDataHelper.GetProductStock(c));
        Assert.Equal(0, GetLotQty(a, "LA"));
        Assert.Equal(0, GetLotQty(c, "LC"));
        Assert.Equal("cancelada", GetPurchaseStatus(purchaseId));
    }

    [Fact]
    public void CancelPurchase_MixedItems_FailureOnOne_RollsBackAll()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 80, notes: "61d-mix");
        var supplierId = SeedSupplier();
        var a = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MXA2", "A LOTE");
        var b = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MXB2", "B SEM");
        var c = TestDataHelper.SeedSimpleProduct(0, 5, 2, "MXC2", "C LOTE");

        var purchaseId = CreateMixedPurchase(supplierId, a, b, c, number: "NF-MIX-F");
        TestDataHelper.FinalizeSimpleCashSale(c, qty: 5, unitPrice: 5, cashReceived: 25);

        var titlesBefore = PayableService.ListTitlesLocal(purchaseId: purchaseId).Count;
        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("já foi vendida ou movimentada", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(10, TestDataHelper.GetProductStock(a));
        Assert.Equal(10, TestDataHelper.GetProductStock(b));
        Assert.Equal(5, TestDataHelper.GetProductStock(c));
        Assert.Equal(10, GetLotQty(a, "LA"));
        Assert.Equal(5, GetLotQty(c, "LC"));
        Assert.Equal(0, CountMovements(a, "estorno_compra", purchaseId));
        Assert.Equal(0, CountMovements(b, "estorno_compra", purchaseId));
        Assert.Equal(0, CountMovements(c, "estorno_compra", purchaseId));
        Assert.Equal(titlesBefore, PayableService.ListTitlesLocal(purchaseId: purchaseId).Count);
    }

    [Fact]
    public void CancelPurchase_Twice_DoesNotDoubleReverse()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(5, 5, 2, "DUP", "DUPLO");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "DUPLO", 10, "D", ExpiryFar);
        PurchaseService.Cancel(purchaseId);
        Assert.Equal(5, TestDataHelper.GetProductStock(productId));
        var mov = CountMovements(productId, "estorno_compra", purchaseId);

        PurchaseService.Cancel(purchaseId);
        Assert.Equal(5, TestDataHelper.GetProductStock(productId));
        Assert.Equal(mov, CountMovements(productId, "estorno_compra", purchaseId));
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
                    LotNumber = "OPENL",
                    ExpiryDate = ExpiryFar,
                },
            ],
        }, closeOnSave: false);

        Assert.Equal(40, TestDataHelper.GetProductStock(productId));
        PurchaseService.Cancel(purchaseId);
        Assert.Equal(40, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
        Assert.Equal(0, CountLotRows(productId));
    }

    [Fact]
    public void Payable_PaidInstallment_BlocksBeforeStockChanges()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "PAID", "PAGO");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "PAGO", 10, "P", ExpiryFar, unitPrice: 5, number: "NF-PAID");
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
        Assert.Equal(10, GetLotQty(productId, "P"));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void LegacyPurchase_WithoutLotOriginMarker_BlocksCancel()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "LEG", "LEGADO");

        var purchaseId = CreateClosedPurchase(supplierId, productId, "LEGADO", 11, "OLD", ExpiryFar);
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE purchases SET lot_origin_recorded = 0 WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", purchaseId);
            cmd.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("antes da rastreabilidade", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("fechada", GetPurchaseStatus(purchaseId));
        Assert.Equal(11, TestDataHelper.GetProductStock(productId));
        Assert.Equal(11, GetLotQty(productId, "OLD"));
        Assert.Equal(0, CountMovements(productId, "estorno_compra", purchaseId));
    }

    [Fact]
    public void Sale_StillUsesFefo_IndependentOfPurchaseCancel()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(openingAmount: 50, notes: "61d-fefo-sale");
        var supplierId = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 5, 2, "FEFO", "VENDA FEFO");

        CreateClosedPurchase(supplierId, productId, "VENDA FEFO", 10, "A", ExpiryNear, number: "NF-A");
        CreateClosedPurchase(supplierId, productId, "VENDA FEFO", 10, "B", ExpiryFar, number: "NF-B");

        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 5, unitPrice: 5, cashReceived: 25);
        Assert.Equal(5, GetLotQty(productId, "A"));
        Assert.Equal(10, GetLotQty(productId, "B"));
    }

    // --- helpers ---

    private static int CreateClosedPurchase(
        int supplierId,
        int productId,
        string name,
        double qty,
        string? lot,
        DateTime? expiry,
        string number = "NF-61D",
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

    private static int CreateMixedPurchase(int supplierId, int a, int b, int c, string number = "NF-MIX")
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
                    ProductId = a,
                    ProductName = "A LOTE",
                    Quantity = 10,
                    UnitPrice = 2,
                    LotNumber = "LA",
                    ExpiryDate = ExpiryFar,
                },
                new PurchaseItemInput
                {
                    ProductId = b,
                    ProductName = "B SEM",
                    Quantity = 10,
                    UnitPrice = 2,
                },
                new PurchaseItemInput
                {
                    ProductId = c,
                    ProductName = "C LOTE",
                    Quantity = 10,
                    UnitPrice = 2,
                    LotNumber = "LC",
                    ExpiryDate = ExpiryFar,
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
            VALUES ('fornecedor', 'juridica', 'FORN 61D', 1, '{"ativo":true,"fornecedores":true}');
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
                'CIG61D', 'CIGARRO 61D', 'CIGARROS', 'UN', 28.5, $stock, 20, 1, $extra
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

    private static int GetLotOriginRecorded(int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(lot_origin_recorded, 0) FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
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

    private static HashSet<string> GetColumns(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(1));
        return set;
    }

    private static void CleanupDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // temp
        }
    }

    private sealed record MovRow(
        string Type, double Qty, double Before, double After, string RefType, int RefId);
}
