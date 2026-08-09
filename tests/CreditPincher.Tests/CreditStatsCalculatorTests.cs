using CreditPincher.Core.Models;
using CreditPincher.Core.Services;
using Xunit;

namespace CreditPincher.Tests;

/// <summary>
/// Ports the plugin's calculator tests so the two implementations stay in agreement.
/// </summary>
public class CreditStatsCalculatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void CalculatesMonthToDateStats()
    {
        var entries = new[]
        {
            Entry("2026-07-01T09:00:00Z", 12.0),
            Entry("2026-07-02T10:00:00Z", 18.0),
            Entry("2026-07-02T15:30:00Z", 7.0),
        };

        var stats = CreditStatsCalculator.Calculate(
            entries,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 10),
            monthlyBudget: 310.0,
            zone: Utc);

        Assert.Equal(37.0, stats.TotalCredits, 4);
        Assert.Equal(10L, stats.DaysInRange);
        Assert.Equal(3, stats.EntryCount);
        Assert.Equal(2, stats.ActiveDays);
        Assert.Equal(3.7, stats.AverageCreditsPerDay, 4);
        Assert.Equal(18.5, stats.AverageCreditsPerActiveDay, 4);
        Assert.Equal(new DateOnly(2026, 7, 2), stats.BusiestDay);
        Assert.Equal(25.0, stats.BusiestDayCredits!.Value, 4);
        Assert.Equal(18.0, stats.HighestSingleEntry!.Value, 4);
        Assert.Equal(new DateOnly(2026, 7, 2), stats.LatestEntryDate);
    }

    [Fact]
    public void ProratesBudgetAcrossTheSelectedRange()
    {
        var stats = CreditStatsCalculator.Calculate(
            [Entry("2026-07-01T09:00:00Z", 20.0)],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 10),
            monthlyBudget: 310.0,
            zone: Utc);

        // 31 days in July, so a 10-day slice is worth 100 credits of the 310 budget.
        Assert.Equal(100.0, stats.ProratedBudgetForRange!.Value, 4);
        Assert.Equal(80.0, stats.RemainingBudgetForRange!.Value, 4);
        Assert.Equal(20.0, stats.BudgetUsedPercent!.Value, 4);
    }

    [Fact]
    public void ProjectsRunOutDayWhenPacingOverBudget()
    {
        var entries = new[]
        {
            Entry("2026-07-01T09:00:00Z", 50.0),
            Entry("2026-07-02T09:00:00Z", 50.0),
        };

        var stats = CreditStatsCalculator.Calculate(
            entries,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 2),
            monthlyBudget: 310.0,
            zone: Utc);

        // 50 credits/day against a 310 budget runs out on day 7.
        Assert.Equal(new YearMonth(2026, 7), stats.ProjectionMonth);
        Assert.Equal(1550.0, stats.ProjectedMonthTotal!.Value, 4);
        Assert.Equal(7, stats.ProjectedBudgetRunOutDay);
    }

    [Fact]
    public void SkipsProjectionWhenRangeSpansMultipleMonths()
    {
        var stats = CreditStatsCalculator.Calculate(
            [Entry("2026-07-31T09:00:00Z", 10.0)],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 8, 15),
            monthlyBudget: 310.0,
            zone: Utc);

        Assert.Null(stats.ProjectionMonth);
        Assert.Null(stats.ProjectedMonthTotal);
        Assert.Null(stats.ProjectedBudgetRunOutDay);
    }

    [Fact]
    public void ReturnsEmptyStatsWithoutEntries()
    {
        var stats = CreditStatsCalculator.Calculate(
            [],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            monthlyBudget: null,
            zone: Utc);

        Assert.Equal(0.0, stats.TotalCredits, 4);
        Assert.Equal(0, stats.ActiveDays);
        Assert.Null(stats.BusiestDay);
        Assert.Null(stats.HighestSingleEntry);
        Assert.Null(stats.ProratedBudgetForRange);
        Assert.Null(stats.BudgetUsedPercent);
    }

    [Fact]
    public void SwapsInvertedRanges()
    {
        var stats = CreditStatsCalculator.Calculate(
            [Entry("2026-07-05T09:00:00Z", 5.0)],
            new DateOnly(2026, 7, 10),
            new DateOnly(2026, 7, 1),
            monthlyBudget: null,
            zone: Utc);

        Assert.Equal(new DateOnly(2026, 7, 1), stats.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 10), stats.EndDate);
        Assert.Equal(5.0, stats.TotalCredits, 4);
    }

    [Fact]
    public void ZeroFillsTheDailySeriesForTheChart()
    {
        var stats = CreditStatsCalculator.Calculate(
            [Entry("2026-07-03T09:00:00Z", 8.0)],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 5),
            monthlyBudget: null,
            zone: Utc);

        Assert.Equal(5, stats.DailyUsage.Count);
        Assert.Equal(new DateOnly(2026, 7, 1), stats.DailyUsage[0].Date);
        Assert.Equal(0.0, stats.DailyUsage[0].Credits, 4);
        Assert.Equal(8.0, stats.DailyUsage[2].Credits, 4);
    }

    private static CreditUsageEntry Entry(string timestamp, double amount) =>
        new(DateTimeOffset.Parse(timestamp).ToUniversalTime(), amount);
}
