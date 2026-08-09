using System;
using System.Collections.Generic;
using System.Linq;
using CreditPincher.Core.Compat;
using CreditPincher.Core.Models;

namespace CreditPincher.Core.Services
{
    /// <summary>
    /// Turns raw entries into the numbers shown on the dashboard and in the tray tooltip.
    /// A direct port of the plugin's calculator, plus the zero-filled daily series the
    /// chart needs (the plugin built that separately in its panel).
    /// </summary>
    public static class CreditStatsCalculator
    {
        public static UsageStats Calculate(
            IEnumerable<CreditUsageEntry> entries,
            DateOnly startDate,
            DateOnly endDate,
            double? monthlyBudget)
        {
            return Calculate(entries, startDate, endDate, monthlyBudget, null);
        }

        public static UsageStats Calculate(
            IEnumerable<CreditUsageEntry> entries,
            DateOnly startDate,
            DateOnly endDate,
            double? monthlyBudget,
            TimeZoneInfo zone)
        {
            if (zone == null)
            {
                zone = TimeZoneInfo.Local;
            }

            var normalizedStart = startDate <= endDate ? startDate : endDate;
            var normalizedEnd = startDate <= endDate ? endDate : startDate;

            var filtered = entries
                .Select(entry => new { Entry = entry, Day = entry.LocalDate(zone) })
                .Where(pair => pair.Day >= normalizedStart && pair.Day <= normalizedEnd)
                .ToList();

            var creditsByDay = filtered
                .GroupBy(pair => pair.Day)
                .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Entry.Amount));

            var totalCredits = filtered.Sum(pair => pair.Entry.Amount);
            var daysInRange = normalizedEnd.DayNumber - normalizedStart.DayNumber + 1;
            var activeDays = creditsByDay.Count;
            var averageCreditsPerDay = daysInRange > 0 ? totalCredits / daysInRange : 0.0;
            var averageCreditsPerActiveDay = activeDays > 0 ? totalCredits / activeDays : 0.0;

            KeyValuePair<DateOnly, double>? busiestDay = creditsByDay.Count == 0
                ? (KeyValuePair<DateOnly, double>?)null
                : creditsByDay.MaxBy(pair => pair.Value);

            double? proratedBudget = monthlyBudget.HasValue
                ? CalculateProratedBudget(normalizedStart, normalizedEnd, monthlyBudget.Value)
                : (double?)null;

            double? budgetUsedPercent = proratedBudget.HasValue && proratedBudget.Value > 0.0
                ? totalCredits / proratedBudget.Value * 100.0
                : (double?)null;

            double? projectedMonthTotal = null;
            double? projectedMonthRemaining = null;
            int? projectedBudgetRunOutDay = null;
            YearMonth? projectionMonth = null;

            var isSingleMonth = YearMonth.From(normalizedStart) == YearMonth.From(normalizedEnd);
            if (monthlyBudget.HasValue && monthlyBudget.Value > 0.0 && isSingleMonth)
            {
                var month = YearMonth.From(normalizedStart);
                projectionMonth = month;
                projectedMonthTotal = averageCreditsPerDay * month.LengthOfMonth;
                projectedMonthRemaining = monthlyBudget.Value - projectedMonthTotal;

                if (averageCreditsPerDay > 0.0 && projectedMonthTotal > monthlyBudget.Value)
                {
                    var runOutDay = (int)Math.Ceiling(monthlyBudget.Value / averageCreditsPerDay);
                    projectedBudgetRunOutDay = MathEx.Clamp(runOutDay, 1, month.LengthOfMonth);
                }
            }

            return new UsageStats
            {
                StartDate = normalizedStart,
                EndDate = normalizedEnd,
                DaysInRange = daysInRange,
                EntryCount = filtered.Count,
                ActiveDays = activeDays,
                TotalCredits = totalCredits,
                AverageCreditsPerDay = averageCreditsPerDay,
                AverageCreditsPerActiveDay = averageCreditsPerActiveDay,
                BusiestDay = busiestDay.HasValue ? busiestDay.Value.Key : (DateOnly?)null,
                BusiestDayCredits = busiestDay.HasValue ? busiestDay.Value.Value : (double?)null,
                HighestSingleEntry = filtered.Count == 0
                    ? (double?)null
                    : filtered.Max(pair => pair.Entry.Amount),
                LatestEntryDate = filtered.Count == 0
                    ? (DateOnly?)null
                    : filtered.Max(pair => pair.Day),
                MonthlyBudget = monthlyBudget,
                ProratedBudgetForRange = proratedBudget,
                RemainingBudgetForRange = proratedBudget - totalCredits,
                BudgetUsedPercent = budgetUsedPercent,
                ProjectedMonthTotal = projectedMonthTotal,
                ProjectedMonthRemaining = projectedMonthRemaining,
                ProjectedBudgetRunOutDay = projectedBudgetRunOutDay,
                ProjectionMonth = projectionMonth,
                DailyUsage = BuildDailySeries(creditsByDay, normalizedStart, daysInRange),
            };
        }

        private static IReadOnlyList<DailyUsage> BuildDailySeries(
            IReadOnlyDictionary<DateOnly, double> creditsByDay,
            DateOnly start,
            int daysInRange)
        {
            var series = new List<DailyUsage>(daysInRange);
            for (var offset = 0; offset < daysInRange; offset++)
            {
                var day = start.AddDays(offset);
                series.Add(new DailyUsage(day, creditsByDay.GetValueOrDefault(day)));
            }

            return series;
        }

        /// <summary>
        /// Spreads the monthly budget evenly across the days of each month the range
        /// touches, so a partial month contributes only its share.
        /// </summary>
        private static double CalculateProratedBudget(DateOnly startDate, DateOnly endDate, double monthlyBudget)
        {
            var total = 0.0;
            for (var day = startDate; day <= endDate; day = day.AddDays(1))
            {
                total += monthlyBudget / DateTime.DaysInMonth(day.Year, day.Month);
            }

            return total;
        }
    }
}
