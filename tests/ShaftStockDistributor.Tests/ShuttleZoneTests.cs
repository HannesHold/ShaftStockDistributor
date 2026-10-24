using ShaftStockDistributor.Core;
using ShaftStockDistributor.Operations;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Tests;

public class ShuttleZoneTests
{
    [Fact]
    public void Two_shuttle_thirds_preset_matches_the_classic_zones()
    {
        var project = ExampleWarehouse.Create();
        ShuttleLayout.ApplyTwoShuttleThirds(project, resetMotion: true);
        var fromRanges = LayoutGeometry.For(project);
        var classic = new LayoutGeometry(project.Layout);
        Assert.Equal(1, project.Shuttle(ShuttleId.Shuttle1).SegmentFrom);
        Assert.Equal(8, project.Shuttle(ShuttleId.Shuttle1).SegmentTo);
        Assert.Equal(5, project.Shuttle(ShuttleId.Shuttle2).SegmentFrom);
        Assert.Equal(12, project.Shuttle(ShuttleId.Shuttle2).SegmentTo);
        Assert.Equal(0, project.Shuttle(ShuttleId.Shuttle1).Home.XMeters);
        Assert.Equal(fromRanges.TotalLengthMeters, project.Shuttle(ShuttleId.Shuttle2).Home.XMeters);
        for (int segment = 1; segment <= 12; segment++)
        {
            Assert.Equal(classic.ZoneOfSegment(segment), fromRanges.ZoneOfSegment(segment));
        }

        var before = AssignmentCostModel.Evaluate(project, AssignmentBuilder.Constructive(project));
        var implied = new DistributorProject
        {
            Layout = project.Layout,
            Articles = project.Articles,
            Optimizer = project.Optimizer,
            Shuttles = project.Shuttles.Select(shuttle => new ShuttleSettings
            {
                Id = shuttle.Id,
                HorizontalSpeedMetersPerSecond = shuttle.HorizontalSpeedMetersPerSecond,
                VerticalSpeedMetersPerSecond = shuttle.VerticalSpeedMetersPerSecond,
                HorizontalAccelerationMetersPerSecondSquared = shuttle.HorizontalAccelerationMetersPerSecondSquared,
                VerticalAccelerationMetersPerSecondSquared = shuttle.VerticalAccelerationMetersPerSecondSquared,
                Home = new ShuttlePose { XMeters = shuttle.Home.XMeters, Level = shuttle.Home.Level },
                Handover = new ShuttlePose { XMeters = shuttle.Handover.XMeters, Level = shuttle.Handover.Level }
            }).ToList()
        };
        ShuttleLayout.FillUnsetRanges(implied);
        var after = AssignmentCostModel.Evaluate(implied, AssignmentBuilder.Constructive(implied));
        Assert.Equal(before.Total, after.Total, 9);
        Assert.Equal(before.Imbalance, after.Imbalance, 9);
        Assert.Equal(before.MiddleTraffic, after.MiddleTraffic, 9);
    }

    [Theory]
    [InlineData(12, 4, 1, 4)]
    [InlineData(12, 3, 1, 5)]
    [InlineData(12, 1, 1, 12)]
    public void Even_split_covers_every_segment_in_order(int segments, int shuttles, int overlap, int firstTo)
    {
        var ranges = ShuttleLayout.EvenRanges(segments, shuttles, overlap);
        Assert.Equal(shuttles, ranges.Length);
        Assert.Equal(1, ranges[0].From);
        Assert.Equal(firstTo, ranges[0].To);
        Assert.Equal(segments, ranges[^1].To);
        var covered = new bool[segments + 1];
        for (int index = 0; index < ranges.Length; index++)
        {
            Assert.True(ranges[index].From <= ranges[index].To);
            if (index > 0)
            {
                Assert.True(ranges[index].From >= ranges[index - 1].From);
                Assert.True(ranges[index].To >= ranges[index - 1].To);
            }

            for (int segment = ranges[index].From; segment <= ranges[index].To; segment++)
            {
                covered[segment] = true;
            }
        }

        Assert.DoesNotContain(covered.Skip(1), open => !open);
    }

    [Fact]
    public void Validation_reports_gaps_order_violations_and_ranges_outside_the_layout()
    {
        var project = ExampleWarehouse.CreateBlank();
        project.Shuttle(ShuttleId.Shuttle1).SegmentFrom = 1;
        project.Shuttle(ShuttleId.Shuttle1).SegmentTo = 2;
        project.Shuttle(ShuttleId.Shuttle2).SegmentFrom = 5;
        project.Shuttle(ShuttleId.Shuttle2).SegmentTo = 6;
        var gaps = ProjectValidator.Validate(project);
        Assert.Contains(gaps, issue => issue.Code == ValidationCodes.ShuttleCoverageGap && issue.Arguments[0] == "3");
        Assert.Contains(gaps, issue => issue.Code == ValidationCodes.ShuttleCoverageGap && issue.Arguments[0] == "4");
        Assert.DoesNotContain(gaps, issue => issue.Code == ValidationCodes.ShuttleOrder);

        project = ExampleWarehouse.CreateBlank();
        project.Shuttle(ShuttleId.Shuttle1).SegmentFrom = 1;
        project.Shuttle(ShuttleId.Shuttle1).SegmentTo = 6;
        project.Shuttle(ShuttleId.Shuttle2).SegmentFrom = 2;
        project.Shuttle(ShuttleId.Shuttle2).SegmentTo = 4;
        var crossed = ProjectValidator.Validate(project);
        Assert.Contains(crossed, issue => issue.Code == ValidationCodes.ShuttleOrder);

        project = ExampleWarehouse.CreateBlank();
        project.Shuttle(ShuttleId.Shuttle2).SegmentTo = 40;
        var outside = ProjectValidator.Validate(project);
        Assert.Contains(outside, issue => issue.Code == ValidationCodes.ShuttleRange && issue.Message.Contains("outside", StringComparison.Ordinal));

        project = ExampleWarehouse.CreateBlank();
        project.Shuttles.Add(ProjectGrid.DefaultShuttle(new ShuttleId(3), 1));
        for (int number = 4; number <= 8; number++)
        {
            project.Shuttles.Add(ProjectGrid.DefaultShuttle(new ShuttleId(number), number));
        }

        ShuttleLayout.ApplyEvenSplit(project, 6, resetHomes: true);
        project.Shuttles.Add(new ShuttleSettings
        {
            Id = new ShuttleId(1),
            SegmentFrom = 1,
            SegmentTo = 2,
            HorizontalSpeedMetersPerSecond = 0.3,
            VerticalSpeedMetersPerSecond = 0.3,
            Home = new ShuttlePose { XMeters = 0, Level = 1 },
            Handover = new ShuttlePose()
        });
        var duplicate = ProjectValidator.Validate(project);
        Assert.Contains(duplicate, issue => issue.Code == ValidationCodes.DuplicateShuttle);

        project.Shuttles.Clear();
        var missing = ProjectValidator.Validate(project);
        Assert.Contains(missing, issue => issue.Code == ValidationCodes.ShuttleMissing);
    }

    [Fact]
    public void Too_many_shuttles_for_the_segment_count_is_an_error()
    {
        var project = ExampleWarehouse.CreateBlank();
        ShuttleLayout.ApplyEvenSplit(project, 6, resetHomes: true);
        project.Shuttles.Add(ProjectGrid.DefaultShuttle(new ShuttleId(7), project.Layout.SegmentCount));
        var issues = ProjectValidator.Validate(project);
        Assert.Contains(issues, issue => issue.Code == ValidationCodes.ShuttleCount);
    }

    [Fact]
    public void Old_json_without_ranges_loads_the_thirds_preset()
    {
        var project = ExampleWarehouse.Create();
        double home = project.Shuttle(ShuttleId.Shuttle2).Home.XMeters;
        string json = System.Text.RegularExpressions.Regex.Replace(
            ProjectSerializer.Serialize(project),
            @"\r?\n\s*""segment(?:From|To)"": \d+,?",
            "");
        Assert.DoesNotContain("segmentFrom", json, StringComparison.Ordinal);
        var loaded = ProjectSerializer.Deserialize(json);
        Assert.Equal(1, loaded.Shuttle(ShuttleId.Shuttle1).SegmentFrom);
        Assert.Equal(8, loaded.Shuttle(ShuttleId.Shuttle1).SegmentTo);
        Assert.Equal(5, loaded.Shuttle(ShuttleId.Shuttle2).SegmentFrom);
        Assert.Equal(12, loaded.Shuttle(ShuttleId.Shuttle2).SegmentTo);
        Assert.Equal(home, loaded.Shuttle(ShuttleId.Shuttle2).Home.XMeters);
        string again = ProjectSerializer.Serialize(loaded);
        Assert.Contains("\"id\": \"shuttle1\"", again, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"shuttle4\"", ProjectSerializer.Serialize(WithShuttle(loaded, 4)), StringComparison.Ordinal);
    }

    [Fact]
    public void Four_shuttles_keep_order_and_do_not_deadlock()
    {
        var project = new DistributorProject
        {
            Name = "Four shuttle aisle",
            AssignmentMode = AssignmentMode.Manual,
            Layout = new LayoutSettings
            {
                SegmentCount = 8,
                LevelsPerSegment = 1,
                ShaftsPerLevel = 2,
                ShaftLengthMeters = 8,
                ShaftPitchMeters = 0.8,
                LevelHeightMeters = 0.5,
                SegmentGapMeters = 0.25,
                TransferDurationSeconds = 4,
                SafetyDistanceMeters = 1
            }
        };
        project.Articles.Add(Article("A", AbcClass.A, 2400, "#4C8DFF"));
        project.Articles.Add(Article("B", AbcClass.A, 2400, "#F0A202"));
        project.Articles.Add(Article("C", AbcClass.B, 800, "#3DDC97"));
        project.Articles.Add(Article("D", AbcClass.C, 400, "#E85D75"));
        ShuttleLayout.ApplyEvenSplit(project, 4, resetHomes: true);
        foreach (var shuttle in project.Shuttles)
        {
            shuttle.HorizontalSpeedMetersPerSecond = 0.5;
            shuttle.VerticalSpeedMetersPerSecond = 0.5;
            shuttle.HorizontalAccelerationMetersPerSecondSquared = 0;
            shuttle.VerticalAccelerationMetersPerSecondSquared = 0;
        }

        Assert.DoesNotContain(ProjectValidator.Validate(project), issue => issue.Severity == ValidationSeverity.Error);
        ProjectGrid.EnsureFullGrid(project);
        Assign(project, 1, "A");
        Assign(project, 3, "A");
        Assign(project, 4, "B");
        Assign(project, 5, "B");
        Assign(project, 6, "C");
        Assign(project, 7, "C");
        Assign(project, 8, "D");

        var session = SimulationSession.Start(project, new SimulationOptions
        {
            DurationSeconds = 500,
            WarmupSeconds = 0,
            Seed = 4,
            Mode = AssignmentMode.Manual
        });
        for (double time = 0; time <= 500; time += 2)
        {
            session.AdvanceTo(time);
            AssertOrder(session, project.Layout.SafetyDistanceMeters);
        }

        var result = session.ToResult();
        Assert.Equal(4, result.Shuttles.Count);
        Assert.True(result.Shuttles.Sum(shuttle => shuttle.Pulls) > 0, "the aisle made no pulls");
        Assert.Contains(result.Shuttles, shuttle => shuttle.Pulls > 0);
        Assert.False(session.Controller.Shuttles.All(shuttle => shuttle.Phase == ShuttlePhase.Blocked), "every shuttle was still blocked");
        Assert.True(result.PalletsPerHour > 0);
        double bound = TheoreticalThroughput.PalletsPerHour(project);
        Assert.True(bound > result.PalletsPerHour, $"upper bound {bound:0.0} sat under {result.PalletsPerHour:0.0}");
    }

    [Fact]
    public void Example_two_shuttle_preset_stays_near_the_known_rate_and_more_shuttles_raise_the_bound()
    {
        var options = new SimulationOptions
        {
            DurationSeconds = 2.5 * 3600,
            WarmupSeconds = 3600,
            Seed = 1,
            Mode = AssignmentMode.Manual
        };
        double previousBound = 0;
        foreach (int count in new[] { 2, 3, 4 })
        {
            var project = ExampleWarehouse.Create();
            if (count == 2)
            {
                ShuttleLayout.ApplyTwoShuttleThirds(project, resetMotion: true);
            }
            else
            {
                ShuttleLayout.ApplyEvenSplit(project, count, resetHomes: true);
            }

            var optimized = AnnealingOptimizer.Run(project, iterations: 400);
            ProjectGrid.ReplaceAssignments(project, optimized.Assignments);
            var run = SimulationSession.Run(project, options);
            double bound = TheoreticalThroughput.PalletsPerHour(project);
            Console.WriteLine(
                $"N={count} pallets/h {run.PalletsPerHour:0.00} offered {run.OfferedPalletsPerHour:0.00} " +
                $"bound {bound:0.00} pulls {string.Join("/", run.Shuttles.Select(shuttle => shuttle.Pulls))} " +
                $"util {string.Join("/", run.Shuttles.Select(shuttle => shuttle.Utilization.ToString("P0")))} " +
                $"cost {optimized.Optimized.Total:0.000} lost {run.LostCrates}");
            Assert.True(run.PalletsPerHour > 20, $"N={count} achieved {run.PalletsPerHour:0.00}");
            Assert.True(bound > run.PalletsPerHour, $"N={count} bound {bound:0.00} under {run.PalletsPerHour:0.00}");
            Assert.Equal(count, run.Shuttles.Count);
            Assert.Contains(run.Shuttles, shuttle => shuttle.Pulls > 0);
            if (count == 2)
            {
                Assert.InRange(run.PalletsPerHour, 45, 70);
                Assert.InRange(bound, 140, 180);
                Assert.True(optimized.Optimized.Total < optimized.Naive.Total);
            }
            else
            {
                Assert.True(bound > previousBound, $"N={count} bound {bound:0.00} did not rise above {previousBound:0.00}");
            }

            previousBound = bound;
        }
    }

    private static DistributorProject WithShuttle(DistributorProject project, int number)
    {
        var copy = project.Clone();
        ShuttleLayout.ApplyEvenSplit(copy, number, resetHomes: true);
        return copy;
    }

    private static void Assign(DistributorProject project, int segment, string article)
    {
        foreach (var slot in project.Assignments.Where(slot => slot.Segment == segment))
        {
            slot.ArticleId = article;
        }
    }

    private static Article Article(string id, AbcClass abc, double inflow, string color) => new()
    {
        Id = id,
        Name = id,
        Class = abc,
        InflowCratesPerHour = inflow,
        CratesPerPallet = 8,
        Offset = 1,
        ColorHex = color,
        Dimensions = new CrateDimensions { LengthMillimeters = 400, WidthMillimeters = 300, HeightMillimeters = 280 }
    };

    private static void AssertOrder(SimulationSession session, double safety)
    {
        var xs = session.Controller.Shuttles.Select(shuttle =>
        {
            var settings = session.Controller.Project.Shuttle(shuttle.Id);
            return ShuttleMotion.At(shuttle, settings, session.Now).X;
        }).ToList();
        for (int index = 1; index < xs.Count; index++)
        {
            Assert.True(xs[index] + 1e-3 >= xs[index - 1], $"passed at {session.Now:0.00}s");
            Assert.True(xs[index] - xs[index - 1] + 1e-3 >= safety, $"closer than {safety} m at {session.Now:0.00}s ({xs[index - 1]:0.00}, {xs[index]:0.00})");
        }

        Assert.True(session.Controller.Interlock.ReservationsAreSafe());
    }
}
