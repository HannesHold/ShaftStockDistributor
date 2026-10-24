using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;

namespace ShaftStockDistributor.Simulation;

public sealed class SaturationSettings
{
    /// <summary>Zero or less disables the offered-versus-achieved rule.</summary>
    public double AchievedRatio { get; set; } = 0.98;

    public bool TreatLostCratesAsSaturation { get; set; } = true;

    /// <summary>Null disables the utilization rule.</summary>
    public double? UtilizationCeiling { get; set; }
}

public sealed class StudyRequest
{
    public double MinCratesPerHour { get; set; } = 600;

    public double MaxCratesPerHour { get; set; } = 4200;

    public double StepCratesPerHour { get; set; } = 600;

    public int Replications { get; set; } = 2;

    public int BaseSeed { get; set; } = 1;

    public double DurationHours { get; set; } = 1;

    public double WarmupHours { get; set; } = 0.1;

    public bool ReoptimizePerScenario { get; set; } = true;

    public bool CompareAssignmentModes { get; set; }

    public AssignmentMode Mode { get; set; } = AssignmentMode.Automatic;

    public int OptimizerIterations { get; set; } = 250;

    public SaturationSettings Thresholds { get; set; } = new();
}

public sealed class StudyProgress
{
    public int Completed { get; init; }

    public int Total { get; init; }

    public string Message { get; init; } = "";
}

public sealed class StudyPoint
{
    public string Scenario { get; init; } = "";

    public AssignmentMode Mode { get; init; }

    public double LoadCratesPerHour { get; init; }

    public int Replication { get; init; }

    public double OfferedPalletsPerHour { get; init; }

    public double AchievedPalletsPerHour { get; init; }

    public double Utilization1 { get; init; }

    public double Utilization2 { get; init; }

    public double AveragePullWaitSeconds { get; init; }

    public double MaxPullWaitSeconds { get; init; }

    public double BlockedSeconds { get; init; }

    public int OverflowingShafts { get; init; }

    public int LostCrates { get; init; }
}

public sealed class ScenarioSaturation
{
    public string Scenario { get; init; } = "";

    public AssignmentMode Mode { get; init; }

    public bool Found { get; init; }

    public double? SaturatedAtCratesPerHour { get; init; }

    public double MaxSustainablePalletsPerHour { get; init; }

    public string Reason { get; init; } = "";
}

public sealed class StudyResult
{
    public required IReadOnlyList<StudyPoint> Points { get; init; }

    public required IReadOnlyList<StudyPoint> Means { get; init; }

    public required IReadOnlyList<ScenarioSaturation> Saturations { get; init; }
}

public static class SaturationDetector
{
    public static ScenarioSaturation Detect(
        string scenario,
        AssignmentMode mode,
        IReadOnlyList<StudyPoint> meansInLoadOrder,
        SaturationSettings settings)
    {
        if (meansInLoadOrder.Count == 0)
        {
            return new ScenarioSaturation
            {
                Scenario = scenario,
                Mode = mode,
                Reason = "No load points were recorded."
            };
        }

        bool[] saturated = meansInLoadOrder.Select(point => IsSaturated(point, settings)).ToArray();
        int index = -1;
        for (int start = 0; start < saturated.Length; start++)
        {
            if (!saturated[start])
            {
                continue;
            }

            bool rest = true;
            for (int later = start; later < saturated.Length; later++)
            {
                if (!saturated[later])
                {
                    rest = false;
                    break;
                }
            }

            if (rest)
            {
                index = start;
                break;
            }
        }

        if (index < 0)
        {
            return new ScenarioSaturation
            {
                Scenario = scenario,
                Mode = mode,
                Found = false,
                MaxSustainablePalletsPerHour = meansInLoadOrder[^1].AchievedPalletsPerHour,
                Reason = "Achieved throughput still followed offered throughput at the highest load."
            };
        }

        var point = meansInLoadOrder[index];
        return new ScenarioSaturation
        {
            Scenario = scenario,
            Mode = mode,
            Found = true,
            SaturatedAtCratesPerHour = point.LoadCratesPerHour,
            MaxSustainablePalletsPerHour = index == 0 ? 0 : meansInLoadOrder[index - 1].AchievedPalletsPerHour,
            Reason = Explain(point, settings)
        };
    }

    public static bool IsSaturated(StudyPoint point, SaturationSettings settings)
    {
        if (settings.AchievedRatio > 0
            && point.OfferedPalletsPerHour > 1e-6
            && point.AchievedPalletsPerHour < settings.AchievedRatio * point.OfferedPalletsPerHour)
        {
            return true;
        }

        if (settings.TreatLostCratesAsSaturation && point.LostCrates > 0)
        {
            return true;
        }

        if (settings.UtilizationCeiling is double ceiling && (point.Utilization1 > ceiling || point.Utilization2 > ceiling))
        {
            return true;
        }

        return false;
    }

    public static string Explain(StudyPoint point, SaturationSettings settings)
    {
        var reasons = new List<string>();
        if (settings.AchievedRatio > 0
            && point.OfferedPalletsPerHour > 1e-6
            && point.AchievedPalletsPerHour < settings.AchievedRatio * point.OfferedPalletsPerHour)
        {
            reasons.Add($"achieved throughput is below {settings.AchievedRatio:P0} of offered");
        }

        if (settings.TreatLostCratesAsSaturation && point.LostCrates > 0)
        {
            reasons.Add("crates were lost");
        }

        if (settings.UtilizationCeiling is double ceiling && (point.Utilization1 > ceiling || point.Utilization2 > ceiling))
        {
            reasons.Add($"a shuttle is above {ceiling:P0} utilization");
        }

        return reasons.Count == 0
            ? "The load is still inside the sustainable range."
            : "Saturation: " + string.Join("; ", reasons) + ".";
    }
}

public static class ScenarioStudyRunner
{
    public static StudyResult Run(
        DistributorProject project,
        StudyRequest request,
        IProgress<StudyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var scenarios = project.Scenarios.Count > 0
            ? project.Scenarios
            : new List<InflowScenario>
            {
                new()
                {
                    Name = "Project inflow",
                    TotalCratesPerHour = Math.Max(0, project.Articles.Sum(article => article.InflowCratesPerHour)),
                    ShareA = 1,
                    ShareB = 0,
                    ShareC = 0
                }
            };
        var loads = BuildLoads(request.MinCratesPerHour, request.MaxCratesPerHour, request.StepCratesPerHour);
        var modes = request.CompareAssignmentModes
            ? new[] { AssignmentMode.Manual, AssignmentMode.Automatic }
            : new[] { request.Mode };
        int replications = Math.Max(1, request.Replications);
        int total = scenarios.Count * modes.Length * loads.Count * replications;
        int completed = 0;
        var points = new List<StudyPoint>(total);

        for (int scenarioIndex = 0; scenarioIndex < scenarios.Count; scenarioIndex++)
        {
            var scenario = scenarios[scenarioIndex];
            var baseProject = project.Clone();
            InflowAllocator.Apply(baseProject, scenario);
            foreach (var mode in modes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assigned = baseProject.Clone();
                assigned.AssignmentMode = mode;
                if (request.ReoptimizePerScenario)
                {
                    assigned.Optimizer.Seed = request.BaseSeed + scenarioIndex * 17 + (mode == AssignmentMode.Automatic ? 3 : 0);
                    var optimized = AnnealingOptimizer.Run(
                        assigned,
                        progress: null,
                        cancellationToken,
                        request.OptimizerIterations);
                    ProjectGrid.ReplaceAssignments(assigned, optimized.Assignments);
                }

                var baseline = assigned.Articles.ToDictionary(article => article.Id, article => article.InflowCratesPerHour, StringComparer.Ordinal);
                double scaleBase = scenario.TotalCratesPerHour <= 1e-9 ? 0 : 1.0 / scenario.TotalCratesPerHour;
                for (int loadIndex = 0; loadIndex < loads.Count; loadIndex++)
                {
                    double load = loads[loadIndex];
                    for (int replication = 0; replication < replications; replication++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var run = assigned.Clone();
                        run.AssignmentMode = mode;
                        foreach (var article in run.Articles)
                        {
                            double basis = baseline.TryGetValue(article.Id, out double value) ? value : 0;
                            article.InflowCratesPerHour = basis * load * scaleBase;
                        }

                        var result = SimulationSession.Run(run, new SimulationOptions
                        {
                            DurationSeconds = Math.Max(1, request.DurationHours * 3600.0),
                            WarmupSeconds = Math.Max(0, request.WarmupHours * 3600.0),
                            Seed = request.BaseSeed + replication * 1009 + loadIndex * 17 + scenarioIndex,
                            Mode = mode
                        }, cancellationToken);
                        points.Add(new StudyPoint
                        {
                            Scenario = scenario.Name,
                            Mode = mode,
                            LoadCratesPerHour = load,
                            Replication = replication + 1,
                            OfferedPalletsPerHour = result.OfferedPalletsPerHour,
                            AchievedPalletsPerHour = result.PalletsPerHour,
                            Utilization1 = result.Utilization1,
                            Utilization2 = result.Utilization2,
                            AveragePullWaitSeconds = result.AveragePullWaitSeconds,
                            MaxPullWaitSeconds = result.MaxPullWaitSeconds,
                            BlockedSeconds = result.BlockedSeconds,
                            OverflowingShafts = result.OverflowingShafts,
                            LostCrates = result.LostCrates
                        });
                        completed++;
                        progress?.Report(new StudyProgress
                        {
                            Completed = completed,
                            Total = total,
                            Message = $"{scenario.Name} / {mode} / {load:0} crates per hour"
                        });
                    }
                }
            }
        }

        var means = points
            .GroupBy(point => (point.Scenario, point.Mode, point.LoadCratesPerHour))
            .OrderBy(group => group.Key.Scenario, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Mode)
            .ThenBy(group => group.Key.LoadCratesPerHour)
            .Select(group => new StudyPoint
            {
                Scenario = group.Key.Scenario,
                Mode = group.Key.Mode,
                LoadCratesPerHour = group.Key.LoadCratesPerHour,
                OfferedPalletsPerHour = group.Average(point => point.OfferedPalletsPerHour),
                AchievedPalletsPerHour = group.Average(point => point.AchievedPalletsPerHour),
                Utilization1 = group.Average(point => point.Utilization1),
                Utilization2 = group.Average(point => point.Utilization2),
                AveragePullWaitSeconds = group.Average(point => point.AveragePullWaitSeconds),
                MaxPullWaitSeconds = group.Max(point => point.MaxPullWaitSeconds),
                BlockedSeconds = group.Average(point => point.BlockedSeconds),
                OverflowingShafts = (int)Math.Round(group.Average(point => point.OverflowingShafts)),
                LostCrates = (int)Math.Round(group.Average(point => (double)point.LostCrates))
            })
            .ToList();

        var saturations = means
            .GroupBy(point => (point.Scenario, point.Mode))
            .Select(group => SaturationDetector.Detect(
                group.Key.Scenario,
                group.Key.Mode,
                group.OrderBy(point => point.LoadCratesPerHour).ToList(),
                request.Thresholds))
            .ToList();

        return new StudyResult
        {
            Points = points,
            Means = means,
            Saturations = saturations
        };
    }

    public static List<double> BuildLoads(double min, double max, double step)
    {
        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The load step must be greater than zero.");
        }

        if (max < min)
        {
            (min, max) = (max, min);
        }

        var loads = new List<double>();
        int count = (int)Math.Floor((max - min) / step + 1e-9) + 1;
        for (int index = 0; index < count; index++)
        {
            loads.Add(min + index * step);
        }

        if (loads.Count == 0 || loads[^1] < max - step * 0.01)
        {
            loads.Add(max);
        }

        return loads;
    }

    public static string ToCsv(StudyResult result)
    {
        var lines = new List<string>
        {
            "Scenario,AssignmentMode,LoadCratesPerHour,Replication,OfferedPalletsPerHour,AchievedPalletsPerHour,Shuttle1Utilization,Shuttle2Utilization,AveragePullWaitSeconds,MaxPullWaitSeconds,BlockedSeconds,OverflowingShafts,LostCrates"
        };
        foreach (var point in result.Points)
        {
            lines.Add(string.Join(',',
                Csv(point.Scenario),
                point.Mode,
                point.LoadCratesPerHour.ToString("0.###"),
                point.Replication.ToString(),
                point.OfferedPalletsPerHour.ToString("0.###"),
                point.AchievedPalletsPerHour.ToString("0.###"),
                point.Utilization1.ToString("0.###"),
                point.Utilization2.ToString("0.###"),
                point.AveragePullWaitSeconds.ToString("0.###"),
                point.MaxPullWaitSeconds.ToString("0.###"),
                point.BlockedSeconds.ToString("0.###"),
                point.OverflowingShafts.ToString(),
                point.LostCrates.ToString()));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
