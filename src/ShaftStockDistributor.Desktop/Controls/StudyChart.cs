using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShaftStockDistributor.Desktop;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.Controls;

/// <summary>
/// Offered and achieved pallets per hour, the saturation knee, and the theoretical shuttle ceiling.
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
        const double padL = 52;
        const double padR = 16;
        const double padB = 36;
        double legendRows = Math.Ceiling((groups.Count + (result.TheoreticalPalletsPerHour > 0 ? 1 : 0)) / 2.0);
        double padT = 12 + legendRows * 18;
        var plot = new Rect(padL, padT, Math.Max(10, bounds.Width - padL - padR), Math.Max(10, bounds.Height - padT - padB));

        var grid = Brush(Color.Parse("#2E3A4A"));
        var label = Brush(Color.Parse("#9AABBC"));
        for (int tick = 0; tick <= 4; tick++)
        {
            double y = plot.Bottom - plot.Height * tick / 4.0;
            context.DrawLine(new Pen(grid, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(context, (maxY * tick / 4.0).ToString("0", CultureInfo.InvariantCulture), new Point(4, y - 8), 11, label);
        }

        for (int tick = 0; tick <= 4; tick++)
        {
            double xValue = minX + (maxX - minX) * tick / 4.0;
            double x = plot.Left + plot.Width * tick / 4.0;
            DrawText(context, xValue.ToString("0", CultureInfo.InvariantCulture), new Point(x - 16, plot.Bottom + 8), 11, label);
        }

        int color = 0;
        double legendX = padL;
        double legendY = 6;
        foreach (var group in groups)
        {
            var ink = Palette[color % Palette.Length];
            color++;
            var ordered = group.OrderBy(point => point.LoadCratesPerHour).ToList();
            DrawPolyline(context, plot, minX, maxX, maxY, ordered.Select(point => (point.LoadCratesPerHour, point.AchievedPalletsPerHour)), ink, 3.2, dashed: false);
            DrawPolyline(context, plot, minX, maxX, maxY, ordered.Select(point => (point.LoadCratesPerHour, point.OfferedPalletsPerHour)), ink, 1.6, dashed: true);
            DrawText(context, group.Key, new Point(legendX, legendY), 12, Brush(ink));
            legendX += 280;
            if (legendX > bounds.Width - 200)
            {
                legendX = padL;
                legendY += 18;
            }
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
            DrawText(context, UiText.TheoreticalCapacity, new Point(legendX, legendY), 12, Brush(white));
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

    private static void DrawText(DrawingContext context, string text, Point origin, double size, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        context.DrawText(formatted, origin);
    }
}
