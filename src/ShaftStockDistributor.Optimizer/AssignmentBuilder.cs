using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

/// <summary>
/// Builds a feasible assignment. A movers go to each shuttle's exclusive segments, near the shuttle that has spare load.
/// B and C movers prefer a shared overlap. An A article that needs three or more shafts keeps one shared shaft as overflow.
/// </summary>
public static class AssignmentBuilder
{
    public static List<ShaftAssignment> Naive(DistributorProject project)
    {
        var slots = ProjectGrid.Materialize(project);
        ClearUnpinned(slots);
        var targets = RemainingTargets(project, slots);
        foreach (var article in project.Articles)
        {
            int left = targets[article.Id];
            if (left <= 0)
            {
                continue;
            }

            foreach (var slot in slots.OrderBy(s => s.Segment).ThenBy(s => s.Level).ThenBy(s => s.Shaft))
            {
                if (left <= 0)
                {
                    break;
                }

                if (slot.ArticleId is not null)
                {
                    continue;
                }

                slot.ArticleId = article.Id;
                left--;
            }
        }

        return slots;
    }

    public static List<ShaftAssignment> Constructive(DistributorProject project)
    {
        var geometry = LayoutGeometry.For(project);
        var slots = ProjectGrid.Materialize(project);
        ClearUnpinned(slots);
        var targets = RemainingTargets(project, slots);
        var load = new Dictionary<ShuttleId, double>();
        foreach (var shuttle in project.Shuttles.OrderBy(shuttle => shuttle.Id))
        {
            load.TryAdd(shuttle.Id, 0);
        }

        foreach (var slot in slots.Where(s => s.Pinned && s.ArticleId is not null))
        {
            var article = project.FindArticle(slot.ArticleId);
            if (article is null)
            {
                continue;
            }

            AddLoad(project, geometry, load, article, slot);
        }

        foreach (var abc in new[] { AbcClass.A, AbcClass.B, AbcClass.C })
        {
            foreach (var article in project.Articles
                         .Where(a => a.Class == abc)
                         .OrderByDescending(a => a.InflowCratesPerHour)
                         .ThenBy(a => a.Id, StringComparer.Ordinal))
            {
                if (abc == AbcClass.A)
                {
                    PlaceFastMover(project, geometry, slots, load, article, targets[article.Id]);
                }
                else
                {
                    PlaceSlowMover(project, geometry, slots, load, article, targets[article.Id]);
                }
            }
        }

        return slots;
    }

    private static void PlaceFastMover(
        DistributorProject project,
        LayoutGeometry geometry,
        List<ShaftAssignment> slots,
        Dictionary<ShuttleId, double> load,
        Article article,
        int remaining)
    {
        bool shared = geometry.SharedSpans().Count > 0;
        int middle = remaining >= 3 && shared ? 1 : 0;
        int exclusive = remaining - middle;
        for (int index = 0; index < exclusive; index++)
        {
            if (load.Count == 0)
            {
                break;
            }

            var owner = load.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
            var slot = NearestExclusive(project, geometry, slots, owner, preferNear: true)
                ?? NearestCenter(geometry, slots);
            if (slot is null)
            {
                foreach (var other in load.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key))
                {
                    if (other.Key == owner)
                    {
                        continue;
                    }

                    slot = NearestExclusive(project, geometry, slots, other.Key, preferNear: true);
                    if (slot is not null)
                    {
                        break;
                    }
                }
            }

            if (slot is null)
            {
                break;
            }

            slot.ArticleId = article.Id;
            AddLoad(project, geometry, load, article, slot);
        }

        if (middle == 1)
        {
            var slot = NearestCenter(geometry, slots) ?? FirstFree(slots);
            if (slot is not null)
            {
                slot.ArticleId = article.Id;
                AddLoad(project, geometry, load, article, slot);
            }
        }
    }

    private static void PlaceSlowMover(
        DistributorProject project,
        LayoutGeometry geometry,
        List<ShaftAssignment> slots,
        Dictionary<ShuttleId, double> load,
        Article article,
        int remaining)
    {
        for (int index = 0; index < remaining; index++)
        {
            var slot = NearestCenter(geometry, slots);
            if (slot is null && load.Count > 0)
            {
                var owner = load.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
                slot = NearestExclusive(project, geometry, slots, owner, preferNear: false)
                    ?? FirstFree(slots);
            }

            slot ??= FirstFree(slots);
            if (slot is null)
            {
                break;
            }

            slot.ArticleId = article.Id;
            AddLoad(project, geometry, load, article, slot);
        }
    }

    private static Dictionary<string, int> RemainingTargets(DistributorProject project, List<ShaftAssignment> slots)
    {
        var geometry = new LayoutGeometry(project.Layout);
        var targets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var article in project.Articles)
        {
            int recommended = ShaftCountCalculator.Recommend(
                article.InflowCratesPerHour,
                article.CratesPerPallet,
                article.Offset,
                geometry.CapacityFor(article),
                project.Optimizer.ExpectedPullWaitSeconds);
            recommended = Math.Min(recommended, geometry.ShaftCount);
            int pinned = slots.Count(slot => slot.Pinned && slot.ArticleId == article.Id);
            targets[article.Id] = Math.Max(0, recommended - pinned);
        }

        return targets;
    }

    private static void AddLoad(
        DistributorProject project,
        LayoutGeometry geometry,
        Dictionary<ShuttleId, double> load,
        Article article,
        ShaftAssignment slot)
    {
        if (slot.Segment < 1 || slot.Segment > geometry.SegmentCount || article.PullTriggerCrates <= 0)
        {
            return;
        }

        var servers = geometry.ServersOf(slot.Segment);
        if (servers.Count == 0)
        {
            return;
        }

        int recommended = Math.Max(1, ShaftCountCalculator.Recommend(
            article.InflowCratesPerHour,
            article.CratesPerPallet,
            article.Offset,
            geometry.CapacityFor(article),
            project.Optimizer.ExpectedPullWaitSeconds));
        double pulls = article.InflowCratesPerHour <= 0
            ? 0
            : (article.InflowCratesPerHour / article.PullTriggerCrates) / recommended;
        double hop = servers.Min(id => Hop(geometry, project.Shuttle(id), slot));
        double transfer = Math.Max(0, project.Layout.TransferDurationSeconds);
        double hours = pulls * (hop + transfer) / 3600.0;
        if (servers.Count == 1)
        {
            Add(load, servers[0], hours);
            return;
        }

        double share = hours / servers.Count;
        foreach (var id in servers)
        {
            Add(load, id, share);
        }
    }

    private static void Add(Dictionary<ShuttleId, double> load, ShuttleId id, double hours)
    {
        load.TryGetValue(id, out double current);
        load[id] = current + hours;
    }

    private static double Hop(LayoutGeometry geometry, ShuttleSettings shuttle, ShaftAssignment slot)
    {
        double yHome = geometry.LevelY(Math.Clamp(shuttle.Home.Level, 1, Math.Max(1, geometry.Levels)));
        return TravelTime.Simultaneous(
            geometry.ShaftX(slot.Segment, slot.Shaft) - shuttle.Home.XMeters,
            geometry.LevelY(slot.Level) - yHome,
            shuttle);
    }

    private static ShaftAssignment? NearestExclusive(
        DistributorProject project,
        LayoutGeometry geometry,
        List<ShaftAssignment> slots,
        ShuttleId owner,
        bool preferNear)
    {
        var shuttle = project.Shuttles.FirstOrDefault(candidate => candidate.Id == owner);
        if (shuttle is null)
        {
            return null;
        }

        var free = slots.Where(slot =>
            slot.ArticleId is null
            && slot.Segment >= 1
            && slot.Segment <= geometry.SegmentCount
            && geometry.ExclusiveOwner(slot.Segment) == owner);
        return preferNear
            ? free.OrderBy(slot => Hop(geometry, shuttle, slot)).ThenBy(slot => slot.Level).ThenBy(slot => slot.Shaft).FirstOrDefault()
            : free.OrderByDescending(slot => Hop(geometry, shuttle, slot)).ThenBy(slot => slot.Level).FirstOrDefault();
    }

    private static ShaftAssignment? NearestCenter(LayoutGeometry geometry, List<ShaftAssignment> slots)
    {
        var span = geometry.PrimarySharedSpan();
        if (span.End <= span.Start)
        {
            return null;
        }

        double center = (span.Start + span.End) / 2.0;
        return slots
            .Where(slot =>
                slot.ArticleId is null
                && slot.Segment >= 1
                && slot.Segment <= geometry.SegmentCount
                && geometry.IsShared(slot.Segment))
            .OrderBy(slot => Math.Abs(geometry.ShaftX(slot.Segment, slot.Shaft) - center))
            .ThenBy(slot => slot.Level)
            .ThenBy(slot => slot.Shaft)
            .FirstOrDefault();
    }

    private static ShaftAssignment? FirstFree(List<ShaftAssignment> slots) =>
        slots.FirstOrDefault(slot => slot.ArticleId is null);

    private static void ClearUnpinned(List<ShaftAssignment> slots)
    {
        foreach (var slot in slots)
        {
            if (!slot.Pinned)
            {
                slot.ArticleId = null;
            }
        }
    }
}
