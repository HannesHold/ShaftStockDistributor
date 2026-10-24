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

    private readonly List<(Rect Rect, ShaftAddress Address, string Tip)> _hits = new();
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
        const double padB = 36;
        var plot = new Rect(padL, padT, Math.Max(10, bounds.Width - padL - padR), Math.Max(10, bounds.Height - padT - padB));
        double segmentGapPx = Math.Clamp(plot.Width * 0.012, 6, 16);
        double levelGapPx = 4;
        double cellW = (plot.Width - (snapshot.SegmentCount - 1) * segmentGapPx) / (snapshot.SegmentCount * snapshot.ShaftsPerLevel);
        double cellH = (plot.Height - (snapshot.Levels - 1) * levelGapPx) / snapshot.Levels;
        int maxPulls = 1;
        foreach (var shaft in snapshot.Shafts)
        {
            maxPulls = Math.Max(maxPulls, shaft.PullCount);
        }

        for (int segment = 1; segment <= snapshot.SegmentCount; segment++)
        {
            double x = SegmentX(plot, segment, snapshot.ShaftsPerLevel, cellW, segmentGapPx);
            double width = snapshot.ShaftsPerLevel * cellW;
            var info = BandOf(snapshot, segment);
            var band = new Rect(x - 2, plot.Y - 4, width + 4, plot.Height + 8);
            context.FillRectangle(Brush(ZoneColor(info)), band);
            DrawLabel(context, segment.ToString(CultureInfo.InvariantCulture), new Point(x, 6), 11, Brush(Color.Parse("#9AABBC")));
            string label = string.IsNullOrEmpty(info.Label) ? "?" : info.Label;
            DrawLabel(context, label, new Point(x + Math.Max(8, width - 8 - label.Length * 7), 6), 11, Brush(ZoneInk(info)));
        }

        foreach (var shaft in snapshot.Shafts)
        {
            double x = SegmentX(plot, shaft.Segment, snapshot.ShaftsPerLevel, cellW, segmentGapPx) + (shaft.Shaft - 1) * cellW;
            double y = plot.Y + (snapshot.Levels - shaft.Level) * (cellH + levelGapPx);
            var rect = new Rect(x + 0.6, y + 0.6, Math.Max(1, cellW - 1.2), Math.Max(1, cellH - 1.2));
            string article = string.IsNullOrEmpty(shaft.ArticleName) ? "Empty" : shaft.ArticleName;
            _hits.Add((rect, shaft.Address, $"{shaft.Address} · {article} · {shaft.Count}/{shaft.Capacity} · {shaft.Zone}"));
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

            if (shaft.Transferring || shaft.RecentlyPulled)
            {
                context.DrawRectangle(null, new Pen(Brush(Color.Parse(shaft.RecentlyPulled ? "#F0A202" : "#F4F7FB")), 1.8), rect, 1, 1);
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
            bool labeled = false;
            foreach (var run in SharedRunsOf(snapshot))
            {
                var span = SegmentPixelSpan(plot, snapshot, cellW, segmentGapPx, run.First, run.Last);
                if (span.Width <= 0)
                {
                    continue;
                }

                context.FillRectangle(Brush(Color.Parse("#40F0A202")), span);
                if (!labeled)
                {
                    DrawLabel(context, UiText.SharedLock, new Point(span.X + 6, span.Y + 6), 12, Brush(Color.Parse("#F0A202")));
                    labeled = true;
                }
            }
        }

        foreach (var shuttle in snapshot.Shuttles)
        {
            double cx = MetersToX(plot, snapshot, shuttle.XMeters, cellW, segmentGapPx);
            double cy = MetersToY(plot, snapshot, shuttle.YMeters, cellH, levelGapPx);
            if (shuttle.TargetXMeters is double targetX && shuttle.TargetYMeters is double targetY)
            {
                double tx = MetersToX(plot, snapshot, targetX, cellW, segmentGapPx);
                double ty = MetersToY(plot, snapshot, targetY, cellH, levelGapPx);
                if (Math.Abs(tx - cx) + Math.Abs(ty - cy) > 4)
                {
                    Color path = ShuttleColor(shuttle.Id.Number);
                    context.DrawLine(new Pen(Brush(path), 2), new Point(cx, cy), new Point(tx, ty));
                    context.DrawEllipse(Brush(path), null, new Point(tx, ty), 5, 5);
                }
            }

            double w = Math.Clamp(cellW * 3.2, 22, 44);
            double h = Math.Clamp(cellH * 0.55, 16, 28);
            var body = new Rect(cx - w / 2, cy - h / 2, w, h);
            Color color = ShuttleColor(shuttle.Id.Number);
            context.DrawRectangle(Brush(color), new Pen(Brush(Color.Parse("#F7FBFF")), 1.4), body, 4, 4);
            if (shuttle.IsBlocked)
            {
                context.DrawRectangle(null, new Pen(Brush(Color.Parse("#E85D75")), 2.2), body.Inflate(3), 5, 5);
                DrawLabel(context, "WAIT", new Point(body.X, body.Y - 14), 10, Brush(Color.Parse("#E85D75")));
            }

            DrawLabel(context, shuttle.Id.Number.ToString(CultureInfo.InvariantCulture), new Point(body.X + 5, body.Y + 1), 11, Brushes.White);
        }

        DrawZoneLegend(context, bounds, padL, snapshot);
    }

    private void DrawZoneLegend(DrawingContext context, Rect bounds, double padL, WarehouseSnapshot snapshot)
    {
        var items = new List<(string Text, Color Color)>();
        var seen = new HashSet<int>();
        foreach (var band in snapshot.Bands)
        {
            if (band.Shared || band.OwnerNumber <= 0 || !seen.Add(band.OwnerNumber))
            {
                continue;
            }

            items.Add((band.OwnerNumber.ToString(CultureInfo.InvariantCulture), ShuttleColor(band.OwnerNumber)));
        }

        if (items.Count == 0)
        {
            foreach (var shuttle in snapshot.Shuttles)
            {
                if (seen.Add(shuttle.Id.Number))
                {
                    items.Add((shuttle.Id.Number.ToString(CultureInfo.InvariantCulture), ShuttleColor(shuttle.Id.Number)));
                }
            }
        }

        if (snapshot.Bands.Any(band => band.Shared) || snapshot.SharedRuns.Count > 0)
        {
            items.Add((UiText.ZoneShared, Color.Parse("#E0B15A")));
        }
        double y = Math.Max(4, bounds.Height - 20);
        double x = padL;
        foreach (var item in items)
        {
            var formatted = new FormattedText(
                item.Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11,
                Brush(item.Color));
            if (x > padL && x + formatted.Width > bounds.Width - 8)
            {
                break;
            }

            context.DrawText(formatted, new Point(x, y));
            x += formatted.Width + 16;
        }
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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        foreach (var hit in _hits)
        {
            if (!hit.Rect.Contains(point))
            {
                continue;
            }

            ToolTip.SetTip(this, hit.Tip);
            return;
        }

        ToolTip.SetTip(this, null);
    }

    private static double SegmentX(Rect plot, int segment, int shaftsPerLevel, double cellW, double segmentGapPx) =>
        plot.X + (segment - 1) * (shaftsPerLevel * cellW + segmentGapPx);

    private static readonly Color[] ShuttleColors =
    [
        Color.Parse("#4C8DFF"),
        Color.Parse("#F0A202"),
        Color.Parse("#3DDC97"),
        Color.Parse("#E85D75"),
        Color.Parse("#B79CFF"),
        Color.Parse("#4ECDC4"),
        Color.Parse("#F7FBFF"),
        Color.Parse("#C084FC")
    ];

    private static Color ShuttleColor(int number)
    {
        int index = Math.Clamp(number, 1, 64) - 1;
        return ShuttleColors[index % ShuttleColors.Length];
    }

    private static SegmentBand BandOf(WarehouseSnapshot snapshot, int segment)
    {
        var band = snapshot.Bands.FirstOrDefault(item => item.Segment == segment);
        if (band is not null)
        {
            return band;
        }

        int exclusive = snapshot.SegmentCount / 3;
        if (exclusive <= 0)
        {
            return new SegmentBand { Segment = segment, Shared = true, Label = "S", Zone = ZoneKind.Shared };
        }

        if (segment <= exclusive)
        {
            return new SegmentBand { Segment = segment, OwnerNumber = 1, Label = "1", Zone = ZoneKind.Shuttle1Only };
        }

        if (segment > snapshot.SegmentCount - exclusive)
        {
            return new SegmentBand { Segment = segment, OwnerNumber = 2, Label = "2", Zone = ZoneKind.Shuttle2Only };
        }

        return new SegmentBand { Segment = segment, Shared = true, Label = "S", Zone = ZoneKind.Shared };
    }

    private static IReadOnlyList<(int First, int Last)> SharedRunsOf(WarehouseSnapshot snapshot)
    {
        if (snapshot.SharedRuns.Count > 0)
        {
            return snapshot.SharedRuns.Select(run => (run.FirstSegment, run.LastSegment)).ToArray();
        }

        int exclusive = snapshot.SegmentCount / 3;
        if (exclusive <= 0)
        {
            return [(1, snapshot.SegmentCount)];
        }

        int first = exclusive + 1;
        int last = snapshot.SegmentCount - exclusive;
        return first <= last ? [(first, last)] : [];
    }

    private static Color ZoneColor(SegmentBand band)
    {
        if (band.Shared || band.OwnerNumber <= 0)
        {
            return Color.Parse("#2A2618");
        }

        var ink = ShuttleColor(band.OwnerNumber);
        return Color.FromRgb(
            (byte)(ink.R * 0.16 + 14),
            (byte)(ink.G * 0.16 + 18),
            (byte)(ink.B * 0.16 + 28));
    }

    private static Color ZoneInk(SegmentBand band) =>
        band.Shared || band.OwnerNumber <= 0 ? Color.Parse("#E0B15A") : ShuttleColor(band.OwnerNumber);

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

    private static Rect SegmentPixelSpan(Rect plot, WarehouseSnapshot snapshot, double cellW, double segmentGapPx, int first, int last)
    {
        if (first < 1 || last < first || last > snapshot.SegmentCount)
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
