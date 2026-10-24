using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShaftStockDistributor.Desktop;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.Controls;

/// <summary>
/// Offered and achieved pallets per hour, the saturation knee, and the shuttle upper bound.
/// Drawn with Avalonia so a headless capture shows the curves rather than an empty plot.
/// </summary>
public sealed class StudyChart : Control
{
    public static readonly StyledProperty<StudyResult?> ResultProperty =
        AvaloniaProperty.Register<StudyChart, StudyResult?>(nameof(Result));

    private static readonly Color[] Palette =
    {
        Color.Parse("#4C8DFF"),
        Color.Parse("#F0A202"),
        Color.Parse("#3DDC97"),
        Color.Parse("#E85D75"),
        Color.Parse("#7C5CFF"),
        Color.Parse("#5AA9E6")
    };

    static StudyChart()
    {
        AffectsRender<StudyChart>(ResultProperty);
    }

    public StudyResult? Result
    {
        get => GetValue(ResultProperty);
        set => SetValue(ResultProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush(Color.Parse("#101820")), bounds);
        var result = Result;
        if (result is null || result.Means.Count == 0)
        {
            DrawText(context, UiText.StudyTitle, new Point(16, 16), 14, Brush(Color.Parse("#9AABBC")));
            return;
        }

        var groups = result.Means
            .GroupBy(point => point.Scenario + " · " + point.Mode)
            .ToList();
        double minX = result.Means.Min(point => point.LoadCratesPerHour);
        double maxX = result.Means.Max(point => point.LoadCratesPerHour);
        double maxY = result.Means.Max(point => Math.Max(point.OfferedPalletsPerHour, point.AchievedPalletsPerHour));
        maxY = Math.Max(maxY, result.TheoreticalPalletsPerHour);
        if (maxX <= minX)
        {
            maxX = minX + 1;
        }

        if (maxY <= 0)
        {
            maxY = 1;
        }

        maxY *= 1.08;
        const double padL = 54;
        const double padR = 16;
        const double padB = 32;
        var legendLabels = groups.Select(group => group.Key).ToList();
        if (result.TheoreticalPalletsPerHour > 0)
        {
            legendLabels.Add(UiText.TheoreticalCapacity);
        }

        double legendWidth = Math.Max(80, bounds.Width - padL - padR);
        int legendRows = CountRows(legendLabels, legendWidth, 12);
        double padT = 8 + legendRows * 18;
        var plot = new Rect(padL, padT, Math.Max(10, bounds.Width - padL - padR), Math.Max(10, bounds.Height - padT - padB));

        var grid = Brush(Color.Parse("#2E3A4A"));
        var label = Brush(Color.Parse("#9AABBC"));
        for (int tick = 0; tick <= 4; tick++)
        {
            double y = plot.Bottom - plot.Height * tick / 4.0;
            context.DrawLine(new Pen(grid, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            string tickText = Math.Max(0, maxY * tick / 4.0).ToString("0", CultureInfo.InvariantCulture);
            var tickLabel = Format(tickText, 11, label);
            double labelY = tick == 0 ? y - tickLabel.Height : y - tickLabel.Height / 2;
            context.DrawText(tickLabel, new Point(Math.Max(2, plot.Left - 8 - tickLabel.Width), labelY));
        }

        for (int tick = 0; tick <= 4; tick++)
        {
            double xValue = minX + (maxX - minX) * tick / 4.0;
            double x = plot.Left + plot.Width * tick / 4.0;
            var xLabel = Format(xValue.ToString("0", CultureInfo.InvariantCulture), 11, label);
            double originX = Math.Clamp(x - xLabel.Width / 2, 2, Math.Max(2, bounds.Width - xLabel.Width - 2));
            context.DrawText(xLabel, new Point(originX, plot.Bottom + 6));
        }

        int color = 0;
        double legendX = padL;
        double legendY = 4;
        foreach (var group in groups)
        {
            var ink = Palette[color % Palette.Length];
            color++;
            var ordered = group.OrderBy(point => point.LoadCratesPerHour).ToList();
            DrawPolyline(context, plot, minX, maxX, maxY, ordered.Select(point => (point.LoadCratesPerHour, point.AchievedPalletsPerHour)), ink, 3.2, dashed: false);
            DrawPolyline(context, plot, minX, maxX, maxY, ordered.Select(point => (point.LoadCratesPerHour, point.OfferedPalletsPerHour)), ink, 1.6, dashed: true);
            var caption = Format(group.Key, 12, Brush(ink));
            if (legendX > padL && legendX + caption.Width > bounds.Width - padR)
            {
                legendX = padL;
                legendY += 18;
            }

            context.DrawText(caption, new Point(legendX, legendY));
            legendX += caption.Width + 16;
        }

        if (result.TheoreticalPalletsPerHour > 0)
        {
            var white = Color.Parse("#F4F7FB");
            DrawPolyline(
                context,
                plot,
                minX,
                maxX,
                maxY,
                new[] { (minX, result.TheoreticalPalletsPerHour), (maxX, result.TheoreticalPalletsPerHour) },
                white,
                1.8,
                dashed: true);
            var boundCaption = Format(UiText.TheoreticalCapacity, 12, Brush(white));
            if (legendX > padL && legendX + boundCaption.Width > bounds.Width - padR)
            {
                legendX = padL;
                legendY += 18;
            }

            context.DrawText(boundCaption, new Point(legendX, legendY));
        }

        foreach (var item in result.Saturations.Where(item => item.Found && item.SaturatedAtCratesPerHour is not null))
        {
            var match = result.Means.FirstOrDefault(point =>
                point.Scenario == item.Scenario
                && point.Mode == item.Mode
                && Math.Abs(point.LoadCratesPerHour - item.SaturatedAtCratesPerHour!.Value) < 1e-6);
            double yValue = match?.AchievedPalletsPerHour ?? 0;
            var center = Map(plot, minX, maxX, maxY, item.SaturatedAtCratesPerHour!.Value, yValue);
            context.DrawEllipse(Brush(Color.Parse("#E85D75")), new Pen(Brushes.White, 2), center, 6, 6);
        }
    }

    private static void DrawPolyline(
        DrawingContext context,
        Rect plot,
        double minX,
        double maxX,
        double maxY,
        IEnumerable<(double X, double Y)> points,
        Color color,
        double thickness,
        bool dashed)
    {
        var list = points.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var pen = new Pen(Brush(color), thickness);
        if (!dashed || list.Count == 1)
        {
            var geometry = new StreamGeometry();
            using (var geo = geometry.Open())
            {
                var first = Map(plot, minX, maxX, maxY, list[0].X, list[0].Y);
                geo.BeginFigure(first, false);
                for (int index = 1; index < list.Count; index++)
                {
                    geo.LineTo(Map(plot, minX, maxX, maxY, list[index].X, list[index].Y));
                }
            }

            context.DrawGeometry(null, pen, geometry);
            return;
        }

        for (int index = 1; index < list.Count; index++)
        {
            var from = Map(plot, minX, maxX, maxY, list[index - 1].X, list[index - 1].Y);
            var to = Map(plot, minX, maxX, maxY, list[index].X, list[index].Y);
            double length = Distance(from, to);
            double step = 10;
            for (double distance = 0; distance < length; distance += step * 2)
            {
                double end = Math.Min(length, distance + step);
                context.DrawLine(pen, Lerp(from, to, distance / length), Lerp(from, to, end / length));
            }
        }
    }

    private static Point Map(Rect plot, double minX, double maxX, double maxY, double x, double y)
    {
        double tx = (x - minX) / (maxX - minX);
        double ty = y / maxY;
        return new Point(plot.Left + tx * plot.Width, plot.Bottom - ty * plot.Height);
    }

    private static Point Lerp(Point from, Point to, double t) =>
        new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

    private static double Distance(Point from, Point to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static readonly Dictionary<uint, IBrush> BrushesByColor = new();

    private static IBrush Brush(Color color)
    {
        uint key = color.ToUInt32();
        if (!BrushesByColor.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            BrushesByColor[key] = brush;
        }

        return brush;
    }

    private static int CountRows(IReadOnlyList<string> labels, double width, double size)
    {
        double x = 0;
        int rows = 1;
        foreach (string label in labels)
        {
            double labelWidth = Measure(label, size) + 16;
            if (x > 0 && x + labelWidth > width)
            {
                rows++;
                x = labelWidth;
            }
            else
            {
                x += labelWidth;
            }
        }

        return rows;
    }

    private static double Measure(string text, double size) =>
        Format(text, size, Brushes.White).Width;

    private static void DrawText(DrawingContext context, string text, Point origin, double size, IBrush brush)
    {
        context.DrawText(Format(text, size, brush), origin);
    }

    private static FormattedText Format(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
}
