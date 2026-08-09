using System.Text;
using CreditPincher.Core.Models;

namespace CreditPincher.Core.Services;

/// <summary>
/// Pure logic for merging two conflicting versions of <c>usage-log.csv</c>
/// (as produced by <c>git show :2:usage-log.csv</c> / <c>git show :3:usage-log.csv</c>)
/// into a single, deduplicated, chronologically sorted CSV.
///
/// Two machines logging usage into the same synced folder is the normal case,
/// so "keep both sides" is almost always the right answer.
/// </summary>
public static class UsageLogMerger
{
    /// <summary>Merges the "ours" and "theirs" CSV contents, returning the resolved CSV text.</summary>
    public static string Merge(string oursContent, string theirsContent)
    {
        var merged = ParseEntries(oursContent)
            .Concat(ParseEntries(theirsContent))
            .DistinctBy(entry => (entry.Timestamp, entry.Amount))
            .OrderBy(entry => entry.Timestamp)
            .ToList();

        var builder = new StringBuilder()
            .Append(UsageLogFormat.Header)
            .Append('\n');

        foreach (var entry in merged)
        {
            builder.Append(UsageLogFormat.FormatLine(entry));
        }

        return builder.ToString();
    }

    public static List<CreditUsageEntry> ParseEntries(string content) => UsageLogFormat.ParseAll(content);
}
