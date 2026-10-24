namespace ShaftStockDistributor.Core;

/// <summary>
/// Which shuttle may serve a segment. The first third of segments is shuttle 1 only,
/// the last third is shuttle 2 only, and the middle third is shared.
/// When the count is not divisible by three, the remainder is added to the shared middle
/// (10 segments => 3 / 4 / 3, 11 segments => 3 / 5 / 3).
/// </summary>
public enum Zone
{
    Shuttle1Only = 0,
    Shared = 1,
    Shuttle2Only = 2
}

public sealed class ZoneMap
{
    public ZoneMap(int segmentCount, int shuttle1OnlyCount, int sharedCount)
    {
        if (segmentCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentCount));
        }

        if (shuttle1OnlyCount < 0 || sharedCount < 0 || shuttle1OnlyCount + sharedCount > segmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sharedCount), "Zone sizes do not cover the segment row.");
        }

        SegmentCount = segmentCount;
        Shuttle1OnlyCount = shuttle1OnlyCount;
        SharedCount = sharedCount;
        Shuttle2OnlyCount = segmentCount - shuttle1OnlyCount - sharedCount;
        SharedStart = shuttle1OnlyCount;
        SharedEnd = shuttle1OnlyCount + sharedCount - 1;
        Shuttle2Start = shuttle1OnlyCount + sharedCount;
    }

    public int SegmentCount { get; }

    public int Shuttle1OnlyCount { get; }

    public int SharedCount { get; }

    public int Shuttle2OnlyCount { get; }

    /// <summary>Inclusive zero-based index of the first shared segment. Equal to <see cref="SharedEnd"/> + 1 when there is no shared zone.</summary>
    public int SharedStart { get; }

    /// <summary>Inclusive zero-based index of the last shared segment. -1 when there is no shared zone.</summary>
    public int SharedEnd { get; }

    public int Shuttle2Start { get; }

    public Zone ZoneOf(int segment)
    {
        if (segment < 0 || segment >= SegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(segment));
        }

        if (segment < Shuttle1OnlyCount)
        {
            return Zone.Shuttle1Only;
        }

        return segment < Shuttle2Start ? Zone.Shared : Zone.Shuttle2Only;
    }

    public static ZoneMap FromSegmentCount(int segmentCount)
    {
        if (segmentCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentCount));
        }

        var exclusive = segmentCount / 3;
        var shared = segmentCount - (2 * exclusive);
        return new ZoneMap(segmentCount, exclusive, shared);
    }
}
