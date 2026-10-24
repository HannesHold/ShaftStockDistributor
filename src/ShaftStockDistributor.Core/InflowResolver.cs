namespace ShaftStockDistributor.Core;

public static class InflowResolver
{
    /// <summary>
    /// Explicit <see cref="Article.CratesPerHour"/> wins. Otherwise the article receives an equal share of
    /// <c>totalCratesPerHour * classShare</c> among the articles of that class that also omit a rate.
    /// </summary>
    public static Dictionary<string, double> Resolve(ArticleSet articles)
    {
        var resolved = new Dictionary<string, double>(articles.Items.Count);
        foreach (var group in articles.Items.GroupBy(article => article.AbcClass))
        {
            var unspecified = group.Where(article => article.CratesPerHour is null).ToList();
            var pool = articles.AbcDefaults.TotalCratesPerHour * articles.AbcDefaults.Share(group.Key);
            var each = unspecified.Count == 0 ? 0 : pool / unspecified.Count;
            foreach (var article in group)
            {
                resolved[article.Id] = article.CratesPerHour ?? each;
            }
        }

        return resolved;
    }
}
