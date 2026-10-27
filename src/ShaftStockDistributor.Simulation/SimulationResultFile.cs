using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Simulation;

/// <summary>
/// A self-contained recorded run. Version 1 is the only format this assembly writes.
/// </summary>
public sealed class SimulationResultDocument
{
    public string Format { get; set; } = SimulationResultFile.FormatName;

    public int Version { get; set; } = SimulationResultFile.CurrentVersion;

    /// <summary><c>simulation</c> for one run, <c>study</c> for a capacity sweep.</summary>
    public string Kind { get; set; } = "simulation";

    public DateTime CreatedUtc { get; set; }

    public ResultProject Project { get; set; } = new();

    public ResultRun Run { get; set; } = new();

    public ResultSummary Summary { get; set; } = new();

    public List<ResultSample> Series { get; set; } = new();

    public List<string> Events { get; set; } = new();

    public Dictionary<string, int> PullsByShaft { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> LostCratesByArticle { get; set; } = new(StringComparer.Ordinal);

    public ResultStudy? Study { get; set; }

    public bool HasSnapshot => Project.Snapshot.ValueKind == JsonValueKind.Object;

    public DistributorProject? ReadProject()
    {
        if (!HasSnapshot)
        {
            return null;
        }

        return ProjectSerializer.Deserialize(Project.Snapshot.GetRawText());
    }
}

public sealed class ResultProject
{
    public string Name { get; set; } = "";

    public string? SourcePath { get; set; }

    public string Sha256 { get; set; } = "";

    public JsonElement Snapshot { get; set; }
}

public sealed class ResultRun
{
    public int Seed { get; set; }

    public double DurationHours { get; set; }

    public double WarmupHours { get; set; }

    public string Mode { get; set; } = "";

    public string? ScenarioName { get; set; }

    /// <summary>Minutes of history used for <see cref="ResultSample.SteadyPalletsPerHour"/>.</summary>
    public double SampleWindowMinutes { get; set; } = 20;
}

public sealed class ResultSummary
{
    public double OfferedPalletsPerHour { get; set; }

    public double AchievedPalletsPerHour { get; set; }

    public double Pallets { get; set; }

    public double SteadyPallets { get; set; }

    public double OpeningPallets { get; set; }

    public double MeasuredHours { get; set; }

    public double AveragePullWaitSeconds { get; set; }

    public double MaxPullWaitSeconds { get; set; }

    public double BlockedSeconds { get; set; }

    public int LostCrates { get; set; }

    public int OverflowingShafts { get; set; }

    public int ReassignmentCount { get; set; }

    public List<ResultShuttle> Shuttles { get; set; } = new();
}

public sealed class ResultShuttle
{
    public int Number { get; set; }

    public int Pulls { get; set; }

    public double Utilization { get; set; }
}

public sealed class ResultSample
{
    public double TimeHours { get; set; }

    public double SteadyPalletsPerHour { get; set; }

    public bool WarmingUp { get; set; }
}

public sealed class ResultStudy
{
    public double TheoreticalPalletsPerHour { get; set; }

    public List<ResultStudyPoint> Points { get; set; } = new();

    public List<ResultStudyPoint> Means { get; set; } = new();

    public List<ResultSaturation> Saturations { get; set; } = new();
}

public sealed class ResultStudyPoint
{
    public string Scenario { get; set; } = "";

    public string Mode { get; set; } = "";

    public double LoadCratesPerHour { get; set; }

    public int Replication { get; set; }

    public double OfferedPalletsPerHour { get; set; }

    public double AchievedPalletsPerHour { get; set; }

    public int LostCrates { get; set; }

    public double BlockedSeconds { get; set; }

    public double AveragePullWaitSeconds { get; set; }

    public List<double> UtilizationByShuttle { get; set; } = new();
}

public sealed class ResultSaturation
{
    public string Scenario { get; set; } = "";

    public string Mode { get; set; } = "";

    public bool Found { get; set; }

    public double? SaturatedAtCratesPerHour { get; set; }

    public double MaxSustainablePalletsPerHour { get; set; }

    public string Reason { get; set; } = "";
}

public static class SimulationResultFile
{
    public const string FormatName = "ssdresult";

    public const int CurrentVersion = 1;

    public const double SampleWindowMinutes = 20;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = ProjectSerializer.CreateOptions();
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }

    public static string Fingerprint(DistributorProject project)
    {
        string json = ProjectSerializer.Serialize(project);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static SimulationResultDocument FromSimulation(
        DistributorProject project,
        SimulationOptions options,
        string? sourcePath = null,
        double sampleSeconds = 60)
    {
        var captured = CaptureProject(project, sourcePath);
        var session = SimulationSession.Start(project, options);
        var rate = new PalletRateWindow();
        var series = new List<ResultSample>();
        double duration = Math.Max(0, options.DurationSeconds);
        double step = sampleSeconds <= 1 ? 60 : sampleSeconds;
        for (double mark = Math.Min(step, duration); mark < duration - 1e-6; mark += step)
        {
            session.AdvanceTo(mark);
            Remember(session, rate, series);
        }

        session.AdvanceTo(duration);
        Remember(session, rate, series);
        var result = session.ToResult();
        var events = session.Capture().Events.ToList();
        string mode = (options.Mode ?? project.AssignmentMode).ToString();
        return Assemble(captured, mode, options.Seed, options.DurationSeconds, options.WarmupSeconds, project.ActiveScenarioName, result, series, events, study: null, kind: "simulation");
    }

    public static SimulationResultDocument FromStudy(
        DistributorProject project,
        StudyRequest request,
        StudyResult result,
        string? sourcePath = null)
    {
        var captured = CaptureProject(project, sourcePath);
        var headline = result.Means.OrderByDescending(point => point.AchievedPalletsPerHour).FirstOrDefault();
        var summary = new ResultSummary();
        if (headline is not null)
        {
            summary.OfferedPalletsPerHour = headline.OfferedPalletsPerHour;
            summary.AchievedPalletsPerHour = headline.AchievedPalletsPerHour;
            summary.MeasuredHours = headline.MeasuredHours;
            summary.AveragePullWaitSeconds = headline.AveragePullWaitSeconds;
            summary.MaxPullWaitSeconds = headline.MaxPullWaitSeconds;
            summary.BlockedSeconds = headline.BlockedSeconds;
            summary.LostCrates = headline.LostCrates;
            summary.OverflowingShafts = headline.OverflowingShafts;
            summary.Shuttles = headline.UtilizationByShuttle.Select((utilization, index) => new ResultShuttle
            {
                Number = index + 1,
                Utilization = utilization
            }).ToList();
        }

        return new SimulationResultDocument
        {
            CreatedUtc = DateTime.UtcNow,
            Kind = "study",
            Project = captured,
            Run = new ResultRun
            {
                Seed = request.BaseSeed,
                DurationHours = request.DurationHours,
                WarmupHours = request.WarmupHours,
                Mode = request.CompareAssignmentModes ? "Manual and automatic" : request.Mode.ToString(),
                ScenarioName = headline?.Scenario,
                SampleWindowMinutes = SampleWindowMinutes
            },
            Summary = summary,
            Study = new ResultStudy
            {
                TheoreticalPalletsPerHour = result.TheoreticalPalletsPerHour,
                Points = result.Points.Select(CopyPoint).ToList(),
                Means = result.Means.Select(CopyPoint).ToList(),
                Saturations = result.Saturations.Select(item => new ResultSaturation
                {
                    Scenario = item.Scenario,
                    Mode = item.Mode.ToString(),
                    Found = item.Found,
                    SaturatedAtCratesPerHour = item.SaturatedAtCratesPerHour,
                    MaxSustainablePalletsPerHour = item.MaxSustainablePalletsPerHour,
                    Reason = item.Reason
                }).ToList()
            }
        };
    }

    public static string Serialize(SimulationResultDocument document) => JsonSerializer.Serialize(document, Options);

    public static SimulationResultDocument Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<SimulationResultDocument>(json, Options)
            ?? throw new InvalidDataException("The result file is empty.");
        if (!string.Equals(document.Format, FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("This file is not a Shaft Stock Distributor result.");
        }

        if (document.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Result version {document.Version} is not supported. This app reads version {CurrentVersion}.");
        }

        document.Kind = string.IsNullOrWhiteSpace(document.Kind) ? "simulation" : document.Kind;
        document.Project ??= new ResultProject();
        document.Run ??= new ResultRun();
        document.Summary ??= new ResultSummary();
        document.Summary.Shuttles ??= new List<ResultShuttle>();
        document.Series ??= new List<ResultSample>();
        document.Events ??= new List<string>();
        document.PullsByShaft ??= new Dictionary<string, int>(StringComparer.Ordinal);
        document.LostCratesByArticle ??= new Dictionary<string, int>(StringComparer.Ordinal);
        if (document.Study is not null)
        {
            document.Study.Points ??= new List<ResultStudyPoint>();
            document.Study.Means ??= new List<ResultStudyPoint>();
            document.Study.Saturations ??= new List<ResultSaturation>();
        }

        return document;
    }

    public static void Save(string path, SimulationResultDocument document)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(document));
    }

    public static SimulationResultDocument Load(string path) => Deserialize(File.ReadAllText(path));

    private static void Remember(SimulationSession session, PalletRateWindow rate, List<ResultSample> series)
    {
        var partial = session.ToResult();
        rate.Observe(session.Now, partial.SteadyPallets);
        var reading = rate.Read(session.Now, SampleWindowMinutes * 60);
        series.Add(new ResultSample
        {
            TimeHours = session.Now / 3600.0,
            SteadyPalletsPerHour = reading.PalletsPerHour,
            WarmingUp = reading.WarmingUp
        });
    }

    private static SimulationResultDocument Assemble(
        ResultProject project,
        string mode,
        int seed,
        double durationSeconds,
        double warmupSeconds,
        string? scenarioName,
        SimulationResult result,
        List<ResultSample> series,
        List<string> events,
        ResultStudy? study,
        string kind) => new()
    {
        CreatedUtc = DateTime.UtcNow,
        Kind = kind,
        Project = project,
        Run = new ResultRun
        {
            Seed = seed,
            DurationHours = durationSeconds / 3600.0,
            WarmupHours = warmupSeconds / 3600.0,
            Mode = mode,
            ScenarioName = scenarioName,
            SampleWindowMinutes = SampleWindowMinutes
        },
        Summary = new ResultSummary
        {
            OfferedPalletsPerHour = result.OfferedPalletsPerHour,
            AchievedPalletsPerHour = result.PalletsPerHour,
            Pallets = result.Pallets,
            SteadyPallets = result.SteadyPallets,
            OpeningPallets = result.OpeningPallets,
            MeasuredHours = result.MeasuredSeconds / 3600.0,
            AveragePullWaitSeconds = result.AveragePullWaitSeconds,
            MaxPullWaitSeconds = result.MaxPullWaitSeconds,
            BlockedSeconds = result.BlockedSeconds,
            LostCrates = result.LostCrates,
            OverflowingShafts = result.OverflowingShafts,
            ReassignmentCount = result.ReassignmentCount,
            Shuttles = result.Shuttles.Select(shuttle => new ResultShuttle
            {
                Number = shuttle.Id.Number,
                Pulls = shuttle.Pulls,
                Utilization = shuttle.Utilization
            }).ToList()
        },
        Series = series,
        Events = events,
        PullsByShaft = new Dictionary<string, int>(result.PullsByShaft, StringComparer.Ordinal),
        LostCratesByArticle = new Dictionary<string, int>(result.LostCratesByArticle, StringComparer.Ordinal),
        Study = study
    };

    private static ResultProject CaptureProject(DistributorProject project, string? sourcePath)
    {
        string json = ProjectSerializer.Serialize(project);
        using var parsed = JsonDocument.Parse(json);
        return new ResultProject
        {
            Name = project.Name,
            SourcePath = sourcePath,
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),
            Snapshot = parsed.RootElement.Clone()
        };
    }

    private static ResultStudyPoint CopyPoint(StudyPoint point) => new()
    {
        Scenario = point.Scenario,
        Mode = point.Mode.ToString(),
        LoadCratesPerHour = point.LoadCratesPerHour,
        Replication = point.Replication,
        OfferedPalletsPerHour = point.OfferedPalletsPerHour,
        AchievedPalletsPerHour = point.AchievedPalletsPerHour,
        LostCrates = point.LostCrates,
        BlockedSeconds = point.BlockedSeconds,
        AveragePullWaitSeconds = point.AveragePullWaitSeconds,
        UtilizationByShuttle = point.UtilizationByShuttle.ToList()
    };
}
