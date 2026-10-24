namespace ShaftStockDistributor.Core;

/// <summary>
/// Conservative pallet ceiling. Each shuttle repeats a trip from its home to the
/// average shaft it may serve, then spends the configured transfer time.
/// One pallet is counted per pull. Shaft-to-shaft trips are often shorter, and a
/// pull can discharge more than one pallet, so measured throughput can sit above this line.
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

        double total = 0;
        foreach (var shuttle in project.Shuttles)
        {
            double seconds = MeanCycleSeconds(geometry, shuttle, project.Layout.TransferDurationSeconds);
            if (seconds <= 1e-6)
            {
                continue;
            }

            total += 3600.0 / seconds;
        }

        return total;
    }

    private static double MeanCycleSeconds(LayoutGeometry geometry, ShuttleSettings shuttle, double transferSeconds)
    {
        int homeLevel = Math.Clamp(shuttle.Home.Level, 1, Math.Max(1, geometry.Levels));
        double homeX = shuttle.Home.XMeters;
        double homeY = geometry.LevelY(homeLevel);
        double sum = 0;
        int count = 0;
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
                    double x = geometry.ShaftX(segment, shaft);
                    double y = geometry.LevelY(level);
                    sum += TravelTime.Simultaneous(x - homeX, y - homeY, shuttle);
                    count++;
                }
            }
        }

        double travel = count == 0 ? 0 : sum / count;
        return travel + Math.Max(0, transferSeconds);
    }
}
