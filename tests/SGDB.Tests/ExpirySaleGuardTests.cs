using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 70I-B1 — motor ExpirySaleGuard / FEFO de venda / transferência dep→gel.
/// Bancos isolados em %TEMP%\SGDB.Tests. Não toca deposito.db.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class ExpirySaleGuardTests
{
    private static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void AmbienteIsolado_NaoUsaBancoDaLoja()
    {
        using var db = Begin();
        Assert.Contains("SGDB.Tests", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deposito.db", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(
            Path.Combine(Path.GetTempPath(), "SGDB.Tests"),
            Path.GetFullPath(DatabaseService.DatabasePath),
            StringComparison.OrdinalIgnoreCase);
    }

    // --- decisão / fórmulas ---

    [Fact]
    public void A_SoVencido_Requested1_Bloqueia()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, Yesterday(), "V");
        var d = Eval(id, 1);
        Assert.True(d.IsBlocked);
        Assert.Equal(10, d.ExpiredQty);
        Assert.Equal(0, d.SellableWarehouseQty);
        Assert.Equal(ExpirySaleRules.InsufficientNonExpired, d.ErrorCode);
    }

    [Fact]
    public void B_VencidoMaisValido_Requested3_Permite()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        Receive(id, 5, Tomorrow(), "OK");
        var d = Eval(id, 3);
        Assert.False(d.IsBlocked);
        Assert.Equal(5, d.SellableWarehouseQty);
    }

    [Fact]
    public void C_VencidoMaisValido_Requested7_Bloqueia()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        Receive(id, 5, Tomorrow(), "OK");
        var d = Eval(id, 7);
        Assert.True(d.IsBlocked);
        Assert.Equal(5, d.SellableWarehouseQty);
        Assert.Equal(2, d.BlockedQty); // 7 físico - 5 capacidade; não inventa excesso acima do stock
    }

    [Fact]
    public void D_VencidoMaisUntracked_Requested3_Permite()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        // stock 10, tracked 5 → untracked 5
        var d = Eval(id, 3);
        Assert.False(d.IsBlocked);
        Assert.Equal(5, d.UntrackedQty);
        Assert.Equal(5, d.SellableWarehouseQty);
    }

    [Fact]
    public void E_Uninformed_Requested2_Permite()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, expiry: null, "SEM");
        var d = Eval(id, 2);
        Assert.False(d.IsBlocked);
        Assert.Equal(10, d.UninformedQty);
        Assert.True(d.HasUninformedExpiry);
        Assert.Equal(10, d.SellableWarehouseQty);
    }

    [Fact]
    public void F_Misto_Requested12_Permite()
    {
        using var _ = Begin();
        var id = Seed(20);
        Receive(id, 5, Yesterday(), "V");
        Receive(id, 10, Tomorrow(), "OK");
        // untracked 5
        var d = Eval(id, 12);
        Assert.False(d.IsBlocked);
        Assert.Equal(15, d.SellableWarehouseQty);
    }

    [Fact]
    public void G_ExpiryOntem_EVencido()
    {
        Assert.True(ExpirySaleRules.IsExpired(Yesterday()));
        Assert.False(ExpirySaleRules.IsValidDated(Yesterday()));
    }

    [Fact]
    public void H_ExpiryHoje_EValido()
    {
        Assert.False(ExpirySaleRules.IsExpired(DateTime.Today));
        Assert.True(ExpirySaleRules.IsValidDated(DateTime.Today));
        using var _ = Begin();
        var id = Seed(5);
        Receive(id, 5, DateTime.Today, "HOJE");
        var d = Eval(id, 5);
        Assert.False(d.IsBlocked);
        Assert.Equal(5, d.ValidQty);
        Assert.Equal(0, d.ExpiredQty);
    }

    [Fact]
    public void I_ExpiryAmanha_EValido()
    {
        Assert.True(ExpirySaleRules.IsValidDated(Tomorrow()));
        using var _ = Begin();
        var id = Seed(5);
        Receive(id, 5, Tomorrow(), "AM");
        Assert.False(Eval(id, 5).IsBlocked);
    }

    [Fact]
    public void J_QuantidadeAbaixoDaTolerancia_NaoCriaFalsoVencido()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 0.0005, Yesterday(), "TINY"); // <= Tolerance 0.0009
        var d = Eval(id, 1);
        Assert.Equal(0, d.ExpiredQty);
        Assert.False(d.HasExpiredStock);
        Assert.False(d.IsBlocked); // untracked ≈ 10
    }

    // --- FEFO venda ---

    [Fact]
    public void FefoVenda_NuncaConsomeVencido_ConsomeValido()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "A");
        Receive(id, 5, Tomorrow(), "B");
        SaleWh(id, 3);
        Assert.Equal(7, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "A"));
        Assert.Equal(2, LotQty(id, "B"));
    }

    [Fact]
    public void FefoVenda_MaisQueValido_BloqueiaAtomico()
    {
        using var _ = Begin();
        var id = Seed(7);
        Receive(id, 5, Yesterday(), "A");
        Receive(id, 2, Tomorrow(), "B");
        var ex = Assert.Throws<ExpirySaleException>(() => SaleWh(id, 3));
        Assert.Equal(ExpirySaleRules.InsufficientNonExpired, ex.ErrorCode);
        Assert.Equal(7, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "A"));
        Assert.Equal(2, LotQty(id, "B"));
        Assert.Equal(0, TestDataHelper.CountMovements(id));
    }

    [Fact]
    public void DeductFefo_DefaultAindaPodeConsumirVencido_CallerLegado()
    {
        // Semântica histórica preservada quando skipExpired=false.
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "A");
        Receive(id, 5, Tomorrow(), "B");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        ProductLotService.DeductFefo(conn, tx, id, 3, skipExpired: false);
        tx.Commit();
        Assert.Equal(2, LotQty(id, "A"));
        Assert.Equal(5, LotQty(id, "B"));
    }

    // --- geladeira ---

    [Fact]
    public void Geladeira_VendaSoFridge_PermiteComWhVencido()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, Yesterday(), "V");
        TestDataHelper.SetProductFridge(id, 10);
        Sale(id, 5);
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, TestDataHelper.GetProductFridge(id));
        Assert.Equal(10, LotQty(id, "V"));
    }

    [Fact]
    public void GeladeiraParcial_MaisWhVencido_BloqueiaInteiro()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, Yesterday(), "V");
        TestDataHelper.SetProductFridge(id, 2);
        var ex = Assert.Throws<ExpirySaleException>(() => Sale(id, 3));
        Assert.Equal(1, ex.Decision.RequestedWarehouseQty);
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(2, TestDataHelper.GetProductFridge(id));
        Assert.Equal(10, LotQty(id, "V"));
        Assert.Equal(0, TestDataHelper.CountMovements(id));
    }

    // --- transferência ---

    [Fact]
    public void Transfer_SoVencido_Bloqueia()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, Yesterday(), "V");
        var ex = Assert.Throws<ExpirySaleException>(
            () => StockService.TransferWarehouseToFridge(id, 1));
        Assert.Equal(ExpirySaleRules.TransferInsufficientNonExpired, ex.ErrorCode);
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, TestDataHelper.GetProductFridge(id));
        Assert.Equal(10, LotQty(id, "V"));
    }

    [Fact]
    public void Transfer_DentroDoValido_Permite()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        Receive(id, 5, Tomorrow(), "OK");
        StockService.TransferWarehouseToFridge(id, 3);
        Assert.Equal(7, TestDataHelper.GetProductStock(id));
        Assert.Equal(3, TestDataHelper.GetProductFridge(id));
        // lotes do depósito não se movem
        Assert.Equal(5, LotQty(id, "V"));
        Assert.Equal(5, LotQty(id, "OK"));
    }

    [Fact]
    public void Transfer_AcimaDoLimite_Bloqueia()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        Receive(id, 5, Tomorrow(), "OK");
        Assert.Throws<ExpirySaleException>(() => StockService.TransferWarehouseToFridge(id, 7));
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, TestDataHelper.GetProductFridge(id));
    }

    [Fact]
    public void Transfer_Untracked_Permite()
    {
        using var _ = Begin();
        var id = Seed(10);
        // sem lotes → untracked 10
        StockService.TransferWarehouseToFridge(id, 4);
        Assert.Equal(6, TestDataHelper.GetProductStock(id));
        Assert.Equal(4, TestDataHelper.GetProductFridge(id));
    }

    [Fact]
    public void Transfer_Uninformed_Permite()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, expiry: null, "SEM");
        StockService.TransferWarehouseToFridge(id, 3);
        Assert.Equal(7, TestDataHelper.GetProductStock(id));
        Assert.Equal(3, TestDataHelper.GetProductFridge(id));
        Assert.Equal(10, LotQty(id, "SEM"));
    }

    [Fact]
    public void Transfer_GeladeiraParaDeposito_PreservaSemInventarLote()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, Tomorrow(), "OK");
        StockService.TransferWarehouseToFridge(id, 4);
        StockService.TransferFridgeToWarehouse(id, 4);
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, TestDataHelper.GetProductFridge(id));
        // retorno não altera lotes (continua 10 no mesmo lote)
        Assert.Equal(10, LotQty(id, "OK"));
    }

    // --- overtracked ---

    [Fact]
    public void OverTracked_SellableLimitadoAoStock()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 8, Yesterday(), "V");
        Receive(id, 12, Tomorrow(), "OK");
        // tracked 20 > stock 10
        var d = Eval(id, 10);
        Assert.Equal(0, d.UntrackedQty);
        Assert.Equal(10, d.SellableWarehouseQty); // MIN(10, 12) — cap físico, não critério 70I
        Assert.False(d.IsBlocked);
        // Toda a quantidade física (10) é explicável por 12 válidos.
        // requested 13/100 só excede stock — política histórica, não 70I.
        Assert.False(Eval(id, 13).IsBlocked);
        Assert.Equal(0, Eval(id, 13).BlockedQty);
        Assert.False(Eval(id, 100).IsBlocked);
        SaleWh(id, 13);
        Assert.Equal(-3, TestDataHelper.GetProductStock(id));
        Assert.Equal(8, LotQty(id, "V"));
        Assert.Equal(0, LotQty(id, "OK"));
    }

    [Fact]
    public void OverTracked_ExpiredMaiorQueStock_SemNegativo()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 12, Yesterday(), "V");
        Receive(id, 8, Tomorrow(), "OK");
        var d = Eval(id, 8);
        Assert.True(d.SellableWarehouseQty >= 0);
        Assert.Equal(8, d.SellableWarehouseQty);
        Assert.False(d.IsBlocked);
        Assert.True(Eval(id, 9).IsBlocked);
        Assert.Equal(1, Eval(id, 9).BlockedQty); // 9 físico - 8 capacidade
    }

    [Fact]
    public void OverTracked_ValidSobra_Request100_NaoBloqueia70I()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        Receive(id, 20, Tomorrow(), "OK");
        Assert.False(Eval(id, 100).IsBlocked);
        Assert.Equal(0, Eval(id, 100).BlockedQty);
        SaleWh(id, 100);
        Assert.Equal(-90, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "V"));
        Assert.Equal(0, LotQty(id, "OK"));
    }

    [Fact]
    public void ExpiredMaisValido_Request11_BloqueiaParteFisica()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        Receive(id, 5, Tomorrow(), "OK");
        var d = Eval(id, 11);
        Assert.True(d.IsBlocked);
        Assert.Equal(5, d.SellableWarehouseQty);
        Assert.Equal(5, d.BlockedQty); // MIN(11,10)-5 = 5; não 11-5=6
        Assert.Throws<ExpirySaleException>(() => SaleWh(id, 11));
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "V"));
        Assert.Equal(5, LotQty(id, "OK"));
    }

    // --- legado / negativo ---

    [Fact]
    public void Legado_SemLotes_VendaNaoBloqueiaComoVencido()
    {
        using var _ = Begin();
        var id = Seed(10);
        SaleWh(id, 3);
        Assert.Equal(7, TestDataHelper.GetProductStock(id));
        Assert.True(TestDataHelper.CountMovements(id) >= 1);
    }

    [Fact]
    public void StockZero_SemLotes_NaoMensagemDeVencido()
    {
        using var _ = Begin();
        var id = Seed(0);
        // Política histórica: ApplySaleDeduction pode ir a negativo.
        SaleWh(id, 2);
        Assert.Equal(-2, TestDataHelper.GetProductStock(id));
        var d = Eval(id, 1);
        Assert.False(d.IsBlocked);
        Assert.DoesNotContain("vencid", d.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StockNegativo_SemLotes_NaoBloqueia70I()
    {
        using var _ = Begin();
        var id = Seed(0);
        SetStock(id, -5);
        var d = Eval(id, 1);
        Assert.False(d.IsBlocked);
        Assert.Equal(0, d.SellableWarehouseQty);
        SaleWh(id, 1);
        Assert.Equal(-6, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void StockZero_ComLotesVencidos_DeductFefoVendaNaoConsomeVencido()
    {
        // stock 0 + lotes vencidos (inconsistente): gate 70I não bloqueia por stock<=0,
        // mas FEFO de venda não deve consumir o vencido.
        using var _ = Begin();
        var id = Seed(0);
        Receive(id, 5, Yesterday(), "V");
        Assert.False(Eval(id, 1).IsBlocked); // stock<=0 → sem bloqueio 70I de quantidade
        SaleWh(id, 1);
        Assert.Equal(-1, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "V"));
    }

    [Fact]
    public void SemLotes_RequestAcimaDoStock_NaoBloqueia70I_PreservaNegativoHistorico()
    {
        using var _ = Begin();
        var id = Seed(10);
        Assert.False(Eval(id, 11).IsBlocked);
        Assert.Equal(0, Eval(id, 11).BlockedQty);
        Assert.DoesNotContain("vencid", Eval(id, 11).Reason, StringComparison.OrdinalIgnoreCase);
        SaleWh(id, 11);
        Assert.Equal(-1, TestDataHelper.GetProductStock(id));
    }

    [Fact]
    public void Uninformed_RequestAcimaDoStock_NaoBloqueia70I()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, expiry: null, "SEM");
        Assert.False(Eval(id, 11).IsBlocked);
        SaleWh(id, 11);
        Assert.Equal(-1, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, LotQty(id, "SEM"));
    }

    [Fact]
    public void Valid_RequestAcimaDoStock_NaoBloqueia70I()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, Tomorrow(), "OK");
        Assert.False(Eval(id, 11).IsBlocked);
        SaleWh(id, 11);
        Assert.Equal(-1, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, LotQty(id, "OK"));
    }

    [Fact]
    public void Venda_ExpiredMaisValido_Request7_BloqueiaAtomico()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        Receive(id, 5, Tomorrow(), "OK");
        Assert.Throws<ExpirySaleException>(() => SaleWh(id, 7));
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "V"));
        Assert.Equal(5, LotQty(id, "OK"));
    }

    [Fact]
    public void Venda_ExpiredMaisUntracked_Request7_Bloqueia()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 5, Yesterday(), "V");
        Assert.Throws<ExpirySaleException>(() => SaleWh(id, 7));
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "V"));
        Assert.Equal(0, TestDataHelper.CountMovements(id));
    }

    [Fact]
    public void StockNegativo_ComLoteVencidoInconsistente_NaoMensagemDeVencido()
    {
        using var _ = Begin();
        var id = Seed(0);
        SetStock(id, -5);
        Receive(id, 5, Yesterday(), "V");
        var d = Eval(id, 1);
        Assert.False(d.IsBlocked);
        Assert.DoesNotContain("vencid", d.Reason, StringComparison.OrdinalIgnoreCase);
        SaleWh(id, 1);
        Assert.Equal(-6, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "V"));
    }

    [Fact]
    public void FefoVenda_ABC_ValidoDatadoDepoisUninformed_IgnoraVencido()
    {
        using var _ = Begin();
        var id = Seed(15);
        Receive(id, 5, Yesterday(), "A");
        Receive(id, 5, Tomorrow(), "B");
        Receive(id, 5, expiry: null, "C");
        SaleWh(id, 7);
        Assert.Equal(8, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, LotQty(id, "A"));
        Assert.Equal(0, LotQty(id, "B"));
        Assert.Equal(3, LotQty(id, "C"));
    }

    [Fact]
    public void Transfer_AcimaDoStockSemLotes_BloqueioDeEstoqueNaoDeVencimento()
    {
        using var _ = Begin();
        var id = Seed(10);
        var ex = Record.Exception(() => StockService.TransferWarehouseToFridge(id, 11));
        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);
        Assert.IsNotType<ExpirySaleException>(ex);
        Assert.Contains("depósito", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(0, TestDataHelper.GetProductFridge(id));
    }

    // --- callers / restore / compra intactos ---

    [Fact]
    public void FinalizeSaleCore_BloqueiaQuandoSoVencido()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "70i-b1");
        var id = Seed(10);
        Receive(id, 10, Yesterday(), "V");
        var ex = Assert.Throws<ExpirySaleException>(() =>
            TestDataHelper.FinalizeSimpleCashSale(id, qty: 1, unitPrice: 5, cashReceived: 5));
        Assert.Equal(ExpirySaleRules.InsufficientNonExpired, ex.ErrorCode);
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(10, LotQty(id, "V"));
        Assert.Equal(0, CountRows("sales"));
        Assert.Equal(0, CountRows("sale_items"));
    }

    [Fact]
    public void Deck_SomenteVencido_RollbackComanda()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "70i-deck");
        var id = Seed(10);
        Receive(id, 10, Yesterday(), "V");
        var tabId = OpenTabService.Create("Deck 70I");
        OpenTabService.AddProduct(tabId, id, 1, 5);
        var lines = OpenTabService.ToCartLines(tabId).ToList();
        Assert.Throws<ExpirySaleException>(() =>
            OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
            {
                Items = lines,
                PaymentType = "Dinheiro",
                CashReceived = 5,
            }));
        Assert.Equal(10, TestDataHelper.GetProductStock(id));
        Assert.Equal(10, LotQty(id, "V"));
        Assert.Equal(0, CountRows("sales"));
        Assert.Equal("open", GetTabStatus(tabId));
        Assert.True(TabSaleIdIsNull(tabId));
    }

    [Fact]
    public void Swap_NovoItemSomenteVencido_Rollback()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "70i-swap");
        var a = Seed(10);
        var b = Seed(10);
        Receive(b, 10, Yesterday(), "V");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, qty: 1, unitPrice: 5, cashReceived: 5);
        var itemId = GetSaleItemId(sale.SaleId);
        Assert.Equal(9, TestDataHelper.GetProductStock(a));
        Assert.Throws<ExpirySaleException>(() =>
            PdvService.SwapSaleItem(sale.SaleId, itemId, b, keepLinePrice: true));
        Assert.Equal(9, TestDataHelper.GetProductStock(a));
        Assert.Equal(10, TestDataHelper.GetProductStock(b));
        Assert.Equal(10, LotQty(b, "V"));
        Assert.Equal(a, GetSaleItemProductId(itemId));
    }

    [Fact]
    public void Exchange_NovoProdutoSomenteVencido_Rollback()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "70i-ex");
        var a = Seed(10);
        var b = Seed(10);
        Receive(b, 10, Yesterday(), "V");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, qty: 1, unitPrice: 5, cashReceived: 5);
        var itemId = GetSaleItemId(sale.SaleId);
        Assert.Throws<ExpirySaleException>(() =>
            SaleExchangeService.Confirm(new SaleExchangeRequest
            {
                OriginalSaleId = sale.SaleId,
                Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
                NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 1, UnitPrice = 5 }],
                PaymentType = "Dinheiro",
            }));
        Assert.Equal(9, TestDataHelper.GetProductStock(a));
        Assert.Equal(10, TestDataHelper.GetProductStock(b));
        Assert.Equal(10, LotQty(b, "V"));
        Assert.Equal(0, CountRows("sale_exchanges"));
    }

    [Fact]
    public void FinalizeSale_MultiItem_SegundoVencido_RollbackTotal()
    {
        using var _ = Begin();
        CashService.OpenSession(50, "70i-multi");
        var ok = Seed(10);
        Receive(ok, 10, Tomorrow(), "OK");
        var bad = Seed(10);
        Receive(bad, 10, Yesterday(), "V");
        Assert.Throws<ExpirySaleException>(() =>
            PdvService.FinalizeSale(new PdvFinalizeRequest
            {
                Items =
                [
                    new PdvCartLine
                    {
                        ProductId = ok, Quantity = 1, UnitPrice = 5, StockUnitsPerSale = 1,
                    },
                    new PdvCartLine
                    {
                        ProductId = bad, Quantity = 1, UnitPrice = 5, StockUnitsPerSale = 1,
                    },
                ],
                PaymentType = "Dinheiro",
                CashReceived = 10,
            }));
        Assert.Equal(10, TestDataHelper.GetProductStock(ok));
        Assert.Equal(10, TestDataHelper.GetProductStock(bad));
        Assert.Equal(10, LotQty(ok, "OK"));
        Assert.Equal(10, LotQty(bad, "V"));
        Assert.Equal(0, TestDataHelper.CountMovements(ok));
        Assert.Equal(0, TestDataHelper.CountMovements(bad));
        Assert.Equal(0, CountRows("sales"));
        Assert.Equal(0, CountRows("sale_items"));
    }

    [Fact]
    public void Restore_NaoFoiAlterado_PodeReporNoMaisProximoInclusiveVencido()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, Yesterday(), "V");
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        StockService.ApplySaleRestore(conn, tx, id, 2, notes: "teste restore");
        tx.Commit();
        Assert.Equal(12, TestDataHelper.GetProductStock(id));
        Assert.Equal(12, LotQty(id, "V"));
    }

    [Fact]
    public void DeductExact_Compra_IntactoComVencido()
    {
        using var _ = Begin();
        var id = Seed(10);
        Receive(id, 10, Yesterday(), "V");
        var lotId = FirstLotId(id);
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        ProductLotService.DeductExact(conn, tx, id, lotId, "V", Yesterday(), 3);
        tx.Commit();
        Assert.Equal(7, LotQty(id, "V"));
    }

    [Fact]
    public void DataRules_AlinhadasComProductExpiryService()
    {
        var today = DateTime.Today;
        Assert.Equal(
            ProductExpiryStatusKind.Expired,
            ProductExpiryService.Classify(today.AddDays(-1)).Kind);
        Assert.Equal(
            ProductExpiryStatusKind.Today,
            ProductExpiryService.Classify(today).Kind);
        Assert.True(ExpirySaleRules.IsExpired(today.AddDays(-1)));
        Assert.False(ExpirySaleRules.IsExpired(today));
    }

    // --- helpers ---

    private static DateTime Yesterday() => DateTime.Today.AddDays(-1);
    private static DateTime Tomorrow() => DateTime.Today.AddDays(1);

    private static int Seed(double stock) =>
        TestDataHelper.SeedSimpleProduct(stock, 5, 2,
            code: "E" + Guid.NewGuid().ToString("N")[..6],
            name: "EXP " + Guid.NewGuid().ToString("N")[..6]);

    private static void Receive(int productId, double qty, DateTime? expiry, string lot) =>
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = qty,
            LotNumber = lot,
            ExpiryDate = expiry,
        });

    private static ExpirySaleDecision Eval(int productId, double requestedWh)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        var d = ExpirySaleGuard.Evaluate(conn, tx, productId, requestedWh);
        tx.Commit();
        return d;
    }

    private static void SaleWh(int productId, double qty)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        // sem fridge → tudo depósito
        StockService.ApplySaleDeduction(conn, tx, productId, qty, notes: "teste 70I");
        tx.Commit();
    }

    private static void Sale(int productId, double qty)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        StockService.ApplySaleDeduction(conn, tx, productId, qty, notes: "teste 70I fridge");
        tx.Commit();
    }

    private static void SetStock(int productId, double stock)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET stock = $s WHERE id = $id;";
        cmd.Parameters.AddWithValue("$s", stock);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static double LotQty(int productId, string lot)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT IFNULL(SUM(quantity),0) FROM product_lots
            WHERE product_id = $pid AND IFNULL(lot_number,'') = $lot;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$lot", lot);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    private static int FirstLotId(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM product_lots WHERE product_id = $pid ORDER BY id LIMIT 1;";
        cmd.Parameters.AddWithValue("$pid", productId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountRows(string table)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = table switch
        {
            "sales" => "SELECT COUNT(*) FROM sales;",
            "sale_items" => "SELECT COUNT(*) FROM sale_items;",
            "sale_exchanges" => "SELECT COUNT(*) FROM sale_exchanges;",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetSaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetSaleItemProductId(int itemId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id FROM sale_items WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", itemId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string GetTabStatus(int tabId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(status,'') FROM open_tabs WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", tabId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static bool TabSaleIdIsNull(int tabId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sale_id FROM open_tabs WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", tabId);
        var o = cmd.ExecuteScalar();
        return o is null or DBNull;
    }
}
