using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaftStockDistributor.Core;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public static class WarehouseMapper
{
    public static Layout ToLayout(LayoutDocument document)
    {
        EnsureVersion(document.FormatVersion, "layout");
        var kinematics = new Kinematics
        {
            HorizontalSpeedMetersPerSecond = document.Kinematics.HorizontalSpeedMetersPerSecond,
            VerticalSpeedMetersPerSecond = document.Kinematics.VerticalSpeedMetersPerSecond,
            HorizontalAccelerationMetersPerSecondSquared = document.Kinematics.HorizontalAccelerationMetersPerSecondSquared,
            VerticalAccelerationMetersPerSecondSquared = document.Kinematics.VerticalAccelerationMetersPerSecondSquared,
            TransferDurationSeconds = document.Kinematics.TransferDurationSeconds
        };

        var home1 = ToPose(document.Shuttle1Home, segmentCount: document.SegmentCount, shafts: document.ShaftsPerLevel, shuttle: 1);
        var home2 = ToPose(document.Shuttle2Home, segmentCount: document.SegmentCount, shafts: document.ShaftsPerLevel, shuttle: 2);
        var overrides = new Dictionary<ShaftLocation, int>();
        foreach (var item in document.ShaftCapacityOverrides)
        {
            overrides[ShaftLocation.FromOneBased(item.Segment, item.Level, item.Shaft)] = item.Capacity;
        }

        return new Layout(
            document.SegmentCount,
            document.LevelsPerSegment,
            document.ShaftsPerLevel,
            document.DefaultShaftCapacity,
            document.ShaftPitchMeters,
            document.LevelHeightMeters,
            document.SafetyDistanceSegments,
            kinematics,
            home1,
            home2,
            overrides,
            document.ExampleData,
            document.Notes);
    }

    public static LayoutDocument ToDocument(Layout layout)
    {
        return new LayoutDocument
        {
            FormatVersion = PlanningParameters.FormatVersion,
            SegmentCount = layout.SegmentCount,
            LevelsPerSegment = layout.LevelsPerSegment,
            ShaftsPerLevel = layout.ShaftsPerLevel,
            DefaultShaftCapacity = layout.DefaultShaftCapacity,
            ShaftPitchMeters = layout.ShaftPitchMeters,
            LevelHeightMeters = layout.LevelHeightMeters,
            SafetyDistanceSegments = layout.SafetyDistanceSegments,
            Kinematics = new KinematicsDocument
            {
                HorizontalSpeedMetersPerSecond = layout.Kinematics.HorizontalSpeedMetersPerSecond,
                VerticalSpeedMetersPerSecond = layout.Kinematics.VerticalSpeedMetersPerSecond,
                HorizontalAccelerationMetersPerSecondSquared = layout.Kinematics.HorizontalAccelerationMetersPerSecondSquared,
                VerticalAccelerationMetersPerSecondSquared = layout.Kinematics.VerticalAccelerationMetersPerSecondSquared,
                TransferDurationSeconds = layout.Kinematics.TransferDurationSeconds
            },
            Shuttle1Home = ToHome(layout.Shuttle1Home),
            Shuttle2Home = ToHome(layout.Shuttle2Home),
            ShaftCapacityOverrides = layout.CapacityOverrides
                .OrderBy(pair => pair.Key)
                .Select(pair => new ShaftCapacityOverrideDocument
                {
                    Segment = pair.Key.Segment + 1,
                    Level = pair.Key.Level + 1,
                    Shaft = pair.Key.Shaft + 1,
                    Capacity = pair.Value
                })
                .ToList(),
            ExampleData = layout.ExampleData,
            Notes = layout.Notes
        };
    }

    public static ArticleSet ToArticles(ArticlesDocument document)
    {
        EnsureVersion(document.FormatVersion, "articles");
        return new ArticleSet
        {
            ExampleData = document.ExampleData,
            Notes = document.Notes,
            DefaultCratesPerPallet = document.DefaultCratesPerPallet,
            DefaultOffset = document.DefaultOffset,
            AbcDefaults = new AbcDefaults
            {
                TotalCratesPerHour = document.AbcDefaults.TotalCratesPerHour,
                ShareA = document.AbcDefaults.ShareA,
                ShareB = document.AbcDefaults.ShareB,
                ShareC = document.AbcDefaults.ShareC
            },
            Items = document.Articles.Select(article => new Article
            {
                Id = article.Id,
                Name = article.Name,
                AbcClass = article.AbcClass,
                CratesPerHour = article.CratesPerHour,
                CratesPerPallet = article.CratesPerPallet ?? document.DefaultCratesPerPallet,
                Offset = article.Offset ?? document.DefaultOffset,
                ShaftCapacity = article.ShaftCapacity
            }).ToList()
        };
    }

    public static ArticlesDocument ToDocument(ArticleSet articles)
    {
        return new ArticlesDocument
        {
            FormatVersion = PlanningParameters.FormatVersion,
            ExampleData = articles.ExampleData,
            Notes = articles.Notes,
            DefaultCratesPerPallet = articles.DefaultCratesPerPallet,
            DefaultOffset = articles.DefaultOffset,
            AbcDefaults = new AbcDefaultsDocument
            {
                TotalCratesPerHour = articles.AbcDefaults.TotalCratesPerHour,
                ShareA = articles.AbcDefaults.ShareA,
                ShareB = articles.AbcDefaults.ShareB,
                ShareC = articles.AbcDefaults.ShareC
            },
            Articles = articles.Items.Select(article => new ArticleDocument
            {
                Id = article.Id,
                Name = article.Name,
                AbcClass = article.AbcClass,
                CratesPerHour = article.CratesPerHour,
                CratesPerPallet = article.CratesPerPallet,
                Offset = article.Offset,
                ShaftCapacity = article.ShaftCapacity
            }).ToList()
        };
    }

    public static Assignment ToAssignment(AssignmentDocument document)
    {
        EnsureVersion(document.FormatVersion, "assignment");
        var assignment = new Assignment { ExampleData = document.ExampleData, Notes = document.Notes };
        foreach (var shaft in document.Shafts)
        {
            assignment.Set(
                ShaftLocation.FromOneBased(shaft.Segment, shaft.Level, shaft.Shaft),
                shaft.ArticleId,
                shaft.Pinned);
        }

        return assignment;
    }

    public static AssignmentDocument ToDocument(Assignment assignment)
    {
        return new AssignmentDocument
        {
            FormatVersion = PlanningParameters.FormatVersion,
            ExampleData = assignment.ExampleData,
            Notes = assignment.Notes,
            Shafts = assignment.Shafts
                .OrderBy(pair => pair.Key)
                .Select(pair => new AssignedShaftDocument
                {
                    Segment = pair.Key.Segment + 1,
                    Level = pair.Key.Level + 1,
                    Shaft = pair.Key.Shaft + 1,
                    ArticleId = pair.Value.ArticleId,
                    Pinned = pair.Value.Pinned
                })
                .ToList()
        };
    }

    public static PlanningParameters ToParameters(ParametersDocument document)
    {
        EnsureVersion(document.FormatVersion, "parameters");
        return new PlanningParameters
        {
            ExpectedPullWaitSeconds = document.ExpectedPullWaitSeconds,
            ExampleData = document.ExampleData,
            Notes = document.Notes,
            Weights = new CostWeights
            {
                Travel = document.Weights.Travel,
                Imbalance = document.Weights.Imbalance,
                MiddleTraffic = document.Weights.MiddleTraffic,
                Violation = document.Weights.Violation,
                Shortfall = document.Weights.Shortfall
            },
            Annealing = new AnnealingOptions
            {
                Seed = document.Annealing.Seed,
                Iterations = document.Annealing.Iterations,
                InitialTemperature = document.Annealing.InitialTemperature,
                Cooling = document.Annealing.Cooling
            }
        };
    }

    public static ParametersDocument ToDocument(PlanningParameters parameters)
    {
        return new ParametersDocument
        {
            FormatVersion = PlanningParameters.FormatVersion,
            ExpectedPullWaitSeconds = parameters.ExpectedPullWaitSeconds,
            ExampleData = parameters.ExampleData,
            Notes = parameters.Notes,
            Weights = new CostWeightsDocument
            {
                Travel = parameters.Weights.Travel,
                Imbalance = parameters.Weights.Imbalance,
                MiddleTraffic = parameters.Weights.MiddleTraffic,
                Violation = parameters.Weights.Violation,
                Shortfall = parameters.Weights.Shortfall
            },
            Annealing = new AnnealingDocument
            {
                Seed = parameters.Annealing.Seed,
                Iterations = parameters.Annealing.Iterations,
                InitialTemperature = parameters.Annealing.InitialTemperature,
                Cooling = parameters.Annealing.Cooling
            }
        };
    }

    private static ShuttlePose ToPose(ShuttleHomeDocument? home, int segmentCount, int shafts, int shuttle)
    {
        if (home is null)
        {
            if (shuttle == 1)
            {
                return new ShuttlePose(0, 0, 0);
            }

            return new ShuttlePose(Math.Max(0, segmentCount - 1), 0, Math.Max(0, shafts - 1));
        }

        return new ShuttlePose(home.Segment - 1, home.Level - 1, home.Shaft - 1);
    }

    private static ShuttleHomeDocument ToHome(ShuttlePose pose) => new()
    {
        Segment = pose.Segment + 1,
        Level = pose.Level + 1,
        Shaft = pose.Shaft + 1
    };

    private static void EnsureVersion(int version, string kind)
    {
        if (version is not (0 or PlanningParameters.FormatVersion))
        {
            throw new InvalidOperationException(
                $"Unsupported {kind} formatVersion {version}. This prototype reads version {PlanningParameters.FormatVersion}.");
        }
    }
}
