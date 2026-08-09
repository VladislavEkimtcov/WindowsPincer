using System;
using System.Globalization;

namespace CreditPincher.Core.Models
{
    /// <summary>
    /// A year + month pair, mirroring <c>java.time.YearMonth</c> from the JetBrains plugin.
    /// Used for the single-month projection statistics.
    /// </summary>
    public struct YearMonth : IComparable<YearMonth>, IEquatable<YearMonth>
    {
        private readonly int _year;
        private readonly int _month;

        public YearMonth(int year, int month)
        {
            _year = year;
            _month = month;
        }

        public int Year { get { return _year; } }

        public int Month { get { return _month; } }

        public static YearMonth From(DateOnly date)
        {
            return new YearMonth(date.Year, date.Month);
        }

        public int LengthOfMonth
        {
            get { return DateTime.DaysInMonth(_year, _month); }
        }

        public DateOnly AtDay(int day)
        {
            return new DateOnly(_year, _month, day);
        }

        public int CompareTo(YearMonth other)
        {
            var byYear = _year.CompareTo(other._year);
            return byYear != 0 ? byYear : _month.CompareTo(other._month);
        }

        public bool Equals(YearMonth other)
        {
            return _year == other._year && _month == other._month;
        }

        public override bool Equals(object obj)
        {
            return obj is YearMonth && Equals((YearMonth)obj);
        }

        public override int GetHashCode()
        {
            return (_year * 397) ^ _month;
        }

        public static bool operator ==(YearMonth left, YearMonth right) { return left.Equals(right); }

        public static bool operator !=(YearMonth left, YearMonth right) { return !left.Equals(right); }

        public override string ToString()
        {
            return new DateOnly(_year, _month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        }
    }
}
