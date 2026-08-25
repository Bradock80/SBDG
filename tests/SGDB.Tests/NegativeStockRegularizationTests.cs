using System.IO;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 69L-B1 — regularização administrativa de estoque negativo no Ajuste de Saldo.
/// Compra com estoque negativo continua bloqueada.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class NegativeStockRegularizationTests
{
    [Fact]
    public void Saldo_Neg174_Para0_SemCusto_PreservaCostPrice()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(-174, 15, 11.50, "N174", "NEG ZERO");

        var result = StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 0, notes: "regulariza");

        Assert.Equal(-174, result.StockBefore);
        Assert.Equal(0, result.StockAfter);
        Assert.Equal(174, result.Quantity);
        Assert.Equal(0, TestDataHelper.GetProductStock(id));
        Assert.Equal(11.50, ProductService.GetById(id)!.CostPrice);
    }

    [Fact]
    public void Saldo_Neg174_Para0_ComCusto_SubstituiCostPrice()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedWithCompra(-174, 11.50, "N174C", "NEG ZERO CUSTO");

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 0, unitCost: 12, notes: "regulariza");

        var p = ProductService.GetById(id)!;
        Assert.Equal(0, p.Stock);
        Assert.Equal(12, p.CostPrice);
        Assert.Equal(12, PrecoCompra(id));
    }

    [Fact]
    public void Saldo_Neg174_Para20_SemCusto_PreservaCostPrice()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(-174, 15, 11.50, "N20", "NEG POS");

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 20);

        Assert.Equal(20, TestDataHelper.GetProductStock(id));
        Assert.Equal(11.50, ProductService.GetById(id)!.CostPrice);
    }

    [Fact]
    public void Saldo_Neg174_Para20_ComCusto_SubstituiSemMediaPonderada()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedWithCompra(-174, 11.50, "N20C", "NEG POS CUSTO");

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 20, unitCost: 12);

        // Substitui — não média absurda com base negativa (ex.: não ~11,8x).
        Assert.Equal(20, TestDataHelper.GetProductStock(id));
        Assert.Equal(12, ProductService.GetById(id)!.CostPrice);
        Assert.Equal(12, PrecoCompra(id));
    }

    [Fact]
    public void Saldo_Negativo_Movement_RegistraBeforeAfterEOperacao()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(-174, 15, 11.50, "NMOV", "NEG MOV");

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 0, notes: "zera físico");

        var mov = LatestMovement(id);
        Assert.Equal("entrada", mov.Type);
        Assert.Equal("ajuste_manual", mov.Operation);
        Assert.Equal(-174, mov.Before);
        Assert.Equal(0, mov.After);
        Assert.Equal(174, mov.Qty);
        Assert.Equal(11.50, mov.UnitPrice);
        Assert.Contains("Ajuste saldo: -174", mov.Notes, StringComparison.Ordinal);
        Assert.Contains("→ 0", mov.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Saldo_NegativoComCusto_Movement_UsaCustoInformado()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(-50, 15, 8, "NMOVC", "NEG MOV CUSTO");

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 10, unitCost: 9.25);

        var mov = LatestMovement(id);
        Assert.Equal(9.25, mov.UnitPrice);
        Assert.Equal(-50, mov.Before);
        Assert.Equal(10, mov.After);
        Assert.Equal(60, mov.Qty);
    }

    [Fact]
    public void Cigarro_RegularizaFisico_PreservaCustoMaco()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedCigarette(stock: -174, cost: 11.50, fator: 20);

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 0);

        var p = ProductService.GetById(id)!;
        Assert.Equal(0, p.Stock);
        Assert.Equal(11.50, p.CostPrice);
        Assert.Equal(11.50, PrecoCompra(id));
        Assert.Equal(20, ProductExtra.Parse(p.ExtraJson).FatorEmbalagem);
    }

    [Fact]
    public void Cigarro_RegularizaComCusto_SubstituiCustoMaco_StockFisico()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = SeedCigarette(stock: -174, cost: 11.50, fator: 20);

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 200, unitCost: 12);

        var p = ProductService.GetById(id)!;
        Assert.Equal(200, p.Stock); // físico
        Assert.Equal(12, p.CostPrice); // maço, não 12/20
        Assert.Equal(12, PrecoCompra(id));
    }

    [Fact]
    public void Geladeira_TotalNegativo_RegularizaDeposito_SemMedia()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        // Depósito -100 + geladeira 20 = total -80 < 0
        var id = TestDataHelper.SeedSimpleProduct(-100, 10, 5, "GFR", "GEL FRIDGE");
        TestDataHelper.SetProductFridge(id, 20);

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 0, unitCost: 7);

        Assert.Equal(0, TestDataHelper.GetProductStock(id));
        Assert.Equal(20, TestDataHelper.GetProductFridge(id));
        // Substitui 7 — não média com base -80.
        Assert.Equal(7, ProductService.GetById(id)!.CostPrice);
    }

    [Fact]
    public void Geladeira_TotalJaNaoNegativo_ContinuaMediaPonderada()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        // Depósito -10 + geladeira 30 = total 20 >= 0 → regra normal
        var id = SeedWithCompra(-10, 5, "GOK", "GEL OK");
        TestDataHelper.SetProductFridge(id, 30);

        StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 0, unitCost: 8);

        // qty entrada = 10; média (20*5 + 10*8)/30 = 6
        Assert.Equal(0, TestDataHelper.GetProductStock(id));
        Assert.Equal(6, ProductService.GetById(id)!.CostPrice);
        Assert.Equal(8, PrecoCompra(id));
    }

    [Fact]
    public void Compra_ComEstoqueTotalNegativo_ContinuaBloqueada()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var supplier = SeedSupplier();
        var id = TestDataHelper.SeedSimpleProduct(-5, 8, 5, "BUYNEG", "COMPRA NEG");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PurchaseService.Create(new PurchaseInput
            {
                SupplierId = supplier,
                EmissionDate = DateTime.Today.ToString("yyyy-MM-dd"),
                EntryDate = DateTime.Today.ToString("yyyy-MM-dd"),
                Number = "NF-NEG-B1",
                GerarEstoque = true,
                UpdateAverageCost = true,
                Items =
                [
                    new PurchaseItemInput
                    {
                        ProductId = id,
                        ProductName = "COMPRA NEG",
                        Quantity = 10,
                        UnitPrice = 7,
                    },
                ],
            }, closeOnSave: true));

        Assert.Contains("Estoque total negativo", ex.Message);
        Assert.Equal(-5, TestDataHelper.GetProductStock(id));
        Assert.Equal(5, ProductService.GetById(id)!.CostPrice);
    }

    [Fact]
    public void Saldo_Regularizacao_FalhaAntesMovement_FazRollback()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var id = TestDataHelper.SeedSimpleProduct(-174, 15, 11.50, "ROLL", "ROLLBACK");
        var movBefore = TestDataHelper.CountMovements(id);

        try
        {
            StockService.TestBeforeAdjustMovement = _ =>
                throw new InvalidOperationException("falha controlada na regularizacao");
            var ex = Assert.Throws<InvalidOperationException>(
                () => StockService.Adjust(id, StockAdjustMode.Saldo, newStock: 0));
            Assert.Contains("falha controlada", ex.Message);
        }
        finally
        {
            StockService.TestBeforeAdjustMovement = null;
        }

        Assert.Equal(-174, TestDataHelper.GetProductStock(id));
        Assert.Equal(11.50, ProductService.GetById(id)!.CostPrice);
        Assert.Equal(movBefore, TestDataHelper.CountMovements(id));
    }

    [Fact]
    public void ZeroNegativeStock_ContinuaZerandoSemAlterarCusto()
    {
        using var db = TempDatabase.Create();
        TestDataHelper.SetSessionRole("admin");
        var a = TestDataHelper.SeedSimpleProduct(-12, 10, 4, "Z1", "ZERO A");
        var b = TestDataHelper.SeedSimpleProduct(-3, 10, 9, "Z2", "ZERO B");
        var ok = TestDataHelper.SeedSimpleProduct(5, 10, 2, "Z3", "ZERO OK");

        var n = StockService.ZeroNegativeStock();

        Assert.Equal(2, n);
        Assert.Equal(0, TestDataHelper.GetProductStock(a));
        Assert.Equal(0, TestDataHelper.GetProductStock(b));
        Assert.Equal(5, TestDataHelper.GetProductStock(ok));
        Assert.Equal(4, ProductService.GetById(a)!.CostPrice);
        Assert.Equal(9, ProductService.GetById(b)!.CostPrice);
    }

    [Fact]
    public void UiSource_NaoExigeCustoNaRegularizacaoDeNegativo()
    {
        var src = File.ReadAllText(Path.Combine(AppSourceRoot(), "Views", "StockAdjustModuleView.xaml.cs"));
        Assert.Contains("totalBefore >= -1e-4", src, StringComparison.Ordinal);
        Assert.Contains("optionalCost", src, StringComparison.Ordinal);
        Assert.Contains("substitui", src, StringComparison.OrdinalIgnoreCase);
    }

    private static int SeedWithCompra(double stock, double cost, string code, string name)
    {
        var extra = new ProductExtra { PrecoCompra = cost };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, $name, 'UN', 15, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedSupplier()
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO people (person_type, person_kind, name, active, roles_json)
            VALUES ('fornecedor', 'juridica', 'FORN 69LB1', 1, '{"ativo":true,"fornecedores":true}');
            SELECT last_insert_rowid();
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int SeedCigarette(double stock, double cost, double fator)
    {
        var extra = new ProductExtra
        {
            FatorEmbalagem = fator,
            QtdAtacado = fator,
            PrecoAvulso = 1.50,
            PrecoAtacado = cost + 1,
            PrecoCompra = cost,
        };
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, group_name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                'CIG69LB1', 'ROTHMANS HAND SELECTED RED', 'Cigarros', 'UN', $sale, $stock, $cost, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$sale", cost + 1);
        cmd.Parameters.AddWithValue("$stock", stock);
        cmd.Parameters.AddWithValue("$cost", cost);
        cmd.Parameters.AddWithValue("$extra", extra.ToJson());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static double PrecoCompra(int productId)
    {
        var p = ProductService.GetById(productId)!;
        return ProductExtra.Parse(p.ExtraJson).PrecoCompra;
    }

    private static MovRow LatestMovement(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT movement_type, quantity, IFNULL(stock_before,0), IFNULL(stock_after,0),
                   IFNULL(unit_price,0), IFNULL(operation,''), IFNULL(notes,'')
            FROM movements
            WHERE product_id = $id
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", productId);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        return new MovRow(
            reader.GetString(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private static string AppSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "src", "SGDB.App");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "SGDB.App"));
    }

    private sealed record MovRow(
        string Type, double Qty, double Before, double After,
        double UnitPrice, string Operation, string Notes);
}
