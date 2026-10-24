namespace ShaftStockDistributor.Core;

/// <summary>
/// Hypothetical warehouse used for demos. Every name and rate is example data, not a customer measurement.
/// </summary>
public static class ExampleWarehouse
{
    public const string ProjectName = "EXAMPLE sorting warehouse";

    public static DistributorProject Create()
    {
        var project = new DistributorProject
        {
            Name = ProjectName,
            IsExampleData = true,
            AssignmentMode = AssignmentMode.Automatic,
            Layout = new LayoutSettings
            {
                SegmentCount = 12,
                LevelsPerSegment = 5,
                ShaftsPerLevel = 9,
                ShaftLengthMeters = 20,
                CrateGapMeters = 0,
                ShaftPitchMeters = 0.45,
                LevelHeightMeters = 0.55,
                SegmentGapMeters = 0.35,
                ShaftWidthMeters = 0.42,
                ShaftClearHeightMeters = 0.40,
                TransferDurationSeconds = 12,
                SafetyDistanceMeters = 1.5
            },
            Optimizer = new OptimizerSettings(),
            Operation = new OperationSettings(),
            Study = new StudySettings()
        };

        project.Articles.Add(Article("EX-A-HALF-LITER", "EXAMPLE half-liter bottle crate", AbcClass.A, 400, 300, 280, FlowAxis.Length, 40, 4, "#3D7EFF"));
        project.Articles.Add(Article("EX-A-THIRD-LITER", "EXAMPLE third-liter bottle crate", AbcClass.A, 370, 280, 240, FlowAxis.Length, 48, 6, "#00B3A4"));
        project.Articles.Add(Article("EX-A-WATER", "EXAMPLE mineral-water crate", AbcClass.A, 400, 300, 300, FlowAxis.Length, 36, 4, "#7C5CFF"));
        project.Articles.Add(Article("EX-B-JUICE", "EXAMPLE juice crate", AbcClass.B, 400, 300, 320, FlowAxis.Length, 32, 4, "#F0A202"));
        project.Articles.Add(Article("EX-B-SOFT", "EXAMPLE soft-drink crate", AbcClass.B, 300, 200, 250, FlowAxis.Length, 40, 5, "#E07A3D"));
        project.Articles.Add(Article("EX-B-WINE", "EXAMPLE wine-bottle crate", AbcClass.B, 430, 320, 350, FlowAxis.Length, 24, 3, "#C45C26"));
        project.Articles.Add(Article("EX-C-SPECIALTY", "EXAMPLE specialty crate", AbcClass.C, 450, 350, 380, FlowAxis.Length, 20, 2, "#8BD450"));
        project.Articles.Add(Article("EX-C-EXPORT", "EXAMPLE export crate", AbcClass.C, 480, 360, 390, FlowAxis.Length, 16, 2, "#5AA9E6"));
        project.Articles.Add(Article("EX-C-OVERSIZE", "EXAMPLE oversized crate", AbcClass.C, 900, 800, 800, FlowAxis.Length, 30, 5, "#E85D75"));

        project.Scenarios.Add(new InflowScenario
        {
            Name = "EXAMPLE balanced 70/20/10",
            TotalCratesPerHour = 1800,
            ShareA = 0.70,
            ShareB = 0.20,
            ShareC = 0.10
        });
        project.Scenarios.Add(new InflowScenario
        {
            Name = "EXAMPLE fast movers 85/10/5",
            TotalCratesPerHour = 1800,
            ShareA = 0.85,
            ShareB = 0.10,
            ShareC = 0.05
        });
        project.Scenarios.Add(new InflowScenario
        {
            Name = "EXAMPLE even mix 40/35/25",
            TotalCratesPerHour = 1800,
            ShareA = 0.40,
            ShareB = 0.35,
            ShareC = 0.25
        });

        MarkCatalogOnly(project, "EX-C-OVERSIZE");
        ProjectGrid.EnsureDefaultShuttles(project);
        ProjectGrid.EnsureFullGrid(project);
        InflowAllocator.Apply(project, project.Scenarios[0]);
        return project;
    }

    public static DistributorProject CreateBlank()
    {
        var project = new DistributorProject
        {
            Name = "New warehouse",
            AssignmentMode = AssignmentMode.Manual,
            Layout = new LayoutSettings
            {
                SegmentCount = 6,
                LevelsPerSegment = 3,
                ShaftsPerLevel = 4,
                ShaftLengthMeters = 20,
                ShaftWidthMeters = null,
                ShaftClearHeightMeters = null
            }
        };
        project.Scenarios.Add(new InflowScenario
        {
            Name = "Base mix",
            TotalCratesPerHour = 800,
            ShareA = 0.70,
            ShareB = 0.20,
            ShareC = 0.10
        });
        ProjectGrid.EnsureDefaultShuttles(project);
        ProjectGrid.EnsureFullGrid(project);
        return project;
    }

    private static Article Article(
        string id,
        string name,
        AbcClass abcClass,
        double length,
        double width,
        double height,
        FlowAxis flow,
        int cratesPerPallet,
        int offset,
        string color) => new()
    {
        Id = id,
        Name = name,
        Class = abcClass,
        Dimensions = new CrateDimensions
        {
            LengthMillimeters = length,
            WidthMillimeters = width,
            HeightMillimeters = height,
            FlowAxis = flow
        },
        CratesPerPallet = cratesPerPallet,
        Offset = offset,
        ClassWeight = 1,
        ColorHex = color
    };

    /// <summary>The oversized example stays in the catalog so validation can warn, but it receives no inflow.</summary>
    public static void MarkCatalogOnly(DistributorProject project, string articleId)
    {
        var article = project.FindArticle(articleId);
        if (article is not null)
        {
            article.ClassWeight = 0;
            article.InflowCratesPerHour = 0;
        }
    }
}
