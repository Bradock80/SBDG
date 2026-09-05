using System.Globalization;
using SGDB.Domain.Commercial;
using SGDB.Domain.Common;
using SGDB.Models;
using SGDB.Utils;

namespace SGDB.Services;

/// <summary>
/// Configuração 71B-B6 da meta. Persistência só via B3. Sem SQL na UI e sem RPC.
/// </summary>
public static class CommercialGoalAdminService
{
    public static bool CanMutate() => AccessControl.CanAccessCommercialPolicy();

    public static bool StationAllowsWrite() => !StoreNetworkMode.IsClient;

    public static CommercialGoalAdminSnapshot LoadEditor(CommercialCompetence competence)
    {
        var defaults = CommercialGoalSettingsService.GetDefault();
        var monthly = CommercialGoalSettingsService.GetMonthlyOverride(competence);
        var resolution = CommercialGoalSettingsService.Resolve(competence);
        return ToSnapshot(competence, defaults, monthly, resolution);
    }

    public static CommercialGoalAdminResult TrySaveDefault(CommercialCompetence competence, string? input) =>
        TryWrite(competence, input, monthly: false);

    public static CommercialGoalAdminResult TrySaveOverride(CommercialCompetence competence, string? input) =>
        TryWrite(competence, input, monthly: true);

    public static CommercialGoalAdminResult TryClearDefault(
        CommercialCompetence competence,
        bool confirmed)
    {
        if (!confirmed)
            return Fail("", LoadEditor(competence));
        if (!CanMutate())
            return Fail("Seu usuário não tem permissão para alterar a meta comercial.", LoadEditor(competence));
        if (!StationAllowsWrite())
            return Fail(StoreNetworkMode.ClientBlockedModuleMessage, LoadEditor(competence));

        var before = CommercialGoalSettingsService.GetDefault();
        if (before.Status == CommercialGoalStoredSettingStatus.Missing)
            return Ok("Meta padrão não configurada.", LoadEditor(competence));

        CommercialGoalSettingsService.ClearDefault();
        return Ok("Meta padrão removida.", LoadEditor(competence));
    }

    public static CommercialGoalAdminResult TryClearOverride(
        CommercialCompetence competence,
        bool confirmed)
    {
        if (!confirmed)
            return Fail("", LoadEditor(competence));
        if (!CanMutate())
            return Fail("Seu usuário não tem permissão para alterar a meta comercial.", LoadEditor(competence));
        if (!StationAllowsWrite())
            return Fail(StoreNetworkMode.ClientBlockedModuleMessage, LoadEditor(competence));

        var before = CommercialGoalSettingsService.GetMonthlyOverride(competence);
        if (before.Status == CommercialGoalStoredSettingStatus.Missing)
            return Ok("Meta específica deste mês não configurada.", LoadEditor(competence));

        CommercialGoalSettingsService.ClearMonthlyOverride(competence);
        return Ok("Meta específica deste mês removida.", LoadEditor(competence));
    }

    public static bool TryParseMoney(string? text, out decimal amount, out string error)
    {
        amount = 0m;
        error = "Informe um valor maior que zero, com até duas casas decimais.";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Informe a meta. Para remover, use o botão Remover.";
            return false;
        }

        var normalized = NormalizeBr(text);
        if (normalized is null
            || !decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        var rounded = MonetaryRounding.RoundDecimal(parsed);
        if (!CommercialGoalSettingsService.IsValidGoalAmount(rounded))
            return false;

        amount = rounded;
        return true;
    }

    static CommercialGoalAdminResult TryWrite(
        CommercialCompetence competence,
        string? input,
        bool monthly)
    {
        if (!CanMutate())
            return Fail("Seu usuário não tem permissão para alterar a meta comercial.", LoadEditor(competence));
        if (!StationAllowsWrite())
            return Fail(StoreNetworkMode.ClientBlockedModuleMessage, LoadEditor(competence));
        if (!TryParseMoney(input, out var amount, out var parseError))
            return Fail(parseError, LoadEditor(competence));

        var saved = monthly
            ? CommercialGoalSettingsService.SetMonthlyOverride(competence, amount)
            : CommercialGoalSettingsService.SetDefault(amount);
        if (!saved.Written)
            return Fail("Não foi possível salvar a meta comercial.", LoadEditor(competence));

        var message = monthly
            ? $"Meta específica de {CommercialGoalUi.FormatCompetenceTitle(competence)} atualizada."
            : "Meta padrão atualizada.";
        return Ok(message, LoadEditor(competence));
    }

    static string? NormalizeBr(string text)
    {
        text = text.Trim()
            .Replace("R$", "", StringComparison.OrdinalIgnoreCase)
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

    static CommercialGoalAdminSnapshot ToSnapshot(
        CommercialCompetence competence,
        CommercialGoalStoredSetting defaults,
        CommercialGoalStoredSetting monthly,
        CommercialGoalSettingResolution resolution)
    {
        return new CommercialGoalAdminSnapshot
        {
            Competence = competence,
            CompetenceTitle = CommercialGoalUi.FormatCompetenceTitle(competence),
            OriginText = CommercialGoalPresentation.GoalOriginText(resolution.Source),
            DefaultEditorText = EditorText(defaults),
            MonthlyEditorText = EditorText(monthly),
            DefaultStatusText = StatusText(defaults, isMonthly: false),
            MonthlyStatusText = StatusText(monthly, isMonthly: true),
            HistoricalDefaultNote = CommercialGoalPresentation.LimitationHistoricalDefaultBody,
            HasDefault = defaults.Status == CommercialGoalStoredSettingStatus.Configured,
            HasMonthlyOverride = monthly.Status == CommercialGoalStoredSettingStatus.Configured,
            CanMutate = CanMutate(),
            StationAllowsWrite = StationAllowsWrite(),
        };
    }

    static string EditorText(CommercialGoalStoredSetting setting) =>
        setting.Status == CommercialGoalStoredSettingStatus.Configured && setting.GoalAmount is decimal amount
            ? amount.ToString("N2", ProductPriceHelper.Br)
            : setting.Status == CommercialGoalStoredSettingStatus.Invalid
                ? setting.RawValue ?? ""
                : "";

    static string StatusText(CommercialGoalStoredSetting setting, bool isMonthly) =>
        setting.Status switch
        {
            CommercialGoalStoredSettingStatus.Configured when setting.GoalAmount is decimal amount =>
                isMonthly
                    ? $"Configurada: {CommercialGoalPresentation.FormatMoney(amount)}"
                    : $"Padrão vigente: {CommercialGoalPresentation.FormatMoney(amount)}",
            CommercialGoalStoredSettingStatus.Invalid =>
                isMonthly
                    ? CommercialGoalPresentation.OriginInvalidOverride
                    : CommercialGoalPresentation.OriginInvalidDefault,
            _ => isMonthly
                ? "Nenhuma meta específica neste mês."
                : "Meta padrão não configurada.",
        };

    static CommercialGoalAdminResult Ok(string message, CommercialGoalAdminSnapshot snapshot) =>
        new() { Succeeded = true, Message = message, Snapshot = snapshot };

    static CommercialGoalAdminResult Fail(string message, CommercialGoalAdminSnapshot snapshot) =>
        new() { Succeeded = false, Message = message, Snapshot = snapshot };
}
