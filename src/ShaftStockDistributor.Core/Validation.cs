namespace ShaftStockDistributor.Core;

public enum ValidationSeverity
{
    Error,
    Warning
}

public sealed record ValidationMessage(ValidationSeverity Severity, string Text)
{
    public bool IsError => Severity == ValidationSeverity.Error;
}

public static class WarehouseValidator
{
    public static IReadOnlyList<ValidationMessage> Validate(Layout layout, ArticleSet articles, Assignment assignment)
    {
        var messages = new List<ValidationMessage>();

        if (layout.LevelsPerSegment > Layout.MaxLevelsPerSegment)
        {
            messages.Add(Error($"A segment has at most {Layout.MaxLevelsPerSegment} levels (configured {layout.LevelsPerSegment})."));
        }

        if (layout.ShaftsPerLevel > Layout.MaxShaftsPerLevel)
        {
            messages.Add(Error($"A level has at most {Layout.MaxShaftsPerLevel} shafts (configured {layout.ShaftsPerLevel})."));
        }

        if (layout.DefaultShaftCapacity < 1)
        {
            messages.Add(Error("Default shaft capacity must be at least 1 crate."));
        }

        if (layout.ShaftPitchMeters <= 0 || layout.LevelHeightMeters <= 0)
        {
            messages.Add(Error("Shaft pitch and level height must be positive."));
        }

        if (layout.SafetyDistanceSegments < 0)
        {
            messages.Add(Error("Safety distance cannot be negative."));
        }

        var kinematics = layout.Kinematics;
        if (kinematics.HorizontalSpeedMetersPerSecond <= 0 || kinematics.VerticalSpeedMetersPerSecond <= 0)
        {
            messages.Add(Error("Shuttle speeds must be positive."));
        }

        if (kinematics.HorizontalAccelerationMetersPerSecondSquared <= 0
            || kinematics.VerticalAccelerationMetersPerSecondSquared <= 0)
        {
            messages.Add(Error("Shuttle accelerations must be positive."));
        }

        if (kinematics.TransferDurationSeconds <= 0)
        {
            messages.Add(Error("Pull transfer duration must be positive."));
        }

        if (!layout.Contains(layout.Shuttle1Home))
        {
            messages.Add(Error("Shuttle 1 home is outside the layout."));
        }

        if (!layout.Contains(layout.Shuttle2Home))
        {
            messages.Add(Error("Shuttle 2 home is outside the layout."));
        }

        if (Math.Abs(layout.Shuttle1Home.Segment - layout.Shuttle2Home.Segment) <= layout.SafetyDistanceSegments)
        {
            messages.Add(Error("Shuttle homes are not separated by more than the safety distance."));
        }

        if (layout.Zones.Shuttle1OnlyCount <= layout.SafetyDistanceSegments
            || layout.Zones.Shuttle2OnlyCount <= layout.SafetyDistanceSegments)
        {
            messages.Add(Error(
                "Each exclusive zone must be wider than the safety distance so a shuttle can wait outside the shared keep-out. "
                + $"Exclusive widths are {layout.Zones.Shuttle1OnlyCount} and {layout.Zones.Shuttle2OnlyCount}, safety is {layout.SafetyDistanceSegments}."));
        }

        if (layout.Shuttle1Home.Segment >= layout.Zones.SharedStart
            || layout.Shuttle2Home.Segment < layout.Zones.Shuttle2Start)
        {
            messages.Add(Warning("A shuttle home is not in its own exclusive zone. Collision avoidance still applies, but handover is no longer at the outer end."));
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var article in articles.Items)
        {
            if (string.IsNullOrWhiteSpace(article.Id))
            {
                messages.Add(Error("An article has an empty id."));
                continue;
            }

            if (!seenIds.Add(article.Id))
            {
                messages.Add(Error($"Duplicate article id '{article.Id}'."));
            }

            if (article.CratesPerPallet < 1)
            {
                messages.Add(Error($"Article '{article.Id}' must have at least 1 crate per pallet."));
            }

            if (article.Offset < 0)
            {
                messages.Add(Error($"Article '{article.Id}' offset cannot be negative."));
            }

            if (article.CratesPerHour is < 0)
            {
                messages.Add(Error($"Article '{article.Id}' inflow cannot be negative."));
            }

            if (article.ShaftCapacity is < 1)
            {
                messages.Add(Error($"Article '{article.Id}' shaft capacity must be at least 1."));
            }

            var capacity = article.ShaftCapacity ?? layout.DefaultShaftCapacity;
            if (article.CratesPerPallet >= 1 && article.Offset >= 0 && capacity < article.PullThreshold)
            {
                messages.Add(Error(
                    $"Article '{article.Id}' shaft capacity {capacity} is below the pull threshold {article.PullThreshold} (pallet + offset)."));
            }
        }

        var shareSum = articles.AbcDefaults.ShareA + articles.AbcDefaults.ShareB + articles.AbcDefaults.ShareC;
        if (shareSum is < 0.99 or > 1.01)
        {
            messages.Add(Warning($"ABC shares sum to {shareSum:0.###}; they are expected to sum to 1."));
        }

        if (articles.AbcDefaults.TotalCratesPerHour < 0)
        {
            messages.Add(Error("ABC default total inflow cannot be negative."));
        }

        foreach (var (location, assigned) in assignment.Shafts)
        {
            var shaft = layout.Find(location);
            if (shaft is null)
            {
                messages.Add(Error($"Assignment {location} is not a shaft in the layout."));
                continue;
            }

            var article = articles.Find(assigned.ArticleId);
            if (article is null)
            {
                messages.Add(Error($"Shaft {location} is assigned to unknown article '{assigned.ArticleId}'."));
                continue;
            }

            var capacity = layout.EffectiveCapacity(shaft, article);
            if (capacity < article.PullThreshold)
            {
                messages.Add(Error(
                    $"Shaft {location} capacity {capacity} is below the pull threshold {article.PullThreshold} of '{article.Id}'."));
            }
        }

        foreach (var article in articles.Items)
        {
            if (string.IsNullOrWhiteSpace(article.Id))
            {
                continue;
            }

            if (assignment.CountFor(article.Id) < 1)
            {
                messages.Add(Error($"Article '{article.Id}' has no shaft."));
            }
        }

        foreach (var (location, capacity) in layout.CapacityOverrides)
        {
            if (layout.Find(location) is null)
            {
                messages.Add(Error($"Capacity override {location} is not a shaft in the layout."));
            }
            else if (capacity < 1)
            {
                messages.Add(Error($"Capacity override {location} must be at least 1."));
            }
        }

        return messages;
    }

    public static bool HasErrors(IReadOnlyList<ValidationMessage> messages) =>
        messages.Any(message => message.IsError);

    private static ValidationMessage Error(string text) => new(ValidationSeverity.Error, text);

    private static ValidationMessage Warning(string text) => new(ValidationSeverity.Warning, text);
}
