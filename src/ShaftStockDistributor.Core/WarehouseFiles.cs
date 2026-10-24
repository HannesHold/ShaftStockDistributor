using System.Text.Json;

namespace ShaftStockDistributor.Core;

public static class WarehouseFiles
{
    public static Layout LoadLayout(string path) => WarehouseMapper.ToLayout(Read<LayoutDocument>(path));

    public static ArticleSet LoadArticles(string path) => WarehouseMapper.ToArticles(Read<ArticlesDocument>(path));

    public static Assignment LoadAssignment(string path) => WarehouseMapper.ToAssignment(Read<AssignmentDocument>(path));

    public static PlanningParameters LoadParameters(string path) => WarehouseMapper.ToParameters(Read<ParametersDocument>(path));

    public static void Save(string path, Layout layout) => Write(path, WarehouseMapper.ToDocument(layout));

    public static void Save(string path, ArticleSet articles) => Write(path, WarehouseMapper.ToDocument(articles));

    public static void Save(string path, Assignment assignment) => Write(path, WarehouseMapper.ToDocument(assignment));

    public static void Save(string path, PlanningParameters parameters) => Write(path, WarehouseMapper.ToDocument(parameters));

    public static string ToJson(Layout layout) => JsonSerializer.Serialize(WarehouseMapper.ToDocument(layout), JsonDefaults.Options);

    public static string ToJson(ArticleSet articles) => JsonSerializer.Serialize(WarehouseMapper.ToDocument(articles), JsonDefaults.Options);

    public static string ToJson(Assignment assignment) => JsonSerializer.Serialize(WarehouseMapper.ToDocument(assignment), JsonDefaults.Options);

    public static string ToJson(PlanningParameters parameters) => JsonSerializer.Serialize(WarehouseMapper.ToDocument(parameters), JsonDefaults.Options);

    public static Layout ParseLayout(string json) => WarehouseMapper.ToLayout(Parse<LayoutDocument>(json));

    public static ArticleSet ParseArticles(string json) => WarehouseMapper.ToArticles(Parse<ArticlesDocument>(json));

    public static Assignment ParseAssignment(string json) => WarehouseMapper.ToAssignment(Parse<AssignmentDocument>(json));

    public static PlanningParameters ParseParameters(string json) => WarehouseMapper.ToParameters(Parse<ParametersDocument>(json));

    private static T Read<T>(string path)
    {
        var json = File.ReadAllText(path);
        return Parse<T>(json);
    }

    private static T Parse<T>(string json)
    {
        var value = JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
        if (value is null)
        {
            throw new InvalidOperationException($"File did not contain a {typeof(T).Name}.");
        }

        return value;
    }

    private static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonDefaults.Options));
    }
}
