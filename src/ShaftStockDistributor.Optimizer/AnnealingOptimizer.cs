using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

public enum OptimizationStart
{
    /// <summary>Ignore unpinned shafts and build a new constructive assignment around the pins.</summary>
    Constructive = 0,

    /// <summary>Keep the current unpinned placement, fill missing demand, then search.</summary>
    KeepCurrent = 1
}

public sealed class OptimizationResult
{
    public required Assignment Assignment { get; init; }

    public required CostBreakdown ConstructiveCost { get; init; }

    public required CostBreakdown FinalCost { get; init; }

    public int AcceptedMoves { get; init; }

    public int Iterations { get; init; }
}

public static class AnnealingOptimizer
{
    public static OptimizationResult Optimize(
        Layout layout,
        ArticleSet articles,
        PlanningParameters parameters,
        Assignment? existing = null,
        OptimizationStart start = OptimizationStart.Constructive)
    {
        var seed = BuildStart(layout, articles, parameters, existing, start);
        var improved = Search(layout, articles, parameters, seed);
        improved.Assignment.ExampleData = existing?.ExampleData ?? articles.ExampleData;
        if (string.IsNullOrWhiteSpace(improved.Assignment.Notes))
        {
            improved.Assignment.Notes = "Optimized assignment. Pinned shafts were left unchanged.";
        }

        return improved;
    }

    private static Assignment BuildStart(
        Layout layout,
        ArticleSet articles,
        PlanningParameters parameters,
        Assignment? existing,
        OptimizationStart start)
    {
        Assignment? locked = null;
        if (existing is not null)
        {
            locked = new Assignment();
            foreach (var (location, shaftAssignment) in existing.Shafts)
            {
                if (shaftAssignment.Pinned || start == OptimizationStart.KeepCurrent)
                {
                    locked.Set(location, shaftAssignment.ArticleId, pinned: true);
                }
            }
        }

        var built = ConstructiveAssigner.Build(layout, articles, parameters, locked);
        if (start == OptimizationStart.KeepCurrent && existing is not null)
        {
            foreach (var location in built.Shafts.Keys.ToList())
            {
                var current = built.Shafts[location];
                var pinned = existing.TryGet(location, out var original) && original.Pinned;
                built.Set(location, current.ArticleId, pinned);
            }
        }

        return built;
    }

    private static OptimizationResult Search(
        Layout layout,
        ArticleSet articles,
        PlanningParameters parameters,
        Assignment start)
    {
        var annealing = parameters.Annealing;
        var iterations = Math.Max(0, annealing.Iterations);
        var current = start.Clone();
        var currentCost = AssignmentCost.Evaluate(layout, articles, current, parameters);
        var best = current.Clone();
        var bestCost = currentCost;
        var accepted = 0;
        var temperature = Math.Max(1e-6, annealing.InitialTemperature);
        var cooling = annealing.Cooling <= 0 || annealing.Cooling >= 1 ? 0.997 : annealing.Cooling;
        var random = new Random(annealing.Seed);
        var demand = ShaftDemandCalculator.ForArticles(layout, articles, InflowResolver.Resolve(articles), parameters);
        var locations = layout.Shafts.Select(shaft => shaft.Location).ToList();

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            Assignment? candidate = null;
            for (var attempt = 0; attempt < 8 && candidate is null; attempt++)
            {
                candidate = TryMutate(current, locations, articles, demand, random);
            }

            if (candidate is null)
            {
                temperature *= cooling;
                continue;
            }

            var cost = AssignmentCost.Evaluate(layout, articles, candidate, parameters);
            var delta = cost.Total - currentCost.Total;
            var accept = delta <= 0 || random.NextDouble() < Math.Exp(-delta / temperature);
            if (accept && !double.IsNaN(cost.Total) && !double.IsPositiveInfinity(cost.Total))
            {
                current = candidate;
                currentCost = cost;
                accepted++;
                if (cost.Total < bestCost.Total)
                {
                    best = candidate.Clone();
                    bestCost = cost;
                }
            }

            temperature *= cooling;
        }

        best.Notes = "Optimized by simulated annealing. Pinned shafts were left unchanged.";
        return new OptimizationResult
        {
            Assignment = best,
            ConstructiveCost = AssignmentCost.Evaluate(layout, articles, start, parameters),
            FinalCost = bestCost,
            AcceptedMoves = accepted,
            Iterations = iterations
        };
    }

    private static Assignment? TryMutate(
        Assignment current,
        List<ShaftLocation> locations,
        ArticleSet articles,
        IReadOnlyDictionary<string, int> demand,
        Random random)
    {
        if (locations.Count == 0 || articles.Items.Count == 0)
        {
            return null;
        }

        var assigned = current.Shafts.Where(pair => !pair.Value.Pinned).Select(pair => pair.Key).ToList();
        var empty = locations.Where(location => !current.Contains(location)).ToList();
        var roll = random.Next(4);
        var next = current.Clone();

        if (roll == 0 && assigned.Count >= 2)
        {
            var left = assigned[random.Next(assigned.Count)];
            var right = assigned[random.Next(assigned.Count)];
            if (left == right)
            {
                return null;
            }

            var leftArticle = next.Shafts[left].ArticleId;
            var rightArticle = next.Shafts[right].ArticleId;
            if (leftArticle == rightArticle)
            {
                return null;
            }

            next.Set(left, rightArticle, pinned: false);
            next.Set(right, leftArticle, pinned: false);
            return next;
        }

        if (roll == 1 && assigned.Count >= 1 && empty.Count >= 1)
        {
            var from = assigned[random.Next(assigned.Count)];
            var to = empty[random.Next(empty.Count)];
            var articleId = next.Shafts[from].ArticleId;
            next.Remove(from);
            next.Set(to, articleId, pinned: false);
            return next;
        }

        if (roll == 2 && empty.Count >= 1)
        {
            var shortArticles = articles.Items
                .Where(article => next.CountFor(article.Id) < demand.GetValueOrDefault(article.Id))
                .Select(article => article.Id)
                .ToList();
            if (shortArticles.Count == 0)
            {
                return null;
            }

            var articleId = shortArticles[random.Next(shortArticles.Count)];
            var to = empty[random.Next(empty.Count)];
            next.Set(to, articleId, pinned: false);
            return next;
        }

        var surplus = assigned.Where(location =>
        {
            var articleId = current.Shafts[location].ArticleId;
            var needed = Math.Max(1, demand.GetValueOrDefault(articleId));
            return current.CountFor(articleId) > needed;
        }).ToList();
        if (surplus.Count == 0)
        {
            return null;
        }

        next.Remove(surplus[random.Next(surplus.Count)]);
        return next;
    }
}
