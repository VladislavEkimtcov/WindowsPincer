using System.Globalization;
using CreditPincher.Core.Models;

namespace CreditPincher.Core.Services;

/// <summary>
/// Renders numbers and the derived budget sentences. Lives in Core (rather than in a
/// view) so the dashboard, the tray tooltip, and the balloon notifications all phrase
/// things identically.
/// </summary>
public sealed class CreditFormatter
{
    public const string NotAvailable = "—";

    private const string NumberPattern = "#,##0.00";

    public CreditFormatter(bool showInDollars = false, double creditsPerDollar = 100.0)
    {
        ShowInDollars = showInDollars;
        CreditsPerDollar = double.IsFinite(creditsPerDollar) && creditsPerDollar > 0 ? creditsPerDollar : 100.0;
    }

    public bool ShowInDollars { get; }

    public double CreditsPerDollar { get; }

    public string Credits(double? value)
    {
        if (value is not { } amount || !double.IsFinite(amount))
        {
            return NotAvailable;
        }

        if (!ShowInDollars)
        {
            return $"{amount.ToString(NumberPattern, CultureInfo.CurrentCulture)} credits";
        }

        var sign = amount < 0 ? "-" : string.Empty;
        var dollars = Math.Abs(amount) / CreditsPerDollar;
        return $"{sign}${dollars.ToString(NumberPattern, CultureInfo.CurrentCulture)}";
    }

    public string Percent(double? value) => value is { } percent && double.IsFinite(percent)
        ? $"{percent.ToString(NumberPattern, CultureInfo.CurrentCulture)}%"
        : NotAvailable;

    public string Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? NotAvailable;

    public string Range(UsageStats stats) =>
        $"{Date(stats.StartDate)} → {Date(stats.EndDate)} ({stats.DaysInRange} day(s))";

    public string BusiestDay(UsageStats stats) => stats.BusiestDay is { } day
        ? $"{Date(day)} ({Credits(stats.BusiestDayCredits)})"
        : NotAvailable;

    public string BudgetInRange(UsageStats stats) =>
        stats.ProratedBudgetForRange is { } prorated && stats.BudgetUsedPercent is { } percent
            ? $"{Credits(prorated)} ({Percent(percent)} used)"
            : NotAvailable;

    public string ProjectedMonthTotal(UsageStats stats) => stats.ProjectedMonthTotal is { } projected
        ? Credits(projected)
        : "Select a single-month range for this stat.";

    /// <summary>
    /// The "budget runway" sentence: when the budget is projected to run out, or how
    /// far under/over the month is trending.
    /// </summary>
    public string Runway(UsageStats stats, DateOnly? today = null)
    {
        if (stats.MonthlyBudget is null)
        {
            return "Set a monthly budget to unlock budget pacing stats.";
        }

        if (stats.ProjectionMonth is not { } month)
        {
            return "Select a single-month range for this stat.";
        }

        if (stats.ProjectedBudgetRunOutDay is { } runOutDay)
        {
            var runOutDate = month.AtDay(runOutDay);
            var reference = today ?? DateOnly.FromDateTime(DateTime.Now);

            if (runOutDate < reference)
            {
                var overBudget = stats.ProjectedMonthRemaining is { } remaining ? Math.Abs(remaining) : (double?)null;
                return $"Projected to finish the month {Credits(overBudget)} over budget";
            }

            return $"Projected to run out on {Date(runOutDate)}";
        }

        return stats.ProjectedMonthRemaining is { } underBudget
            ? $"On pace to finish {Credits(underBudget)} under budget"
            : NotAvailable;
    }

    /// <summary>Short line for the tray tooltip (Windows caps this at 127 characters).</summary>
    public string TrayTooltip(UsageStats monthToDate)
    {
        var headline = $"CreditPincher — {Credits(monthToDate.TotalCredits)} this month";

        if (monthToDate.MonthlyBudget is { } budget && budget > 0)
        {
            var used = monthToDate.TotalCredits / budget * 100.0;
            headline += $" ({used.ToString("0", CultureInfo.CurrentCulture)}% of budget)";
        }

        return headline.Length <= 127 ? headline : headline[..127];
    }
}
