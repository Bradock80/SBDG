using System.Text.Json;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 55B — Caracterização estoque global × lotes/FEFO + query read-only.
/// Não altera produção: documenta comportamento atual.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class StockLotConsistencyCharacterizationTests
{
    [Fact]
    public void Receive_AlteraLote_MasNaoAlteraProductsStock()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(stock: 0, salePrice: 5, costPrice: 2);

        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 10,
            LotNumber = "L-REC",
            ExpiryDate = DateTime.Today.AddMonths(3),
        });

        Assert.Equal(0, TestDataHelper.GetProductStock(productId));
        Assert.Equal(10, SumLots(productId));
    }

    [Fact]
    public void Compra_ComLote_SobeGlobalELoteJuntos()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(stock: 0, salePrice: 8, costPrice: 4, code: "C001", name: "CERVEJA LOTE");
        var supplierId = SeedSupplier();

        PurchaseService.Create(new PurchaseInput
        {
            SupplierId = supplierId,
            EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
            EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
            Number = "NF-55B-1",
            GerarEstoque = true,
            Items =
            [
                new PurchaseItemInput
                {
                    ProductId = productId,
                    ProductName = "CERVEJA LOTE",
                    Quantity = 50,
                    UnitPrice = 4,
                    LotNumber = "LOTE-A",
                    ExpiryDate = DateTime.Today.AddMonths(6),
                },
            ],
        }, closeOnSave: true);

        Assert.Equal(50, TestDataHelper.GetProductStock(productId));
        Assert.Equal(50, SumLots(productId));
    }

    [Fact]
    public void Venda_Fefo_BaixaGlobalELotes_Total90()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 20, notes: "55b");
        var productId = SeedProductWithLots(
            stock: 100,
            ("A", DateTime.Today.AddDays(10), 60),
            ("B", DateTime.Today.AddDays(40), 40));

        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 10, unitPrice: 5, cashReceived: 50);

        Assert.Equal(90, TestDataHelper.GetProductStock(productId));
        Assert.Equal(90, SumLots(productId));
        Assert.Equal(50, GetLotQty(productId, "A")); // FEFO: vence primeiro
        Assert.Equal(40, GetLotQty(productId, "B"));
    }

    [Fact]
    public void Fefo_Ordem_ValidadeDepoisSemValidade()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 20, notes: "55b-fefo");
        var productId = SeedProductWithLots(
            stock: 90,
            ("A", DateTime.Today.AddDays(5), 30),
            ("B", DateTime.Today.AddDays(30), 30),
            ("C", null, 30));

        // Baixa 35 → esgota A (30) e 5 de B
        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 35, unitPrice: 5, cashReceived: 175);

        Assert.Equal(0, GetLotQty(productId, "A"));
        Assert.Equal(25, GetLotQty(productId, "B"));
        Assert.Equal(30, GetLotQty(productId, "C"));
        Assert.Equal(55, TestDataHelper.GetProductStock(productId));
    }

    [Fact]
    public void Venda_LotesInsuficientes_BaixaGlobalEZeraLotes_SemErro()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 20, notes: "55b-insuf");
        var productId = SeedProductWithLots(
            stock: 100,
            ("A", DateTime.Today.AddDays(10), 80));

        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 90, unitPrice: 5, cashReceived: 450);

        Assert.Equal(10, TestDataHelper.GetProductStock(productId));
        Assert.Equal(0, SumLots(productId));
    }

    [Fact]
    public void Cancelamento_RestauraGlobalELoteViaNearest()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.GrantPdvCancelPermission();
        CashService.OpenSession(openingAmount: 50, notes: "55b-cancel");
        var productId = SeedProductWithLots(
            stock: 50,
            ("A", DateTime.Today.AddDays(10), 50));

        var sale = TestDataHelper.FinalizeSimpleCashSale(productId, qty: 10, unitPrice: 5, cashReceived: 50);
        Assert.Equal(40, TestDataHelper.GetProductStock(productId));
        Assert.Equal(40, SumLots(productId));

        PdvService.CancelSale(sale.SaleId);

        Assert.Equal(50, TestDataHelper.GetProductStock(productId));
        Assert.Equal(50, SumLots(productId));
    }

    [Fact]
    public void Inventario_Consolida_AlteraGlobal_NaoAlteraLotes()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProductWithLots(
            stock: 100,
            ("A", DateTime.Today.AddDays(20), 100));

        var session = InventoryService.CreateSession();
        var item = InventoryService.ListItems(session.Id).Single(i => i.ProductId == productId);
        InventoryService.SetCounted(item.Id, 80);
        InventoryService.Consolidate(session.Id);

        Assert.Equal(80, TestDataHelper.GetProductStock(productId));
        Assert.Equal(100, SumLots(productId));

        var rows = StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = true,
            OnlyWithLotsOrExpiryControl = true,
        });
        var row = Assert.Single(rows, r => r.ProductId == productId);
        Assert.Equal(-20, row.Difference);
        Assert.Equal("Lotes > Global", row.Situation);
    }

    [Fact]
    public void AjusteSaldo_AlteraGlobal_NaoAlteraLotes()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProductWithLots(
            stock: 100,
            ("A", DateTime.Today.AddDays(20), 100));

        StockService.Adjust(productId, StockAdjustMode.Saldo, newStock: 120);

        Assert.Equal(120, TestDataHelper.GetProductStock(productId));
        Assert.Equal(100, SumLots(productId));
        var row = Assert.Single(StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = true,
            OnlyWithLotsOrExpiryControl = true,
        }), r => r.ProductId == productId);
        Assert.Equal(20, row.Difference);
    }

    [Fact]
    public void AjusteEntrada_AlteraSoGlobal()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProductWithLots(
            stock: 100,
            ("A", DateTime.Today.AddDays(20), 100));

        StockService.Adjust(productId, StockAdjustMode.Entrada, quantity: 15);

        Assert.Equal(115, TestDataHelper.GetProductStock(productId));
        Assert.Equal(100, SumLots(productId));
    }

    [Fact]
    public void AjusteSaida_AlteraSoGlobal()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProductWithLots(
            stock: 100,
            ("A", DateTime.Today.AddDays(20), 100));

        StockService.Adjust(productId, StockAdjustMode.Saida, quantity: 15);

        Assert.Equal(85, TestDataHelper.GetProductStock(productId));
        Assert.Equal(100, SumLots(productId));
    }

    [Fact]
    public void ProdutoSemLotes_VendaFunciona_ENaoApareceNoFiltroPrincipal()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 20, notes: "55b-semlote");
        var productId = TestDataHelper.SeedSimpleProduct(stock: 50, salePrice: 5, costPrice: 2, code: "S50", name: "SEM LOTE");

        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 5, unitPrice: 5, cashReceived: 25);
        Assert.Equal(45, TestDataHelper.GetProductStock(productId));

        var principal = StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = false,
            OnlyWithLotsOrExpiryControl = true,
        });
        Assert.DoesNotContain(principal, r => r.ProductId == productId);

        var todos = StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = false,
            OnlyWithLotsOrExpiryControl = false,
        });
        Assert.Contains(todos, r => r.ProductId == productId && r.GlobalStock == 45 && r.LotsStock == 0);
    }

    [Fact]
    public void NullExpiry_FicaPorUltimoNoFefo()
    {
        using var db = TempDatabase.Create();
        CashService.OpenSession(openingAmount: 20, notes: "55b-null");
        var productId = SeedProductWithLots(
            stock: 40,
            ("COM", DateTime.Today.AddDays(15), 20),
            ("SEM", null, 20));

        TestDataHelper.FinalizeSimpleCashSale(productId, qty: 25, unitPrice: 5, cashReceived: 125);

        Assert.Equal(0, GetLotQty(productId, "COM"));
        Assert.Equal(15, GetLotQty(productId, "SEM"));
    }

    [Fact]
    public void Query_Igualdade_NaoApareceComoDivergente()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProductWithLots(
            stock: 100,
            ("A", DateTime.Today.AddDays(20), 100));

        var rows = StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = true,
            OnlyWithLotsOrExpiryControl = true,
        });
        Assert.DoesNotContain(rows, r => r.ProductId == productId);
    }

    [Fact]
    public void Query_GlobalMaior_DifferenceMais20()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProductWithLots(
            stock: 120,
            ("A", DateTime.Today.AddDays(20), 100));

        var row = Assert.Single(StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = true,
            OnlyWithLotsOrExpiryControl = true,
        }), r => r.ProductId == productId);
        Assert.Equal(20, row.Difference);
        Assert.Equal("Global > Lotes", row.Situation);
    }

    [Fact]
    public void Query_LotesMaior_DifferenceMenos20()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProductWithLots(
            stock: 80,
            ("A", DateTime.Today.AddDays(20), 100));

        var row = Assert.Single(StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = true,
            OnlyWithLotsOrExpiryControl = true,
        }), r => r.ProductId == productId);
        Assert.Equal(-20, row.Difference);
        Assert.Equal("Lotes > Global", row.Situation);
    }

    [Fact]
    public void Query_Tolerancia_NaoAparece()
    {
        using var db = TempDatabase.Create();
        var productId = TestDataHelper.SeedSimpleProduct(stock: 100.0, salePrice: 5, costPrice: 2, code: "TOL", name: "TOLERANCIA");
        ProductLotService.Receive(new ProductLotReceiveInput
        {
            ProductId = productId,
            Quantity = 100.0005,
            LotNumber = "T",
            ExpiryDate = DateTime.Today.AddMonths(1),
        });

        // |100 - 100.0005| = 0.0005 <= 0.0009
        var rows = StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = true,
            OnlyWithLotsOrExpiryControl = true,
        });
        Assert.DoesNotContain(rows, r => r.ProductId == productId);
    }

    [Fact]
    public void Query_ControleValidadeSemLote_ApareceNoFiltroPrincipalQuandoDivergente()
    {
        using var db = TempDatabase.Create();
        var productId = SeedProductWithExpiryControl(stock: 30, code: "CV1", name: "COM CONTROLE");

        var rows = StockLotConsistencyService.List(new StockLotConsistencyQuery
        {
            OnlyDivergent = true,
            OnlyWithLotsOrExpiryControl = true,
        });
        var row = Assert.Single(rows, r => r.ProductId == productId);
        Assert.True(row.ExpiryControl);
        Assert.False(row.HasLots);
        Assert.Equal(30, row.Difference);
    }

    private static int SeedProductWithExpiryControl(double stock, string code, string name)
    {
        var extra = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["controle_validade"] = true,
        });
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (code, name, unit, sale_price, stock, cost_price, active, extra_json)
            VALUES ($code, $name, 'UN', 5, $stock, 2, 1, $extra);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedProductWithLots(double stock, params (string Lot, DateTime? Expiry, double Qty)[] lots)
    {
        var productId = TestDataHelper.SeedSimpleProduct(stock, salePrice: 5, costPrice: 2,
            code: $"L{Guid.NewGuid():N}"[..8], name: $"PROD LOTE {Guid.NewGuid():N}"[..18]);
        foreach (var (lot, expiry, qty) in lots)
        {
            ProductLotService.Receive(new ProductLotReceiveInput
            {
                ProductId = productId,
                Quantity = qty,
                LotNumber = lot,
                ExpiryDate = expiry,
            });
        }
        return productId;
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORNECEDOR 55B', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
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
}
