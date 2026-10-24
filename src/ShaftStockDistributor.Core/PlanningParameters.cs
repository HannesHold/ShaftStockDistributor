namespace ShaftStockDistributor.Core;

public sealed class CostWeights
{
    public double Travel { get; set; } = 1.0;

    /// <summary>
    /// Above 1 so a one-sided packing is not treated as cheap just because every trip is short.
    /// An idle shuttle is a real capacity loss even when the busy shuttle's trips are brief.
    /// </summary>
    public double Imbalance { get; set; } = 2.5;

    public double MiddleTraffic { get; set; } = 0.35;

    /// <summary>Penalty applied once per hard constraint violation (missing article, illegal capacity, unknown shaft).</summary>
    public double Violation { get; set; } = 1_000_000;

    /// <summary>Penalty per shaft an article is short of its calculated demand.</summary>
    public double Shortfall { get; set; } = 10_000;
}

public sealed class AnnealingOptions
{
    public int Seed { get; set; } = 1;

    public int Iterations { get; set; } = 4000;

    public double InitialTemperature { get; set; } = 200;

    public double Cooling { get; set; } = 0.997;
}

/// <summary>Planner knobs that are not part of the physical layout.</summary>
public sealed class PlanningParameters
{
    public const int FormatVersion = 1;

    /// <summary>
    /// Planning assumption for how long a ready shaft waits before a shuttle starts the pull.
    /// Used only to size shaft counts and not as a simulation input. The simulation measures the real wait.
    /// </summary>
    public double ExpectedPullWaitSeconds { get; set; } = 300;

    public CostWeights Weights { get; set; } = new();

    public AnnealingOptions Annealing { get; set; } = new();

    public bool ExampleData { get; set; }

    public string? Notes { get; set; }

    public static PlanningParameters CreateDefault() => new();
}
