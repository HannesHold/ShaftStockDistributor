using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ShaftStockDistributor.Desktop.Controls;

public sealed class LoadPoint
{
    public string Series { get; init; } = "";

    public double Load { get; init; }

    public double Achieved { get; init; }

    public double Lost { get; init; }

    public string ColorHex { get; init; } = "#4C8DFF";
}

/// <summary>
/// Achieved pallets per hour (solid, left axis) and lost crates (dashed, right axis) against load.
/// </summary>
public sealed class LoadChart : Control
{
    public LoadChart()
    {
        ClipToBounds = true;
    }

    public static readonly StyledProperty<IReadOnlyList<LoadPoint>?> PointsProperty =
        AvaloniaProperty.Register<LoadChart, IReadOnlyList<LoadPoint>?>(nameof(Points));

    static LoadChart()
    {
        AffectsRender<LoadChart>(PointsProperty);
    }

    public IReadOnlyList<LoadPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush(Color.Parse("#101820")), bounds);
        var points = Points ?? Array.Empty<LoadPoint>();
        const double padL = 48;
        const double padR = 48;
        const double padB = 28;
        const double padT = 22;
        var plot = new Rect(padL, padT, Math.Max(8, bounds.Width - padL - padR), Math.Max(8, bounds.Height - padT - padB));
        var grid = Brush(Color.Parse("#2E3A4A"));
        var label = Brush(Color.Parse("#9AABBC"));
        var lostInk = Brush(Color.Parse("#E85D75"));
        if (points.Count == 0)
        {
            context.DrawText(Format("No study points", 13, label), new Point(plot.Left, plot.Top));
            return;
        }

        double minLoad = points.Min(point => point.Load);
        double maxLoad = points.Max(point => point.Load);
        if (Math.Abs(maxLoad - minLoad) < 1e-6)
        {
            maxLoad = minLoad + 1;
        }

        double maxAchieved = Math.Max(1, points.Max(point => point.Achieved) * 1.15);
        double maxLost = Math.Max(1, points.Max(point => point.Lost) * 1.15);
        for (int tick = 0; tick <= 2; tick++)
        {
            double y = plot.Bottom - plot.Height * tick / 2.0;
            context.DrawLine(new Pen(grid, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            context.DrawText(Format((maxAchieved * tick / 2).ToString("0", CultureInfo.InvariantCulture), 11, label), new Point(4, y - 7));
            var right = Format((maxLost * tick / 2).ToString("0", CultureInfo.InvariantCulture), 11, lostInk);
            context.DrawText(right, new Point(plot.Right + 6, y - 7));
        }

        context.DrawText(Format("pallets/h", 11, label), new Point(plot.Left, 2));
        var lostLabel = Format("lost", 11, lostInk);
        context.DrawText(lostLabel, new Point(plot.Right - lostLabel.Width, 2));

        foreach (var group in points.GroupBy(point => point.Series))
        {
            var ordered = group.OrderBy(point => point.Load).ToList();
            var color = Brush(Parse(ordered[0].ColorHex));
            DrawLine(context, plot, ordered, minLoad, maxLoad, maxAchieved, color, dashed: false, point => point.Achieved);
            DrawLine(context, plot, ordered, minLoad, maxLoad, maxLost, color, dashed: true, point => point.Lost);
        }

        var start = Format(minLoad.ToString("0", CultureInfo.InvariantCulture), 11, label);
        var end = Format(maxLoad.ToString("0", CultureInfo.InvariantCulture) + " crates/h", 11, label);
        context.DrawText(start, new Point(plot.Left, plot.Bottom + 4));
        context.DrawText(end, new Point(plot.Right - end.Width, plot.Bottom + 4));
    }

    private static void DrawLine(
        DrawingContext context,
        Rect plot,
        IReadOnlyList<LoadPoint> ordered,
        double minLoad,
        double maxLoad,
        double maxY,
        IBrush color,
        bool dashed,
        Func<LoadPoint, double> value)
    {
        var geometry = new StreamGeometry();
        var markers = new List<Point>(ordered.Count);
        using (var figure = geometry.Open())
        {
            for (int index = 0; index < ordered.Count; index++)
            {
                double x = plot.Left + plot.Width * (ordered[index].Load - minLoad) / (maxLoad - minLoad);
                double y = plot.Bottom - plot.Height * Math.Clamp(value(ordered[index]) / maxY, 0, 1);
                var point = new Point(x, y);
                markers.Add(point);
                if (index == 0)
                {
                    figure.BeginFigure(point, false);
                }
                else
                {
                    figure.LineTo(point);
                }
            }
        }

        var pen = new Pen(color, dashed ? 1.4 : 2.4);
        if (dashed)
        {
            pen.DashStyle = new DashStyle(new double[] { 5, 4 }, 0);
        }

        context.DrawGeometry(null, pen, geometry);
        if (!dashed)
        {
            foreach (var marker in markers)
            {
                context.DrawEllipse(color, null, marker, 3.2, 3.2);
            }
        }
    }

    private static Color Parse(string hex)
    {
        try
        {
            return Color.Parse(hex);
        }
        catch (FormatException)
        {
            return Color.Parse("#4C8DFF");
        }
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

    private static FormattedText Format(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
}
