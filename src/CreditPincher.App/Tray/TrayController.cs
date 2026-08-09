using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CreditPincher.App.Platform;
using CreditPincher.App.Theming;
using CreditPincher.App.Views;
using CreditPincher.Core.Compat;
using CreditPincher.Core.Models;
using CreditPincher.Core.Services;
using Forms = System.Windows.Forms;

namespace CreditPincher.App.Tray
{
    /// <summary>
    /// Owns the notification-area icon: its menu, its tooltip, the budget balloons,
    /// the periodic auto-backup, and the windows it opens. This is effectively the
    /// application shell — the plugin's tool window becomes a tray icon here.
    /// </summary>
    public sealed class TrayController : IDisposable
    {
        private readonly AppServices _services;
        private readonly HotKeyManager _hotKeys = new HotKeyManager();
        private readonly Forms.NotifyIcon _notifyIcon;
        private readonly Forms.ToolStripMenuItem _backupItem;
        private readonly Forms.ToolStripMenuItem _startupItem;
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _autoBackupTimer;
        private readonly EventHandler _themeChangedHandler;

        private DashboardWindow _dashboard;
        private QuickLogWindow _quickLog;
        private IntPtr _currentIconHandle;
        private bool _backupInProgress;
        private bool _disposed;

        public TrayController(AppServices services)
        {
            _services = services;

            _backupItem = new Forms.ToolStripMenuItem("&Back up now", null, (sender, args) => BackupNow());
            _startupItem = new Forms.ToolStripMenuItem("Start with &Windows", null, (sender, args) => ToggleStartup())
            {
                CheckOnClick = false,
                Checked = StartupManager.IsEnabled(),
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(new Forms.ToolStripMenuItem("&Log usage…", null, (sender, args) => ShowQuickLog())
            {
                // Fully qualified: System.Windows also defines a FontStyle.
                Font = new Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold),
            });
            menu.Items.Add(new Forms.ToolStripMenuItem("Open &dashboard", null, (sender, args) => ShowDashboard()));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_backupItem);
            menu.Items.Add(new Forms.ToolStripMenuItem("Open storage &folder", null, (sender, args) => OpenStorageFolder()));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_startupItem);
            menu.Items.Add(new Forms.ToolStripMenuItem(
                "&Settings…", null, (sender, args) => ShowDashboard(DashboardWindow.SettingsTabIndex)));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(new Forms.ToolStripMenuItem("E&xit", null, (sender, args) => ExitApplication()));

            _notifyIcon = new Forms.NotifyIcon
            {
                ContextMenuStrip = menu,
                Visible = true,
                Text = "CreditPincher",
            };

            _notifyIcon.MouseClick += OnTrayMouseClick;
            _notifyIcon.DoubleClick += (sender, args) => ShowDashboard();
            _notifyIcon.BalloonTipClicked += (sender, args) => ShowDashboard();

            _services.DataChanged += (sender, args) => Refresh();

            // The tray icon is drawn by us, not by WPF, so it has to be redrawn by hand
            // when the palette changes.
            _themeChangedHandler = (sender, args) => Refresh();
            ThemeManager.Changed += _themeChangedHandler;

            // Cheap safety net: catches midnight rollover and month changes even if
            // nothing was logged, so the tooltip never goes stale.
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMinutes(5),
            };
            _refreshTimer.Tick += (sender, args) => Refresh();
            _refreshTimer.Start();

            _autoBackupTimer = new DispatcherTimer(DispatcherPriority.Background);
            _autoBackupTimer.Tick += (sender, args) => BackupNow(true);
            ApplyAutoBackupSettings();

            ApplyHotKeySettings();
            _hotKeys.Pressed += ShowQuickLog;

            Refresh();
        }

        public HotKeyManager HotKeys
        {
            get { return _hotKeys; }
        }

        public void ShowDashboard()
        {
            ShowDashboard(0);
        }

        /// <summary>Opens (or focuses) the dashboard, optionally on a specific tab.</summary>
        public void ShowDashboard(int tabIndex)
        {
            if (_dashboard == null)
            {
                _dashboard = new DashboardWindow(_services, this);
                _dashboard.Closed += (sender, args) => _dashboard = null;
            }

            _dashboard.SelectTab(tabIndex);
            ShowAndActivate(_dashboard);
        }

        /// <summary>Opens the compact "how many credits?" box.</summary>
        public void ShowQuickLog()
        {
            if (_quickLog == null)
            {
                _quickLog = new QuickLogWindow(_services);
                _quickLog.Closed += (sender, args) => _quickLog = null;
            }

            ShowAndActivate(_quickLog);
        }

        public void BackupNow()
        {
            BackupNow(false);
        }

        /// <summary>Commits and pushes the storage directory, reporting through the tray.</summary>
        public void BackupNow(bool silent)
        {
            if (_backupInProgress)
            {
                return;
            }

            var git = _services.Git;
            _backupInProgress = true;
            _backupItem.Enabled = false;

            Task.Factory.StartNew(() =>
            {
                if (!git.IsGitRepository())
                {
                    return new GitBackupService.GitResult(
                        false, "The storage folder is not connected to a git remote yet.");
                }

                return git.CommitAndPush();
            }).ContinueWith(
                task =>
                {
                    _backupInProgress = false;
                    _backupItem.Enabled = true;

                    var result = task.IsFaulted
                        ? new GitBackupService.GitResult(false, DescribeFault(task))
                        : task.Result;

                    if (result.Success)
                    {
                        if (!silent)
                        {
                            ShowBalloon(
                                "Backup complete",
                                "Your usage log was pushed to the remote repository.",
                                Forms.ToolTipIcon.Info);
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
                },
                TaskScheduler.FromCurrentSynchronizationContext());
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
            if (alert.HasValue)
            {
                var due = alert.Value;
                _services.SettingsStore.Save(_services.Settings);
                var used = formatter.Percent(due.UsedPercent);

                ShowBalloon(
                    due.ThresholdPercent >= 100
                        ? "Monthly budget reached"
                        : due.ThresholdPercent + "% of budget used",
                    formatter.Credits(due.TotalCredits) + " of " + formatter.Credits(due.MonthlyBudget) +
                    " used this month (" + used + ").",
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

            var minutes = MathEx.Clamp(_services.Settings.AutoBackupIntervalMinutes, 5, 24 * 60);
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
            ThemeManager.Changed -= _themeChangedHandler;
            _refreshTimer.Stop();
            _autoBackupTimer.Stop();
            _hotKeys.Dispose();

            _notifyIcon.Visible = false;

            if (_notifyIcon.Icon != null)
            {
                _notifyIcon.Icon.Dispose();
            }

            if (_notifyIcon.ContextMenuStrip != null)
            {
                _notifyIcon.ContextMenuStrip.Dispose();
            }

            _notifyIcon.Dispose();

            if (_currentIconHandle != IntPtr.Zero)
            {
                TrayIconRenderer.DestroyIcon(_currentIconHandle);
                _currentIconHandle = IntPtr.Zero;
            }
        }

        private void OnTrayMouseClick(object sender, Forms.MouseEventArgs e)
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
            var smallIconWidth = Forms.SystemInformation.SmallIconSize.Width;
            int size;
            if (smallIconWidth <= 16)
            {
                size = 16;
            }
            else if (smallIconWidth <= 24)
            {
                size = 24;
            }
            else if (smallIconWidth <= 32)
            {
                size = 32;
            }
            else
            {
                size = 48;
            }

            var previousIcon = _notifyIcon.Icon;
            var previousHandle = _currentIconHandle;

            var theme = ThemeManager.Current.TrayNeutralColor;
            var neutral = System.Drawing.Color.FromArgb(theme.R, theme.G, theme.B);

            _notifyIcon.Icon = TrayIconRenderer.Create(status, size, neutral, out _currentIconHandle);

            if (previousIcon != null)
            {
                previousIcon.Dispose();
            }

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
                ShowBalloon(
                    "Could not change startup",
                    "Windows refused the change to the per-user Run key.",
                    Forms.ToolTipIcon.Error);
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

        private static string DescribeFault(Task task)
        {
            if (task.Exception == null)
            {
                return "Backup failed.";
            }

            return task.Exception.GetBaseException().Message ?? "Backup failed.";
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
            var candidate = text
                .Split('\n')
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));

            var line = candidate == null ? "Unknown error." : candidate.Trim();
            return line.Length <= 200 ? line : line.Substring(0, 200);
        }
    }
}
