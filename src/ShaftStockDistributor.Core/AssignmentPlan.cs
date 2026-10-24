namespace ShaftStockDistributor.Core;

/// <summary>Remembers the inflow the current shaft plan was sized for.</summary>
public static class AssignmentPlan
{
    public static void RememberInflows(DistributorProject project)
    {
        foreach (var article in project.Articles)
        {
            article.PlannedInflowCratesPerHour = article.InflowCratesPerHour;
        }
    }

    public static double PlannedInflow(Article article) =>
        article.PlannedInflowCratesPerHour ?? article.InflowCratesPerHour;
}
