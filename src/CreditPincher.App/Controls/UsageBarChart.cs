using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CreditPincher.Core.Models;

namespace CreditPincher.App.Controls;

/// <summary>
/// Daily usage bars for the selected range, with a hover highlight and tooltip.
/// A WPF re-implementation of the plugin's Swing chart.
/// </summary>
public sealed class UsageBarChart : FrameworkElement
{
    private const double LeftMargin = 62;
    private const double RightMargin = 14;
    private const double TopMargin = 12;
    private const double BottomMargin = 26;
    private const int TickCount = 4;
    private const string NumberPattern = "#,##0.00";

    private static readonly Brush BarBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x35, 0x74, 0xA8)));
    private static readonly Brush HoverBarBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x56, 0xA6, 0xE2)));
    private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEC)));
    private static readonly Brush AxisBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD2)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0x6E, 0x78)));
    private static readonly Pen GridPen = Freeze(new Pen(GridBrush, 1));
    private static readonly Pen AxisPen = Freeze(new Pen(AxisBrush, 1));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private IReadOnlyList<DailyUsage> _data = [];
    private bool _showInDollars;
    private double _creditsPerDollar = 100.0;
    private int _hoveredIndex = -1;

    public UsageBarChart()
    {
        MinHeight = 150;
        ToolTip = null;
    }

    public void UpdateData(IReadOnlyList<DailyUsage> data, bool showInDollars, double creditsPerDollar)
    {
        _data = data;
        _showInDollars = showInDollars;
        _creditsPerDollar = double.IsFinite(creditsPerDollar) && creditsPerDollar > 0 ? creditsPerDollar : 100.0;
        _hoveredIndex = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = BarIndexAt(e.GetPosition(this));
        if (index == _hoveredIndex)
        {
            return;
        }

        _hoveredIndex = index;
        ToolTip = index >= 0 ? DescribeBar(_data[index]) : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoveredIndex == -1)
        {
            return;
        }

        _hoveredIndex = -1;
        ToolTip = null;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width;
        return new Size(width, Math.Max(MinHeight, 180));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

        // A transparent fill is what makes the element hit-testable for hover.
        drawingContext.DrawRectangle(Brushes.Transparent, null, bounds);

        var chartWidth = ActualWidth - LeftMargin - RightMargin;
        var chartHeight = ActualHeight - TopMargin - BottomMargin;
        if (chartWidth <= 0 || chartHeight <= 0)
        {
            return;
        }

        var maxUsage = _data.Count == 0 ? 0.0 : _data.Max(point => point.Credits);
        var maxValue = maxUsage > 0 ? maxUsage : 10.0;
        var baseline = ActualHeight - BottomMargin;

        for (var tick = 0; tick < TickCount; tick++)
        {
            var y = Snap(baseline - tick * chartHeight / (TickCount - 1));
            drawingContext.DrawLine(GridPen, new Point(LeftMargin, y), new Point(ActualWidth - RightMargin, y));

            var value = tick * maxValue / (TickCount - 1);
            var label = BuildText(FormatValue(value), 10);
            drawingContext.DrawText(label, new Point(LeftMargin - 6 - label.Width, y - label.Height / 2));
        }

        drawingContext.DrawLine(AxisPen, new Point(LeftMargin, Snap(baseline)), new Point(ActualWidth - RightMargin, Snap(baseline)));

        if (_data.Count == 0)
        {
            var empty = BuildText("No data in the selected range", 12);
            drawingContext.DrawText(empty, new Point(
                LeftMargin + (chartWidth - empty.Width) / 2,
                TopMargin + (chartHeight - empty.Height) / 2));
            return;
        }

        var step = chartWidth / _data.Count;
        var gap = step > 4 ? 2.0 : 0.0;
        var barWidth = Math.Max(1.0, step - gap);

        for (var index = 0; index < _data.Count; index++)
        {
            var credits = _data[index].Credits;
            if (credits <= 0)
            {
                continue;
            }

            var barHeight = credits / maxValue * chartHeight;
            var rectangle = new Rect(LeftMargin + index * step, baseline - barHeight, barWidth, barHeight);
            drawingContext.DrawRectangle(index == _hoveredIndex ? HoverBarBrush : BarBrush, null, rectangle);
        }

        DrawDateLabels(drawingContext, step, barWidth, baseline);
    }

    private void DrawDateLabels(DrawingContext drawingContext, double step, double barWidth, double baseline)
    {
        var sample = BuildText("00-00", 10);
        var labelInterval = Math.Max(1, (int)Math.Ceiling((sample.Width + 14) / step));

        for (var index = 0; index < _data.Count; index += labelInterval)
        {
            var centre = LeftMargin + index * step + barWidth / 2;
            drawingContext.DrawLine(AxisPen, new Point(Snap(centre), baseline), new Point(Snap(centre), baseline + 4));

            var label = BuildText(_data[index].Date.ToString("MM-dd", CultureInfo.InvariantCulture), 10);
            drawingContext.DrawText(label, new Point(centre - label.Width / 2, baseline + 6));
        }
    }

    private int BarIndexAt(Point position)
    {
        if (_data.Count == 0)
        {
            return -1;
        }

        var chartWidth = ActualWidth - LeftMargin - RightMargin;
        var chartHeight = ActualHeight - TopMargin - BottomMargin;
        if (chartWidth <= 0 || chartHeight <= 0)
        {
            return -1;
        }

        if (position.X < LeftMargin || position.X > ActualWidth - RightMargin ||
            position.Y < TopMargin || position.Y > ActualHeight - BottomMargin)
        {
            return -1;
        }

        var step = chartWidth / _data.Count;
        var index = Math.Clamp((int)((position.X - LeftMargin) / step), 0, _data.Count - 1);

        // Hovering anywhere in the bar's column reads better than requiring a hit on a
        // one-pixel-tall bar, so zero days get a small hover band at the baseline.
        var maxUsage = _data.Max(point => point.Credits);
        var maxValue = maxUsage > 0 ? maxUsage : 1.0;
        var barHeight = _data[index].Credits / maxValue * chartHeight;
        var baseline = ActualHeight - BottomMargin;
        var top = barHeight < 6 ? baseline - 6 : baseline - barHeight;

        return position.Y >= top && position.Y <= baseline + 2 ? index : -1;
    }

    private string DescribeBar(DailyUsage usage)
    {
        var date = usage.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var credits = usage.Credits.ToString(NumberPattern, CultureInfo.CurrentCulture);
        var dollars = (usage.Credits / _creditsPerDollar).ToString(NumberPattern, CultureInfo.CurrentCulture);
        return $"{date}\nUsage: {credits} credits\nCost: ${dollars}";
    }

    private string FormatValue(double value) => _showInDollars
        ? "$" + (value / _creditsPerDollar).ToString(NumberPattern, CultureInfo.CurrentCulture)
        : value.ToString(NumberPattern, CultureInfo.CurrentCulture);

    private FormattedText BuildText(string text, double fontSize) => new(
        text,
        CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        LabelTypeface,
        fontSize,
        TextBrush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Aligns hairlines to device pixels so grid lines stay crisp.</summary>
    private static double Snap(double value) => Math.Round(value) + 0.5;

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
