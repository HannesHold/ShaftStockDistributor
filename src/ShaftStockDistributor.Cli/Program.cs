using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Help();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0] switch
            {
                "sample" => Sample(args),
                "optimize" => Optimize(args),
                "simulate" => Simulate(args),
                "study" => Study(args),
                _ => Unknown(args[0])
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Sample(string[] args)
    {
        string path = Value(args, "--out") ?? "samples/example-warehouse.json";
        var project = ExampleWarehouse.Create();
        var optimized = AnnealingOptimizer.Run(project, iterations: 400);
        ProjectGrid.ReplaceAssignments(project, optimized.Assignments);
        ProjectSerializer.Save(path, project);
        Console.WriteLine($"Wrote example project to {path}");
        Console.WriteLine($"Naive cost {optimized.Naive.Total:0.###}; optimized cost {optimized.Optimized.Total:0.###}");
        return 0;
    }

    private static int Optimize(string[] args)
    {
        var project = Load(args);
        var result = AnnealingOptimizer.Run(project);
        ProjectGrid.ReplaceAssignments(project, result.Assignments);
        string? output = Value(args, "--out");
        if (output is not null)
        {
            ProjectSerializer.Save(output, project);
        }

        Console.WriteLine($"Before {result.Before}");
        Console.WriteLine($"Naive  {result.Naive}");
        Console.WriteLine($"Optimized {result.Optimized}");
        Console.WriteLine($"Changed shafts: {result.ChangedShafts}");
        return 0;
    }

    private static int Simulate(string[] args)
    {
        string path = ProjectPath(args);
        var project = ProjectSerializer.Load(path);
        double hours = Parse(Value(args, "--hours"), 2.5);
        double warmup = Parse(Value(args, "--warmup"), 1);
        int seed = (int)Parse(Value(args, "--seed"), 1);
        var mode = Enum.TryParse<AssignmentMode>(Value(args, "--mode"), true, out var parsed) ? parsed : project.AssignmentMode;
        var options = new SimulationOptions
        {
            DurationSeconds = hours * 3600,
            WarmupSeconds = warmup * 3600,
            Seed = seed,
            Mode = mode
        };
        string? resultPath = Value(args, "--out-result");
        if (resultPath is null)
        {
            PrintResult(SimulationSession.Run(project, options));
            return 0;
        }

        var document = SimulationResultFile.FromSimulation(project, options, path);
        SimulationResultFile.Save(resultPath, document);
        PrintDocument(document);
        Console.WriteLine($"Wrote {resultPath}");
        return 0;
    }

    private static int Study(string[] args)
    {
        string path = ProjectPath(args);
        var project = ProjectSerializer.Load(path);
        var request = new StudyRequest
        {
            MinCratesPerHour = Parse(Value(args, "--min"), project.Study.MinCratesPerHour),
            MaxCratesPerHour = Parse(Value(args, "--max"), project.Study.MaxCratesPerHour),
            StepCratesPerHour = Parse(Value(args, "--step"), project.Study.StepCratesPerHour),
            Replications = (int)Parse(Value(args, "--reps"), project.Study.Replications),
            BaseSeed = (int)Parse(Value(args, "--seed"), project.Study.BaseSeed),
            DurationHours = Parse(Value(args, "--hours"), project.Study.DurationHours),
            WarmupHours = Parse(Value(args, "--warmup"), project.Study.WarmupHours),
            ReoptimizePerScenario = !args.Contains("--no-optimize"),
            CompareAssignmentModes = args.Contains("--compare-modes"),
            Mode = project.AssignmentMode,
            OptimizerIterations = (int)Parse(Value(args, "--iterations"), 120),
            Thresholds = new SaturationSettings
            {
                AchievedRatio = project.Study.AchievedRatioThreshold,
                TreatLostCratesAsSaturation = project.Study.TreatLostCratesAsSaturation,
                UtilizationCeiling = project.Study.UtilizationCeiling
            }
        };
        var progress = new Progress<StudyProgress>(update =>
            Console.WriteLine($"[{update.Completed}/{update.Total}] {update.Message}"));
        var result = ScenarioStudyRunner.Run(project, request, progress);
        foreach (var point in result.Means)
        {
            Console.WriteLine(
                $"{point.Scenario} | {point.Mode} | {point.LoadCratesPerHour:0} crates/h | offered {point.OfferedPalletsPerHour:0.0} | achieved {point.AchievedPalletsPerHour:0.0} | util {FormatUtil(point)} | lost {point.LostCrates}");
        }

        foreach (var saturation in result.Saturations)
        {
            string at = saturation.Found ? $"{saturation.SaturatedAtCratesPerHour:0} crates/h" : "not reached";
            Console.WriteLine($"Saturation {saturation.Scenario} / {saturation.Mode}: {at}. Sustainable {saturation.MaxSustainablePalletsPerHour:0.0} pallets/h. {saturation.Reason}");
        }

        string? csv = Value(args, "--csv");
        if (csv is not null)
        {
            File.WriteAllText(csv, ScenarioStudyRunner.ToCsv(result));
            Console.WriteLine($"Wrote {csv}");
        }

        string? resultPath = Value(args, "--out-result");
        if (resultPath is not null)
        {
            SimulationResultFile.Save(resultPath, SimulationResultFile.FromStudy(project, request, result, path));
            Console.WriteLine($"Wrote {resultPath}");
        }

        return 0;
    }

    private static void PrintResult(SimulationResult result)
    {
        Console.WriteLine($"Pallets {result.Pallets:0.00} ({result.PalletsPerHour:0.00} per hour), offered {result.OfferedPalletsPerHour:0.00} per hour");
        if (result.Shuttles.Count == 0)
        {
            Console.WriteLine($"Pulls shuttle 1 {result.PullsShuttle1}, shuttle 2 {result.PullsShuttle2}");
            Console.WriteLine($"Utilization shuttle 1 {result.Utilization1:P1}, shuttle 2 {result.Utilization2:P1}");
        }
        else
        {
            Console.WriteLine("Pulls " + string.Join(", ", result.Shuttles.Select(shuttle => $"shuttle {shuttle.Id.Number} {shuttle.Pulls}")));
            Console.WriteLine("Utilization " + string.Join(", ", result.Shuttles.Select(shuttle => $"shuttle {shuttle.Id.Number} {shuttle.Utilization:P1}")));
        }
        Console.WriteLine($"Pull wait average {result.AveragePullWaitSeconds:0.0} s, max {result.MaxPullWaitSeconds:0.0} s");
        Console.WriteLine($"Blocked {result.BlockedSeconds:0.0} s, lost crates {result.LostCrates}, overflowing shafts {result.OverflowingShafts}");
        Console.WriteLine($"Controller jobs {result.ControllerPullJobs}, hardware jobs {result.HardwarePullJobs}");
    }

    private static void PrintDocument(SimulationResultDocument document)
    {
        var summary = document.Summary;
        Console.WriteLine($"Pallets {summary.Pallets:0.00} ({summary.AchievedPalletsPerHour:0.00} per hour), offered {summary.OfferedPalletsPerHour:0.00} per hour");
        Console.WriteLine("Pulls " + string.Join(", ", summary.Shuttles.Select(shuttle => $"shuttle {shuttle.Number} {shuttle.Pulls}")));
        Console.WriteLine("Utilization " + string.Join(", ", summary.Shuttles.Select(shuttle => $"shuttle {shuttle.Number} {shuttle.Utilization:P1}")));
        Console.WriteLine($"Pull wait average {summary.AveragePullWaitSeconds:0.0} s, max {summary.MaxPullWaitSeconds:0.0} s");
        Console.WriteLine($"Blocked {summary.BlockedSeconds:0.0} s, lost crates {summary.LostCrates}, overflowing shafts {summary.OverflowingShafts}");
    }

    private static string ProjectPath(string[] args)
    {
        string? path = Value(args, "--project") ?? Value(args, "-p");
        if (path is null)
        {
            throw new InvalidOperationException("Pass --project <file>.");
        }

        return path;
    }

    private static DistributorProject Load(string[] args) => ProjectSerializer.Load(ProjectPath(args));

    private static string? Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double Parse(string? text, double fallback) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    private static string FormatUtil(StudyPoint point)
    {
        if (point.UtilizationByShuttle.Count == 0)
        {
            return $"{point.Utilization1:P0}/{point.Utilization2:P0}";
        }

        return string.Join("/", point.UtilizationByShuttle.Select(value => value.ToString("P0")));
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Help();
        return 1;
    }

    private static void Help()
    {
        Console.WriteLine("""
            shaftstock sample [--out file]
            shaftstock optimize --project file [--out file]
            shaftstock simulate --project file [--hours 2.5] [--warmup 1] [--seed 1] [--mode Automatic|Manual] [--out-result file.ssdresult.json]
            shaftstock study --project file [--min 300] [--max 4000] [--step 500] [--reps 3] [--hours 2.5] [--warmup 1] [--iterations 120] [--compare-modes] [--no-optimize] [--csv file] [--out-result file.ssdresult.json]
            """);
    }
}
