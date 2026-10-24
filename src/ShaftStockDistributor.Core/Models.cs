namespace ShaftStockDistributor.Core;

/// <summary>Crate size in millimeters. The flow axis is the dimension that stacks along the shaft.</summary>
public sealed class CrateDimensions
{
    public double LengthMillimeters { get; set; } = 400;

    public double WidthMillimeters { get; set; } = 300;

    public double HeightMillimeters { get; set; } = 280;

    public FlowAxis FlowAxis { get; set; } = FlowAxis.Length;

    public double FlowLengthMeters => AxisMillimeters(FlowAxis) / 1000.0;

    public (double First, double Second) CrossSectionMillimeters => FlowAxis switch
    {
        FlowAxis.Length => (WidthMillimeters, HeightMillimeters),
        FlowAxis.Width => (LengthMillimeters, HeightMillimeters),
        _ => (LengthMillimeters, WidthMillimeters)
    };

    public double AxisMillimeters(FlowAxis axis) => axis switch
    {
        FlowAxis.Length => LengthMillimeters,
        FlowAxis.Width => WidthMillimeters,
        _ => HeightMillimeters
    };
}

public sealed class Article
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public AbcClass Class { get; set; } = AbcClass.B;

    public CrateDimensions Dimensions { get; set; } = new();

    /// <summary>Crates that make one full pallet of this article.</summary>
    public int CratesPerPallet { get; set; } = 40;

    /// <summary>Extra crates required on top of a full pallet before a pull is triggered.</summary>
    public int Offset { get; set; } = 4;

    /// <summary>Relative weight inside the ABC class. Used when a scenario does not override it.</summary>
    public double ClassWeight { get; set; } = 1;

    /// <summary>Active inflow used by the optimizer and the simulation. Scenarios overwrite this.</summary>
    public double InflowCratesPerHour { get; set; }

    /// <summary>
    /// Inflow the stored shaft plan was sized for. Null means the active inflow is the plan.
    /// A scenario change or a load sweep can replace <see cref="InflowCratesPerHour"/> and leave this in place,
    /// so automatic mode can tell a shifted split from the plan.
    /// </summary>
    public double? PlannedInflowCratesPerHour { get; set; }

    public string ColorHex { get; set; } = "#4C8DFF";

    public int PullTriggerCrates => Math.Max(0, CratesPerPallet + Offset);
}

/// <summary>A position along the rear aisle. Level is one-based.</summary>
public sealed class ShuttlePose
{
    public double XMeters { get; set; }

    public int Level { get; set; } = 1;
}

public sealed class ShuttleSettings
{
    public ShuttleId Id { get; set; }

    public double HorizontalSpeedMetersPerSecond { get; set; } = 0.3;

    public double VerticalSpeedMetersPerSecond { get; set; } = 0.3;

    /// <summary>Zero disables acceleration and uses constant speed.</summary>
    public double HorizontalAccelerationMetersPerSecondSquared { get; set; } = 0.2;

    public double VerticalAccelerationMetersPerSecondSquared { get; set; } = 0.2;

    public ShuttlePose Home { get; set; } = new();

    /// <summary>Stored for the customer. Travel back to this pose is not applied until the real pose is known.</summary>
    public ShuttlePose Handover { get; set; } = new();
}

public sealed class LayoutSettings
{
    public int SegmentCount { get; set; } = 12;

    public int LevelsPerSegment { get; set; } = 5;

    public int ShaftsPerLevel { get; set; } = 9;

    public double ShaftLengthMeters { get; set; } = 20;

    /// <summary>Empty gap between crates along the flow. Default is zero.</summary>
    public double CrateGapMeters { get; set; }

    public double ShaftPitchMeters { get; set; } = 0.45;

    public double LevelHeightMeters { get; set; } = 0.55;

    /// <summary>Extra space between neighboring segments, so the frame and the zone boundary are visible.</summary>
    public double SegmentGapMeters { get; set; } = 0.35;

    /// <summary>Optional clear width of a shaft. Unset skips the cross-section check.</summary>
    public double? ShaftWidthMeters { get; set; }

    /// <summary>Optional clear height of a shaft. Unset skips the cross-section check.</summary>
    public double? ShaftClearHeightMeters { get; set; }

    public double TransferDurationSeconds { get; set; } = 12;

    /// <summary>Shuttles must stay at least this far apart, measured along the aisle.</summary>
    public double SafetyDistanceMeters { get; set; } = 1.5;
}

public sealed class ShaftAssignment
{
    public int Segment { get; set; }

    public int Level { get; set; }

    public int Shaft { get; set; }

    public string? ArticleId { get; set; }

    public bool Pinned { get; set; }

    public ShaftAddress Address => new(Segment, Level, Shaft);
}

/// <summary>Named inflow mix. There is no measured inflow yet; every number is a what-if.</summary>
public sealed class InflowScenario
{
    public string Name { get; set; } = "Scenario";

    public double TotalCratesPerHour { get; set; } = 1800;

    public double ShareA { get; set; } = 0.70;

    public double ShareB { get; set; } = 0.20;

    public double ShareC { get; set; } = 0.10;

    /// <summary>Optional per-article weights. Missing articles use <see cref="Article.ClassWeight"/>.</summary>
    public Dictionary<string, double> ArticleWeights { get; set; } = new(StringComparer.Ordinal);
}

public sealed class OptimizerSettings
{
    public double ExpectedPullWaitSeconds { get; set; } = 90;

    public int Iterations { get; set; } = 800;

    public int Seed { get; set; } = 1;

    public double Cooling { get; set; } = 0.997;

    public double TravelWeight { get; set; } = 1.0;

    public double ImbalanceWeight { get; set; } = 1.2;

    public double MiddleTrafficWeight { get; set; } = 0.8;

    public double PenaltyWeight { get; set; } = 4.0;
}

public sealed class OperationSettings
{
    public double ReassignmentCooldownSeconds { get; set; } = 180;

    /// <summary>A new article must score this much better (fraction) before an empty shaft switches.</summary>
    public double Hysteresis { get; set; } = 0.25;

    public int MaxReassignmentsPerHour { get; set; } = 24;

    public double ObservationWindowSeconds { get; set; } = 600;
}

public sealed class StudySettings
{
    public double MinCratesPerHour { get; set; } = 300;

    public double MaxCratesPerHour { get; set; } = 4000;

    public double StepCratesPerHour { get; set; } = 500;

    public int Replications { get; set; } = 3;

    public int BaseSeed { get; set; } = 1;

    public double DurationHours { get; set; } = 2.5;

    public double WarmupHours { get; set; } = 1;

    public bool ReoptimizePerScenario { get; set; } = true;

    public bool CompareAssignmentModes { get; set; }

    public double AchievedRatioThreshold { get; set; } = 0.90;

    public bool TreatLostCratesAsSaturation { get; set; } = true;

    /// <summary>Null disables the utilization rule. A value such as 0.95 marks saturation when either shuttle exceeds it.</summary>
    public double? UtilizationCeiling { get; set; }
}

public sealed class DistributorProject
{
    public int SchemaVersion { get; set; } = ProjectSerializer.CurrentSchemaVersion;

    public string Name { get; set; } = "New warehouse";

    public bool IsExampleData { get; set; }

    public AssignmentMode AssignmentMode { get; set; } = AssignmentMode.Automatic;

    public string? ActiveScenarioName { get; set; }

    public LayoutSettings Layout { get; set; } = new();

    public List<ShuttleSettings> Shuttles { get; set; } = new();

    public List<Article> Articles { get; set; } = new();

    /// <summary>Crate sizes the article generator may assign. Empty means the built-in catalog.</summary>
    public List<CrateType> CrateTypes { get; set; } = new();

    public List<ShaftAssignment> Assignments { get; set; } = new();

    public List<InflowScenario> Scenarios { get; set; } = new();

    public OptimizerSettings Optimizer { get; set; } = new();

    public OperationSettings Operation { get; set; } = new();

    public StudySettings Study { get; set; } = new();

    public ShuttleSettings Shuttle(ShuttleId id) =>
        Shuttles.First(s => s.Id == id);

    public Article? FindArticle(string? id) =>
        id is null ? null : Articles.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    public DistributorProject Clone()
    {
        var copy = ProjectSerializer.Clone(this);
        return copy;
    }
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    IReadOnlyList<string> Arguments);
