using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CreditPincher.App.Tray;

/// <summary>How the current month is tracking against the budget.</summary>
public enum TrayStatus
{
    /// <summary>No budget set, or nothing logged yet.</summary>
    Neutral,

    /// <summary>Comfortably inside the budget.</summary>
    Ok,

    /// <summary>Past the warning threshold for the month.</summary>
    Warning,

    /// <summary>Over budget for the month.</summary>
    Over,
}

/// <summary>
/// Draws the tray icon at runtime so it can carry the budget state as colour —
/// the whole point of living in the notification area is being readable at a glance.
/// </summary>
public static class TrayIconRenderer
{
    /// <summary>
    /// Renders a bar-chart glyph tinted for <paramref name="status"/>.
    /// The caller owns <paramref name="iconHandle"/> and must pass it to
    /// <see cref="DestroyIcon"/> once the icon is no longer assigned.
    /// </summary>
    public static Icon Create(TrayStatus status, int size, out IntPtr iconHandle)
    {
        size = Math.Max(16, size);

        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var scale = size / 256f;
            var radius = 56f * scale;

            using (var background = new SolidBrush(BackgroundFor(status)))
            using (var shape = RoundedRectangle(new RectangleF(0, 0, size - 1, size - 1), radius))
            {
                graphics.FillPath(background, shape);
            }

            // Three ascending bars, matching the application icon.
            (float X, float Top)[] bars = [(52f, 156f), (108f, 112f), (164f, 60f)];
            using var barBrush = new SolidBrush(Color.White);
            foreach (var (x, top) in bars)
            {
                var rectangle = new RectangleF(x * scale, top * scale, 40f * scale, (206f - top) * scale);
                using var path = RoundedRectangle(rectangle, 8f * scale);
                graphics.FillPath(barBrush, path);
            }
        }

        iconHandle = bitmap.GetHicon();

        // Clone off the handle-backed icon so the caller can destroy the handle
        // immediately after the icon is replaced without invalidating what Windows drew.
        using var handleIcon = Icon.FromHandle(iconHandle);
        return (Icon)handleIcon.Clone();
    }

    public static TrayStatus StatusFor(double? monthlyBudget, double totalCredits, int warningThresholdPercent = 80)
    {
        if (monthlyBudget is not { } budget || budget <= 0 || !double.IsFinite(budget))
        {
            return TrayStatus.Neutral;
        }

        var usedPercent = totalCredits / budget * 100.0;
        return usedPercent switch
        {
            >= 100.0 => TrayStatus.Over,
            _ when usedPercent >= warningThresholdPercent => TrayStatus.Warning,
            _ => TrayStatus.Ok,
        };
    }

    private static Color BackgroundFor(TrayStatus status) => status switch
    {
        TrayStatus.Ok => Color.FromArgb(46, 125, 50),
        TrayStatus.Warning => Color.FromArgb(178, 107, 0),
        TrayStatus.Over => Color.FromArgb(192, 57, 43),
        _ => Color.FromArgb(53, 116, 168),
    };

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1f, Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height)));

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr handle);
}
