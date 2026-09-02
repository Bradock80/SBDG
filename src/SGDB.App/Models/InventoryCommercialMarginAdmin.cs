using SGDB.Services;

namespace SGDB.Models;

public sealed class InventoryCommercialMarginAdminSnapshot
{
    public InventoryCommercialMarginPolicyResolutionStatus Status { get; init; }
    public decimal? EffectivePercent { get; init; }
    public string? RawValue { get; init; }
    public string StatusText { get; init; } = "";
    public string EditorText { get; init; } = "";
    public bool CanMutate { get; init; }
    public bool StationAllowsWrite { get; init; }
    public IReadOnlyList<InventoryCommercialMarginSettingReason> Reasons { get; init; } = [];
}

public sealed class InventoryCommercialMarginAdminResult
{
    public bool Succeeded { get; init; }
    public bool Audited { get; init; }
    public string Message { get; init; } = "";
    public InventoryCommercialMarginAdminSnapshot Snapshot { get; init; } = new();
}
