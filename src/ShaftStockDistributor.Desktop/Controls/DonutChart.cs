using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ShaftStockDistributor.Desktop.Controls;

public sealed class DonutSlice
{
    public string Label { get; init; } = "";

    public double Value { get; init; }

    public string ColorHex { get; init; } = "#4C8DFF";
}

/// <summary>A ring chart. A full circle is drawn as a stroke so a 100% share still looks like a donut.</summary>
public sealed class DonutChart : Control
{
    public DonutChart()
    {
        ClipToBounds = true;
    }

    public static readonly StyledProperty<IReadOnlyList<DonutSlice>?> SlicesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<DonutSlice>?>(nameof(Slices));

    public static readonly StyledProperty<string?> CenterProperty =
        AvaloniaProperty.Register<DonutChart, string?>(nameof(Center));

    static DonutChart()
    {
        AffectsRender<DonutChart>(SlicesProperty, CenterProperty);
    }

    public IReadOnlyList<DonutSlice>? Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public string? Center
    {
        get => GetValue(CenterProperty);
        set => SetValue(CenterProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush(Color.Parse("#101820")), bounds);
        var slices = (Slices ?? Array.Empty<DonutSlice>()).Where(slice => slice.Value > 1e-9).ToList();
        double total = slices.Sum(slice => slice.Value);
        double side = Math.Min(bounds.Height - 12, Math.Max(48, bounds.Width * 0.46));
        var center = new Point(12 + side / 2, bounds.Height / 2);
        double outer = side / 2 - 4;
        double inner = outer * 0.62;
        if (slices.Count == 0 || total <= 1e-9)
        {
            context.DrawEllipse(null, new Pen(Brush(Color.Parse("#243140")), outer - inner), center, (outer + inner) / 2, (outer + inner) / 2);
        }
        else if (slices.Count == 1)
        {
            context.DrawEllipse(null, new Pen(Brush(Parse(slices[0].ColorHex)), outer - inner), center, (outer + inner) / 2, (outer + inner) / 2);
        }
        else
        {
            double start = 0;
            foreach (var slice in slices)
            {
                double sweep = 360.0 * slice.Value / total;
                context.DrawGeometry(Brush(Parse(slice.ColorHex)), null, Ring(center, outer, inner, start, Math.Min(359.2, sweep)));
                start += sweep;
            }
        }

        var hole = Brush(Color.Parse("#101820"));
        context.DrawEllipse(hole, null, center, inner - 1, inner - 1);
        var ink = Brush(Color.Parse("#F4F7FB"));
        string centerText = string.IsNullOrWhiteSpace(Center) ? "" : Center;
        var formatted = Format(centerText, 16, ink);
        context.DrawText(formatted, new Point(center.X - formatted.Width / 2, center.Y - formatted.Height / 2));

        double legendX = center.X + outer + 16;
        double legendY = Math.Max(8, center.Y - slices.Count * 11);
        var label = Brush(Color.Parse("#C5D2E0"));
        int row = 0;
        foreach (var slice in slices)
        {
            double y = legendY + row * 22;
            context.FillRectangle(Brush(Parse(slice.ColorHex)), new Rect(legendX, y + 3, 10, 10));
            double share = total <= 1e-9 ? 0 : 100.0 * slice.Value / total;
            var text = Format($"{slice.Label}  {share:0}%", 12, label);
            context.DrawText(text, new Point(legendX + 16, y));
            row++;
        }
    }

    private static StreamGeometry Ring(Point center, double outer, double inner, double start, double sweep)
    {
        var geometry = new StreamGeometry();
        using var figure = geometry.Open();
        var outerStart = Polar(center, outer, start);
        var outerEnd = Polar(center, outer, start + sweep);
        var innerEnd = Polar(center, inner, start + sweep);
        var innerStart = Polar(center, inner, start);
        figure.BeginFigure(outerStart, true);
        figure.ArcTo(outerEnd, new Size(outer, outer), 0, sweep > 180, SweepDirection.Clockwise);
        figure.LineTo(innerEnd);
        figure.ArcTo(innerStart, new Size(inner, inner), 0, sweep > 180, SweepDirection.CounterClockwise);
        figure.EndFigure(true);
        return geometry;
    }

    private static Point Polar(Point center, double radius, double degrees)
    {
        double radians = (degrees - 90) * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
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
