using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69D-C1 — custo médio atômico (depósito + geladeira) na transação da compra.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class PurchaseAverageCostTests
{
    private static readonly JsonSerializerOptions NetworkJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static TempDatabase BeginDb()
    {
        PurchaseService.TestBeforeApplySalePrice = null;
        PurchaseService.TestAfterApplySalePrice = null;
        PurchaseService.TestBeforeApplyAverageCost = null;
        PurchaseService.TestAfterApplyAverageCost = null;
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        return db;
    }

    [Fact]
    public void Deposito10_Mais10a7_Custo6_PrecoCompra7()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "C1", "MEDIA 6");

        CreateClosed(supplier, productId, "MEDIA 6", 10, 7);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(6, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void Deposito100_Mais10a7_Custo518()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(100, 8, 5, "C2", "MEDIA 518");

        CreateClosed(supplier, productId, "MEDIA 518", 10, 7);

        Assert.Equal(5.18, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(7, PrecoCompra(ProductService.GetById(productId)!));
    }

    [Fact]
    public void Deposito0_Geladeira20_Mais10a7_Custo567()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 8, 5, "C3", "GELA 20");
        TestDataHelper.SetProductFridge(productId, 20);

        CreateClosed(supplier, productId, "GELA 20", 10, 7);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(5.67, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(20, TestDataHelper.GetProductFridge(productId));
    }

    [Fact]
    public void Deposito10_Geladeira90_Mais10a7_Custo518()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "C4", "GELA 90");
        TestDataHelper.SetProductFridge(productId, 90);

        CreateClosed(supplier, productId, "GELA 90", 10, 7);

        Assert.Equal(5.18, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
        Assert.Equal(90, TestDataHelper.GetProductFridge(productId));
    }

    [Fact]
    public void EstoqueTotalZero_Entrada7_Custo7()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 8, 5, "C5", "ZERO");

        CreateClosed(supplier, productId, "ZERO", 10, 7);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(7, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
    }

    [Fact]
    public void ProdutoNovo_NaoSofreDuplaMedia()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(0, 8, 7, "C7", "NOVO 7");

        CreateClosed(supplier, productId, "NOVO 7", 10, 7);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(7, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void DuasLinhasMesmoProduto_MediaAgregada633()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "C8", "DUAS LINHAS");

        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-2L",
            GerarEstoque = true,
            Items =
            [
                Item(productId, "DUAS LINHAS", 10, 6),
                Item(productId, "DUAS LINHAS", 10, 8),
            ],
        }, closeOnSave: true);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(6.33, product.CostPrice);
        Assert.Equal(8, PrecoCompra(product));
        Assert.Equal(30, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void FalhaAntesDoCusto_RollbackCompraEstoquePrecoCompra()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RB1", "ROLLBACK ANTES CUSTO");
        SetPrecoCompra(productId, 5);

        try
        {
            PurchaseService.TestBeforeApplyAverageCost = () =>
                throw new InvalidOperationException("falha controlada antes do custo");
            var ex = Assert.Throws<InvalidOperationException>(() =>
                CreateClosed(supplier, productId, "ROLLBACK ANTES CUSTO", 10, 7));
            Assert.Contains("falha controlada antes do custo", ex.Message);
        }
        finally
        {
            PurchaseService.TestBeforeApplyAverageCost = null;
        }

        var product = ProductService.GetById(productId)!;
        Assert.Equal(5, product.CostPrice);
        Assert.Equal(5, PrecoCompra(product));
        Assert.Equal(8, product.SalePrice);
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountPurchases());
    }

    [Fact]
    public void FalhaDepoisDoCusto_RollbackPreservaTudo()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "RB2", "ROLLBACK DEPOIS CUSTO");
        SetPrecoCompra(productId, 5);

        try
        {
            PurchaseService.TestAfterApplyAverageCost = () =>
                throw new InvalidOperationException("falha controlada depois do custo");
            var ex = Assert.Throws<InvalidOperationException>(() =>
                CreateClosed(supplier, productId, "ROLLBACK DEPOIS CUSTO", 10, 7, sale: 9, updateSale: true));
            Assert.Contains("falha controlada depois do custo", ex.Message);
        }
        finally
        {
            PurchaseService.TestAfterApplyAverageCost = null;
        }

        var product = ProductService.GetById(productId)!;
        Assert.Equal(5, product.CostPrice);
        Assert.Equal(5, PrecoCompra(product));
        Assert.Equal(8, product.SalePrice);
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountPurchases());
        Assert.Equal(0, CountTable("payable_titles"));
    }

    [Fact]
    public void Host_CreateLocal_AplicaCustoNaMesmaTx()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "HT", "HOST CUSTO");

        PurchaseService.CreateLocal(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-HC",
            GerarEstoque = true,
            Items = [Item(productId, "HOST CUSTO", 10, 7)],
        }, closeOnSave: true);

        Assert.Equal(6, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(20, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void TransferenciaGeladeira_NaoMudaCusto()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var productId = TestDataHelper.SeedSimpleProduct(30, 8, 5, "TR", "TRANSFER");
        TestDataHelper.SetProductFridge(productId, 0);

        StockService.TransferWarehouseToFridge(productId, 10);
        Assert.Equal(5, ProductService.GetById(productId)!.CostPrice);

        StockService.TransferFridgeToWarehouse(productId, 4);
        Assert.Equal(5, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(24, TestDataHelper.GetProductStock(productId));
        Assert.Equal(6, TestDataHelper.GetProductFridge(productId));
    }

    [Fact]
    public void AjusteEntradaComCusto_UsaDepositoMaisGeladeira()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var productId = TestDataHelper.SeedSimpleProduct(0, 8, 5, "AJ", "AJUSTE MEDIA");
        TestDataHelper.SetProductFridge(productId, 20);

        StockService.Adjust(productId, StockAdjustMode.Entrada, quantity: 10, unitCost: 7);

        Assert.Equal(5.67, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(20, TestDataHelper.GetProductFridge(productId));
        Assert.Equal(7, PrecoCompra(ProductService.GetById(productId)!));
    }

    [Fact]
    public void InventarioSemCusto_NaoMudaCostPrice()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var productId = TestDataHelper.SeedSimpleProduct(100, 8, 5, "INV", "INVENTARIO");
        var session = InventoryService.CreateSession();
        var item = InventoryService.ListItems(session.Id).Single(i => i.ProductId == productId);
        InventoryService.SetCounted(item.Id, 80);

        InventoryService.Consolidate(session.Id);

        Assert.Equal(5, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(80, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void Cigarro_UnidadeEmMacos_200mais100a12_Custo1067()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var cig = SeedCigarette(stock: 200, sale: 10, cost: 10, fator: 20);

        CreateClosed(supplier, cig, "Rothmans Blue", qty: 100, unit: 0.60);

        var product = ProductService.GetById(cig)!;
        Assert.Equal(10.67, product.CostPrice);
        Assert.Equal(12, PrecoCompra(product));
        Assert.Equal(300, TestDataHelper.GetProductStock(cig));
    }

    [Fact]
    public void Cigarro_EstoqueAnteriorMaisCompra_EGeladeira()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var cig = SeedCigarette(stock: 0, sale: 10, cost: 10, fator: 20);
        TestDataHelper.SetProductFridge(cig, 200);

        CreateClosed(supplier, cig, "Rothmans Blue", qty: 100, unit: 0.60);

        Assert.Equal(10.67, ProductService.GetById(cig)!.CostPrice);
        Assert.Equal(100, TestDataHelper.GetProductStock(cig));
        Assert.Equal(200, TestDataHelper.GetProductFridge(cig));
    }

    [Fact]
    public void Cigarro_MacoAvulso69B_Preservados()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var cig = SeedCigarette(stock: 200, sale: 10, cost: 8, fator: 20, avulso: 1.50, atacado: 10);

        CreateClosed(supplier, cig, "Rothmans Blue", 20, 0.40, sale: 10, updateSale: false);

        var product = ProductService.GetById(cig)!;
        var extra = ProductExtra.Parse(product.ExtraJson);
        Assert.Equal(10, product.SalePrice);
        Assert.Equal(1.50, extra.PrecoAvulso);
        Assert.Equal(10, extra.PrecoAtacado);
        Assert.Equal(20, extra.FatorEmbalagem);

        var maco = PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Maco);
        var avulso = PdvService.ResolveManualSale(product, PdvCigaretteSaleMode.Avulso);
        Assert.Equal(10, maco.UnitPrice);
        Assert.Equal(1.50, avulso.UnitPrice);
    }

    [Fact]
    public void QuantidadeInvalida_NaN_Bloqueia()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "NAN", "NAN QTD");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateClosed(supplier, productId, "NAN QTD", double.NaN, 7));
        Assert.Contains("Quantidade", ex.Message);
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, CountPurchases());
    }

    [Fact]
    public void PrecoUnitarioInfinito_Bloqueia()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "INF", "INF PRECO");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateClosed(supplier, productId, "INF PRECO", 10, double.PositiveInfinity));
        Assert.Contains("Preço unitário inválido", ex.Message);
        Assert.Equal(5, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(0, CountPurchases());
    }

    [Fact]
    public void EstoqueTotalNegativo_BloqueiaCompra()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(-5, 8, 5, "NEG", "NEGATIVO");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateClosed(supplier, productId, "NEGATIVO", 10, 7));
        Assert.Contains("Estoque total negativo", ex.Message);
        Assert.Equal(-5, TestDataHelper.GetProductStock(productId));
        Assert.Equal(5, ProductService.GetById(productId)!.CostPrice);
        Assert.Equal(0, CountPurchases());
    }

    [Fact]
    public void SemGerarEstoque_AtualizaUltimoCusto_NaoMoveEstoque()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "SG", "SEM ESTOQUE");

        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplier,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-SG",
            GerarEstoque = false,
            Items = [Item(productId, "SEM ESTOQUE", 10, 7)],
        }, closeOnSave: true);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(7, product.CostPrice);
        Assert.Equal(7, PrecoCompra(product));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void TelaCompras_NaoChamaMaisUpdatePosCommit()
    {
        var form = File.ReadAllText(Path.Combine(AppSourceRoot(), "Views", "PurchaseFormWindow.xaml.cs"));
        Assert.DoesNotContain("ApplyPurchasePricesToProducts", form);
        var client = File.ReadAllText(Path.Combine(AppSourceRoot(), "Services", "StoreNetworkClient.cs"));
        var create = client[client.IndexOf("public static int CreatePurchase", StringComparison.Ordinal)..];
        create = create[..create.IndexOf("public static void UpdatePurchase", StringComparison.Ordinal)];
        Assert.DoesNotContain("UpdateProduct", create);
    }

    [Fact]
    public void Formula_Geladeira20_Mais10a7_567()
    {
        var cost = PurchaseAverageCostRules.WeightedAverageFromLines(
            warehouseBefore: 0, fridgeBefore: 20, costBefore: 5,
            name: "AGUA", group: "Bebidas", packFactor: 1,
            lines: [(10, 7)]);
        Assert.Equal(5.67, cost);
    }

    [Fact]
    public void Dto_SerializaUpdateAverageCostPadraoTrue()
    {
        var input = new PurchaseInput
        {
            SupplierId = 1,
            EmissionDate = "2026-08-22",
            EntryDate = "2026-08-22",
            Number = "1",
            Items = [Item(1, "X", 1, 5)],
        };
        Assert.True(input.UpdateAverageCost);
        var json = JsonSerializer.Serialize(input, NetworkJson);
        Assert.Contains("\"updateAverageCost\":true", json);
    }

    [Fact]
    public void CancelCompra_RestauraMediaDoDeposito()
    {
        using var _ = BeginDb();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var productId = TestDataHelper.SeedSimpleProduct(10, 8, 5, "CAN", "CANCEL MEDIA");

        var purchaseId = CreateClosed(supplier, productId, "CANCEL MEDIA", 10, 7);
        Assert.Equal(6, ProductService.GetById(productId)!.CostPrice);

        PurchaseService.Cancel(purchaseId);

        var product = ProductService.GetById(productId)!;
        Assert.Equal(5, product.CostPrice);
        Assert.Equal(0, PrecoCompra(product));
        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
    }

    private static int CreateClosed(
        int supplierId, int productId, string name, double qty, double unit,
        double sale = 8, bool updateSale = false, string number = "NF-C1")
    {
        return PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = number,
            GerarEstoque = true,
            Items = [Item(productId, name, qty, unit, sale, updateSale)],
        }, closeOnSave: true);
    }

    private static PurchaseItemInput Item(
        int productId, string name, double qty, double unit, double sale = 8, bool update = false) =>
        new()
        {
            ProductId = productId,
            ProductName = name,
            Quantity = qty,
            UnitPrice = unit,
            SalePrice = sale,
            UpdateSalePrice = update,
        };

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN 69DC1', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCigarette(
        double stock, double sale, double cost, double fator, double avulso = 1.50, double atacado = 10)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAvulso = avulso,
            PrecoAtacado = atacado,
            PrecoCompra = cost,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIG69DC', 'Rothmans Blue', 'Cigarros', 'UN', $sale, $stock, $cost, 1, $extra
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

    private static void SetPrecoCompra(int productId, double value)
    {
        var product = ProductService.GetById(productId)!;
        var extra = ProductExtra.Parse(product.ExtraJson);
        extra.PrecoCompra = value;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET extra_json = $extra WHERE id = $id;";
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static int CountPurchases() => CountTable("purchases");

    private static int CountTable(string table)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static string AppSourceRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SGDB.App"));
}
