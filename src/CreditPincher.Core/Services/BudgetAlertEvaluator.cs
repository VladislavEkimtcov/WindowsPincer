using System;
using System.Globalization;
using System.Linq;
using CreditPincher.Core.Compat;
using CreditPincher.Core.Models;

namespace CreditPincher.Core.Services
{
    /// <summary>
    /// Decides whether a tray notification is due for the current month's budget.
    ///
    /// Each threshold fires at most once per calendar month; the highest crossed one wins,
    /// so a machine that was switched off through 80% and comes back at 105% gets a single
    /// "100%" alert rather than a burst of them.
    /// </summary>
    public static class BudgetAlertEvaluator
    {
        public struct Alert
        {
            private readonly int _thresholdPercent;
            private readonly double _usedPercent;
            private readonly double _monthlyBudget;
            private readonly double _totalCredits;

            public Alert(int thresholdPercent, double usedPercent, double monthlyBudget, double totalCredits)
            {
                _thresholdPercent = thresholdPercent;
                _usedPercent = usedPercent;
                _monthlyBudget = monthlyBudget;
                _totalCredits = totalCredits;
            }

            public int ThresholdPercent { get { return _thresholdPercent; } }

            public double UsedPercent { get { return _usedPercent; } }

            public double MonthlyBudget { get { return _monthlyBudget; } }

            public double TotalCredits { get { return _totalCredits; } }
        }

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

            if (!monthToDate.MonthlyBudget.HasValue)
            {
                return null;
            }

            var budget = monthToDate.MonthlyBudget.Value;
            if (budget <= 0 || !MathEx.IsFinite(budget))
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

        public static string MonthKey(YearMonth month)
        {
            return month.Year.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                   month.Month.ToString("D2", CultureInfo.InvariantCulture);
        }
    }
}
