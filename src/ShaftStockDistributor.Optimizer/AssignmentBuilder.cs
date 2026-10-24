using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

/// <summary>
/// Builds a feasible assignment. A movers go to the exclusive ends, near the shuttle that has spare load.
/// B and C movers prefer the shared middle. A multi-shaft A article keeps one middle shaft as overflow.
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
        var geometry = new LayoutGeometry(project.Layout);
        var slots = ProjectGrid.Materialize(project);
        ClearUnpinned(slots);
        var targets = RemainingTargets(project, slots);
        var load = new Dictionary<ShuttleId, double>
        {
            [ShuttleId.Shuttle1] = 0,
            [ShuttleId.Shuttle2] = 0
        };

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
        int middle = remaining >= 3 ? 1 : 0;
        int exclusive = remaining - middle;
        for (int index = 0; index < exclusive; index++)
        {
            var owner = load[ShuttleId.Shuttle1] <= load[ShuttleId.Shuttle2]
                ? ShuttleId.Shuttle1
                : ShuttleId.Shuttle2;
            var zone = owner == ShuttleId.Shuttle1 ? ZoneKind.Shuttle1Only : ZoneKind.Shuttle2Only;
            var slot = Nearest(geometry, slots, zone, project.Shuttle(owner), preferNear: true)
                ?? NearestCenter(geometry, slots)
                ?? Nearest(
                    geometry,
                    slots,
                    owner == ShuttleId.Shuttle1 ? ZoneKind.Shuttle2Only : ZoneKind.Shuttle1Only,
                    project.Shuttle(owner == ShuttleId.Shuttle1 ? ShuttleId.Shuttle2 : ShuttleId.Shuttle1),
                    preferNear: true);
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
            if (slot is null)
            {
                var owner = load[ShuttleId.Shuttle1] <= load[ShuttleId.Shuttle2]
                    ? ShuttleId.Shuttle1
                    : ShuttleId.Shuttle2;
                var zone = owner == ShuttleId.Shuttle1 ? ZoneKind.Shuttle1Only : ZoneKind.Shuttle2Only;
                slot = Nearest(geometry, slots, zone, project.Shuttle(owner), preferNear: false)
                    ?? FirstFree(slots);
            }

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

        int recommended = Math.Max(1, ShaftCountCalculator.Recommend(
            article.InflowCratesPerHour,
            article.CratesPerPallet,
            article.Offset,
            geometry.CapacityFor(article),
            project.Optimizer.ExpectedPullWaitSeconds));
        double pulls = article.InflowCratesPerHour <= 0
            ? 0
            : (article.InflowCratesPerHour / article.PullTriggerCrates) / recommended;
        var zone = geometry.ZoneOfSegment(slot.Segment);
        var shuttle1 = project.Shuttle(ShuttleId.Shuttle1);
        var shuttle2 = project.Shuttle(ShuttleId.Shuttle2);
        double to1 = Hop(geometry, shuttle1, slot);
        double to2 = Hop(geometry, shuttle2, slot);
        double transfer = Math.Max(0, project.Layout.TransferDurationSeconds);
        if (zone == ZoneKind.Shared)
        {
            double hop = Math.Min(to1, to2);
            double hours = pulls * (hop + transfer) / 3600.0;
            load[ShuttleId.Shuttle1] += hours / 2.0;
            load[ShuttleId.Shuttle2] += hours / 2.0;
        }
        else
        {
            var owner = zone == ZoneKind.Shuttle1Only ? ShuttleId.Shuttle1 : ShuttleId.Shuttle2;
            double hop = owner == ShuttleId.Shuttle1 ? to1 : to2;
            load[owner] += pulls * (hop + transfer) / 3600.0;
        }
    }

    private static double Hop(LayoutGeometry geometry, ShuttleSettings shuttle, ShaftAssignment slot)
    {
        double yHome = geometry.LevelY(Math.Clamp(shuttle.Home.Level, 1, Math.Max(1, geometry.Levels)));
        return TravelTime.Simultaneous(
            geometry.ShaftX(slot.Segment, slot.Shaft) - shuttle.Home.XMeters,
            geometry.LevelY(slot.Level) - yHome,
            shuttle);
    }

    private static ShaftAssignment? Nearest(
        LayoutGeometry geometry,
        List<ShaftAssignment> slots,
        ZoneKind zone,
        ShuttleSettings shuttle,
        bool preferNear)
    {
        var free = slots.Where(slot => slot.ArticleId is null && geometry.ZoneOfSegment(slot.Segment) == zone);
        return preferNear
            ? free.OrderBy(slot => Hop(geometry, shuttle, slot)).ThenBy(slot => slot.Level).ThenBy(slot => slot.Shaft).FirstOrDefault()
            : free.OrderByDescending(slot => Hop(geometry, shuttle, slot)).ThenBy(slot => slot.Level).FirstOrDefault();
    }

    private static ShaftAssignment? NearestCenter(LayoutGeometry geometry, List<ShaftAssignment> slots)
    {
        var span = geometry.ZoneSpan(ZoneKind.Shared);
        if (span.End <= span.Start && geometry.ExclusiveSegmentCount == geometry.SegmentCount)
        {
            return null;
        }

        double center = (span.Start + span.End) / 2.0;
        return slots
            .Where(slot => slot.ArticleId is null && geometry.ZoneOfSegment(slot.Segment) == ZoneKind.Shared)
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
