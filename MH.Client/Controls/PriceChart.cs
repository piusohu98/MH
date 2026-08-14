using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MH.Core.Contracts;
using MH.Core.Models;

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

    public static readonly DependencyProperty CommonPrice7DaysProperty = DependencyProperty.Register(
        nameof(CommonPrice7Days),
        typeof(decimal?),
        typeof(PriceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public decimal? CommonPrice7Days
    {
        get => (decimal?)GetValue(CommonPrice7DaysProperty);
        set => SetValue(CommonPrice7DaysProperty, value);
    }

    public static readonly DependencyProperty EventsProperty = DependencyProperty.Register(
        nameof(Events),
        typeof(IReadOnlyList<MarketEventDto>),
        typeof(PriceChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<MarketEventDto>? Events
    {
        get => (IReadOnlyList<MarketEventDto>?)GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    public sealed record EventBand(
        string Id,
        MarketEventType Type,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string Label);

    public static IReadOnlyList<EventBand> GetVisibleEventBands(
        IReadOnlyList<MarketEventDto>? events,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var startUtc = rangeStartUtc.ToUniversalTime();
        var endUtc = rangeEndUtc.ToUniversalTime();
        if (startUtc >= endUtc || events is null || events.Count == 0)
        {
            return [];
        }

        return events
            .Where(eventItem => eventItem.Type is MarketEventType.Holiday or MarketEventType.SupplyChange)
            .Select(eventItem =>
            {
                var eventStartUtc = eventItem.StartsAtUtc.ToUniversalTime();
                var eventEndUtc = eventItem.EndsAtUtc.ToUniversalTime();
                return new
                {
                    Event = eventItem,
                    StartUtc = eventStartUtc < startUtc ? startUtc : eventStartUtc,
                    EndUtc = eventEndUtc > endUtc ? endUtc : eventEndUtc
                };
            })
            .Where(item => item.StartUtc < item.EndUtc)
            .OrderBy(item => item.StartUtc)
            .ThenBy(item => item.EndUtc)
            .ThenBy(item => item.Event.Id, StringComparer.Ordinal)
            .Select(item => new EventBand(
                item.Event.Id,
                item.Event.Type,
                item.StartUtc,
                item.EndUtc,
                item.Event.Label))
            .ToArray();
    }

    public static double MapUtcToX(
        DateTimeOffset valueUtc,
        DateTimeOffset chartStartUtc,
        DateTimeOffset chartEndUtc,
        double left,
        double right)
    {
        var startUtc = chartStartUtc.ToUniversalTime();
        var endUtc = chartEndUtc.ToUniversalTime();
        if (startUtc >= endUtc)
        {
            return (left + right) / 2d;
        }

        var fraction = Math.Clamp(
            (valueUtc.ToUniversalTime() - startUtc).TotalSeconds / (endUtc - startUtc).TotalSeconds,
            0d,
            1d);
        return left + (right - left) * fraction;
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

        var left = 72d;
        var right = Math.Max(left + 1, ActualWidth - 18d);
        var top = 34d;
        var bottom = Math.Max(top + 1, ActualHeight - 38d);
        var minimum = bars.Min(bar => (double)bar.Close);
        var maximum = bars.Max(bar => (double)bar.Close);
        if (CommonPrice7Days is decimal commonPrice)
        {
            minimum = Math.Min(minimum, (double)commonPrice);
            maximum = Math.Max(maximum, (double)commonPrice);
        }

        var range = Math.Max(1d, maximum - minimum);
        var chartStartUtc = bars[0].StartUtc.ToUniversalTime();
        var chartEndUtc = bars[^1].EndUtc.ToUniversalTime();
        var eventBands = GetVisibleEventBands(Events, chartStartUtc, chartEndUtc);
        var xForUtc = (DateTimeOffset value) => MapUtcToX(value, chartStartUtc, chartEndUtc, left, right);
        foreach (var eventBand in eventBands)
        {
            var eventBrush = new SolidColorBrush(eventBand.Type == MarketEventType.Holiday
                ? Color.FromArgb(54, 245, 158, 11)
                : Color.FromArgb(54, 129, 140, 248));
            eventBrush.Freeze();
            var eventLeft = xForUtc(eventBand.StartUtc);
            var eventRight = xForUtc(eventBand.EndUtc);
            drawingContext.DrawRectangle(
                eventBrush,
                null,
                new Rect(eventLeft, top, Math.Max(1d, eventRight - eventLeft), bottom - top));
        }

        if (eventBands.Count > 0)
        {
            DrawText(
                drawingContext,
                "淡金：节日 · 淡紫：供给变化",
                new Point(left, 8),
                Brushes.LightSlateGray,
                10);
        }

        var points = new Point[bars.Length];
        for (var index = 0; index < bars.Length; index++)
        {
            var x = bars.Length == 1
                ? (left + right) / 2d
                : MapUtcToX(bars[index].EndUtc, chartStartUtc, chartEndUtc, left, right);
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

        DrawText(
            drawingContext,
            maximum.ToString("N0", CultureInfo.InvariantCulture),
            new Point(6, Math.Max(2, top - 17)),
            Brushes.LightSteelBlue,
            11);
        DrawText(
            drawingContext,
            minimum.ToString("N0", CultureInfo.InvariantCulture),
            new Point(6, Math.Max(top, bottom - 8)),
            Brushes.LightSteelBlue,
            11);

        if (CommonPrice7Days is decimal median)
        {
            var medianY = bottom - ((double)median - minimum) / range * (bottom - top);
            var medianPen = new Pen(new SolidColorBrush(Color.FromRgb(148, 163, 184)), 1)
            {
                DashStyle = new DashStyle([4, 4], 0)
            };
            medianPen.Freeze();
            drawingContext.DrawLine(medianPen, new Point(left, medianY), new Point(right, medianY));
            DrawText(
                drawingContext,
                $"近 7 天常见价 {median.ToString("N0", CultureInfo.InvariantCulture)}",
                new Point(left + 6, Math.Max(top, medianY - 17)),
                Brushes.LightSlateGray,
                11);
        }

        foreach (var (point, bar) in points.Zip(bars))
        {
            if (bar.HasOcrAnomaly)
            {
                drawingContext.DrawEllipse(Brushes.OrangeRed, null, point, 4, 4);
            }
        }

        var latestPoint = points[^1];
        drawingContext.DrawEllipse(Brushes.White, null, latestPoint, 4.5, 4.5);
        DrawText(
            drawingContext,
            $"最新 {bars[^1].Close.ToString("N0", CultureInfo.InvariantCulture)}",
            new Point(Math.Min(latestPoint.X + 7, Math.Max(left, right - 74)), Math.Max(top, latestPoint.Y - 18)),
            Brushes.White,
            11);
        DrawText(
            drawingContext,
            bars[0].EndUtc.ToUniversalTime().ToString("MM-dd", CultureInfo.InvariantCulture),
            new Point(left, bottom + 8),
            Brushes.LightSlateGray,
            11);
        var lastDateText = bars[^1].EndUtc.ToUniversalTime().ToString("MM-dd", CultureInfo.InvariantCulture);
        DrawText(
            drawingContext,
            lastDateText,
            new Point(Math.Max(left, right - 34), bottom + 8),
            Brushes.LightSlateGray,
            11);
        DrawText(
            drawingContext,
            $"{bars.Length} 根日线",
            new Point(Math.Max(left, right - 74), 8),
            Brushes.LightSteelBlue,
            11);
    }

    private void DrawLabel(DrawingContext drawingContext, string text, Brush brush)
        => DrawText(drawingContext, text, new Point(18, 8), brush, 13);

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        Point point,
        Brush brush,
        double fontSize)
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
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(formattedText, point);
    }
}
