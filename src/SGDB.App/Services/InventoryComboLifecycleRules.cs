using SGDB.Models;

namespace SGDB.Services;

/// <summary>
/// Regras puras do ciclo comercial de combo. Sem I/O.
/// </summary>
public static class InventoryComboLifecycleRules
{
    public const string OriginEstoqueInteligente = "Estoque Inteligente";
    public const string NoSafePriceMessage =
        "Nenhum preço de combo seguro dentro da margem mínima de 20%.";
    public const string ReturnableBlockedMessage =
        "Produto retornável sem suporte seguro de vasilhame em combos.";
    public const int RejectedHoldDays = 7;
    public const string TableName = "inventory_combo_campaigns";

    public static string SuggestionKey(int targetProductId, int anchorProductId) =>
        $"t{targetProductId}:a{anchorProductId}";

    public static string DefaultCommercialName(string targetName, string anchorName)
    {
        var t = (targetName ?? "").Trim();
        var a = (anchorName ?? "").Trim();
        if (t.Length == 0) t = "Produto";
        if (a.Length == 0) a = "Acompanhante";
        return $"COMBO GIRO — {t} + {a}";
    }

    public static string DefaultInternalCode(int targetProductId, int anchorProductId) =>
        $"CMB{targetProductId:D4}{anchorProductId:D4}";

    public static double MarginPercent(double price, double cost)
    {
        if (!IsFinite(price) || price <= 0 || !IsFinite(cost) || cost < 0)
            return double.NaN;
        return ((price - cost) / price) * 100.0;
    }

    public static bool HasKnownCost(double cost) =>
        IsFinite(cost) && cost > InventoryCommercialFactsEngine.MoneyEpsilon;

    public static string? ValidatePrice(double price, double cost)
    {
        if (!HasKnownCost(cost))
            return "Custo ausente. Nunca tratar custo ausente como zero.";
        if (!IsFinite(price) || price <= 0)
            return "Informe um preço de combo válido.";
        if (price + 0.0001 < cost)
            return "Nunca aprovar preço abaixo do custo.";
        var margin = MarginPercent(price, cost);
        if (!InventorySmartMargin.MeetsAbsoluteMinimum(margin))
            return NoSafePriceMessage;
        return null;
    }

    public static string? ValidatePeriod(DateOnly start, DateOnly end, DateOnly today)
    {
        if (end < start)
            return "A data final deve ser igual ou posterior à data inicial.";
        if (end < today)
            return "A vigência já está encerrada.";
        return null;
    }

    public static string? ValidateQuantity(double maxQty)
    {
        if (!IsFinite(maxQty) || maxQty < 0)
            return "Quantidade máxima inválida.";
        return null;
    }

    public static bool IsReturnable(ProductExtra? extra) =>
        extra?.VasilhameTipoId is > 0;

    public static bool CanRejectedReappear(
        DateOnly rejectedOn,
        string storedSignature,
        string currentSignature,
        DateOnly today)
    {
        if (!string.Equals(storedSignature ?? "", currentSignature ?? "", StringComparison.Ordinal))
            return true;
        return today >= rejectedOn.AddDays(RejectedHoldDays);
    }

    public static string Signature(
        int targetId,
        int anchorId,
        double price,
        double cost,
        InventoryComboPairEvidence evidence,
        double maxSafe)
    {
        return string.Join('|',
            targetId.ToString(),
            anchorId.ToString(),
            Math.Round(price, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Math.Round(cost, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            evidence.ToString(),
            Math.Round(maxSafe, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static InventoryComboLifecycleStatus EffectiveStatus(
        InventoryComboLifecycleStatus stored,
        DateOnly today,
        DateOnly start,
        DateOnly end,
        double soldQty,
        double maxQty)
    {
        if (stored == InventoryComboLifecycleStatus.Rejected)
            return InventoryComboLifecycleStatus.Rejected;
        if (stored == InventoryComboLifecycleStatus.Finished)
            return InventoryComboLifecycleStatus.Finished;
        if (stored == InventoryComboLifecycleStatus.Suggested)
            return InventoryComboLifecycleStatus.Suggested;
        if (end < today)
            return InventoryComboLifecycleStatus.Expired;
        if (maxQty > 0 && soldQty + 0.0001 >= maxQty)
            return InventoryComboLifecycleStatus.Finished;
        if (stored == InventoryComboLifecycleStatus.Paused)
            return InventoryComboLifecycleStatus.Paused;
        if (today < start)
            return InventoryComboLifecycleStatus.Paused;
        return InventoryComboLifecycleStatus.Active;
    }

    public static bool IsPdvSellable(InventoryComboLifecycleStatus effective) =>
        effective == InventoryComboLifecycleStatus.Active;

    public static string StatusLabel(InventoryComboLifecycleStatus status) =>
        status switch
        {
            InventoryComboLifecycleStatus.Suggested => "Sugerido",
            InventoryComboLifecycleStatus.Active => "Ativo",
            InventoryComboLifecycleStatus.Paused => "Pausado",
            InventoryComboLifecycleStatus.Finished => "Encerrado",
            InventoryComboLifecycleStatus.Expired => "Expirado",
            InventoryComboLifecycleStatus.Rejected => "Descartado",
            _ => status.ToString(),
        };

    public static string ParseStatus(string? raw)
    {
        return (raw ?? "").Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "paused" => "paused",
            "finished" => "finished",
            "expired" => "expired",
            "rejected" => "rejected",
            "suggested" => "suggested",
            _ => "paused",
        };
    }

    public static InventoryComboLifecycleStatus ToStatus(string? raw) =>
        ParseStatus(raw) switch
        {
            "active" => InventoryComboLifecycleStatus.Active,
            "paused" => InventoryComboLifecycleStatus.Paused,
            "finished" => InventoryComboLifecycleStatus.Finished,
            "expired" => InventoryComboLifecycleStatus.Expired,
            "rejected" => InventoryComboLifecycleStatus.Rejected,
            "suggested" => InventoryComboLifecycleStatus.Suggested,
            _ => InventoryComboLifecycleStatus.Paused,
        };

    public static string ToDb(InventoryComboLifecycleStatus status) =>
        status switch
        {
            InventoryComboLifecycleStatus.Active => "active",
            InventoryComboLifecycleStatus.Paused => "paused",
            InventoryComboLifecycleStatus.Finished => "finished",
            InventoryComboLifecycleStatus.Expired => "expired",
            InventoryComboLifecycleStatus.Rejected => "rejected",
            _ => "suggested",
        };

    static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
