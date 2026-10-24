namespace ShaftStockDistributor.Core;

public static class ProjectValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(DistributorProject project)
    {
        var issues = new List<ValidationIssue>();
        var layout = project.Layout;

        ErrorIf(issues, layout.SegmentCount < 1, ValidationCodes.SegmentCount);
        ErrorIf(issues, layout.LevelsPerSegment < 1, ValidationCodes.LevelCount);
        ErrorIf(issues, layout.ShaftsPerLevel < 1, ValidationCodes.ShaftCount);
        WarnIf(issues, layout.LevelsPerSegment > 5, ValidationCodes.LevelAboveTypical);
        WarnIf(issues, layout.ShaftsPerLevel > 9, ValidationCodes.ShaftAboveTypical);
        ErrorIf(issues, layout.ShaftLengthMeters <= 0, ValidationCodes.ShaftLength);
        ErrorIf(issues, layout.CrateGapMeters < 0, ValidationCodes.CrateGap);
        ErrorIf(issues, layout.ShaftPitchMeters <= 0, ValidationCodes.ShaftPitch);
        ErrorIf(issues, layout.LevelHeightMeters <= 0, ValidationCodes.LevelHeight);
        ErrorIf(issues, layout.TransferDurationSeconds < 0, ValidationCodes.TransferDuration);
        ErrorIf(issues, layout.SafetyDistanceMeters < 0, ValidationCodes.SafetyDistance);

        if (project.Shuttles.All(s => s.Id != ShuttleId.Shuttle1) || project.Shuttles.All(s => s.Id != ShuttleId.Shuttle2))
        {
            issues.Add(Make(ValidationSeverity.Error, ValidationCodes.ShuttleMissing));
        }

        foreach (var shuttle in project.Shuttles)
        {
            string name = shuttle.Id == ShuttleId.Shuttle1 ? "Shuttle 1" : "Shuttle 2";
            CheckSpeed(issues, name, "horizontal", shuttle.HorizontalSpeedMetersPerSecond);
            CheckSpeed(issues, name, "vertical", shuttle.VerticalSpeedMetersPerSecond);
            CheckAccel(issues, name, "horizontal", shuttle.HorizontalAccelerationMetersPerSecondSquared);
            CheckAccel(issues, name, "vertical", shuttle.VerticalAccelerationMetersPerSecondSquared);
            CheckLevel(issues, name, "home", shuttle.Home.Level, layout.LevelsPerSegment);
            CheckLevel(issues, name, "handover", shuttle.Handover.Level, layout.LevelsPerSegment);
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var article in project.Articles)
        {
            if (string.IsNullOrWhiteSpace(article.Id))
            {
                issues.Add(Make(ValidationSeverity.Error, ValidationCodes.ArticleId));
            }
            else if (!seenIds.Add(article.Id))
            {
                issues.Add(Make(ValidationSeverity.Error, ValidationCodes.DuplicateArticleId, article.Id));
            }

            if (string.IsNullOrWhiteSpace(article.Name))
            {
                issues.Add(Make(ValidationSeverity.Error, ValidationCodes.ArticleName, article.Id));
            }

            CheckDimension(issues, article, "length", article.Dimensions.LengthMillimeters);
            CheckDimension(issues, article, "width", article.Dimensions.WidthMillimeters);
            CheckDimension(issues, article, "height", article.Dimensions.HeightMillimeters);
            ErrorIf(issues, article.CratesPerPallet < 1, ValidationCodes.CratesPerPallet, article.Name);
            ErrorIf(issues, article.Offset < 0, ValidationCodes.Offset, article.Name);
            ErrorIf(issues, article.InflowCratesPerHour < 0, ValidationCodes.Inflow, article.Name);

            if (article.Dimensions.FlowLengthMeters > 0 && layout.ShaftLengthMeters > 0 && article.CratesPerPallet >= 1 && article.Offset >= 0)
            {
                int capacity = new LayoutGeometry(layout).CapacityFor(article);
                int trigger = article.PullTriggerCrates;
                if (capacity < trigger)
                {
                    issues.Add(Make(
                        ValidationSeverity.Warning,
                        ValidationCodes.PullNeverTriggers,
                        article.Name,
                        capacity.ToString(),
                        trigger.ToString(),
                        article.CratesPerPallet.ToString(),
                        article.Offset.ToString()));
                }
            }

            if (layout.ShaftWidthMeters is double width && layout.ShaftClearHeightMeters is double height && width > 0 && height > 0)
            {
                var (first, second) = article.Dimensions.CrossSectionMillimeters;
                double a = first / 1000.0;
                double b = second / 1000.0;
                bool fits = (a <= width + 1e-9 && b <= height + 1e-9) || (a <= height + 1e-9 && b <= width + 1e-9);
                if (!fits)
                {
                    issues.Add(Make(
                        ValidationSeverity.Warning,
                        ValidationCodes.CrossSection,
                        article.Name,
                        width.ToString("0.###"),
                        height.ToString("0.###")));
                }
            }
        }

        var addresses = new HashSet<ShaftAddress>();
        foreach (var slot in project.Assignments)
        {
            var address = slot.Address;
            if (!addresses.Add(address))
            {
                issues.Add(Make(ValidationSeverity.Error, ValidationCodes.DuplicateAddress, address.ToString()));
            }

            if (!string.IsNullOrEmpty(slot.ArticleId) && project.FindArticle(slot.ArticleId) is null)
            {
                issues.Add(Make(ValidationSeverity.Error, ValidationCodes.UnknownArticle, address.ToString(), slot.ArticleId));
            }
        }

        foreach (var scenario in project.Scenarios)
        {
            if (string.IsNullOrWhiteSpace(scenario.Name))
            {
                issues.Add(Make(ValidationSeverity.Error, ValidationCodes.ScenarioName));
            }

            ErrorIf(issues, scenario.TotalCratesPerHour < 0, ValidationCodes.ScenarioTotal, scenario.Name);
            double share = scenario.ShareA + scenario.ShareB + scenario.ShareC;
            if (Math.Abs(share - 1.0) > 0.011)
            {
                issues.Add(Make(ValidationSeverity.Warning, ValidationCodes.ScenarioShares, scenario.Name, share.ToString("0.###")));
            }
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<ValidationIssue> issues) =>
        issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    private static void CheckSpeed(List<ValidationIssue> issues, string shuttle, string axis, double speed) =>
        ErrorIf(issues, speed <= 0, ValidationCodes.ShuttleSpeed, shuttle, axis);

    private static void CheckAccel(List<ValidationIssue> issues, string shuttle, string axis, double acceleration) =>
        ErrorIf(issues, acceleration < 0, ValidationCodes.ShuttleAcceleration, shuttle, axis);

    private static void CheckLevel(List<ValidationIssue> issues, string shuttle, string pose, int level, int levels) =>
        ErrorIf(issues, levels >= 1 && (level < 1 || level > levels), ValidationCodes.ShuttleHomeLevel, shuttle, pose, levels.ToString());

    private static void CheckDimension(List<ValidationIssue> issues, Article article, string axis, double millimeters) =>
        ErrorIf(issues, millimeters <= 0, ValidationCodes.CrateDimension, article.Name, axis);

    private static void ErrorIf(List<ValidationIssue> issues, bool when, string code, params string[] args)
    {
        if (when)
        {
            issues.Add(Make(ValidationSeverity.Error, code, args));
        }
    }

    private static void WarnIf(List<ValidationIssue> issues, bool when, string code, params string[] args)
    {
        if (when)
        {
            issues.Add(Make(ValidationSeverity.Warning, code, args));
        }
    }

    private static ValidationIssue Make(ValidationSeverity severity, string code, params string[] args)
    {
        var issue = new ValidationIssue(severity, code, "", args);
        return issue with { Message = ValidationText.Format(issue) };
    }
}
