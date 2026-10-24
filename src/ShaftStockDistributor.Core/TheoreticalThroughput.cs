namespace ShaftStockDistributor.Core;

/// <summary>
/// Upper bound on pallet throughput for the aisle.
/// Each shuttle repeats the mean shaft-to-shaft trip inside the zone it may serve
/// (the next pull starts where the last one finished, not at home), then spends the
/// transfer. Discharge uses the fullest pull that can actually trigger. Shared-zone
/// waiting is ignored, so a real aisle should stay under this line.
/// </summary>
public static class TheoreticalThroughput
{
    public static double PalletsPerHour(DistributorProject project)
    {
        if (project.Shuttles.Count == 0)
        {
            return 0;
        }

        var geometry = new LayoutGeometry(project.Layout);
        if (geometry.ShaftCount == 0)
        {
            return 0;
        }

        double palletsPerPull = MaxPalletsPerPull(project, geometry);
        double total = 0;
        foreach (var shuttle in project.Shuttles)
        {
            double seconds = MeanCycleSeconds(geometry, shuttle, project.Layout.TransferDurationSeconds);
            if (seconds <= 1e-6 || double.IsInfinity(seconds))
            {
                continue;
            }

            total += 3600.0 / seconds * palletsPerPull;
        }

        return total;
    }

    private static double MaxPalletsPerPull(DistributorProject project, LayoutGeometry geometry)
    {
        double max = 0;
        foreach (var article in project.Articles)
        {
            if (article.CratesPerPallet <= 0)
            {
                continue;
            }

            if (article.ClassWeight <= 0 && article.InflowCratesPerHour <= 0)
            {
                continue;
            }

            if (geometry.CapacityFor(article) < article.PullTriggerCrates)
            {
                continue;
            }

            double ratio = article.PullTriggerCrates / (double)article.CratesPerPallet;
            if (ratio > max)
            {
                max = ratio;
            }
        }

        return max <= 0 ? 1 : max;
    }

    private static double MeanCycleSeconds(LayoutGeometry geometry, ShuttleSettings shuttle, double transferSeconds)
    {
        var points = new List<(double X, double Y)>();
        for (int segment = 1; segment <= geometry.SegmentCount; segment++)
        {
            var zone = geometry.ZoneOfSegment(segment);
            if (!geometry.ShuttleCanServe(shuttle.Id, zone))
            {
                continue;
            }

            for (int level = 1; level <= geometry.Levels; level++)
            {
                for (int shaft = 1; shaft <= geometry.ShaftsPerLevel; shaft++)
                {
                    points.Add((geometry.ShaftX(segment, shaft), geometry.LevelY(level)));
                }
            }
        }

        if (points.Count == 0)
        {
            return 0;
        }

        if (points.Count == 1)
        {
            return Math.Max(0, transferSeconds);
        }

        double sum = 0;
        int pairs = 0;
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
            {
                sum += TravelTime.Simultaneous(
                    points[j].X - points[i].X,
                    points[j].Y - points[i].Y,
                    shuttle);
                pairs++;
            }
        }

        double travel = pairs == 0 ? 0 : sum / pairs;
        return travel + Math.Max(0, transferSeconds);
    }
}
