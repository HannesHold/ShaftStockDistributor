using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Tests;

public class SimulationTests
{
    [Fact]
    public void Same_seed_reproduces_the_run()
    {
        var (layout, articles, assignment) = OptimizedSample(iterations: 30);
        var options = new SimulationOptions { Hours = 2, Seed = 11, RecordTrace = true };

        var first = WarehouseSimulator.Run(layout, articles, assignment, options);
        var second = WarehouseSimulator.Run(layout, articles, assignment, options);

        Assert.Equal(first.PullsShuttle1, second.PullsShuttle1);
        Assert.Equal(first.PullsShuttle2, second.PullsShuttle2);
        Assert.Equal(first.RejectedCrates, second.RejectedCrates);
        Assert.Equal(first.AcceptedCrates, second.AcceptedCrates);
        Assert.Equal(first.AveragePullWaitSeconds, second.AveragePullWaitSeconds);
        Assert.Equal(first.MaxPullWaitSeconds, second.MaxPullWaitSeconds);
        Assert.Equal(first.BlockedSeconds, second.BlockedSeconds);
        Assert.Equal(first.CollisionViolations, second.CollisionViolations);
        Assert.Equal(first.Trace.Count, second.Trace.Count);
        Assert.Equal(first.Trace[^1], second.Trace[^1]);
    }

    [Fact]
    public void Shuttles_never_occupy_conflicting_positions()
    {
        var (layout, articles, assignment) = OptimizedSample(iterations: 40);
        var result = WarehouseSimulator.Run(
            layout,
            articles,
            assignment,
            new SimulationOptions { Hours = 4, Seed = 1, RecordTrace = true });

        Assert.Equal(0, result.CollisionViolations);
        Assert.Null(result.FirstCollision);
        Assert.NotEmpty(result.Trace);
        foreach (var sample in result.Trace)
        {
            var conflict = sample.Shuttle1RangeEnd + layout.SafetyDistanceSegments >= sample.Shuttle2RangeStart
                && sample.Shuttle2RangeEnd + layout.SafetyDistanceSegments >= sample.Shuttle1RangeStart;
            Assert.False(conflict, $"Overlap at t={sample.TimeSeconds}: {sample}");
        }

        Assert.True(result.PullsShuttle1 > 0);
        Assert.True(result.PullsShuttle2 > 0);
        Assert.True(result.AcceptedCrates > 0);
    }

    [Fact]
    public void Waiting_shaft_does_not_block_the_other_shafts_of_the_article()
    {
        var layout = SmallLayout();
        var articles = new ArticleSet
        {
            Items =
            [
                new Article
                {
                    Id = "SPREAD",
                    Name = "Example multi-shaft crate",
                    AbcClass = AbcClass.A,
                    CratesPerHour = 300,
                    CratesPerPallet = 20,
                    Offset = 0,
                    ShaftCapacity = 40
                }
            ]
        };
        var assignment = new Assignment();
        assignment.Set(new ShaftLocation(0, 0, 0), "SPREAD", pinned: false);
        assignment.Set(new ShaftLocation(0, 0, 1), "SPREAD", pinned: false);
        assignment.Set(new ShaftLocation(0, 0, 2), "SPREAD", pinned: false);

        var result = WarehouseSimulator.Run(
            layout,
            articles,
            assignment,
            new SimulationOptions { Hours = 2, Seed = 3, RecordTrace = false });

        Assert.Equal(3, result.ShaftPulls.Count);
        Assert.All(result.ShaftPulls, shaft => Assert.True(shaft.Pulls > 0, $"{shaft}"));
        Assert.Equal(0, result.CollisionViolations);
    }

    [Fact]
    public void Exclusive_zone_is_served_only_by_its_shuttle()
    {
        var layout = SmallLayout();
        var articles = new ArticleSet
        {
            Items =
            [
                new Article
                {
                    Id = "ONLY-LEFT",
                    Name = "Example left-zone crate",
                    AbcClass = AbcClass.A,
                    CratesPerHour = 400,
                    CratesPerPallet = 10,
                    Offset = 2,
                    ShaftCapacity = 20
                }
            ]
        };
        var assignment = new Assignment();
        assignment.Set(new ShaftLocation(0, 0, 0), "ONLY-LEFT", pinned: false);

        var result = WarehouseSimulator.Run(
            layout,
            articles,
            assignment,
            new SimulationOptions { Hours = 1, Seed = 5, RecordTrace = true });

        Assert.Equal(0, result.CollisionViolations);
        Assert.True(result.PullsShuttle1 > 0);
        Assert.Equal(0, result.PullsShuttle2);
        Assert.All(result.Trace, sample =>
        {
            var conflict = ShuttleClearance.RangesConflict(
                sample.Shuttle1RangeStart,
                sample.Shuttle1RangeEnd,
                sample.Shuttle2RangeStart,
                sample.Shuttle2RangeEnd,
                layout.SafetyDistanceSegments);
            Assert.False(conflict);
        });
    }

    private static (Layout Layout, ArticleSet Articles, Assignment Assignment) OptimizedSample(int iterations)
    {
        var layout = SampleWarehouse.CreateLayout();
        var articles = SampleWarehouse.CreateArticles();
        var parameters = SampleWarehouse.CreateParameters();
        parameters.Annealing.Iterations = iterations;
        var optimized = AnnealingOptimizer.Optimize(layout, articles, parameters);
        return (layout, articles, optimized.Assignment);
    }

    private static Layout SmallLayout()
    {
        var document = SampleWarehouse.Layout();
        document.SegmentCount = 9;
        document.LevelsPerSegment = 2;
        document.ShaftsPerLevel = 3;
        document.Shuttle1Home = new ShuttleHomeDocument { Segment = 1, Level = 1, Shaft = 1 };
        document.Shuttle2Home = new ShuttleHomeDocument { Segment = 9, Level = 1, Shaft = 3 };
        document.ExampleData = true;
        return WarehouseMapper.ToLayout(document);
    }
}
