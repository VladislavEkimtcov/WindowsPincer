using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CreditPincher.App.Theming
{
    /// <summary>
    /// The four colour schemes, and the machinery that swaps between them live.
    ///
    /// Every brush in App.xaml is referenced with <c>{DynamicResource}</c>, so replacing
    /// the entries in <c>Application.Current.Resources</c> repaints every open window
    /// without rebuilding any of them. (<c>{StaticResource}</c> would not work here:
    /// WPF freezes Freezable values when it seals a Style, and a frozen brush can be
    /// neither mutated nor re-resolved.)
    /// </summary>
    public static class ThemeManager
    {
        public const string DefaultKey = "light";

        private static readonly ThemePalette[] Palettes =
        {
            new ThemePalette
            {
                Key = "light",
                Label = "Light",
                IsDark = false,
                WindowBackgroundBrush = "#FFF4F5F7",
                CardBrush = "#FFFFFFFF",
                LineBrush = "#FFE0E3E8",
                TextPrimaryBrush = "#FF1F2328",
                TextSecondaryBrush = "#FF666E78",
                AccentBrush = "#FF3574A8",
                AccentHoverBrush = "#FF2C6191",
                AccentSoftBrush = "#FFE8F1F9",
                AccentForegroundBrush = "#FFFFFFFF",
                ControlBackgroundBrush = "#FFFDFDFD",
                ControlHoverBrush = "#FFE8F1F9",
                SelectionBrush = "#FFD8E7F5",
                ScrollThumbBrush = "#FFC2C7CE",
                OkBrush = "#FF2E7D32",
                WarnBrush = "#FFB26B00",
                DangerBrush = "#FFC0392B",
                ChartGridBrush = "#FFE6E8EC",
                ChartAxisBrush = "#FFC8CCD2",
                ChartBarBrush = "#FF3574A8",
                ChartBarHoverBrush = "#FF56A6E2",
                TrayNeutral = "#FF3574A8",
            },
            new ThemePalette
            {
                Key = "dark",
                Label = "Dark",
                IsDark = true,
                WindowBackgroundBrush = "#FF1B1B1F",
                CardBrush = "#FF27272C",
                LineBrush = "#FF3A3A42",
                TextPrimaryBrush = "#FFF2F2F4",
                TextSecondaryBrush = "#FF9A9AA4",
                AccentBrush = "#FF3B8BD6",
                AccentHoverBrush = "#FF57A2E8",
                AccentSoftBrush = "#FF223243",
                AccentForegroundBrush = "#FFFFFFFF",
                ControlBackgroundBrush = "#FF32323A",
                ControlHoverBrush = "#FF3E3E48",
                SelectionBrush = "#FF35506B",
                ScrollThumbBrush = "#FF4A4A55",
                OkBrush = "#FF4CAF50",
                WarnBrush = "#FFE0A03A",
                DangerBrush = "#FFE5544A",
                ChartGridBrush = "#FF32323A",
                ChartAxisBrush = "#FF4A4A55",
                ChartBarBrush = "#FF3B8BD6",
                ChartBarHoverBrush = "#FF6FB6EE",
                TrayNeutral = "#FF3B8BD6",
            },
            new ThemePalette
            {
                // The 360-era blade green on near-black.
                Key = "xbox",
                Label = "Xbox",
                IsDark = true,
                WindowBackgroundBrush = "#FF0B0F0B",
                CardBrush = "#FF151A15",
                LineBrush = "#FF243024",
                TextPrimaryBrush = "#FFEDF5ED",
                TextSecondaryBrush = "#FF8FA88F",
                AccentBrush = "#FF107C10",
                AccentHoverBrush = "#FF16A116",
                AccentSoftBrush = "#FF12240F",
                AccentForegroundBrush = "#FFFFFFFF",
                ControlBackgroundBrush = "#FF1B231B",
                ControlHoverBrush = "#FF24331F",
                SelectionBrush = "#FF1E4A1E",
                ScrollThumbBrush = "#FF2E4A2E",
                OkBrush = "#FF3FBF3F",
                WarnBrush = "#FFC9A227",
                DangerBrush = "#FFD64545",
                ChartGridBrush = "#FF22301F",
                ChartAxisBrush = "#FF33472F",
                ChartBarBrush = "#FF107C10",
                ChartBarHoverBrush = "#FF3FBF3F",
                TrayNeutral = "#FF107C10",
            },
            new ThemePalette
            {
                // Zune HD: hot orange over brown-black, dark text on the accent.
                Key = "zune",
                Label = "Zune",
                IsDark = true,
                WindowBackgroundBrush = "#FF120D08",
                CardBrush = "#FF1E1710",
                LineBrush = "#FF362A1D",
                TextPrimaryBrush = "#FFF6EFE6",
                TextSecondaryBrush = "#FFB09B84",
                AccentBrush = "#FFF0640A",
                AccentHoverBrush = "#FFFF7C28",
                AccentSoftBrush = "#FF32200E",
                AccentForegroundBrush = "#FF1A1206",
                ControlBackgroundBrush = "#FF261D14",
                ControlHoverBrush = "#FF34271A",
                SelectionBrush = "#FF5A3410",
                ScrollThumbBrush = "#FF4A3722",
                OkBrush = "#FF7FB03F",
                WarnBrush = "#FFE0A03A",
                DangerBrush = "#FFD9503F",
                ChartGridBrush = "#FF2E2417",
                ChartAxisBrush = "#FF46351F",
                ChartBarBrush = "#FFF0640A",
                ChartBarHoverBrush = "#FFFF8C3A",
                TrayNeutral = "#FFF0640A",
            },
        };

        private static ThemePalette _current = Palettes[0];

        /// <summary>Raised after the palette changes, so owners can repaint what WPF cannot.</summary>
        public static event EventHandler Changed;

        public static ThemePalette Current
        {
            get { return _current; }
        }

        public static IReadOnlyList<ThemePalette> All
        {
            get { return Palettes; }
        }

        public static ThemePalette Find(string key)
        {
            return Palettes.FirstOrDefault(palette =>
                       string.Equals(palette.Key, key, StringComparison.OrdinalIgnoreCase))
                   ?? Palettes[0];
        }

        /// <summary>Switches to <paramref name="key"/>, falling back to Light if unknown.</summary>
        public static void Apply(string key)
        {
            var palette = Find(key);
            _current = palette;

            var application = Application.Current;
            if (application == null)
            {
                return;
            }

            foreach (var brush in palette.Brushes())
            {
                var solid = new SolidColorBrush(ThemePalette.Parse(brush.Value));
                solid.Freeze();
                application.Resources[brush.Key] = solid;
            }

            var changed = Changed;
            if (changed != null)
            {
                changed(null, EventArgs.Empty);
            }
        }
    }
}
