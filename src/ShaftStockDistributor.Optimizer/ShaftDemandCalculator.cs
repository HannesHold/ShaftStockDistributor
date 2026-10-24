namespace ShaftStockDistributor.Optimizer;

/// <summary>
/// How many shafts an article needs so crates can keep arriving while a full shaft waits for a pull.
/// </summary>
public static class ShaftDemandCalculator
{
    public static int RequiredShafts(
        double cratesPerHour,
        int cratesPerPallet,
        int offset,
        int shaftCapacity,
        double expectedPullWaitSeconds,
        double transferDurationSeconds)
    {
        if (cratesPerPallet < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cratesPerPallet));
        }

        if (cratesPerHour <= 0)
        {
            return 1;
        }

        var threshold = cratesPerPallet + offset;
        if (threshold < 1 || shaftCapacity < 1)
        {
            return 1;
        }

        var exposureHours = Math.Max(0, expectedPullWaitSeconds + transferDurationSeconds) / 3600.0;
        if (exposureHours <= 0)
        {
            return 1;
        }

        var buffer = shaftCapacity - threshold;
        double required;
        if (buffer <= 0)
        {
            // No spare room after the trigger. Size from the whole shaft instead, which still overflows
            // if the shuttle is late. Validation reports the capacity error separately.
            required = cratesPerHour * exposureHours / shaftCapacity;
        }
        else
        {
            // Arrivals during one pull wait must fit in the buffer above the trigger.
            var fromBuffer = cratesPerHour * exposureHours / buffer;
            // Enough shafts that consecutive fills are staggered relative to one pull wait.
            var fromStagger = cratesPerHour * exposureHours / threshold;
            required = Math.Max(fromBuffer, fromStagger);
        }

        return Math.Max(1, Ceil(required));
    }

    public static Dictionary<string, int> ForArticles(
        Core.Layout layout,
        Core.ArticleSet articles,
        IReadOnlyDictionary<string, double> inflows,
        Core.PlanningParameters parameters)
    {
        var demand = new Dictionary<string, int>(articles.Items.Count);
        foreach (var article in articles.Items)
        {
            var capacity = article.ShaftCapacity ?? layout.DefaultShaftCapacity;
            demand[article.Id] = RequiredShafts(
                inflows.GetValueOrDefault(article.Id),
                Math.Max(1, article.CratesPerPallet),
                article.Offset,
                Math.Max(1, capacity),
                parameters.ExpectedPullWaitSeconds,
                layout.Kinematics.TransferDurationSeconds);
        }

        return demand;
    }

    private static int Ceil(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(value - 1e-9);
    }
}
