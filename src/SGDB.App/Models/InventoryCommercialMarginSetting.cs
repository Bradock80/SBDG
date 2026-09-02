namespace SGDB.Models;

/// <summary>
/// Leitura persistida da margem bruta mínima global sobre preço de venda.
/// Não é margem observada, objetivo de formação de preço, percentual do cadastro nem faixa analítica.
/// </summary>
public enum InventoryCommercialMarginSettingStatus
{
    Configured = 0,
    Missing,
    Invalid,
}

public enum InventoryCommercialMarginSettingReason
{
    None = 0,
    Missing,
    Invalid,
    EmptyValue,
    NonInvariantFormat,
    OutOfRange,
}

public sealed class InventoryCommercialMarginSetting
{
    public InventoryCommercialMarginSettingStatus Status { get; init; } =
        InventoryCommercialMarginSettingStatus.Missing;
    public decimal? MinimumGrossMarginPercent { get; init; }
    public string? RawValue { get; init; }
    public IReadOnlyList<InventoryCommercialMarginSettingReason> Reasons { get; init; } = [];
    public int QueryCount { get; init; }
}

public sealed class InventoryCommercialMarginSaveResult
{
    public bool Written { get; init; }
    public InventoryCommercialMarginSetting Setting { get; init; } = new();
}
