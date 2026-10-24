using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

public static class AnnealingOptimizer
{
    public static OptimizationResult Run(
        DistributorProject project,
        IProgress<OptimizeProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int? iterations = null)
    {
        var beforeSlots = ProjectGrid.Materialize(project);
        var before = AssignmentCostModel.Evaluate(project, beforeSlots);
        var naiveSlots = AssignmentBuilder.Naive(project);
        var naive = AssignmentCostModel.Evaluate(project, naiveSlots);

        int steps = Math.Max(0, iterations ?? project.Optimizer.Iterations);
        var current = AssignmentBuilder.Constructive(project);
        var currentCost = AssignmentCostModel.Evaluate(project, current);
        var best = Clone(current);
        var bestCost = currentCost;
        var random = new Random(project.Optimizer.Seed);
        double temperature = Math.Max(0.02, bestCost.Total * 0.08);
        double cooling = project.Optimizer.Cooling is > 0 and < 1 ? project.Optimizer.Cooling : 0.997;

        for (int iteration = 1; iteration <= steps; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neighbor = Mutate(current, random);
            var neighborCost = AssignmentCostModel.Evaluate(project, neighbor);
            double delta = neighborCost.Total - currentCost.Total;
            if (delta <= 0 || random.NextDouble() < Math.Exp(-delta / Math.Max(temperature, 1e-9)))
            {
                current = neighbor;
                currentCost = neighborCost;
                if (neighborCost.Total + 1e-12 < bestCost.Total)
                {
                    best = Clone(neighbor);
                    bestCost = neighborCost;
                }
            }

            temperature *= cooling;
            if (iteration == steps || iteration % 25 == 0)
            {
                progress?.Report(new OptimizeProgress
                {
                    Iteration = iteration,
                    Iterations = steps,
                    Temperature = temperature,
                    CurrentCost = currentCost.Total,
                    BestCost = bestCost.Total
                });
            }
        }

        if (steps == 0)
        {
            progress?.Report(new OptimizeProgress
            {
                Iteration = 0,
                Iterations = 0,
                Temperature = temperature,
                CurrentCost = bestCost.Total,
                BestCost = bestCost.Total
            });
        }

        AssignmentPlan.RememberInflows(project);
        return new OptimizationResult
        {
            Assignments = best,
            Before = before,
            Naive = naive,
            Optimized = bestCost,
            Iterations = steps,
            ChangedShafts = CountChanges(beforeSlots, best)
        };
    }

    private static int CountChanges(List<ShaftAssignment> left, List<ShaftAssignment> right)
    {
        var map = right.ToDictionary(slot => slot.Address);
        int changes = 0;
        foreach (var slot in left)
        {
            if (!map.TryGetValue(slot.Address, out var other) || other.ArticleId != slot.ArticleId)
            {
                changes++;
            }
        }

        return changes;
    }

    private static List<ShaftAssignment> Mutate(List<ShaftAssignment> source, Random random)
    {
        var next = Clone(source);
        var movable = new List<int>();
        for (int index = 0; index < next.Count; index++)
        {
            if (!next[index].Pinned)
            {
                movable.Add(index);
            }
        }

        if (movable.Count < 2)
        {
            return next;
        }

        int first = movable[random.Next(movable.Count)];
        int second = movable[random.Next(movable.Count)];
        if (first == second)
        {
            return next;
        }

        bool moveOntoEmpty = random.Next(2) == 0
            && next[first].ArticleId is not null
            && next[second].ArticleId is null;
        if (moveOntoEmpty)
        {
            next[second].ArticleId = next[first].ArticleId;
            next[first].ArticleId = null;
        }
        else
        {
            (next[first].ArticleId, next[second].ArticleId) = (next[second].ArticleId, next[first].ArticleId);
        }

        return next;
    }

    private static List<ShaftAssignment> Clone(List<ShaftAssignment> source) =>
        source.Select(slot => new ShaftAssignment
        {
            Segment = slot.Segment,
            Level = slot.Level,
            Shaft = slot.Shaft,
            ArticleId = slot.ArticleId,
            Pinned = slot.Pinned
        }).ToList();
}
