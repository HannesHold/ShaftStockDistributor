using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.Controls;

/// <summary>
/// Front elevation of the rack row. Shaft columns follow the segment geometry,
/// and shuttle X is mapped through the same columns so a moving shuttle stays
/// aligned with the shaft it is traveling toward.
/// </summary>
public sealed class WarehouseCanvas : Control
{
    public static readonly StyledProperty<WarehouseSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<WarehouseCanvas, WarehouseSnapshot?>(nameof(Snapshot));

    public static readonly StyledProperty<bool> ShowHeatmapProperty =
        AvaloniaProperty.Register<WarehouseCanvas, bool>(nameof(ShowHeatmap));

    public static readonly StyledProperty<ShaftAddress?> SelectedAddressProperty =
        AvaloniaProperty.Register<WarehouseCanvas, ShaftAddress?>(nameof(SelectedAddress));

    public static readonly StyledProperty<ICommand?> ShaftClickCommandProperty =
        AvaloniaProperty.Register<WarehouseCanvas, ICommand?>(nameof(ShaftClickCommand));

    private readonly List<(Rect Rect, ShaftAddress Address)> _hits = new();
    private readonly Dictionary<uint, IBrush> _brushes = new();

    static WarehouseCanvas()
    {
        AffectsRender<WarehouseCanvas>(SnapshotProperty, ShowHeatmapProperty, SelectedAddressProperty);
    }

    public WarehouseSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public bool ShowHeatmap
    {
        get => GetValue(ShowHeatmapProperty);
        set => SetValue(ShowHeatmapProperty, value);
    }

    public ShaftAddress? SelectedAddress
    {
        get => GetValue(SelectedAddressProperty);
        set => SetValue(SelectedAddressProperty, value);
    }

    public ICommand? ShaftClickCommand
    {
        get => GetValue(ShaftClickCommandProperty);
        set => SetValue(ShaftClickCommandProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        _hits.Clear();
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush(Color.Parse("#101820")), bounds);
        var snapshot = Snapshot;
        if (snapshot is null || snapshot.SegmentCount <= 0 || snapshot.Levels <= 0 || snapshot.ShaftsPerLevel <= 0)
        {
            DrawLabel(context, UiText.DemoHint, new Point(24, 24), 14, Brush(Color.Parse("#9AABBC")));
            return;
        }

        const double padL = 36;
        const double padR = 16;
        const double padT = 28;
        const double padB = 22;
        var plot = new Rect(padL, padT, Math.Max(10, bounds.Width - padL - padR), Math.Max(10, bounds.Height - padT - padB));
        double segmentGapPx = Math.Clamp(plot.Width * 0.012, 6, 16);
        double levelGapPx = 4;
        double cellW = (plot.Width - (snapshot.SegmentCount - 1) * segmentGapPx) / (snapshot.SegmentCount * snapshot.ShaftsPerLevel);
        double cellH = (plot.Height - (snapshot.Levels - 1) * levelGapPx) / snapshot.Levels;
        int exclusive = snapshot.SegmentCount / 3;
        int maxPulls = 1;
        foreach (var shaft in snapshot.Shafts)
        {
            maxPulls = Math.Max(maxPulls, shaft.PullCount);
        }

        for (int segment = 1; segment <= snapshot.SegmentCount; segment++)
        {
            double x = SegmentX(plot, segment, snapshot.ShaftsPerLevel, cellW, segmentGapPx);
            double width = snapshot.ShaftsPerLevel * cellW;
            var zone = ZoneOf(segment, snapshot.SegmentCount, exclusive);
            var band = new Rect(x - 2, plot.Y - 4, width + 4, plot.Height + 8);
            context.FillRectangle(Brush(ZoneColor(zone)), band);
            string label = zone switch
            {
                ZoneKind.Shuttle1Only => "1",
                ZoneKind.Shuttle2Only => "2",
                _ => "S"
            };
            DrawLabel(context, segment.ToString(CultureInfo.InvariantCulture), new Point(x, 6), 11, Brush(Color.Parse("#9AABBC")));
            DrawLabel(context, label, new Point(x + width - 12, 6), 11, Brush(ZoneInk(zone)));
        }

        foreach (var shaft in snapshot.Shafts)
        {
            double x = SegmentX(plot, shaft.Segment, snapshot.ShaftsPerLevel, cellW, segmentGapPx) + (shaft.Shaft - 1) * cellW;
            double y = plot.Y + (snapshot.Levels - shaft.Level) * (cellH + levelGapPx);
            var rect = new Rect(x + 0.6, y + 0.6, Math.Max(1, cellW - 1.2), Math.Max(1, cellH - 1.2));
            _hits.Add((rect, shaft.Address));
            context.FillRectangle(Brush(Color.Parse("#243447")), rect);
            context.DrawRectangle(null, new Pen(Brush(Color.Parse("#31465C")), 0.8), rect);
            Color ink = ShowHeatmap ? Heat(shaft.PullCount, maxPulls) : Parse(shaft.ColorHex);
            if (!string.IsNullOrEmpty(shaft.ArticleId))
            {
                double fill = Math.Clamp(shaft.VisualFill, 0, 1);
                double bar = Math.Max(fill > 0 ? 2 : 0, rect.Height * fill);
                if (bar > 0)
                {
                    context.FillRectangle(Brush(ink), new Rect(rect.X, rect.Bottom - bar, rect.Width, bar));
                }

                context.FillRectangle(Brush(ink), new Rect(rect.X, rect.Y, rect.Width, Math.Min(3, rect.Height)));
            }

            if (shaft.Transferring)
            {
                context.DrawRectangle(null, new Pen(Brush(Color.Parse("#F4F7FB")), 1.4), rect, 1, 1);
            }

            if (shaft.Pinned)
            {
                context.FillRectangle(Brush(Color.Parse("#F4F7FB")), new Rect(rect.Right - 4, rect.Y + 1, 3, 3));
            }

            if (SelectedAddress is ShaftAddress selected && selected == shaft.Address)
            {
                context.DrawRectangle(null, new Pen(Brush(Color.Parse("#8EBEFF")), 1.6), rect.Inflate(0.4), 1, 1);
            }
        }

        if (snapshot.SharedZoneLocked)
        {
            var span = ZonePixelSpan(plot, snapshot, cellW, segmentGapPx, ZoneKind.Shared, exclusive);
            if (span.Width > 0)
            {
                context.FillRectangle(Brush(Color.Parse("#40F0A202")), span);
                DrawLabel(context, UiText.SharedLock, new Point(span.X + 6, span.Y + 6), 12, Brush(Color.Parse("#F0A202")));
            }
        }

        foreach (var shuttle in snapshot.Shuttles)
        {
            double cx = MetersToX(plot, snapshot, shuttle.XMeters, cellW, segmentGapPx);
            double cy = MetersToY(plot, snapshot, shuttle.YMeters, cellH, levelGapPx);
            double w = Math.Clamp(cellW * 2.4, 16, 36);
            double h = Math.Clamp(cellH * 0.42, 12, 22);
            var body = new Rect(cx - w / 2, cy - h / 2, w, h);
            Color color = shuttle.Id == ShuttleId.Shuttle1 ? Color.Parse("#4C8DFF") : Color.Parse("#F0A202");
            context.DrawRectangle(Brush(color), new Pen(Brush(Color.Parse("#F7FBFF")), 1.4), body, 4, 4);
            if (shuttle.IsBlocked)
            {
                context.DrawRectangle(null, new Pen(Brush(Color.Parse("#E85D75")), 2.2), body.Inflate(3), 5, 5);
                DrawLabel(context, "WAIT", new Point(body.X, body.Y - 14), 10, Brush(Color.Parse("#E85D75")));
            }

            DrawLabel(context, shuttle.Id == ShuttleId.Shuttle1 ? "1" : "2", new Point(body.X + 5, body.Y + 1), 11, Brushes.White);
        }

        DrawLabel(context, UiText.ZoneShuttle1, new Point(padL, bounds.Height - 16), 11, Brush(Color.Parse("#6FA0E8")));
        DrawLabel(context, UiText.ZoneShared, new Point(padL + 110, bounds.Height - 16), 11, Brush(Color.Parse("#E0B15A")));
        DrawLabel(context, UiText.ZoneShuttle2, new Point(padL + 230, bounds.Height - 16), 11, Brush(Color.Parse("#B79CFF")));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        foreach (var hit in _hits)
        {
            if (!hit.Rect.Contains(point))
            {
                continue;
            }

            if (ShaftClickCommand?.CanExecute(hit.Address) == true)
            {
                ShaftClickCommand.Execute(hit.Address);
            }

            e.Handled = true;
            return;
        }
    }

    private static double SegmentX(Rect plot, int segment, int shaftsPerLevel, double cellW, double segmentGapPx) =>
        plot.X + (segment - 1) * (shaftsPerLevel * cellW + segmentGapPx);

    private static ZoneKind ZoneOf(int segment, int segmentCount, int exclusive)
    {
        if (exclusive <= 0)
        {
            return ZoneKind.Shared;
        }

        if (segment <= exclusive)
        {
            return ZoneKind.Shuttle1Only;
        }

        return segment > segmentCount - exclusive ? ZoneKind.Shuttle2Only : ZoneKind.Shared;
    }

    private static Color ZoneColor(ZoneKind zone) => zone switch
    {
        ZoneKind.Shuttle1Only => Color.Parse("#14283C"),
        ZoneKind.Shuttle2Only => Color.Parse("#241C38"),
        _ => Color.Parse("#2A2618")
    };

    private static Color ZoneInk(ZoneKind zone) => zone switch
    {
        ZoneKind.Shuttle1Only => Color.Parse("#6FA0E8"),
        ZoneKind.Shuttle2Only => Color.Parse("#B79CFF"),
        _ => Color.Parse("#E0B15A")
    };

    private static Color Heat(int pulls, int max)
    {
        double t = Math.Clamp(pulls / (double)max, 0, 1);
        byte r = (byte)(40 + t * 192);
        byte g = (byte)(90 - t * 40);
        byte b = (byte)(80 - t * 40);
        return Color.FromRgb(r, g, b);
    }

    private static Color Parse(string hex)
    {
        try
        {
            return Color.Parse(string.IsNullOrWhiteSpace(hex) ? "#5C6770" : hex);
        }
        catch (FormatException)
        {
            return Color.Parse("#5C6770");
        }
    }

    private double MetersToX(Rect plot, WarehouseSnapshot snapshot, double meters, double cellW, double segmentGapPx)
    {
        double pitch = Math.Max(0.0001, snapshot.ShaftPitchMeters);
        double segmentWidth = snapshot.ShaftsPerLevel * pitch;
        double gap = Math.Max(0, snapshot.SegmentGapMeters);
        double stride = segmentWidth + gap;
        int index = stride <= 0 ? 0 : (int)Math.Floor(meters / stride);
        index = Math.Clamp(index, 0, Math.Max(0, snapshot.SegmentCount - 1));
        double within = meters - index * stride;
        double origin = SegmentX(plot, index + 1, snapshot.ShaftsPerLevel, cellW, segmentGapPx);
        double shaftSpan = snapshot.ShaftsPerLevel * cellW;
        if (within <= segmentWidth)
        {
            return origin + (within / segmentWidth) * shaftSpan;
        }

        double gapT = gap <= 0 ? 0 : (within - segmentWidth) / gap;
        return origin + shaftSpan + gapT * segmentGapPx;
    }

    private static double MetersToY(Rect plot, WarehouseSnapshot snapshot, double yMeters, double cellH, double levelGapPx)
    {
        double bottom = plot.Y + (snapshot.Levels - 1) * (cellH + levelGapPx) + cellH / 2;
        if (snapshot.TotalHeightMeters <= 0.0001 || snapshot.Levels <= 1)
        {
            return bottom;
        }

        double t = Math.Clamp(yMeters / snapshot.TotalHeightMeters, 0, 1);
        double top = plot.Y + cellH / 2;
        return bottom + (top - bottom) * t;
    }

    private Rect ZonePixelSpan(Rect plot, WarehouseSnapshot snapshot, double cellW, double segmentGapPx, ZoneKind zone, int exclusive)
    {
        if (exclusive <= 0)
        {
            return new Rect(plot.X, plot.Y, plot.Width, plot.Height);
        }

        int first = zone switch
        {
            ZoneKind.Shuttle1Only => 1,
            ZoneKind.Shuttle2Only => snapshot.SegmentCount - exclusive + 1,
            _ => exclusive + 1
        };
        int last = zone switch
        {
            ZoneKind.Shuttle1Only => exclusive,
            ZoneKind.Shuttle2Only => snapshot.SegmentCount,
            _ => snapshot.SegmentCount - exclusive
        };
        if (first > last)
        {
            return default;
        }

        double x = SegmentX(plot, first, snapshot.ShaftsPerLevel, cellW, segmentGapPx);
        double end = SegmentX(plot, last, snapshot.ShaftsPerLevel, cellW, segmentGapPx) + snapshot.ShaftsPerLevel * cellW;
        return new Rect(x, plot.Y, Math.Max(0, end - x), plot.Height);
    }

    private IBrush Brush(Color color)
    {
        uint key = color.ToUInt32();
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(color);
            _brushes[key] = brush;
        }

        return brush;
    }

    private static void DrawLabel(DrawingContext context, string text, Point origin, double size, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        context.DrawText(formatted, origin);
    }
}
