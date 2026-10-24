namespace ShaftStockDistributor.Core;

/// <summary>
/// Travel time when the horizontal and vertical axes move together.
/// The move finishes when the slower axis finishes.
/// </summary>
public static class TravelTime
{
    public static double Axis(double distanceMeters, double speedMetersPerSecond, double accelerationMetersPerSecondSquared)
    {
        double distance = Math.Abs(distanceMeters);
        if (distance <= 1e-9)
        {
            return 0;
        }

        if (speedMetersPerSecond <= 1e-9)
        {
            return double.PositiveInfinity;
        }

        if (accelerationMetersPerSecondSquared <= 1e-9)
        {
            return distance / speedMetersPerSecond;
        }

        double accelDistance = speedMetersPerSecond * speedMetersPerSecond / (2.0 * accelerationMetersPerSecondSquared);
        if (distance + 1e-12 >= 2.0 * accelDistance)
        {
            double cruise = distance - 2.0 * accelDistance;
            return 2.0 * (speedMetersPerSecond / accelerationMetersPerSecondSquared) + cruise / speedMetersPerSecond;
        }

        return 2.0 * Math.Sqrt(distance / accelerationMetersPerSecondSquared);
    }

    public static double Simultaneous(double deltaXMeters, double deltaYMeters, ShuttleSettings shuttle) =>
        Math.Max(
            Axis(deltaXMeters, shuttle.HorizontalSpeedMetersPerSecond, shuttle.HorizontalAccelerationMetersPerSecondSquared),
            Axis(deltaYMeters, shuttle.VerticalSpeedMetersPerSecond, shuttle.VerticalAccelerationMetersPerSecondSquared));

    public static double Position(double from, double to, double speed, double acceleration, double elapsedSeconds)
    {
        double delta = to - from;
        double distance = Math.Abs(delta);
        if (distance <= 1e-12)
        {
            return from;
        }

        double covered = DistanceCovered(distance, speed, acceleration, elapsedSeconds);
        if (covered > distance)
        {
            covered = distance;
        }

        return from + Math.Sign(delta) * covered;
    }

    public static double DistanceCovered(double distance, double speed, double acceleration, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0 || distance <= 0)
        {
            return 0;
        }

        double totalTime = Axis(distance, speed, acceleration);
        if (double.IsInfinity(totalTime))
        {
            return 0;
        }

        if (elapsedSeconds >= totalTime)
        {
            return distance;
        }

        if (acceleration <= 1e-9 || speed <= 1e-9)
        {
            return Math.Min(distance, speed * elapsedSeconds);
        }

        double accelDistance = speed * speed / (2.0 * acceleration);
        bool triangular = distance < 2.0 * accelDistance - 1e-12;
        if (triangular)
        {
            double half = totalTime / 2.0;
            if (elapsedSeconds <= half)
            {
                return 0.5 * acceleration * elapsedSeconds * elapsedSeconds;
            }

            double remaining = totalTime - elapsedSeconds;
            return distance - 0.5 * acceleration * remaining * remaining;
        }

        double accelTime = speed / acceleration;
        double accelDist = 0.5 * acceleration * accelTime * accelTime;
        if (elapsedSeconds <= accelTime)
        {
            return 0.5 * acceleration * elapsedSeconds * elapsedSeconds;
        }

        if (elapsedSeconds >= totalTime - accelTime)
        {
            double remaining = totalTime - elapsedSeconds;
            return distance - 0.5 * acceleration * remaining * remaining;
        }

        return accelDist + speed * (elapsedSeconds - accelTime);
    }
}
