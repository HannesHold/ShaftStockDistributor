namespace ShaftStockDistributor.Core;

public static class ValidationCodes
{
    public const string SegmentCount = "SegmentCount";
    public const string LevelCount = "LevelCount";
    public const string ShaftCount = "ShaftCount";
    public const string LevelAboveTypical = "LevelAboveTypical";
    public const string ShaftAboveTypical = "ShaftAboveTypical";
    public const string ShaftLength = "ShaftLength";
    public const string CrateGap = "CrateGap";
    public const string ShaftPitch = "ShaftPitch";
    public const string LevelHeight = "LevelHeight";
    public const string TransferDuration = "TransferDuration";
    public const string SafetyDistance = "SafetyDistance";
    public const string ShuttleSpeed = "ShuttleSpeed";
    public const string ShuttleAcceleration = "ShuttleAcceleration";
    public const string ShuttleHomeLevel = "ShuttleHomeLevel";
    public const string ShuttleMissing = "ShuttleMissing";
    public const string DuplicateArticleId = "DuplicateArticleId";
    public const string ArticleId = "ArticleId";
    public const string ArticleName = "ArticleName";
    public const string CrateDimension = "CrateDimension";
    public const string CratesPerPallet = "CratesPerPallet";
    public const string Offset = "Offset";
    public const string Inflow = "Inflow";
    public const string PullNeverTriggers = "PullNeverTriggers";
    public const string CrossSection = "CrossSection";
    public const string ScenarioShares = "ScenarioShares";
    public const string ScenarioTotal = "ScenarioTotal";
    public const string ScenarioName = "ScenarioName";
    public const string UnknownArticle = "UnknownArticle";
    public const string DuplicateAddress = "DuplicateAddress";
}

public static class ValidationText
{
    public static string Format(ValidationIssue issue)
    {
        string A(int index) => index < issue.Arguments.Count ? issue.Arguments[index] : "";
        return issue.Code switch
        {
            ValidationCodes.SegmentCount => "Segment count must be at least 1.",
            ValidationCodes.LevelCount => "Each segment needs at least 1 level.",
            ValidationCodes.ShaftCount => "Each level needs at least 1 shaft.",
            ValidationCodes.LevelAboveTypical => "This layout has more than 5 levels. The usual rack has at most 5.",
            ValidationCodes.ShaftAboveTypical => "This layout has more than 9 shafts on a level. The usual rack has at most 9.",
            ValidationCodes.ShaftLength => "Shaft length must be greater than zero.",
            ValidationCodes.CrateGap => "The gap between crates cannot be negative.",
            ValidationCodes.ShaftPitch => "Shaft pitch must be greater than zero.",
            ValidationCodes.LevelHeight => "Level height must be greater than zero.",
            ValidationCodes.TransferDuration => "Transfer duration cannot be negative.",
            ValidationCodes.SafetyDistance => "Safety distance cannot be negative.",
            ValidationCodes.ShuttleSpeed => $"{A(0)} needs a {A(1)} speed greater than zero.",
            ValidationCodes.ShuttleAcceleration => $"{A(0)} has a negative {A(1)} acceleration. Use zero for constant speed.",
            ValidationCodes.ShuttleHomeLevel => $"{A(0)} {A(1)} level must be between 1 and {A(2)}.",
            ValidationCodes.ShuttleMissing => "The project needs both shuttle 1 and shuttle 2.",
            ValidationCodes.DuplicateArticleId => $"Article id '{A(0)}' is used more than once.",
            ValidationCodes.ArticleId => "Every article needs an id.",
            ValidationCodes.ArticleName => $"Article '{A(0)}' needs a name.",
            ValidationCodes.CrateDimension => $"Article '{A(0)}' needs a {A(1)} greater than zero.",
            ValidationCodes.CratesPerPallet => $"Article '{A(0)}' needs at least 1 crate per pallet.",
            ValidationCodes.Offset => $"Article '{A(0)}' has a negative offset.",
            ValidationCodes.Inflow => $"Article '{A(0)}' has a negative inflow.",
            ValidationCodes.PullNeverTriggers =>
                $"Article '{A(0)}' never reaches a pull. One shaft holds {A(1)} crates, but a pull needs {A(2)} (pallet {A(3)} + offset {A(4)}).",
            ValidationCodes.CrossSection =>
                $"Article '{A(0)}' does not fit the shaft cross-section ({A(1)} m by {A(2)} m).",
            ValidationCodes.ScenarioShares => $"Scenario '{A(0)}' shares sum to {A(1)}, not 100%. They are scaled to 100% when the scenario is applied.",
            ValidationCodes.ScenarioTotal => $"Scenario '{A(0)}' has a negative total inflow.",
            ValidationCodes.ScenarioName => "Every scenario needs a name.",
            ValidationCodes.UnknownArticle => $"Shaft {A(0)} points at unknown article '{A(1)}'.",
            ValidationCodes.DuplicateAddress => $"Shaft {A(0)} is listed more than once.",
            _ => issue.Message
        };
    }
}
