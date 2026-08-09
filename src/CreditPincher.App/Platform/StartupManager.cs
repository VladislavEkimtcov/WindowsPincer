using Microsoft.Win32;

namespace CreditPincher.App.Platform;

/// <summary>
/// "Start with Windows", implemented as a per-user Run key so it never needs
/// elevation and never touches machine-wide state.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CreditPincher";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Returns true when the registry was updated to match <paramref name="enabled"/>.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executablePath))
            {
                return false;
            }

            // --tray keeps the auto-start silent even when "open dashboard on startup" is on.
            key.SetValue(ValueName, $"\"{executablePath}\" --tray", RegistryValueKind.String);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
