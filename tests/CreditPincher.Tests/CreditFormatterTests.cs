using System.Globalization;
using CreditPincher.Core.Models;
using CreditPincher.Core.Services;
using Xunit;

namespace CreditPincher.Tests;

public class CreditFormatterTests
{
    public CreditFormatterTests()
    {
        // Pin the culture so the "#,##0.00" expectations below are stable on any machine.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void FormatsCreditsAndDollars()
    {
        var credits = new CreditFormatter(showInDollars: false);
        var dollars = new CreditFormatter(showInDollars: true, creditsPerDollar: 100.0);

        Assert.Equal("1,234.50 credits", credits.Credits(1234.5));
        Assert.Equal("$12.35", dollars.Credits(1234.5));
        Assert.Equal("-$12.35", dollars.Credits(-1234.5));
        Assert.Equal(CreditFormatter.NotAvailable, credits.Credits(null));
    }

    [Fact]
    public void HonoursACustomCreditRate()
    {
        var formatter = new CreditFormatter(showInDollars: true, creditsPerDollar: 250.0);

        Assert.Equal("$4.00", formatter.Credits(1000.0));
    }

    [Fact]
    public void FallsBackToTheDefaultRateWhenMisconfigured()
    {
        var formatter = new CreditFormatter(showInDollars: true, creditsPerDollar: 0);

        Assert.Equal(100.0, formatter.CreditsPerDollar, 4);
    }

    [Fact]
    public void DescribesAnOverBudgetProjection()
    {
        var formatter = new CreditFormatter();
        var stats = CreditStatsCalculator.Calculate(
            [
                new CreditUsageEntry(DateTimeOffset.Parse("2026-07-01T09:00:00Z"), 50.0),
                new CreditUsageEntry(DateTimeOffset.Parse("2026-07-02T09:00:00Z"), 50.0),
            ],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 2),
            monthlyBudget: 310.0,
            zone: TimeZoneInfo.Utc);

        // Reference "today" is day 2, so day 7 is still ahead of us.
        Assert.Equal("Projected to run out on 2026-07-07", formatter.Runway(stats, new DateOnly(2026, 7, 2)));

        // Same numbers viewed later in the month: the run-out day is already behind us.
        Assert.StartsWith("Projected to finish the month", formatter.Runway(stats, new DateOnly(2026, 7, 20)));
    }

    [Fact]
    public void AsksForABudgetWhenNoneIsSet()
    {
        var formatter = new CreditFormatter();
        var stats = CreditStatsCalculator.Calculate(
            [],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            monthlyBudget: null,
            zone: TimeZoneInfo.Utc);

        Assert.Contains("Set a monthly budget", formatter.Runway(stats));
    }

    [Fact]
    public void KeepsTheTrayTooltipWithinTheWindowsLimit()
    {
        var formatter = new CreditFormatter();
        var stats = CreditStatsCalculator.Calculate(
            [new CreditUsageEntry(DateTimeOffset.Parse("2026-07-01T09:00:00Z"), 123456789.0)],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            monthlyBudget: 310.0,
            zone: TimeZoneInfo.Utc);

        var tooltip = formatter.TrayTooltip(stats);

        Assert.True(tooltip.Length <= 127);
        Assert.Contains("CreditPincher", tooltip);
    }
}
