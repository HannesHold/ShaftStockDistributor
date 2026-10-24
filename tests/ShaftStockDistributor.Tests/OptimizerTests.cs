using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;

namespace ShaftStockDistributor.Tests;

public class OptimizerTests
{
    [Fact]
    public void Optimizer_beats_the_left_to_right_baseline_and_keeps_its_best()
    {
        var (layout, articles, parameters) = Sample();
        parameters.Annealing.Iterations = 600;
        parameters.Annealing.Seed = 2;

        var naive = NaiveAssigner.AssignInOrder(layout, articles, parameters);
        var naiveCost = AssignmentCost.Evaluate(layout, articles, naive, parameters);
        var optimized = AnnealingOptimizer.Optimize(layout, articles, parameters);

        Assert.True(optimized.ConstructiveCost.Total < naiveCost.Total, $"constructive {optimized.ConstructiveCost.Total} vs naive {naiveCost.Total}");
        Assert.True(optimized.FinalCost.Total <= optimized.ConstructiveCost.Total);
        Assert.True(optimized.FinalCost.Total < naiveCost.Total);
        Assert.Equal(0, optimized.FinalCost.HardViolations);
        Assert.All(articles.Items, article => Assert.True(optimized.Assignment.CountFor(article.Id) >= 1));
    }

    [Fact]
    public void Pinned_shaft_survives_optimization()
    {
        var (layout, articles, parameters) = Sample();
        parameters.Annealing.Iterations = 400;
        var pinnedLocation = new ShaftLocation(layout.SegmentCount - 1, 0, layout.ShaftsPerLevel - 1);
        var otherPinned = new ShaftLocation(0, 0, 0);
        var locked = new Assignment();
        locked.Set(pinnedLocation, "BEER-20-05", pinned: true);
        locked.Set(otherPinned, "WINE-12-075", pinned: true);

        var optimized = AnnealingOptimizer.Optimize(layout, articles, parameters, locked);

        Assert.True(optimized.Assignment.TryGet(pinnedLocation, out var kept));
        Assert.Equal("BEER-20-05", kept.ArticleId);
        Assert.True(kept.Pinned);
        Assert.True(optimized.Assignment.TryGet(otherPinned, out var wine));
        Assert.Equal("WINE-12-075", wine.ArticleId);
        Assert.True(wine.Pinned);
        Assert.Equal(Zone.Shuttle2Only, layout.Find(pinnedLocation)!.Zone);
        Assert.Equal(Zone.Shuttle1Only, layout.Find(otherPinned)!.Zone);
    }

    [Fact]
    public void Constructive_heuristic_places_fast_and_slow_movers()
    {
        var (layout, articles, parameters) = Sample();
        var assignment = ConstructiveAssigner.Build(layout, articles, parameters);

        var fast = assignment.Shafts
            .Where(pair => pair.Value.ArticleId == "BEER-20-05")
            .Select(pair => layout.Find(pair.Key)!)
            .ToList();
        Assert.True(fast.Count >= 2);
        Assert.Contains(fast, shaft => shaft.Zone == Zone.Shared);
        Assert.Contains(fast, shaft => shaft.Zone is Zone.Shuttle1Only or Zone.Shuttle2Only);

        var slow = assignment.Shafts
            .Where(pair => pair.Value.ArticleId is "WINE-12-075" or "SPIRITS-06" or "KEG-PARTY")
            .Select(pair => layout.Find(pair.Key)!)
            .ToList();
        Assert.NotEmpty(slow);
        Assert.All(slow, shaft => Assert.Equal(Zone.Shared, shaft.Zone));
    }

    private static (Layout Layout, ArticleSet Articles, PlanningParameters Parameters) Sample() =>
        (SampleWarehouse.CreateLayout(), SampleWarehouse.CreateArticles(), SampleWarehouse.CreateParameters());
}
