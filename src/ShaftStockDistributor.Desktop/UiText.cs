using System.Resources;

namespace ShaftStockDistributor.Desktop;

/// <summary>UI copy loaded from Resources/Strings.resx so another language can be added later.</summary>
public static class UiText
{
    private static readonly ResourceManager Resources = new(
        "ShaftStockDistributor.Desktop.Resources.Strings",
        typeof(UiText).Assembly);

    public static string AppTitle => S();
    public static string Tagline => S();
    public static string ExampleBadge => S();
    public static string NavLive => S();
    public static string NavLayout => S();
    public static string NavArticles => S();
    public static string NavOptimizer => S();
    public static string NavStudy => S();
    public static string NavOperation => S();
    public static string New => S();
    public static string Open => S();
    public static string Save => S();
    public static string ExportJson => S();
    public static string ExportCsv => S();
    public static string StartDemo => S();
    public static string ModeAutomatic => S();
    public static string ModeManual => S();
    public static string ModeConfirmTitle => S();
    public static string ModeConfirmBody => S();
    public static string Confirm => S();
    public static string Cancel => S();
    public static string Play => S();
    public static string Pause => S();
    public static string Step => S();
    public static string Reset => S();
    public static string Speed => S();
    public static string Seed => S();
    public static string Scenario => S();
    public static string Heatmap => S();
    public static string PalletsPerHour => S();
    public static string TotalPallets => S();
    public static string Offered => S();
    public static string Utilization => S();
    public static string Shuttle1 => S();
    public static string Shuttle2 => S();
    public static string AverageWait => S();
    public static string Blocked => S();
    public static string LostCrates => S();
    public static string Overflowing => S();
    public static string Pulls => S();
    public static string ClickShaft => S();
    public static string ApplyAssignment => S();
    public static string Pin => S();
    public static string Unpin => S();
    public static string SharedLock => S();
    public static string Waiting => S();
    public static string LayoutTitle => S();
    public static string ArticlesTitle => S();
    public static string OptimizerTitle => S();
    public static string StudyTitle => S();
    public static string OperationTitle => S();
    public static string ShaftLength => S();
    public static string CrateGap => S();
    public static string Segments => S();
    public static string Levels => S();
    public static string ShaftsPerLevel => S();
    public static string Pitch => S();
    public static string LevelHeight => S();
    public static string SegmentGap => S();
    public static string ShaftWidth => S();
    public static string ShaftHeight => S();
    public static string Transfer => S();
    public static string Safety => S();
    public static string HorizontalSpeed => S();
    public static string VerticalSpeed => S();
    public static string HorizontalAcceleration => S();
    public static string VerticalAcceleration => S();
    public static string HomeX => S();
    public static string HomeLevel => S();
    public static string HandoverX => S();
    public static string HandoverLevel => S();
    public static string Apply => S();
    public static string AddArticle => S();
    public static string RemoveArticle => S();
    public static string ArticleId => S();
    public static string ArticleName => S();
    public static string ClassLabel => S();
    public static string Length => S();
    public static string Width => S();
    public static string Height => S();
    public static string FlowAxis => S();
    public static string CratesPerPallet => S();
    public static string Offset => S();
    public static string Weight => S();
    public static string Inflow => S();
    public static string Color => S();
    public static string RunOptimizer => S();
    public static string Iterations => S();
    public static string ExpectedWait => S();
    public static string CostBefore => S();
    public static string CostNaive => S();
    public static string CostAfter => S();
    public static string ChangedShafts => S();
    public static string RunStudy => S();
    public static string MinLoad => S();
    public static string MaxLoad => S();
    public static string StepLoad => S();
    public static string Replications => S();
    public static string Duration => S();
    public static string Warmup => S();
    public static string Reoptimize => S();
    public static string CompareModes => S();
    public static string AchievedRatio => S();
    public static string LostRule => S();
    public static string UtilizationCeiling => S();
    public static string ExportResults => S();
    public static string Saturation => S();
    public static string Sustainable => S();
    public static string JobQueue => S();
    public static string EventLog => S();
    public static string ValidationTitle => S();
    public static string NoIssues => S();
    public static string StatusReady => S();
    public static string DemoHint => S();
    public static string ZoneShuttle1 => S();
    public static string ZoneShared => S();
    public static string ZoneShuttle2 => S();
    public static string ApplyLayout => S();
    public static string PlaybackReset => S();
    public static string CannotReassignFilled => S();
    public static string Saved => S();
    public static string Exported => S();
    public static string StudyExported => S();
    public static string OptimizerRunning => S();
    public static string StudyRunning => S();
    public static string Validation_NameRequired => S();
    public static string Validation_IdRequired => S();
    public static string Validation_Positive => S();
    public static string Validation_NonNegative => S();
    public static string ClearArticle => S();
    public static string SimTime => S();
    public static string Weights => S();
    public static string TravelWeight => S();
    public static string ImbalanceWeight => S();
    public static string MiddleWeight => S();
    public static string PenaltyWeight => S();
    public static string AddScenario => S();
    public static string RemoveScenario => S();
    public static string ShareA => S();
    public static string ShareB => S();
    public static string ShareC => S();
    public static string TotalInflow => S();
    public static string ScenarioName => S();
    public static string NotReached => S();
    public static string PalletsUnit => S();

    public static string FormatIssue(ShaftStockDistributor.Core.ValidationIssue issue)
    {
        string key = "Validation_" + issue.Code;
        string? template = Resources.GetString(key);
        if (string.IsNullOrEmpty(template))
        {
            return issue.Message;
        }

        try
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, issue.Arguments.ToArray());
        }
        catch (FormatException)
        {
            return issue.Message;
        }
    }

    private static string S([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => Resources.GetString(name) ?? name;
}
