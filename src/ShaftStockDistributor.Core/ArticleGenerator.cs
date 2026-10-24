using System.Globalization;

namespace ShaftStockDistributor.Core;

public enum ArticleGenerationError
{
    None = 0,
    Count = 1,
    NegativePercent = 2,
    PercentSum = 3,
    NoCrateTypes = 4
}

public sealed record ArticleGenerationRequest
{
    public const int MaxCount = 2000;

    public int Count { get; init; }

    public double PercentA { get; init; }

    public double PercentB { get; init; }

    public double PercentC { get; init; }

    public bool ReplaceExisting { get; init; }

    /// <summary>Null draws from a non-deterministic generator. A value repeats the same catalog.</summary>
    public int? Seed { get; init; }

    public double TotalCratesPerHour { get; init; } = 1800;

    public IReadOnlyList<CrateType> CrateTypes { get; init; } = [];
}

public sealed class ArticleGenerationResult
{
    public bool Succeeded { get; init; }

    public ArticleGenerationError Error { get; init; }

    public IReadOnlyList<Article> Articles { get; init; } = [];

    public int CountA { get; init; }

    public int CountB { get; init; }

    public int CountC { get; init; }

    public static ArticleGenerationResult Fail(ArticleGenerationError error) =>
        new() { Succeeded = false, Error = error };
}

/// <summary>
/// Builds an English article catalog from configured crate types.
/// Count percentages use the largest-remainder method so the classes sum to the requested count.
/// Every A mover is given a higher demand weight than every B mover, and every B mover a higher weight than every C mover.
/// </summary>
public static class ArticleGenerator
{
    private const double PercentTolerance = 0.05;

    private static readonly string[] Products =
    [
        "Spring water", "Mineral water", "Sparkling water", "Still water",
        "Apple juice", "Orange juice", "Grape juice", "Tomato juice",
        "Cola", "Lemonade", "Ginger ale", "Iced tea",
        "Lager", "Pilsner", "Pale ale", "Wheat beer",
        "Red wine", "White wine", "Cider", "Prosecco",
        "Energy drink", "Sports drink", "Tonic water", "Soda water"
    ];

    private static readonly string[] Colors =
    [
        "#3D7EFF", "#00B3A4", "#7C5CFF", "#F0A202", "#E07A3D",
        "#C45C26", "#8BD450", "#5AA9E6", "#E85D75", "#4C8DFF",
        "#2EC4B6", "#9B5DE5", "#F15BB5", "#00BBF9", "#00F5D4"
    ];

    public static ArticleGenerationError Validate(ArticleGenerationRequest request)
    {
        if (request.Count < 1 || request.Count > ArticleGenerationRequest.MaxCount)
        {
            return ArticleGenerationError.Count;
        }

        if (request.PercentA < 0 || request.PercentB < 0 || request.PercentC < 0)
        {
            return ArticleGenerationError.NegativePercent;
        }

        double sum = request.PercentA + request.PercentB + request.PercentC;
        if (Math.Abs(sum - 100.0) > PercentTolerance)
        {
            return ArticleGenerationError.PercentSum;
        }

        if (UsableTypes(request).Count == 0)
        {
            return ArticleGenerationError.NoCrateTypes;
        }

        return ArticleGenerationError.None;
    }

    public static (int A, int B, int C) SplitCounts(int count, double percentA, double percentB, double percentC)
    {
        double[] exact = [count * percentA / 100.0, count * percentB / 100.0, count * percentC / 100.0];
        var whole = new int[3];
        for (int index = 0; index < 3; index++)
        {
            whole[index] = (int)Math.Floor(exact[index] + 1e-9);
        }

        int remainder = count - (whole[0] + whole[1] + whole[2]);
        var order = new[] { 0, 1, 2 }
            .OrderByDescending(index => exact[index] - whole[index])
            .ThenBy(index => index)
            .ToArray();
        if (remainder > 0)
        {
            for (int step = 0; step < remainder; step++)
            {
                whole[order[step % 3]]++;
            }
        }
        else if (remainder < 0)
        {
            var peel = order.Reverse().ToArray();
            for (int step = 0; step < -remainder; step++)
            {
                int index = peel[step % 3];
                if (whole[index] > 0)
                {
                    whole[index]--;
                }
            }
        }

        return (whole[0], whole[1], whole[2]);
    }

    public static ArticleGenerationResult Generate(ArticleGenerationRequest request, ISet<string>? reservedIds = null)
    {
        var error = Validate(request);
        if (error != ArticleGenerationError.None)
        {
            return ArticleGenerationResult.Fail(error);
        }

        var types = UsableTypes(request);
        var (countA, countB, countC) = SplitCounts(request.Count, request.PercentA, request.PercentB, request.PercentC);
        var random = request.Seed is int seed ? new Random(seed) : Random.Shared;
        var usedIds = reservedIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(reservedIds, StringComparer.Ordinal);
        var articles = new List<Article>(request.Count);
        var weights = new List<double>(request.Count);
        AddClass(articles, weights, usedIds, random, types, AbcClass.A, countA, 5.0);
        AddClass(articles, weights, usedIds, random, types, AbcClass.B, countB, 2.0);
        AddClass(articles, weights, usedIds, random, types, AbcClass.C, countC, 0.7);

        double totalWeight = weights.Sum();
        double totalCrates = Math.Max(0, request.TotalCratesPerHour);
        for (int index = 0; index < articles.Count; index++)
        {
            double share = totalWeight <= 1e-12 ? 0 : weights[index] / totalWeight;
            articles[index].ClassWeight = weights[index];
            articles[index].InflowCratesPerHour = totalCrates * share;
            articles[index].PlannedInflowCratesPerHour = null;
        }

        return new ArticleGenerationResult
        {
            Succeeded = true,
            Articles = articles,
            CountA = countA,
            CountB = countB,
            CountC = countC
        };
    }

    /// <summary>
    /// Writes the catalog onto the project and clears the shaft plan.
    /// Layout, scenarios, and the project name stay in place.
    /// </summary>
    public static ArticleGenerationResult Apply(DistributorProject project, ArticleGenerationRequest request)
    {
        var reserved = request.ReplaceExisting
            ? new HashSet<string>(StringComparer.Ordinal)
            : project.Articles.Select(article => article.Id).ToHashSet(StringComparer.Ordinal);
        var generated = Generate(request, reserved);
        if (!generated.Succeeded)
        {
            return generated;
        }

        if (request.ReplaceExisting)
        {
            project.Articles.Clear();
        }

        project.Articles.AddRange(generated.Articles);
        project.IsExampleData = false;
        var liveIds = project.Articles.Select(article => article.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var slot in project.Assignments)
        {
            slot.ArticleId = null;
            slot.Pinned = false;
        }

        foreach (var article in project.Articles)
        {
            article.PlannedInflowCratesPerHour = null;
        }

        foreach (var scenario in project.Scenarios)
        {
            foreach (string key in scenario.ArticleWeights.Keys.Where(key => !liveIds.Contains(key)).ToList())
            {
                scenario.ArticleWeights.Remove(key);
            }
        }

        return generated;
    }

    private static void AddClass(
        List<Article> articles,
        List<double> weights,
        HashSet<string> usedIds,
        Random random,
        IReadOnlyList<CrateType> types,
        AbcClass abcClass,
        int count,
        double baseWeight)
    {
        for (int index = 0; index < count; index++)
        {
            var crate = types[random.Next(types.Count)];
            string product = Products[random.Next(Products.Length)];
            double jitter = 0.85 + (random.NextDouble() * 0.15);
            string id = NextId(usedIds, abcClass);
            usedIds.Add(id);
            articles.Add(new Article
            {
                Id = id,
                Name = product + ", " + crate.Name.ToLower(CultureInfo.InvariantCulture),
                Class = abcClass,
                Dimensions = Copy(crate.Dimensions),
                CratesPerPallet = crate.CratesPerPallet,
                Offset = crate.Offset,
                ColorHex = Colors[articles.Count % Colors.Length]
            });
            weights.Add(baseWeight * jitter);
        }
    }

    private static string NextId(HashSet<string> usedIds, AbcClass abcClass)
    {
        string prefix = abcClass + "-";
        int number = 1;
        while (usedIds.Contains(prefix + number.ToString("000", CultureInfo.InvariantCulture)))
        {
            number++;
        }

        return prefix + number.ToString("000", CultureInfo.InvariantCulture);
    }

    private static CrateDimensions Copy(CrateDimensions source) => new()
    {
        LengthMillimeters = source.LengthMillimeters,
        WidthMillimeters = source.WidthMillimeters,
        HeightMillimeters = source.HeightMillimeters,
        FlowAxis = source.FlowAxis
    };

    private static List<CrateType> UsableTypes(ArticleGenerationRequest request) =>
        (request.CrateTypes ?? [])
            .Where(type => type.CratesPerPallet >= 1
                && type.Dimensions is { LengthMillimeters: > 0, WidthMillimeters: > 0, HeightMillimeters: > 0 }
                && !string.IsNullOrWhiteSpace(type.Name))
            .ToList();
}
