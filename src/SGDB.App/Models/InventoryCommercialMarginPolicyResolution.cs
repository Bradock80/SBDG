namespace SGDB.Models;

/// <summary>
/// Resolução 70F-B3C da margem bruta mínima global. Somente política da loja.
/// Não é piso, promoção nem política por grupo/produto.
/// </summary>
public enum InventoryCommercialMarginPolicyResolutionStatus
{
    Available = 0,
    Missing,
    Invalid,
}

/// <summary>
/// Origem efetiva. Nesta versão só existe Global; None = sem política utilizável.
/// </summary>
public enum InventoryCommercialMarginPolicySource
{
    None = 0,
    Global,
}

public sealed class InventoryCommercialMarginPolicyResolution
{
    public InventoryCommercialMarginPolicyResolutionStatus Status { get; init; } =
        InventoryCommercialMarginPolicyResolutionStatus.Missing;
    public InventoryCommercialMarginPolicySource Source { get; init; } =
        InventoryCommercialMarginPolicySource.None;
    public decimal? EffectiveMinimumGrossMarginPercent { get; init; }
    public IReadOnlyList<InventoryCommercialMarginSettingReason> Reasons { get; init; } = [];
}
