using System;
using System.IO;
using CreditPincher.Core.Services;
using Xunit;

namespace CreditPincher.Tests
{
    public class CreditUsageStorageTests : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "creditpincher-tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void CreatesStorageFilesOnFirstUse()
        {
            using (var storage = new CreditUsageStorage(_directory))
            {
                Assert.True(File.Exists(storage.UsageLogPath));
                Assert.True(File.Exists(storage.BudgetPath));
                Assert.StartsWith(UsageLogFormat.Header, File.ReadAllText(storage.UsageLogPath));
            }
        }

        [Fact]
        public void RoundTripsUsageEntries()
        {
            using (var storage = new CreditUsageStorage(_directory))
            {
                storage.AddUsage(12.5);
                storage.AddUsage(3.0);

                var entries = storage.LoadEntries();

                Assert.Equal(2, entries.Count);
                Assert.Equal(12.5, entries[0].Amount, 4);
                Assert.Equal(3.0, entries[1].Amount, 4);
            }
        }

        [Fact]
        public void RoundTripsMonthlyBudget()
        {
            using (var storage = new CreditUsageStorage(_directory))
            {
                Assert.Null(storage.LoadMonthlyBudget());

                storage.SaveMonthlyBudget(310.0);
                Assert.Equal(310.0, storage.LoadMonthlyBudget().Value, 4);

                storage.SaveMonthlyBudget(null);
                Assert.Null(storage.LoadMonthlyBudget());
            }
        }

        [Fact]
        public void RejectsNonPositiveAmounts()
        {
            using (var storage = new CreditUsageStorage(_directory))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => storage.AddUsage(0.0));
                Assert.Throws<ArgumentOutOfRangeException>(() => storage.AddUsage(-1.0));
                Assert.Throws<ArgumentOutOfRangeException>(() => storage.AddUsage(double.NaN));
            }
        }

        [Fact]
        public void ReadsLogsWrittenByTheJetBrainsPlugin()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(
                Path.Combine(_directory, "usage-log.csv"),
                "timestamp,amount\n" +
                "2026-07-01T09:00:00Z,12.0\n" +
                "2026-07-02T10:15:30.123456Z,7.25\n" +
                "not-a-timestamp,5.0\n" +
                "2026-07-03T11:00:00Z,not-a-number\n");

            using (var storage = new CreditUsageStorage(_directory))
            {
                var entries = storage.LoadEntries();

                Assert.Equal(2, entries.Count);
                Assert.Equal(12.0, entries[0].Amount, 4);
                Assert.Equal(7.25, entries[1].Amount, 4);
            }
        }

        [Fact]
        public void RepairsALogMissingItsTrailingNewline()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(
                Path.Combine(_directory, "usage-log.csv"),
                "timestamp,amount\n2026-07-01T09:00:00Z,12.0");

            using (var storage = new CreditUsageStorage(_directory))
            {
                storage.AddUsage(4.0);

                var entries = storage.LoadEntries();
                Assert.Equal(2, entries.Count);
                Assert.Equal(12.0, entries[0].Amount, 4);
                Assert.Equal(4.0, entries[1].Amount, 4);
            }
        }

        [Fact]
        public void DeletesASingleEntry()
        {
            using (var storage = new CreditUsageStorage(_directory))
            {
                var first = storage.AddUsage(12.5);
                storage.AddUsage(3.0);

                Assert.True(storage.DeleteEntry(first));

                var entries = storage.LoadEntries();
                Assert.Single(entries);
                Assert.Equal(3.0, entries[0].Amount, 4);
            }
        }

        [Fact]
        public void WritesTimestampsJavaCanParse()
        {
            using (var storage = new CreditUsageStorage(_directory))
            {
                storage.AddUsage(1.0);

                var line = File.ReadAllLines(storage.UsageLogPath)[1];
                var timestamp = line.Split(',')[0];

                // java.time.Instant.parse requires the trailing 'Z' and an ISO-8601 body.
                Assert.EndsWith("Z", timestamp);
                Assert.Contains("T", timestamp);
                Assert.NotNull(UsageLogFormat.ParseLine(line));
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }

            GC.SuppressFinalize(this);
        }
    }
}
