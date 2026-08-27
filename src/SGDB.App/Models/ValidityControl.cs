using SGDB.Services;
using SGDB.Utils;

namespace SGDB.Models;

public enum ValidityControlFilterKind
{
    All = 0,
    Expired,
    Today,
    Days7,
    Days15,
    Days30,
    Days60,
    Days90,
    Uninformed,
}

public enum ValidityControlRowKind
{
    Lot = 0,
    UninformedLot,
    MissingExpiry,
    UntrackedStock,
}

/// <summary>
/// Origem do custo usado no Valor do lote. Não afirma FIFO nem custo histórico exato.
/// </summary>
public enum LotCostSource
{
    Unavailable = 0,
    LotRecorded,
    CurrentAverageEstimate,
}

/// <summary>
/// Recomendação da Central de Validades. Não executa a ação.
/// RemoveExpired = retirar/conferir vencido, não baixa automática.
/// ConsiderPromotion = reservado para 70D/70F (giro + sobra). O 70B2 não emite.
/// </summary>
public enum ValiditySuggestedAction
{
    None = 0,
    Monitor,
    PrioritizeSale,
    ConsiderPromotion,
    RemoveExpired,
    ReviewData,
}

public sealed class ValidityControlRow
{
    public int ProductId { get; init; }
    public int? LotId { get; init; }
    public string ProductName { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string BrandName { get; init; } = "";
    public string LotDisplay { get; init; } = "—";
    public double Quantity { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public int? DaysRemaining { get; init; }
    public ProductExpiryStatus Status { get; init; } = ProductExpiryStatus.Uninformed;
    public string StatusDisplay { get; init; } = ProductExpiryStatus.Uninformed.Label;
    public double UnitCost { get; init; }
    public string OriginDisplay { get; init; } = "—";
    public ValidityControlRowKind RowKind { get; init; }
    public string Tone { get; init; } = "ok";
    public double StockFridge { get; init; }
    public double Stock { get; init; }
    /// <summary>Custo efetivamente usado no Valor do lote; null se Sem custo.</summary>
    public double? UsedCost { get; init; }
    public LotCostSource CostSource { get; init; }
    /// <summary>qty segura × custo usado; null se Sem custo (não confundir com R$ 0,00).</summary>
    public double? LotValue { get; init; }
    /// <summary>Recomendação; não executa. 0 = mais urgente.</summary>
    public ValiditySuggestedAction SuggestedAction { get; init; }
    public int AttentionRank { get; init; }
    public string SuggestedActionReason { get; init; } = "";

    public string QtyDisplay => ProductLotListRow.FormatQty(Quantity);
    public string ActionUiDisplay => ValidityControlUi.ActionLabel(SuggestedAction);
    public string CostSourceDisplay => ValidityControlUi.CostSourceLabel(CostSource);
    public string LotValueTooltip => ValidityControlUi.LotValueTooltip(this);
    public string ActionTooltip => ValidityControlUi.ActionTooltip(this);
    public string SuggestedActionDisplay => ActionUiDisplay;
    public string ExpiryDisplay => ProductExpiryService.FormatDisplay(ExpiryDate);
    public string DaysDisplay => ProductExpiryService.FormatDays(DaysRemaining);
    public string CostDisplay => ProductPriceHelper.MoneyBr(UnitCost);
    public string LotValueDisplay => ProductPriceHelper.MoneyBrOrDash(LotValue);
    public ProductExpiryStatusKind Bucket => Status.Kind;
}

public sealed class ValidityControlCards
{
    public int Expired { get; set; }
    public int Today { get; set; }
    public int Days7 { get; set; }
    public int Days15 { get; set; }
    public int Days30 { get; set; }
    public int Days60 { get; set; }
    public int Days90 { get; set; }
    public int Ok { get; set; }
    public int Uninformed { get; set; }

    public int Total =>
        Expired + Today + Days7 + Days15 + Days30 + Days60 + Days90 + Ok + Uninformed;
}

public sealed class ValidityControlSnapshot
{
    public IReadOnlyList<ValidityControlRow> Rows { get; init; } = [];
    public ValidityControlCards Cards { get; init; } = new();
}

public sealed class ValidityControlProductInput
{
    public int ProductId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string BrandName { get; init; } = "";
    public double Stock { get; init; }
    public double StockFridge { get; init; }
    public double CostPrice { get; init; }
    public bool ExplicitExpiryControl { get; init; }
    public IReadOnlyList<ProductLot> Lots { get; init; } = [];
}
