using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CreditPincher.App.Theming
{
    /// <summary>
    /// Paints the window's title bar to match the theme.
    ///
    /// WPF only styles the client area; the caption is drawn by the desktop window
    /// manager, so a dark window with a white title bar looks broken. Windows 10 build
    /// 19041 and later accept <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> to flip it.
    /// </summary>
    public static class TitleBarTheme
    {
        private const int UseImmersiveDarkMode = 20;
        private const int UseImmersiveDarkModeBefore20H1 = 19;

        /// <summary>
        /// Keeps <paramref name="window"/>'s title bar in step with the current theme,
        /// now and after every theme change, for as long as the window is open.
        /// </summary>
        public static void Attach(Window window)
        {
            EventHandler onThemeChanged = (sender, args) => Apply(window);

            // The handle only exists once the window is sourced.
            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            {
                Apply(window);
            }
            else
            {
                window.SourceInitialized += (sender, args) => Apply(window);
            }

            ThemeManager.Changed += onThemeChanged;
            window.Closed += (sender, args) => ThemeManager.Changed -= onThemeChanged;
        }

        public static void Apply(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                var dark = ThemeManager.Current.IsDark ? 1 : 0;

                // Older builds used attribute 19 for the same flag; setting both is harmless.
                if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));
                }

                // The caption only repaints on a state change, so nudge it.
                if (window.IsVisible)
                {
                    window.Visibility = Visibility.Hidden;
                    window.Visibility = Visibility.Visible;
                }
            }
            catch (Exception)
            {
                // A stubborn title bar is cosmetic; never let it break the window.
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
