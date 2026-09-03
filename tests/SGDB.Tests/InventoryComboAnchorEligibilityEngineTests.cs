using System.IO;
using System.Reflection;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71A-B1 — âncora pura. Sem SQL, UI, par, preço, B5, PurchaseService ou kit.
/// </summary>
public class InventoryComboAnchorEligibilityEngineTests
{
    [Fact]
    public void QueryCount_e_zero() =>
        Assert.Equal(0, InventoryComboAnchorEligibilityEngine.ExpectedQueryCount);

    [Fact]
    public void Saudavel_cobertura_normal_elegivel()
    {
        var result = Eval();
        AssertEligible(result);
        Assert.Equal(InventoryAttentionConfidence.Reliable, result.Confidence);
    }

    [Fact]
    public void Composition_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(composition: true));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorComposition);
    }

    [Fact]
    public void AmbiguousUnit_bloqueia()
    {
        var result = Eval(facts: ComboEligibilityHarness.Facts(
            canEvaluate: false,
            limitations: InventoryCommercialFactsReason.AmbiguousSaleUnit));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorAmbiguousUnit);
    }

    [Fact]
    public void Estoque_negativo_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: -2, band: InventoryCoverageBand.Negative));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorStockUnsafe);
    }

    [Fact]
    public void Estoque_zero_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 0, band: InventoryCoverageBand.Zero));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorStockUnsafe);
    }

    [Fact]
    public void Anomalia_de_local_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(anomaly: true));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorLocationAnomaly);
    }

    [Fact]
    public void Sem_evidencia_fisica_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(evidence: false));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorNoPhysicalEvidence);
    }

    [Fact]
    public void Historico_menor_que_30_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(history: 20));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorInsufficientHistory);
    }

    [Fact]
    public void Financeiro_indisponivel_bloqueia()
    {
        var result = Eval(facts: ComboEligibilityHarness.Facts(canEvaluate: false));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorFinancialUnavailable);
    }

    [Fact]
    public void Custo_invalido_bloqueia()
    {
        var result = Eval(facts: ComboEligibilityHarness.Facts(
            canEvaluate: false,
            cost: InventoryCommercialCostQuality.Invalid));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorFinancialUnavailable);
    }

    [Fact]
    public void Preco_invalido_bloqueia()
    {
        var result = Eval(facts: ComboEligibilityHarness.Facts(
            canEvaluate: false,
            price: InventoryCommercialPriceQuality.Invalid));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorFinancialUnavailable);
    }

    [Fact]
    public void Validade_urgente_bloqueia()
    {
        var result = Eval(attention: ComboEligibilityHarness.Attention(
            primary: InventoryAttentionReason.NearExpiryWithoutSurplus,
            family: InventoryAttentionFamily.Expiry,
            action: InventoryOperatorAction.PrioritizeSale));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorExpiryUrgent);
    }

    [Fact]
    public void ReviewData_70G_bloqueia()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.ReviewData,
            primary: InventoryPurchaseGuidanceReason.StructuralDataIssue,
            status: InventoryPurchaseGuidanceStatus.ReviewData));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorReviewData);
    }

    [Fact]
    public void ReviewData_vence_cobertura_normal()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.ReviewData,
            primary: InventoryPurchaseGuidanceReason.StructuralDataIssue,
            status: InventoryPurchaseGuidanceStatus.ReviewData));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorReviewData);
    }

    [Fact]
    public void NotApplicable_bloqueia()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.None,
            primary: InventoryPurchaseGuidanceReason.CompositionProduct,
            status: InventoryPurchaseGuidanceStatus.NotApplicable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorNotApplicable);
    }

    [Fact]
    public void ConsiderReplenishment_bloqueia()
    {
        var result = Eval(
            turnover: ComboEligibilityHarness.Turnover(stock: 80, vmv30: 2, band: InventoryCoverageBand.Normal),
            guidance: ComboEligibilityHarness.Guidance(
                action: InventoryPurchaseGuidanceAction.ConsiderReplenishment,
                primary: InventoryPurchaseGuidanceReason.LowCoverage,
                status: InventoryPurchaseGuidanceStatus.GuidanceAvailable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorConsiderReplenishment);
    }

    [Fact]
    public void ConsiderReplenishment_vence_cobertura_normal()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.ConsiderReplenishment,
            primary: InventoryPurchaseGuidanceReason.CriticalCoverage,
            status: InventoryPurchaseGuidanceStatus.GuidanceAvailable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorConsiderReplenishment);
    }

    [Fact]
    public void DoNotReplenishNow_excess_bloqueia()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            primary: InventoryPurchaseGuidanceReason.ProjectedExcess30,
            status: InventoryPurchaseGuidanceStatus.GuidanceAvailable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorDoNotReplenishNow);
    }

    [Fact]
    public void DoNotReplenishNow_expiry_surplus_bloqueia()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            primary: InventoryPurchaseGuidanceReason.ProjectedExpirySurplus,
            status: InventoryPurchaseGuidanceStatus.GuidanceAvailable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorDoNotReplenishNow);
    }

    [Fact]
    public void DoNotReplenishNow_idle_bloqueia()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            primary: InventoryPurchaseGuidanceReason.IdleStock,
            status: InventoryPurchaseGuidanceStatus.GuidanceAvailable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorDoNotReplenishNow);
    }

    [Fact]
    public void Expired_bloqueia()
    {
        var result = Eval(
            attention: ComboEligibilityHarness.Attention(primary: InventoryAttentionReason.Expired),
            guidance: ComboEligibilityHarness.Guidance(
                action: InventoryPurchaseGuidanceAction.DoNotReplenishNow,
                primary: InventoryPurchaseGuidanceReason.Expired,
                status: InventoryPurchaseGuidanceStatus.GuidanceAvailable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorExpiryUrgent);
    }

    [Fact]
    public void ExpiresToday_bloqueia()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.DoNotReplenishNow,
            primary: InventoryPurchaseGuidanceReason.ExpiresToday,
            status: InventoryPurchaseGuidanceStatus.GuidanceAvailable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorExpiryUrgent);
    }

    [Fact]
    public void Monitor_InsufficientHistory_bloqueia()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.Monitor,
            primary: InventoryPurchaseGuidanceReason.InsufficientHistory));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorInsufficientHistory);
    }

    [Fact]
    public void Monitor_LocationLimitation_bloqueia()
    {
        var result = Eval(guidance: ComboEligibilityHarness.Guidance(
            action: InventoryPurchaseGuidanceAction.Monitor,
            primary: InventoryPurchaseGuidanceReason.LocationLimitation));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorLocationAnomaly);
    }

    [Fact]
    public void Monitor_NoObservableDemand_bloqueia()
    {
        var result = Eval(
            turnover: ComboEligibilityHarness.Turnover(vmv30: 0, band: InventoryCoverageBand.NotCalculable),
            guidance: ComboEligibilityHarness.Guidance(
                action: InventoryPurchaseGuidanceAction.Monitor,
                primary: InventoryPurchaseGuidanceReason.NoObservableDemand));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorNoObservableDemand);
    }

    [Theory]
    [InlineData(InventoryCoverageBand.Negative)]
    [InlineData(InventoryCoverageBand.Zero)]
    [InlineData(InventoryCoverageBand.NotCalculable)]
    [InlineData(InventoryCoverageBand.Critical)]
    [InlineData(InventoryCoverageBand.Low)]
    [InlineData(InventoryCoverageBand.Attention)]
    public void Coverage_nao_normal_bloqueia(InventoryCoverageBand band)
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(band: band));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorCoverageUnsafe);
    }

    [Fact]
    public void Attention_com_VMV_alto_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 200, vmv30: 20, band: InventoryCoverageBand.Attention));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorCoverageUnsafe);
    }

    [Fact]
    public void Guardrail_cobertura_restante_igual_a_15_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 16, vmv30: 1, band: InventoryCoverageBand.Normal));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorUnitGuardrail);
    }

    [Fact]
    public void Guardrail_cobertura_restante_maior_que_15_elegivel()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 17, vmv30: 1, band: InventoryCoverageBand.Normal));
        AssertEligible(result);
    }

    [Fact]
    public void Vmv_zero_estoque_1_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 1, vmv30: 0, band: InventoryCoverageBand.NotCalculable));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorNoObservableDemand);
    }

    [Fact]
    public void Vmv_zero_estoque_2_nao_e_ancora_sem_giro()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 2, vmv30: 0, band: InventoryCoverageBand.Normal));
        AssertBlocked(result, ComboAnchorEligibilityReason.AnchorNoObservableDemand);
    }

    [Fact]
    public void Engine_nao_depende_de_SQL_UI_B5_compra_ou_kit()
    {
        AssertNoTypeReference(typeof(InventoryComboAnchorEligibilityEngine), "Sqlite", "DatabaseService");
        var text = ReadSource("src", "SGDB.App", "Services", "InventoryComboAnchorEligibilityEngine.cs");
        Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PurchaseService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PdvService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductCompositionService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryPromotionSuggestion", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PairFloor", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT ", text, StringComparison.OrdinalIgnoreCase);
    }

    static InventoryComboAnchorEligibility Eval(
        ProductTurnoverRow? turnover = null,
        InventoryAttentionResult? attention = null,
        InventoryCommercialFacts? facts = null,
        InventoryPurchaseGuidanceResult? guidance = null) =>
        InventoryComboAnchorEligibilityEngine.Evaluate(ComboEligibilityHarness.Input(
            turnover, attention, facts, guidance));

    static void AssertBlocked(InventoryComboAnchorEligibility result, ComboAnchorEligibilityReason reason)
    {
        Assert.Equal(ComboEligibilityStatus.Blocked, result.Status);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(ComboEligibilityHarness.ProductId, result.ProductId);
    }

    static void AssertEligible(InventoryComboAnchorEligibility result)
    {
        Assert.Equal(ComboEligibilityStatus.Eligible, result.Status);
        Assert.Equal(ComboAnchorEligibilityReason.HealthyNormalCoverage, result.Reason);
        Assert.Equal(ComboEligibilityHarness.ProductId, result.ProductId);
    }

    static void AssertNoTypeReference(Type type, params string[] tokens)
    {
        var text = type.ToString() + string.Join(
            " ",
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.ToString() ?? ""));
        foreach (var token in tokens)
            Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
    }

    static string ReadSource(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}
