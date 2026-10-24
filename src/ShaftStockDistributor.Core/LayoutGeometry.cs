namespace ShaftStockDistributor.Core;

/// <summary>Positions, zones, and derived shaft capacity for one layout.</summary>
public sealed class LayoutGeometry
{
    private readonly List<ShuttleRange> _ranges;

    public LayoutGeometry(LayoutSettings layout, IReadOnlyList<ShuttleSettings>? shuttles = null)
    {
        Layout = layout;
        SegmentCount = Math.Max(0, layout.SegmentCount);
        Levels = Math.Max(0, layout.LevelsPerSegment);
        ShaftsPerLevel = Math.Max(0, layout.ShaftsPerLevel);
        SegmentWidthMeters = ShaftsPerLevel * layout.ShaftPitchMeters;
        TotalLengthMeters = SegmentCount <= 0
            ? 0
            : SegmentCount * SegmentWidthMeters + Math.Max(0, SegmentCount - 1) * layout.SegmentGapMeters;
        TotalHeightMeters = Math.Max(0, Levels - 1) * layout.LevelHeightMeters;
        ExclusiveSegmentCount = SegmentCount / 3;
        _ranges = Resolve(SegmentCount, shuttles);
    }

    public static LayoutGeometry For(DistributorProject project) => new(project.Layout, project.Shuttles);

    public LayoutSettings Layout { get; }

    public int SegmentCount { get; }

    public int Levels { get; }

    public int ShaftsPerLevel { get; }

    /// <summary>Length of each exclusive end in the two-shuttle thirds preset. Custom ranges do not use this number.</summary>
    public int ExclusiveSegmentCount { get; }

    public double SegmentWidthMeters { get; }

    public double TotalLengthMeters { get; }

    public double TotalHeightMeters { get; }

    public int ShaftCount => SegmentCount * Levels * ShaftsPerLevel;

    public IReadOnlyList<ShuttleId> ServersOf(int segment)
    {
        if (segment < 1 || segment > SegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(segment));
        }

        return _ranges
            .Where(range => segment >= range.From && segment <= range.To)
            .Select(range => range.Id)
            .ToArray();
    }

    public bool IsShared(int segment) => ServersOf(segment).Count > 1;

    public ShuttleId? ExclusiveOwner(int segment)
    {
        var servers = ServersOf(segment);
        return servers.Count == 1 ? servers[0] : null;
    }

    public IReadOnlyList<int> ExclusiveSegments(ShuttleId shuttle)
    {
        var segments = new List<int>();
        for (int segment = 1; segment <= SegmentCount; segment++)
        {
            if (ExclusiveOwner(segment) == shuttle)
            {
                segments.Add(segment);
            }
        }

        return segments;
    }

    public ZoneKind ZoneOfSegment(int segment)
    {
        var servers = ServersOf(segment);
        if (servers.Count == 0)
        {
            throw new InvalidOperationException($"Segment {segment} is not covered by a shuttle.");
        }

        if (servers.Count > 1)
        {
            return ZoneKind.Shared;
        }

        if (servers[0] == ShuttleId.Shuttle1)
        {
            return ZoneKind.Shuttle1Only;
        }

        if (servers[0] == ShuttleId.Shuttle2)
        {
            return ZoneKind.Shuttle2Only;
        }

        return ZoneKind.Exclusive;
    }

    public string ZoneLabel(int segment)
    {
        var servers = ServersOf(segment);
        if (servers.Count == 0)
        {
            return "Uncovered";
        }

        if (servers.Count == 1)
        {
            return "Shuttle " + servers[0].Number.ToString();
        }

        return "Shared " + string.Join("+", servers.Select(server => server.Number.ToString()));
    }

    public bool ShuttleCanServe(ShuttleId shuttle, int segment)
    {
        if (segment < 1 || segment > SegmentCount)
        {
            return false;
        }

        return ServersOf(segment).Contains(shuttle);
    }

    public bool ShuttleCanServe(ShuttleId shuttle, ZoneKind zone) => zone switch
    {
        ZoneKind.Shuttle1Only => shuttle == ShuttleId.Shuttle1,
        ZoneKind.Shuttle2Only => shuttle == ShuttleId.Shuttle2,
        ZoneKind.Shared => true,
        _ => false
    };

    public double ShaftX(int segment, int shaft)
    {
        double pitch = Layout.ShaftPitchMeters;
        double gap = Layout.SegmentGapMeters;
        return (segment - 1) * (SegmentWidthMeters + gap) + (shaft - 0.5) * pitch;
    }

    public double LevelY(int level) => (level - 1) * Layout.LevelHeightMeters;

    public (double Start, double End) SegmentSpan(int segment)
    {
        double start = (segment - 1) * (SegmentWidthMeters + Layout.SegmentGapMeters);
        return (start, start + SegmentWidthMeters);
    }

    public int SegmentAt(double x)
    {
        for (int segment = 1; segment <= SegmentCount; segment++)
        {
            var span = SegmentSpan(segment);
            if (x >= span.Start - 1e-9 && x <= span.End + 1e-9)
            {
                return segment;
            }
        }

        return 0;
    }

    public (double Start, double End) ZoneSpan(ZoneKind zone)
    {
        if (SegmentCount == 0)
        {
            return (0, 0);
        }

        int? first = null;
        int last = 0;
        for (int segment = 1; segment <= SegmentCount; segment++)
        {
            if (ServersOf(segment).Count == 0 || ZoneOfSegment(segment) != zone)
            {
                continue;
            }

            first ??= segment;
            last = segment;
        }

        if (first is null)
        {
            return (0, 0);
        }

        var start = SegmentSpan(first.Value);
        var end = SegmentSpan(last);
        return (start.Start, end.End);
    }

    public IReadOnlyList<(double Start, double End)> SharedSpans()
    {
        var spans = new List<(double Start, double End)>();
        int? runStart = null;
        int runEnd = 0;
        for (int segment = 1; segment <= SegmentCount; segment++)
        {
            if (ServersOf(segment).Count > 1)
            {
                runStart ??= segment;
                runEnd = segment;
                continue;
            }

            if (runStart is int start)
            {
                spans.Add(Merge(start, runEnd));
                runStart = null;
            }
        }

        if (runStart is int last)
        {
            spans.Add(Merge(last, runEnd));
        }

        return spans;
    }

    public (double Start, double End) PrimarySharedSpan()
    {
        var spans = SharedSpans();
        if (spans.Count == 0)
        {
            return (0, 0);
        }

        double mid = TotalLengthMeters / 2.0;
        return spans
            .OrderByDescending(span => span.End - span.Start)
            .ThenBy(span => Math.Abs((span.Start + span.End) / 2.0 - mid))
            .First();
    }

    public bool ContainsShared(double x)
    {
        foreach (var span in SharedSpans())
        {
            if (x >= span.Start - 1e-9 && x <= span.End + 1e-9)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many crates of this article fit in any shaft of this layout.</summary>
    public int CapacityFor(Article article)
    {
        double pitch = article.Dimensions.FlowLengthMeters + Layout.CrateGapMeters;
        if (pitch <= 1e-9 || Layout.ShaftLengthMeters <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(Layout.ShaftLengthMeters / pitch);
    }

    public IEnumerable<ShaftAddress> AllAddresses()
    {
        for (int segment = 1; segment <= SegmentCount; segment++)
        {
            for (int level = 1; level <= Levels; level++)
            {
                for (int shaft = 1; shaft <= ShaftsPerLevel; shaft++)
                {
                    yield return new ShaftAddress(segment, level, shaft);
                }
            }
        }
    }

    private (double Start, double End) Merge(int first, int last)
    {
        var start = SegmentSpan(first);
        var end = SegmentSpan(last);
        return (start.Start, end.End);
    }

    private static List<ShuttleRange> Resolve(int segmentCount, IReadOnlyList<ShuttleSettings>? shuttles)
    {
        if (shuttles is null || shuttles.Count == 0
            || (shuttles.All(ShuttleLayout.RangesUnset) && ShuttleLayout.IsClassicPair(shuttles)))
        {
            return ShuttleLayout.TwoShuttleRanges(segmentCount)
                .Select(range => new ShuttleRange(range.Id, range.From, range.To))
                .ToList();
        }

        return shuttles
            .OrderBy(shuttle => shuttle.Id)
            .Select(shuttle => new ShuttleRange(shuttle.Id, shuttle.SegmentFrom, shuttle.SegmentTo))
            .ToList();
    }

    public readonly record struct ShuttleRange(ShuttleId Id, int From, int To);
}
