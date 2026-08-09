using System.Globalization;

namespace CreditPincher.Core.Models;

/// <summary>
/// A year + month pair, mirroring <c>java.time.YearMonth</c> from the JetBrains plugin.
/// Used for the single-month projection statistics.
/// </summary>
public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public static YearMonth From(DateOnly date) => new(date.Year, date.Month);

    public int LengthOfMonth => DateTime.DaysInMonth(Year, Month);

    public DateOnly AtDay(int day) => new(Year, Month, day);

    public int CompareTo(YearMonth other)
    {
        var byYear = Year.CompareTo(other.Year);
        return byYear != 0 ? byYear : Month.CompareTo(other.Month);
    }

    public override string ToString() =>
        new DateOnly(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture);
}
