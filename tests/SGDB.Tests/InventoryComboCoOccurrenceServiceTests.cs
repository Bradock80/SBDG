using System.Globalization;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71A-B2 — coocorrência por transação em banco TEMP. Sem deposito.db. Sem UI. Sem B3.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryComboCoOccurrenceServiceTests
{
    static readonly DateTime Today = new(2026, 9, 3);
    static readonly DateTime WindowStart = Today.AddDays(-(InventoryIntelligenceEngine.Window90 - 1));

    static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void Input_vazio_nao_abre_consulta()
    {
        using var db = Begin();
        var emptyTargets = InventoryComboCoOccurrenceService.Load([], [1], History(1), Today);
        var emptyAnchors = InventoryComboCoOccurrenceService.Load([1], [], History(1), Today);
        var nulos = InventoryComboCoOccurrenceService.Load(null, null, null, Today);
        Assert.Equal(0, emptyTargets.QueryCount);
        Assert.Equal(0, emptyAnchors.QueryCount);
        Assert.Equal(0, nulos.QueryCount);
        Assert.Empty(emptyTargets.Rows);
        Assert.Empty(emptyAnchors.Rows);
        Assert.Empty(nulos.Rows);
    }

    [Fact]
    public void Target_igual_anchor_nao_forma_par()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        InsertSale(Today, (a, 1));
        var same = InventoryComboCoOccurrenceService.Load([a], [a], History(a), Today);
        Assert.Equal(0, same.QueryCount);
        Assert.Empty(same.Rows);

        var b = InsertProduct("B");
        InsertSale(Today, (a, 1), (b, 1));
        var mixed = InventoryComboCoOccurrenceService.Load([a], [a, b], History(a), Today);
        Assert.Equal(1, mixed.QueryCount);
        var row = Assert.Single(mixed.Rows);
        Assert.Equal(a, row.TargetProductId);
        Assert.Equal(b, row.AnchorProductId);
        Assert.DoesNotContain(mixed.Rows, r => r.TargetProductId == r.AnchorProductId);
    }

    [Fact]
    public void Duplicatas_de_input_sao_normalizadas()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var b = InsertProduct("B");
        for (var i = 0; i < 5; i++)
            InsertSale(Today.AddDays(-i), (a, 1), (b, 1));

        var snap = InventoryComboCoOccurrenceService.Load(
            [a, a, a], [b, b], History(a), Today);
        Assert.Equal(1, snap.QueryCount);
        Assert.Equal([a], snap.RequestedTargetIds);
        Assert.Equal([b], snap.RequestedAnchorIds);
        Assert.Single(snap.Rows);
        Assert.Equal(5, snap.Rows[0].PairTransactions);
        Assert.Equal(5, snap.Rows[0].TargetTransactions);
    }

    [Fact]
    public void Cenario_controlado_A_B_C_D()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var b = InsertProduct("B");
        var c = InsertProduct("C");
        var d = InsertProduct("D");
        SeedControlledWindow(a, b, c);

        var snap = InventoryComboCoOccurrenceService.Load(
            [a], [b, c, d], History(a), Today);

        Assert.Equal(1, snap.QueryCount);
        Assert.Equal(3, snap.Rows.Count);

        var ab = Row(snap, a, b);
        Assert.Equal(4, ab.PairTransactions);
        Assert.Equal(10, ab.TargetTransactions);
        Assert.Equal(0.4, ab.ConfidenceTargetToAnchor);
        Assert.Equal(InventoryComboPairEvidence.Observed, ab.Evidence);

        var ac = Row(snap, a, c);
        Assert.Equal(2, ac.PairTransactions);
        Assert.Equal(10, ac.TargetTransactions);
        Assert.Equal(0.2, ac.ConfidenceTargetToAnchor);
        Assert.Equal(InventoryComboPairEvidence.Weak, ac.Evidence);

        var ad = Row(snap, a, d);
        Assert.Equal(0, ad.PairTransactions);
        Assert.Equal(10, ad.TargetTransactions);
        Assert.Equal(0, ad.ConfidenceTargetToAnchor);
        Assert.Equal(InventoryComboPairEvidence.NoneObserved, ad.Evidence);
    }

    [Fact]
    public void Venda_cancelada_nao_conta()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var b = InsertProduct("B");
        var c = InsertProduct("C");
        var d = InsertProduct("D");
        SeedControlledWindow(a, b, c);
        InsertSale(Today, cancelled: 1, (a, 1), (b, 1));

        var snap = InventoryComboCoOccurrenceService.Load(
            [a], [b, c, d], History(a), Today);
        Assert.Equal(4, Row(snap, a, b).PairTransactions);
        Assert.Equal(10, Row(snap, a, b).TargetTransactions);
        Assert.Equal(2, Row(snap, a, c).PairTransactions);
        Assert.Equal(0, Row(snap, a, d).PairTransactions);
    }

    [Fact]
    public void Fora_da_janela_nao_conta_e_fronteira_inclusiva()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var b = InsertProduct("B");
        var c = InsertProduct("C");
        SeedControlledWindow(a, b, c);
        InsertSale(WindowStart.AddDays(-1), (a, 1), (b, 1));
        InsertSale(Today.AddDays(1), (a, 1), (b, 1));

        var snap = InventoryComboCoOccurrenceService.Load(
            [a], [b], History(a), Today);
        Assert.Equal(4, Row(snap, a, b).PairTransactions);
        Assert.Equal(10, Row(snap, a, b).TargetTransactions);

        var edgeT = InsertProduct("EdgeT");
        var edgeA = InsertProduct("EdgeA");
        InsertSale(WindowStart, (edgeT, 1), (edgeA, 1));
        for (var i = 0; i < 4; i++)
            InsertSale(Today.AddDays(-i), (edgeT, 1));

        var edge = InventoryComboCoOccurrenceService.Load(
            [edgeT], [edgeA], History(edgeT), Today);
        Assert.Equal(1, Row(edge, edgeT, edgeA).PairTransactions);
        Assert.Equal(5, Row(edge, edgeT, edgeA).TargetTransactions);
        Assert.Equal(InventoryComboPairEvidence.Weak, Row(edge, edgeT, edgeA).Evidence);
    }

    [Fact]
    public void Quantidade_maior_que_1_conta_uma_transacao()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var b = InsertProduct("B");
        InsertSale(Today, (a, 3), (b, 2));
        for (var i = 1; i < 5; i++)
            InsertSale(Today.AddDays(-i), (a, 1));

        var snap = InventoryComboCoOccurrenceService.Load(
            [a], [b], History(a), Today);
        var row = Row(snap, a, b);
        Assert.Equal(1, row.PairTransactions);
        Assert.Equal(5, row.TargetTransactions);
        Assert.Equal(InventoryComboPairEvidence.Weak, row.Evidence);
    }

    [Fact]
    public void Sku_duplicado_na_mesma_venda_e_presenca_unica()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var b = InsertProduct("B");
        var saleId = InsertSale(Today, (a, 1), (b, 1));
        InsertExtraLine(saleId, a, 2);
        for (var i = 1; i < 5; i++)
            InsertSale(Today.AddDays(-i), (a, 1));

        var snap = InventoryComboCoOccurrenceService.Load(
            [a], [b], History(a), Today);
        var row = Row(snap, a, b);
        Assert.Equal(1, row.PairTransactions);
        Assert.Equal(5, row.TargetTransactions);
    }

    [Fact]
    public void Venda_do_kit_pai_nao_explode_componentes()
    {
        using var db = Begin();
        var comp1 = InsertProduct("Comp1");
        var comp2 = InsertProduct("Comp2");
        var kit = InsertProduct(
            "Kit",
            extraJson: $"{{\"composicao\":true,\"composicao_itens\":[{{\"product_id\":{comp1},\"quantity\":1}},{{\"product_id\":{comp2},\"quantity\":1}}]}}");

        for (var i = 0; i < 10; i++)
            InsertSale(Today.AddDays(-i), (kit, 1));
        for (var i = 0; i < 10; i++)
            InsertSale(Today.AddDays(-i), (comp1, 1));

        var snap = InventoryComboCoOccurrenceService.Load(
            [comp1], [comp2, kit], History(comp1), Today);
        Assert.Equal(1, snap.QueryCount);

        var vsComp2 = Row(snap, comp1, comp2);
        Assert.Equal(0, vsComp2.PairTransactions);
        Assert.Equal(10, vsComp2.TargetTransactions);
        Assert.Equal(InventoryComboPairEvidence.NoneObserved, vsComp2.Evidence);

        var vsKit = Row(snap, comp1, kit);
        Assert.Equal(0, vsKit.PairTransactions);
        Assert.Equal(10, vsKit.TargetTransactions);
        Assert.Equal(InventoryComboPairEvidence.NoneObserved, vsKit.Evidence);
    }

    [Fact]
    public void Denominador_e_transacoes_do_target_nao_do_anchor()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var b = InsertProduct("B");
        for (var i = 0; i < 4; i++)
            InsertSale(Today.AddDays(-i), (a, 1), (b, 1));
        for (var i = 4; i < 10; i++)
            InsertSale(Today.AddDays(-i), (a, 1));
        for (var i = 0; i < 46; i++)
            InsertSale(Today.AddDays(-(i % 80)), (b, 1));

        var snap = InventoryComboCoOccurrenceService.Load(
            [a], [b], History(a), Today);
        var row = Row(snap, a, b);
        Assert.Equal(4, row.PairTransactions);
        Assert.Equal(10, row.TargetTransactions);
        Assert.Equal(0.4, row.ConfidenceTargetToAnchor);
        Assert.NotEqual(4d / 50d, row.ConfidenceTargetToAnchor);
    }

    [Fact]
    public void Varios_targets_uma_consulta_sem_misturar_denominadores()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var x = InsertProduct("X");
        var b = InsertProduct("B");
        var c = InsertProduct("C");
        var d = InsertProduct("D");
        SeedControlledWindow(a, b, c);
        for (var i = 0; i < 3; i++)
            InsertSale(Today.AddDays(-i), (x, 1), (b, 1));
        for (var i = 3; i < 8; i++)
            InsertSale(Today.AddDays(-i), (x, 1));

        var snap = InventoryComboCoOccurrenceService.Load(
            [a, x], [b, c, d], History(a, x), Today);

        Assert.Equal(1, snap.QueryCount);
        Assert.Equal(6, snap.Rows.Count);

        var ab = Row(snap, a, b);
        Assert.Equal(10, ab.TargetTransactions);
        Assert.Equal(4, ab.PairTransactions);
        Assert.Equal(0.4, ab.ConfidenceTargetToAnchor);

        var xb = Row(snap, x, b);
        Assert.Equal(8, xb.TargetTransactions);
        Assert.Equal(3, xb.PairTransactions);
        Assert.Equal(0.375, xb.ConfidenceTargetToAnchor);
        Assert.Equal(InventoryComboPairEvidence.Observed, xb.Evidence);

        Assert.Equal(0, Row(snap, x, d).PairTransactions);
        Assert.Equal(8, Row(snap, x, d).TargetTransactions);
    }

    [Fact]
    public void Query_budget_lote_e_1_vazio_e_0()
    {
        using var db = Begin();
        var a = InsertProduct("A");
        var x = InsertProduct("X");
        var b = InsertProduct("B");
        var c = InsertProduct("C");
        InsertSale(Today, (a, 1), (b, 1));
        InsertSale(Today, (x, 1), (c, 1));

        var lote = InventoryComboCoOccurrenceService.Load(
            [a, x], [b, c], History(a, x), Today);
        Assert.Equal(1, lote.QueryCount);
        Assert.Equal(InventoryComboCoOccurrenceService.ExpectedQueryCount, lote.QueryCount);

        var vazio = InventoryComboCoOccurrenceService.Load([], [b], History(a), Today);
        Assert.Equal(0, vazio.QueryCount);
    }

    static void SeedControlledWindow(int a, int b, int c)
    {
        for (var i = 0; i < 4; i++)
            InsertSale(Today.AddDays(-i), (a, 1), (b, 1));
        for (var i = 4; i < 6; i++)
            InsertSale(Today.AddDays(-i), (a, 1), (c, 1));
        for (var i = 6; i < 10; i++)
            InsertSale(Today.AddDays(-i), (a, 1));
    }

    static Dictionary<int, int> History(params int[] ids)
    {
        var map = new Dictionary<int, int>();
        foreach (var id in ids)
            map[id] = InventoryIntelligenceEngine.Window90;
        return map;
    }

    static InventoryComboPairCoOccurrenceFacts Row(
        InventoryComboCoOccurrenceSnapshot snap, int target, int anchor)
    {
        var match = snap.Rows.SingleOrDefault(r =>
            r.TargetProductId == target && r.AnchorProductId == anchor);
        Assert.NotNull(match);
        return match!;
    }

    static int InsertProduct(string name, string? extraJson = null)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (code, name, unit, sale_price, stock, cost_price, extra_json, active)
            VALUES ($code, $name, 'UN', 10, 10, 6, $extra, 1);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$code", name);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$extra", extraJson ?? "{}");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static int InsertSale(DateTime sessionDate, params (int ProductId, double Qty)[] items) =>
        InsertSale(sessionDate, cancelled: 0, items);

    static int InsertSale(DateTime sessionDate, int cancelled, params (int ProductId, double Qty)[] items)
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
            sale.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sale.Parameters.AddWithValue("$total", items.Sum(i => i.Qty * 10));
            sale.Parameters.AddWithValue("$c", cancelled);
            sale.Parameters.AddWithValue("$created", DateBrHelper.NowUtcIso());
            saleId = Convert.ToInt32(sale.ExecuteScalar());
        }

        foreach (var item in items)
        {
            using var line = conn.CreateCommand();
            line.Transaction = tx;
            line.CommandText = """
                INSERT INTO sale_items (
                  sale_id, product_id, product_code, product_name, unit,
                  quantity, unit_price, subtotal, stock_qty
                ) VALUES ($sale, $pid, 'SKU', 'Item', 'UN', $qty, 10, $sub, 0);
                """;
            line.Parameters.AddWithValue("$sale", saleId);
            line.Parameters.AddWithValue("$pid", item.ProductId);
            line.Parameters.AddWithValue("$qty", item.Qty);
            line.Parameters.AddWithValue("$sub", item.Qty * 10);
            line.ExecuteNonQuery();
        }

        tx.Commit();
        return saleId;
    }

    static void InsertExtraLine(int saleId, int productId, double qty)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sale_items (
              sale_id, product_id, product_code, product_name, unit,
              quantity, unit_price, subtotal, stock_qty
            ) VALUES ($sale, $pid, 'SKU', 'Item', 'UN', $qty, 10, $sub, 0);
            """;
        cmd.Parameters.AddWithValue("$sale", saleId);
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$qty", qty);
        cmd.Parameters.AddWithValue("$sub", qty * 10);
        cmd.ExecuteNonQuery();
    }
}
