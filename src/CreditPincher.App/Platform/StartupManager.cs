using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace CreditPincher.App.Platform
{
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
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    var value = key.GetValue(ValueName) as string;
                    return value != null && value.Length > 0;
                }
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
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                                 ?? Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (!enabled)
                    {
                        key.DeleteValue(ValueName, false);
                        return true;
                    }

                    var executablePath = ExecutablePath();
                    if (string.IsNullOrEmpty(executablePath))
                    {
                        return false;
                    }

                    // --tray keeps the auto-start silent even when "open dashboard on startup" is on.
                    key.SetValue(ValueName, "\"" + executablePath + "\" --tray", RegistryValueKind.String);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Path of the running executable. .NET Framework has no
        /// <c>Environment.ProcessPath</c>, so this reads the process' main module.
        /// </summary>
        public static string ExecutablePath()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    return process.MainModule.FileName;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
