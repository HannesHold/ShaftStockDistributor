using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Operations;

/// <summary>Observed crates in the recent window, used to decide whether a shaft is spare.</summary>
public readonly record struct FlowSample(double CratesPerHour, int Samples, double ElapsedSeconds);

/// <summary>
/// Moves an empty, unpinned shaft only when its article is above the plan and another article is short.
/// A shaft that still holds crates, and every pinned shaft, never moves.
/// </summary>
public static class AutomaticAssignmentPolicy
{
    public const double PackedFill = 0.72;

    public static string? Suggest(
        ShaftRuntime shaft,
        IReadOnlyList<ShaftRuntime> shafts,
        IReadOnlyDictionary<string, ArticleRuntime> articles,
        OperationSettings settings,
        AssignmentMode mode,
        double now,
        Func<string, FlowSample> observed,
        int reassignmentsInLastHour,
        IReadOnlyDictionary<string, int> plannedShaftCounts,
        Func<Article, double, int> recommend)
    {
        if (mode != AssignmentMode.Automatic || shaft.Pinned || shaft.Count != 0 || shaft.TransferLocked)
        {
            return null;
        }

        if (now < shaft.ReassignNotBefore || reassignmentsInLastHour >= settings.MaxReassignmentsPerHour)
        {
            return null;
        }

        if (now + 1e-9 < Math.Max(0, settings.ObservationWindowSeconds))
        {
            return null;
        }

        if (shaft.ArticleId is not null
            && articles.TryGetValue(shaft.ArticleId, out var owner)
            && Assigned(shafts, shaft.ArticleId) <= Floor(owner, shaft, shafts, observed, plannedShaftCounts, settings, recommend))
        {
            return null;
        }

        string? bestId = null;
        double bestScore = 0;
        double currentScore = 0;
        foreach (var article in articles.Values.OrderBy(item => item.Article.Id, StringComparer.Ordinal))
        {
            var sample = observed(article.Article.Id);
            if (!IsShort(article, shafts, sample, recommend))
            {
                continue;
            }

            double score = Score(article, shaft, shafts, sample, recommend);
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

        if (shaft.ArticleId is not null)
        {
            double required = currentScore * (1.0 + Math.Max(0, settings.Hysteresis));
            if (bestScore <= required)
            {
                return null;
            }
        }

        return bestId;
    }

    private static int Floor(
        ArticleRuntime article,
        ShaftRuntime candidate,
        IReadOnlyList<ShaftRuntime> shafts,
        Func<string, FlowSample> observed,
        IReadOnlyDictionary<string, int> plannedShaftCounts,
        OperationSettings settings,
        Func<Article, double, int> recommend)
    {
        string id = article.Article.Id;
        int plannedCount = plannedShaftCounts.TryGetValue(id, out int stored) ? stored : Assigned(shafts, id);
        var sample = observed(id);
        int recommended = Math.Max(0, recommend(article.Article, Math.Max(0, sample.CratesPerHour)));
        if (Stressed(article, candidate, shafts))
        {
            return Math.Max(plannedCount, Assigned(shafts, id));
        }

        if (ClearlyBelow(AssignmentPlan.PlannedInflow(article.Article), sample, settings.Hysteresis))
        {
            return recommended;
        }

        return Math.Max(plannedCount, recommended);
    }

    private static bool ClearlyBelow(double plannedInflow, FlowSample sample, double hysteresis)
    {
        if (plannedInflow <= 1e-9)
        {
            return sample.CratesPerHour <= 1e-9;
        }

        double elapsedHours = Math.Max(sample.ElapsedSeconds, 1) / 3600.0;
        double standardDeviation = Math.Sqrt(Math.Max(sample.Samples, 1)) / elapsedHours;
        double upper = Math.Max(0, sample.CratesPerHour) + 2.0 * standardDeviation;
        return upper < plannedInflow * (1.0 - Math.Max(0, hysteresis));
    }

    private static bool Stressed(ArticleRuntime article, ShaftRuntime candidate, IReadOnlyList<ShaftRuntime> shafts)
    {
        if (article.MeasuredLost > 0)
        {
            return true;
        }

        var siblings = shafts.Where(shaft => shaft.ArticleId == article.Article.Id && shaft.Address != candidate.Address).ToList();
        if (siblings.Count == 0)
        {
            return false;
        }

        return siblings.Average(Fill) >= PackedFill;
    }

    private static bool IsShort(
        ArticleRuntime article,
        IReadOnlyList<ShaftRuntime> shafts,
        FlowSample sample,
        Func<Article, double, int> recommend)
    {
        if (sample.CratesPerHour <= 1e-9)
        {
            return false;
        }

        int assigned = Assigned(shafts, article.Article.Id);
        int recommended = Math.Max(0, recommend(article.Article, sample.CratesPerHour));
        if (assigned < recommended || article.MeasuredLost > 0)
        {
            return true;
        }

        var owned = shafts.Where(shaft => shaft.ArticleId == article.Article.Id).ToList();
        return owned.Count > 0 && owned.Average(Fill) >= PackedFill;
    }

    private static double Score(
        ArticleRuntime article,
        ShaftRuntime candidate,
        IReadOnlyList<ShaftRuntime> shafts,
        FlowSample sample,
        Func<Article, double, int> recommend)
    {
        int assigned = shafts.Count(shaft => shaft.ArticleId == article.Article.Id && shaft.Address != candidate.Address);
        int recommended = Math.Max(0, recommend(article.Article, Math.Max(0, sample.CratesPerHour)));
        int shortfall = Math.Max(0, recommended - assigned);
        double loss = article.MeasuredLost > 0 ? 1 : 0;
        return (shortfall + loss + 0.25) * (Math.Max(0, sample.CratesPerHour) + 1.0);
    }

    private static int Assigned(IReadOnlyList<ShaftRuntime> shafts, string articleId) =>
        shafts.Count(shaft => shaft.ArticleId == articleId);

    private static double Fill(ShaftRuntime shaft) =>
        shaft.Capacity <= 0 ? 1 : shaft.Count / (double)shaft.Capacity;
}
