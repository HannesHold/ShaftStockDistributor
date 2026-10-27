using ShaftStockDistributor.Cli;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Tests;

public sealed class ResultFileTests
{
    [Fact]
    public void Simulation_result_round_trips_the_snapshot_and_the_series()
    {
        var project = TestWarehouses.SteadySmall();
        string hash = SimulationResultFile.Fingerprint(project);
        var document = SimulationResultFile.FromSimulation(project, new SimulationOptions
        {
            DurationSeconds = 180,
            WarmupSeconds = 60,
            Seed = 7,
            Mode = AssignmentMode.Manual
        }, sourcePath: "steady.json", sampleSeconds: 60);

        var loaded = SimulationResultFile.Deserialize(SimulationResultFile.Serialize(document));
        Assert.Equal(SimulationResultFile.FormatName, loaded.Format);
        Assert.Equal(SimulationResultFile.CurrentVersion, loaded.Version);
        Assert.Equal("simulation", loaded.Kind);
        Assert.Equal(hash, loaded.Project.Sha256);
        Assert.Equal("steady.json", loaded.Project.SourcePath);
        Assert.Equal("Steady fixture", loaded.ReadProject()!.Name);
        Assert.Equal("Manual", loaded.Run.Mode);
        Assert.Equal(7, loaded.Run.Seed);
        Assert.Equal(180 / 3600.0, loaded.Run.DurationHours, 3);
        Assert.NotEmpty(loaded.Series);
        Assert.Equal(document.Series[^1].TimeHours, loaded.Series[^1].TimeHours, 5);
        Assert.Equal(document.Summary.AchievedPalletsPerHour, loaded.Summary.AchievedPalletsPerHour, 5);
        Assert.NotEmpty(loaded.Summary.Shuttles);
        Assert.Null(loaded.Study);
    }

    [Fact]
    public void Unknown_result_format_is_rejected()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationResultFile.Deserialize("{\"format\":\"warehouse\",\"version\":1}"));
        Assert.Contains("not a Shaft Stock Distributor result", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Simulate_writes_a_result_file_the_desktop_can_load()
    {
        var project = TestWarehouses.SteadySmall();
        using var directory = TempDirectory.Create();
        string projectPath = Path.Combine(directory.Path, "warehouse.json");
        string resultPath = Path.Combine(directory.Path, "run.ssdresult.json");
        ProjectSerializer.Save(projectPath, project);

        int code = Program.Main(new[]
        {
            "simulate",
            "--project", projectPath,
            "--hours", "0.05",
            "--warmup", "0.01",
            "--seed", "1",
            "--mode", "Manual",
            "--out-result", resultPath
        });

        Assert.Equal(0, code);
        var loaded = SimulationResultFile.Load(resultPath);
        Assert.Equal("ssdresult", loaded.Format);
        Assert.Equal(1, loaded.Version);
        Assert.Equal("simulation", loaded.Kind);
        Assert.Equal("Manual", loaded.Run.Mode);
        Assert.Equal(1, loaded.Run.Seed);
        Assert.NotEmpty(loaded.Series);
        Assert.Equal("Steady fixture", loaded.ReadProject()!.Name);
        Assert.Equal(SimulationResultFile.Fingerprint(ProjectSerializer.Load(projectPath)), loaded.Project.Sha256);
        Assert.True(loaded.Summary.Shuttles.Count >= 2);
    }

    [Fact]
    public void Study_writes_load_points_and_keeps_the_csv()
    {
        var project = TestWarehouses.SteadySmall();
        project.Scenarios.Add(new InflowScenario
        {
            Name = "Steady mix",
            TotalCratesPerHour = 280,
            ShareA = 0.8,
            ShareB = 0,
            ShareC = 0.2
        });
        using var directory = TempDirectory.Create();
        string projectPath = Path.Combine(directory.Path, "warehouse.json");
        string resultPath = Path.Combine(directory.Path, "study.ssdresult.json");
        string csvPath = Path.Combine(directory.Path, "study.csv");
        ProjectSerializer.Save(projectPath, project);

        int code = Program.Main(new[]
        {
            "study",
            "--project", projectPath,
            "--min", "200",
            "--max", "400",
            "--step", "200",
            "--reps", "1",
            "--hours", "0.05",
            "--warmup", "0.01",
            "--seed", "2",
            "--no-optimize",
            "--csv", csvPath,
            "--out-result", resultPath
        });

        Assert.Equal(0, code);
        Assert.Contains("Scenario", File.ReadAllText(csvPath), StringComparison.Ordinal);
        var loaded = SimulationResultFile.Load(resultPath);
        Assert.Equal("study", loaded.Kind);
        Assert.Empty(loaded.Series);
        Assert.NotNull(loaded.Study);
        Assert.NotEmpty(loaded.Study.Means);
        Assert.Contains(loaded.Study.Means, point => point.LoadCratesPerHour == 200);
        Assert.Contains(loaded.Study.Means, point => point.LoadCratesPerHour == 400);
        Assert.NotEmpty(loaded.Study.Saturations);
        Assert.Equal("Steady fixture", loaded.ReadProject()!.Name);
        Assert.Equal(2, loaded.Run.Seed);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ssd-result-" + Guid.NewGuid().ToString("N"));

        public static TempDirectory Create()
        {
            var directory = new TempDirectory();
            Directory.CreateDirectory(directory.Path);
            return directory;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
