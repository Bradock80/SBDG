using System.Globalization;
using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;
using SGDB.Utils;

namespace SGDB.Tests;

/// <summary>
/// 71A-B5 — pipeline real em banco TEMP. Sem EXE, sem deposito.db, sem writes de produção.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryComboIntelligencePipelineTests
{
    static readonly DateTime Today = new(2026, 9, 3);

    static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        InventoryCommercialMarginSettingsService.Save(20m);
        return db;
    }

    [Fact]
    public void Sem_target_elegivel_query_9_B2_zero()
    {
        using var db = Begin();
        TestDataHelper.SeedSimpleProduct(20, 10, 6, "NT1", "Sem tese");
        var calls = 0;
        var pipeline = Run((targets, anchors, history, today) =>
        {
            calls++;
            return InventoryComboCoOccurrenceService.Load(targets, anchors, history, today);
        });

        Assert.Equal(7, pipeline.Projection.QueryCount);
        Assert.Equal(1, pipeline.Facts.QueryCount);
        Assert.Equal(1, pipeline.Setting.QueryCount);
        Assert.Equal(
            9,
            pipeline.Projection.QueryCount + pipeline.Facts.QueryCount + pipeline.Setting.QueryCount);
        Assert.Equal(9, pipeline.Combo.QueryCount);
        Assert.Equal(0, pipeline.Combo.CoOccurrenceQueryCount);
        Assert.Equal(0, calls);
        Assert.Equal(0, pipeline.Combo.EligibleTargets);
        Assert.Empty(pipeline.Combo.Targets);
    }

    [Fact]
    public void Com_target_e_anchor_query_10_B2_uma_vez()
    {
        using var db = Begin();
        var idle = SeedIdle("TIDLE", "Alvo parado");
        var healthy = SeedHealthy("AHIT", "Ancora saudavel");
        var calls = 0;
        IReadOnlyList<int>? targets = null;
        IReadOnlyList<int>? anchors = null;
        var pipeline = Run((targetIds, anchorIds, history, today) =>
        {
            calls++;
            targets = targetIds.ToList();
            anchors = anchorIds.ToList();
            return InventoryComboCoOccurrenceService.Load(targetIds, anchorIds, history, today);
        });

        Assert.Equal(7, pipeline.Projection.QueryCount);
        Assert.Equal(1, pipeline.Facts.QueryCount);
        Assert.Equal(1, pipeline.Setting.QueryCount);
        Assert.Equal(10, pipeline.Combo.QueryCount);
        Assert.Equal(1, pipeline.Combo.CoOccurrenceQueryCount);
        Assert.Equal(1, calls);
        Assert.Equal(1, pipeline.Combo.CoOccurrenceCalls);
        Assert.True(pipeline.Combo.EligibleTargets >= 1);
        Assert.Contains(idle, pipeline.Combo.ByProductId.Keys);
        Assert.Contains(idle, targets!);
        Assert.Contains(healthy, anchors!);
        Assert.DoesNotContain(idle, anchors!);
        var paramsB2 = InventoryComboIntelligenceComposer.EstimateCoOccurrenceParameterCount(
            targets!.Count, anchors!.Count);
        Assert.Equal(2 + targets.Count + anchors.Count, paramsB2);
        Assert.True(paramsB2 < 999);
        Assert.True(paramsB2 < InventoryComboIntelligenceComposer.SqliteMaxVariableNumber);
    }

    [Fact]
    public void Dois_targets_B2_uma_consulta()
    {
        using var db = Begin();
        var t1 = SeedIdle("T1", "Parado 1");
        var t2 = SeedIdle("T2", "Parado 2");
        var healthy = SeedHealthy("A1", "Ancora");
        var calls = 0;
        var pipeline = Run((targets, anchors, history, today) =>
        {
            calls++;
            return InventoryComboCoOccurrenceService.Load(targets, anchors, history, today);
        });

        Assert.Equal(1, calls);
        Assert.Equal(10, pipeline.Combo.QueryCount);
        Assert.True(pipeline.Combo.EligibleTargets >= 2);
        Assert.Contains(t1, pipeline.Combo.ByProductId.Keys);
        Assert.Contains(t2, pipeline.Combo.ByProductId.Keys);
        Assert.DoesNotContain(healthy, pipeline.Combo.ByProductId.Keys);
    }

    [Fact]
    public void Ranking_integrado_Observed_antes_de_Weak()
    {
        using var db = Begin();
        var t1 = SeedExcessTarget("TX", "Alvo excesso");
        var a1 = SeedHealthy("A1", "Ancora Observed", stock: 44);
        var a2 = SeedHealthy("A2", "Ancora Weak", stock: 36);
        SeedControlledHistory(t1, a1, a2);
        var pipeline = Run();
        Assert.True(pipeline.Combo.ByProductId.ContainsKey(t1));
        var suggestions = pipeline.Combo.ByProductId[t1].Suggestions;
        Assert.NotEmpty(suggestions);
        Assert.Equal(a1, suggestions[0].AnchorProductId);
        Assert.Equal(InventoryComboPairEvidence.Observed, suggestions[0].PairEvidence);
        if (suggestions.Count > 1)
        {
            Assert.Equal(a2, suggestions[1].AnchorProductId);
            Assert.Equal(InventoryComboPairEvidence.Weak, suggestions[1].PairEvidence);
        }
    }

    static PipelineResult Run(InventoryComboCoOccurrenceLoader? loader = null)
    {
        var projection = InventoryProjectionService.Load(Today);
        var attention = InventoryAttentionComposer.Build(projection);
        var facts = InventoryCommercialFactsService.Load(
            InventoryCommercialEligibilityComposer.ProductIds(projection));
        var setting = InventoryCommercialMarginSettingsService.Load();
        var policy = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        var guidance = InventoryPurchaseGuidanceComposer.Compose(projection);
        var combo = InventoryComboIntelligenceComposer.Compose(
            new InventoryComboIntelligenceComposeInput
            {
                Today = projection.Today,
                Intelligence = projection.Intelligence,
                Attention = attention,
                Facts = facts,
                Guidance = guidance,
                PolicyResolution = policy,
            },
            loader);
        return new PipelineResult(projection, facts, setting, combo);
    }

    sealed record PipelineResult(
        InventoryProjectionSnapshot Projection,
        InventoryCommercialFactsSnapshot Facts,
        InventoryCommercialMarginSetting Setting,
        InventoryComboIntelligenceSnapshot Combo);

    static int SeedIdle(string code, string name)
    {
        var id = TestDataHelper.SeedSimpleProduct(80, 10, 6, code, name);
        StampInbound(id, Today.AddDays(-100));
        return id;
    }

    static int SeedExcessTarget(string code, string name)
    {
        var id = TestDataHelper.SeedSimpleProduct(120, 10, 6, code, name);
        StampInbound(id, Today.AddDays(-90));
        return id;
    }

    static int SeedHealthy(string code, string name, double stock = 40)
    {
        var id = TestDataHelper.SeedSimpleProduct(stock, 10, 6, code, name);
        StampInbound(id, Today.AddDays(-90));
        InsertLot(id, stock, Today.AddDays(180));
        for (var i = 0; i < 30; i++)
            InsertSale(Today.AddDays(-i), (id, 2));
        return id;
    }

    static void SeedControlledHistory(int target, int a1, int a2)
    {
        for (var i = 0; i < 4; i++)
            InsertSale(Today.AddDays(-i), (target, 2), (a1, 2));
        for (var i = 4; i < 6; i++)
            InsertSale(Today.AddDays(-i), (target, 2), (a2, 2));
        for (var i = 6; i < 30; i++)
            InsertSale(Today.AddDays(-i), (target, 2));
    }

    static int InsertLot(int productId, double quantity, DateTime expiry)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO product_lots (product_id, lot_number, expiry_date, quantity, unit_cost)
            VALUES ($p, $l, $e, $q, $c);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$p", productId);
        cmd.Parameters.AddWithValue("$l", "L" + productId);
        cmd.Parameters.AddWithValue("$e", expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$q", quantity);
        cmd.Parameters.AddWithValue("$c", 6);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void StampInbound(int productId, DateTime date)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO movements (
              product_id, movement_type, quantity, unit_price, notes, created_at, operation
            ) VALUES (
              $pid, 'entrada', 1, 0, '71a inbound', $at, 'entrada_compra'
            );
            """;
        cmd.Parameters.AddWithValue("$pid", productId);
        cmd.Parameters.AddWithValue("$at", date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    static int InsertSale(DateTime sessionDate, params (int ProductId, double Qty)[] items)
    {
        using var conn = DatabaseService.OpenConnection();
        using var tx = conn.BeginTransaction();
        int saleId;
        using (var sale = conn.CreateCommand())
        {
            sale.Transaction = tx;
            sale.CommandText = """
                INSERT INTO sales (session_date, total, payment_type, cancelled, created_at)
                VALUES ($d, $total, 'Dinheiro', 0, $created);
                SELECT last_insert_rowid();
                """;
            sale.Parameters.AddWithValue("$d", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sale.Parameters.AddWithValue("$total", items.Sum(i => i.Qty * 10));
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
}
