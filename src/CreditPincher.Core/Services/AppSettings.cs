using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreditPincher.Core.Services;

/// <summary>
/// Tray-app preferences. These are deliberately kept out of the storage directory:
/// that folder is shared with the IDE plugin (and often git-synced), while these
/// settings are per-machine.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Show money instead of raw credits throughout the UI.</summary>
    public bool ShowInDollars { get; set; }

    /// <summary>How many credits equal one dollar. The plugin hard-coded 100.</summary>
    public double CreditsPerDollar { get; set; } = 100.0;

    /// <summary>Pop a tray notification as budget thresholds are crossed.</summary>
    public bool NotifyOnBudgetThresholds { get; set; } = true;

    /// <summary>Percentages of the month's budget that trigger a notification.</summary>
    public List<int> BudgetNotificationThresholds { get; set; } = [80, 100];

    /// <summary>Highest threshold already announced, and the month it was announced for.</summary>
    public int LastNotifiedThreshold { get; set; }

    public string? LastNotifiedMonth { get; set; }

    /// <summary>Commit and push automatically on the interval below.</summary>
    public bool AutoBackupEnabled { get; set; }

    public int AutoBackupIntervalMinutes { get; set; } = 60;

    /// <summary>Register a system-wide hotkey that opens the quick-log box.</summary>
    public bool QuickLogHotkeyEnabled { get; set; } = true;

    /// <summary>Modifier names joined with '+', e.g. "Ctrl+Alt".</summary>
    public string QuickLogHotkeyModifiers { get; set; } = "Ctrl+Alt";

    /// <summary>Key name from <c>System.Windows.Input.Key</c>, e.g. "U".</summary>
    public string QuickLogHotkeyKey { get; set; } = "U";

    /// <summary>Open the dashboard when the app starts, instead of staying in the tray.</summary>
    public bool ShowDashboardOnStartup { get; set; }

    [JsonIgnore]
    public double SafeCreditsPerDollar =>
        double.IsFinite(CreditsPerDollar) && CreditsPerDollar > 0 ? CreditsPerDollar : 100.0;
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under %APPDATA%.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _lock = new();

    public SettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? DefaultSettingsPath();
    }

    public string SettingsPath { get; }

    public static string DefaultSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CreditPincher",
        "settings.json");

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

                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
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

                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, SerializerOptions));
            }
            catch (Exception)
            {
                // Losing a preference is not worth crashing over.
            }
        }
    }
}
