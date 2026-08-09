using System.Windows;
using System.Windows.Threading;
using CreditPincher.Core.Models;
using CreditPincher.Core.Services;

namespace CreditPincher.App;

/// <summary>
/// The single place that owns storage, settings and git for the process.
/// Windows and the tray icon all read from here and listen to <see cref="DataChanged"/>,
/// so a usage logged from the hotkey box immediately refreshes an open dashboard.
/// </summary>
public sealed class AppServices : IDisposable
{
    private readonly Dispatcher _dispatcher;

    public AppServices(Dispatcher dispatcher, IGitConflictResolver conflictResolver)
    {
        _dispatcher = dispatcher;
        SettingsStore = new SettingsStore();
        Settings = SettingsStore.Load();
        Storage = new CreditUsageStorage();
        Git = new GitBackupService(Storage.StorageDirectory, conflictResolver);

        // The IDE plugin or a git pull may edit the same files while we are running.
        Storage.Changed += (_, _) => _dispatcher.BeginInvoke(() => DataChanged?.Invoke(this, EventArgs.Empty));
        Storage.StartWatching();
    }

    /// <summary>Raised on the UI thread whenever entries, the budget, or settings change.</summary>
    public event EventHandler? DataChanged;

    public CreditUsageStorage Storage { get; }

    public SettingsStore SettingsStore { get; }

    public AppSettings Settings { get; }

    public GitBackupService Git { get; }

    /// <summary>A formatter reflecting the current credits/dollars preference.</summary>
    public CreditFormatter Formatter => new(Settings.ShowInDollars, Settings.SafeCreditsPerDollar);

    public void SaveSettings()
    {
        SettingsStore.Save(Settings);
        RaiseDataChanged();
    }

    public void RaiseDataChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcher.BeginInvoke(() => DataChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    /// <summary>Stats for the current calendar month, used by the tray tooltip and alerts.</summary>
    public UsageStats MonthToDate()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return CreditStatsCalculator.Calculate(
            Storage.LoadEntries(),
            new DateOnly(today.Year, today.Month, 1),
            today,
            Storage.LoadMonthlyBudget());
    }

    public void Dispose() => Storage.Dispose();

    /// <summary>Convenience accessor; the app always has exactly one instance.</summary>
    public static AppServices Current => ((App)Application.Current).Services;
}
