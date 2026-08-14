using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MH.Core.Contracts;

namespace MH.Client.Controls;

public sealed class PriceChart : FrameworkElement
{
    public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(
        nameof(Bars),
        typeof(IReadOnlyList<PriceBarDto>),
        typeof(PriceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<PriceBarDto>? Bars
    {
        get => (IReadOnlyList<PriceBarDto>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        background.Freeze();
        drawingContext.DrawRoundedRectangle(background, null, new Rect(0, 0, ActualWidth, ActualHeight), 10, 10);

        var bars = Bars?.OrderBy(bar => bar.EndUtc).ToArray() ?? [];
        if (bars.Length == 0)
        {
            DrawLabel(drawingContext, "暂无历史行情", Brushes.LightSlateGray);
            return;
        }

        var left = 18d;
        var right = Math.Max(left + 1, ActualWidth - 18d);
        var top = 22d;
        var bottom = Math.Max(top + 1, ActualHeight - 24d);
        var minimum = bars.Min(bar => bar.Close);
        var maximum = bars.Max(bar => bar.Close);
        var range = Math.Max(1d, maximum - minimum);
        var points = new Point[bars.Length];
        for (var index = 0; index < bars.Length; index++)
        {
            var x = bars.Length == 1
                ? (left + right) / 2d
                : left + (right - left) * index / (bars.Length - 1d);
            var y = bottom - (bars[index].Close - minimum) / range * (bottom - top);
            points[index] = new Point(x, y);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            for (var index = 1; index < points.Length; index++)
            {
                context.LineTo(points[index], isStroked: true, isSmoothJoin: true);
            }
        }

        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(96, 165, 250)), 2);
        linePen.Freeze();
        drawingContext.DrawGeometry(null, linePen, geometry);

        foreach (var (point, bar) in points.Zip(bars))
        {
            if (bar.HasOcrAnomaly)
            {
                drawingContext.DrawEllipse(Brushes.OrangeRed, null, point, 4, 4);
            }
        }

        DrawLabel(
            drawingContext,
            $"收盘 {bars[^1].Close.ToString("N0", CultureInfo.InvariantCulture)} · {bars.Length} 根日线",
            Brushes.LightSteelBlue);
    }

    private void DrawLabel(DrawingContext drawingContext, string text, Brush brush)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            13,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(formattedText, new Point(18, 8));
    }
}
