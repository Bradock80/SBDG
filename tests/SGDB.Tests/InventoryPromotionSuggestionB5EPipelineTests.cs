using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70F-B5E — pipeline real em banco temporário. Sem EXE, sem deposito.db da loja, sem writes.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryPromotionSuggestionB5EPipelineTests
{
    static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void Load_pipeline_gera_B5_com_9_queries_e_populacao_70C()
    {
        using var db = Begin();
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B5EA", "Sugestao A");
        var b = TestDataHelper.SeedSimpleProduct(15, 12, 7, "B5EB", "Sugestao B");
        var saleBeforeA = ReadSalePrice(a);
        var extraBeforeA = ReadExtraJson(a);

        var pipeline = RunPipeline();
        Assert.Equal(7, pipeline.Snapshot.QueryCount);
        Assert.Equal(9, pipeline.Commercial.QueryCount);
        Assert.Equal(9, pipeline.Promotion.QueryCount);
        Assert.Equal(9, pipeline.PromotionPresented.QueryCount);
        Assert.Equal(pipeline.Snapshot.Intelligence.Rows.Count, pipeline.Promotion.Rows.Count);
        Assert.Equal(pipeline.Snapshot.Intelligence.Rows.Count, pipeline.PromotionPresented.Rows.Count);
        Assert.Equal(
            pipeline.Snapshot.Intelligence.Rows.Select(r => r.ProductId),
            pipeline.Promotion.Rows.Select(r => r.ProductId));
        Assert.True(pipeline.Promotion.ByProductId.ContainsKey(a));
        Assert.True(pipeline.PromotionPresented.ByProductId.ContainsKey(a));
        Assert.True(pipeline.PromotionPresented.ByProductId.ContainsKey(b));
        Assert.Equal(1, pipeline.Facts.QueryCount);
        Assert.Equal(1, pipeline.Setting.QueryCount);

        var detail = InventoryProjectionDetail.TryCreate(
            pipeline.Snapshot, pipeline.Presented, a,
            pipeline.AttentionPresented, pipeline.CommercialPresented, pipeline.PromotionPresented);
        Assert.NotNull(detail);
        Assert.Equal(a, detail!.PromotionSuggestion.ProductId);
        Assert.False(detail.PromotionSuggestion.IsJoinMissing);
        Assert.Same(pipeline.PromotionPresented.ByProductId[a], detail.PromotionSuggestion);

        var missing = InventoryProjectionDetail.TryCreate(
            pipeline.Snapshot, pipeline.Presented, a,
            pipeline.AttentionPresented, pipeline.CommercialPresented,
            new InventoryPromotionSuggestionPresentationSnapshot());
        Assert.NotNull(missing);
        Assert.True(missing!.PromotionSuggestion.IsJoinMissing);
        Assert.Equal(
            InventoryPromotionSuggestionPresentation.MissingAnalysis,
            missing.PromotionSuggestion.Explanation);
        Assert.Empty(missing.PromotionSuggestion.ScenarioOptions);

        Assert.Equal(saleBeforeA, ReadSalePrice(a));
        Assert.Equal(extraBeforeA, ReadExtraJson(a));
        Assert.Equal(9, pipeline.Promotion.QueryCount);
    }

    [Fact]
    public void Refresh_substitui_snapshot_B5_e_detalhe_usa_o_novo()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B5ER", "Refresh B5");
        var first = RunPipeline();
        Assert.True(first.PromotionPresented.ByProductId.ContainsKey(id));
        var firstRow = first.PromotionPresented.ByProductId[id];

        InventoryCommercialMarginSettingsService.Save(20m);
        var second = RunPipeline();
        Assert.True(second.PromotionPresented.ByProductId.ContainsKey(id));
        var secondRow = second.PromotionPresented.ByProductId[id];
        Assert.NotSame(firstRow, secondRow);

        var detail = InventoryProjectionDetail.TryCreate(
            second.Snapshot, second.Presented, id,
            second.AttentionPresented, second.CommercialPresented, second.PromotionPresented);
        Assert.NotNull(detail);
        Assert.Same(secondRow, detail!.PromotionSuggestion);
        Assert.NotSame(firstRow, detail.PromotionSuggestion);
        Assert.Equal(10, ReadSalePrice(id));
    }

    [Fact]
    public void Extras_B4_nao_entram_na_populacao()
    {
        using var db = Begin();
        var id = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B5EX", "Pop");
        var pipeline = RunPipeline();
        Assert.Equal(
            pipeline.Snapshot.Intelligence.Rows.Count,
            pipeline.Promotion.Rows.Count);
        Assert.Contains(id, pipeline.Promotion.Rows.Select(r => r.ProductId));
        Assert.DoesNotContain(0, pipeline.Promotion.Rows.Select(r => r.ProductId));
        Assert.Equal(pipeline.Commercial.Rows.Count, pipeline.Promotion.Rows.Count);
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
        var promotion = InventoryPromotionSuggestionComposer.Compose(snapshot.Intelligence, commercial);
        var promotionPresented = InventoryPromotionSuggestionPresentation.Apply(promotion);
        return new PipelineResult(
            snapshot, presented, attentionPresented, facts, setting,
            commercial, commercialPresented, promotion, promotionPresented);
    }

    sealed record PipelineResult(
        InventoryProjectionSnapshot Snapshot,
        InventoryProjectionPresentationSnapshot Presented,
        InventoryAttentionPresentationSnapshot AttentionPresented,
        InventoryCommercialFactsSnapshot Facts,
        InventoryCommercialMarginSetting Setting,
        InventoryCommercialScenarioSnapshot Commercial,
        InventoryCommercialScenarioPresentationSnapshot CommercialPresented,
        InventoryPromotionSuggestionSnapshot Promotion,
        InventoryPromotionSuggestionPresentationSnapshot PromotionPresented);

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
