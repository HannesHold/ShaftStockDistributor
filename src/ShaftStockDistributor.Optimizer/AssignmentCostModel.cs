using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

/// <summary>
/// Planning cost. Travel is estimated from each shuttle's home, because the real handover pose is still unknown.
/// Axes move together, so a hop takes the longer of the two axis times.
/// A shared shaft is charged to the nearest shuttle that may serve it. With two shuttles that is the old rule,
/// and imbalance is still <c>(max load - min load) / sum</c>, which is <c>|load1 - load2| / (load1 + load2)</c>.
/// </summary>
public static class AssignmentCostModel
{
    public static CostBreakdown Evaluate(DistributorProject project, IReadOnlyList<ShaftAssignment> assignments)
    {
        var geometry = LayoutGeometry.For(project);
        var weights = project.Optimizer;
        double transfer = Math.Max(0, project.Layout.TransferDurationSeconds);
        var shuttles = project.Shuttles.OrderBy(shuttle => shuttle.Id).ToList();
        var load = new Dictionary<ShuttleId, double>();
        foreach (var shuttle in shuttles)
        {
            load.TryAdd(shuttle.Id, 0);
        }

        var byArticle = assignments
            .Where(slot => !string.IsNullOrEmpty(slot.ArticleId))
            .GroupBy(slot => slot.ArticleId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        double travelHours = 0;
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

                var servers = geometry.ServersOf(slot.Segment);
                if (servers.Count == 0)
                {
                    penalty += 5;
                    continue;
                }

                ShuttleId nearest = servers[0];
                double hop = double.PositiveInfinity;
                foreach (var id in servers.OrderBy(id => id))
                {
                    var shuttle = shuttles.FirstOrDefault(candidate => candidate.Id == id);
                    if (shuttle is null)
                    {
                        continue;
                    }

                    double candidate = Hop(geometry, shuttle, slot);
                    if (candidate < hop)
                    {
                        hop = candidate;
                        nearest = id;
                    }
                }

                if (double.IsInfinity(hop))
                {
                    penalty += 5;
                    continue;
                }

                if (servers.Count > 1)
                {
                    middlePulls += pullsPerShaft;
                }

                double hours = pullsPerShaft * (hop + transfer) / 3600.0;
                travelHours += hours;
                totalPulls += pullsPerShaft;
                load.TryGetValue(nearest, out double current);
                load[nearest] = current + hours;
            }
        }

        double sum = load.Values.Sum();
        double max = load.Count == 0 ? 0 : load.Values.Max();
        double min = load.Count == 0 ? 0 : load.Values.Min();
        double imbalance = (max - min) / Math.Max(sum, 1e-6);
        double middle = totalPulls <= 1e-9 ? 0 : middlePulls / totalPulls;
        double total = weights.TravelWeight * travelHours
            + weights.ImbalanceWeight * imbalance
            + weights.MiddleTrafficWeight * middle
            + weights.PenaltyWeight * penalty;
        load.TryGetValue(ShuttleId.Shuttle1, out double load1);
        load.TryGetValue(ShuttleId.Shuttle2, out double load2);

        return new CostBreakdown
        {
            Total = total,
            Travel = travelHours,
            Imbalance = imbalance,
            MiddleTraffic = middle,
            Penalty = penalty,
            Shuttle1LoadHours = load1,
            Shuttle2LoadHours = load2,
            LoadHours = load
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
