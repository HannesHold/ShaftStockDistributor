using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaftStockDistributor.Core;

public static class ProjectSerializer
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Serialize(DistributorProject project)
    {
        project.SchemaVersion = CurrentSchemaVersion;
        return JsonSerializer.Serialize(project, Options);
    }

    public static DistributorProject Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<DistributorProject>(json, Options)
            ?? throw new InvalidDataException("The project file is empty.");
        project.Shuttles ??= new List<ShuttleSettings>();
        project.Articles ??= new List<Article>();
        project.CrateTypes ??= new List<CrateType>();
        project.Assignments ??= new List<ShaftAssignment>();
        project.Scenarios ??= new List<InflowScenario>();
        project.Layout ??= new LayoutSettings();
        project.Optimizer ??= new OptimizerSettings();
        project.Operation ??= new OperationSettings();
        project.Study ??= new StudySettings();
        foreach (var scenario in project.Scenarios)
        {
            scenario.ArticleWeights ??= new Dictionary<string, double>(StringComparer.Ordinal);
        }

        foreach (var article in project.Articles)
        {
            article.Dimensions ??= new CrateDimensions();
        }

        foreach (var crateType in project.CrateTypes)
        {
            crateType.Dimensions ??= new CrateDimensions();
        }

        foreach (var shuttle in project.Shuttles)
        {
            shuttle.Home ??= new ShuttlePose();
            shuttle.Handover ??= new ShuttlePose();
        }

        ShuttleLayout.FillUnsetRanges(project);
        return project;
    }

    public static DistributorProject Load(string path) => Deserialize(File.ReadAllText(path));

    public static void Save(string path, DistributorProject project)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(project));
    }

    public static DistributorProject Clone(DistributorProject project) => Deserialize(Serialize(project));

    public static string SerializeAssignments(DistributorProject project)
    {
        var payload = new AssignmentExport
        {
            Project = project.Name,
            IsExampleData = project.IsExampleData,
            AssignmentMode = project.AssignmentMode,
            Shafts = project.Assignments
                .Where(slot => !string.IsNullOrEmpty(slot.ArticleId) || slot.Pinned)
                .OrderBy(slot => slot.Address)
                .Select(slot => new AssignmentExportRow
                {
                    Segment = slot.Segment,
                    Level = slot.Level,
                    Shaft = slot.Shaft,
                    ArticleId = slot.ArticleId ?? "",
                    Pinned = slot.Pinned
                })
                .ToList()
        };
        return JsonSerializer.Serialize(payload, Options);
    }

    private sealed class AssignmentExport
    {
        public string Project { get; set; } = "";

        public bool IsExampleData { get; set; }

        public AssignmentMode AssignmentMode { get; set; }

        public List<AssignmentExportRow> Shafts { get; set; } = new();
    }

    private sealed class AssignmentExportRow
    {
        public int Segment { get; set; }

        public int Level { get; set; }

        public int Shaft { get; set; }

        public string ArticleId { get; set; } = "";

        public bool Pinned { get; set; }
    }
}
