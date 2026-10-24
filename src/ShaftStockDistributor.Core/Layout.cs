namespace ShaftStockDistributor.Core;

public sealed class Shaft
{
    public required ShaftLocation Location { get; init; }

    public required Zone Zone { get; init; }

    /// <summary>Physical crate capacity before an article-specific override is applied.</summary>
    public required int PhysicalCapacity { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }
}

/// <summary>
/// One row of flow racks. A segment is one rack, a level is one Ebene, a shaft is one Schacht.
/// Crates enter at the front and flow to the back, where a shuttle lets them out.
/// </summary>
public sealed class Layout
{
    private readonly Dictionary<ShaftLocation, Shaft> _byLocation;
    private readonly Dictionary<ShaftLocation, int> _capacityOverrides;

    public Layout(
        int segmentCount,
        int levelsPerSegment,
        int shaftsPerLevel,
        int defaultShaftCapacity,
        double shaftPitchMeters,
        double levelHeightMeters,
        int safetyDistanceSegments,
        Kinematics kinematics,
        ShuttlePose shuttle1Home,
        ShuttlePose shuttle2Home,
        IReadOnlyDictionary<ShaftLocation, int>? capacityOverrides,
        bool exampleData,
        string? notes)
    {
        if (segmentCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentCount));
        }

        if (levelsPerSegment < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(levelsPerSegment));
        }

        if (shaftsPerLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(shaftsPerLevel));
        }

        SegmentCount = segmentCount;
        LevelsPerSegment = levelsPerSegment;
        ShaftsPerLevel = shaftsPerLevel;
        DefaultShaftCapacity = defaultShaftCapacity;
        ShaftPitchMeters = shaftPitchMeters;
        LevelHeightMeters = levelHeightMeters;
        SafetyDistanceSegments = safetyDistanceSegments;
        Kinematics = kinematics;
        Shuttle1Home = shuttle1Home;
        Shuttle2Home = shuttle2Home;
        Zones = ZoneMap.FromSegmentCount(segmentCount);
        ExampleData = exampleData;
        Notes = notes;
        _capacityOverrides = capacityOverrides is null
            ? []
            : new Dictionary<ShaftLocation, int>(capacityOverrides);

        var shafts = new List<Shaft>(segmentCount * levelsPerSegment * shaftsPerLevel);
        for (var segment = 0; segment < segmentCount; segment++)
        {
            var zone = Zones.ZoneOf(segment);
            for (var level = 0; level < levelsPerSegment; level++)
            {
                for (var shaft = 0; shaft < shaftsPerLevel; shaft++)
                {
                    var location = new ShaftLocation(segment, level, shaft);
                    var physical = _capacityOverrides.TryGetValue(location, out var over)
                        ? over
                        : defaultShaftCapacity;
                    shafts.Add(new Shaft
                    {
                        Location = location,
                        Zone = zone,
                        PhysicalCapacity = physical,
                        X = X(segment, shaft),
                        Y = Y(level)
                    });
                }
            }
        }

        Shafts = shafts;
        _byLocation = shafts.ToDictionary(shaft => shaft.Location);
    }

    public const int MaxLevelsPerSegment = 5;

    public const int MaxShaftsPerLevel = 9;

    public int SegmentCount { get; }

    public int LevelsPerSegment { get; }

    public int ShaftsPerLevel { get; }

    public int DefaultShaftCapacity { get; }

    public double ShaftPitchMeters { get; }

    public double LevelHeightMeters { get; }

    /// <summary>
    /// Minimum gap, in segments, that the two shuttles must keep. The shared zone plus this margin
    /// is locked by at most one shuttle at a time.
    /// </summary>
    public int SafetyDistanceSegments { get; }

    public Kinematics Kinematics { get; }

    public ShuttlePose Shuttle1Home { get; }

    public ShuttlePose Shuttle2Home { get; }

    public ZoneMap Zones { get; }

    public bool ExampleData { get; }

    public string? Notes { get; }

    public IReadOnlyList<Shaft> Shafts { get; }

    public IReadOnlyDictionary<ShaftLocation, int> CapacityOverrides => _capacityOverrides;

    public double X(int segment, int shaft) => ((segment * ShaftsPerLevel) + shaft) * ShaftPitchMeters;

    public double Y(int level) => level * LevelHeightMeters;

    public Shaft? Find(ShaftLocation location) =>
        _byLocation.TryGetValue(location, out var shaft) ? shaft : null;

    public bool Contains(ShuttlePose pose) =>
        pose.Segment >= 0 && pose.Segment < SegmentCount
        && pose.Level >= 0 && pose.Level < LevelsPerSegment
        && pose.Shaft >= 0 && pose.Shaft < ShaftsPerLevel;

    public double TravelSeconds(ShuttlePose from, ShuttlePose to) =>
        Kinematics.TravelSeconds(X(to.Segment, to.Shaft) - X(from.Segment, from.Shaft), Y(to.Level) - Y(from.Level));

    public double TravelSeconds(ShuttlePose from, Shaft to) =>
        Kinematics.TravelSeconds(to.X - from.X(this), to.Y - from.Y(this));

    /// <summary>
    /// Crates that fit when <paramref name="article"/> occupies the shaft.
    /// A per-shaft physical override wins; otherwise the article capacity; otherwise the layout default.
    /// </summary>
    public int EffectiveCapacity(Shaft shaft, Article article)
    {
        if (_capacityOverrides.ContainsKey(shaft.Location))
        {
            return shaft.PhysicalCapacity;
        }

        return article.ShaftCapacity ?? DefaultShaftCapacity;
    }

    /// <summary>
    /// Segments a shuttle must keep clear when the other shuttle works the shared zone.
    /// Inclusive zero-based bounds. Empty (start &gt; end) when the row has no shared segments.
    /// </summary>
    public (int Start, int End) SharedKeepOut()
    {
        if (Zones.SharedCount == 0)
        {
            return (1, 0);
        }

        var start = Math.Max(0, Zones.SharedStart - SafetyDistanceSegments);
        var end = Math.Min(SegmentCount - 1, Zones.SharedEnd + SafetyDistanceSegments);
        return (start, end);
    }
}
