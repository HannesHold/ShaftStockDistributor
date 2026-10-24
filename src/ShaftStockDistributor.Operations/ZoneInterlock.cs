using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Operations;

public readonly record struct Interval(double Start, double End)
{
    public static Interval Span(double a, double b) => a <= b ? new Interval(a, b) : new Interval(b, a);

    public static Interval Point(double x) => new(x, x);

    public bool Hits(double start, double end)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return End >= start && Start <= end;
    }
}

/// <summary>
/// Keeps the two shuttles at least the safety distance apart along the aisle.
/// Level does not matter: they share one rear aisle and cannot pass.
/// </summary>
public sealed class ZoneInterlock
{
    private readonly Dictionary<ShuttleId, Interval> _held = new();

    public ZoneInterlock(double safetyDistanceMeters)
    {
        SafetyDistanceMeters = Math.Max(0, safetyDistanceMeters);
    }

    public double SafetyDistanceMeters { get; }

    public Interval? Of(ShuttleId shuttle) => _held.TryGetValue(shuttle, out var interval) ? interval : null;

    public bool Conflicts(ShuttleId shuttle, Interval candidate)
    {
        foreach (var pair in _held)
        {
            if (pair.Key == shuttle)
            {
                continue;
            }

            if (TooClose(pair.Value, candidate, SafetyDistanceMeters))
            {
                return true;
            }
        }

        return false;
    }

    public void Hold(ShuttleId shuttle, Interval interval)
    {
        _held[shuttle] = interval;
        if (Violates())
        {
            throw new InvalidOperationException("Shuttle paths overlap inside the safety distance.");
        }
    }

    public bool ReservationsAreSafe() => !Violates();

    public static bool TooClose(Interval left, Interval right, double safety)
    {
        if (left.End < right.Start)
        {
            return right.Start - left.End < safety - 1e-9;
        }

        if (right.End < left.Start)
        {
            return left.Start - right.End < safety - 1e-9;
        }

        return true;
    }

    private bool Violates()
    {
        if (!_held.TryGetValue(ShuttleId.Shuttle1, out var first) || !_held.TryGetValue(ShuttleId.Shuttle2, out var second))
        {
            return false;
        }

        return TooClose(first, second, SafetyDistanceMeters);
    }
}
