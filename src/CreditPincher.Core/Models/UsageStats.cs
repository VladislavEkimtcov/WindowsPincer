using System;
using System.Collections.Generic;

namespace CreditPincher.Core.Models
{
    /// <summary>
    /// Everything the dashboard shows for one date range. Nullable members mean
    /// "not computable" (usually because no budget is set, or the range spans
    /// more than one calendar month).
    /// </summary>
    public sealed class UsageStats
    {
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public long DaysInRange { get; set; }
        public int EntryCount { get; set; }
        public int ActiveDays { get; set; }
        public double TotalCredits { get; set; }
        public double AverageCreditsPerDay { get; set; }
        public double AverageCreditsPerActiveDay { get; set; }

        public DateOnly? BusiestDay { get; set; }
        public double? BusiestDayCredits { get; set; }
        public double? HighestSingleEntry { get; set; }
        public DateOnly? LatestEntryDate { get; set; }

        public double? MonthlyBudget { get; set; }
        public double? ProratedBudgetForRange { get; set; }
        public double? RemainingBudgetForRange { get; set; }
        public double? BudgetUsedPercent { get; set; }

        public double? ProjectedMonthTotal { get; set; }
        public double? ProjectedMonthRemaining { get; set; }
        public int? ProjectedBudgetRunOutDay { get; set; }
        public YearMonth? ProjectionMonth { get; set; }

        /// <summary>Credits per day, keyed by calendar day, for every day in the range (zero-filled).</summary>
        public IReadOnlyList<DailyUsage> DailyUsage { get; set; }
    }

    /// <summary>One bar of the usage chart.</summary>
    public struct DailyUsage : IEquatable<DailyUsage>
    {
        private readonly DateOnly _date;
        private readonly double _credits;

        public DailyUsage(DateOnly date, double credits)
        {
            _date = date;
            _credits = credits;
        }

        public DateOnly Date { get { return _date; } }

        public double Credits { get { return _credits; } }

        public bool Equals(DailyUsage other)
        {
            return _date == other._date && _credits.Equals(other._credits);
        }

        public override bool Equals(object obj)
        {
            return obj is DailyUsage && Equals((DailyUsage)obj);
        }

        public override int GetHashCode()
        {
            return _date.GetHashCode() ^ _credits.GetHashCode();
        }
    }
}
