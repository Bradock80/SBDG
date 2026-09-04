namespace SGDB.Models;

/// <summary>
/// 71A-B3 — fatos 70F do par + política de margem. Sem I/O, elegibilidade ou coocorrência.
/// </summary>
public sealed class InventoryComboPairFinancialInput
{
    public InventoryCommercialFacts? TargetFacts { get; init; }
    public InventoryCommercialFacts? AnchorFacts { get; init; }
    public InventoryCommercialMarginPolicy? MinGrossMarginPolicy { get; init; }
}

public enum InventoryComboPairFinancialStatus
{
    Available = 0,
    Unavailable,
}

public enum InventoryComboPairFinancialReason
{
    None = 0,
    TargetFinancialUnavailable,
    AnchorFinancialUnavailable,
    MarginPolicyUnavailable,
    InvalidPairValues,
    PriceBelowFloor,
}

public enum InventoryComboPairFinancialScenarioKind
{
    CurrentPrices = 0,
    TargetReductionReference,
}

/// <summary>
/// Cenário financeiro unitário do par. Lucro e margem brutos do preço simulado.
/// ReductionFromCurrent é fato numérico, sem linguagem comercial.
/// </summary>
public sealed class InventoryComboPairFinancialScenario
{
    public InventoryComboPairFinancialScenarioKind Kind { get; init; }
    public double PairPrice { get; init; }
    public double GrossProfit { get; init; }
    public double GrossMargin { get; init; }
    public double ReductionFromCurrent { get; init; }
}

/// <summary>
/// Fatos financeiros do par target+âncora. Sem ranking, sugestão ou apresentação.
/// </summary>
public sealed class InventoryComboPairFinancialFacts
{
    public InventoryComboPairFinancialStatus Status { get; init; } =
        InventoryComboPairFinancialStatus.Unavailable;
    public InventoryComboPairFinancialReason Reason { get; init; }
    public double? NormalPairPrice { get; init; }
    public double? PairCost { get; init; }
    public double? PairFloorPrice { get; init; }
    public double? TargetFloorPrice { get; init; }
    public IReadOnlyList<InventoryComboPairFinancialScenario> Scenarios { get; init; } = [];
}
