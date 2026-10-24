using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

/// <summary>
/// Builds a feasible assignment before local search.
/// A articles are split across the two shuttles by pull rate and placed near that shuttle's home,
/// in its exclusive zone. An A article with two or more shafts keeps one shaft in the shared middle
/// as flexible capacity. B and C articles prefer the shared middle because they are pulled rarely,
/// which keeps the shuttles out of the middle most of the time.
/// </summary>
public static class ConstructiveAssigner
{
    public static Assignment Build(
        Layout layout,
        ArticleSet articles,
        PlanningParameters parameters,
        Assignment? locked = null)
    {
        var assignment = new Assignment
        {
            Notes = "Constructive assignment. A movers near shuttle homes; B/C movers prefer the shared middle. Pins were kept."
        };
        var counts = new Dictionary<string, int>();
        if (locked is not null)
        {
            foreach (var (location, shaftAssignment) in locked.Shafts.OrderBy(pair => pair.Key))
            {
                if (!shaftAssignment.Pinned || layout.Find(location) is null || string.IsNullOrWhiteSpace(shaftAssignment.ArticleId))
                {
                    continue;
                }

                assignment.Set(location, shaftAssignment.ArticleId, pinned: true);
                counts[shaftAssignment.ArticleId] = counts.GetValueOrDefault(shaftAssignment.ArticleId) + 1;
            }
        }

        var inflows = InflowResolver.Resolve(articles);
        var demand = ShaftDemandCalculator.ForArticles(layout, articles, inflows, parameters);
        var zone1 = Pool(layout, Zone.Shuttle1Only, assignment, layout.Shuttle1Home);
        var zone2 = Pool(layout, Zone.Shuttle2Only, assignment, layout.Shuttle2Home);
        var shared = layout.Shafts.Where(shaft => shaft.Zone == Zone.Shared && !assignment.Contains(shaft.Location)).ToList();
        var sharedFrom1 = shared.OrderBy(shaft => layout.TravelSeconds(layout.Shuttle1Home, shaft)).ThenBy(shaft => shaft.Location).ToList();
        var sharedFrom2 = shared.OrderBy(shaft => layout.TravelSeconds(layout.Shuttle2Home, shaft)).ThenBy(shaft => shaft.Location).ToList();

        double load1 = SeedLoad(layout, assignment, articles, inflows, demand, shuttle: 1);
        double load2 = SeedLoad(layout, assignment, articles, inflows, demand, shuttle: 2);

        var fastMovers = articles.Items
            .Where(article => article.AbcClass == AbcClass.A)
            .OrderByDescending(article => inflows.GetValueOrDefault(article.Id))
            .ThenBy(article => article.Id, StringComparer.Ordinal)
            .ToList();

        var owner = new Dictionary<string, int>();
        foreach (var article in fastMovers)
        {
            var remaining = Remaining(article.Id, demand, counts);
            if (remaining <= 0)
            {
                continue;
            }

            var portion = PullsPerHour(article, inflows) * remaining / Math.Max(1, demand.GetValueOrDefault(article.Id));
            if (load1 <= load2)
            {
                owner[article.Id] = 1;
                load1 += portion;
            }
            else
            {
                owner[article.Id] = 2;
                load2 += portion;
            }
        }

        foreach (var article in fastMovers)
        {
            var remaining = Remaining(article.Id, demand, counts);
            if (remaining <= 0)
            {
                continue;
            }

            var shuttle = owner.GetValueOrDefault(article.Id, load1 <= load2 ? 1 : 2);
            var exclusive = shuttle == 1 ? zone1 : zone2;
            var flexPool = shuttle == 1 ? sharedFrom1 : sharedFrom2;
            var other = shuttle == 1 ? zone2 : zone1;
            var flex = remaining >= 2 ? 1 : 0;
            var placedExclusive = TakeSpread(exclusive, remaining - flex, assignment, article.Id, counts);
            var spill = (remaining - flex) - placedExclusive;
            var placedFlex = TakeClosest(flexPool, flex + spill, assignment, article.Id, counts);
            RemoveTaken(sharedFrom1, assignment);
            RemoveTaken(sharedFrom2, assignment);
            var leftover = (flex + spill) - placedFlex;
            if (leftover > 0)
            {
                TakeSpread(other, leftover, assignment, article.Id, counts);
            }
        }

        var slow = articles.Items
            .Where(article => article.AbcClass != AbcClass.A)
            .OrderBy(article => article.AbcClass)
            .ThenByDescending(article => inflows.GetValueOrDefault(article.Id))
            .ThenBy(article => article.Id, StringComparer.Ordinal)
            .ToList();

        var sharedSpread = shared.Where(shaft => !assignment.Contains(shaft.Location))
            .OrderBy(shaft => shaft.Location)
            .ToList();

        foreach (var article in slow)
        {
            var remaining = Remaining(article.Id, demand, counts);
            if (remaining <= 0)
            {
                continue;
            }

            var placed = TakeRoundRobin(sharedSpread, remaining, assignment, article.Id, counts);
            var leftover = remaining - placed;
            if (leftover <= 0)
            {
                continue;
            }

            var lighter = SeedLoad(layout, assignment, articles, inflows, demand, 1)
                <= SeedLoad(layout, assignment, articles, inflows, demand, 2)
                    ? zone1
                    : zone2;
            TakeFarthest(lighter, leftover, assignment, article.Id, counts);
        }

        var anywhere = layout.Shafts.Where(shaft => !assignment.Contains(shaft.Location))
            .OrderBy(shaft => shaft.Location)
            .ToList();
        foreach (var article in articles.Items.OrderBy(article => article.Id, StringComparer.Ordinal))
        {
            if (counts.GetValueOrDefault(article.Id) > 0)
            {
                continue;
            }

            TakeClosest(anywhere, 1, assignment, article.Id, counts);
        }

        return assignment;
    }

    private static List<Shaft> Pool(Layout layout, Zone zone, Assignment assignment, ShuttlePose home)
    {
        return layout.Shafts
            .Where(shaft => shaft.Zone == zone && !assignment.Contains(shaft.Location))
            .OrderBy(shaft => layout.TravelSeconds(home, shaft))
            .ThenBy(shaft => shaft.Location)
            .ToList();
    }

    private static double SeedLoad(
        Layout layout,
        Assignment assignment,
        ArticleSet articles,
        IReadOnlyDictionary<string, double> inflows,
        IReadOnlyDictionary<string, int> demand,
        int shuttle)
    {
        double load = 0;
        foreach (var (location, assigned) in assignment.Shafts)
        {
            var shaft = layout.Find(location);
            var article = articles.Find(assigned.ArticleId);
            if (shaft is null || article is null)
            {
                continue;
            }

            var serves = shaft.Zone switch
            {
                Zone.Shuttle1Only => shuttle == 1,
                Zone.Shuttle2Only => shuttle == 2,
                _ => true
            };
            if (!serves)
            {
                continue;
            }

            var share = shaft.Zone == Zone.Shared ? 0.5 : 1.0;
            var shafts = Math.Max(1, demand.GetValueOrDefault(article.Id));
            load += share * PullsPerHour(article, inflows) / shafts;
        }

        return load;
    }

    private static double PullsPerHour(Article article, IReadOnlyDictionary<string, double> inflows)
    {
        var threshold = Math.Max(1, article.PullThreshold);
        return Math.Max(0, inflows.GetValueOrDefault(article.Id)) / threshold;
    }

    private static int Remaining(string articleId, IReadOnlyDictionary<string, int> demand, Dictionary<string, int> counts) =>
        Math.Max(0, demand.GetValueOrDefault(articleId) - counts.GetValueOrDefault(articleId));

    private static int TakeSpread(List<Shaft> pool, int count, Assignment assignment, string articleId, Dictionary<string, int> counts)
    {
        if (count <= 0)
        {
            return 0;
        }

        var taken = 0;
        var usedSegments = new HashSet<int>();
        for (var index = 0; index < pool.Count && taken < count;)
        {
            var shaft = pool[index];
            if (assignment.Contains(shaft.Location) || usedSegments.Contains(shaft.Location.Segment))
            {
                index++;
                continue;
            }

            pool.RemoveAt(index);
            Place(assignment, shaft, articleId, counts);
            usedSegments.Add(shaft.Location.Segment);
            taken++;
        }

        while (taken < count && pool.Count > 0)
        {
            var shaft = pool[0];
            pool.RemoveAt(0);
            if (assignment.Contains(shaft.Location))
            {
                continue;
            }

            Place(assignment, shaft, articleId, counts);
            taken++;
        }

        return taken;
    }

    private static int TakeClosest(List<Shaft> pool, int count, Assignment assignment, string articleId, Dictionary<string, int> counts)
    {
        var taken = 0;
        while (taken < count && pool.Count > 0)
        {
            var shaft = pool[0];
            pool.RemoveAt(0);
            if (assignment.Contains(shaft.Location))
            {
                continue;
            }

            Place(assignment, shaft, articleId, counts);
            taken++;
        }

        return taken;
    }

    private static int TakeFarthest(List<Shaft> poolClosestFirst, int count, Assignment assignment, string articleId, Dictionary<string, int> counts)
    {
        var taken = 0;
        for (var index = poolClosestFirst.Count - 1; index >= 0 && taken < count; index--)
        {
            var shaft = poolClosestFirst[index];
            if (assignment.Contains(shaft.Location))
            {
                poolClosestFirst.RemoveAt(index);
                continue;
            }

            poolClosestFirst.RemoveAt(index);
            Place(assignment, shaft, articleId, counts);
            taken++;
        }

        return taken;
    }

    private static int TakeRoundRobin(List<Shaft> pool, int count, Assignment assignment, string articleId, Dictionary<string, int> counts)
    {
        var segments = pool.Select(shaft => shaft.Location.Segment).Distinct().OrderBy(segment => segment).ToList();
        if (segments.Count == 0 || count <= 0)
        {
            return 0;
        }

        var taken = 0;
        var cursor = 0;
        var idle = 0;
        while (taken < count && pool.Count > 0 && idle < segments.Count)
        {
            var segment = segments[cursor % segments.Count];
            cursor++;
            var index = pool.FindIndex(shaft => shaft.Location.Segment == segment && !assignment.Contains(shaft.Location));
            if (index < 0)
            {
                idle++;
                continue;
            }

            idle = 0;
            var shaft = pool[index];
            pool.RemoveAt(index);
            Place(assignment, shaft, articleId, counts);
            taken++;
        }

        return taken;
    }

    private static void RemoveTaken(List<Shaft> pool, Assignment assignment)
    {
        pool.RemoveAll(shaft => assignment.Contains(shaft.Location));
    }

    private static void Place(Assignment assignment, Shaft shaft, string articleId, Dictionary<string, int> counts)
    {
        assignment.Set(shaft.Location, articleId, pinned: false);
        counts[articleId] = counts.GetValueOrDefault(articleId) + 1;
    }
}
