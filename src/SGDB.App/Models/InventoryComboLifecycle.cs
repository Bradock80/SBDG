using SGDB.Services;

namespace SGDB.Models;

public enum InventoryComboLifecycleStatus
{
    Suggested = 0,
    Active,
    Paused,
    Finished,
    Expired,
    Rejected,
}

public sealed class InventoryComboCampaign
{
    public int Id { get; init; }
    public int? ProductId { get; init; }
    public string SuggestionKey { get; init; } = "";
    public InventoryComboLifecycleStatus Status { get; init; }
    public InventoryComboLifecycleStatus EffectiveStatus { get; init; }
    public string CommercialName { get; init; } = "";
    public string InternalCode { get; init; } = "";
    public int TargetProductId { get; init; }
    public int AnchorProductId { get; init; }
    public string TargetName { get; init; } = "";
    public string AnchorName { get; init; } = "";
    public double TargetQty { get; init; } = 1;
    public double AnchorQty { get; init; } = 1;
    public double Price { get; init; }
    public double Cost { get; init; }
    public double MarginPercent { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public double MaxQty { get; init; }
    public double SoldQty { get; init; }
    public string Origin { get; init; } = InventoryComboLifecycleRules.OriginEstoqueInteligente;
    public string Reason { get; init; } = "";
    public string Limitations { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string SuggestionJson { get; init; } = "{}";
    public string ApprovedBy { get; init; } = "";
    public string? ApprovedAt { get; init; }
    public string RejectedBy { get; init; } = "";
    public string? RejectedAt { get; init; }
    public string RejectionSignature { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public string UpdatedAt { get; init; } = "";

    public double RemainingQty =>
        MaxQty <= 0 ? double.PositiveInfinity : Math.Max(0, MaxQty - SoldQty);
}

public sealed class InventoryComboApprovalDraft
{
    public string SuggestionKey { get; init; } = "";
    public string CommercialName { get; set; } = "";
    public int TargetProductId { get; init; }
    public int AnchorProductId { get; init; }
    public string TargetName { get; init; } = "";
    public string AnchorName { get; init; } = "";
    public double TargetQty { get; init; } = 1;
    public double AnchorQty { get; init; } = 1;
    public double TargetStock { get; init; }
    public double AnchorStock { get; init; }
    public double SeparatePrice { get; init; }
    public double SuggestedPrice { get; set; }
    public double DiscountAmount { get; init; }
    public double DiscountPercent { get; init; }
    public double Cost { get; init; }
    public double GrossProfit { get; init; }
    public double MarginPercent { get; init; }
    public double MaxSafeQuantity { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string Limitations { get; init; } = "";
    public string SuggestionJson { get; init; } = "{}";
    public string Signature { get; init; } = "";
    public bool HasSafePrice { get; init; } = true;
    public string? BlockReason { get; init; }
}

public sealed class InventoryComboApprovalResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public InventoryComboCampaign? Campaign { get; init; }
}

public static class InventoryComboLifecycleUi
{
    public const string SuggestionsTab = "Sugestões";
    public const string ActiveTab = "Combos ativos";
    public const string PausedTab = "Pausados";
    public const string FinishedTab = "Encerrados";
    public const string ExpiredTab = "Expirados";
    public const string RejectedTab = "Descartados";
    public const string ApproveAction = "Aprovar combo";
    public const string DiscardAction = "Descartar sugestão";
    public const string DetailsAction = "Ver detalhes";
    public const string PdvWarning =
        "O combo será cadastrado como Kit/Combo e ficará disponível no PDV.";
    public const string NoSafePrice = InventoryComboLifecycleRules.NoSafePriceMessage;
    public const string ReturnableBlocked = InventoryComboLifecycleRules.ReturnableBlockedMessage;
}
