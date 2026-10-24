using ShaftStockDistributor.Core;
using ShaftStockDistributor.Operations;

namespace ShaftStockDistributor.Tests;

public static class TestWarehouses
{
    public static DistributorProject BalancedPlanningCase()
    {
        var project = new DistributorProject
        {
            Name = "Planning fixture",
            AssignmentMode = AssignmentMode.Manual,
            Layout = new LayoutSettings
            {
                SegmentCount = 6,
                LevelsPerSegment = 2,
                ShaftsPerLevel = 2,
                ShaftLengthMeters = 20,
                ShaftPitchMeters = 0.5,
                LevelHeightMeters = 0.6,
                SegmentGapMeters = 0.2,
                TransferDurationSeconds = 12,
                SafetyDistanceMeters = 1
            },
            Optimizer = new OptimizerSettings
            {
                ExpectedPullWaitSeconds = 90,
                Iterations = 200,
                Seed = 2,
                TravelWeight = 1,
                ImbalanceWeight = 2,
                MiddleTrafficWeight = 0.5,
                PenaltyWeight = 4
            }
        };
        project.Articles.Add(Crate("A1", AbcClass.A, 500));
        project.Articles.Add(Crate("A2", AbcClass.A, 500));
        project.Articles.Add(Crate("B1", AbcClass.B, 80));
        project.Articles.Add(Crate("C1", AbcClass.C, 40));
        ProjectGrid.EnsureDefaultShuttles(project);
        ProjectGrid.EnsureFullGrid(project);
        return project;
    }

    public static DistributorProject SteadySmall()
    {
        var project = new DistributorProject
        {
            Name = "Steady fixture",
            AssignmentMode = AssignmentMode.Manual,
            Layout = new LayoutSettings
            {
                SegmentCount = 6,
                LevelsPerSegment = 2,
                ShaftsPerLevel = 2,
                ShaftLengthMeters = 20,
                ShaftPitchMeters = 0.8,
                LevelHeightMeters = 0.5,
                SegmentGapMeters = 0.2,
                TransferDurationSeconds = 8,
                SafetyDistanceMeters = 1.0
            },
            Operation = new OperationSettings { Hysteresis = 0.5, ReassignmentCooldownSeconds = 300 }
        };
        project.Articles.Add(Crate("FAST", AbcClass.A, 240, 20, 2));
        project.Articles.Add(Crate("SLOW", AbcClass.C, 40, 20, 2));
        ProjectGrid.EnsureDefaultShuttles(project);
        foreach (var shuttle in project.Shuttles)
        {
            shuttle.HorizontalSpeedMetersPerSecond = 0.6;
            shuttle.VerticalSpeedMetersPerSecond = 0.6;
            shuttle.HorizontalAccelerationMetersPerSecondSquared = 0;
            shuttle.VerticalAccelerationMetersPerSecondSquared = 0;
        }

        ProjectGrid.EnsureFullGrid(project);
        Assign(project, 1, 1, 1, "FAST");
        Assign(project, 2, 1, 1, "FAST");
        Assign(project, 5, 1, 1, "SLOW");
        Assign(project, 6, 1, 1, "SLOW");
        return project;
    }

    public static DistributorProject Contention()
    {
        var project = new DistributorProject
        {
            Name = "Contention fixture",
            AssignmentMode = AssignmentMode.Manual,
            Layout = new LayoutSettings
            {
                SegmentCount = 6,
                LevelsPerSegment = 1,
                ShaftsPerLevel = 1,
                ShaftLengthMeters = 8,
                ShaftPitchMeters = 1,
                LevelHeightMeters = 0.5,
                SegmentGapMeters = 0,
                TransferDurationSeconds = 6,
                SafetyDistanceMeters = 1.2
            }
        };
        project.Articles.Add(Crate("LEFT", AbcClass.A, 3000, 4, 1, 500));
        project.Articles.Add(Crate("RIGHT", AbcClass.A, 3000, 4, 1, 500));
        ProjectGrid.EnsureDefaultShuttles(project);
        foreach (var shuttle in project.Shuttles)
        {
            shuttle.HorizontalSpeedMetersPerSecond = 0.4;
            shuttle.VerticalSpeedMetersPerSecond = 0.4;
            shuttle.HorizontalAccelerationMetersPerSecondSquared = 0;
            shuttle.VerticalAccelerationMetersPerSecondSquared = 0;
        }

        ProjectGrid.EnsureFullGrid(project);
        Assign(project, 3, 1, 1, "LEFT");
        Assign(project, 4, 1, 1, "RIGHT");
        return project;
    }

    public static DistributorProject TwoShaftWarehouse()
    {
        var project = new DistributorProject
        {
            Layout = new LayoutSettings
            {
                SegmentCount = 3,
                LevelsPerSegment = 1,
                ShaftsPerLevel = 1,
                ShaftLengthMeters = 20,
                TransferDurationSeconds = 5,
                SafetyDistanceMeters = 1
            }
        };
        project.Articles.Add(Crate("KEEP", AbcClass.B, 100, 10, 2));
        project.Articles.Add(Crate("OTHER", AbcClass.A, 20, 10, 2));
        ProjectGrid.EnsureDefaultShuttles(project);
        ProjectGrid.EnsureFullGrid(project);
        Assign(project, 1, 1, 1, "KEEP");
        Assign(project, 3, 1, 1, "OTHER");
        return project;
    }

    public static DistributorProject EasyToSaturate()
    {
        var project = new DistributorProject
        {
            Name = "Saturation fixture",
            AssignmentMode = AssignmentMode.Manual,
            Layout = new LayoutSettings
            {
                SegmentCount = 3,
                LevelsPerSegment = 1,
                ShaftsPerLevel = 1,
                ShaftLengthMeters = 2,
                ShaftPitchMeters = 1,
                TransferDurationSeconds = 20,
                SafetyDistanceMeters = 0.5
            }
        };
        project.Articles.Add(Crate("ONLY", AbcClass.A, 20, 2, 1, 500));
        project.Scenarios.Add(new InflowScenario
        {
            Name = "Only article",
            TotalCratesPerHour = 20,
            ShareA = 1,
            ShareB = 0,
            ShareC = 0
        });
        ProjectGrid.EnsureDefaultShuttles(project);
        foreach (var shuttle in project.Shuttles)
        {
            shuttle.HorizontalSpeedMetersPerSecond = 0.05;
            shuttle.VerticalSpeedMetersPerSecond = 0.05;
            shuttle.HorizontalAccelerationMetersPerSecondSquared = 0;
            shuttle.VerticalAccelerationMetersPerSecondSquared = 0;
        }

        ProjectGrid.EnsureFullGrid(project);
        Assign(project, 2, 1, 1, "ONLY");
        InflowAllocator.Apply(project, project.Scenarios[0]);
        return project;
    }

    public static (List<ShaftRuntime> Shafts, Dictionary<string, ArticleRuntime> Articles) PolicyFixture()
    {
        var shafts = new List<ShaftRuntime>
        {
            new()
            {
                Address = new ShaftAddress(1, 1, 1),
                Zone = ZoneKind.Shuttle1Only,
                X = 0.5,
                Y = 0,
                ArticleId = "SLOW",
                Capacity = 30,
                Count = 2
            },
            new()
            {
                Address = new ShaftAddress(2, 1, 1),
                Zone = ZoneKind.Shared,
                X = 2,
                Y = 0,
                ArticleId = "SLOW",
                Capacity = 30,
                Count = 0
            }
        };
        var articles = new Dictionary<string, ArticleRuntime>(StringComparer.Ordinal)
        {
            ["SLOW"] = new ArticleRuntime { Article = Crate("SLOW", AbcClass.C, 10) },
            ["FAST"] = new ArticleRuntime { Article = Crate("FAST", AbcClass.A, 400) }
        };
        return (shafts, articles);
    }

    private static Article Crate(string id, AbcClass abc, double inflow, int pallet = 40, int offset = 5, double lengthMm = 400) => new()
    {
        Id = id,
        Name = id,
        Class = abc,
        InflowCratesPerHour = inflow,
        CratesPerPallet = pallet,
        Offset = offset,
        Dimensions = new CrateDimensions { LengthMillimeters = lengthMm, WidthMillimeters = 300, HeightMillimeters = 280 }
    };

    private static void Assign(DistributorProject project, int segment, int level, int shaft, string articleId)
    {
        var slot = project.Assignments.Single(item => item.Segment == segment && item.Level == level && item.Shaft == shaft);
        slot.ArticleId = articleId;
    }
}
