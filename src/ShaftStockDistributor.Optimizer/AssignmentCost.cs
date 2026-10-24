using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Optimizer;

public readonly record struct CostBreakdown(
    double Travel,
    double Imbalance,
    double MiddleTraffic,
    double Violation,
    double Total,
    double LoadShuttle1,
    double LoadShuttle2,
    int HardViolations,
    int ShortfallShafts)
{
    public static CostBreakdown Infinite { get; } = new(0, 0, 0, double.PositiveInfinity, double.PositiveInfinity, 0, 0, 0, 0);
}

/// <summary>
/// Surrogate cost used by local search. Travel is estimated as a trip from the responsible shuttle's
/// home (the handover position), because the planner tries to keep fast movers near that home.
/// Axes move together, so each trip uses <see cref="Kinematics.TravelSeconds"/>.
/// This is not the simulation's measured travel; the simulation follows the real job sequence.
/// </summary>
public static class AssignmentCost
{
    public static CostBreakdown Evaluate(
        Layout layout,
        ArticleSet articles,
        Assignment assignment,
        PlanningParameters parameters)
    {
        var inflows = InflowResolver.Resolve(articles);
        var demand = ShaftDemandCalculator.ForArticles(layout, articles, inflows, parameters);
        var counts = articles.Items.ToDictionary(article => article.Id, _ => 0);

        double travel = 0;
        double load1 = 0;
        double load2 = 0;
        double middle = 0;
        var hard = 0;

        foreach (var (location, assigned) in assignment.Shafts)
        {
            var shaft = layout.Find(location);
            var article = articles.Find(assigned.ArticleId);
            if (shaft is null || article is null)
            {
                hard++;
                continue;
            }

            counts[article.Id] = counts.GetValueOrDefault(article.Id) + 1;
            var capacity = layout.EffectiveCapacity(shaft, article);
            if (article.CratesPerPallet < 1 || article.Offset < 0 || capacity < article.PullThreshold)
            {
                hard++;
            }
        }

        var shortfall = 0;
        foreach (var article in articles.Items)
        {
            if (string.IsNullOrWhiteSpace(article.Id))
            {
                hard++;
                continue;
            }

            var count = counts.GetValueOrDefault(article.Id);
            if (count == 0)
            {
                hard++;
            }

            var needed = demand.GetValueOrDefault(article.Id, 1);
            if (count < needed)
            {
                shortfall += needed - count;
            }
        }

        foreach (var (location, assigned) in assignment.Shafts)
        {
            var shaft = layout.Find(location);
            var article = articles.Find(assigned.ArticleId);
            if (shaft is null || article is null || article.PullThreshold < 1)
            {
                continue;
            }

            var count = Math.Max(1, counts.GetValueOrDefault(article.Id));
            var inflow = Math.Max(0, inflows.GetValueOrDefault(article.Id));
            var pullsPerHour = (inflow / count) / article.PullThreshold;
            if (pullsPerHour <= 0)
            {
                continue;
            }

            var (trip, shuttle) = ExpectedTrip(layout, shaft);
            var pullSeconds = pullsPerHour * trip;
            travel += pullSeconds;
            if (shaft.Zone == Zone.Shared)
            {
                middle += pullSeconds;
                load1 += 0.5 * pullSeconds;
                load2 += 0.5 * pullSeconds;
            }
            else if (shuttle == 1)
            {
                load1 += pullSeconds;
            }
            else
            {
                load2 += pullSeconds;
            }
        }

        var weights = parameters.Weights;
        var imbalance = Math.Abs(load1 - load2);
        var violation = (hard * weights.Violation) + (shortfall * weights.Shortfall);
        var total = (weights.Travel * travel)
            + (weights.Imbalance * imbalance)
            + (weights.MiddleTraffic * middle)
            + violation;

        return new CostBreakdown(travel, imbalance, middle, violation, total, load1, load2, hard, shortfall);
    }

    private static (double Seconds, int Shuttle) ExpectedTrip(Layout layout, Shaft shaft)
    {
        var toHome1 = layout.TravelSeconds(layout.Shuttle1Home, shaft);
        var toHome2 = layout.TravelSeconds(layout.Shuttle2Home, shaft);
        return shaft.Zone switch
        {
            Zone.Shuttle1Only => (toHome1, 1),
            Zone.Shuttle2Only => (toHome2, 2),
            _ => toHome1 <= toHome2 ? (toHome1, 1) : (toHome2, 2)
        };
    }
}
