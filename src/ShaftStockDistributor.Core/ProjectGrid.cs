namespace ShaftStockDistributor.Core;

public static class ProjectGrid
{
    public static void EnsureFullGrid(DistributorProject project)
    {
        var geometry = new LayoutGeometry(project.Layout);
        var existing = project.Assignments.ToDictionary(a => a.Address);
        var rebuilt = new List<ShaftAssignment>(geometry.ShaftCount);
        foreach (var address in geometry.AllAddresses())
        {
            if (existing.TryGetValue(address, out var kept))
            {
                rebuilt.Add(new ShaftAssignment
                {
                    Segment = address.Segment,
                    Level = address.Level,
                    Shaft = address.Shaft,
                    ArticleId = kept.ArticleId,
                    Pinned = kept.Pinned
                });
            }
            else
            {
                rebuilt.Add(new ShaftAssignment
                {
                    Segment = address.Segment,
                    Level = address.Level,
                    Shaft = address.Shaft
                });
            }
        }

        project.Assignments = rebuilt;
    }

    public static List<ShaftAssignment> Materialize(DistributorProject project)
    {
        var geometry = new LayoutGeometry(project.Layout);
        var existing = new Dictionary<ShaftAddress, ShaftAssignment>();
        foreach (var slot in project.Assignments)
        {
            existing[slot.Address] = slot;
        }

        var list = new List<ShaftAssignment>(geometry.ShaftCount);
        foreach (var address in geometry.AllAddresses())
        {
            if (existing.TryGetValue(address, out var kept))
            {
                list.Add(new ShaftAssignment
                {
                    Segment = address.Segment,
                    Level = address.Level,
                    Shaft = address.Shaft,
                    ArticleId = kept.ArticleId,
                    Pinned = kept.Pinned
                });
            }
            else
            {
                list.Add(new ShaftAssignment
                {
                    Segment = address.Segment,
                    Level = address.Level,
                    Shaft = address.Shaft
                });
            }
        }

        return list;
    }

    public static void ReplaceAssignments(DistributorProject project, IReadOnlyList<ShaftAssignment> assignments)
    {
        project.Assignments = assignments.Select(slot => new ShaftAssignment
        {
            Segment = slot.Segment,
            Level = slot.Level,
            Shaft = slot.Shaft,
            ArticleId = slot.ArticleId,
            Pinned = slot.Pinned
        }).ToList();
    }

    public static void EnsureDefaultShuttles(DistributorProject project) => ShuttleLayout.Ensure(project);

    public static ShuttleSettings DefaultShuttle(ShuttleId id, double homeX) => new()
    {
        Id = id,
        HorizontalSpeedMetersPerSecond = 0.3,
        VerticalSpeedMetersPerSecond = 0.3,
        HorizontalAccelerationMetersPerSecondSquared = 0.2,
        VerticalAccelerationMetersPerSecondSquared = 0.2,
        Home = new ShuttlePose { XMeters = homeX, Level = 1 },
        Handover = new ShuttlePose { XMeters = homeX, Level = 1 }
    };

    public static string ToAssignmentCsv(DistributorProject project)
    {
        var zoned = LayoutGeometry.For(project);
        var lines = new List<string> { "Segment,Level,Shaft,Zone,ArticleId,ArticleName,Pinned" };
        foreach (var slot in project.Assignments.OrderBy(a => a.Address))
        {
            var article = project.FindArticle(slot.ArticleId);
            string zone = slot.Segment >= 1 && slot.Segment <= zoned.SegmentCount
                ? zoned.ZoneLabel(slot.Segment)
                : "";
            lines.Add(string.Join(',',
                slot.Segment.ToString(),
                slot.Level.ToString(),
                slot.Shaft.ToString(),
                zone,
                Csv(slot.ArticleId),
                Csv(article?.Name),
                slot.Pinned ? "true" : "false"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string ToAssignmentJson(DistributorProject project) =>
        ProjectSerializer.SerializeAssignments(project);

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
