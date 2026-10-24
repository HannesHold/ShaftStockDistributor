namespace ShaftStockDistributor.Core;

/// <summary>Positions, zones, and derived shaft capacity for one layout.</summary>
public sealed class LayoutGeometry
{
    public LayoutGeometry(LayoutSettings layout)
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
    }

    public LayoutSettings Layout { get; }

    public int SegmentCount { get; }

    public int Levels { get; }

    public int ShaftsPerLevel { get; }

    public int ExclusiveSegmentCount { get; }

    public double SegmentWidthMeters { get; }

    public double TotalLengthMeters { get; }

    public double TotalHeightMeters { get; }

    public int ShaftCount => SegmentCount * Levels * ShaftsPerLevel;

    public ZoneKind ZoneOfSegment(int segment)
    {
        if (segment < 1 || segment > SegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(segment));
        }

        if (segment <= ExclusiveSegmentCount)
        {
            return ZoneKind.Shuttle1Only;
        }

        if (segment > SegmentCount - ExclusiveSegmentCount)
        {
            return ZoneKind.Shuttle2Only;
        }

        return ZoneKind.Shared;
    }

    public bool ShuttleCanServe(ShuttleId shuttle, ZoneKind zone) => zone switch
    {
        ZoneKind.Shuttle1Only => shuttle == ShuttleId.Shuttle1,
        ZoneKind.Shuttle2Only => shuttle == ShuttleId.Shuttle2,
        _ => true
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

    public (double Start, double End) ZoneSpan(ZoneKind zone)
    {
        if (SegmentCount == 0)
        {
            return (0, 0);
        }

        int first = 1;
        int last = SegmentCount;
        switch (zone)
        {
            case ZoneKind.Shuttle1Only:
                last = Math.Max(1, ExclusiveSegmentCount);
                if (ExclusiveSegmentCount == 0)
                {
                    return (0, 0);
                }

                break;
            case ZoneKind.Shuttle2Only:
                if (ExclusiveSegmentCount == 0)
                {
                    return (0, 0);
                }

                first = SegmentCount - ExclusiveSegmentCount + 1;
                break;
            default:
                first = ExclusiveSegmentCount + 1;
                last = SegmentCount - ExclusiveSegmentCount;
                if (first > last)
                {
                    return (0, 0);
                }

                break;
        }

        var a = SegmentSpan(first);
        var b = SegmentSpan(last);
        return (a.Start, b.End);
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
}
