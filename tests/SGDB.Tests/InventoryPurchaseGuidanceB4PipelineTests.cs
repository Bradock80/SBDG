using SGDB.Models;
using SGDB.Services;
using SGDB.Tests.Infrastructure;

namespace SGDB.Tests;

/// <summary>
/// 70G-B4 — pipeline real em banco temporário. Sem EXE, sem deposito.db da loja, sem writes.
/// </summary>
[Collection(TempDatabaseCollection.Name)]
public class InventoryPurchaseGuidanceB4PipelineTests
{
    static TempDatabase Begin()
    {
        var db = TempDatabase.Create();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        TestDataHelper.SetSessionRole("admin");
        return db;
    }

    [Fact]
    public void Load_pipeline_9_queries_e_B4_zero()
    {
        using var db = Begin();
        var a = TestDataHelper.SeedSimpleProduct(20, 10, 6, "B4GA", "Reposicao A");
        var b = TestDataHelper.SeedSimpleProduct(15, 12, 7, "B4GB", "Reposicao B");
        var saleBeforeA = ReadSalePrice(a);
        var extraBeforeA = ReadExtraJson(a);

        var pipeline = RunPipeline();
        Assert.Equal(7, pipeline.Snapshot.QueryCount);
        Assert.Equal(1, pipeline.Facts.QueryCount);
        Assert.Equal(1, pipeline.Setting.QueryCount);
        Assert.Equal(9, pipeline.Commercial.QueryCount);
        Assert.Equal(0, pipeline.Guidance.QueryCount);
        Assert.Equal(0, pipeline.GuidancePresented.QueryCount);
        Assert.Equal(0, InventoryPurchaseGuidanceUi.ExpectedQueryCount);
        Assert.Equal(
            9,
            pipeline.Snapshot.QueryCount
            + pipeline.Facts.QueryCount
            + pipeline.Setting.QueryCount
            + pipeline.Guidance.QueryCount
            + InventoryPurchaseGuidanceUi.ExpectedQueryCount);

        Assert.Equal(pipeline.Snapshot.Intelligence.Rows.Count, pipeline.Guidance.Results.Count);
        Assert.Equal(pipeline.Snapshot.Intelligence.Rows.Count, pipeline.GuidancePresented.Rows.Count);
        Assert.True(pipeline.Guidance.ByProductId.ContainsKey(a));
        Assert.True(pipeline.GuidancePresented.ByProductId.ContainsKey(b));

        var rows = InventoryPurchaseGuidanceUi.Apply(
            pipeline.GuidancePresented, pipeline.Snapshot.Intelligence.Rows,
            InventoryPurchaseGuidanceUiFilter.Cleared());
        Assert.All(rows, r => Assert.NotEqual(InventoryPurchaseGuidanceAction.None, r.Action));
        var counts = InventoryPurchaseGuidanceUi.CountCards(pipeline.GuidancePresented.Rows);
        Assert.Equal(counts.All, rows.Count);

        var detail = InventoryProjectionDetail.TryCreate(
            pipeline.Snapshot, pipeline.Presented, a,
            pipeline.AttentionPresented, pipeline.CommercialPresented, pipeline.PromotionPresented);
        Assert.NotNull(detail);

        Assert.Equal(saleBeforeA, ReadSalePrice(a));
        Assert.Equal(extraBeforeA, ReadExtraJson(a));
        Assert.Equal(12, ReadSalePrice(b));
    }

    [Fact]
    public void Servidor_nao_bloqueia_modulo()
    {
        using var db = Begin();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleServer);
        try
        {
            Assert.False(StoreNetworkMode.IsModuleBlockedOnClient(InventoryPurchaseGuidanceUi.ModuleId));
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
    }

    [Fact]
    public void Cliente_bloqueia_modulo_sem_carregar_pipeline()
    {
        using var db = Begin();
        StoreNetworkMode.SetRole(StoreNetworkMode.RoleClient);
        try
        {
            Assert.True(StoreNetworkMode.IsModuleBlockedOnClient(InventoryPurchaseGuidanceUi.ModuleId));
            Assert.True(StoreNetworkMode.IsClient);
        }
        finally
        {
            StoreNetworkMode.SetRole(StoreNetworkMode.RoleStandalone);
        }
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
        var guidance = InventoryPurchaseGuidanceComposer.Compose(snapshot);
        var guidancePresented = InventoryPurchaseGuidancePresentation.Apply(
            guidance, snapshot.Intelligence, snapshot);
        return new PipelineResult(
            snapshot, presented, attentionPresented, facts, setting,
            commercial, commercialPresented, promotion, promotionPresented,
            guidance, guidancePresented);
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
        InventoryPromotionSuggestionPresentationSnapshot PromotionPresented,
        InventoryPurchaseGuidanceSnapshot Guidance,
        InventoryPurchaseGuidancePresentationSnapshot GuidancePresented);

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
