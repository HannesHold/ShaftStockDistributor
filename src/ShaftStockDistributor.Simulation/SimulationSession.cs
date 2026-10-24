using ShaftStockDistributor.Core;
using ShaftStockDistributor.Operations;

namespace ShaftStockDistributor.Simulation;

/// <summary>
/// Discrete-event clock. Arrivals are random; every pull decision is made by <see cref="OperationsController"/>.
/// </summary>
public sealed class SimulationSession
{
    private readonly PriorityQueue<Action, (double Time, long Sequence)> _queue = new();
    private readonly SimulationOptions _options;
    private readonly CancellationToken _cancellation;
    private long _sequence;
    private double _now;

    private SimulationSession(SimulationOptions options, CancellationToken cancellation)
    {
        _options = options;
        _cancellation = cancellation;
    }

    public OperationsController Controller { get; private set; } = null!;

    public SimulatedHardwareAdapter Hardware { get; private set; } = null!;

    public double Now => _now;

    public double? NextEventTime => _queue.TryPeek(out _, out var key) ? key.Time : null;

    public static SimulationSession Start(DistributorProject project, SimulationOptions options, CancellationToken cancellation = default)
    {
        var session = new SimulationSession(options, cancellation);
        session.Hardware = new SimulatedHardwareAdapter(() => session._now, session.Schedule);
        session.Controller = new OperationsController(
            project,
            session.Hardware,
            () => session._now,
            options.WarmupSeconds,
            options.Mode);
        session.SeedArrivals(new Random(options.Seed));
        return session;
    }

    public static SimulationResult Run(DistributorProject project, SimulationOptions options, CancellationToken cancellation = default)
    {
        var session = Start(project, options, cancellation);
        session.AdvanceTo(options.DurationSeconds);
        return session.ToResult();
    }

    public void AdvanceTo(double targetSeconds)
    {
        if (targetSeconds < _now)
        {
            return;
        }

        int steps = 0;
        while (_queue.TryPeek(out var action, out var key) && key.Time <= targetSeconds + 1e-9)
        {
            _queue.Dequeue();
            _now = key.Time;
            if ((++steps & 255) == 0)
            {
                _cancellation.ThrowIfCancellationRequested();
            }

            action();
        }

        _now = targetSeconds;
    }

    public WarehouseSnapshot Capture() => BuildSnapshot(ToResult());

    public SimulationResult ToResult()
    {
        double measured = Math.Max(0, _now - _options.WarmupSeconds);
        double hours = measured / 3600.0;
        double pallets = Controller.MeasuredPallets;
        var lost = Controller.Articles.ToDictionary(pair => pair.Key, pair => pair.Value.MeasuredLost, StringComparer.Ordinal);
        var pulls = Controller.Shafts
            .Where(shaft => shaft.MeasuredPullCount > 0)
            .ToDictionary(shaft => shaft.Address.ToString(), shaft => shaft.MeasuredPullCount, StringComparer.Ordinal);
        double util1 = measured <= 1e-9 ? 0 : Controller.BusySecondsAt(ShuttleId.Shuttle1, _now) / measured;
        double util2 = measured <= 1e-9 ? 0 : Controller.BusySecondsAt(ShuttleId.Shuttle2, _now) / measured;
        return new SimulationResult
        {
            Seed = _options.Seed,
            DurationSeconds = _now,
            WarmupSeconds = _options.WarmupSeconds,
            MeasuredSeconds = measured,
            Pallets = pallets,
            PalletsPerHour = hours <= 1e-12 ? 0 : pallets / hours,
            OfferedPalletsPerHour = InflowAllocator.OfferedPalletsPerHour(Controller.Project.Articles),
            PullsShuttle1 = Controller.Shuttles.First(shuttle => shuttle.Id == ShuttleId.Shuttle1).MeasuredPulls,
            PullsShuttle2 = Controller.Shuttles.First(shuttle => shuttle.Id == ShuttleId.Shuttle2).MeasuredPulls,
            Utilization1 = util1,
            Utilization2 = util2,
            AveragePullWaitSeconds = Controller.WaitAverage,
            MaxPullWaitSeconds = Controller.WaitMax,
            BlockedSeconds = Controller.BlockedSecondsAt(_now),
            OverflowingShafts = Controller.Shafts.Count(shaft => shaft.MeasuredOverflow),
            LostCrates = Controller.MeasuredLost,
            ControllerPullJobs = Controller.PullJobsSent,
            HardwarePullJobs = Hardware.PullJobsSent,
            LostCratesByArticle = lost,
            PullsByShaft = pulls
        };
    }

    private void SeedArrivals(Random random)
    {
        foreach (var article in Controller.Project.Articles.OrderBy(article => article.Id, StringComparer.Ordinal))
        {
            ScheduleNext(article.Id, article.InflowCratesPerHour, random, 0);
        }
    }

    private void ScheduleNext(string articleId, double cratesPerHour, Random random, double notBefore)
    {
        double rate = cratesPerHour / 3600.0;
        if (rate <= 1e-12)
        {
            return;
        }

        double delay = SampleExponential(random, rate);
        if (double.IsInfinity(delay))
        {
            return;
        }

        double at = notBefore + delay;
        Schedule(at, () =>
        {
            Controller.TryAcceptCrate(articleId, _now);
            ScheduleNext(articleId, cratesPerHour, random, _now);
        });
    }

    private void Schedule(double time, Action action)
    {
        long sequence = ++_sequence;
        _queue.Enqueue(action, (time, sequence));
    }

    private static double SampleExponential(Random random, double ratePerSecond)
    {
        double unit = 1.0 - random.NextDouble();
        if (unit <= 0)
        {
            unit = double.Epsilon;
        }

        return -Math.Log(unit) / ratePerSecond;
    }

    private WarehouseSnapshot BuildSnapshot(SimulationResult kpis)
    {
        var geometry = Controller.Geometry;
        var span = geometry.ZoneSpan(ZoneKind.Shared);
        var shafts = Controller.Shafts.Select(shaft =>
        {
            var article = shaft.ArticleId is null ? null : Controller.Project.FindArticle(shaft.ArticleId);
            double fill = shaft.Capacity <= 0 ? 0 : shaft.Count / (double)shaft.Capacity;
            if (shaft.TransferLocked && shaft.TransferEnd > shaft.TransferStart)
            {
                double progress = Math.Clamp((_now - shaft.TransferStart) / (shaft.TransferEnd - shaft.TransferStart), 0, 1);
                double shown = shaft.FrozenDischarge * (1 - progress);
                fill = shaft.Capacity <= 0 ? 0 : shown / shaft.Capacity;
            }

            return new ShaftVisual
            {
                Segment = shaft.Address.Segment,
                Level = shaft.Address.Level,
                Shaft = shaft.Address.Shaft,
                Zone = shaft.Zone,
                ArticleId = shaft.ArticleId,
                ArticleName = article?.Name ?? "",
                ColorHex = article?.ColorHex ?? "#5C6770",
                Count = shaft.Count,
                Capacity = shaft.Capacity,
                VisualFill = Math.Clamp(fill, 0, 1),
                Pinned = shaft.Pinned,
                PullCount = shaft.MeasuredPullCount,
                Transferring = shaft.TransferLocked,
                XMeters = shaft.X,
                YMeters = shaft.Y
            };
        }).ToList();

        var shuttles = Controller.Shuttles.Select(shuttle =>
        {
            var settings = Controller.Project.Shuttle(shuttle.Id);
            var (x, y) = ShuttleMotion.At(shuttle, settings, _now);
            bool inShared = span.End > span.Start && x >= span.Start && x <= span.End;
            return new ShuttleVisual
            {
                Id = shuttle.Id,
                XMeters = x,
                YMeters = y,
                Level = shuttle.Level,
                Phase = shuttle.Phase == ShuttlePhase.Blocked ? "Waiting" : shuttle.Phase.ToString(),
                IsBlocked = shuttle.Phase == ShuttlePhase.Blocked,
                InSharedZone = inShared
            };
        }).ToList();

        bool locked = Controller.Shuttles.Any(shuttle =>
        {
            var interval = Controller.Interlock.Of(shuttle.Id);
            return interval is Interval held && held.Hits(span.Start, span.End)
                && shuttle.Phase is ShuttlePhase.Traveling or ShuttlePhase.Transferring or ShuttlePhase.Blocked;
        });

        var jobs = Controller.OpenJobs.Select(job =>
        {
            int crates = 0;
            var shaft = Controller.Shafts.FirstOrDefault(candidate => candidate.Address == job.Address);
            if (shaft is not null)
            {
                crates = shaft.TransferLocked ? shaft.FrozenDischarge : shaft.Count;
            }

            return new JobVisual
            {
                Id = job.Id,
                Article = Controller.Project.FindArticle(job.ArticleId)?.Name ?? job.ArticleId,
                Location = job.Kind == PullKind.Reposition ? "Home" : job.Address.ToString(),
                State = job.Phase.ToString(),
                Shuttle = job.Shuttle switch
                {
                    ShuttleId.Shuttle1 => "Shuttle 1",
                    ShuttleId.Shuttle2 => "Shuttle 2",
                    _ => ""
                },
                Crates = crates
            };
        }).ToList();

        var flows = Controller.Articles.Values
            .OrderBy(article => article.Article.Class)
            .ThenBy(article => article.Article.Id, StringComparer.Ordinal)
            .Select(article => new ArticleFlowVisual
            {
                Id = article.Article.Id,
                Name = article.Article.Name,
                ClassName = article.Article.Class.ToString(),
                ColorHex = article.Article.ColorHex,
                InflowCratesPerHour = article.Article.InflowCratesPerHour,
                LostCrates = article.MeasuredLost
            })
            .ToList();

        var events = Controller.Events.TakeLast(80).Reverse().ToList();
        return new WarehouseSnapshot
        {
            TimeSeconds = _now,
            TotalLengthMeters = geometry.TotalLengthMeters,
            TotalHeightMeters = Math.Max(geometry.TotalHeightMeters, geometry.Layout.LevelHeightMeters),
            ShaftPitchMeters = geometry.Layout.ShaftPitchMeters,
            LevelHeightMeters = geometry.Layout.LevelHeightMeters,
            SegmentGapMeters = geometry.Layout.SegmentGapMeters,
            SegmentCount = geometry.SegmentCount,
            Levels = geometry.Levels,
            ShaftsPerLevel = geometry.ShaftsPerLevel,
            SharedZoneLocked = locked,
            AnyShuttleBlocked = shuttles.Any(shuttle => shuttle.IsBlocked),
            Shafts = shafts,
            Shuttles = shuttles,
            Jobs = jobs,
            Events = events,
            ArticleFlows = flows,
            Kpis = kpis
        };
    }
}
