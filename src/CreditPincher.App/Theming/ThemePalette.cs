using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CreditPincher.App.Theming
{
    /// <summary>
    /// One colour scheme. Every field maps to a brush resource of the same name in
    /// App.xaml; the light values there are the design-time defaults and are replaced
    /// at runtime by <see cref="ThemeManager"/>.
    /// </summary>
    public sealed class ThemePalette
    {
        public string Key { get; set; }

        public string Label { get; set; }

        /// <summary>Drives the DWM dark title bar, so the window frame matches the content.</summary>
        public bool IsDark { get; set; }

        public string WindowBackgroundBrush { get; set; }
        public string CardBrush { get; set; }
        public string LineBrush { get; set; }
        public string TextPrimaryBrush { get; set; }
        public string TextSecondaryBrush { get; set; }

        public string AccentBrush { get; set; }
        public string AccentHoverBrush { get; set; }
        public string AccentSoftBrush { get; set; }

        /// <summary>Text drawn on top of <see cref="AccentBrush"/>.</summary>
        public string AccentForegroundBrush { get; set; }

        public string ControlBackgroundBrush { get; set; }
        public string ControlHoverBrush { get; set; }
        public string SelectionBrush { get; set; }
        public string ScrollThumbBrush { get; set; }

        public string OkBrush { get; set; }
        public string WarnBrush { get; set; }
        public string DangerBrush { get; set; }

        public string ChartGridBrush { get; set; }
        public string ChartAxisBrush { get; set; }
        public string ChartBarBrush { get; set; }
        public string ChartBarHoverBrush { get; set; }

        /// <summary>Tray icon background when no budget is set — the theme's own colour.</summary>
        public string TrayNeutral { get; set; }

        /// <summary>Every brush resource this palette defines, keyed by resource name.</summary>
        public IEnumerable<KeyValuePair<string, string>> Brushes()
        {
            yield return Entry("WindowBackgroundBrush", WindowBackgroundBrush);
            yield return Entry("CardBrush", CardBrush);
            yield return Entry("LineBrush", LineBrush);
            yield return Entry("TextPrimaryBrush", TextPrimaryBrush);
            yield return Entry("TextSecondaryBrush", TextSecondaryBrush);
            yield return Entry("AccentBrush", AccentBrush);
            yield return Entry("AccentHoverBrush", AccentHoverBrush);
            yield return Entry("AccentSoftBrush", AccentSoftBrush);
            yield return Entry("AccentForegroundBrush", AccentForegroundBrush);
            yield return Entry("ControlBackgroundBrush", ControlBackgroundBrush);
            yield return Entry("ControlHoverBrush", ControlHoverBrush);
            yield return Entry("SelectionBrush", SelectionBrush);
            yield return Entry("ScrollThumbBrush", ScrollThumbBrush);
            yield return Entry("OkBrush", OkBrush);
            yield return Entry("WarnBrush", WarnBrush);
            yield return Entry("DangerBrush", DangerBrush);
            yield return Entry("ChartGridBrush", ChartGridBrush);
            yield return Entry("ChartAxisBrush", ChartAxisBrush);
            yield return Entry("ChartBarBrush", ChartBarBrush);
            yield return Entry("ChartBarHoverBrush", ChartBarHoverBrush);
        }

        public Color TrayNeutralColor
        {
            get { return Parse(TrayNeutral); }
        }

        public static Color Parse(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static KeyValuePair<string, string> Entry(string name, string hex)
        {
            return new KeyValuePair<string, string>(name, hex);
        }
    }
}
