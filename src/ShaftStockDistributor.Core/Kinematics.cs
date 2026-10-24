namespace ShaftStockDistributor.Core;

/// <summary>
/// Shuttle motion limits. Both axes move at the same time, so travel time is the slower axis
/// (Chebyshev combination of the two trapezoidal profiles).
/// </summary>
public sealed class Kinematics
{
    public double HorizontalSpeedMetersPerSecond { get; init; } = 1.5;

    public double VerticalSpeedMetersPerSecond { get; init; } = 0.6;

    public double HorizontalAccelerationMetersPerSecondSquared { get; init; } = 0.5;

    public double VerticalAccelerationMetersPerSecondSquared { get; init; } = 0.3;

    /// <summary>Fixed time the shaft contents need to run out onto the rear conveyor.</summary>
    public double TransferDurationSeconds { get; init; } = 30;

    /// <summary>
    /// Time to cover <paramref name="distanceMeters"/> with a trapezoidal (or triangular) speed profile.
    /// Acceleration and deceleration are symmetric. A non-positive acceleration falls back to constant speed.
    /// </summary>
    public static double AxisSeconds(double distanceMeters, double speedMetersPerSecond, double acceleration)
    {
        var distance = Math.Abs(distanceMeters);
        if (distance <= 1e-9)
        {
            return 0;
        }

        if (speedMetersPerSecond <= 0)
        {
            return double.PositiveInfinity;
        }

        if (acceleration <= 0)
        {
            return distance / speedMetersPerSecond;
        }

        var rampDistance = (speedMetersPerSecond * speedMetersPerSecond) / acceleration;
        if (distance >= rampDistance)
        {
            var cruise = distance - rampDistance;
            return (2 * (speedMetersPerSecond / acceleration)) + (cruise / speedMetersPerSecond);
        }

        return 2 * Math.Sqrt(distance / acceleration);
    }

    public double TravelSeconds(double deltaXMeters, double deltaYMeters)
    {
        var horizontal = AxisSeconds(deltaXMeters, HorizontalSpeedMetersPerSecond, HorizontalAccelerationMetersPerSecondSquared);
        var vertical = AxisSeconds(deltaYMeters, VerticalSpeedMetersPerSecond, VerticalAccelerationMetersPerSecondSquared);
        return Math.Max(horizontal, vertical);
    }
}

/// <summary>Shuttle pose aligned with a shaft coordinate (or a home position expressed the same way).</summary>
public readonly record struct ShuttlePose(int Segment, int Level, int Shaft)
{
    public double X(Layout layout) => layout.X(Segment, Shaft);

    public double Y(Layout layout) => layout.Y(Level);
}
