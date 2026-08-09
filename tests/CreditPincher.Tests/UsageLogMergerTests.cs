using CreditPincher.Core.Services;
using Xunit;

namespace CreditPincher.Tests
{
    public class UsageLogMergerTests
    {
        [Fact]
        public void MergesBothSidesChronologicallyWithoutDuplicates()
        {
            const string ours =
                "timestamp,amount\n" +
                "2026-07-01T09:00:00Z,12.0\n" +
                "2026-07-03T09:00:00Z,5.0\n";

            const string theirs =
                "timestamp,amount\n" +
                "2026-07-01T09:00:00Z,12.0\n" +
                "2026-07-02T09:00:00Z,7.0\n";

            var merged = UsageLogMerger.Merge(ours, theirs);
            var entries = UsageLogMerger.ParseEntries(merged);

            Assert.Equal(3, entries.Count);
            Assert.Equal(12.0, entries[0].Amount, 4);
            Assert.Equal(7.0, entries[1].Amount, 4);
            Assert.Equal(5.0, entries[2].Amount, 4);
            Assert.StartsWith(UsageLogFormat.Header, merged);
        }

        [Fact]
        public void KeepsSameTimestampEntriesWithDifferentAmounts()
        {
            const string ours = "timestamp,amount\n2026-07-01T09:00:00Z,12.0\n";
            const string theirs = "timestamp,amount\n2026-07-01T09:00:00Z,8.0\n";

            var entries = UsageLogMerger.ParseEntries(UsageLogMerger.Merge(ours, theirs));

            Assert.Equal(2, entries.Count);
        }

        [Fact]
        public void HandlesAnEmptySide()
        {
            const string ours = "timestamp,amount\n2026-07-01T09:00:00Z,12.0\n";

            var entries = UsageLogMerger.ParseEntries(UsageLogMerger.Merge(ours, string.Empty));

            Assert.Single(entries);
        }

        [Theory]
        [InlineData(12.0, "12.0")]
        [InlineData(12.5, "12.5")]
        [InlineData(0.25, "0.25")]
        public void FormatsAmountsLikeTheKotlinPlugin(double amount, string expected)
        {
            Assert.Equal(expected, UsageLogFormat.FormatAmount(amount));
        }
    }
}
