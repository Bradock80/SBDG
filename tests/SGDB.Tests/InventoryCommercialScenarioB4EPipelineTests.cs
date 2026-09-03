using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70F-B4E — pipeline real em banco temporário. Sem EXE, sem deposito.db da loja, sem writes.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryCommercialScenarioB4EPipelineTests
{
    static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void Load_pipeline_gera_snapshot_B4_com_9_queries()
    {
        using var db = Begin();
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B4EA", "Cenario A");
        var b = TestDataHelper.SeedSimpleProduct(15, 12, 7, "B4EB", "Cenario B");
        var saleBeforeA = ReadSalePrice(a);
        var saleBeforeB = ReadSalePrice(b);
        var extraBeforeA = ReadExtraJson(a);

        var snapshot = InventoryProjectionService.Load();
        var presented = InventoryProjectionPresentation.Apply(snapshot);
        var attention = InventoryAttentionComposer.Build(snapshot);
        var attentionPresented = InventoryAttentionPresentation.Apply(attention, presented);
        var eligibility = InventoryCommercialEligibilityComposer.Build(snapshot, attention);
        var ids = InventoryCommercialEligibilityComposer.ProductIds(snapshot);
        var facts = InventoryCommercialFactsService.Load(ids);
        var setting = InventoryCommercialMarginSettingsService.Load();
        var policy = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        var commercial = InventoryCommercialScenarioComposer.Compose(
            snapshot.Intelligence, snapshot, attention, eligibility, facts, policy);
        var commercialPresented = InventoryCommercialScenarioPresentation.Apply(commercial);

        Assert.Equal(7, snapshot.QueryCount);
        Assert.Equal(1, setting.QueryCount);
        Assert.Equal(9, commercial.QueryCount);
        Assert.Equal(9, commercialPresented.QueryCount);
        Assert.True(ids.Count >= 2, "70C deve listar os produtos semeados.");
        Assert.Equal(1, facts.QueryCount);
        Assert.Equal(snapshot.Intelligence.Rows.Count, eligibility.Count);
        Assert.Equal(snapshot.Intelligence.Rows.Count, commercial.Rows.Count);
        Assert.Equal(snapshot.Intelligence.Rows.Count, commercialPresented.Rows.Count);
        Assert.Contains(a, ids);
        Assert.Contains(b, ids);
        Assert.True(commercial.ByProductId.ContainsKey(a));
        Assert.True(commercialPresented.ByProductId.ContainsKey(a));
        Assert.True(commercialPresented.ByProductId.ContainsKey(b));

        var detail = InventoryProjectionDetail.TryCreate(
            snapshot, presented, a, attentionPresented, commercialPresented);
        Assert.NotNull(detail);
        Assert.Equal(a, detail!.Commercial.ProductId);
        Assert.False(detail.Commercial.IsJoinMissing);
        Assert.Same(commercialPresented.ByProductId[a], detail.Commercial);

        var missing = InventoryProjectionDetail.TryCreate(
            snapshot, presented, a, attentionPresented, new InventoryCommercialScenarioPresentationSnapshot());
        Assert.NotNull(missing);
        Assert.True(missing!.Commercial.IsJoinMissing);
        Assert.Equal(InventoryCommercialScenarioPresentation.MissingAnalysis, missing.Commercial.Explanation);
        Assert.Empty(missing.Commercial.Scenarios);

        Assert.Equal(saleBeforeA, ReadSalePrice(a));
        Assert.Equal(saleBeforeB, ReadSalePrice(b));
        Assert.Equal(extraBeforeA, ReadExtraJson(a));
        Assert.Equal(7, snapshot.QueryCount);
        Assert.Equal(1, facts.QueryCount);
        Assert.Equal(1, setting.QueryCount);
        Assert.Equal(9, commercial.QueryCount);
    }

    [Fact]
    public void Refresh_substitui_snapshot_B4_e_detalhe_usa_o_novo()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B4ER", "Refresh");
        var first = RunPipeline();
        Assert.True(first.CommercialPresented.ByProductId.ContainsKey(id));
        var firstRow = first.CommercialPresented.ByProductId[id];

        InventoryCommercialMarginSettingsService.Save(20m);
        var second = RunPipeline();
        Assert.True(second.CommercialPresented.ByProductId.ContainsKey(id));
        var secondRow = second.CommercialPresented.ByProductId[id];
        Assert.NotSame(firstRow, secondRow);

        var detail = InventoryProjectionDetail.TryCreate(
            second.Snapshot, second.Presented, id, second.AttentionPresented, second.CommercialPresented);
        Assert.NotNull(detail);
        Assert.Same(secondRow, detail!.Commercial);
        Assert.NotSame(firstRow, detail.Commercial);
        Assert.Equal(10, ReadSalePrice(id));
    }

    [Fact]
    public void Policy_Load_e_B2_batch_uma_vez()
    {
        using var db = Begin();
        TestDataHelper.SeedSimpleProduct(20, 10, 6, "B4EP1", "P1");
        TestDataHelper.SeedSimpleProduct(18, 11, 5, "B4EP2", "P2");
        var snapshot = InventoryProjectionService.Load();
        var ids = InventoryCommercialEligibilityComposer.ProductIds(snapshot);
        Assert.True(ids.Count >= 2);
        var facts = InventoryCommercialFactsService.Load(ids);
        Assert.Equal(1, facts.QueryCount);
        Assert.Equal(ids.Count, facts.RequestedProductIds.Count);
        var setting = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(1, setting.QueryCount);
        var again = InventoryCommercialMarginSettingsService.Load();
        Assert.Equal(1, again.QueryCount);
    }

    static PipelineResult RunPipeline()
    {
        var snapshot = InventoryProjectionService.Load();
        var presented = InventoryProjectionPresentation.Apply(snapshot);
        var attention = InventoryAttentionComposer.Build(snapshot);
        var attentionPresented = InventoryAttentionPresentation.Apply(attention, presented);
        var eligibility = InventoryCommercialEligibilityComposer.Build(snapshot, attention);
        var facts = InventoryCommercialFactsService.Load(
            InventoryCommercialEligibilityComposer.ProductIds(snapshot));
        var setting = InventoryCommercialMarginSettingsService.Load();
        var policy = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        var commercial = InventoryCommercialScenarioComposer.Compose(
            snapshot.Intelligence, snapshot, attention, eligibility, facts, policy);
        var commercialPresented = InventoryCommercialScenarioPresentation.Apply(commercial);
        return new PipelineResult(snapshot, presented, attentionPresented, commercial, commercialPresented);
    }

    sealed record PipelineResult(
        InventoryProjectionSnapshot Snapshot,
        InventoryProjectionPresentationSnapshot Presented,
        InventoryAttentionPresentationSnapshot AttentionPresented,
        InventoryCommercialScenarioSnapshot Commercial,
        InventoryCommercialScenarioPresentationSnapshot CommercialPresented);

    static double ReadSalePrice(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sale_price FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    static string ReadExtraJson(int productId)
    {
        using var conn = DatabaseService.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT extra_json FROM products WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
