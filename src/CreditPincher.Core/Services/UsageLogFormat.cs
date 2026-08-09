using System;
using System.Collections.Generic;
using System.Globalization;
using CreditPincher.Core.Models;

namespace CreditPincher.Core.Services
{
    /// <summary>
    /// Reading and writing of the <c>usage-log.csv</c> line format.
    ///
    /// The format is deliberately byte-compatible with the JetBrains plugin, which
    /// writes <c>Instant.toString(),Double.toString()</c>. Keeping the two in sync
    /// means the same <c>~/.creditpincher</c> folder can be shared (or git-synced)
    /// between a machine running the IDE plugin and one running this tray app.
    /// </summary>
    public static class UsageLogFormat
    {
        public const string Header = "timestamp,amount";

        /// <summary>Parses one CSV line, returning null for blank/garbage lines.</summary>
        public static CreditUsageEntry? ParseLine(string line)
        {
            var separator = line.IndexOf(',');
            if (separator < 0)
            {
                return null;
            }

            var timestampText = line.Substring(0, separator).Trim();
            var amountText = line.Substring(separator + 1).Trim();

            DateTimeOffset timestamp;
            if (!DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal,
                    out timestamp))
            {
                return null;
            }

            double amount;
            if (!double.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
            {
                return null;
            }

            return new CreditUsageEntry(timestamp.ToUniversalTime(), amount);
        }

        /// <summary>Parses a whole file body, skipping the header row.</summary>
        public static List<CreditUsageEntry> ParseAll(string content)
        {
            var entries = new List<CreditUsageEntry>();
            var lines = content.Split('\n');

            // Skip the header row, exactly like the plugin's `drop(1)`.
            for (var i = 1; i < lines.Length; i++)
            {
                var parsed = ParseLine(lines[i].TrimEnd('\r'));
                if (parsed.HasValue)
                {
                    entries.Add(parsed.Value);
                }
            }

            return entries;
        }

        /// <summary>Renders one entry as a CSV line, including the trailing newline.</summary>
        public static string FormatLine(CreditUsageEntry entry)
        {
            return FormatTimestamp(entry.Timestamp) + "," + FormatAmount(entry.Amount) + "\n";
        }

        /// <summary>ISO-8601 UTC, parseable by <c>java.time.Instant.parse</c>.</summary>
        public static string FormatTimestamp(DateTimeOffset timestamp)
        {
            var utc = timestamp.ToUniversalTime();
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Round-trippable number text. A ".0" is appended to whole numbers so the file
        /// looks the same as the Kotlin <c>Double.toString()</c> output.
        /// </summary>
        public static string FormatAmount(double amount)
        {
            var text = amount.ToString("R", CultureInfo.InvariantCulture);
            var hasFractionOrExponent =
                text.IndexOf('.') >= 0 || text.IndexOf('E') >= 0 || text.IndexOf('e') >= 0;
            return hasFractionOrExponent ? text : text + ".0";
        }
    }
}
