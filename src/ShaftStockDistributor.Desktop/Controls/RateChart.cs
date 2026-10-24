using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ShaftStockDistributor.Desktop;

namespace ShaftStockDistributor.Desktop.Controls;

/// <summary>
/// Compact pallet-rate chart. The axis starts at zero and uses a few ticks so labels stay apart.
/// </summary>
public sealed class RateChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> SamplesProperty =
        AvaloniaProperty.Register<RateChart, IReadOnlyList<double>?>(nameof(Samples));

    public static readonly StyledProperty<double> OfferedProperty =
        AvaloniaProperty.Register<RateChart, double>(nameof(Offered));

    public static readonly StyledProperty<double> SpanMinutesProperty =
        AvaloniaProperty.Register<RateChart, double>(nameof(SpanMinutes));

    static RateChart()
    {
        AffectsRender<RateChart>(SamplesProperty, OfferedProperty, SpanMinutesProperty);
    }

    public IReadOnlyList<double>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public double Offered
    {
        get => GetValue(OfferedProperty);
        set => SetValue(OfferedProperty, value);
    }

    public double SpanMinutes
    {
        get => GetValue(SpanMinutesProperty);
        set => SetValue(SpanMinutesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush(Color.Parse("#101820")), bounds);
        using (context.PushClip(bounds))
        {
            Draw(context, bounds);
        }
    }

    private void Draw(DrawingContext context, Rect bounds)
    {
        var samples = Samples ?? Array.Empty<double>();
        double peak = Offered;
        foreach (double sample in samples)
        {
            if (sample > peak)
            {
                peak = sample;
            }
        }

        double maxY = NiceMax(peak);
        const double padL = 46;
        const double padR = 10;
        const double padB = 22;
        const double padT = 22;
        var plot = new Rect(padL, padT, Math.Max(8, bounds.Width - padL - padR), Math.Max(8, bounds.Height - padT - padB));
        var grid = Brush(Color.Parse("#2E3A4A"));
        var label = Brush(Color.Parse("#9AABBC"));
        var offered = Brush(Color.Parse("#C5D2E0"));
        var ink = Brush(Color.Parse("#4C8DFF"));

        for (int tick = 0; tick <= 2; tick++)
        {
            double value = maxY * tick / 2.0;
            double y = plot.Bottom - plot.Height * tick / 2.0;
            context.DrawLine(new Pen(grid, 1), new Point(plot.Left, y), new Point(plot.Right, y));
            string text = maxY >= 10 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.0", CultureInfo.InvariantCulture);
            var formatted = Format(text, 11, label);
            context.DrawText(formatted, new Point(plot.Left - 8 - formatted.Width, y - formatted.Height / 2));
        }

        var startLabel = Format("0", 11, label);
        context.DrawText(startLabel, new Point(plot.Left, plot.Bottom + 4));
        string end = SpanMinutes >= 0.5
            ? SpanMinutes.ToString("0", CultureInfo.InvariantCulture) + " min"
            : UiText.SimTime;
        var endLabel = Format(end, 11, label);
        context.DrawText(endLabel, new Point(Math.Max(plot.Left + startLabel.Width + 8, plot.Right - endLabel.Width), plot.Bottom + 4));

        if (Offered > 0)
        {
            double y = plot.Bottom - plot.Height * Math.Clamp(Offered / maxY, 0, 1);
            var pen = new Pen(offered, 1.4) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            context.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            var caption = Format(UiText.Offered, 11, offered);
            context.DrawText(caption, new Point(plot.Right - caption.Width, 3));
        }

        if (samples.Count == 0)
        {
            var hint = Format(UiText.RateWarmingUp, 13, label);
            context.DrawText(hint, new Point(plot.Left + 8, plot.Top + 6));
            return;
        }

        var geometry = new StreamGeometry();
        using (var geo = geometry.Open())
        {
            for (int index = 0; index < samples.Count; index++)
            {
                double x = samples.Count == 1
                    ? plot.Left
                    : plot.Left + plot.Width * index / (samples.Count - 1);
                double y = plot.Bottom - plot.Height * Math.Clamp(samples[index] / maxY, 0, 1);
                var point = new Point(x, y);
                if (index == 0)
                {
                    geo.BeginFigure(point, false);
                }
                else
                {
                    geo.LineTo(point);
                }
            }
        }

        context.DrawGeometry(null, new Pen(ink, 2.4), geometry);
    }

    private static double NiceMax(double value)
    {
        if (value <= 1e-6)
        {
            return 10;
        }

        double padded = Math.Max(10, value * 1.2);
        double exponent = Math.Pow(10, Math.Floor(Math.Log10(padded)));
        double[] steps = { 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };
        foreach (double step in steps)
        {
            double candidate = step * exponent;
            if (candidate + 1e-6 >= padded)
            {
                return candidate;
            }
        }

        return 10 * exponent;
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
