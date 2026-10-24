using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Tests;

public sealed class ExampleRunTests
{
    [Fact]
    public void Optimized_example_beats_the_naive_baseline_and_both_simulations_run()
    {
        var project = ExampleWarehouse.Create();
        var result = AnnealingOptimizer.Run(project, iterations: 400);
        Assert.True(result.Optimized.Total < result.Naive.Total, $"{result.Optimized} should beat {result.Naive}");

        var optimized = project.Clone();
        ProjectGrid.ReplaceAssignments(optimized, result.Assignments);
        var naive = project.Clone();
        ProjectGrid.ReplaceAssignments(naive, AssignmentBuilder.Naive(project));

        var manual = new SimulationOptions { DurationSeconds = 3600, WarmupSeconds = 360, Seed = 1, Mode = AssignmentMode.Manual };
        var automatic = new SimulationOptions { DurationSeconds = 3600, WarmupSeconds = 360, Seed = 1, Mode = AssignmentMode.Automatic };
        var optimizedRun = SimulationSession.Run(optimized, manual);
        var automaticRun = SimulationSession.Run(optimized, automatic);
        var naiveRun = SimulationSession.Run(naive, manual);
        Assert.Equal(optimizedRun.ControllerPullJobs, optimizedRun.HardwarePullJobs);
        Assert.Equal(automaticRun.ControllerPullJobs, automaticRun.HardwarePullJobs);
        Assert.Equal(naiveRun.ControllerPullJobs, naiveRun.HardwarePullJobs);
        Assert.True(optimizedRun.Pallets > 0);
        Assert.True(naiveRun.Pallets > 0);

        Console.WriteLine($"COST naive {result.Naive}");
        Console.WriteLine($"COST optimized {result.Optimized}");
        Print("optimized manual", optimizedRun);
        Print("optimized automatic", automaticRun);
        Print("naive manual", naiveRun);
    }

    private static void Print(string label, SimulationResult run)
    {
        Console.WriteLine(
            $"{label}: pallets/h {run.PalletsPerHour:0.00} offered {run.OfferedPalletsPerHour:0.00} " +
            $"pulls {run.PullsShuttle1}/{run.PullsShuttle2} util {run.Utilization1:P1}/{run.Utilization2:P1} " +
            $"wait {run.AveragePullWaitSeconds:0.0}s blocked {run.BlockedSeconds:0.0}s lost {run.LostCrates}");
    }
}
