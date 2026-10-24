namespace ShaftStockDistributor.Core;

/// <summary>
/// Builds shuttle ranges. The two-shuttle thirds preset is the historical aisle:
/// each end keeps <c>segmentCount / 3</c> segments, and both shuttles cover the middle.
/// Even split hands the segments out in order and lets neighbours share a few.
/// </summary>
public static class ShuttleLayout
{
    public static bool IsClassicPair(IReadOnlyList<ShuttleSettings> shuttles) =>
        shuttles.Count == 2
        && shuttles.Any(shuttle => shuttle.Id == ShuttleId.Shuttle1)
        && shuttles.Any(shuttle => shuttle.Id == ShuttleId.Shuttle2);

    public static bool RangesUnset(ShuttleSettings shuttle) => shuttle.SegmentFrom <= 0 && shuttle.SegmentTo <= 0;

    public static void Ensure(DistributorProject project)
    {
        if (project.Shuttles.Count == 0)
        {
            ApplyTwoShuttleThirds(project, resetMotion: true);
            return;
        }

        FillUnsetRanges(project);
        project.Shuttles = project.Shuttles.OrderBy(shuttle => shuttle.Id).ToList();
    }

    public static void FillUnsetRanges(DistributorProject project)
    {
        if (project.Shuttles.Count == 0)
        {
            return;
        }

        if (project.Shuttles.Any(shuttle => !RangesUnset(shuttle)))
        {
            project.Shuttles = project.Shuttles.OrderBy(shuttle => shuttle.Id).ToList();
            return;
        }

        if (IsClassicPair(project.Shuttles))
        {
            ApplyTwoShuttleThirds(project, resetMotion: false);
            return;
        }

        AssignEvenRanges(project, resetHomes: false);
    }

    /// <summary>
    /// Shuttle 1 serves <c>1 .. count - exclusive</c>. Shuttle 2 serves <c>exclusive + 1 .. count</c>.
    /// <c>exclusive</c> is <c>count / 3</c>, so the overlap is the old shared middle.
    /// </summary>
    public static void ApplyTwoShuttleThirds(DistributorProject project, bool resetMotion)
    {
        var geometry = new LayoutGeometry(project.Layout);
        var ranges = TwoShuttleRanges(geometry.SegmentCount);
        var first = project.Shuttles.FirstOrDefault(shuttle => shuttle.Id == ShuttleId.Shuttle1)
            ?? ProjectGrid.DefaultShuttle(ShuttleId.Shuttle1, 0);
        var second = project.Shuttles.FirstOrDefault(shuttle => shuttle.Id == ShuttleId.Shuttle2)
            ?? ProjectGrid.DefaultShuttle(ShuttleId.Shuttle2, geometry.TotalLengthMeters);
        first.SegmentFrom = ranges[0].From;
        first.SegmentTo = ranges[0].To;
        second.SegmentFrom = ranges[1].From;
        second.SegmentTo = ranges[1].To;
        if (resetMotion)
        {
            SetPose(first, 0);
            SetPose(second, geometry.TotalLengthMeters);
        }

        project.Shuttles = [first, second];
    }

    public static (ShuttleId Id, int From, int To)[] TwoShuttleRanges(int segmentCount)
    {
        int count = Math.Max(0, segmentCount);
        if (count == 0)
        {
            return [(ShuttleId.Shuttle1, 0, 0), (ShuttleId.Shuttle2, 0, 0)];
        }

        int exclusive = count / 3;
        int leftTo = Math.Max(1, count - exclusive);
        int rightFrom = Math.Min(count, exclusive + 1);
        return [(ShuttleId.Shuttle1, 1, leftTo), (ShuttleId.Shuttle2, rightFrom, count)];
    }

    /// <summary>One shared segment between neighbours, or more when the aisle is long relative to the fleet.</summary>
    public static int DefaultOverlap(int segmentCount, int shuttleCount)
    {
        if (shuttleCount <= 1 || segmentCount <= 1)
        {
            return 0;
        }

        return Math.Max(1, segmentCount / (3 * shuttleCount));
    }

    public static (int From, int To)[] EvenRanges(int segmentCount, int shuttleCount, int overlap)
    {
        if (segmentCount < 1)
        {
            return [];
        }

        shuttleCount = Math.Clamp(shuttleCount, 1, segmentCount);
        if (shuttleCount == 1)
        {
            return [(1, segmentCount)];
        }

        overlap = Math.Max(0, overlap);
        int baseWidth = segmentCount / shuttleCount;
        int extra = segmentCount % shuttleCount;
        var ranges = new (int From, int To)[shuttleCount];
        int cursor = 1;
        for (int index = 0; index < shuttleCount; index++)
        {
            int width = baseWidth + (index < extra ? 1 : 0);
            int from = cursor;
            int to = cursor + width - 1;
            if (index < shuttleCount - 1)
            {
                to = Math.Min(segmentCount, to + overlap);
            }

            ranges[index] = (from, to);
            cursor += width;
        }

        return ranges;
    }

    public static void ApplyEvenSplit(DistributorProject project, int shuttleCount, bool resetHomes)
    {
        int segments = Math.Max(1, project.Layout.SegmentCount);
        shuttleCount = Math.Clamp(shuttleCount, 1, segments);
        var existing = project.Shuttles.ToList();
        var next = new List<ShuttleSettings>(shuttleCount);
        for (int number = 1; number <= shuttleCount; number++)
        {
            var id = new ShuttleId(number);
            next.Add(existing.FirstOrDefault(shuttle => shuttle.Id == id) ?? ProjectGrid.DefaultShuttle(id, 0));
        }

        project.Shuttles = next;
        AssignEvenRanges(project, resetHomes);
    }

    public static double SpacedHome(LayoutGeometry geometry, int index, int count)
    {
        if (count <= 1)
        {
            return geometry.TotalLengthMeters / 2.0;
        }

        if (geometry.TotalLengthMeters <= 0)
        {
            return 0;
        }

        return index * geometry.TotalLengthMeters / (count - 1);
    }

    private static void AssignEvenRanges(DistributorProject project, bool resetHomes)
    {
        var ordered = project.Shuttles.OrderBy(shuttle => shuttle.Id).ToList();
        int segments = Math.Max(1, project.Layout.SegmentCount);
        int count = Math.Min(ordered.Count, segments);
        if (count == 0)
        {
            project.Shuttles = ordered;
            return;
        }

        var ranges = EvenRanges(segments, count, DefaultOverlap(segments, count));
        var geometry = new LayoutGeometry(project.Layout);
        for (int index = 0; index < ranges.Length; index++)
        {
            ordered[index].SegmentFrom = ranges[index].From;
            ordered[index].SegmentTo = ranges[index].To;
            if (!resetHomes)
            {
                continue;
            }

            double x = SpacedHome(geometry, index, ranges.Length);
            SetPose(ordered[index], x);
        }

        project.Shuttles = ordered;
    }

    private static void SetPose(ShuttleSettings shuttle, double x)
    {
        shuttle.Home ??= new ShuttlePose();
        shuttle.Handover ??= new ShuttlePose();
        shuttle.Home.XMeters = x;
        shuttle.Home.Level = 1;
        shuttle.Handover.XMeters = x;
        shuttle.Handover.Level = 1;
    }
}
