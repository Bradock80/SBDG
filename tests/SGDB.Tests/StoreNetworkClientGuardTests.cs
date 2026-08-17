using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// Segurança Rede Loja: no modo cliente, mutações comerciais locais são recusadas
/// antes de alterar o SQLite do notebook.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class StoreNetworkClientGuardTests
{
    [Fact]
    public void FinalizeSale_Client_NaoGravaVendaLocal()
    {
        using var db = TempDatabase.Create();
        SetClientRole();

        var productId = TestDataHelper.SeedSimpleProduct(100, 10, 4);
        var salesBefore = Count("sales");
        var stockBefore = TestDataHelper.GetProductStock(productId);

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            TestDataHelper.FinalizeSimpleCashSale(productId, 2, 10, 20));

        Assert.Equal(salesBefore, Count("sales"));
        Assert.Equal(0, Count("sale_items"));
        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountWhere("cash_movements", "IFNULL(ref_type,'') = 'sale'"));
        Assert.Equal(0, CountWhere("movements", "IFNULL(ref_type,'') = 'sale'"));
    }

    [Fact]
    public void CancelSale_Client_NaoAlteraVendaLocal()
    {
        using var db = TempDatabase.Create();
        // Venda criada em standalone; depois vira cliente e tenta cancelar.
        CashService.OpenSession(50, "teste");
        TestDataHelper.GrantPdvCancelPermission();
        var productId = TestDataHelper.SeedSimpleProduct(50, 10, 4);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 10, 10);
        var stockAfterSale = TestDataHelper.GetProductStock(productId);

        SetClientRole();

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            PdvService.CancelSale(sale.SaleId));

        Assert.Equal(0, GetCancelled(sale.SaleId));
        Assert.Equal(stockAfterSale, TestDataHelper.GetProductStock(productId));
        Assert.True(CountWhere("cash_movements", "IFNULL(ref_type,'') = 'sale'") >= 1);
    }

    [Fact]
    public void SettleOpenTab_Client_NaoFechaDeckLocal()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(50, "teste");
        var productId = TestDataHelper.SeedSimpleProduct(40, 8, 3);
        var tabId = OpenTabService.Create("Deck Cliente");
        OpenTabService.AddProduct(tabId, productId, 2, 8);
        var lines = OpenTabService.ToCartLines(tabId).ToList();
        var stockBefore = TestDataHelper.GetProductStock(productId);
        var salesBefore = Count("sales");

        SetClientRole();

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            OpenTabSettlementService.SettleOpenTab(tabId, new PdvFinalizeRequest
            {
                Items = lines,
                PaymentType = "Dinheiro",
                CashReceived = 16,
            }));

        Assert.Equal(salesBefore, Count("sales"));
        Assert.Equal(stockBefore, TestDataHelper.GetProductStock(productId));
        Assert.Equal("open", GetTabStatus(tabId));
        Assert.True(IsNullSaleId(tabId));
    }

    [Fact]
    public void CashOpen_Client_NaoAbreCaixaLocal()
    {
        using var db = TempDatabase.Create();
        SetClientRole();
        var sessionsBefore = Count("cash_sessions");

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            CashService.OpenSession(100, "bloqueado"));

        Assert.Equal(sessionsBefore, Count("cash_sessions"));
        Assert.False(CashService.IsOperational());
    }

    [Fact]
    public void FiadoPayment_Client_NaoGravaPagamentoLocal()
    {
        using var db = TempDatabase.Create();
        SetClientRole();
        var paymentsBefore = Count("fiado_payments");

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            FiadoService.RegisterPayment(1, new FiadoReceberInput
            {
                Amount = 10,
                PrincipalAmount = 10,
                InterestAmount = 0,
                PaymentDate = DateTime.Today.ToString("dd/MM/yyyy"),
                Payments = [new FiadoReceberPart { PaymentType = "Dinheiro", Amount = 10 }],
            }));

        Assert.Equal(paymentsBefore, Count("fiado_payments"));
    }

    [Fact]
    public void FinalizeSale_Standalone_ContinuaFuncionando()
    {
        using var db = TempDatabase.Create();
        Assert.False(StoreNetworkMode.IsClient);

        CashService.OpenSession(50, "ok");
        var productId = TestDataHelper.SeedSimpleProduct(20, 5, 2);
        var result = TestDataHelper.FinalizeSimpleCashSale(productId, 1, 5, 5);

        Assert.True(result.SaleId > 0);
        Assert.Equal(19, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void BankImportOfx_Client_NaoGravaLocal()
    {
        using var db = TempDatabase.Create();
        var accountId = BankService.SaveAccount(
            null, "Conta Teste", "Banco", "1", "123", "corrente", null, 0, true, null);
        var movBefore = Count("bank_movements");

        SetClientRole();

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            BankService.ImportOfx(accountId, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sgdb-inexistente.ofx")));

        Assert.Equal(movBefore, Count("bank_movements"));
    }

    [Fact]
    public void BankReconcile_Client_NaoAlteraLocal()
    {
        using var db = TempDatabase.Create();
        var accountId = BankService.SaveAccount(
            null, "Conta Conf", "Banco", "1", "456", "corrente", null, 100, true, null);
        BankService.AddMovement(accountId, DateTime.Today, "credito", "teste", 50, 0, paymentType: "Pix");
        var pendingBefore = CountWhere("bank_movements", "reconciliation_status = 'pendente'");

        SetClientRole();

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            BankService.ConferirTodos(accountId, null, null));

        Assert.Equal(pendingBefore, CountWhere("bank_movements", "reconciliation_status = 'pendente'"));
        Assert.Equal(0, CountWhere("bank_movements", "reconciliation_status = 'conferido'"));
    }

    [Fact]
    public void CatalogCreate_Client_NaoGravaLocal()
    {
        using var db = TempDatabase.Create();
        var brandsBefore = Count("product_brands");

        SetClientRole();

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            ProductCatalogService.Create(ProductCatalogKind.Brands, "MARCA TESTE E7"));

        Assert.Equal(brandsBefore, Count("product_brands"));
    }

    [Fact]
    public void PaymentMethodSave_Client_NaoGravaLocal()
    {
        using var db = TempDatabase.Create();
        var feesBefore = Count("payment_method_fees");

        SetClientRole();

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            PaymentMethodsService.Save(new PaymentMethodInput
            {
                Name = "FORMA TESTE E7",
                ApiLabel = "Forma Teste E7",
                Active = true,
            }));

        Assert.Equal(feesBefore, Count("payment_method_fees"));
    }

    [Fact]
    public void ProductLotReceive_Client_NaoGravaLocal()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(10, 5, 2);
        var lotsBefore = Count("product_lots");

        SetClientRole();

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            ProductLotService.Receive(new ProductLotReceiveInput
            {
                ProductId = productId,
                Quantity = 5,
                LotNumber = "L1",
                ExpiryDate = DateTime.Today.AddMonths(6),
            }));

        Assert.Equal(lotsBefore, Count("product_lots"));
    }

    [Fact]
    public void ProductCreateLocal_Client_NaoGravaLocal()
    {
        using var db = TempDatabase.Create();
        var productsBefore = Count("products");

        SetClientRole();

        Assert.Throws<StoreNetworkClientBlockedException>(() =>
            ProductService.CreateLocal(new ProductInput
            {
                Code = "E7LOCAL",
                Name = "PRODUTO LOCAL BLOQUEADO",
                Unit = "UN",
                CostPrice = 1,
                SalePrice = 2,
                Stock = 0,
                MinStock = 0,
                Active = true,
            }));

        Assert.Equal(productsBefore, Count("products"));
    }

    [Fact]
    public void IsModuleBlockedOnClient_BloqueiaCaixaFiadoDecks()
    {
        using var db = TempDatabase.Create();
        SetClientRole();

        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("caixa"));
        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("fiado"));
        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("decks"));
        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("estoque_inventario"));
        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("contas_bancarias"));
        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("relatorio_dre"));
        Assert.True(StoreNetworkMode.IsModuleBlockedOnClient("usuarios"));
        Assert.Equal(
            ApplicationLoginService.LocalUserAdministrationMessage,
            StoreNetworkMode.BlockedModuleMessage("usuarios"));
        Assert.False(StoreNetworkMode.IsModuleBlockedOnClient("produtos"));
        Assert.False(StoreNetworkMode.IsModuleBlockedOnClient("compras"));
        Assert.False(StoreNetworkMode.IsModuleBlockedOnClient("pagar"));
        Assert.False(StoreNetworkMode.IsModuleBlockedOnClient("inicio"));
    }

    private static void SetClientRole() =>
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);

    private static int Count(string table)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CountWhere(string table, string where)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {where};";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetCancelled(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cancelled FROM sales WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string GetTabStatus(int tabId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM open_tabs WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", tabId);
        return (string)(cmd.ExecuteScalar() ?? "");
    }

    private static bool IsNullSaleId(int tabId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sale_id FROM open_tabs WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", tabId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull;
    }
}
