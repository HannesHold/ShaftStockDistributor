namespace ShaftStockDistributor.Core;

/// <summary>On-disk layout. Segment, level, and shaft indexes are one-based.</summary>
public sealed class LayoutDocument
{
    public int FormatVersion { get; set; } = PlanningParameters.FormatVersion;

    public int SegmentCount { get; set; } = 12;

    public int LevelsPerSegment { get; set; } = 5;

    public int ShaftsPerLevel { get; set; } = 9;

    public int DefaultShaftCapacity { get; set; } = 48;

    public double ShaftPitchMeters { get; set; } = 0.45;

    public double LevelHeightMeters { get; set; } = 0.50;

    public int SafetyDistanceSegments { get; set; } = 1;

    public KinematicsDocument Kinematics { get; set; } = new();

    public ShuttleHomeDocument? Shuttle1Home { get; set; }

    public ShuttleHomeDocument? Shuttle2Home { get; set; }

    public List<ShaftCapacityOverrideDocument> ShaftCapacityOverrides { get; set; } = [];

    public bool ExampleData { get; set; }

    public string? Notes { get; set; }
}

public sealed class KinematicsDocument
{
    public double HorizontalSpeedMetersPerSecond { get; set; } = 1.5;

    public double VerticalSpeedMetersPerSecond { get; set; } = 0.6;

    public double HorizontalAccelerationMetersPerSecondSquared { get; set; } = 0.5;

    public double VerticalAccelerationMetersPerSecondSquared { get; set; } = 0.3;

    public double TransferDurationSeconds { get; set; } = 30;
}

public sealed class ShuttleHomeDocument
{
    public int Segment { get; set; } = 1;

    public int Level { get; set; } = 1;

    public int Shaft { get; set; } = 1;
}

public sealed class ShaftCapacityOverrideDocument
{
    public int Segment { get; set; }

    public int Level { get; set; }

    public int Shaft { get; set; }

    public int Capacity { get; set; }
}

public sealed class ArticlesDocument
{
    public int FormatVersion { get; set; } = PlanningParameters.FormatVersion;

    public bool ExampleData { get; set; }

    public string? Notes { get; set; }

    public AbcDefaultsDocument AbcDefaults { get; set; } = new();

    public int DefaultCratesPerPallet { get; set; } = 40;

    public int DefaultOffset { get; set; } = 4;

    public List<ArticleDocument> Articles { get; set; } = [];
}

public sealed class AbcDefaultsDocument
{
    public double TotalCratesPerHour { get; set; } = 1500;

    public double ShareA { get; set; } = 0.70;

    public double ShareB { get; set; } = 0.20;

    public double ShareC { get; set; } = 0.10;
}

public sealed class ArticleDocument
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public AbcClass AbcClass { get; set; } = AbcClass.B;

    public double? CratesPerHour { get; set; }

    public int? CratesPerPallet { get; set; }

    public int? Offset { get; set; }

    public int? ShaftCapacity { get; set; }
}

public sealed class AssignmentDocument
{
    public int FormatVersion { get; set; } = PlanningParameters.FormatVersion;

    public bool ExampleData { get; set; }

    public string? Notes { get; set; }

    public List<AssignedShaftDocument> Shafts { get; set; } = [];
}

public sealed class AssignedShaftDocument
{
    public int Segment { get; set; }

    public int Level { get; set; }

    public int Shaft { get; set; }

    public string ArticleId { get; set; } = "";

    public bool Pinned { get; set; }
}

public sealed class ParametersDocument
{
    public int FormatVersion { get; set; } = PlanningParameters.FormatVersion;

    public double ExpectedPullWaitSeconds { get; set; } = 300;

    public CostWeightsDocument Weights { get; set; } = new();

    public AnnealingDocument Annealing { get; set; } = new();

    public bool ExampleData { get; set; }

    public string? Notes { get; set; }
}

public sealed class CostWeightsDocument
{
    public double Travel { get; set; } = 1;

    public double Imbalance { get; set; } = 2.5;

    public double MiddleTraffic { get; set; } = 0.35;

    public double Violation { get; set; } = 1_000_000;

    public double Shortfall { get; set; } = 10_000;
}

public sealed class AnnealingDocument
{
    public int Seed { get; set; } = 1;

    public int Iterations { get; set; } = 4000;

    public double InitialTemperature { get; set; } = 200;

    public double Cooling { get; set; } = 0.997;
}
