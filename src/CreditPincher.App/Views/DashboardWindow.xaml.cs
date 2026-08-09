using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CreditPincher.App.Platform;
using CreditPincher.App.Tray;
using CreditPincher.Core.Models;
using CreditPincher.Core.Services;

namespace CreditPincher.App.Views;

/// <summary>
/// The full view of the data: everything the plugin's tool window offered, plus the
/// history, backup and settings the tray app needs to stand on its own.
/// </summary>
public partial class DashboardWindow : Window
{
    public const int DashboardTabIndex = 0;
    public const int HistoryTabIndex = 1;
    public const int BackupTabIndex = 2;
    public const int SettingsTabIndex = 3;

    private readonly AppServices _services;
    private readonly TrayController _tray;
    private readonly EventHandler _dataChangedHandler;

    private IReadOnlyList<CreditUsageEntry> _entries = [];
    private bool _loaded;

    public DashboardWindow(AppServices services, TrayController tray)
    {
        _services = services;
        _tray = tray;

        InitializeComponent();
        TryApplyIcon();

        var today = DateTime.Today;
        StartPicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
        EndPicker.SelectedDate = today;

        StoragePathBox.Text = _services.Storage.StorageDirectory;

        LoadSettingsIntoUi();
        _loaded = true;

        Refresh();
        RefreshGitState();

        _dataChangedHandler = (_, _) => Refresh();
        _services.DataChanged += _dataChangedHandler;
        Closed += (_, _) => _services.DataChanged -= _dataChangedHandler;
    }

    /// <summary>One row of the History tab.</summary>
    public sealed record EntryRow(string When, string Amount, CreditUsageEntry Entry);

    public void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.Items.Count)
        {
            Tabs.SelectedIndex = index;
        }
    }

    // ----------------------------------------------------------------- refresh

    private void Refresh()
    {
        if (!_loaded)
        {
            return;
        }

        _entries = _services.Storage.LoadEntries();
        var budget = _services.Storage.LoadMonthlyBudget();
        var formatter = _services.Formatter;

        var stats = CreditStatsCalculator.Calculate(_entries, SelectedStart(), SelectedEnd(), budget);
        var monthToDate = _services.MonthToDate();

        MonthTotalText.Text = formatter.Credits(monthToDate.TotalCredits);
        UnitToggleButton.Content = _services.Settings.ShowInDollars ? "Show in credits" : "Show in dollars";
        UpdateBudgetHeadline(monthToDate, formatter);

        RangeValue.Text = formatter.Range(stats);
        TotalValue.Text = formatter.Credits(stats.TotalCredits);
        EntryCountValue.Text = stats.EntryCount.ToString(CultureInfo.CurrentCulture);
        ActiveDaysValue.Text = stats.ActiveDays.ToString(CultureInfo.CurrentCulture);
        AveragePerDayValue.Text = formatter.Credits(stats.AverageCreditsPerDay);
        AveragePerActiveDayValue.Text = formatter.Credits(stats.AverageCreditsPerActiveDay);
        BusiestDayValue.Text = formatter.BusiestDay(stats);
        HighestEntryValue.Text = formatter.Credits(stats.HighestSingleEntry);
        BudgetValue.Text = formatter.Credits(stats.MonthlyBudget);
        RangeBudgetValue.Text = formatter.BudgetInRange(stats);
        RemainingBudgetValue.Text = formatter.Credits(stats.RemainingBudgetForRange);
        ProjectedMonthValue.Text = formatter.ProjectedMonthTotal(stats);
        RunwayValue.Text = formatter.Runway(stats);
        LastEntryValue.Text = formatter.Date(stats.LatestEntryDate);

        Chart.UpdateData(stats.DailyUsage, _services.Settings.ShowInDollars, _services.Settings.SafeCreditsPerDollar);

        if (!BudgetBox.IsKeyboardFocused)
        {
            BudgetBox.Text = budget is { } value ? value.ToString("0.####", CultureInfo.CurrentCulture) : string.Empty;
        }

        RefreshHistory(formatter);
    }

    private void UpdateBudgetHeadline(UsageStats monthToDate, CreditFormatter formatter)
    {
        if (monthToDate.MonthlyBudget is not { } budget || budget <= 0)
        {
            BudgetProgress.Value = 0;
            BudgetSummaryText.Text = "No monthly budget set — add one below to unlock pacing stats.";
            return;
        }

        var usedPercent = monthToDate.TotalCredits / budget * 100.0;
        BudgetProgress.Value = Math.Clamp(usedPercent, 0, 100);
        BudgetProgress.Foreground = usedPercent switch
        {
            >= 100 => (Brush)FindResource("DangerBrush"),
            >= 80 => (Brush)FindResource("WarnBrush"),
            _ => (Brush)FindResource("AccentBrush"),
        };

        BudgetSummaryText.Text =
            $"{formatter.Percent(usedPercent)} of {formatter.Credits(budget)} used — {formatter.Runway(monthToDate)}";
    }

    private void RefreshHistory(CreditFormatter formatter)
    {
        var onlyRange = OnlyRangeCheck.IsChecked == true;
        var start = SelectedStart();
        var end = SelectedEnd();

        var rows = _entries
            .Where(entry => !onlyRange || IsWithin(entry, start, end))
            .OrderByDescending(entry => entry.Timestamp)
            .Select(entry => new EntryRow(
                entry.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
                formatter.Credits(entry.Amount),
                entry))
            .ToList();

        EntriesList.ItemsSource = rows;
        HistoryCountText.Text = rows.Count == 1 ? "1 entry" : $"{rows.Count} entries";
    }

    private static bool IsWithin(CreditUsageEntry entry, DateOnly start, DateOnly end)
    {
        var day = entry.LocalDate();
        return day >= start && day <= end;
    }

    private DateOnly SelectedStart() => DateOnly.FromDateTime(StartPicker.SelectedDate ?? DateTime.Today);

    private DateOnly SelectedEnd() => DateOnly.FromDateTime(EndPicker.SelectedDate ?? DateTime.Today);

    // ----------------------------------------------------------------- dashboard tab

    private void OnAmountKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            LogUsage();
        }
    }

    private void OnLogUsageClick(object sender, RoutedEventArgs e) => LogUsage();

    private void LogUsage()
    {
        if (!TryParseAmount(AmountBox.Text, out var amount))
        {
            LogStatusText.Foreground = (Brush)FindResource("DangerBrush");
            LogStatusText.Text = "Enter a positive number of credits before submitting.";
            return;
        }

        try
        {
            _services.Storage.AddUsage(amount);
        }
        catch (Exception exception)
        {
            LogStatusText.Foreground = (Brush)FindResource("DangerBrush");
            LogStatusText.Text = exception.Message;
            return;
        }

        AmountBox.Text = string.Empty;
        LogStatusText.Foreground = (Brush)FindResource("AccentBrush");
        LogStatusText.Text = $"Recorded {_services.Formatter.Credits(amount)}.";
        _services.RaiseDataChanged();
    }

    private void OnBudgetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SaveBudget();
        }
    }

    private void OnSaveBudgetClick(object sender, RoutedEventArgs e) => SaveBudget();

    private void OnClearBudgetClick(object sender, RoutedEventArgs e)
    {
        BudgetBox.Text = string.Empty;
        _services.Storage.SaveMonthlyBudget(null);
        BudgetStatusText.Text = "Monthly budget cleared.";
        _services.RaiseDataChanged();
    }

    private void SaveBudget()
    {
        var text = BudgetBox.Text.Trim();

        if (text.Length == 0)
        {
            _services.Storage.SaveMonthlyBudget(null);
            BudgetStatusText.Text = "Monthly budget cleared.";
            _services.RaiseDataChanged();
            return;
        }

        if (!TryParseAmount(text, out var amount))
        {
            BudgetStatusText.Text = "Enter a positive budget, or leave it blank to clear it.";
            return;
        }

        _services.Storage.SaveMonthlyBudget(amount);
        BudgetStatusText.Text = $"Monthly budget saved: {_services.Formatter.Credits(amount)}.";
        _services.RaiseDataChanged();
    }

    private void OnToggleUnitClick(object sender, RoutedEventArgs e)
    {
        _services.Settings.ShowInDollars = !_services.Settings.ShowInDollars;
        ShowDollarsCheck.IsChecked = _services.Settings.ShowInDollars;
        _services.SaveSettings();
    }

    private void OnRangeChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnThisMonthClick(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        SetRange(new DateTime(today.Year, today.Month, 1), today);
    }

    private void OnLast7Click(object sender, RoutedEventArgs e) =>
        SetRange(DateTime.Today.AddDays(-6), DateTime.Today);

    private void OnLast30Click(object sender, RoutedEventArgs e) =>
        SetRange(DateTime.Today.AddDays(-29), DateTime.Today);

    private void OnThisYearClick(object sender, RoutedEventArgs e) =>
        SetRange(new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today);

    private void SetRange(DateTime start, DateTime end)
    {
        StartPicker.SelectedDate = start;
        EndPicker.SelectedDate = end;
        Refresh();
    }

    // ----------------------------------------------------------------- history tab

    private void OnHistoryFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            Refresh();
        }
    }

    private void OnDeleteEntryClick(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not EntryRow row)
        {
            HistoryStatusText.Text = "Select an entry first.";
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Delete the entry from {row.When} ({row.Amount})?",
            "CreditPincher",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        HistoryStatusText.Text = _services.Storage.DeleteEntry(row.Entry)
            ? "Entry deleted."
            : "That entry was already gone.";

        _services.RaiseDataChanged();
    }

    // ----------------------------------------------------------------- backup tab

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _services.Storage.StorageDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            AppendGitOutput(exception.Message);
        }
    }

    private void RefreshGitState()
    {
        GitStatusText.Text = "Checking git status…";
        SetGitButtonsEnabled(false);

        RunGit(
            DescribeGitState,
            state =>
            {
                GitStatusText.Text = state.Message;
                GitConnectPanel.Visibility = state.IsRepo ? Visibility.Collapsed : Visibility.Visible;
                GitBackupPanel.Visibility = state.IsRepo ? Visibility.Visible : Visibility.Collapsed;
                SetGitButtonsEnabled(true);
            });
    }

    /// <summary>Summary of the repository, computed off the UI thread.</summary>
    private sealed record GitState(bool IsRepo, string Message);

    private GitState DescribeGitState()
    {
        var git = _services.Git;

        if (!git.IsGitAvailable())
        {
            return new GitState(false, "git was not found on PATH. Install Git for Windows to enable backups.");
        }

        if (!git.IsGitRepository())
        {
            return new GitState(false, "This storage folder is not a git repository yet.");
        }

        var remote = git.GetRemoteUrl() ?? "no remote configured";
        var when = git.GetLastCommitTime() is { } time
            ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "never";

        return new GitState(true, $"Backed by git ({remote}). Last commit: {when}.");
    }

    private void OnConnectGitClick(object sender, RoutedEventArgs e)
    {
        var url = RemoteUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            GitStatusText.Text = "Enter a remote repository URL before connecting.";
            return;
        }

        GitStatusText.Text = "Connecting to the remote repository…";
        SetGitButtonsEnabled(false);

        RunGit(
            () => _services.Git.ConnectToRemote(url),
            result =>
            {
                AppendGitOutput(result.Output);
                GitStatusText.Text = result.Success
                    ? "Connected and pushed the initial commit."
                    : "Could not connect to the remote repository — see the log below.";
                SetGitButtonsEnabled(true);
                RefreshGitState();
            });
    }

    private void OnBackupClick(object sender, RoutedEventArgs e)
    {
        GitStatusText.Text = "Committing and pushing…";
        SetGitButtonsEnabled(false);

        RunGit(
            () => _services.Git.CommitAndPush(onStatusUpdate: message =>
                Dispatcher.Invoke(() => GitStatusText.Text = message)),
            result =>
            {
                AppendGitOutput(result.Output);
                GitStatusText.Text = result switch
                {
                    { Success: true } => "Changes committed and pushed.",
                    { Conflict: true } => "A merge conflict is still unresolved — nothing was pushed.",
                    _ => "Commit/push failed — see the log below.",
                };
                SetGitButtonsEnabled(true);
                _services.RaiseDataChanged();
            });
    }

    private void OnPullClick(object sender, RoutedEventArgs e)
    {
        GitStatusText.Text = "Pulling from the remote…";
        SetGitButtonsEnabled(false);

        RunGit(
            () => _services.Git.Pull(message => Dispatcher.Invoke(() => GitStatusText.Text = message)),
            result =>
            {
                AppendGitOutput(result.Output);
                GitStatusText.Text = result.Success ? "Up to date with the remote." : "Pull failed — see the log below.";
                SetGitButtonsEnabled(true);
                _services.RaiseDataChanged();
            });
    }

    private void OnAutoBackupChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _services.Settings.AutoBackupEnabled = AutoBackupCheck.IsChecked == true;
        _services.SettingsStore.Save(_services.Settings);
        _tray.ApplyAutoBackupSettings();
    }

    private void OnApplyAutoBackupClick(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(AutoBackupIntervalBox.Text.Trim(), out var minutes) && minutes >= 5)
        {
            _services.Settings.AutoBackupIntervalMinutes = Math.Min(minutes, 24 * 60);
        }
        else
        {
            GitStatusText.Text = "Enter an interval of at least 5 minutes.";
            return;
        }

        AutoBackupIntervalBox.Text = _services.Settings.AutoBackupIntervalMinutes.ToString(CultureInfo.CurrentCulture);
        _services.SettingsStore.Save(_services.Settings);
        _tray.ApplyAutoBackupSettings();
        GitStatusText.Text = $"Automatic backups every {_services.Settings.AutoBackupIntervalMinutes} minutes.";
    }

    private void SetGitButtonsEnabled(bool enabled)
    {
        ConnectButton.IsEnabled = enabled;
        BackupButton.IsEnabled = enabled;
        PullButton.IsEnabled = enabled;
    }

    private void AppendGitOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        GitOutputBox.AppendText($"[{stamp}] {output.Trim()}{Environment.NewLine}");
        GitOutputBox.ScrollToEnd();
    }

    /// <summary>Runs a git call off the UI thread and hands the result back on it.</summary>
    private void RunGit<T>(Func<T> work, Action<T> onCompleted)
    {
        Task.Run(work).ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    AppendGitOutput(task.Exception?.GetBaseException().Message ?? "git failed.");
                    GitStatusText.Text = "The git command failed unexpectedly.";
                    SetGitButtonsEnabled(true);
                    return;
                }

                onCompleted(task.Result);
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ----------------------------------------------------------------- settings tab

    private void LoadSettingsIntoUi()
    {
        var settings = _services.Settings;

        ShowDollarsCheck.IsChecked = settings.ShowInDollars;
        CreditsPerDollarBox.Text = settings.SafeCreditsPerDollar.ToString("0.####", CultureInfo.CurrentCulture);
        StartWithWindowsCheck.IsChecked = StartupManager.IsEnabled();
        ShowDashboardOnStartupCheck.IsChecked = settings.ShowDashboardOnStartup;
        NotifyCheck.IsChecked = settings.NotifyOnBudgetThresholds;
        ThresholdsBox.Text = string.Join(", ", settings.BudgetNotificationThresholds);
        HotkeyCheck.IsChecked = settings.QuickLogHotkeyEnabled;
        HotkeyModifiersBox.Text = settings.QuickLogHotkeyModifiers;
        HotkeyKeyBox.Text = settings.QuickLogHotkeyKey;
        AutoBackupCheck.IsChecked = settings.AutoBackupEnabled;
        AutoBackupIntervalBox.Text = settings.AutoBackupIntervalMinutes.ToString(CultureInfo.CurrentCulture);

        UpdateHotkeyStatus();
    }

    private void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        var settings = _services.Settings;
        var problems = new List<string>();

        settings.ShowInDollars = ShowDollarsCheck.IsChecked == true;

        if (double.TryParse(CreditsPerDollarBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var rate) &&
            double.IsFinite(rate) && rate > 0)
        {
            settings.CreditsPerDollar = rate;
        }
        else
        {
            problems.Add("credits per dollar must be a positive number");
        }

        settings.ShowDashboardOnStartup = ShowDashboardOnStartupCheck.IsChecked == true;
        settings.NotifyOnBudgetThresholds = NotifyCheck.IsChecked == true;

        var thresholds = ThresholdsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : -1)
            .Where(value => value is > 0 and <= 1000)
            .Distinct()
            .OrderBy(value => value)
            .ToList();

        if (thresholds.Count > 0)
        {
            settings.BudgetNotificationThresholds = thresholds;
        }
        else if (settings.NotifyOnBudgetThresholds)
        {
            problems.Add("thresholds must be one or more percentages");
        }

        ThresholdsBox.Text = string.Join(", ", settings.BudgetNotificationThresholds);

        settings.QuickLogHotkeyEnabled = HotkeyCheck.IsChecked == true;
        settings.QuickLogHotkeyModifiers = HotkeyModifiersBox.Text.Trim();
        settings.QuickLogHotkeyKey = HotkeyKeyBox.Text.Trim();

        var wantStartup = StartWithWindowsCheck.IsChecked == true;
        if (wantStartup != StartupManager.IsEnabled() && !StartupManager.SetEnabled(wantStartup))
        {
            problems.Add("Windows refused the startup change");
        }

        StartWithWindowsCheck.IsChecked = StartupManager.IsEnabled();

        if (!_tray.ApplyHotKeySettings())
        {
            problems.Add(_tray.HotKeys.LastError ?? "the hotkey could not be registered");
        }

        _services.SaveSettings();
        UpdateHotkeyStatus();

        SettingsStatusText.Text = problems.Count == 0
            ? "Settings saved."
            : "Saved, but: " + string.Join("; ", problems) + ".";
    }

    private void UpdateHotkeyStatus()
    {
        var settings = _services.Settings;

        if (!settings.QuickLogHotkeyEnabled)
        {
            HotkeyStatusText.Text = "The shortcut is turned off.";
            return;
        }

        var described = HotKeyManager.Describe(settings.QuickLogHotkeyModifiers, settings.QuickLogHotkeyKey);
        HotkeyStatusText.Text = _tray.HotKeys.IsRegistered
            ? $"{described} opens the quick-log box from anywhere."
            : $"{described} is not active: {_tray.HotKeys.LastError ?? "unknown reason"}";
    }

    // ----------------------------------------------------------------- shared

    private static bool TryParseAmount(string text, out double amount)
    {
        text = text.Trim();

        var parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out amount) ||
                     double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out amount);

        return parsed && double.IsFinite(amount) && amount > 0;
    }

    private void TryApplyIcon()
    {
        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        }
        catch (Exception)
        {
            // A missing window icon is cosmetic; never let it stop the window opening.
        }
    }
}
