using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69D-C2-B1 — reversão segura de custo no cancelamento/reabertura de compra.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseCancelCostTests
{
    private static TempDatabase BeginDb()
    {
        PurchaseService.TestBeforeReverseCancelCost = null;
        PurchaseService.TestAfterReverseCancelCost = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Theory]
    [InlineData("transferencia_geladeira", PostPurchaseMovementKind.SafeLocation)]
    [InlineData("retorno_geladeira", PostPurchaseMovementKind.SafeLocation)]
    [InlineData("entrada_compra", PostPurchaseMovementKind.SafePurchase)]
    [InlineData("entrada_nfe", PostPurchaseMovementKind.SafePurchase)]
    [InlineData("estorno_compra", PostPurchaseMovementKind.SafePurchase)]
    [InlineData("venda", PostPurchaseMovementKind.Sale)]
    [InlineData("cancelamento_venda", PostPurchaseMovementKind.SaleRestore)]
    [InlineData("entrada_manual", PostPurchaseMovementKind.Unsafe)]
    [InlineData("saida_manual", PostPurchaseMovementKind.Unsafe)]
    [InlineData("ajuste_manual", PostPurchaseMovementKind.Unsafe)]
    [InlineData("ajuste_geladeira", PostPurchaseMovementKind.Unsafe)]
    [InlineData("devolucao_troca", PostPurchaseMovementKind.Unsafe)]
    [InlineData("unificacao_produto", PostPurchaseMovementKind.Unsafe)]
    [InlineData("", PostPurchaseMovementKind.Unsafe)]
    public void ClassificaOperacoesPosteriores(string op, PostPurchaseMovementKind expected)
    {
        Assert.Equal(expected, PurchaseCancelCostRules.ClassifyOperation(op));
    }

    [Fact]
    public void UmaCompra_Cancelar_PrecoCompraZero_CustoVolta5()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "P1", "UMA COMPRA");
        var purchaseId = CreateClosed(supplier, productId, "UMA COMPRA", 10, 7);

        PurchaseService.Cancel(purchaseId);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(5, product.CostPrice);
        Assert.Equal(0, PrecoCompra(product));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal("cancelada", Status(purchaseId));
    }

    [Fact]
    public void A7_B9_CancelarB_PrecoVolta7()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "P2", "CANCEL B");
        CreateClosed(supplier, productId, "CANCEL B", 10, 7, number: "NF-A");
        var b = CreateClosed(supplier, productId, "CANCEL B", 10, 9, number: "NF-B");

        PurchaseService.Cancel(b);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(6, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void A7_B9_CancelarA_PrecoContinua9_Custo7()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "P3", "CANCEL A");
        var a = CreateClosed(supplier, productId, "CANCEL A", 10, 7, number: "NF-A");
        CreateClosed(supplier, productId, "CANCEL A", 10, 9, number: "NF-B");

        PurchaseService.Cancel(a);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(7, product.CostPrice);
        Assert.Equal(9, PrecoCompra(product));
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void A7_B9_C11_CancelarB_UltimaValidaC()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "P4", "MEIO");
        CreateClosed(supplier, productId, "MEIO", 10, 7, number: "NF-A");
        var b = CreateClosed(supplier, productId, "MEIO", 10, 9, number: "NF-B");
        CreateClosed(supplier, productId, "MEIO", 10, 11, number: "NF-C");

        PurchaseService.Cancel(b);

        Assert.Equal(11, PrecoCompra(ProductService.GetById(productId)!));
        Assert.Equal(30, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void CompraSemEstoque_ContaComoUltimaCompra()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "P5", "SEM EST");
        CreateClosed(supplier, productId, "SEM EST", 10, 7, number: "NF-A", gerarEstoque: false);
        CreateClosed(supplier, productId, "SEM EST", 10, 9, number: "NF-B", gerarEstoque: false);

        Assert.Equal(9, PrecoCompra(ProductService.GetById(productId)!));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void Geladeira_CancelarSemMovimento_VoltaCusto5()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 8, 5, "GELA", "GELA 20");
        TestDataHelper.SetProductFridge(productId, 20);
        var purchaseId = CreateClosed(supplier, productId, "GELA 20", 10, 7);

        Assert.Equal(5.67, ProductService.GetById(productId)!.CostPrice);
        PurchaseService.Cancel(purchaseId);

        Assert.Equal(5, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(20, TestDataHelper.GetProductFridge(productId));
    }

    [Fact]
    public void Cigarro_CancelarSemMovimento_VoltaCusto10()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var cig = SeedCigarette(200, 10, 10, 20);
        var purchaseId = CreateClosed(supplier, cig, "Rothmans Blue", 100, 0.60);

        Assert.Equal(10.67, ProductService.GetById(cig)!.CostPrice);
        PurchaseService.Cancel(purchaseId);

        Assert.Equal(10, ProductService.GetById(cig)!.CostPrice);
        Assert.Equal(0, PrecoCompra(ProductService.GetById(cig)!));
        Assert.Equal(200, TestDataHelper.GetProductStock(cig));
    }

    [Fact]
    public void VendaPosteriorSemLote_BloqueiaCancelamento()
    {
        using var _ = BeginDb();
        CashService.OpenSession(50, "c2b1-venda");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "V1", "VENDA POST");
        var purchaseId = CreateClosed(supplier, productId, "VENDA POST", 10, 7);
        TestDataHelper.FinalizeSimpleCashSale(productId, 5, 8, 40);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage, ex.Message);
        Assert.DoesNotContain("média", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fórmula", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(6, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(15, TestDataHelper.GetProductStock(productId));
        Assert.Equal("fechada", Status(purchaseId));
        Assert.Equal(7, PrecoCompra(ProductService.GetById(productId)!));
        Assert.Equal(0, CountEstorno(productId, purchaseId));
    }

    [Fact]
    public void VendaDepoisCancelada_PermiteCancelarCompra()
    {
        using var _ = BeginDb();
        CashService.OpenSession(80, "c2b1-vd-canc");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "V2", "VENDA CANC");
        var purchaseId = CreateClosed(supplier, productId, "VENDA CANC", 10, 7);
        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, 5, 8, 40);
        PdvService.CancelSale(sale.SaleId);

        PurchaseService.Cancel(purchaseId);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(5, product.CostPrice);
        Assert.Equal(0, PrecoCompra(product));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal("cancelada", Status(purchaseId));
    }

    [Fact]
    public void AjusteEntradaComCusto_Bloqueia()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "AJ1", "AJ ENTRADA");
        var purchaseId = CreateClosed(supplier, productId, "AJ ENTRADA", 10, 7);
        StockService.Adjust(productId, StockAdjustMode.Entrada, quantity: 2, unitCost: 9, notes: "entrada");

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage, ex.Message);
        Assert.Equal("fechada", Status(purchaseId));
        Assert.Equal(22, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void AjusteEntradaSemCusto_Bloqueia()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "AJ2", "AJ SEM");
        var purchaseId = CreateClosed(supplier, productId, "AJ SEM", 10, 7);
        StockService.Adjust(productId, StockAdjustMode.Entrada, quantity: 2, notes: "entrada");

        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage,
            Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId)).Message);
        Assert.Equal(22, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void AjusteSaida_Bloqueia()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "AJ3", "AJ SAIDA");
        var purchaseId = CreateClosed(supplier, productId, "AJ SAIDA", 10, 7);
        StockService.Adjust(productId, StockAdjustMode.Saida, quantity: 2, notes: "saida");

        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage,
            Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId)).Message);
    }

    [Fact]
    public void AjusteSaldo_Bloqueia()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "AJ4", "AJ SALDO");
        var purchaseId = CreateClosed(supplier, productId, "AJ SALDO", 10, 7);
        StockService.Adjust(productId, StockAdjustMode.Saldo, newStock: 12, notes: "saldo");

        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage,
            Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId)).Message);
    }

    [Fact]
    public void InventarioPosterior_Bloqueia()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "INV", "INV POST");
        var purchaseId = CreateClosed(supplier, productId, "INV POST", 10, 7);
        var session = InventoryService.CreateSession();
        var item = InventoryService.ListItems(session.Id).Single(i => i.ProductId == productId);
        InventoryService.SetCounted(item.Id, 18);
        InventoryService.Consolidate(session.Id);

        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage,
            Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId)).Message);
        Assert.Equal(18, TestDataHelper.GetProductStock(productId));
        Assert.Equal("fechada", Status(purchaseId));
    }

    [Fact]
    public void TransferenciaGeladeira_NaoBloqueiaPorCusto()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "TR", "TRANSF");
        var purchaseId = CreateClosed(supplier, productId, "TRANSF", 10, 7);
        StockService.TransferWarehouseToFridge(productId, 5);

        PurchaseService.Cancel(purchaseId);

        Assert.Equal(5, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(5, TestDataHelper.GetProductStock(productId));
        Assert.Equal(5, TestDataHelper.GetProductFridge(productId));
        Assert.Equal("cancelada", Status(purchaseId));
    }

    [Fact]
    public void GerarEstoqueFalse_CancelarNaoMexesStockNemCriaEstorno()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "GE", "SEM GERAR");
        var purchaseId = CreateClosed(supplier, productId, "SEM GERAR", 10, 7, gerarEstoque: false);
        Assert.Equal(7, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        var titles = PayableService.ListTitlesLocal(purchaseId: purchaseId).Count;
        Assert.True(titles > 0);

        PurchaseService.Cancel(purchaseId);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(5, product.CostPrice);
        Assert.Equal(0, PrecoCompra(product));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, TestDataHelper.GetProductFridge(productId));
        Assert.Equal(0, CountEstorno(productId, purchaseId));
        Assert.Equal("cancelada", Status(purchaseId));
        Assert.Empty(PayableService.ListTitlesLocal(purchaseId: purchaseId));
    }

    [Fact]
    public void LoteParcialmenteConsumido_BloqueiaMensagemDeLote()
    {
        using var _ = BeginDb();
        CashService.OpenSession(80, "c2b1-lote");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 8, 5, "LT", "LOTE");
        var purchaseId = CreateClosed(supplier, productId, "LOTE", 20, 7, lot: "B",
            expiry: DateTime.Today.AddDays(200));
        TestDataHelper.FinalizeSimpleCashSale(productId, 8, 8, 64);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
        Assert.Contains("já foi vendida ou movimentada", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("fechada", Status(purchaseId));
        Assert.Equal(12, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void FalhaAntesDoCusto_RollbackCancelamento()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RB1", "RB ANTES");
        var purchaseId = CreateClosed(supplier, productId, "RB ANTES", 10, 7);
        var titles = PayableService.ListTitlesLocal(purchaseId: purchaseId).Count;

        try
        {
            PurchaseService.TestBeforeReverseCancelCost = () =>
                throw new InvalidOperationException("falha controlada antes do custo");
            var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
            Assert.Contains("falha controlada antes do custo", ex.Message);
        }
        finally
        {
            PurchaseService.TestBeforeReverseCancelCost = null;
        }

        var product = ProductService.GetById(productId)!;
        Assert.Equal(6, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal("fechada", Status(purchaseId));
        Assert.Equal(titles, PayableService.ListTitlesLocal(purchaseId: purchaseId).Count);
        Assert.Equal(0, CountEstorno(productId, purchaseId));
    }

    [Fact]
    public void FalhaDepoisDoCusto_RollbackPreservaTudo()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RB2", "RB DEPOIS");
        var purchaseId = CreateClosed(supplier, productId, "RB DEPOIS", 10, 7);
        var titles = PayableService.ListTitlesLocal(purchaseId: purchaseId).Count;

        try
        {
            PurchaseService.TestAfterReverseCancelCost = () =>
                throw new InvalidOperationException("falha controlada depois do custo");
            var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId));
            Assert.Contains("falha controlada depois do custo", ex.Message);
        }
        finally
        {
            PurchaseService.TestAfterReverseCancelCost = null;
        }

        var product = ProductService.GetById(productId)!;
        Assert.Equal(6, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal("fechada", Status(purchaseId));
        Assert.Equal(titles, PayableService.ListTitlesLocal(purchaseId: purchaseId).Count);
        Assert.Equal(0, CountEstorno(productId, purchaseId));
    }

    [Fact]
    public void Reabrir_MesmasProtecoesDaVendaPosterior()
    {
        using var _ = BeginDb();
        CashService.OpenSession(50, "c2b1-reopen");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RP", "REOPEN");
        var purchaseId = CreateClosed(supplier, productId, "REOPEN", 10, 7);
        TestDataHelper.FinalizeSimpleCashSale(productId, 5, 8, 40);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Reopen(purchaseId));
        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage, ex.Message);
        Assert.Equal("fechada", Status(purchaseId));
        Assert.Equal(15, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void Reabrir_SemMovimento_RestauraCustoEPreco()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RP2", "REOPEN OK");
        var purchaseId = CreateClosed(supplier, productId, "REOPEN OK", 10, 7);

        PurchaseService.Reopen(purchaseId);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(5, product.CostPrice);
        Assert.Equal(0, PrecoCompra(product));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal("aberta", Status(purchaseId));
    }

    [Fact]
    public void Audit_RegistraCustoEPrecoCompra()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "AU", "AUDIT");
        var purchaseId = CreateClosed(supplier, productId, "AUDIT", 10, 7);
        PurchaseService.Cancel(purchaseId);

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT user_login, details FROM audit_log
            WHERE action = 'alterar' AND entity = 'produto'
            ORDER BY id DESC LIMIT 5;
            """;
        using var reader = cmd.ExecuteReader();
        string? details = null;
        string? login = null;
        while (reader.Read())
        {
            var d = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (!d.Contains("cancelamento_compra", StringComparison.Ordinal))
                continue;
            details = d;
            login = reader.IsDBNull(0) ? "" : reader.GetString(0);
            break;
        }

        Assert.False(string.IsNullOrWhiteSpace(details));
        Assert.Contains("admin_teste", login);
        Assert.DoesNotContain("token", details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PIN", details, StringComparison.OrdinalIgnoreCase);

        Assert.True(AuditPayloadBuilder.TryParse(details, out var doc));
        var p = doc.Payload;
        Assert.Equal("cancelamento_compra", p.GetProperty("op").GetString());
        Assert.Equal(purchaseId, p.GetProperty("purchase_id").GetInt32());
        Assert.Equal(productId, p.GetProperty("product_id").GetInt32());
        Assert.Equal(6, p.GetProperty("cost_before").GetDouble());
        Assert.Equal(5, p.GetProperty("cost_after").GetDouble());
        Assert.Equal(7, p.GetProperty("preco_compra_before").GetDouble());
        Assert.Equal(0, p.GetProperty("preco_compra_after").GetDouble());
        Assert.Equal("cancelamento_compra", p.GetProperty("source").GetString());
    }

    [Fact]
    public void SemGerarEstoque_AposCompraComEstoque_CancelarB_RestauraMediaEPreco()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "GE2", "B SEM GERAR");
        CreateClosed(supplier, productId, "B SEM GERAR", 10, 7, number: "NF-A");
        var b = CreateClosed(supplier, productId, "B SEM GERAR", 10, 9, number: "NF-B", gerarEstoque: false);
        Assert.Equal(9, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));

        PurchaseService.Cancel(b);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(6, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountEstorno(productId, b));
    }

    [Fact]
    public void CompraComEstoque_DepoisSemGerar_CancelarA_Bloqueia()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "GE3", "A BLOQUEADA");
        var a = CreateClosed(supplier, productId, "A BLOQUEADA", 10, 7, number: "NF-A");
        CreateClosed(supplier, productId, "A BLOQUEADA", 10, 9, number: "NF-B", gerarEstoque: false);

        var ex = Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(a));
        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage, ex.Message);
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal("fechada", Status(a));
    }

    [Fact]
    public void AjusteGeladeira_Bloqueia()
    {
        using var _ = BeginDb();
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "FG", "FRIDGE AJ");
        var purchaseId = CreateClosed(supplier, productId, "FRIDGE AJ", 10, 7);
        StockService.AdjustFridge(productId, StockAdjustMode.Entrada, quantity: 2, notes: "ajuste geladeira");

        Assert.Equal(PurchaseCancelCostRules.UnsafePostMovementMessage,
            Assert.Throws<InvalidOperationException>(() => PurchaseService.Cancel(purchaseId)).Message);
    }

    [Fact]
    public void ParseCostFrom_AuditDaCompra()
    {
        var json = AuditPayloadBuilder.Serialize("x", AuditPayloadBuilder.ProductChange(
            3, "C", "N",
            new Dictionary<string, object> { ["preco_custo"] = new { de = 5.0, para = 6.0 } },
            "compra", 9));
        Assert.True(PurchaseCancelCostRules.TryParseCostFrom(json, 9, 3, out var from));
        Assert.Equal(5, from);
    }

    private static int CreateClosed(
        int supplierId, int productId, string name, double qty, double unit,
        string number = "NF-C2B1", bool gerarEstoque = true,
        string? lot = null, DateTime? expiry = null)
    {
        return PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = number,
            GerarEstoque = gerarEstoque,
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
            VALUES ('fornecedor', 'juridica', 'FORN C2B1', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCigarette(double stock, double sale, double cost, double fator)
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
                'CIGC2B1', 'Rothmans Blue', 'Cigarros', 'UN', $sale, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$sale", sale);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double PrecoCompra(Product product) =>
        ProductExtra.Parse(product.ExtraJson).PrecoCompra;

    private static string Status(int id)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM purchases WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static int CountEstorno(int productId, int purchaseId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM movements
            WHERE product_id = $pid AND IFNULL(operation,'') = 'estorno_compra' AND ref_id = $rid;
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$rid", purchaseId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
