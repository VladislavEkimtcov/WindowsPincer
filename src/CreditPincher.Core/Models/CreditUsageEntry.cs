using System;

namespace CreditPincher.Core.Models
{
    /// <summary>
    /// A single logged usage of AI credits. <see cref="Timestamp"/> is always UTC so the
    /// on-disk representation matches what the JetBrains plugin writes (a Java <c>Instant</c>).
    /// </summary>
    public struct CreditUsageEntry : IEquatable<CreditUsageEntry>
    {
        private readonly DateTimeOffset _timestamp;
        private readonly double _amount;

        /// <param name="timestamp">Moment the usage was recorded, in UTC.</param>
        /// <param name="amount">Credits consumed. Always positive and finite.</param>
        public CreditUsageEntry(DateTimeOffset timestamp, double amount)
        {
            _timestamp = timestamp;
            _amount = amount;
        }

        public DateTimeOffset Timestamp { get { return _timestamp; } }

        public double Amount { get { return _amount; } }

        /// <summary>The calendar day this entry belongs to, in the given time zone.</summary>
        public DateOnly LocalDate()
        {
            return LocalDate(null);
        }

        /// <summary>The calendar day this entry belongs to, in the given time zone.</summary>
        public DateOnly LocalDate(TimeZoneInfo zone)
        {
            return DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(_timestamp, zone ?? TimeZoneInfo.Local).DateTime);
        }

        public bool Equals(CreditUsageEntry other)
        {
            return _timestamp == other._timestamp && _amount.Equals(other._amount);
        }

        public override bool Equals(object obj)
        {
            return obj is CreditUsageEntry && Equals((CreditUsageEntry)obj);
        }

        public override int GetHashCode()
        {
            return _timestamp.GetHashCode() ^ _amount.GetHashCode();
        }
    }
}
