using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

/// <summary>
/// Baseline: articles in id order, each given its calculated shaft count, packed from the left
/// (segment, then level, then shaft). Ignores zones and shuttle homes. Pins are kept.
/// </summary>
public static class NaiveAssigner
{
    public static Assignment AssignInOrder(
        Layout layout,
        ArticleSet articles,
        PlanningParameters parameters,
        Assignment? locked = null)
    {
        var assignment = new Assignment
        {
            Notes = "Naive baseline: articles in id order, packed from the left. Not an optimized plan."
        };
        var counts = new Dictionary<string, int>();
        var occupied = new HashSet<ShaftLocation>();
        if (locked is not null)
        {
            foreach (var (location, shaftAssignment) in locked.Shafts)
            {
                if (!shaftAssignment.Pinned || layout.Find(location) is null)
                {
                    continue;
                }

                assignment.Set(location, shaftAssignment.ArticleId, pinned: true);
                occupied.Add(location);
                counts[shaftAssignment.ArticleId] = counts.GetValueOrDefault(shaftAssignment.ArticleId) + 1;
            }
        }

        var inflows = InflowResolver.Resolve(articles);
        var demand = ShaftDemandCalculator.ForArticles(layout, articles, inflows, parameters);
        var cursor = layout.Shafts.OrderBy(shaft => shaft.Location).ToList();
        var index = 0;

        foreach (var article in articles.Items.OrderBy(article => article.Id, StringComparer.Ordinal))
        {
            var remaining = Math.Max(0, demand.GetValueOrDefault(article.Id) - counts.GetValueOrDefault(article.Id));
            while (remaining > 0 && index < cursor.Count)
            {
                var shaft = cursor[index++];
                if (!occupied.Add(shaft.Location))
                {
                    continue;
                }

                assignment.Set(shaft.Location, article.Id, pinned: false);
                remaining--;
            }
        }

        foreach (var article in articles.Items.OrderBy(article => article.Id, StringComparer.Ordinal))
        {
            if (assignment.CountFor(article.Id) > 0)
            {
                continue;
            }

            while (index < cursor.Count)
            {
                var shaft = cursor[index++];
                if (!occupied.Add(shaft.Location))
                {
                    continue;
                }

                assignment.Set(shaft.Location, article.Id, pinned: false);
                break;
            }
        }

        return assignment;
    }
}
