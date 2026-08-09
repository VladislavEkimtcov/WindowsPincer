using System;
using System.Globalization;
using CreditPincher.Core.Compat;
using CreditPincher.Core.Models;

namespace CreditPincher.Core.Services
{
    /// <summary>
    /// Renders numbers and the derived budget sentences. Lives in Core (rather than in a
    /// view) so the dashboard, the tray tooltip, and the balloon notifications all phrase
    /// things identically.
    /// </summary>
    public sealed class CreditFormatter
    {
        public const string NotAvailable = "—";

        private const string NumberPattern = "#,##0.00";

        public CreditFormatter()
            : this(false, 100.0)
        {
        }

        public CreditFormatter(bool showInDollars)
            : this(showInDollars, 100.0)
        {
        }

        public CreditFormatter(bool showInDollars, double creditsPerDollar)
        {
            ShowInDollars = showInDollars;
            CreditsPerDollar = MathEx.IsFinite(creditsPerDollar) && creditsPerDollar > 0 ? creditsPerDollar : 100.0;
        }

        public bool ShowInDollars { get; private set; }

        public double CreditsPerDollar { get; private set; }

        public string Credits(double? value)
        {
            if (!value.HasValue || !MathEx.IsFinite(value.Value))
            {
                return NotAvailable;
            }

            var amount = value.Value;

            if (!ShowInDollars)
            {
                return amount.ToString(NumberPattern, CultureInfo.CurrentCulture) + " credits";
            }

            var sign = amount < 0 ? "-" : string.Empty;
            var dollars = Math.Abs(amount) / CreditsPerDollar;
            return sign + "$" + dollars.ToString(NumberPattern, CultureInfo.CurrentCulture);
        }

        public string Percent(double? value)
        {
            return value.HasValue && MathEx.IsFinite(value.Value)
                ? value.Value.ToString(NumberPattern, CultureInfo.CurrentCulture) + "%"
                : NotAvailable;
        }

        public string Date(DateOnly? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : NotAvailable;
        }

        public string Range(UsageStats stats)
        {
            return Date(stats.StartDate) + " → " + Date(stats.EndDate) +
                   " (" + stats.DaysInRange.ToString(CultureInfo.CurrentCulture) + " day(s))";
        }

        public string BusiestDay(UsageStats stats)
        {
            return stats.BusiestDay.HasValue
                ? Date(stats.BusiestDay.Value) + " (" + Credits(stats.BusiestDayCredits) + ")"
                : NotAvailable;
        }

        public string BudgetInRange(UsageStats stats)
        {
            return stats.ProratedBudgetForRange.HasValue && stats.BudgetUsedPercent.HasValue
                ? Credits(stats.ProratedBudgetForRange.Value) + " (" + Percent(stats.BudgetUsedPercent.Value) + " used)"
                : NotAvailable;
        }

        public string ProjectedMonthTotal(UsageStats stats)
        {
            return stats.ProjectedMonthTotal.HasValue
                ? Credits(stats.ProjectedMonthTotal.Value)
                : "Select a single-month range for this stat.";
        }

        public string Runway(UsageStats stats)
        {
            return Runway(stats, null);
        }

        /// <summary>
        /// The "budget runway" sentence: when the budget is projected to run out, or how
        /// far under/over the month is trending.
        /// </summary>
        public string Runway(UsageStats stats, DateOnly? today)
        {
            if (!stats.MonthlyBudget.HasValue)
            {
                return "Set a monthly budget to unlock budget pacing stats.";
            }

            if (!stats.ProjectionMonth.HasValue)
            {
                return "Select a single-month range for this stat.";
            }

            var month = stats.ProjectionMonth.Value;

            if (stats.ProjectedBudgetRunOutDay.HasValue)
            {
                var runOutDate = month.AtDay(stats.ProjectedBudgetRunOutDay.Value);
                var reference = today.HasValue ? today.Value : DateOnly.FromDateTime(DateTime.Now);

                if (runOutDate < reference)
                {
                    double? overBudget = stats.ProjectedMonthRemaining.HasValue
                        ? Math.Abs(stats.ProjectedMonthRemaining.Value)
                        : (double?)null;
                    return "Projected to finish the month " + Credits(overBudget) + " over budget";
                }

                return "Projected to run out on " + Date(runOutDate);
            }

            return stats.ProjectedMonthRemaining.HasValue
                ? "On pace to finish " + Credits(stats.ProjectedMonthRemaining.Value) + " under budget"
                : NotAvailable;
        }

        /// <summary>Short line for the tray tooltip (Windows caps this at 127 characters).</summary>
        public string TrayTooltip(UsageStats monthToDate)
        {
            var headline = "CreditPincher — " + Credits(monthToDate.TotalCredits) + " this month";

            if (monthToDate.MonthlyBudget.HasValue && monthToDate.MonthlyBudget.Value > 0)
            {
                var used = monthToDate.TotalCredits / monthToDate.MonthlyBudget.Value * 100.0;
                headline += " (" + used.ToString("0", CultureInfo.CurrentCulture) + "% of budget)";
            }

            return headline.Length <= 127 ? headline : headline.Substring(0, 127);
        }
    }
}
