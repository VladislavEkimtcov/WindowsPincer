using CreditPincher.Core.Models;
using CreditPincher.Core.Services;
using Xunit;

namespace CreditPincher.Tests;

public class BudgetAlertEvaluatorTests
{
    private static readonly YearMonth July = new(2026, 7);

    [Fact]
    public void FiresOnceWhenAThresholdIsCrossed()
    {
        var settings = new AppSettings { BudgetNotificationThresholds = [80, 100] };
        var stats = StatsWith(totalCredits: 250.0, budget: 300.0);

        var first = BudgetAlertEvaluator.Evaluate(settings, stats, July);
        var second = BudgetAlertEvaluator.Evaluate(settings, stats, July);

        Assert.NotNull(first);
        Assert.Equal(80, first!.Value.ThresholdPercent);
        Assert.Null(second);
    }

    [Fact]
    public void ReportsOnlyTheHighestCrossedThreshold()
    {
        var settings = new AppSettings { BudgetNotificationThresholds = [50, 80, 100] };
        var stats = StatsWith(totalCredits: 330.0, budget: 300.0);

        var alert = BudgetAlertEvaluator.Evaluate(settings, stats, July);

        Assert.NotNull(alert);
        Assert.Equal(100, alert!.Value.ThresholdPercent);
        Assert.Equal(110.0, alert.Value.UsedPercent, 4);
    }

    [Fact]
    public void ResetsAtTheStartOfANewMonth()
    {
        var settings = new AppSettings { BudgetNotificationThresholds = [80] };
        var stats = StatsWith(totalCredits: 250.0, budget: 300.0);

        Assert.NotNull(BudgetAlertEvaluator.Evaluate(settings, stats, July));
        Assert.Null(BudgetAlertEvaluator.Evaluate(settings, stats, July));
        Assert.NotNull(BudgetAlertEvaluator.Evaluate(settings, stats, new YearMonth(2026, 8)));
    }

    [Fact]
    public void StaysQuietWithoutABudgetOrWhenDisabled()
    {
        var settings = new AppSettings { BudgetNotificationThresholds = [80] };

        Assert.Null(BudgetAlertEvaluator.Evaluate(settings, StatsWith(250.0, budget: null), July));

        settings.NotifyOnBudgetThresholds = false;
        Assert.Null(BudgetAlertEvaluator.Evaluate(settings, StatsWith(250.0, budget: 300.0), July));
    }

    private static UsageStats StatsWith(double totalCredits, double? budget) => new()
    {
        StartDate = July.AtDay(1),
        EndDate = July.AtDay(15),
        DaysInRange = 15,
        EntryCount = 1,
        ActiveDays = 1,
        TotalCredits = totalCredits,
        AverageCreditsPerDay = totalCredits / 15.0,
        AverageCreditsPerActiveDay = totalCredits,
        MonthlyBudget = budget,
        DailyUsage = [],
    };
}
