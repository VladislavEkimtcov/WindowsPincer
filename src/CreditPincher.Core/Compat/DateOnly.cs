using System.Globalization;

namespace System
{
    /// <summary>
    /// A date without a time, matching the shape of .NET 8's <c>System.DateOnly</c>.
    ///
    /// The original code was written against .NET 8; this build targets the .NET
    /// Framework 4.8 that ships with Windows, which predates the type. Only the
    /// members CreditPincher actually uses are implemented, and they behave
    /// identically, so the calling code did not have to change.
    /// </summary>
    public struct DateOnly : IComparable<DateOnly>, IEquatable<DateOnly>
    {
        private readonly DateTime _date;

        public DateOnly(int year, int month, int day)
        {
            _date = new DateTime(year, month, day);
        }

        private DateOnly(DateTime date)
        {
            _date = date.Date;
        }

        public int Year { get { return _date.Year; } }

        public int Month { get { return _date.Month; } }

        public int Day { get { return _date.Day; } }

        /// <summary>Days since 0001-01-01, so ranges can be measured by subtraction.</summary>
        public int DayNumber { get { return (int)(_date.Ticks / TimeSpan.TicksPerDay); } }

        public static DateOnly FromDateTime(DateTime value)
        {
            return new DateOnly(value.Date);
        }

        public DateTime ToDateTime()
        {
            return _date;
        }

        public DateOnly AddDays(int days)
        {
            return new DateOnly(_date.AddDays(days));
        }

        public int CompareTo(DateOnly other)
        {
            return _date.CompareTo(other._date);
        }

        public bool Equals(DateOnly other)
        {
            return _date == other._date;
        }

        public override bool Equals(object obj)
        {
            return obj is DateOnly && Equals((DateOnly)obj);
        }

        public override int GetHashCode()
        {
            return _date.GetHashCode();
        }

        public override string ToString()
        {
            return _date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public string ToString(string format, IFormatProvider provider)
        {
            return _date.ToString(format, provider);
        }

        public static bool operator ==(DateOnly left, DateOnly right) { return left._date == right._date; }

        public static bool operator !=(DateOnly left, DateOnly right) { return left._date != right._date; }

        public static bool operator <(DateOnly left, DateOnly right) { return left._date < right._date; }

        public static bool operator <=(DateOnly left, DateOnly right) { return left._date <= right._date; }

        public static bool operator >(DateOnly left, DateOnly right) { return left._date > right._date; }

        public static bool operator >=(DateOnly left, DateOnly right) { return left._date >= right._date; }
    }
}
