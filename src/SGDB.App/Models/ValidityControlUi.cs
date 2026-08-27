using SGDB.Utils;

namespace SGDB.Models;

/// <summary>
/// Rótulos da Central de Validades (70B3). Não calcula regra de negócio.
/// </summary>
public static class ValidityControlUi
{
    public const string FridgeDisclaimer =
        "Lotes e validades referem-se ao estoque rastreado do depósito. A geladeira ainda não possui rastreamento individual por lote.";

    public const string FridgeNoLotTracking =
        "Geladeira sem rastreamento por lote nesta versão.";

    public const string LotValueNotLoss =
        "Valor do lote = quantidade registrada × custo utilizado. Não representa previsão de perda.";

    public static string ActionLabel(ValiditySuggestedAction action) =>
        action switch
        {
            ValiditySuggestedAction.RemoveExpired => "Retirar / conferir",
            ValiditySuggestedAction.PrioritizeSale => "Priorizar saída",
            ValiditySuggestedAction.Monitor => "Monitorar",
            ValiditySuggestedAction.ReviewData => "Revisar dados",
            ValiditySuggestedAction.ConsiderPromotion => "—",
            _ => "—",
        };

    public static string CostSourceLabel(LotCostSource source) =>
        source switch
        {
            LotCostSource.LotRecorded => "Lançado no lote",
            LotCostSource.CurrentAverageEstimate => "Estimado pelo custo médio atual",
            _ => "Sem custo disponível",
        };

    public static string LotValueTooltip(ValidityControlRow row)
    {
        var origin = CostSourceLabel(row.CostSource);
        if (row.LotValue is null)
            return $"{origin}. Valor do lote indisponível. {LotValueNotLoss}";
        var used = row.UsedCost is double cost ? ProductPriceHelper.MoneyBr(cost) : "—";
        return $"{origin}. Custo utilizado: {used}. {LotValueNotLoss}";
    }

    public static string ActionTooltip(ValidityControlRow row)
    {
        var reason = row.SuggestedActionReason?.Trim() ?? "";
        return string.IsNullOrEmpty(reason)
            ? ActionLabel(row.SuggestedAction)
            : reason;
    }

    public static string FormatSelectionDetail(ValidityControlRow row)
    {
        var used = row.UsedCost is double cost ? ProductPriceHelper.MoneyBr(cost) : "—";
        var depot = ProductLotListRow.FormatQty(row.Stock);
        var fridge = ProductLotListRow.FormatQty(row.StockFridge);
        var reason = string.IsNullOrWhiteSpace(row.SuggestedActionReason)
            ? "—"
            : row.SuggestedActionReason.Trim();
        return
            $"{row.ProductName} · Lote {row.LotDisplay} · {row.ExpiryDisplay} · " +
            $"Qtd {row.QtyDisplay} · Valor {row.LotValueDisplay} · Custo {used} " +
            $"({CostSourceLabel(row.CostSource)}) · {ActionLabel(row.SuggestedAction)} · {reason} · " +
            $"Depósito {depot} · Geladeira {fridge}. {FridgeNoLotTracking}";
    }
}
