using System.Globalization;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// 70F-B3D — operação administrativa da margem mínima global.
/// Persistência via B3B, resolução via B3C. Sem SQL na UI e sem RPC.
/// </summary>
public static class InventoryCommercialMarginAdminService
{
    public const string ModuleId = "politica_comercial";
    public const string AuditEntity = "politica_comercial";
    public const string AuditEntityId = "global";
    public const string Origin = "sistema.politica_comercial";
    public const string ClientBlockedMessage =
        "Esta configuração deve ser alterada no computador servidor da Rede Loja.";

    public static bool CanMutate() => AccessControl.CanAccessCommercialPolicy();

    public static bool StationAllowsWrite() => !StoreNetworkMode.IsClient;

    public static InventoryCommercialMarginAdminSnapshot LoadSnapshot()
    {
        var setting = InventoryCommercialMarginSettingsService.Load();
        var resolution = InventoryCommercialMarginPolicyResolver.Resolve(setting);
        return ToSnapshot(setting, resolution);
    }

    public static InventoryCommercialMarginAdminResult TrySave(string? input)
    {
        if (!CanMutate())
            return Fail("Seu usuário não tem permissão para alterar a política comercial.", LoadSnapshot());
        if (!StationAllowsWrite())
            return Fail(ClientBlockedMessage, LoadSnapshot());
        if (!TryParsePercent(input, out var percent, out var parseError))
            return Fail(parseError, LoadSnapshot());

        var before = InventoryCommercialMarginSettingsService.Load();
        var saved = InventoryCommercialMarginSettingsService.Save(percent);
        if (!saved.Written)
            return Fail("Não foi possível salvar a política comercial.", LoadSnapshot());

        AuditChange("salvar", before, saved.Setting);
        return Ok("Política comercial atualizada.", LoadSnapshot(), audited: true);
    }

    public static InventoryCommercialMarginAdminResult TryClear(bool confirmed)
    {
        if (!confirmed)
            return Fail("", LoadSnapshot());
        if (!CanMutate())
            return Fail("Seu usuário não tem permissão para alterar a política comercial.", LoadSnapshot());
        if (!StationAllowsWrite())
            return Fail(ClientBlockedMessage, LoadSnapshot());

        var before = InventoryCommercialMarginSettingsService.Load();
        if (before.Status == InventoryCommercialMarginSettingStatus.Missing)
            return Ok("Margem mínima não configurada.", LoadSnapshot(), audited: false);

        InventoryCommercialMarginSettingsService.Clear();
        AuditChange("remover", before, InventoryCommercialMarginSettingsService.Load());
        return Ok("Margem mínima não configurada.", LoadSnapshot(), audited: true);
    }

    public static bool TryParsePercent(string? text, out decimal percent, out string error)
    {
        percent = 0m;
        error = "Informe uma margem entre 0% e menos de 100%.";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Informe a margem mínima. Deixe de salvar para manter o estado atual, ou remova a configuração.";
            return false;
        }

        var normalized = NormalizeBr(text);
        if (normalized is null
            || !decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out percent))
        {
            return false;
        }

        if (!InventoryCommercialMarginSettingsService.IsValidPercent(percent))
            return false;

        return true;
    }

    static string? NormalizeBr(string text)
    {
        text = text.Trim()
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("\u00A0", "")
            .Replace(" ", "");
        if (text.Length == 0)
            return null;

        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            return lastComma > lastDot
                ? text.Replace(".", "").Replace(',', '.')
                : text.Replace(",", "");
        }

        if (lastComma >= 0)
            return text.Replace(',', '.');
        return text;
    }

    static InventoryCommercialMarginAdminSnapshot ToSnapshot(
        InventoryCommercialMarginSetting setting,
        InventoryCommercialMarginPolicyResolution resolution)
    {
        var editor = resolution.Status switch
        {
            InventoryCommercialMarginPolicyResolutionStatus.Available
                when resolution.EffectiveMinimumGrossMarginPercent is decimal value =>
                FormatPercent(value),
            InventoryCommercialMarginPolicyResolutionStatus.Invalid =>
                setting.RawValue ?? "",
            _ => "",
        };

        var statusText = resolution.Status switch
        {
            InventoryCommercialMarginPolicyResolutionStatus.Available
                when resolution.EffectiveMinimumGrossMarginPercent is decimal value =>
                $"Política vigente: {FormatPercent(value)}%",
            InventoryCommercialMarginPolicyResolutionStatus.Invalid =>
                "A configuração armazenada é inválida.",
            _ => "Margem mínima não configurada.",
        };

        return new InventoryCommercialMarginAdminSnapshot
        {
            Status = resolution.Status,
            EffectivePercent = resolution.EffectiveMinimumGrossMarginPercent,
            RawValue = setting.RawValue,
            StatusText = statusText,
            EditorText = editor,
            CanMutate = CanMutate(),
            StationAllowsWrite = StationAllowsWrite(),
            Reasons = resolution.Reasons,
        };
    }

    static string FormatPercent(decimal value) =>
        value.ToString("0.####", ProductPriceHelper.Br);

    static void AuditChange(
        string operation,
        InventoryCommercialMarginSetting before,
        InventoryCommercialMarginSetting after)
    {
        var previousPercent = before.Status == InventoryCommercialMarginSettingStatus.Configured
            ? before.MinimumGrossMarginPercent
            : null;
        var newPercent = after.Status == InventoryCommercialMarginSettingStatus.Configured
            ? after.MinimumGrossMarginPercent
            : null;
        var summary = operation == "remover"
            ? "Margem mínima global removida"
            : $"Margem mínima global: {FormatPercent(newPercent ?? 0m)}%";

        AuditService.LogJson(
            "alterar",
            AuditEntity,
            AuditEntityId,
            AuditPayloadBuilder.CommercialPolicyChange(
                operation,
                before.Status.ToString(),
                previousPercent,
                before.RawValue,
                newPercent,
                after.RawValue),
            summary);
    }

    static InventoryCommercialMarginAdminResult Ok(
        string message,
        InventoryCommercialMarginAdminSnapshot snapshot,
        bool audited) =>
        new()
        {
            Succeeded = true,
            Audited = audited,
            Message = message,
            Snapshot = snapshot,
        };

    static InventoryCommercialMarginAdminResult Fail(
        string message,
        InventoryCommercialMarginAdminSnapshot snapshot) =>
        new()
        {
            Succeeded = false,
            Audited = false,
            Message = message,
            Snapshot = snapshot,
        };
}
