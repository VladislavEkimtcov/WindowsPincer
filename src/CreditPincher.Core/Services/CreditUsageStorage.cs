using System.Globalization;
using System.Text;
using CreditPincher.Core.Models;

namespace CreditPincher.Core.Services;

/// <summary>
/// Plain-text storage for usage entries and the monthly budget.
///
/// Layout (identical to the JetBrains plugin, so the folder can be shared or
/// git-synced between machines):
/// <code>
/// %USERPROFILE%\.creditpincher\
///     usage-log.csv        timestamp,amount
///     monthly-budget.txt   a single number, or empty
/// </code>
///
/// Unlike the plugin, this class assumes another process (the IDE plugin, a git
/// pull, a second tray instance) may be touching the same files, so writes are
/// retried on sharing violations and <see cref="Changed"/> fires when something
/// else edits them.
/// </summary>
public sealed class CreditUsageStorage : IDisposable
{
    private const string UsageLogFileName = "usage-log.csv";
    private const string BudgetFileName = "monthly-budget.txt";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _lock = new();
    private readonly TimeProvider _timeProvider;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _disposed;

    public CreditUsageStorage(string? storageDirectory = null, TimeProvider? timeProvider = null)
    {
        StorageDirectory = storageDirectory ?? DefaultStorageDirectory();
        UsageLogPath = Path.Combine(StorageDirectory, UsageLogFileName);
        BudgetPath = Path.Combine(StorageDirectory, BudgetFileName);
        _timeProvider = timeProvider ?? TimeProvider.System;

        Initialize();
    }

    /// <summary>Raised (off the UI thread) when the files change on disk behind our back.</summary>
    public event EventHandler? Changed;

    public string StorageDirectory { get; }

    public string UsageLogPath { get; }

    public string BudgetPath { get; }

    public static string DefaultStorageDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".creditpincher");

    public void Initialize()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(StorageDirectory);
            EnsureFileExists(UsageLogPath, UsageLogFormat.Header + "\n");
            EnsureFileExists(BudgetPath, string.Empty);
        }
    }

    public IReadOnlyList<CreditUsageEntry> LoadEntries()
    {
        lock (_lock)
        {
            return Retry(() =>
            {
                if (!File.Exists(UsageLogPath))
                {
                    return new List<CreditUsageEntry>();
                }

                using var stream = new FileStream(
                    UsageLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Utf8NoBom);
                return UsageLogFormat.ParseAll(reader.ReadToEnd());
            });
        }
    }

    public CreditUsageEntry AddUsage(double amount)
    {
        ValidatePositiveFiniteAmount(amount, nameof(amount));

        var entry = new CreditUsageEntry(_timeProvider.GetUtcNow(), amount);

        lock (_lock)
        {
            Retry(() =>
            {
                EnsureHeaderAndTrailingNewline();
                using var stream = new FileStream(
                    UsageLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Utf8NoBom);
                writer.Write(UsageLogFormat.FormatLine(entry));
                return true;
            });
        }

        return entry;
    }

    /// <summary>
    /// Rewrites the whole log. Used by conflict resolution and by
    /// <see cref="DeleteEntry"/>; ordinary logging appends instead.
    /// </summary>
    public void ReplaceEntries(IEnumerable<CreditUsageEntry> entries)
    {
        var ordered = entries.OrderBy(entry => entry.Timestamp).ToList();
        var builder = new StringBuilder().Append(UsageLogFormat.Header).Append('\n');
        foreach (var entry in ordered)
        {
            builder.Append(UsageLogFormat.FormatLine(entry));
        }

        lock (_lock)
        {
            Retry(() =>
            {
                WriteAllTextAtomic(UsageLogPath, builder.ToString());
                return true;
            });
        }
    }

    /// <summary>Removes the first entry matching timestamp and amount. Returns true if something was removed.</summary>
    public bool DeleteEntry(CreditUsageEntry entry)
    {
        lock (_lock)
        {
            var entries = LoadEntries().ToList();
            var index = entries.FindIndex(candidate =>
                candidate.Timestamp == entry.Timestamp &&
                candidate.Amount.Equals(entry.Amount));

            if (index < 0)
            {
                return false;
            }

            entries.RemoveAt(index);
            ReplaceEntries(entries);
            return true;
        }
    }

    public double? LoadMonthlyBudget()
    {
        lock (_lock)
        {
            return Retry<double?>(() =>
            {
                if (!File.Exists(BudgetPath))
                {
                    return null;
                }

                using var stream = new FileStream(
                    BudgetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Utf8NoBom);
                var text = reader.ReadToEnd().Trim();

                if (text.Length == 0)
                {
                    return null;
                }

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : null;
            });
        }
    }

    public void SaveMonthlyBudget(double? amount)
    {
        if (amount is { } value)
        {
            ValidatePositiveFiniteAmount(value, nameof(amount));
        }

        var text = amount is { } budget ? UsageLogFormat.FormatAmount(budget) : string.Empty;

        lock (_lock)
        {
            Retry(() =>
            {
                WriteAllTextAtomic(BudgetPath, text);
                return true;
            });
        }
    }

    /// <summary>
    /// Starts watching the storage folder so the dashboard can live-update when the
    /// IDE plugin (or a git pull) writes to the same files.
    /// </summary>
    public void StartWatching()
    {
        if (_watcher is not null || _disposed)
        {
            return;
        }

        _watcher = new FileSystemWatcher(StorageDirectory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Dispose();
        _debounce = null;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        var name = e.Name;
        if (name is not null &&
            !name.Equals(UsageLogFileName, StringComparison.OrdinalIgnoreCase) &&
            !name.Equals(BudgetFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Editors and git write in bursts; collapse them into a single notification.
        _debounce ??= new Timer(_ => Changed?.Invoke(this, EventArgs.Empty), null, Timeout.Infinite, Timeout.Infinite);
        _debounce.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Guards against a log whose last append was cut short (or that was hand-edited
    /// without a trailing newline), which would otherwise glue two entries together.
    /// </summary>
    private void EnsureHeaderAndTrailingNewline()
    {
        if (!File.Exists(UsageLogPath))
        {
            WriteAllTextAtomic(UsageLogPath, UsageLogFormat.Header + "\n");
            return;
        }

        using var stream = new FileStream(UsageLogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (stream.Length == 0)
        {
            using var writer = new StreamWriter(stream, Utf8NoBom);
            writer.Write(UsageLogFormat.Header + "\n");
            return;
        }

        stream.Seek(-1, SeekOrigin.End);
        var lastByte = stream.ReadByte();
        if (lastByte != '\n')
        {
            stream.Seek(0, SeekOrigin.End);
            using var writer = new StreamWriter(stream, Utf8NoBom);
            writer.Write('\n');
        }
    }

    private static void EnsureFileExists(string path, string initialContents)
    {
        if (!File.Exists(path))
        {
            WriteAllTextAtomic(path, initialContents);
        }
    }

    /// <summary>Write via a temp file + replace so a crash mid-write cannot truncate the log.</summary>
    private static void WriteAllTextAtomic(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, contents, Utf8NoBom);

        if (File.Exists(path))
        {
            File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, path);
        }
    }

    private static void ValidatePositiveFiniteAmount(double amount, string parameterName)
    {
        if (!double.IsFinite(amount) || amount <= 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, amount, "Amount must be a positive finite number.");
        }
    }

    /// <summary>
    /// Retries a few times on transient IO errors. The plugin, a git command, and
    /// antivirus can all hold these files open for a moment.
    /// </summary>
    private static T Retry<T>(Func<T> action, int attempts = 5)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(40 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(40 * attempt);
            }
        }
    }
}
