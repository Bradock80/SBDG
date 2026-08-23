using SGDB.Domain.Sales;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69G — finalização, cancelamento por turno e troca integral/parcial.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PdvQuantityGuardAndShiftCancelTests
{
    private const string IncidentEan = "7896588700608";

    [Fact]
    public void TotalExtremo_BloqueiaFinalizacao()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "teste");

        var pid = TestDataHelper.SeedSimpleProduct(10_000, 50, 20, "EXT", "Atacado");
        var salesBefore = CountSales();
        var cashBefore = CountAllSaleCash();
        var stockBefore = TestDataHelper.GetProductStock(pid);

        var ex = Assert.Throws<PdvException>(() =>
            TestDataHelper.FinalizeSimpleCashSale(pid, 5000, 50, 250_000));

        Assert.Equal(PdvQuantityValidationRules.MessageExtremeTotal, ex.Message);
        Assert.Equal(salesBefore, CountSales());
        Assert.Equal(cashBefore, CountAllSaleCash());
        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(pid));
    }

    [Fact]
    public void CarrinhoContaminado_NaoFinaliza_SemCaixaSemVendaSemEstoque()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(50, "teste");

        var pid = TestDataHelper.SeedSimpleProduct(100, 7.50, 4.50, "00178", "CHOPP DE VINHO BODEGÃO 600ML");
        SetBarcode(pid, IncidentEan);
        var salesBefore = CountSales();
        var cashBefore = CountAllSaleCash();
        var stockBefore = TestDataHelper.GetProductStock(pid);
        var movBefore = TestDataHelper.CountMovements(pid);

        var ex = Assert.Throws<PdvException>(() =>
            TestDataHelper.FinalizeSimpleCashSale(pid, 7896588700608d, 7.50, 0));

        Assert.Equal(PdvQuantityValidationRules.MessageGtinInQuantity, ex.Message);
        Assert.Equal(salesBefore, CountSales());
        Assert.Equal(cashBefore, CountAllSaleCash());
        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(pid));
        Assert.Equal(movBefore, TestDataHelper.CountMovements(pid));
    }

    [Fact]
    public void VendaSessionDateOntem_SessaoAindaAberta_CriadaAposMeiaNoite_CancelamentoPermitido()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        var yesterday = DateTime.Today.AddDays(-1);

        CashService.OpenSession(50, "turno ontem", yesterday);
        var pid = TestDataHelper.SeedSimpleProduct(100, 10, 4, "M1", "Meia-noite");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = pid, Code = "M1", Name = "Meia-noite", Unit = "UN",
                    Quantity = 2, UnitPrice = 10, StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 20,
        }, yesterday);

        Assert.Equal(98, TestDataHelper.GetProductStock(pid));
        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(100, TestDataHelper.GetProductStock(pid));
        Assert.Equal(0, CountCashMovementsForSale(sale.SaleId));
        Assert.True(IsSaleCancelled(sale.SaleId));
    }

    [Fact]
    public void VendaDeSessaoEncerrada_BloqueioPreservado()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        var yesterday = DateTime.Today.AddDays(-1);

        CashService.OpenSession(50, "ontem", yesterday);
        var pid = TestDataHelper.SeedSimpleProduct(40, 10, 4, "F1", "Fechado");
        var sale = PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = pid, Code = "F1", Name = "Fechado", Unit = "UN",
                    Quantity = 1, UnitPrice = 10, StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 10,
        }, yesterday);
        CashService.CloseSession(60, "fecha ontem", yesterday);
        CashService.OpenSession(40, "hoje");

        var ex = Assert.Throws<PdvException>(() => PdvService.CancelSale(sale.SaleId));
        Assert.Contains("turno aberto", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(IsSaleCancelled(sale.SaleId));
        Assert.Equal(39, TestDataHelper.GetProductStock(pid));
        Assert.True(CountCashMovementsForSale(sale.SaleId) >= 1);
    }

    [Fact]
    public void VendaDaSessaoAberta_EstoqueECaixaRevertidos()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(50, "hoje");
        var pid = TestDataHelper.SeedSimpleProduct(30, 10, 4, "A1", "Aberto");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 3, 10, 30);
        Assert.Equal(27, TestDataHelper.GetProductStock(pid));
        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(30, TestDataHelper.GetProductStock(pid));
        Assert.Equal(0, CountCashMovementsForSale(sale.SaleId));
    }

    [Fact]
    public void VendaSemTroca_CancelaNormalmente()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(80, "t");
        var pid = TestDataHelper.SeedSimpleProduct(20, 8, 3, "S1", "Sem troca");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 2, 8, 16);
        PdvService.CancelSale(sale.SaleId);
        Assert.True(IsSaleCancelled(sale.SaleId));
        Assert.Equal(20, TestDataHelper.GetProductStock(pid));
    }

    [Fact]
    public void TrocaIntegralAnterior_BloqueiaCancelSale_NaoDuplicaEstoqueNemCaixa()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(100, "t");

        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 4, "TI", "Integral");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);
        Assert.Equal(18, TestDataHelper.GetProductStock(pid));

        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 2 }],
            NewItems = [],
            PaymentType = "Dinheiro",
        });

        var stockAfterExchange = TestDataHelper.GetProductStock(pid);
        Assert.Equal(20, stockAfterExchange);
        var cashNetAfterExchange = CashNet();
        var cancelMovBefore = CountStockRef(pid, "sale_cancel", sale.SaleId);

        var ex = Assert.Throws<PdvException>(() => PdvService.CancelSale(sale.SaleId));
        Assert.Equal(PdvService.MessageExchangeIntegralCancel, ex.Message);
        Assert.False(IsSaleCancelled(sale.SaleId));
        Assert.Equal(stockAfterExchange, TestDataHelper.GetProductStock(pid));
        Assert.Equal(cancelMovBefore, CountStockRef(pid, "sale_cancel", sale.SaleId));
        Assert.Equal(cashNetAfterExchange, CashNet());
        Assert.True(CountCashMovementsForSale(sale.SaleId) >= 1);
    }

    [Fact]
    public void TrocaParcial_TambemBloqueiaCancelSale_FailClosed()
    {
        using var db = TempDatabase.Create();
        EnsureStandalone();
        TestDataHelper.SetSessionRole("admin");
        CashService.OpenSession(100, "t");

        var pid = TestDataHelper.SeedSimpleProduct(20, 10, 4, "TP", "Parcial");
        var sale = TestDataHelper.FinalizeSimpleCashSale(pid, 2, 10, 20);
        var itemId = GetSaleItemId(sale.SaleId);

        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = itemId, Qty = 1 }],
            NewItems = [],
            PaymentType = "Dinheiro",
        });

        var stockAfter = TestDataHelper.GetProductStock(pid);
        var cashAfter = CashNet();
        var ex = Assert.Throws<PdvException>(() => PdvService.CancelSale(sale.SaleId));
        Assert.Equal(PdvService.MessageExchangePartialCancel, ex.Message);
        Assert.False(IsSaleCancelled(sale.SaleId));
        Assert.Equal(stockAfter, TestDataHelper.GetProductStock(pid));
        Assert.Equal(cashAfter, CashNet());
    }

    private static void EnsureStandalone() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);

    private static void SetBarcode(int productId, string barcode)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET barcode = $b WHERE id = $id;";
        cmd.Parameters.AddWithValue("$b", barcode);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static int GetSaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static bool IsSaleCancelled(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cancelled FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    }

    private static int CountSales()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sales;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountAllSaleCash()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cash_movements WHERE IFNULL(ref_type,'') = 'sale';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountCashMovementsForSale(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM cash_movements
            WHERE IFNULL(ref_type, '') = 'sale' AND ref_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static long CountStockRef(int productId, string refType, int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE product_id = $pid
              AND IFNULL(ref_type, '') = $ref
              AND ref_id = $sale;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$ref", refType);
        cmd.Parameters.AddWithValue("$sale", saleId);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    private static double CashNet()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(SUM(IFNULL(amount_in,0) - IFNULL(amount_out,0)), 0) FROM cash_movements;";
        return Convert.ToDouble(cmd.ExecuteScalar());
    }
}
