namespace ShaftStockDistributor.Core;

/// <summary>Whether the stored shaft plan still addresses the current layout and catalog.</summary>
public static class AssignmentFit
{
    public static bool MatchesLayout(DistributorProject project)
    {
        var geometry = new LayoutGeometry(project.Layout);
        if (geometry.ShaftCount <= 0)
        {
            return project.Assignments.Count == 0;
        }

        var expected = new HashSet<ShaftAddress>();
        foreach (var address in geometry.AllAddresses())
        {
            expected.Add(address);
        }

        if (project.Assignments.Count != expected.Count)
        {
            return false;
        }

        var seen = new HashSet<ShaftAddress>();
        foreach (var slot in project.Assignments)
        {
            if (!expected.Contains(slot.Address) || !seen.Add(slot.Address))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(slot.ArticleId) && project.FindArticle(slot.ArticleId) is null)
            {
                return false;
            }
        }

        return seen.Count == expected.Count;
    }
}
