namespace ShaftStockDistributor.Core;

/// <summary>Splits a scenario total across ABC classes and then across the articles in each class.</summary>
public static class InflowAllocator
{
    public static IReadOnlyDictionary<string, double> Compute(IReadOnlyList<Article> articles, InflowScenario scenario)
    {
        double shareSum = scenario.ShareA + scenario.ShareB + scenario.ShareC;
        double normalizer = shareSum > 1e-9 ? shareSum : 1;
        var result = new Dictionary<string, double>(StringComparer.Ordinal);

        AllocateClass(articles, scenario, AbcClass.A, scenario.ShareA / normalizer, result);
        AllocateClass(articles, scenario, AbcClass.B, scenario.ShareB / normalizer, result);
        AllocateClass(articles, scenario, AbcClass.C, scenario.ShareC / normalizer, result);

        foreach (var article in articles)
        {
            result.TryAdd(article.Id, 0);
        }

        return result;
    }

    public static void Apply(DistributorProject project, InflowScenario scenario)
    {
        var flows = Compute(project.Articles, scenario);
        foreach (var article in project.Articles)
        {
            article.InflowCratesPerHour = flows.TryGetValue(article.Id, out double value) ? value : 0;
        }

        project.ActiveScenarioName = scenario.Name;
    }

    public static double OfferedPalletsPerHour(IEnumerable<Article> articles) =>
        articles.Sum(article => article.CratesPerPallet <= 0
            ? 0
            : article.InflowCratesPerHour / article.CratesPerPallet);

    private static void AllocateClass(
        IReadOnlyList<Article> articles,
        InflowScenario scenario,
        AbcClass abcClass,
        double normalizedShare,
        Dictionary<string, double> result)
    {
        var group = articles.Where(article => article.Class == abcClass).ToList();
        if (group.Count == 0)
        {
            return;
        }

        double budget = scenario.TotalCratesPerHour * normalizedShare;
        double weightSum = group.Sum(article => WeightOf(scenario, article));
        foreach (var article in group)
        {
            result[article.Id] = weightSum <= 1e-12 ? 0 : budget * WeightOf(scenario, article) / weightSum;
        }
    }

    private static double WeightOf(InflowScenario scenario, Article article)
    {
        if (scenario.ArticleWeights.TryGetValue(article.Id, out double weight))
        {
            return Math.Max(0, weight);
        }

        return Math.Max(0, article.ClassWeight);
    }
}
