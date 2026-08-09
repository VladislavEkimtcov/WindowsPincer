namespace CreditPincher.Core.Models;

/// <summary>
/// Everything the dashboard shows for one date range. Nullable members mean
/// "not computable" (usually because no budget is set, or the range spans
/// more than one calendar month).
/// </summary>
public sealed record UsageStats
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required long DaysInRange { get; init; }
    public required int EntryCount { get; init; }
    public required int ActiveDays { get; init; }
    public required double TotalCredits { get; init; }
    public required double AverageCreditsPerDay { get; init; }
    public required double AverageCreditsPerActiveDay { get; init; }

    public DateOnly? BusiestDay { get; init; }
    public double? BusiestDayCredits { get; init; }
    public double? HighestSingleEntry { get; init; }
    public DateOnly? LatestEntryDate { get; init; }

    public double? MonthlyBudget { get; init; }
    public double? ProratedBudgetForRange { get; init; }
    public double? RemainingBudgetForRange { get; init; }
    public double? BudgetUsedPercent { get; init; }

    public double? ProjectedMonthTotal { get; init; }
    public double? ProjectedMonthRemaining { get; init; }
    public int? ProjectedBudgetRunOutDay { get; init; }
    public YearMonth? ProjectionMonth { get; init; }

    /// <summary>Credits per day, keyed by calendar day, for every day in the range (zero-filled).</summary>
    public required IReadOnlyList<DailyUsage> DailyUsage { get; init; }
}

/// <summary>One bar of the usage chart.</summary>
public readonly record struct DailyUsage(DateOnly Date, double Credits);
