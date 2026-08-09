using System.Globalization;
using CreditPincher.Core.Models;

namespace CreditPincher.Core.Services;

/// <summary>
/// Decides whether a tray notification is due for the current month's budget.
///
/// Each threshold fires at most once per calendar month; the highest crossed one wins,
/// so a machine that was switched off through 80% and comes back at 105% gets a single
/// "100%" alert rather than a burst of them.
/// </summary>
public static class BudgetAlertEvaluator
{
    public readonly record struct Alert(int ThresholdPercent, double UsedPercent, double MonthlyBudget, double TotalCredits);

    /// <summary>
    /// Returns the alert to show, or null when nothing is due. When an alert is
    /// returned, <paramref name="settings"/> has been updated to record it and should
    /// be persisted by the caller.
    /// </summary>
    public static Alert? Evaluate(AppSettings settings, UsageStats monthToDate, YearMonth month)
    {
        if (!settings.NotifyOnBudgetThresholds)
        {
            return null;
        }

        if (monthToDate.MonthlyBudget is not { } budget || budget <= 0 || !double.IsFinite(budget))
        {
            return null;
        }

        var monthKey = MonthKey(month);
        if (!string.Equals(settings.LastNotifiedMonth, monthKey, StringComparison.Ordinal))
        {
            settings.LastNotifiedMonth = monthKey;
            settings.LastNotifiedThreshold = 0;
        }

        var usedPercent = monthToDate.TotalCredits / budget * 100.0;

        var crossed = settings.BudgetNotificationThresholds
            .Where(threshold => threshold > settings.LastNotifiedThreshold && usedPercent >= threshold)
            .DefaultIfEmpty(0)
            .Max();

        if (crossed <= 0)
        {
            return null;
        }

        settings.LastNotifiedThreshold = crossed;
        return new Alert(crossed, usedPercent, budget, monthToDate.TotalCredits);
    }

    public static string MonthKey(YearMonth month) =>
        string.Create(CultureInfo.InvariantCulture, $"{month.Year:D4}-{month.Month:D2}");
}
