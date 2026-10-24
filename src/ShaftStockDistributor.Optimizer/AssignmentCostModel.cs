using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

/// <summary>
/// Planning cost. Travel is estimated from each shuttle's home, because the real handover pose is still unknown.
/// Axes move together, so a hop takes the longer of the two axis times.
/// </summary>
public static class AssignmentCostModel
{
    public static CostBreakdown Evaluate(DistributorProject project, IReadOnlyList<ShaftAssignment> assignments)
    {
        var geometry = new LayoutGeometry(project.Layout);
        var weights = project.Optimizer;
        var shuttle1 = project.Shuttles.First(s => s.Id == ShuttleId.Shuttle1);
        var shuttle2 = project.Shuttles.First(s => s.Id == ShuttleId.Shuttle2);
        double transfer = Math.Max(0, project.Layout.TransferDurationSeconds);

        var byArticle = assignments
            .Where(slot => !string.IsNullOrEmpty(slot.ArticleId))
            .GroupBy(slot => slot.ArticleId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        double travelHours = 0;
        double load1 = 0;
        double load2 = 0;
        double middlePulls = 0;
        double totalPulls = 0;
        double penalty = 0;

        foreach (var article in project.Articles)
        {
            int capacity = geometry.CapacityFor(article);
            int recommended = Math.Min(
                geometry.ShaftCount,
                ShaftCountCalculator.Recommend(
                    article.InflowCratesPerHour,
                    article.CratesPerPallet,
                    article.Offset,
                    capacity,
                    weights.ExpectedPullWaitSeconds));
            byArticle.TryGetValue(article.Id, out var owned);
            int assigned = owned?.Count ?? 0;
            if (capacity < article.PullTriggerCrates && article.InflowCratesPerHour > 0)
            {
                penalty += 2;
            }

            if (assigned < recommended)
            {
                penalty += (recommended - assigned) * (1.0 + article.InflowCratesPerHour / 100.0);
            }

            if (assigned == 0 || article.PullTriggerCrates <= 0 || article.InflowCratesPerHour <= 0)
            {
                continue;
            }

            double pullsPerShaft = (article.InflowCratesPerHour / article.PullTriggerCrates) / assigned;
            foreach (var slot in owned!)
            {
                if (slot.Segment < 1 || slot.Segment > geometry.SegmentCount)
                {
                    penalty += 5;
                    continue;
                }

                var zone = geometry.ZoneOfSegment(slot.Segment);
                double to1 = Hop(geometry, shuttle1, slot);
                double to2 = Hop(geometry, shuttle2, slot);
                double hop;
                switch (zone)
                {
                    case ZoneKind.Shuttle1Only:
                        hop = to1;
                        break;
                    case ZoneKind.Shuttle2Only:
                        hop = to2;
                        break;
                    default:
                        hop = Math.Min(to1, to2);
                        middlePulls += pullsPerShaft;
                        break;
                }

                if (double.IsInfinity(hop))
                {
                    penalty += 5;
                    continue;
                }

                double hours = pullsPerShaft * (hop + transfer) / 3600.0;
                travelHours += hours;
                totalPulls += pullsPerShaft;
                if (zone == ZoneKind.Shared)
                {
                    if (to1 <= to2)
                    {
                        load1 += hours;
                    }
                    else
                    {
                        load2 += hours;
                    }
                }
                else if (zone == ZoneKind.Shuttle1Only)
                {
                    load1 += hours;
                }
                else
                {
                    load2 += hours;
                }
            }
        }

        double imbalance = Math.Abs(load1 - load2) / Math.Max(load1 + load2, 1e-6);
        double middle = totalPulls <= 1e-9 ? 0 : middlePulls / totalPulls;
        double total = weights.TravelWeight * travelHours
            + weights.ImbalanceWeight * imbalance
            + weights.MiddleTrafficWeight * middle
            + weights.PenaltyWeight * penalty;

        return new CostBreakdown
        {
            Total = total,
            Travel = travelHours,
            Imbalance = imbalance,
            MiddleTraffic = middle,
            Penalty = penalty,
            Shuttle1LoadHours = load1,
            Shuttle2LoadHours = load2
        };
    }

    private static double Hop(LayoutGeometry geometry, ShuttleSettings shuttle, ShaftAssignment slot)
    {
        double x = geometry.ShaftX(slot.Segment, slot.Shaft);
        double y = geometry.LevelY(slot.Level);
        double homeY = geometry.LevelY(Math.Clamp(shuttle.Home.Level, 1, Math.Max(1, geometry.Levels)));
        return TravelTime.Simultaneous(x - shuttle.Home.XMeters, y - homeY, shuttle);
    }
}
