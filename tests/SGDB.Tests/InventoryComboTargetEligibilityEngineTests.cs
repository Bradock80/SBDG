using System.IO;
using System.Reflection;
using SGDB.Models;
using SGDB.Services;

namespace SGDB.Tests;

/// <summary>
/// 71A-B1 — alvo puro. Sem SQL, UI, par, preço, B5, PurchaseService ou kit.
/// </summary>
public class InventoryComboTargetEligibilityEngineTests
{
    [Fact]
    public void QueryCount_e_zero()
    {
        Assert.Equal(0, InventoryComboTargetEligibilityEngine.ExpectedQueryCount);
        Assert.Equal(0, InventoryComboEligibility.ExpectedQueryCount);
        Assert.Equal(9, InventoryCommercialScenarioComposer.ExpectedPipelineQueryCount);
    }

    [Fact]
    public void Epsilon_reusa_70C() =>
        Assert.Equal(InventoryIntelligenceEngine.Epsilon, InventoryComboTargetEligibilityEngine.Epsilon);

    [Fact]
    public void Expired_bloqueia()
    {
        var result = Eval(attention: Attention(primary: InventoryAttentionReason.Expired));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetExpired);
    }

    [Fact]
    public void ExpiresToday_bloqueia()
    {
        var result = Eval(attention: Attention(primary: InventoryAttentionReason.ExpiresToday));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetExpiresToday);
    }

    [Fact]
    public void Composition_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(composition: true, idle: true));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetComposition);
    }

    [Fact]
    public void AmbiguousUnit_bloqueia()
    {
        var result = Eval(facts: ComboEligibilityHarness.Facts(
            canEvaluate: false,
            limitations: InventoryCommercialFactsReason.AmbiguousSaleUnit));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetAmbiguousUnit);
    }

    [Fact]
    public void Estoque_negativo_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(stock: -1, band: InventoryCoverageBand.Negative));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetStockUnsafe);
    }

    [Fact]
    public void Estoque_zero_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 0, vmv30: 0, band: InventoryCoverageBand.Zero));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetStockUnsafe);
    }

    [Fact]
    public void Anomalia_de_local_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(anomaly: true, idle: true));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetStockUnsafe);
    }

    [Fact]
    public void Sem_evidencia_fisica_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(evidence: false, idle: true));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetNoPhysicalEvidence);
    }

    [Fact]
    public void ReviewData_bloqueia()
    {
        var result = Eval(attention: ComboEligibilityHarness.Attention(
            primary: InventoryAttentionReason.InconsistentStockTotals,
            family: InventoryAttentionFamily.DataQuality,
            action: InventoryOperatorAction.ReviewData,
            surplus: 8));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetReviewData);
    }

    [Fact]
    public void ZeroWithDemand_bloqueia()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 0, vmv30: 2, band: InventoryCoverageBand.Zero, zeroWithDemand: true));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetZeroWithDemand);
    }

    [Fact]
    public void Unavailable_bloqueia()
    {
        var result = Eval(attention: ComboEligibilityHarness.Attention(
            confidence: InventoryAttentionConfidence.Unavailable,
            surplus: 9));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetAnalysisUnavailable);
    }

    [Fact]
    public void ExpirySurplus_elegivel()
    {
        var result = Eval(attention: ComboEligibilityHarness.Attention(
            primary: InventoryAttentionReason.SurplusAtExpiry,
            family: InventoryAttentionFamily.Expiry,
            action: InventoryOperatorAction.PrioritizeSale,
            surplus: 4,
            excess: 20));
        AssertEligible(result, ComboTargetEligibilityReason.ExpirySurplus);
        Assert.Equal(InventoryAttentionConfidence.Reliable, result.Confidence);
    }

    [Fact]
    public void ProjectedExcess_elegivel()
    {
        var result = Eval(attention: ComboEligibilityHarness.Attention(
            primary: InventoryAttentionReason.ProjectedExcess30,
            family: InventoryAttentionFamily.Excess,
            action: InventoryOperatorAction.EvaluateExcess,
            excess: 12));
        AssertEligible(result, ComboTargetEligibilityReason.ProjectedExcess);
    }

    [Fact]
    public void Idle_elegivel()
    {
        var result = Eval(
            turnover: ComboEligibilityHarness.Turnover(idle: true),
            attention: ComboEligibilityHarness.Attention(
                primary: InventoryAttentionReason.Idle,
                family: InventoryAttentionFamily.Turnover,
                action: InventoryOperatorAction.Monitor));
        AssertEligible(result, ComboTargetEligibilityReason.Idle);
    }

    [Fact]
    public void Precedencia_surplus_vence_excess_e_idle()
    {
        var result = Eval(
            turnover: ComboEligibilityHarness.Turnover(idle: true),
            attention: ComboEligibilityHarness.Attention(surplus: 3, excess: 40));
        AssertEligible(result, ComboTargetEligibilityReason.ExpirySurplus);
    }

    [Fact]
    public void Precedencia_excess_vence_idle()
    {
        var result = Eval(
            turnover: ComboEligibilityHarness.Turnover(idle: true),
            attention: ComboEligibilityHarness.Attention(excess: 8));
        AssertEligible(result, ComboTargetEligibilityReason.ProjectedExcess);
    }

    [Fact]
    public void NearExpiryWithoutSurplus_sozinho_nao_elegivel()
    {
        var result = Eval(attention: ComboEligibilityHarness.Attention(
            primary: InventoryAttentionReason.NearExpiryWithoutSurplus,
            family: InventoryAttentionFamily.Expiry,
            action: InventoryOperatorAction.PrioritizeSale));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetNoTurnoverNeed);
    }

    [Fact]
    public void Cobertura_critica_sozinha_nao_e_alvo()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 2, vmv30: 1, band: InventoryCoverageBand.Critical));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetNoTurnoverNeed);
    }

    [Fact]
    public void Cobertura_baixa_sozinha_nao_e_alvo()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(
            stock: 5, vmv30: 1, band: InventoryCoverageBand.Low));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetNoTurnoverNeed);
    }

    [Fact]
    public void Sem_tese_bloqueia()
    {
        var result = Eval();
        AssertBlocked(result, ComboTargetEligibilityReason.TargetNoTurnoverNeed);
    }

    [Fact]
    public void Expired_vence_excess()
    {
        var result = Eval(attention: ComboEligibilityHarness.Attention(
            primary: InventoryAttentionReason.Expired,
            family: InventoryAttentionFamily.Expiry,
            excess: 25));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetExpired);
    }

    [Fact]
    public void Composition_vence_idle()
    {
        var result = Eval(turnover: ComboEligibilityHarness.Turnover(composition: true, idle: true));
        AssertBlocked(result, ComboTargetEligibilityReason.TargetComposition);
    }

    [Fact]
    public void Surplus_NaN_nao_e_tese()
    {
        var result = Eval(attention: ComboEligibilityHarness.Attention(surplus: double.NaN, excess: 5));
        AssertEligible(result, ComboTargetEligibilityReason.ProjectedExcess);
    }

    [Fact]
    public void Engine_nao_depende_de_SQL_UI_B5_compra_ou_kit()
    {
        AssertNoTypeReference(typeof(InventoryComboTargetEligibilityEngine), "Sqlite", "DatabaseService");
        foreach (var relative in new[]
                 {
                     Path.Combine("src", "SGDB.App", "Services", "InventoryComboTargetEligibilityEngine.cs"),
                     Path.Combine("src", "SGDB.App", "Services", "InventoryComboEligibility.cs"),
                     Path.Combine("src", "SGDB.App", "Models", "InventoryComboEligibility.cs"),
                 })
        {
            var text = ReadSource(relative.Split(Path.DirectorySeparatorChar));
            Assert.DoesNotContain("DatabaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sqlite", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PurchaseService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PdvService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ProductCompositionService", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryPromotionSuggestion", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InventoryComboSuggestion", text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Windows", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MessageBox", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT ", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    static InventoryComboTargetEligibility Eval(
        ProductTurnoverRow? turnover = null,
        InventoryAttentionResult? attention = null,
        InventoryCommercialFacts? facts = null,
        InventoryPurchaseGuidanceResult? guidance = null) =>
        InventoryComboTargetEligibilityEngine.Evaluate(ComboEligibilityHarness.Input(
            turnover, attention, facts, guidance));

    static InventoryAttentionResult Attention(
        InventoryAttentionReason primary = InventoryAttentionReason.None,
        InventoryAttentionFamily family = InventoryAttentionFamily.Normal,
        InventoryOperatorAction action = InventoryOperatorAction.Monitor,
        InventoryAttentionConfidence confidence = InventoryAttentionConfidence.Reliable,
        double? surplus = null,
        double? excess = null) =>
        ComboEligibilityHarness.Attention(primary, family, action, confidence, surplus, excess);

    static void AssertBlocked(InventoryComboTargetEligibility result, ComboTargetEligibilityReason reason)
    {
        Assert.Equal(ComboEligibilityStatus.Blocked, result.Status);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(ComboEligibilityHarness.ProductId, result.ProductId);
    }

    static void AssertEligible(InventoryComboTargetEligibility result, ComboTargetEligibilityReason reason)
    {
        Assert.Equal(ComboEligibilityStatus.Eligible, result.Status);
        Assert.Equal(reason, result.Reason);
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
