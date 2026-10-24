using System.Globalization;
using System.Text.Json;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

var exit = Run(args);
return exit;

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine(Help());
        return args.Length == 0 ? 1 : 0;
    }

    try
    {
        var options = Parse(args);
        return args[0] switch
        {
            "sample" => Sample(options),
            "optimize" => Optimize(options),
            "simulate" => Simulate(options),
            "compare" => Compare(options),
            "validate" => Validate(options),
            _ => Unknown(args[0])
        };
    }
    catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or IOException or JsonException)
    {
        Console.Error.WriteLine(ex.Message);
        return 3;
    }
}

static int Sample(Dictionary<string, string> options)
{
    var directory = Required(options, "out");
    Directory.CreateDirectory(directory);
    WarehouseFiles.Save(Path.Combine(directory, "layout.json"), WarehouseMapper.ToLayout(SampleWarehouse.Layout()));
    WarehouseFiles.Save(Path.Combine(directory, "articles.json"), WarehouseMapper.ToArticles(SampleWarehouse.Articles()));
    WarehouseFiles.Save(Path.Combine(directory, "parameters.json"), WarehouseMapper.ToParameters(SampleWarehouse.Parameters()));
    Console.WriteLine($"Wrote example layout, articles, and parameters to {directory}");
    Console.WriteLine("These files are example data, not measurements from a warehouse.");
    return 0;
}

static int Optimize(Dictionary<string, string> options)
{
    var layout = WarehouseFiles.LoadLayout(Required(options, "layout"));
    var articles = WarehouseFiles.LoadArticles(Required(options, "articles"));
    var parameters = LoadParameters(options);
    Assignment? current = options.TryGetValue("current", out var currentPath)
        ? WarehouseFiles.LoadAssignment(currentPath)
        : null;
    var start = options.ContainsKey("keep-current")
        ? OptimizationStart.KeepCurrent
        : OptimizationStart.Constructive;
    var result = AnnealingOptimizer.Optimize(layout, articles, parameters, current, start);
    var outPath = Required(options, "out");
    WarehouseFiles.Save(outPath, result.Assignment);
    Console.WriteLine($"Wrote {outPath}");
    Console.WriteLine(Invariant(
        $"Cost {result.ConstructiveCost.Total:0.##} -> {result.FinalCost.Total:0.##} (travel {result.FinalCost.Travel:0.#}, imbalance {result.FinalCost.Imbalance:0.#}, middle {result.FinalCost.MiddleTraffic:0.#}, penalty {result.FinalCost.Violation:0.#})"));
    Console.WriteLine(Invariant(
        $"Shuttle load {result.FinalCost.LoadShuttle1:0.#} / {result.FinalCost.LoadShuttle2:0.#}, accepted moves {result.AcceptedMoves}, iterations {result.Iterations}"));
    PrintDemand(layout, articles, parameters, result.Assignment);

    if (options.TryGetValue("naive-out", out var naivePath))
    {
        var naive = NaiveAssigner.AssignInOrder(layout, articles, parameters, current);
        naive.ExampleData = articles.ExampleData || layout.ExampleData;
        naive.Notes = "Naive baseline produced for comparison: articles in id order, packed from the left. Not a recommended plan.";
        WarehouseFiles.Save(naivePath, naive);
        var naiveCost = AssignmentCost.Evaluate(layout, articles, naive, parameters);
        Console.WriteLine(Invariant($"Wrote naive baseline {naivePath} (cost {naiveCost.Total:0.##})"));
    }

    return ReportValidation(layout, articles, result.Assignment);
}

static int Simulate(Dictionary<string, string> options)
{
    var layout = WarehouseFiles.LoadLayout(Required(options, "layout"));
    var articles = WarehouseFiles.LoadArticles(Required(options, "articles"));
    var assignment = WarehouseFiles.LoadAssignment(Required(options, "assignment"));
    var code = ReportValidation(layout, articles, assignment);
    if (code != 0)
    {
        return code;
    }

    var result = WarehouseSimulator.Run(layout, articles, assignment, ReadOptions(options));
    Console.Write(KpiReport.Format(result, "Simulation"));
    return result.CollisionViolations == 0 ? 0 : 4;
}

static int Compare(Dictionary<string, string> options)
{
    var layout = WarehouseFiles.LoadLayout(Required(options, "layout"));
    var articles = WarehouseFiles.LoadArticles(Required(options, "articles"));
    var first = WarehouseFiles.LoadAssignment(Required(options, "first"));
    var second = WarehouseFiles.LoadAssignment(Required(options, "second"));
    var firstCode = ReportValidation(layout, articles, first);
    var secondCode = ReportValidation(layout, articles, second);
    if (firstCode != 0 || secondCode != 0)
    {
        return 2;
    }

    var sim = ReadOptions(options);
    var left = WarehouseSimulator.Run(layout, articles, first, sim);
    var right = WarehouseSimulator.Run(layout, articles, second, sim);
    var leftName = options.GetValueOrDefault("first-name", "first");
    var rightName = options.GetValueOrDefault("second-name", "second");
    Console.Write(KpiReport.Compare(left, right, leftName, rightName));
    return left.CollisionViolations == 0 && right.CollisionViolations == 0 ? 0 : 4;
}

static int Validate(Dictionary<string, string> options)
{
    var layout = WarehouseFiles.LoadLayout(Required(options, "layout"));
    var articles = WarehouseFiles.LoadArticles(Required(options, "articles"));
    var assignment = options.TryGetValue("assignment", out var path)
        ? WarehouseFiles.LoadAssignment(path)
        : new Assignment();
    return ReportValidation(layout, articles, assignment);
}

static int ReportValidation(Layout layout, ArticleSet articles, Assignment assignment)
{
    var messages = WarehouseValidator.Validate(layout, articles, assignment);
    if (messages.Count == 0)
    {
        Console.WriteLine("Validation: ok");
        return 0;
    }

    foreach (var message in messages)
    {
        Console.WriteLine($"{message.Severity}: {message.Text}");
    }

    return WarehouseValidator.HasErrors(messages) ? 2 : 0;
}

static void PrintDemand(Layout layout, ArticleSet articles, PlanningParameters parameters, Assignment assignment)
{
    var inflows = InflowResolver.Resolve(articles);
    var demand = ShaftDemandCalculator.ForArticles(layout, articles, inflows, parameters);
    Console.WriteLine("Article demand (calculated shafts / assigned):");
    foreach (var article in articles.Items)
    {
        Console.WriteLine(Invariant(
            $"  {article.Id,-16} {article.AbcClass}  inflow {inflows.GetValueOrDefault(article.Id):0.##}/h  shafts {demand.GetValueOrDefault(article.Id)} / {assignment.CountFor(article.Id)}"));
    }
}

static SimulationOptions ReadOptions(Dictionary<string, string> options)
{
    return new SimulationOptions
    {
        Hours = options.TryGetValue("hours", out var hours) ? double.Parse(hours, CultureInfo.InvariantCulture) : 8,
        Seed = options.TryGetValue("seed", out var seed) ? int.Parse(seed, CultureInfo.InvariantCulture) : 1,
        RecordTrace = false
    };
}

static PlanningParameters LoadParameters(Dictionary<string, string> options) =>
    options.TryGetValue("parameters", out var path)
        ? WarehouseFiles.LoadParameters(path)
        : PlanningParameters.CreateDefault();

static Dictionary<string, string> Parse(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < args.Length; i++)
    {
        var token = args[i];
        if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length < 3)
        {
            throw new ArgumentException($"Unexpected argument '{token}'. Options look like --layout path.");
        }

        var key = token[2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            options[key] = args[++i];
        }
        else
        {
            options[key] = "true";
        }
    }

    return options;
}

static string Invariant(FormattableString message) => FormattableString.Invariant(message);

static string Required(Dictionary<string, string> options, string key) =>
    options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing --{key}.");

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    Console.Error.WriteLine(Help());
    return 1;
}

static string Help() =>
    """
    ShaftStockDistributor

    sample    --out <dir>
              Write example layout.json, articles.json, and parameters.json.
              The files are marked exampleData and are not warehouse measurements.

    optimize  --layout <file> --articles <file> --out <assignment.json>
              [--parameters <file>] [--current <assignment.json>] [--keep-current]
              [--naive-out <assignment.json>]
              Build an assignment and improve it by simulated annealing.
              Pinned shafts in --current stay put. --keep-current also starts from
              the other shafts instead of rebuilding them.
              --naive-out writes an articles-in-order baseline for compare.

    simulate  --layout <file> --articles <file> --assignment <file>
              [--parameters <file>] [--hours 8] [--seed 1]

    compare   --layout <file> --articles <file> --first <assignment.json> --second <assignment.json>
              [--parameters <file>] [--hours 8] [--seed 1]
              [--first-name optimized] [--second-name naive]

    validate  --layout <file> --articles <file> [--assignment <file>]

    Indexes in the JSON files are one-based. Exit code 0 is success, 1 is usage,
    2 is a validation error, 3 is a bad file, 4 means the simulation saw a shuttle collision.
    """;
