using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Operations;

/// <summary>
/// Picks a better article for an empty shaft. Non-empty shafts and pinned shafts never move.
/// </summary>
public static class AutomaticAssignmentPolicy
{
    public static string? Suggest(
        ShaftRuntime shaft,
        IReadOnlyList<ShaftRuntime> shafts,
        IReadOnlyDictionary<string, ArticleRuntime> articles,
        OperationSettings settings,
        AssignmentMode mode,
        double now,
        Func<string, double> observedPerHour,
        int reassignmentsInLastHour)
    {
        if (mode != AssignmentMode.Automatic || shaft.Pinned || shaft.Count != 0 || shaft.TransferLocked)
        {
            return null;
        }

        if (now < shaft.ReassignNotBefore || reassignmentsInLastHour >= settings.MaxReassignmentsPerHour)
        {
            return null;
        }

        string? bestId = null;
        double bestScore = double.NegativeInfinity;
        double currentScore = double.NegativeInfinity;
        foreach (var article in articles.Values.OrderBy(a => a.Article.Id, StringComparer.Ordinal))
        {
            double planned = Math.Max(0, article.Article.InflowCratesPerHour);
            double observed = Math.Max(0, observedPerHour(article.Article.Id));
            if (planned <= 0 && observed <= 0)
            {
                continue;
            }

            double score = Score(article, shaft, shafts, Math.Max(observed, planned * 0.5));
            if (article.Article.Id == shaft.ArticleId)
            {
                currentScore = score;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestId = article.Article.Id;
            }
        }

        if (bestId is null || bestId == shaft.ArticleId)
        {
            return null;
        }

        if (shaft.ArticleId is not null && bestScore < currentScore * (1.0 + Math.Max(0, settings.Hysteresis)))
        {
            return null;
        }

        return bestId;
    }

    private static double Score(ArticleRuntime article, ShaftRuntime candidate, IReadOnlyList<ShaftRuntime> shafts, double rate)
    {
        var others = shafts.Where(shaft => shaft.ArticleId == article.Article.Id && shaft.Address != candidate.Address).ToList();
        double pressure = others.Count == 0
            ? 1
            : others.Average(shaft => shaft.Capacity <= 0 ? 1 : shaft.Count / (double)shaft.Capacity);
        double boost = article.Article.Class switch
        {
            AbcClass.A => 1.4,
            AbcClass.B => 1.0,
            _ => 0.75
        };
        return (rate + 1.0) * (0.25 + pressure) * boost;
    }
}
