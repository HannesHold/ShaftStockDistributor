using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

public sealed class CostBreakdown
{
    public double Total { get; init; }

    public double Travel { get; init; }

    public double Imbalance { get; init; }

    public double MiddleTraffic { get; init; }

    public double Penalty { get; init; }

    public double Shuttle1LoadHours { get; init; }

    public double Shuttle2LoadHours { get; init; }

    public override string ToString() =>
        $"total {Total:0.###} (travel {Travel:0.###}, imbalance {Imbalance:0.###}, middle {MiddleTraffic:0.###}, penalty {Penalty:0.###})";
}

public sealed class OptimizeProgress
{
    public int Iteration { get; init; }

    public int Iterations { get; init; }

    public double Temperature { get; init; }

    public double CurrentCost { get; init; }

    public double BestCost { get; init; }
}

public sealed class OptimizationResult
{
    public required IReadOnlyList<ShaftAssignment> Assignments { get; init; }

    public required CostBreakdown Before { get; init; }

    public required CostBreakdown Naive { get; init; }

    public required CostBreakdown Optimized { get; init; }

    public int Iterations { get; init; }

    public int ChangedShafts { get; init; }
}
