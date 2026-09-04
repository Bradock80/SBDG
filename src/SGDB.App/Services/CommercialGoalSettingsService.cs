using System.Globalization;
using System.Text.RegularExpressions;
using SGDB.Domain.Commercial;
using SGDB.Domain.Common;

namespace SGDB.Services;

/// <summary>
/// Persistência 71B-B3 da meta de lucro bruto: default + override YYYY-MM em app_settings.
/// Culture-invariant. Sem UI, DRE, CMV ou Rede Loja.
/// </summary>
public static class CommercialGoalSettingsService
{
    public const int ExpectedSingleKeyQueryCount = 1;
    public const int ExpectedResolveMaxQueryCount = 2;

    static readonly Regex InvariantMoneyPattern = new(
        @"^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string DefaultKey => CommercialGoalSettingKeys.Default;

    public static string MonthlyKey(CommercialCompetence competence) =>
        CommercialGoalSettingKeys.Monthly(competence);

    public static CommercialGoalStoredSetting GetDefault() =>
        LoadKey(DefaultKey);

    public static CommercialGoalStoredSetting GetMonthlyOverride(CommercialCompetence competence) =>
        LoadKey(MonthlyKey(competence));

    public static CommercialGoalSettingResolution Resolve(CommercialCompetence competence)
    {
        var monthly = GetMonthlyOverride(competence);
        if (monthly.Status == CommercialGoalStoredSettingStatus.Configured)
        {
            return new CommercialGoalSettingResolution
            {
                Competence = competence,
                Source = CommercialGoalSettingSource.MonthlyOverride,
                GoalAmount = monthly.GoalAmount,
                HasValidGoal = true,
                MonthlyOverride = monthly,
                DefaultSetting = null,
                QueryCount = monthly.QueryCount,
            };
        }

        if (monthly.Status == CommercialGoalStoredSettingStatus.Invalid)
        {
            return new CommercialGoalSettingResolution
            {
                Competence = competence,
                Source = CommercialGoalSettingSource.InvalidMonthlyOverride,
                GoalAmount = null,
                HasValidGoal = false,
                MonthlyOverride = monthly,
                DefaultSetting = null,
                QueryCount = monthly.QueryCount,
            };
        }

        var defaults = GetDefault();
        if (defaults.Status == CommercialGoalStoredSettingStatus.Configured)
        {
            return new CommercialGoalSettingResolution
            {
                Competence = competence,
                Source = CommercialGoalSettingSource.Default,
                GoalAmount = defaults.GoalAmount,
                HasValidGoal = true,
                MonthlyOverride = monthly,
                DefaultSetting = defaults,
                QueryCount = monthly.QueryCount + defaults.QueryCount,
            };
        }

        if (defaults.Status == CommercialGoalStoredSettingStatus.Invalid)
        {
            return new CommercialGoalSettingResolution
            {
                Competence = competence,
                Source = CommercialGoalSettingSource.InvalidDefault,
                GoalAmount = null,
                HasValidGoal = false,
                MonthlyOverride = monthly,
                DefaultSetting = defaults,
                QueryCount = monthly.QueryCount + defaults.QueryCount,
            };
        }

        return new CommercialGoalSettingResolution
        {
            Competence = competence,
            Source = CommercialGoalSettingSource.None,
            GoalAmount = null,
            HasValidGoal = false,
            MonthlyOverride = monthly,
            DefaultSetting = defaults,
            QueryCount = monthly.QueryCount + defaults.QueryCount,
        };
    }

    public static CommercialGoalSettingSaveResult SetDefault(decimal goal) =>
        TryWrite(DefaultKey, goal);

    public static CommercialGoalStoredSetting ClearDefault()
    {
        AppSettingsService.DeleteSetting(DefaultKey);
        return Missing(ExpectedSingleKeyQueryCount);
    }

    public static CommercialGoalSettingSaveResult SetMonthlyOverride(
        CommercialCompetence competence,
        decimal goal) =>
        TryWrite(MonthlyKey(competence), goal);

    public static CommercialGoalStoredSetting ClearMonthlyOverride(CommercialCompetence competence)
    {
        AppSettingsService.DeleteSetting(MonthlyKey(competence));
        return Missing(ExpectedSingleKeyQueryCount);
    }

    public static bool IsValidGoalAmount(decimal value) =>
        value > 0m;

    static CommercialGoalSettingSaveResult TryWrite(string key, decimal value)
    {
        if (!IsValidGoalAmount(value))
            return Reject(value);

        var rounded = MonetaryRounding.RoundDecimal(value);
        if (!IsValidGoalAmount(rounded))
            return Reject(value);

        var serialized = Serialize(rounded);
        AppSettingsService.SetSetting(key, serialized);
        return new CommercialGoalSettingSaveResult
        {
            Written = true,
            Setting = Configured(rounded, serialized, ExpectedSingleKeyQueryCount),
        };
    }

    static CommercialGoalStoredSetting LoadKey(string key)
    {
        var raw = AppSettingsService.GetSetting(key);
        return ClassifyRaw(raw, rowExists: raw is not null, ExpectedSingleKeyQueryCount);
    }

    static CommercialGoalStoredSetting ClassifyRaw(string? raw, bool rowExists, int queryCount)
    {
        if (!rowExists)
            return Missing(queryCount);

        if (string.IsNullOrEmpty(raw))
            return Invalid(raw ?? "", CommercialGoalStoredSettingReason.EmptyValue, queryCount);

        if (string.IsNullOrWhiteSpace(raw) || !InvariantMoneyPattern.IsMatch(raw))
            return Invalid(raw, CommercialGoalStoredSettingReason.NonInvariantFormat, queryCount);

        if (!decimal.TryParse(
                raw,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
            return Invalid(raw, CommercialGoalStoredSettingReason.NonInvariantFormat, queryCount);

        if (!IsValidGoalAmount(parsed))
            return Invalid(raw, CommercialGoalStoredSettingReason.NotPositive, queryCount);

        var amount = MonetaryRounding.RoundDecimal(parsed);
        if (amount != parsed)
            return Invalid(raw, CommercialGoalStoredSettingReason.NonInvariantFormat, queryCount);

        if (!IsValidGoalAmount(amount))
            return Invalid(raw, CommercialGoalStoredSettingReason.NotPositive, queryCount);

        return Configured(amount, raw, queryCount);
    }

    static string Serialize(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    static CommercialGoalStoredSetting Missing(int queryCount) =>
        new()
        {
            Status = CommercialGoalStoredSettingStatus.Missing,
            Reasons = [CommercialGoalStoredSettingReason.Missing],
            QueryCount = queryCount,
        };

    static CommercialGoalStoredSetting Configured(decimal amount, string raw, int queryCount) =>
        new()
        {
            Status = CommercialGoalStoredSettingStatus.Configured,
            GoalAmount = amount,
            RawValue = raw,
            QueryCount = queryCount,
        };

    static CommercialGoalStoredSetting Invalid(
        string raw,
        CommercialGoalStoredSettingReason reason,
        int queryCount) =>
        new()
        {
            Status = CommercialGoalStoredSettingStatus.Invalid,
            RawValue = raw,
            Reasons = [reason, CommercialGoalStoredSettingReason.Invalid],
            QueryCount = queryCount,
        };

    static CommercialGoalSettingSaveResult Reject(decimal value) =>
        new()
        {
            Written = false,
            Setting = Invalid(
                Serialize(MonetaryRounding.RoundDecimal(value)),
                CommercialGoalStoredSettingReason.NotPositive,
                queryCount: 0),
        };
}
