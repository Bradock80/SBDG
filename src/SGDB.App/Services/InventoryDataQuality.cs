using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Motivos objetivos de qualidade de dados e onde corrigir.
/// Sem I/O. Não afirma ausência quando lucro/margem existem.
/// </summary>
public static class InventoryDataQuality
{
    public readonly record struct Info(
        bool Blocking,
        bool Partial,
        bool Unavailable,
        bool HasFinancial,
        InventoryDataFixLocation Location,
        string LocationText,
        IReadOnlyList<string> Reasons);

    public static Info Describe(
        InventoryAttentionResult? attention,
        InventoryPurchaseGuidanceResult? guidance,
        InventoryCommercialFacts? facts,
        ProductTurnoverRow? turnover)
    {
        var reasons = new List<string>(6);
        var location = InventoryDataFixLocation.None;
        var blocking = false;
        var partial = false;
        var unavailable = attention?.Confidence == InventoryAttentionConfidence.Unavailable
            && guidance?.Confidence == InventoryAttentionConfidence.Unavailable;

        void Add(string reason, InventoryDataFixLocation where, bool block)
        {
            if (reasons.Contains(reason))
                return;
            reasons.Add(reason);
            if (location == InventoryDataFixLocation.None)
                location = where;
            if (block)
                blocking = true;
            else
                partial = true;
        }

        if (turnover is not null
            && InventoryIntelligenceEngine.IsFinite(turnover.TotalStock)
            && turnover.TotalStock < -InventoryIntelligenceEngine.Epsilon)
            Add("Estoque negativo", InventoryDataFixLocation.StockAdjust, true);

        if (attention?.PrimaryReason == InventoryAttentionReason.NegativeStock
            || attention?.PrimaryReason == InventoryAttentionReason.NegativeLocationStock
            || attention?.PrimaryReason == InventoryAttentionReason.NegativeWarehouseStock)
            Add("Estoque negativo", InventoryDataFixLocation.StockAdjust, true);

        if (attention?.PrimaryReason == InventoryAttentionReason.TrackedQuantityExceedsWarehouse
            || attention?.PrimaryReason == InventoryAttentionReason.InconsistentStockTotals)
            Add("Saldo do lote divergente", InventoryDataFixLocation.LotsAndExpiry, true);

        if (attention?.PrimaryReason == InventoryAttentionReason.InvalidLotQuantity
            || attention?.PrimaryReason == InventoryAttentionReason.NoLot)
            Add("Validade sem quantidade confiável", InventoryDataFixLocation.LotsAndExpiry, true);

        if (attention?.PrimaryReason == InventoryAttentionReason.InvalidExpiryDate
            || attention?.PrimaryReason == InventoryAttentionReason.Undated
            || attention?.PrimaryReason == InventoryAttentionReason.DuplicateLotId)
            Add("Validade sem quantidade", InventoryDataFixLocation.LotsAndExpiry, true);

        if (facts is { CostQuality: not InventoryCommercialCostQuality.Known })
            Add("Custo ausente", InventoryDataFixLocation.ProductCadastro, true);
        if (facts is { PriceQuality: not InventoryCommercialPriceQuality.Usable })
            Add("Preço ausente", InventoryDataFixLocation.ProductCadastro, true);
        if (facts is { AllowsSale: false })
            Add("Produto sem permissão de venda", InventoryDataFixLocation.ProductCadastro, true);

        if (turnover is { PackFactor: < 2, MinStock: 0 }
            && guidance?.Action == InventoryPurchaseGuidanceAction.ConsiderReplenishment)
            Add("Fator de embalagem ausente", InventoryDataFixLocation.PackFactor, false);

        if (attention?.PrimaryReason == InventoryAttentionReason.InsufficientHistory
            || guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.InsufficientHistory)
            Add("Histórico insuficiente", InventoryDataFixLocation.Purchases, false);

        if (guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.NoPhysicalEvidence
            || attention?.PrimaryReason == InventoryAttentionReason.NoPhysicalEvidence)
            Add("Produto sem saldo físico confiável", InventoryDataFixLocation.Inventory, true);

        if (guidance?.PrimaryReason == InventoryPurchaseGuidanceReason.StructuralDataIssue
            && reasons.Count == 0)
            Add("Conflito entre fontes", InventoryDataFixLocation.Inventory, true);

        var hasFinancial = facts is { CanEvaluateFinancialScenario: true }
            || (facts?.CurrentAverageCost is double && facts.CatalogSalePrice is double);
        if (hasFinancial)
            unavailable = false;

        return new Info(
            blocking,
            partial,
            unavailable && !hasFinancial,
            hasFinancial,
            location,
            LocationLabel(location),
            reasons);
    }

    public static string LocationLabel(InventoryDataFixLocation location) =>
        location switch
        {
            InventoryDataFixLocation.ProductCadastro => "Cadastro do produto",
            InventoryDataFixLocation.StockAdjust => "Ajuste de estoque",
            InventoryDataFixLocation.LotsAndExpiry => "Lotes e validades",
            InventoryDataFixLocation.Purchases => "Compras",
            InventoryDataFixLocation.Inventory => "Inventário",
            InventoryDataFixLocation.PackFactor => "Fator de embalagem",
            _ => "",
        };
}
