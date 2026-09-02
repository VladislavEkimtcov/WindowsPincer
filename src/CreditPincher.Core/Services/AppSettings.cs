using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CreditPincher.Core.Compat;

namespace CreditPincher.Core.Services
{
    /// <summary>
    /// Tray-app preferences. These are deliberately kept out of the storage directory:
    /// that folder is shared with the IDE plugin (and often git-synced), while these
    /// settings are per-machine.
    /// </summary>
    public sealed class AppSettings
    {
        private List<int> _budgetNotificationThresholds = new List<int> { 80, 100 };

        /// <summary>Show money instead of raw credits throughout the UI.</summary>
        public bool ShowInDollars { get; set; }

        /// <summary>How many credits equal one dollar. The plugin hard-coded 100.</summary>
        public double CreditsPerDollar { get; set; }

        /// <summary>Pop a tray notification as budget thresholds are crossed.</summary>
        public bool NotifyOnBudgetThresholds { get; set; }

        /// <summary>Percentages of the month's budget that trigger a notification.</summary>
        public List<int> BudgetNotificationThresholds
        {
            get { return _budgetNotificationThresholds; }
            set { _budgetNotificationThresholds = value ?? new List<int>(); }
        }

        /// <summary>Highest threshold already announced, and the month it was announced for.</summary>
        public int LastNotifiedThreshold { get; set; }

        public string LastNotifiedMonth { get; set; }

        /// <summary>Commit and push automatically on the interval below.</summary>
        public bool AutoBackupEnabled { get; set; }

        public int AutoBackupIntervalMinutes { get; set; }

        /// <summary>Pull from the remote automatically on the interval below.</summary>
        public bool AutoPullEnabled { get; set; }

        public int AutoPullIntervalMinutes { get; set; }

        /// <summary>Register a system-wide hotkey that opens the quick-log box.</summary>
        public bool QuickLogHotkeyEnabled { get; set; }

        /// <summary>Modifier names joined with '+', e.g. "Ctrl+Alt".</summary>
        public string QuickLogHotkeyModifiers { get; set; }

        /// <summary>Key name from <c>System.Windows.Input.Key</c>, e.g. "U".</summary>
        public string QuickLogHotkeyKey { get; set; }

        /// <summary>Open the dashboard when the app starts, instead of staying in the tray.</summary>
        public bool ShowDashboardOnStartup { get; set; }

        /// <summary>Colour scheme key: "light", "dark", "xbox" or "zune".</summary>
        public string Theme { get; set; }

        public AppSettings()
        {
            // C# 5 has no property initialisers, so the non-zero defaults live here.
            CreditsPerDollar = 100.0;
            Theme = "light";
            NotifyOnBudgetThresholds = true;
            AutoBackupIntervalMinutes = 60;
            AutoPullIntervalMinutes = 60;
            QuickLogHotkeyEnabled = true;
            QuickLogHotkeyModifiers = "Ctrl+Alt";
            QuickLogHotkeyKey = "U";
        }

        public double SafeCreditsPerDollar
        {
            get { return MathEx.IsFinite(CreditsPerDollar) && CreditsPerDollar > 0 ? CreditsPerDollar : 100.0; }
        }
    }

    /// <summary>Loads and saves <see cref="AppSettings"/> as JSON under %APPDATA%.</summary>
    public sealed class SettingsStore
    {
        private readonly object _lock = new object();

        public SettingsStore()
            : this(null)
        {
        }

        public SettingsStore(string settingsPath)
        {
            SettingsPath = settingsPath ?? DefaultSettingsPath();
        }

        public string SettingsPath { get; private set; }

        public static string DefaultSettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CreditPincher",
                "settings.json");
        }

        public AppSettings Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(SettingsPath))
                    {
                        return new AppSettings();
                    }

                    return FromJson(File.ReadAllText(SettingsPath));
                }
                catch (Exception)
                {
                    // A corrupt settings file must never stop the app from starting.
                    return new AppSettings();
                }
            }
        }

        public void Save(AppSettings settings)
        {
            lock (_lock)
            {
                try
                {
                    var directory = Path.GetDirectoryName(SettingsPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(SettingsPath, ToJson(settings));
                }
                catch (Exception)
                {
                    // Losing a preference is not worth crashing over.
                }
            }
        }

        /// <summary>
        /// Reads the settings object. Anything missing or of the wrong shape keeps its
        /// default, which is what makes hand-editing (and older files) safe.
        /// </summary>
        internal static AppSettings FromJson(string json)
        {
            var members = SimpleJson.Parse(json);
            var settings = new AppSettings();

            settings.ShowInDollars = ReadBool(members, "ShowInDollars", settings.ShowInDollars);
            settings.CreditsPerDollar = ReadDouble(members, "CreditsPerDollar", settings.CreditsPerDollar);
            settings.NotifyOnBudgetThresholds =
                ReadBool(members, "NotifyOnBudgetThresholds", settings.NotifyOnBudgetThresholds);
            settings.LastNotifiedThreshold = ReadInt(members, "LastNotifiedThreshold", settings.LastNotifiedThreshold);
            settings.LastNotifiedMonth = ReadString(members, "LastNotifiedMonth", settings.LastNotifiedMonth);
            settings.AutoBackupEnabled = ReadBool(members, "AutoBackupEnabled", settings.AutoBackupEnabled);
            settings.AutoBackupIntervalMinutes =
                ReadInt(members, "AutoBackupIntervalMinutes", settings.AutoBackupIntervalMinutes);
            settings.AutoPullEnabled = ReadBool(members, "AutoPullEnabled", settings.AutoPullEnabled);
            settings.AutoPullIntervalMinutes =
                ReadInt(members, "AutoPullIntervalMinutes", settings.AutoPullIntervalMinutes);
            settings.QuickLogHotkeyEnabled = ReadBool(members, "QuickLogHotkeyEnabled", settings.QuickLogHotkeyEnabled);
            settings.QuickLogHotkeyModifiers =
                ReadString(members, "QuickLogHotkeyModifiers", settings.QuickLogHotkeyModifiers);
            settings.QuickLogHotkeyKey = ReadString(members, "QuickLogHotkeyKey", settings.QuickLogHotkeyKey);
            settings.ShowDashboardOnStartup =
                ReadBool(members, "ShowDashboardOnStartup", settings.ShowDashboardOnStartup);
            settings.Theme = ReadString(members, "Theme", settings.Theme);

            object thresholds;
            if (members.TryGetValue("BudgetNotificationThresholds", out thresholds))
            {
                var items = thresholds as List<object>;
                if (items != null)
                {
                    var parsed = new List<int>();
                    foreach (var item in items)
                    {
                        if (item is double)
                        {
                            parsed.Add((int)Math.Round((double)item));
                        }
                    }

                    if (parsed.Count > 0)
                    {
                        settings.BudgetNotificationThresholds = parsed;
                    }
                }
            }

            return settings;
        }

        internal static string ToJson(AppSettings settings)
        {
            var members = new List<KeyValuePair<string, object>>
            {
                Member("ShowInDollars", settings.ShowInDollars),
                Member("CreditsPerDollar", settings.CreditsPerDollar),
                Member("NotifyOnBudgetThresholds", settings.NotifyOnBudgetThresholds),
                Member("BudgetNotificationThresholds", settings.BudgetNotificationThresholds),
                Member("LastNotifiedThreshold", settings.LastNotifiedThreshold),
                Member("LastNotifiedMonth", settings.LastNotifiedMonth),
                Member("AutoBackupEnabled", settings.AutoBackupEnabled),
                Member("AutoBackupIntervalMinutes", settings.AutoBackupIntervalMinutes),
                Member("AutoPullEnabled", settings.AutoPullEnabled),
                Member("AutoPullIntervalMinutes", settings.AutoPullIntervalMinutes),
                Member("QuickLogHotkeyEnabled", settings.QuickLogHotkeyEnabled),
                Member("QuickLogHotkeyModifiers", settings.QuickLogHotkeyModifiers),
                Member("QuickLogHotkeyKey", settings.QuickLogHotkeyKey),
                Member("ShowDashboardOnStartup", settings.ShowDashboardOnStartup),
                Member("Theme", settings.Theme),
            };

            return SimpleJson.Write(members);
        }

        private static KeyValuePair<string, object> Member(string name, object value)
        {
            return new KeyValuePair<string, object>(name, value);
        }

        private static bool ReadBool(IDictionary<string, object> members, string name, bool fallback)
        {
            object value;
            return members.TryGetValue(name, out value) && value is bool ? (bool)value : fallback;
        }

        private static double ReadDouble(IDictionary<string, object> members, string name, double fallback)
        {
            object value;
            return members.TryGetValue(name, out value) && value is double ? (double)value : fallback;
        }

        private static int ReadInt(IDictionary<string, object> members, string name, int fallback)
        {
            object value;
            return members.TryGetValue(name, out value) && value is double
                ? (int)Math.Round((double)value)
                : fallback;
        }

        private static string ReadString(IDictionary<string, object> members, string name, string fallback)
        {
            object value;
            return members.TryGetValue(name, out value) && value is string ? (string)value : fallback;
        }
    }
}
