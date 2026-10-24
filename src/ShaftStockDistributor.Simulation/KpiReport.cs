using System.Globalization;
using System.Text;

namespace ShaftStockDistributor.Simulation;

public static class KpiReport
{
    public static string Format(SimulationResult result, string? title = null)
    {
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            text.AppendLine(title);
        }

        text.AppendLine(culture, $"Horizon: {result.Hours:0.###} h, seed {result.Seed}");
        text.AppendLine(culture, $"Arrivals: {result.Arrivals}, accepted: {result.AcceptedCrates}, rejected/lost: {result.RejectedCrates}");
        text.AppendLine(culture, $"Pulls: shuttle 1 = {result.PullsShuttle1}, shuttle 2 = {result.PullsShuttle2}, total = {result.PullCount}");
        text.AppendLine(culture, $"Utilization: shuttle 1 = {Percent(result.UtilizationShuttle1)}, shuttle 2 = {Percent(result.UtilizationShuttle2)}");
        text.AppendLine(culture, $"Busy time: shuttle 1 = {result.BusySecondsShuttle1:0.0} s, shuttle 2 = {result.BusySecondsShuttle2:0.0} s");
        text.AppendLine(culture, $"Blocked time: shuttle 1 = {result.BlockedSecondsShuttle1:0.0} s, shuttle 2 = {result.BlockedSecondsShuttle2:0.0} s, total = {result.BlockedSeconds:0.0} s");
        text.AppendLine(culture, $"Pull wait: average = {result.AveragePullWaitSeconds:0.0} s, max = {result.MaxPullWaitSeconds:0.0} s");
        text.AppendLine(culture, $"Collision violations: {result.CollisionViolations}");
        if (!string.IsNullOrWhiteSpace(result.FirstCollision))
        {
            text.AppendLine(result.FirstCollision);
        }

        text.AppendLine("Overflow events (shaft reached capacity while a pull was still waiting):");
        if (result.OverflowEventsByArticle.Count == 0)
        {
            text.AppendLine("  (none)");
        }
        else
        {
            foreach (var (article, count) in result.OverflowEventsByArticle.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                text.AppendLine(culture, $"  {article}: {count}");
            }
        }

        text.AppendLine("Per-shaft pull counts (one-based segment / level / shaft):");
        if (result.ShaftPulls.Count == 0)
        {
            text.AppendLine("  (no assigned shafts)");
        }
        else
        {
            foreach (var shaft in result.ShaftPulls.OrderBy(shaft => shaft.Segment).ThenBy(shaft => shaft.Level).ThenBy(shaft => shaft.Shaft))
            {
                text.AppendLine(culture, $"  S{shaft.Segment + 1:D2}-L{shaft.Level + 1}-H{shaft.Shaft + 1}  {shaft.ArticleId,-16}  pulls {shaft.Pulls,4}  residual {shaft.ResidualCrates,3}");
            }
        }

        return text.ToString();
    }

    public static string Compare(SimulationResult left, SimulationResult right, string leftName, string rightName)
    {
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine(culture, $"{"KPI",-28} {leftName,16} {rightName,16}");
        Line(text, "Pulls shuttle 1", left.PullsShuttle1, right.PullsShuttle1);
        Line(text, "Pulls shuttle 2", left.PullsShuttle2, right.PullsShuttle2);
        Line(text, "Utilization shuttle 1", Percent(left.UtilizationShuttle1), Percent(right.UtilizationShuttle1));
        Line(text, "Utilization shuttle 2", Percent(left.UtilizationShuttle2), Percent(right.UtilizationShuttle2));
        Line(text, "Blocked seconds", left.BlockedSeconds.ToString("0.0", culture), right.BlockedSeconds.ToString("0.0", culture));
        Line(text, "Avg pull wait s", left.AveragePullWaitSeconds.ToString("0.0", culture), right.AveragePullWaitSeconds.ToString("0.0", culture));
        Line(text, "Max pull wait s", left.MaxPullWaitSeconds.ToString("0.0", culture), right.MaxPullWaitSeconds.ToString("0.0", culture));
        Line(text, "Rejected crates", left.RejectedCrates, right.RejectedCrates);
        Line(text, "Overflow events", left.OverflowEventsByArticle.Values.Sum(), right.OverflowEventsByArticle.Values.Sum());
        Line(text, "Collision violations", left.CollisionViolations, right.CollisionViolations);
        text.AppendLine();
        text.AppendLine(culture, $"--- {leftName} ---");
        text.Append(Format(left));
        text.AppendLine();
        text.AppendLine(culture, $"--- {rightName} ---");
        text.Append(Format(right));
        return text.ToString();
    }

    private static void Line(StringBuilder text, string name, object left, object right)
    {
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{name,-28} {left,16} {right,16}"));
    }

    private static string Percent(double value) =>
        (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
