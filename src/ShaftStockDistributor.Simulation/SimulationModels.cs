using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Simulation;

public sealed class SimulationOptions
{
    public double DurationSeconds { get; set; } = 3600;

    public double WarmupSeconds { get; set; }

    public int Seed { get; set; } = 1;

    /// <summary>Null keeps the mode stored on the project.</summary>
    public AssignmentMode? Mode { get; set; }

    /// <summary>Fill assigned shafts to the pull trigger so a live demo starts in motion.</summary>
    public bool WarmStart { get; set; }
}

public sealed class SimulationResult
{
    public int Seed { get; init; }

    public double DurationSeconds { get; init; }

    public double WarmupSeconds { get; init; }

    public double MeasuredSeconds { get; init; }

    public double Pallets { get; init; }

    /// <summary>Pallets pulled from crates that were already in the shafts when the run started.</summary>
    public double OpeningPallets { get; init; }

    /// <summary>Pallets pulled from crates that arrived during the run.</summary>
    public double SteadyPallets { get; init; }

    public double PalletsPerHour { get; init; }

    public double OfferedPalletsPerHour { get; init; }

    public int PullsShuttle1 { get; init; }

    public int PullsShuttle2 { get; init; }

    public double Utilization1 { get; init; }

    public double Utilization2 { get; init; }

    /// <summary>Pulls and utilization for every shuttle, in aisle order.</summary>
    public IReadOnlyList<ShuttleKpi> Shuttles { get; init; } = [];

    public double AveragePullWaitSeconds { get; init; }

    public double MaxPullWaitSeconds { get; init; }

    public double BlockedSeconds { get; init; }

    public int OverflowingShafts { get; init; }

    public int LostCrates { get; init; }

    public int ControllerPullJobs { get; init; }

    public int HardwarePullJobs { get; init; }

    public int ReassignmentCount { get; init; }

    public required Dictionary<string, int> LostCratesByArticle { get; init; }

    public required Dictionary<string, int> PullsByShaft { get; init; }
}

public sealed class ShuttleKpi
{
    public ShuttleId Id { get; init; }

    public int Pulls { get; init; }

    public double Utilization { get; init; }
}

public sealed class SegmentBand
{
    public int Segment { get; init; }

    public ZoneKind Zone { get; init; }

    public bool Shared { get; init; }

    public int OwnerNumber { get; init; }

    public string Label { get; init; } = "";
}

public sealed class SharedRun
{
    public int FirstSegment { get; init; }

    public int LastSegment { get; init; }
}

public sealed class ShaftVisual
{
    public int Segment { get; init; }

    public int Level { get; init; }

    public int Shaft { get; init; }

    public ZoneKind Zone { get; init; }

    public string? ArticleId { get; init; }

    public string ArticleName { get; init; } = "";

    public string ColorHex { get; init; } = "#5C6770";

    public int Count { get; init; }

    public int Capacity { get; init; }

    public double VisualFill { get; init; }

    public bool Pinned { get; init; }

    public int PullCount { get; init; }

    public bool Transferring { get; init; }

    public bool RecentlyPulled { get; init; }

    public double XMeters { get; init; }

    public double YMeters { get; init; }

    public ShaftAddress Address => new(Segment, Level, Shaft);
}

public sealed class ShuttleVisual
{
    public ShuttleId Id { get; init; }

    public double XMeters { get; init; }

    public double YMeters { get; init; }

    public int Level { get; init; }

    public string Phase { get; init; } = "Idle";

    public bool IsBlocked { get; init; }

    public bool InSharedZone { get; init; }

    public double? TargetXMeters { get; init; }

    public double? TargetYMeters { get; init; }
}

public sealed class JobVisual
{
    public string Id { get; init; } = "";

    public string Article { get; init; } = "";

    public string Location { get; init; } = "";

    public string State { get; init; } = "";

    public string Shuttle { get; init; } = "";

    public int Crates { get; init; }
}

public sealed class ArticleFlowVisual
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string ClassName { get; init; } = "";

    public string ColorHex { get; init; } = "#5C6770";

    public double InflowCratesPerHour { get; init; }

    public int LostCrates { get; init; }
}

public sealed class WarehouseSnapshot
{
    public double TimeSeconds { get; init; }

    public double TotalLengthMeters { get; init; }

    public double TotalHeightMeters { get; init; }

    public double ShaftPitchMeters { get; init; }

    public double LevelHeightMeters { get; init; }

    public double SegmentGapMeters { get; init; }

    public int SegmentCount { get; init; }

    public int Levels { get; init; }

    public int ShaftsPerLevel { get; init; }

    public bool SharedZoneLocked { get; init; }

    public bool AnyShuttleBlocked { get; init; }

    public IReadOnlyList<SegmentBand> Bands { get; init; } = [];

    public IReadOnlyList<SharedRun> SharedRuns { get; init; } = [];

    public required IReadOnlyList<ShaftVisual> Shafts { get; init; }

    public required IReadOnlyList<ShuttleVisual> Shuttles { get; init; }

    public required IReadOnlyList<JobVisual> Jobs { get; init; }

    public required IReadOnlyList<string> Events { get; init; }

    public required IReadOnlyList<ArticleFlowVisual> ArticleFlows { get; init; }

    public required SimulationResult Kpis { get; init; }
}
