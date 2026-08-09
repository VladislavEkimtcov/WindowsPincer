namespace CreditPincher.Core.Models;

/// <summary>
/// A single logged usage of AI credits. <see cref="Timestamp"/> is always UTC so the
/// on-disk representation matches what the JetBrains plugin writes (a Java <c>Instant</c>).
/// </summary>
/// <param name="Timestamp">Moment the usage was recorded, in UTC.</param>
/// <param name="Amount">Credits consumed. Always positive and finite.</param>
public readonly record struct CreditUsageEntry(DateTimeOffset Timestamp, double Amount)
{
    /// <summary>The calendar day this entry belongs to, in the given time zone.</summary>
    public DateOnly LocalDate(TimeZoneInfo? zone = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Timestamp, zone ?? TimeZoneInfo.Local).DateTime);
}
