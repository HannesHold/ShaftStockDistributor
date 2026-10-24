using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Operations;

public enum ShuttlePhase
{
    Idle = 0,
    Traveling = 1,
    Transferring = 2,
    Blocked = 3
}

public enum JobPhase
{
    Pending = 0,
    Traveling = 1,
    Transferring = 2,
    Completed = 3,
    Failed = 4
}

public sealed class ShaftRuntime
{
    public required ShaftAddress Address { get; init; }

    public required ZoneKind Zone { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }

    public string? ArticleId { get; set; }

    public bool Pinned { get; set; }

    public int Count { get; set; }

    public int Capacity { get; set; }

    public bool TransferLocked { get; set; }

    public int FrozenDischarge { get; set; }

    public double? TriggerTime { get; set; }

    public double TransferStart { get; set; }

    public double TransferEnd { get; set; }

    public string? ActiveJobId { get; set; }

    public int PullCount { get; set; }

    public int MeasuredPullCount { get; set; }

    public bool MeasuredOverflow { get; set; }

    public double ReassignNotBefore { get; set; }
}

public sealed class ShuttleRuntime
{
    public required ShuttleId Id { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    public int Level { get; set; } = 1;

    public ShuttlePhase Phase { get; set; } = ShuttlePhase.Idle;

    public string? JobId { get; set; }

    public bool InMotion { get; set; }

    public double MotionStart { get; set; }

    public double MotionFromX { get; set; }

    public double MotionFromY { get; set; }

    public double MotionToX { get; set; }

    public double MotionToY { get; set; }

    public double BusySeconds { get; set; }

    public double BlockedSeconds { get; set; }

    public double? BlockedSince { get; set; }

    public int Pulls { get; set; }

    public int MeasuredPulls { get; set; }
}

public sealed class ArticleRuntime
{
    public required Article Article { get; init; }

    public int MeasuredLost { get; set; }

    public double MeasuredPallets { get; set; }

    public Queue<double> Arrivals { get; } = new();
}

public sealed class PullJob
{
    public required string Id { get; init; }

    public PullKind Kind { get; init; } = PullKind.Discharge;

    public ShaftAddress Address { get; init; }

    public string ArticleId { get; init; } = "";

    public JobPhase Phase { get; set; }

    public ShuttleId? Shuttle { get; set; }

    public double Created { get; init; }

    public double ToX { get; set; }

    public double ToY { get; set; }

    public int ToLevel { get; set; }
}

public static class ShuttleMotion
{
    public static (double X, double Y) At(ShuttleRuntime shuttle, ShuttleSettings settings, double now)
    {
        if (!shuttle.InMotion)
        {
            return (shuttle.X, shuttle.Y);
        }

        double elapsed = now - shuttle.MotionStart;
        double x = TravelTime.Position(
            shuttle.MotionFromX,
            shuttle.MotionToX,
            settings.HorizontalSpeedMetersPerSecond,
            settings.HorizontalAccelerationMetersPerSecondSquared,
            elapsed);
        double y = TravelTime.Position(
            shuttle.MotionFromY,
            shuttle.MotionToY,
            settings.VerticalSpeedMetersPerSecond,
            settings.VerticalAccelerationMetersPerSecondSquared,
            elapsed);
        return (x, y);
    }
}
