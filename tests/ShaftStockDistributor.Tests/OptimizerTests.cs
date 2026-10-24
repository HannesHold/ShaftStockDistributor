using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;

namespace ShaftStockDistributor.Tests;

public class OptimizerTests
{
    [Fact]
    public void Optimized_assignment_beats_the_naive_left_to_right_baseline()
    {
        var project = TestWarehouses.BalancedPlanningCase();
        var result = AnnealingOptimizer.Run(project, iterations: 200);
        Assert.True(
            result.Optimized.Total < result.Naive.Total,
            $"Expected a lower cost than the naive baseline. Naive {result.Naive}; optimized {result.Optimized}.");
        Assert.True(result.Optimized.Imbalance < result.Naive.Imbalance);
    }

    [Fact]
    public void Pinned_shafts_keep_their_article()
    {
        var project = TestWarehouses.BalancedPlanningCase();
        var pinned = project.Assignments.First(slot => slot.Segment == 6 && slot.Level == 1 && slot.Shaft == 1);
        pinned.ArticleId = "A1";
        pinned.Pinned = true;

        var result = AnnealingOptimizer.Run(project, iterations: 80);
        var after = result.Assignments.Single(slot => slot.Segment == 6 && slot.Level == 1 && slot.Shaft == 1);
        Assert.Equal("A1", after.ArticleId);
        Assert.True(after.Pinned);
    }
}
