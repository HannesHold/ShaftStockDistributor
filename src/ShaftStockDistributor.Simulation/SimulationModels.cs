namespace ShaftStockDistributor.Simulation;

public sealed class SimulationOptions
{
    public double Hours { get; init; } = 8;

    public int Seed { get; init; } = 1;

    /// <summary>Keep a position sample after every event so tests can audit shuttle clearance.</summary>
    public bool RecordTrace { get; init; } = true;
}

public sealed record ShaftPullKpi(int Segment, int Level, int Shaft, string ArticleId, int Pulls, int ResidualCrates);

public sealed record ShuttleTraceSample(
    double TimeSeconds,
    int Shuttle1Segment,
    int Shuttle1Level,
    int Shuttle1RangeStart,
    int Shuttle1RangeEnd,
    int Shuttle2Segment,
    int Shuttle2Level,
    int Shuttle2RangeStart,
    int Shuttle2RangeEnd);

public sealed class SimulationResult
{
    public double Hours { get; init; }

    public int Seed { get; init; }

    public int PullsShuttle1 { get; init; }

    public int PullsShuttle2 { get; init; }

    /// <summary>Share of the horizon spent travelling or transferring. Blocking is reported separately.</summary>
    public double UtilizationShuttle1 { get; init; }

    public double UtilizationShuttle2 { get; init; }

    public double BlockedSecondsShuttle1 { get; init; }

    public double BlockedSecondsShuttle2 { get; init; }

    public double BusySecondsShuttle1 { get; init; }

    public double BusySecondsShuttle2 { get; init; }

    public double AveragePullWaitSeconds { get; init; }

    public double MaxPullWaitSeconds { get; init; }

    public int PullCount { get; init; }

    public int Arrivals { get; init; }

    public int AcceptedCrates { get; init; }

    public int RejectedCrates { get; init; }

    public IReadOnlyDictionary<string, int> OverflowEventsByArticle { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<ShaftPullKpi> ShaftPulls { get; init; } = [];

    public int CollisionViolations { get; init; }

    public string? FirstCollision { get; init; }

    public IReadOnlyList<ShuttleTraceSample> Trace { get; init; } = [];

    public double BlockedSeconds => BlockedSecondsShuttle1 + BlockedSecondsShuttle2;
}

public static class ShuttleClearance
{
    /// <summary>
    /// Two shuttles conflict when their occupied segment ranges come within <paramref name="safetyDistanceSegments"/>
    /// of each other. A safety distance of 1 forbids adjacent segments as well as the same segment.
    /// </summary>
    public static bool RangesConflict(int aStart, int aEnd, int bStart, int bEnd, int safetyDistanceSegments) =>
        !(aEnd + safetyDistanceSegments < bStart || bEnd + safetyDistanceSegments < aStart);
}
