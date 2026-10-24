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

        var manual = Steady(AssignmentMode.Manual);
        var automatic = Steady(AssignmentMode.Automatic);
        var optimizedRun = SimulationSession.Run(optimized, manual);
        var automaticRun = SimulationSession.Run(optimized.Clone(), automatic);
        var naiveRun = SimulationSession.Run(naive, manual);
        Assert.Equal(optimizedRun.ControllerPullJobs, optimizedRun.HardwarePullJobs);
        Assert.Equal(automaticRun.ControllerPullJobs, automaticRun.HardwarePullJobs);
        Assert.Equal(naiveRun.ControllerPullJobs, naiveRun.HardwarePullJobs);
        Assert.True(optimizedRun.Pallets > 0);
        Assert.True(naiveRun.Pallets > 0);
        Assert.Equal(optimizedRun.Pallets, automaticRun.Pallets);
        Assert.Equal(optimizedRun.LostCrates, automaticRun.LostCrates);
        Assert.Equal(0, automaticRun.ReassignmentCount);

        var shifted = optimized.Clone();
        InflowAllocator.Apply(shifted, new InflowScenario
        {
            Name = "EXAMPLE shifted 25/35/40",
            TotalCratesPerHour = 1800,
            ShareA = 0.25,
            ShareB = 0.35,
            ShareC = 0.40
        });
        var shiftedManual = SimulationSession.Run(shifted, manual);
        var shiftedAutomatic = SimulationSession.Run(shifted.Clone(), automatic);
        Assert.True(shiftedAutomatic.PalletsPerHour > shiftedManual.PalletsPerHour,
            $"shifted automatic {shiftedAutomatic.PalletsPerHour:0.00} did not beat manual {shiftedManual.PalletsPerHour:0.00}");
        Assert.True(shiftedAutomatic.LostCrates < shiftedManual.LostCrates,
            $"shifted automatic lost {shiftedAutomatic.LostCrates}, manual lost {shiftedManual.LostCrates}");

        Console.WriteLine($"COST naive {result.Naive}");
        Console.WriteLine($"COST optimized {result.Optimized}");
        double bound = TheoreticalThroughput.PalletsPerHour(optimized);
        Assert.True(bound > optimizedRun.PalletsPerHour, $"upper bound {bound:0.0} sat under measured {optimizedRun.PalletsPerHour:0.0}");
        Assert.True(bound > shiftedAutomatic.PalletsPerHour, $"upper bound {bound:0.0} sat under shifted {shiftedAutomatic.PalletsPerHour:0.0}");
        Assert.True(bound > 130 && bound < 300, $"upper bound {bound:0.0} is outside the expected band");
        Console.WriteLine($"THEORETICAL {bound:0.00} pallets/h");
        Print("optimized manual", optimizedRun);
        Print("optimized automatic", automaticRun);
        Print("naive manual", naiveRun);
        Print("shifted manual", shiftedManual);
        Print("shifted automatic", shiftedAutomatic);
    }

    private static SimulationOptions Steady(AssignmentMode mode) => new()
    {
        DurationSeconds = 2.5 * 3600,
        WarmupSeconds = 3600,
        Seed = 1,
        Mode = mode
    };

    private static void Print(string label, SimulationResult run)
    {
        Console.WriteLine(
            $"{label}: pallets/h {run.PalletsPerHour:0.00} offered {run.OfferedPalletsPerHour:0.00} " +
            $"pulls {run.PullsShuttle1}/{run.PullsShuttle2} util {run.Utilization1:P1}/{run.Utilization2:P1} " +
            $"wait {run.AveragePullWaitSeconds:0.0}s blocked {run.BlockedSeconds:0.0}s lost {run.LostCrates} reassigned {run.ReassignmentCount}");
    }
}
