namespace ShaftStockDistributor.Core;

public sealed class Article
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public AbcClass AbcClass { get; set; } = AbcClass.B;

    /// <summary>Crates per hour. Null means "derive from the ABC share".</summary>
    public double? CratesPerHour { get; set; }

    public int CratesPerPallet { get; set; } = 40;

    /// <summary>Extra crates above a full pallet required before an automatic pull.</summary>
    public int Offset { get; set; } = 4;

    /// <summary>How many crates of this type fit in a shaft. Null uses the layout default.</summary>
    public int? ShaftCapacity { get; set; }

    public int PullThreshold => CratesPerPallet + Offset;
}

public sealed class AbcDefaults
{
    /// <summary>
    /// Pool divided across articles of a class that do not set <see cref="Article.CratesPerHour"/>.
    /// Articles with an explicit rate do not consume this pool.
    /// </summary>
    public double TotalCratesPerHour { get; set; } = 1500;

    public double ShareA { get; set; } = 0.70;

    public double ShareB { get; set; } = 0.20;

    public double ShareC { get; set; } = 0.10;

    public double Share(AbcClass abcClass) => abcClass switch
    {
        AbcClass.A => ShareA,
        AbcClass.B => ShareB,
        _ => ShareC
    };
}

public sealed class ArticleSet
{
    public AbcDefaults AbcDefaults { get; set; } = new();

    public int DefaultCratesPerPallet { get; set; } = 40;

    public int DefaultOffset { get; set; } = 4;

    public List<Article> Items { get; set; } = [];

    public bool ExampleData { get; set; }

    public string? Notes { get; set; }

    public Article? Find(string id) => Items.FirstOrDefault(article => article.Id == id);
}
