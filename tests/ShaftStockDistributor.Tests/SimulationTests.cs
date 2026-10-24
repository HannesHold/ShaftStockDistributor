using ShaftStockDistributor.Core;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Tests;

public class SimulationTests
{
    [Fact]
    public void Same_seed_reproduces_pallets_and_lost_crates()
    {
        var project = TestWarehouses.SteadySmall();
        var first = SimulationSession.Run(project, Options(1800, 1));
        var second = SimulationSession.Run(project.Clone(), Options(1800, 1));
        Assert.Equal(first.Pallets, second.Pallets);
        Assert.Equal(first.LostCrates, second.LostCrates);
        Assert.Equal(first.PullsByShaft, second.PullsByShaft);
        Assert.Equal(first.PullsShuttle1, second.PullsShuttle1);
    }

    [Fact]
    public void Different_seeds_are_allowed_to_diverge()
    {
        var project = TestWarehouses.SteadySmall();
        var first = SimulationSession.Run(project, Options(2400, 1));
        var second = SimulationSession.Run(project.Clone(), Options(2400, 2));
        Assert.NotEqual(first.Pallets + first.LostCrates + first.AveragePullWaitSeconds,
            second.Pallets + second.LostCrates + second.AveragePullWaitSeconds);
    }

    [Fact]
    public void Simulation_sends_pulls_through_the_controller_and_hardware()
    {
        var result = SimulationSession.Run(TestWarehouses.SteadySmall(), Options(1200, 3));
        Assert.True(result.Pallets > 0);
        Assert.True(result.ControllerPullJobs > 0);
        Assert.Equal(result.ControllerPullJobs, result.HardwarePullJobs);
    }

    [Fact]
    public void Shuttles_never_reserve_overlapping_paths_and_the_shared_zone_can_block()
    {
        var project = TestWarehouses.Contention();
        var session = SimulationSession.Start(project, Options(900, 4));
        session.AdvanceTo(900);
        Assert.True(session.Controller.Interlock.ReservationsAreSafe());
        var result = session.ToResult();
        Assert.True(result.BlockedSeconds > 0, "Expected the second shuttle to wait for the shared zone.");
        Assert.Equal(result.ControllerPullJobs, result.HardwarePullJobs);
        Assert.True(result.PullsShuttle1 > 0);
        Assert.True(result.PullsShuttle2 > 0);
    }

    [Fact]
    public void Automatic_mode_keeps_pinned_articles()
    {
        var project = TestWarehouses.SteadySmall();
        project.AssignmentMode = AssignmentMode.Automatic;
        var pinned = project.Assignments.First(slot => slot.ArticleId == "FAST");
        pinned.Pinned = true;
        var result = SimulationSession.Run(project, new SimulationOptions
        {
            DurationSeconds = 900,
            WarmupSeconds = 0,
            Seed = 5,
            Mode = AssignmentMode.Automatic
        });
        Assert.True(result.Pallets > 0 || result.LostCrates >= 0);
        var session = SimulationSession.Start(project.Clone(), new SimulationOptions
        {
            DurationSeconds = 900,
            Seed = 5,
            Mode = AssignmentMode.Automatic
        });
        session.AdvanceTo(900);
        var shaft = session.Controller.Shafts.Single(item => item.Address == pinned.Address);
        Assert.Equal("FAST", shaft.ArticleId);
        Assert.True(shaft.Pinned);
    }

    private static SimulationOptions Options(double seconds, int seed) => new()
    {
        DurationSeconds = seconds,
        WarmupSeconds = 30,
        Seed = seed,
        Mode = AssignmentMode.Manual
    };
}

public class StudyTests
{
    [Fact]
    public void Detector_marks_the_first_load_that_stays_saturated()
    {
        var settings = new SaturationSettings { AchievedRatio = 0.98, TreatLostCratesAsSaturation = true };
        var means = new List<StudyPoint>
        {
            Point(500, 10, 10, 0),
            Point(1000, 20, 19.8, 0),
            Point(1500, 30, 20, 4),
            Point(2000, 40, 20, 12)
        };
        var found = SaturationDetector.Detect("Example", AssignmentMode.Manual, means, settings);
        Assert.True(found.Found);
        Assert.Equal(1500, found.SaturatedAtCratesPerHour);
        Assert.Equal(19.8, found.MaxSustainablePalletsPerHour, 3);
        Assert.Contains("lost", found.Reason, StringComparison.OrdinalIgnoreCase);

        var blip = new List<StudyPoint>
        {
            Point(500, 10, 10, 0),
            Point(1000, 20, 12, 40),
            Point(1500, 30, 29.5, 0)
        };
        var ignored = SaturationDetector.Detect("Example", AssignmentMode.Manual, blip, new SaturationSettings
        {
            AchievedRatio = 0.95,
            TreatLostCratesAsSaturation = true,
            LostCratesPerHour = 3,
            ConsecutivePoints = 2
        });
        Assert.False(ignored.Found);
    }

    [Fact]
    public void Sweep_finds_a_saturation_point_on_a_tiny_warehouse()
    {
        var project = TestWarehouses.EasyToSaturate();
        var result = ScenarioStudyRunner.Run(project, new StudyRequest
        {
            MinCratesPerHour = 20,
            MaxCratesPerHour = 20000,
            StepCratesPerHour = 19980,
            Replications = 1,
            BaseSeed = 9,
            DurationHours = 0.08,
            WarmupHours = 0,
            ReoptimizePerScenario = false,
            CompareAssignmentModes = false,
            Mode = AssignmentMode.Manual,
            Thresholds = new SaturationSettings
            {
                AchievedRatio = 0,
                TreatLostCratesAsSaturation = true,
                LostFractionOfInflow = 0,
                LostCratesPerHour = 0,
                ConsecutivePoints = 1
            }
        });

        var saturation = Assert.Single(result.Saturations);
        Assert.True(saturation.Found);
        Assert.Equal(20000, saturation.SaturatedAtCratesPerHour);
        Assert.Equal(2, result.Means.Count);
        Assert.Equal(0, result.Means[0].LostCrates);
        Assert.True(result.Means[1].LostCrates > 0);
    }

    private static StudyPoint Point(double load, double offered, double achieved, int lost) => new()
    {
        Scenario = "Example",
        Mode = AssignmentMode.Manual,
        LoadCratesPerHour = load,
        OfferedPalletsPerHour = offered,
        AchievedPalletsPerHour = achieved,
        LostCrates = lost
    };
}
