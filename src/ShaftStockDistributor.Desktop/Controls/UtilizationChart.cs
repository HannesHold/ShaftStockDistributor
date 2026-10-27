using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ShaftStockDistributor.Desktop.Controls;

public sealed class BarPoint
{
    public string Label { get; init; } = "";

    /// <summary>Share from 0 to 1.</summary>
    public double Value { get; init; }

    public string ColorHex { get; init; } = "#4C8DFF";
}

/// <summary>Horizontal utilization bars, one per shuttle.</summary>
public sealed class UtilizationChart : Control
{
    public UtilizationChart()
    {
        ClipToBounds = true;
    }

    public static readonly StyledProperty<IReadOnlyList<BarPoint>?> BarsProperty =
        AvaloniaProperty.Register<UtilizationChart, IReadOnlyList<BarPoint>?>(nameof(Bars));

    static UtilizationChart()
    {
        AffectsRender<UtilizationChart>(BarsProperty);
    }

    public IReadOnlyList<BarPoint>? Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush(Color.Parse("#101820")), bounds);
        var bars = Bars ?? Array.Empty<BarPoint>();
        if (bars.Count == 0)
        {
            var empty = Format("No shuttles", 13, Brush(Color.Parse("#9AABBC")));
            context.DrawText(empty, new Point(12, 12));
            return;
        }

        double row = Math.Max(22, (bounds.Height - 8) / bars.Count);
        var track = Brush(Color.Parse("#243140"));
        var label = Brush(Color.Parse("#C5D2E0"));
        var ink = Brush(Color.Parse("#F4F7FB"));
        for (int index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            double y = 6 + index * row;
            var name = Format(bar.Label, 12, label);
            context.DrawText(name, new Point(8, y));
            double left = 88;
            double width = Math.Max(20, bounds.Width - left - 52);
            var rail = new Rect(left, y + 2, width, 12);
            context.FillRectangle(track, rail);
            double fill = width * Math.Clamp(bar.Value, 0, 1);
            if (fill > 0.5)
            {
                context.FillRectangle(Brush(Parse(bar.ColorHex)), new Rect(left, y + 2, fill, 12));
            }

            var percent = Format((bar.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%", 12, ink);
            context.DrawText(percent, new Point(left + width + 8, y));
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
