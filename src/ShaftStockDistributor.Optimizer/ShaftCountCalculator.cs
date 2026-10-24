namespace ShaftStockDistributor.Optimizer;

/// <summary>
/// How many shafts an article needs so crates that arrive during a pull wait still fit.
/// Headroom is the space left after the pull threshold (pallet + offset).
/// </summary>
public static class ShaftCountCalculator
{
    public static int Recommend(
        double inflowCratesPerHour,
        int cratesPerPallet,
        int offset,
        int shaftCapacity,
        double expectedPullWaitSeconds)
    {
        if (inflowCratesPerHour <= 1e-9)
        {
            return 0;
        }

        if (shaftCapacity <= 0 || cratesPerPallet < 1)
        {
            return 1;
        }

        int trigger = cratesPerPallet + Math.Max(0, offset);
        if (shaftCapacity < trigger)
        {
            // A pull can never start. One shaft makes the stall visible; validation warns.
            return 1;
        }

        int headroom = shaftCapacity - trigger;
        double cratesDuringWait = inflowCratesPerHour * (expectedPullWaitSeconds / 3600.0);
        if (headroom <= 0)
        {
            return Math.Max(1, (int)Math.Ceiling(cratesDuringWait));
        }

        return Math.Max(1, (int)Math.Ceiling(cratesDuringWait / headroom));
    }
}
