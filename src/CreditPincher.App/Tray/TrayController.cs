using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using CreditPincher.App.Platform;
using CreditPincher.App.Views;
using CreditPincher.Core.Models;
using CreditPincher.Core.Services;
using Forms = System.Windows.Forms;

namespace CreditPincher.App.Tray;

/// <summary>
/// Owns the notification-area icon: its menu, its tooltip, the budget balloons,
/// the periodic auto-backup, and the windows it opens. This is effectively the
/// application shell — the plugin's tool window becomes a tray icon here.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly AppServices _services;
    private readonly HotKeyManager _hotKeys = new();
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _backupItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _autoBackupTimer;

    private DashboardWindow? _dashboard;
    private QuickLogWindow? _quickLog;
    private IntPtr _currentIconHandle;
    private bool _backupInProgress;
    private bool _disposed;

    public TrayController(AppServices services)
    {
        _services = services;

        _backupItem = new Forms.ToolStripMenuItem("&Back up now", null, (_, _) => BackupNow());
        _startupItem = new Forms.ToolStripMenuItem("Start with &Windows", null, (_, _) => ToggleStartup())
        {
            CheckOnClick = false,
            Checked = StartupManager.IsEnabled(),
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("&Log usage…", null, (_, _) => ShowQuickLog())
        {
            Font = new Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold),
        });
        menu.Items.Add(new Forms.ToolStripMenuItem("Open &dashboard", null, (_, _) => ShowDashboard()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_backupItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Open storage &folder", null, (_, _) => OpenStorageFolder()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("&Settings…", null, (_, _) => ShowDashboard(DashboardWindow.SettingsTabIndex)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("E&xit", null, (_, _) => ExitApplication()));

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "CreditPincher",
        };

        _notifyIcon.MouseClick += OnTrayMouseClick;
        _notifyIcon.DoubleClick += (_, _) => ShowDashboard();
        _notifyIcon.BalloonTipClicked += (_, _) => ShowDashboard();

        _services.DataChanged += (_, _) => Refresh();

        // Cheap safety net: catches midnight rollover and month changes even if
        // nothing was logged, so the tooltip never goes stale.
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        _autoBackupTimer = new DispatcherTimer(DispatcherPriority.Background);
        _autoBackupTimer.Tick += (_, _) => BackupNow(silent: true);
        ApplyAutoBackupSettings();

        ApplyHotKeySettings();
        _hotKeys.Pressed += ShowQuickLog;

        Refresh();
    }

    public HotKeyManager HotKeys => _hotKeys;

    /// <summary>Opens (or focuses) the dashboard, optionally on a specific tab.</summary>
    public void ShowDashboard(int tabIndex = 0)
    {
        if (_dashboard is null)
        {
            _dashboard = new DashboardWindow(_services, this);
            _dashboard.Closed += (_, _) => _dashboard = null;
        }

        _dashboard.SelectTab(tabIndex);
        ShowAndActivate(_dashboard);
    }

    /// <summary>Opens the compact "how many credits?" box.</summary>
    public void ShowQuickLog()
    {
        if (_quickLog is null)
        {
            _quickLog = new QuickLogWindow(_services);
            _quickLog.Closed += (_, _) => _quickLog = null;
        }

        ShowAndActivate(_quickLog);
    }

    /// <summary>Commits and pushes the storage directory, reporting through the tray.</summary>
    public void BackupNow(bool silent = false)
    {
        if (_backupInProgress)
        {
            return;
        }

        var git = _services.Git;
        _backupInProgress = true;
        _backupItem.Enabled = false;

        Task.Run(() =>
        {
            if (!git.IsGitRepository())
            {
                return new GitBackupService.GitResult(false, "The storage folder is not connected to a git remote yet.");
            }

            return git.CommitAndPush();
        }).ContinueWith(task =>
        {
            _backupInProgress = false;
            _backupItem.Enabled = true;

            var result = task.IsFaulted
                ? new GitBackupService.GitResult(false, task.Exception?.GetBaseException().Message ?? "Backup failed.")
                : task.Result;

            if (result.Success)
            {
                if (!silent)
                {
                    ShowBalloon("Backup complete", "Your usage log was pushed to the remote repository.", Forms.ToolTipIcon.Info);
                }
            }
            else if (result.Conflict)
            {
                ShowBalloon(
                    "Backup needs your input",
                    "A merge conflict could not be resolved automatically. Open the dashboard to sort it out.",
                    Forms.ToolTipIcon.Warning);
            }
            else if (!silent)
            {
                ShowBalloon("Backup failed", FirstLine(result.Output), Forms.ToolTipIcon.Error);
            }

            _services.RaiseDataChanged();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Re-reads the log to refresh the icon colour, tooltip, and menu state.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        UsageStats monthToDate;
        try
        {
            monthToDate = _services.MonthToDate();
        }
        catch (Exception)
        {
            // A transient read failure must not take the tray icon down.
            return;
        }

        var formatter = _services.Formatter;
        _notifyIcon.Text = formatter.TrayTooltip(monthToDate);

        var status = TrayIconRenderer.StatusFor(monthToDate.MonthlyBudget, monthToDate.TotalCredits);
        SetIcon(status);

        _startupItem.Checked = StartupManager.IsEnabled();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var alert = BudgetAlertEvaluator.Evaluate(_services.Settings, monthToDate, YearMonth.From(today));
        if (alert is { } due)
        {
            _services.SettingsStore.Save(_services.Settings);
            var used = formatter.Percent(due.UsedPercent);
            ShowBalloon(
                due.ThresholdPercent >= 100 ? "Monthly budget reached" : $"{due.ThresholdPercent}% of budget used",
                $"{formatter.Credits(due.TotalCredits)} of {formatter.Credits(due.MonthlyBudget)} used this month ({used}).",
                due.ThresholdPercent >= 100 ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        }
    }

    /// <summary>Applies the auto-backup interval after it changes in Settings.</summary>
    public void ApplyAutoBackupSettings()
    {
        _autoBackupTimer.Stop();

        if (!_services.Settings.AutoBackupEnabled)
        {
            return;
        }

        var minutes = Math.Clamp(_services.Settings.AutoBackupIntervalMinutes, 5, 24 * 60);
        _autoBackupTimer.Interval = TimeSpan.FromMinutes(minutes);
        _autoBackupTimer.Start();
    }

    /// <summary>Applies the hotkey preference after it changes in Settings.</summary>
    public bool ApplyHotKeySettings()
    {
        if (!_services.Settings.QuickLogHotkeyEnabled)
        {
            _hotKeys.Unregister();
            return true;
        }

        return _hotKeys.Register(
            _services.Settings.QuickLogHotkeyModifiers,
            _services.Settings.QuickLogHotkeyKey);
    }

    public void ShowBalloon(string title, string message, Forms.ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(6000);
    }

    public void ExitApplication()
    {
        _notifyIcon.Visible = false;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Stop();
        _autoBackupTimer.Stop();
        _hotKeys.Dispose();

        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();

        if (_currentIconHandle != IntPtr.Zero)
        {
            TrayIconRenderer.DestroyIcon(_currentIconHandle);
            _currentIconHandle = IntPtr.Zero;
        }
    }

    private void OnTrayMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        // Left click opens the dashboard; middle click is the fast path to logging.
        if (e.Button == Forms.MouseButtons.Left)
        {
            ShowDashboard();
        }
        else if (e.Button == Forms.MouseButtons.Middle)
        {
            ShowQuickLog();
        }
    }

    private void SetIcon(TrayStatus status)
    {
        var size = Forms.SystemInformation.SmallIconSize.Width switch
        {
            <= 16 => 16,
            <= 24 => 24,
            <= 32 => 32,
            _ => 48,
        };

        var previousIcon = _notifyIcon.Icon;
        var previousHandle = _currentIconHandle;

        _notifyIcon.Icon = TrayIconRenderer.Create(status, size, out _currentIconHandle);

        previousIcon?.Dispose();
        if (previousHandle != IntPtr.Zero)
        {
            TrayIconRenderer.DestroyIcon(previousHandle);
        }
    }

    private void ToggleStartup()
    {
        var enable = !StartupManager.IsEnabled();
        if (!StartupManager.SetEnabled(enable))
        {
            ShowBalloon("Could not change startup", "Windows refused the change to the per-user Run key.", Forms.ToolTipIcon.Error);
        }

        _startupItem.Checked = StartupManager.IsEnabled();
        _services.RaiseDataChanged();
    }

    private void OpenStorageFolder()
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
            ShowBalloon("Could not open the folder", exception.Message, Forms.ToolTipIcon.Error);
        }
    }

    private static void ShowAndActivate(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        // A brief topmost flip is the reliable way to pull a window in front of whatever
        // the user was doing; the original setting is put back so a pinned window stays pinned.
        var wasTopmost = window.Topmost;
        window.Activate();
        window.Topmost = true;
        window.Topmost = wasTopmost;
        window.Focus();
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n').FirstOrDefault(part => !string.IsNullOrWhiteSpace(part))?.Trim() ?? "Unknown error.";
        return line.Length <= 200 ? line : line[..200];
    }
}
