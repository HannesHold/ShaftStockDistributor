namespace ShaftStockDistributor.Simulation;

/// <summary>
/// Sliding pallet rate. Until the window has elapsed the reading is the cumulative
/// average and is marked as still warming up.
/// </summary>
public sealed class PalletRateWindow
{
    private readonly List<(double Time, double Cumulative)> _trace = new();

    public IReadOnlyList<(double Time, double Cumulative)> Points => _trace;

    public void Clear() => _trace.Clear();

    public void Observe(double timeSeconds, double cumulativePallets)
    {
        if (_trace.Count > 0 && timeSeconds <= _trace[^1].Time + 1e-9)
        {
            _trace[^1] = (Math.Max(0, timeSeconds), Math.Max(0, cumulativePallets));
            return;
        }

        _trace.Add((Math.Max(0, timeSeconds), Math.Max(0, cumulativePallets)));
        while (_trace.Count > 8000)
        {
            _trace.RemoveAt(0);
        }
    }

    public RateReading Read(double nowSeconds, double windowSeconds)
    {
        windowSeconds = Math.Max(1, windowSeconds);
        if (nowSeconds < 1 || _trace.Count == 0)
        {
            return new RateReading(0, WarmingUp: true);
        }

        int index = IndexAt(nowSeconds);
        if (index < 0)
        {
            return new RateReading(0, WarmingUp: true);
        }

        double cumulative = _trace[index].Cumulative;
        if (nowSeconds < windowSeconds)
        {
            return new RateReading(Math.Max(0, cumulative / nowSeconds * 3600.0), WarmingUp: true);
        }

        double start = nowSeconds - windowSeconds;
        double baseline = Baseline(start);
        return new RateReading(Math.Max(0, (cumulative - baseline) / windowSeconds * 3600.0), WarmingUp: false);
    }

    private int IndexAt(double time)
    {
        int low = 0;
        int high = _trace.Count - 1;
        int found = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (_trace[mid].Time <= time + 1e-9)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    private double Baseline(double start)
    {
        if (_trace.Count == 0)
        {
            return 0;
        }

        if (start <= _trace[0].Time)
        {
            return _trace[0].Cumulative;
        }

        int index = IndexAt(start);
        if (index < 0)
        {
            return 0;
        }

        if (index >= _trace.Count - 1 || _trace[index].Time >= start - 1e-9)
        {
            return _trace[index].Cumulative;
        }

        var left = _trace[index];
        var right = _trace[index + 1];
        double span = right.Time - left.Time;
        if (span <= 1e-9)
        {
            return right.Cumulative;
        }

        double fraction = Math.Clamp((start - left.Time) / span, 0, 1);
        return left.Cumulative + fraction * (right.Cumulative - left.Cumulative);
    }
}

public readonly record struct RateReading(double PalletsPerHour, bool WarmingUp);
