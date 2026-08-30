using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// ETAPA 70C-B1 — motor de giro físico (VMV, cobertura, última venda).
/// Bancos isolados em %TEMP%\SGDB.Tests. Não toca deposito.db nem o EXE da loja.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryIntelligenceTests
{
    private static readonly DateTime Today = DateTime.Today;
    private const double Tol = 0.0001;

    private static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    private static TempDatabase BeginWithCash()
    {
        var db = Begin();
        CashService.OpenSession(100, "70c-b1");
        return db;
    }

    private static ProductTurnoverRow Row(int productId, DateTime? today = null)
    {
        var row = InventoryIntelligenceService.GetByProductId(productId, today ?? Today);
        Assert.NotNull(row);
        return row!;
    }

    private static int SaleItemId(int saleId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM sale_items WHERE sale_id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetSaleDate(int saleId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sales SET session_date = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$id", saleId);
        cmd.ExecuteNonQuery();
    }

    private static void SetProductCreated(int productId, DateTime date)
    {
        // products.created_at é UTC naive; gravamos 03:00 UTC = 00:00 Brasília.
        var utc = date.Date - DateBrHelper.BrazilOffset;
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE products SET created_at = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    private static void StampInbound(int productId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at, operation
            ) VALUES (
              $pid, 'entrada', 1, 0, '70c inbound', $at, 'entrada_compra'
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$at", date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static int InsertLegacySale(
        int productId, double quantity, DateTime sessionDate, double stockQty = 0, int cancelled = 0)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        int saleId;
        using (var sale = conn.CreateCommand())
        {
            sale.Transaction = tx;
            sale.CommandText = """
                INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
                VALUES ($d, $total, 'Dinheiro', $c, $created);
                SELECT last_insert_rowid();
                """;
            sale.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd"));
            sale.Parameters.AddWithValue("$total", quantity * 10);
            sale.Parameters.AddWithValue("$c", cancelled);
            sale.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
            saleId = Convert.ToInt32(sale.ExecuteScalar());
        }
        using (var item = conn.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                INSERT INTO sale_items (
                  sale_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, stock_qty
                ) VALUES ($sale, $pid, 'LEG', 'Legado', 'UN', $qty, 10, $sub, $stock);
                """;
            item.Parameters.AddWithValue("$sale", saleId);
            item.Parameters.AddWithValue("$pid", productId);
            item.Parameters.AddWithValue("$qty", quantity);
            item.Parameters.AddWithValue("$sub", quantity * 10);
            item.Parameters.AddWithValue("$stock", stockQty);
            item.ExecuteNonQuery();
        }
        tx.Commit();
        return saleId;
    }

    private static void InsertRawMovement(
        int productId, string type, double qty, string operation, string? refType = null, int? refId = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at,
              operation, ref_type, ref_id
            ) VALUES (
              $pid, $type, $qty, 0, 'teste 70c', datetime('now','localtime'),
              $op, $refType, $refId
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$qty", qty);
        cmd.Parameters.AddWithValue("$op", operation);
        cmd.Parameters.AddWithValue("$refType", (object?)refType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$refId", refId is int id ? id : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static int SeedKit(int componentId, double componentQty, string code = "KIT1")
    {
        var extra = new ProductExtra
        {
            Composicao = true,
            ComposicaoItens =
            [
                new ProductCompositionItem
                {
                    ProductId = componentId,
                    Quantity = componentQty,
                    Code = "CMP",
                    Name = "Componente",
                    Unit = "UN",
                },
            ],
        }.ToJson();

        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (
                code, name, unit, sale_price, stock, cost_price, active, extra_json
            ) VALUES (
                $code, 'Kit Teste', 'UN', 20, 0, 0, 1, $extra
            );
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$extra", extra);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void AssertFinite(ProductTurnoverRow row)
    {
        Assert.True(InventoryIntelligenceEngine.IsFinite(row.Vmv7));
        Assert.True(InventoryIntelligenceEngine.IsFinite(row.Vmv30));
        Assert.True(InventoryIntelligenceEngine.IsFinite(row.Vmv90));
        Assert.True(InventoryIntelligenceEngine.IsFinite(row.TotalStock));
        if (row.CoverageDays is double c)
            Assert.True(InventoryIntelligenceEngine.IsFinite(c));
        Assert.False(double.IsNaN(row.Vmv7));
        Assert.False(double.IsInfinity(row.Vmv7));
        Assert.False(double.IsNaN(row.Vmv30));
        Assert.False(double.IsInfinity(row.Vmv30));
    }

    [Fact]
    public void AmbienteIsolado_NaoUsaBancoDaLoja()
    {
        using var db = Begin();
        Assert.Contains("SGDB.Tests", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deposito.db", DatabaseService.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void T01_VendaNormal_ContaQuantidadeFisica()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "V1", "Venda Normal");
        TestDataHelper.FinalizeSimpleCashSale(id, 3, 10, 30);
        var row = Row(id);
        Assert.Equal(3, row.Vmv7, Tol);
        Assert.Equal(3, row.Vmv30, Tol);
        AssertFinite(row);
    }

    [Fact]
    public void T02_MultiplasVendas_Somam()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "V2", "Multiplas");
        TestDataHelper.FinalizeSimpleCashSale(id, 2, 10, 20);
        TestDataHelper.FinalizeSimpleCashSale(id, 5, 10, 50);
        Assert.Equal(7, Row(id).Vmv7, Tol);
    }

    [Fact]
    public void T03_VendaCancelada_NaoEntraNoVmv()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "V3", "Cancelada");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 4, 10, 40);
        PdvService.CancelSale(sale.SaleId);
        Assert.Equal(0, Row(id).Vmv7, Tol);
    }

    [Fact]
    public void T04_CanceladaMantemSaleItems_MasNaoEntra()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "V4", "Cancelada Itens");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 4, 10, 40);
        PdvService.CancelSale(sale.SaleId);
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), SUM(quantity) FROM sale_items WHERE sale_id = $id;";
        cmd.Parameters.AddWithValue("$id", sale.SaleId);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(1, r.GetInt32(0));
        Assert.Equal(4, r.GetDouble(1), Tol);
        Assert.Equal(0, Row(id).Vmv30, Tol);
        Assert.Null(Row(id).LastValidSaleDate);
    }

    [Fact]
    public void T05_DevolucaoParcial_ReduzQuantidadeLiquida()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "D1", "Dev Parcial");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 10, 10, 100);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 3 }],
        });
        Assert.Equal(7, Row(id).Vmv7, Tol);
    }

    [Fact]
    public void T06_DevolucaoTotal_EmDataPosterior_NaoApagaVendaOriginal()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "D2", "Dev Total");
        StampInbound(id, Today.AddDays(-40));
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 10, 10, 100);
        var saleDate = Today.AddDays(-10);
        SetSaleDate(sale.SaleId, saleDate);
        SetProductCreated(id, Today.AddDays(-40));
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 10 }],
        });
        var row = Row(id);
        Assert.Equal(0, row.Vmv30, Tol);
        Assert.Equal(saleDate.Date, row.LastValidSaleDate);
        Assert.Equal(10, row.DaysWithoutSale);
    }

    [Fact]
    public void T07_DevolucaoEmDataPosterior_NaoReescreveOPassado()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "D3", "Dev Posterior");
        StampInbound(id, Today.AddDays(-40));
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 10, 10, 100);
        SetSaleDate(sale.SaleId, Today.AddDays(-10));
        SetProductCreated(id, Today.AddDays(-90));
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 2 }],
        });
        var row = Row(id);
        // Janela 7: só devolução (bruto 0). Demanda operacional = MAX(0, 0-2) = 0 — nunca VMV negativo.
        // Janela 30: bruto 10 − devolução 2 = 8.
        Assert.Equal(0, row.Vmv7, Tol);
        Assert.Equal(8 / 30.0, row.Vmv30, 0.001);
        Assert.Equal(Today.AddDays(-10), row.LastValidSaleDate);
    }

    [Fact]
    public void T08_TrocaAparaB_EventosNaDataDaTroca()
    {
        using var db = BeginWithCash();
        var a = TestDataHelper.SeedSimpleProduct(100, 10, 4, "TA", "Troca A");
        var b = TestDataHelper.SeedSimpleProduct(50, 12, 5, "TB", "Troca B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 2 }],
            NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 2, UnitPrice = 12 }],
            PaymentType = "Dinheiro",
        });
        var rowA = Row(a);
        var rowB = Row(b);
        Assert.Equal(0, rowA.Vmv7, Tol);
        Assert.Equal(2, rowB.Vmv7, Tol);
        Assert.Equal(Today, rowA.LastValidSaleDate);
        Assert.Equal(Today, rowB.LastValidSaleDate);
    }

    [Fact]
    public void T09_TrocaApenasDevolucao()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "TD", "So Devolve");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 5, 10, 50);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 5 }],
        });
        var row = Row(id);
        Assert.Equal(0, row.Vmv7, Tol);
        Assert.Equal(Today, row.LastValidSaleDate);
    }

    [Fact]
    public void T10_NovoItemDeTroca_ContaPositivo()
    {
        using var db = BeginWithCash();
        var a = TestDataHelper.SeedSimpleProduct(100, 10, 4, "N1", "Origem");
        var b = TestDataHelper.SeedSimpleProduct(50, 8, 3, "N2", "Novo Troca");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 1 }],
            NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 4, UnitPrice = 8 }],
            PaymentType = "Dinheiro",
        });
        Assert.Equal(4, Row(b).Vmv7, Tol);
    }

    [Fact]
    public void T11_NaoDuplicaSaleExchangeNewItemMaisMovement()
    {
        using var db = BeginWithCash();
        var a = TestDataHelper.SeedSimpleProduct(100, 10, 4, "X1", "Origem Dup");
        var b = TestDataHelper.SeedSimpleProduct(50, 8, 3, "X2", "Novo Dup");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 1, 10, 10);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 1 }],
            NewItems = [new SaleExchangeNewLine { ProductId = b, Qty = 3, UnitPrice = 8 }],
            PaymentType = "Dinheiro",
        });
        Assert.Equal(3, Row(b).Vmv7, Tol);
    }

    [Fact]
    public void T12_SwapDoMesmoDia_ContaSomenteEstadoFinal()
    {
        using var db = BeginWithCash();
        var a = TestDataHelper.SeedSimpleProduct(100, 10, 4, "SA", "Swap A");
        var b = TestDataHelper.SeedSimpleProduct(50, 12, 5, "SB", "Swap B");
        var sale = TestDataHelper.FinalizeSimpleCashSale(a, 2, 10, 20);
        PdvService.SwapSaleItem(
            sale.SaleId, SaleItemId(sale.SaleId), b, keepLinePrice: false,
            confirmedPayments: [new PdvPaymentPart { PaymentType = "Dinheiro", Amount = 24 }],
            cashReceived: 24);
        var rowA = Row(a);
        var rowB = Row(b);
        Assert.Equal(0, rowA.Vmv7, Tol);
        Assert.Equal(2, rowB.Vmv7, Tol);
        Assert.Equal(Today, rowA.LastValidSaleDate);
        Assert.Equal(Today, rowB.LastValidSaleDate);
    }

    [Fact]
    public void T13_T14_T15_KitContaComponentes_NaoOSkuComercial()
    {
        using var db = BeginWithCash();
        var comp = TestDataHelper.SeedSimpleProduct(100, 2, 1, "CMP", "Componente");
        var kit = SeedKit(comp, 3);
        PdvService.FinalizeSale(new PdvFinalizeRequest
        {
            Items =
            [
                new PdvCartLine
                {
                    ProductId = kit, Code = "KIT1", Name = "Kit Teste", Unit = "UN",
                    Quantity = 2, UnitPrice = 20, StockUnitsPerSale = 1,
                },
            ],
            PaymentType = "Dinheiro",
            CashReceived = 40,
        });
        var rowKit = Row(kit);
        var rowComp = Row(comp);
        Assert.Equal(0, rowKit.Vmv7, Tol);
        Assert.Equal(6, rowComp.Vmv7, Tol);
        Assert.Null(rowKit.LastValidSaleDate);
        Assert.Equal(Today, rowComp.LastValidSaleDate);
    }

    [Fact]
    public void T16_KitLegadoSemMovement_NaoInventaComponentes()
    {
        using var db = Begin();
        var comp = TestDataHelper.SeedSimpleProduct(100, 2, 1, "C16", "Comp Legado");
        var kit = SeedKit(comp, 4, "KITL");
        InsertLegacySale(kit, 2, Today, stockQty: 2);
        Assert.Equal(0, Row(kit).Vmv7, Tol);
        Assert.Equal(0, Row(comp).Vmv7, Tol);
    }

    [Fact]
    public void T17_T44_ProdutoNuncaVendido()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(10, 10, 4, "NV", "Nunca");
        var row = Row(id);
        Assert.Null(row.LastValidSaleDate);
        Assert.Null(row.DaysWithoutSale);
        Assert.Equal(InventoryTurnoverSituation.NeverSold, row.Situation);
    }

    [Fact]
    public void T18_T19_T20_T21_ProdutoNovo_HistoricoInsuficiente()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(10, 10, 4, "PN", "Produto Novo");
        SetProductCreated(id, Today.AddDays(-4));
        var row = Row(id);
        Assert.Equal(5, row.HistoryDays);
        Assert.True(row.IsHistoryInsufficient7);
        Assert.True(row.IsHistoryInsufficient30);
        Assert.True(row.IsHistoryInsufficient90);
        Assert.Equal(InventoryTurnoverSituation.NeverSold, row.Situation);
    }

    [Fact]
    public void T22_T23_T24_Vmv7_30_90_ComHistoricoCompleto()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(500, 10, 4, "VM", "VMV Janelas");
        StampInbound(id, Today.AddDays(-120));
        SetProductCreated(id, Today.AddDays(-200));
        var s1 = TestDataHelper.FinalizeSimpleCashSale(id, 7, 10, 70);
        var s2 = TestDataHelper.FinalizeSimpleCashSale(id, 8, 10, 80);
        var s3 = TestDataHelper.FinalizeSimpleCashSale(id, 9, 10, 90);
        SetSaleDate(s1.SaleId, Today.AddDays(-2));
        SetSaleDate(s2.SaleId, Today.AddDays(-20));
        SetSaleDate(s3.SaleId, Today.AddDays(-60));
        var row = Row(id);
        Assert.Equal(7 / 7.0, row.Vmv7, 0.001);
        Assert.Equal(15 / 30.0, row.Vmv30, 0.001);
        Assert.Equal(24 / 90.0, row.Vmv90, 0.001);
        Assert.False(row.IsHistoryInsufficient7);
        Assert.False(row.IsHistoryInsufficient30);
        Assert.False(row.IsHistoryInsufficient90);
    }

    [Fact]
    public void T25_LimiteExatoDe7Dias()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "L7", "Limite 7");
        StampInbound(id, Today.AddDays(-40));
        SetProductCreated(id, Today.AddDays(-80));
        var inside = TestDataHelper.FinalizeSimpleCashSale(id, 3, 10, 30);
        var edge = TestDataHelper.FinalizeSimpleCashSale(id, 5, 10, 50);
        SetSaleDate(inside.SaleId, Today.AddDays(-6));
        SetSaleDate(edge.SaleId, Today.AddDays(-7));
        Assert.Equal(3 / 7.0, Row(id).Vmv7, 0.001);
    }

    [Fact]
    public void T26_LimiteExatoDe30Dias()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "L30", "Limite 30");
        StampInbound(id, Today.AddDays(-80));
        SetProductCreated(id, Today.AddDays(-120));
        var inside = TestDataHelper.FinalizeSimpleCashSale(id, 3, 10, 30);
        var edge = TestDataHelper.FinalizeSimpleCashSale(id, 5, 10, 50);
        SetSaleDate(inside.SaleId, Today.AddDays(-29));
        SetSaleDate(edge.SaleId, Today.AddDays(-30));
        Assert.Equal(3 / 30.0, Row(id).Vmv30, 0.001);
    }

    [Fact]
    public void T27_LimiteExatoDe90Dias()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "L90", "Limite 90");
        StampInbound(id, Today.AddDays(-120));
        SetProductCreated(id, Today.AddDays(-200));
        var inside = TestDataHelper.FinalizeSimpleCashSale(id, 3, 10, 30);
        var edge = TestDataHelper.FinalizeSimpleCashSale(id, 5, 10, 50);
        SetSaleDate(inside.SaleId, Today.AddDays(-89));
        SetSaleDate(edge.SaleId, Today.AddDays(-90));
        Assert.Equal(3 / 90.0, Row(id).Vmv90, 0.001);
    }

    [Fact]
    public void T28_EventoForaDaJanela90()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "OUT", "Fora");
        StampInbound(id, Today.AddDays(-200));
        SetProductCreated(id, Today.AddDays(-250));
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 40, 10, 400);
        SetSaleDate(sale.SaleId, Today.AddDays(-90));
        var row = Row(id);
        Assert.Equal(0, row.Vmv7, Tol);
        Assert.Equal(0, row.Vmv30, Tol);
        Assert.Equal(0, row.Vmv90, Tol);
        Assert.Equal(Today.AddDays(-90), row.LastValidSaleDate);
    }

    [Fact]
    public void T29_T30_T31_EstoqueDepositoGeladeiraTotal()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(12, 10, 4, "ST", "Estoque");
        TestDataHelper.SetProductFridge(id, 5);
        var row = Row(id);
        Assert.Equal(12, row.Stock, Tol);
        Assert.Equal(5, row.StockFridge, Tol);
        Assert.Equal(17, row.TotalStock, Tol);
    }

    [Fact]
    public void T32_EstoqueZero()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(0, 10, 4, "Z", "Zero");
        var row = Row(id);
        Assert.Equal(0, row.TotalStock, Tol);
        Assert.Equal(InventoryCoverageState.ZeroStock, row.CoverageState);
        Assert.Null(row.CoverageDays);
        Assert.Equal(InventoryTurnoverSituation.ZeroStock, row.Situation);
    }

    [Fact]
    public void T33_EstoqueNegativo()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(5, 10, 4, "NEG", "Negativo");
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE products SET stock = -3 WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        var row = Row(id);
        Assert.Equal(InventoryCoverageState.NegativeStock, row.CoverageState);
        Assert.Null(row.CoverageDays);
        Assert.Equal(InventoryTurnoverSituation.NegativeStock, row.Situation);
    }

    [Fact]
    public void T34_VmvZero_SemGiro()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(20, 10, 4, "SG", "Sem Giro");
        SetProductCreated(id, Today.AddDays(-40));
        var row = Row(id);
        Assert.Equal(0, row.Vmv30, Tol);
        Assert.Equal(InventoryCoverageState.NoTurnover, row.CoverageState);
        Assert.Null(row.CoverageDays);
        Assert.Equal(InventoryTurnoverSituation.NeverSold, row.Situation);
    }

    [Fact]
    public void T35_CoberturaCalculavel()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(90, 10, 4, "COV", "Cobertura");
        StampInbound(id, Today.AddDays(-40));
        SetProductCreated(id, Today.AddDays(-80));
        TestDataHelper.FinalizeSimpleCashSale(id, 10, 10, 100);
        var row = Row(id);
        Assert.Equal(InventoryCoverageState.Calculable, row.CoverageState);
        Assert.NotNull(row.CoverageDays);
        Assert.Equal(80 / (10 / 30.0), row.CoverageDays!.Value, 0.05);
    }

    [Fact]
    public void T36_CoberturaComVmvZero_NaoRetornaInfinity()
    {
        var (state, days) = InventoryIntelligenceEngine.ClassifyCoverage(10, 0);
        Assert.Equal(InventoryCoverageState.NoTurnover, state);
        Assert.Null(days);
        Assert.Null(InventoryIntelligenceEngine.SafeRatio(10, 0));
    }

    [Fact]
    public void T37_AusenciaDeNaN()
    {
        Assert.Null(InventoryIntelligenceEngine.SafeRatio(double.NaN, 7));
        Assert.Null(InventoryIntelligenceEngine.SafeRatio(1, double.NaN));
        var row = InventoryIntelligenceEngine.BuildRow(
            1, "X", "X", 1, 0, Today,
            new InventoryIntelligenceEngine.LifeStartDecision(Today, "test", "test", true),
            [new InventoryIntelligenceEngine.DailyFlow(Today, double.NaN, 0, false)]);
        AssertFinite(row);
        Assert.Equal(0, row.Vmv7, Tol);
    }

    [Fact]
    public void T38_QuantidadeFracionaria()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "FR", "Fracao");
        TestDataHelper.FinalizeSimpleCashSale(id, 0.5, 10, 10);
        Assert.Equal(0.5, Row(id).Vmv7, Tol);
    }

    [Fact]
    public void T39_ProdutoKeepAposMerge()
    {
        using var db = BeginWithCash();
        var keep = TestDataHelper.SeedSimpleProduct(10, 10, 4, "KEEP", "Keep");
        var absorb = TestDataHelper.SeedSimpleProduct(10, 10, 4, "ABS", "Absorb");
        TestDataHelper.FinalizeSimpleCashSale(absorb, 6, 10, 60);
        ProductService.MergeProducts(keep, absorb);
        var rowKeep = Row(keep);
        Assert.Equal(6, rowKeep.Vmv7, Tol);
        Assert.Null(InventoryIntelligenceService.GetByProductId(absorb));
    }

    [Fact]
    public void T40_NaoReconstroiAbsorbViaAuditLog()
    {
        using var db = BeginWithCash();
        var keep = TestDataHelper.SeedSimpleProduct(10, 10, 4, "K40", "Keep40");
        var absorb = TestDataHelper.SeedSimpleProduct(10, 10, 4, "A40", "Abs40");
        TestDataHelper.FinalizeSimpleCashSale(absorb, 2, 10, 20);
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO audit_log (user_login, action, entity, entity_id, details)
                VALUES ('t', 'merge', 'product', $id, 'ABSORB tinha venda 999 que nao deve ser somada');
                """;
            cmd.Parameters.AddWithValue("$id", absorb);
            cmd.ExecuteNonQuery();
        }
        ProductService.MergeProducts(keep, absorb);
        Assert.Equal(2, Row(keep).Vmv7, Tol);

        var src = File.ReadAllText(FindSource("InventoryIntelligenceService.cs"));
        Assert.DoesNotContain("audit_log", src, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void T41_FallbackLegadoSemMovement()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(20, 10, 4, "FB", "Fallback");
        InsertLegacySale(id, 8, Today, stockQty: 8);
        Assert.Equal(8, Row(id).Vmv7, Tol);
    }

    [Fact]
    public void T42_FallbackNaoDuplicaSaleItemMaisMovement()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "ND", "Nao Duplica");
        TestDataHelper.FinalizeSimpleCashSale(id, 5, 10, 50);
        Assert.Equal(5, Row(id).Vmv7, Tol);
    }

    [Fact]
    public void T43_UltimaVendaValida()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "UV", "Ultima");
        SetProductCreated(id, Today.AddDays(-20));
        var old = TestDataHelper.FinalizeSimpleCashSale(id, 1, 10, 10);
        var recent = TestDataHelper.FinalizeSimpleCashSale(id, 1, 10, 10);
        SetSaleDate(old.SaleId, Today.AddDays(-8));
        SetSaleDate(recent.SaleId, Today.AddDays(-3));
        var row = Row(id);
        Assert.Equal(Today.AddDays(-3), row.LastValidSaleDate);
        Assert.Equal(3, row.DaysWithoutSale);
    }

    [Fact]
    public void T45_DiasSemVenda()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(50, 10, 4, "DS", "Dias Sem");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 1, 10, 10);
        SetSaleDate(sale.SaleId, Today.AddDays(-12));
        SetProductCreated(id, Today.AddDays(-20));
        Assert.Equal(12, Row(id).DaysWithoutSale);
    }

    [Fact]
    public void T46_ProdutoNovoNaoEntraFalsamenteEmFiltro306090()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(8, 10, 4, "NEW", "Novo Filtro");
        var row = Row(id);
        Assert.True(row.HistoryDays < 30);
        Assert.False(row.HasPhysicalAvailabilityEvidence);
        Assert.False(row.QualifiesForDaysWithoutSaleFilter(30));
        Assert.False(row.QualifiesForDaysWithoutSaleFilter(60));
        Assert.False(row.QualifiesForDaysWithoutSaleFilter(90));
    }

    [Fact]
    public void T47_ValoresAnomalosGrandes_NaoCausamOverflow()
    {
        var life = new InventoryIntelligenceEngine.LifeStartDecision(Today.AddDays(-90), "t", "t", true);
        var row = InventoryIntelligenceEngine.BuildRow(
            1, "BIG", "BIG", 1e20, 1e20, Today, life,
            [new InventoryIntelligenceEngine.DailyFlow(Today, 1e20, 0, true)]);
        AssertFinite(row);
        Assert.NotNull(row.CoverageDays);
        Assert.True(row.CoverageDays > 0);
        Assert.Null(InventoryIntelligenceEngine.SafeRatio(1, double.PositiveInfinity));
        Assert.Null(InventoryIntelligenceEngine.SafeRatio(double.PositiveInfinity, 1));
    }

    [Fact]
    public void T48_MotorNaoDependeDeCashSessions()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(30, 10, 4, "CS", "Sem Caixa");
        InsertLegacySale(id, 4, Today, stockQty: 4);
        using (var conn = DatabaseService.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM cash_sessions;";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(4, Row(id).Vmv7, Tol);
        var src = File.ReadAllText(FindSource("InventoryIntelligenceService.cs"));
        Assert.DoesNotContain("cash_sessions", src, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void T49_MovementsDeAjusteNaoEntram()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "AJ", "Ajuste");
        TestDataHelper.FinalizeSimpleCashSale(id, 2, 10, 20);
        InsertRawMovement(id, "saida", 40, "ajuste_manual");
        Assert.Equal(2, Row(id).Vmv7, Tol);
    }

    [Fact]
    public void T50_MovementsDeTransferenciaNaoEntram()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "TR", "Transfer");
        TestDataHelper.FinalizeSimpleCashSale(id, 2, 10, 20);
        InsertRawMovement(id, "saida", 15, "transferencia_geladeira");
        Assert.Equal(2, Row(id).Vmv7, Tol);
    }

    [Fact]
    public void QueryCount_ConstanteSemNPlusOne()
    {
        using var db = BeginWithCash();
        for (var i = 0; i < 8; i++)
        {
            var id = TestDataHelper.SeedSimpleProduct(20, 10, 4, $"Q{i}", $"Q{i}");
            TestDataHelper.FinalizeSimpleCashSale(id, 1, 10, 10);
        }
        var snap = InventoryIntelligenceService.Load(Today);
        Assert.Equal(InventoryIntelligenceService.ExpectedQueryCount, snap.QueryCount);
        Assert.True(snap.Rows.Count >= 8);
    }

    [Fact]
    public void LifeStart_CadastroAnteriorNaoDiluiQuandoHaEntradaFisica()
    {
        var today = Today;
        var created = today.AddDays(-90);
        var purchase = today.AddDays(-60);
        var sale = today.AddDays(-30);
        var life = InventoryIntelligenceEngine.ResolveLifeStart(today, created, purchase, sale);
        Assert.Equal(purchase, life.StartDate);
        Assert.True(life.HasPhysicalAvailabilityEvidence);
        Assert.Equal("trusted_inbound", life.Source);
        Assert.NotEqual(created, life.StartDate);
        Assert.Equal(61, InventoryIntelligenceEngine.HistoryDays(today, life.StartDate));
    }

    [Fact]
    public void Fallback_UsaStockQtyQuandoPresente()
    {
        Assert.Equal(20, InventoryIntelligenceService.PhysicalQty(1, 20), Tol);
        Assert.Equal(3, InventoryIntelligenceService.PhysicalQty(3, 0), Tol);
    }

    [Fact]
    public void R1A_CadastroJaneiro_EntradaAbril_InicioNaoEJaneiro()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(100, 10, 4, "R1A", "Disp Abril");
        var jan = Today.AddDays(-100);
        var abril = Today.AddDays(-20);
        SetProductCreated(id, jan);
        StampInbound(id, abril);
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 6, 10, 60);
        SetSaleDate(sale.SaleId, abril);
        var row = Row(id);
        Assert.True(row.HasPhysicalAvailabilityEvidence);
        Assert.Equal((Today - abril).Days + 1, row.HistoryDays);
        Assert.True(row.HistoryDays < (Today - jan).Days + 1);
        Assert.Equal(6 / (double)row.HistoryDays, row.Vmv30, 0.01);
    }

    [Fact]
    public void R1B_SemCompra_UsaPrimeiraVenda_SemDataFutura()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(50, 10, 4, "R1B", "So Venda");
        SetProductCreated(id, Today.AddDays(-80));
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 2, 10, 20);
        SetSaleDate(sale.SaleId, Today.AddDays(-10));
        var row = Row(id);
        Assert.True(row.HasPhysicalAvailabilityEvidence);
        Assert.Equal(11, row.HistoryDays);
        Assert.Equal(Today.AddDays(-10), row.LastValidSaleDate);
        Assert.True(row.HistoryDays <= 11);
    }

    [Fact]
    public void R1C_SomenteCadastro_NuncaEntrouNuncaVendeu()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(7, 10, 4, "R1C", "So Cadastro");
        SetProductCreated(id, Today.AddDays(-90));
        var row = Row(id);
        Assert.False(row.HasPhysicalAvailabilityEvidence);
        Assert.Null(row.LastValidSaleDate);
        Assert.Equal(InventoryTurnoverSituation.NeverSold, row.Situation);
        Assert.False(row.QualifiesForDaysWithoutSaleFilter(30));
        Assert.Equal(91, row.HistoryDays);
    }

    [Fact]
    public void R1_SaidaDeAjuste_NaoProvaDisponibilidade()
    {
        Assert.True(InventoryIntelligenceService.IsTrustedInboundOperation("entrada_compra"));
        Assert.False(InventoryIntelligenceService.IsTrustedInboundOperation("saida_manual"));
        Assert.False(InventoryIntelligenceService.IsTrustedInboundOperation("devolucao_troca"));
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(50, 10, 4, "R1S", "Ajuste Saida");
        SetProductCreated(id, Today.AddDays(-90));
        InsertRawMovement(id, "saida", 4, "saida_manual");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 1, 10, 10);
        SetSaleDate(sale.SaleId, Today.AddDays(-5));
        var row = Row(id);
        Assert.Equal(6, row.HistoryDays);
        Assert.True(row.HasPhysicalAvailabilityEvidence);
    }

    [Fact]
    public void R2_TrocaModernaELegada_NaoPerdeNemDuplica()
    {
        using var db = BeginWithCash();
        var moderno = TestDataHelper.SeedSimpleProduct(80, 10, 4, "MOD", "Troca Moderna");
        var legado = TestDataHelper.SeedSimpleProduct(80, 10, 4, "LEGX", "Troca Legada");
        var saleMod = TestDataHelper.FinalizeSimpleCashSale(moderno, 5, 10, 50);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = saleMod.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(saleMod.SaleId), Qty = 2 }],
        });
        var saleLeg = InsertLegacySale(legado, 4, Today, stockQty: 4);
        InsertLegacyExchange(saleLeg, legado, returnQty: 1, createdAtUtc: DateBrHelper.NowUtcIso());

        Assert.Equal(3, Row(moderno).Vmv7, Tol);
        Assert.Equal(3, Row(legado).Vmv7, Tol);
    }

    [Fact]
    public void R3_VendaEDevolucaoMesmoDia_LastValidSalePermanece()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(50, 10, 4, "R3", "Mesmo Dia");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 1, 10, 10);
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 1 }],
        });
        var row = Row(id);
        Assert.Equal(0, row.Vmv7, Tol);
        Assert.Equal(Today, row.LastValidSaleDate);
        Assert.Equal(0, row.DaysWithoutSale);
    }

    [Fact]
    public void R3_VendaCancelada_NaoELastValidSale()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(50, 10, 4, "R3C", "Cancel Last");
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 2, 10, 20);
        PdvService.CancelSale(sale.SaleId);
        var row = Row(id);
        Assert.Null(row.LastValidSaleDate);
        Assert.Equal(0, row.Vmv30, Tol);
    }

    [Fact]
    public void R4_VendasMaioresQueDevolucoes()
    {
        var net = InventoryIntelligenceEngine.NetPhysicalDemand(5, 2);
        Assert.Equal(3, net, Tol);
        Assert.Equal(3 / 7.0, InventoryIntelligenceEngine.Vmv(net, 30, 7), 0.001);
    }

    [Fact]
    public void R4_VendasIguaisDevolucoes_VmvZero()
    {
        Assert.Equal(0, InventoryIntelligenceEngine.NetPhysicalDemand(4, 4), Tol);
        Assert.Equal(0, InventoryIntelligenceEngine.Vmv(0, 30, 7), Tol);
    }

    [Fact]
    public void R4_DevolucoesMaioresQueVendas_VmvNaoNegativo()
    {
        Assert.Equal(0, InventoryIntelligenceEngine.NetPhysicalDemand(1, 3), Tol);
        var vmv = InventoryIntelligenceEngine.Vmv(-2, 30, 7);
        Assert.True(vmv >= 0);
        Assert.Equal(0, vmv, Tol);
        var (state, days) = InventoryIntelligenceEngine.ClassifyCoverage(10, vmv);
        Assert.Equal(InventoryCoverageState.NoTurnover, state);
        Assert.Null(days);
    }

    [Fact]
    public void R4_SomenteDevolucoesNaJanela_VmvZero()
    {
        using var db = BeginWithCash();
        var id = TestDataHelper.SeedSimpleProduct(80, 10, 4, "R4D", "So Dev Janela");
        StampInbound(id, Today.AddDays(-40));
        var sale = TestDataHelper.FinalizeSimpleCashSale(id, 3, 10, 30);
        SetSaleDate(sale.SaleId, Today.AddDays(-20));
        SaleExchangeService.Confirm(new SaleExchangeRequest
        {
            OriginalSaleId = sale.SaleId,
            Returns = [new SaleExchangeReturnLine { SaleItemId = SaleItemId(sale.SaleId), Qty = 3 }],
        });
        var row = Row(id);
        Assert.Equal(0, row.Vmv7, Tol);
        Assert.True(row.Vmv7 >= 0);
        Assert.True(row.Vmv30 >= 0);
        Assert.Equal(Today.AddDays(-20), row.LastValidSaleDate);
        if (row.CoverageDays is double c)
            Assert.True(c > 0 && InventoryIntelligenceEngine.IsFinite(c));
    }

    [Fact]
    public void R4_CoberturaNuncaRecebeVmvNegativo()
    {
        var (state, days) = InventoryIntelligenceEngine.ClassifyCoverage(12, -3);
        Assert.Equal(InventoryCoverageState.NoTurnover, state);
        Assert.Null(days);
        Assert.Null(InventoryIntelligenceEngine.SafeRatio(12, -3));
    }

    [Fact]
    public void CodigoNaoAlteraAbcNemCriaIndice()
    {
        var svc = File.ReadAllText(FindSource("InventoryIntelligenceService.cs"));
        Assert.DoesNotContain("ListCurvaAbc", svc);
        Assert.DoesNotContain("idx_sale_items_product", svc);
        Assert.DoesNotContain("CREATE INDEX", svc, StringComparison.OrdinalIgnoreCase);
    }

    private static void InsertLegacyExchange(int originalSaleId, int productId, double returnQty, string createdAtUtc)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        int exchangeId;
        using (var ex = conn.CreateCommand())
        {
            ex.Transaction = tx;
            ex.CommandText = """
                INSERT INTO sale_exchanges (
                  original_sale_id, created_at, return_total, new_total, difference
                ) VALUES ($sale, $created, $ret, 0, $diff);
                SELECT last_insert_rowid();
                """;
            ex.Parameters.AddWithValue("$sale", originalSaleId);
            ex.Parameters.AddWithValue("$created", createdAtUtc);
            ex.Parameters.AddWithValue("$ret", returnQty * 10);
            ex.Parameters.AddWithValue("$diff", -returnQty * 10);
            exchangeId = Convert.ToInt32(ex.ExecuteScalar());
        }
        using (var item = conn.CreateCommand())
        {
            item.Transaction = tx;
            item.CommandText = """
                INSERT INTO sale_exchange_return_items (
                  exchange_id, sale_item_id, product_id, product_code, product_name,
                  qty, unit_price, amount
                ) VALUES ($ex, 0, $pid, 'LEGX', 'Troca Legada', $qty, 10, $amt);
                """;
            item.Parameters.AddWithValue("$ex", exchangeId);
            item.Parameters.AddWithValue("$pid", productId);
            item.Parameters.AddWithValue("$qty", returnQty);
            item.Parameters.AddWithValue("$amt", returnQty * 10);
            item.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static string FindSource(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SGDB.App", "Services", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(fileName);
    }
}
