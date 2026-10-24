using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Operations;

/// <summary>
/// Decides pulls, reserves the shared aisle, and may retarget an empty shaft.
/// The simulation does not pull on its own; it only delivers arrivals and hardware callbacks to this class.
/// </summary>
public sealed class OperationsController
{
    private readonly DistributorProject _project;
    private readonly LayoutGeometry _geometry;
    private readonly IWarehouseHardware _hardware;
    private readonly Func<double> _clock;
    private readonly Dictionary<string, ArticleRuntime> _articles;
    private readonly Dictionary<ShaftAddress, ShaftRuntime> _byAddress;
    private readonly List<PullJob> _jobs = new();
    private readonly List<string> _events = new();
    private readonly Queue<double> _reassignmentTimes = new();
    private readonly ZoneInterlock _interlock;
    private readonly double _warmup;
    private int _sequence;
    private ShuttleId _preferred = ShuttleId.Shuttle1;
    private double _waitSum;
    private int _waitCount;
    private double _waitMax;

    public OperationsController(
        DistributorProject project,
        IWarehouseHardware hardware,
        Func<double> clock,
        double warmupSeconds = 0,
        AssignmentMode? mode = null)
    {
        _project = project;
        _geometry = new LayoutGeometry(project.Layout);
        _hardware = hardware;
        _clock = clock;
        _warmup = Math.Max(0, warmupSeconds);
        Mode = mode ?? project.AssignmentMode;
        _interlock = new ZoneInterlock(project.Layout.SafetyDistanceMeters);
        _articles = project.Articles.ToDictionary(
            article => article.Id,
            article => new ArticleRuntime { Article = article },
            StringComparer.Ordinal);

        var slots = ProjectGrid.Materialize(project);
        Shafts = new List<ShaftRuntime>(slots.Count);
        foreach (var slot in slots)
        {
            var zone = _geometry.ZoneOfSegment(slot.Segment);
            var shaft = new ShaftRuntime
            {
                Address = slot.Address,
                Zone = zone,
                X = _geometry.ShaftX(slot.Segment, slot.Shaft),
                Y = _geometry.LevelY(slot.Level),
                ArticleId = slot.ArticleId,
                Pinned = slot.Pinned
            };
            ApplyCapacity(shaft);
            Shafts.Add(shaft);
        }

        _byAddress = Shafts.ToDictionary(shaft => shaft.Address);
        Shuttles = project.Shuttles
            .OrderBy(shuttle => shuttle.Id)
            .Select(shuttle => new ShuttleRuntime
            {
                Id = shuttle.Id,
                X = shuttle.Home.XMeters,
                Y = _geometry.LevelY(Math.Clamp(shuttle.Home.Level, 1, Math.Max(1, _geometry.Levels))),
                Level = Math.Clamp(shuttle.Home.Level, 1, Math.Max(1, _geometry.Levels))
            })
            .ToList();

        foreach (var shuttle in Shuttles)
        {
            _interlock.Hold(shuttle.Id, Interval.Point(shuttle.X));
        }

        hardware.JobCompleted += OnHardwareCompleted;
    }

    public AssignmentMode Mode { get; private set; }

    public LayoutGeometry Geometry => _geometry;

    public DistributorProject Project => _project;

    public List<ShaftRuntime> Shafts { get; }

    public List<ShuttleRuntime> Shuttles { get; }

    public ZoneInterlock Interlock => _interlock;

    public IReadOnlyDictionary<string, ArticleRuntime> Articles => _articles;

    public IReadOnlyList<string> Events => _events;

    public int PullJobsSent { get; private set; }

    public int ReassignmentCount { get; private set; }

    public double MeasuredPallets => _articles.Values.Sum(article => article.MeasuredPallets);

    public int MeasuredLost => _articles.Values.Sum(article => article.MeasuredLost);

    public int WaitSamples => _waitCount;

    public double WaitAverage => _waitCount == 0 ? 0 : _waitSum / _waitCount;

    public double WaitMax => _waitMax;

    public IReadOnlyList<PullJob> OpenJobs => _jobs
        .Where(job => job.Phase is JobPhase.Pending or JobPhase.Traveling or JobPhase.Transferring)
        .ToList();

    public bool TryAcceptCrate(string articleId, double now)
    {
        if (!_articles.TryGetValue(articleId, out var article))
        {
            return false;
        }

        article.Arrivals.Enqueue(now);
        TrimArrivals(article, now);
        var shaft = SelectShaft(articleId);
        if (shaft is null)
        {
            article.MeasuredLost += now >= _warmup ? 1 : 0;
            foreach (var full in Shafts.Where(candidate => candidate.ArticleId == articleId && candidate.Capacity > 0 && candidate.Count >= candidate.Capacity))
            {
                if (now >= _warmup)
                {
                    full.MeasuredOverflow = true;
                }
            }

            Log(now, $"Rejected a crate of {article.Article.Name}. Every assigned shaft is full or locked.");
            return false;
        }

        shaft.Count++;
        MaybeEnqueue(shaft, now);
        Dispatch(now);
        return true;
    }

    public void OnShaftCountReported(ShaftAddress address, int count, double now)
    {
        if (!_byAddress.TryGetValue(address, out var shaft) || shaft.TransferLocked || shaft.ArticleId is null)
        {
            return;
        }

        if (count < 0)
        {
            count = 0;
        }

        if (shaft.Capacity > 0 && count > shaft.Capacity)
        {
            int lost = count - shaft.Capacity;
            count = shaft.Capacity;
            if (_articles.TryGetValue(shaft.ArticleId, out var article) && now >= _warmup)
            {
                article.MeasuredLost += lost;
                shaft.MeasuredOverflow = true;
            }
        }

        shaft.Count = count;
        MaybeEnqueue(shaft, now);
        Dispatch(now);
    }

    public void SetMode(AssignmentMode mode, double now)
    {
        Mode = mode;
        Log(now, mode == AssignmentMode.Automatic
            ? "Assignment mode is automatic. Only empty shafts can change, and pinned shafts stay put."
            : "Assignment mode is manual. The stored assignment is kept.");
    }

    public bool SetShaftAssignment(ShaftAddress address, string? articleId, bool pinned)
    {
        if (!_byAddress.TryGetValue(address, out var shaft))
        {
            return false;
        }

        if (shaft.TransferLocked && shaft.ArticleId != articleId)
        {
            return false;
        }

        if (shaft.Count > 0 && shaft.ArticleId != articleId)
        {
            shaft.Pinned = pinned;
            return false;
        }

        shaft.Pinned = pinned;
        if (shaft.ArticleId != articleId)
        {
            shaft.ArticleId = articleId;
            ApplyCapacity(shaft);
            shaft.TriggerTime = null;
        }

        return true;
    }

    public bool TryReassignShaft(ShaftAddress address, double now)
    {
        if (!_byAddress.TryGetValue(address, out var shaft))
        {
            return false;
        }

        string? before = shaft.ArticleId;
        ApplyPolicy(shaft, now);
        return shaft.ArticleId != before;
    }

    public void Dispatch(double now)
    {
        var ordered = Shuttles
            .Where(shuttle => shuttle.Phase is not ShuttlePhase.Traveling and not ShuttlePhase.Transferring)
            .OrderBy(shuttle => shuttle.BusySeconds)
            .ThenBy(shuttle => shuttle.Id == _preferred ? 0 : 1)
            .ThenBy(shuttle => shuttle.Id)
            .ToList();

        foreach (var shuttle in ordered)
        {
            if (shuttle.Phase is ShuttlePhase.Traveling or ShuttlePhase.Transferring)
            {
                continue;
            }

            bool otherBlocked = Other(shuttle).Phase == ShuttlePhase.Blocked;
            if (otherBlocked && InsideShared(shuttle.X) && TryYield(shuttle, now))
            {
                continue;
            }

            var job = SelectJob(shuttle, now, skipShared: otherBlocked);
            if (job is not null)
            {
                StartPull(shuttle, job, now);
                if (_byAddress.TryGetValue(job.Address, out var shaft) && shaft.Zone == ZoneKind.Shared)
                {
                    _preferred = Other(shuttle).Id;
                }

                continue;
            }

            if (HasOnlyConflictingWork(shuttle))
            {
                MarkBlocked(shuttle, now);
                continue;
            }

            ClearBlocked(shuttle, now);
            if (TryYield(shuttle, now))
            {
                continue;
            }

            shuttle.Phase = ShuttlePhase.Idle;
        }
    }

    public double BusySecondsAt(ShuttleId id, double now)
    {
        var shuttle = Shuttle(id);
        double extra = shuttle.Phase switch
        {
            ShuttlePhase.Traveling => Measure(shuttle.MotionStart, now),
            ShuttlePhase.Transferring => Measure(ActiveTransferStart(shuttle), now),
            _ => 0
        };
        return shuttle.BusySeconds + extra;
    }

    public double BlockedSecondsAt(double now)
    {
        double extra = 0;
        foreach (var shuttle in Shuttles)
        {
            if (shuttle.BlockedSince is double since)
            {
                extra += Measure(since, now);
            }
        }

        return Shuttles.Sum(shuttle => shuttle.BlockedSeconds) + extra;
    }

    public double ObservedPerHour(string articleId, double now)
    {
        if (!_articles.TryGetValue(articleId, out var article))
        {
            return 0;
        }

        TrimArrivals(article, now);
        double window = Math.Max(1, _project.Operation.ObservationWindowSeconds);
        if (now < 1 || article.Arrivals.Count == 0)
        {
            return article.Article.InflowCratesPerHour;
        }

        double elapsed = Math.Min(window, Math.Max(now, 1));
        return article.Arrivals.Count / elapsed * 3600.0;
    }

    private void OnHardwareCompleted(object? sender, JobResultEvent e) => OnJobStage(e, _clock());

    private void OnJobStage(JobResultEvent result, double now)
    {
        var job = _jobs.FirstOrDefault(candidate => candidate.Id == result.JobId);
        if (job is null)
        {
            return;
        }

        switch (result.Stage)
        {
            case HardwareStage.Failed:
                Fail(job, now, result.FailureReason ?? "The plant reported a failure.");
                break;
            case HardwareStage.ArrivedAtShaft:
                Arrive(job, now);
                break;
            case HardwareStage.TransferFinished:
                Finish(job, now);
                break;
        }
    }

    private void Arrive(PullJob job, double now)
    {
        if (job.Phase != JobPhase.Traveling || job.Shuttle is not ShuttleId shuttleId)
        {
            return;
        }

        var shuttle = Shuttle(shuttleId);
        shuttle.X = job.ToX;
        shuttle.Y = job.ToY;
        shuttle.Level = job.ToLevel;
        shuttle.InMotion = false;
        shuttle.BusySeconds += Measure(shuttle.MotionStart, now);
        _interlock.Hold(shuttle.Id, Interval.Point(shuttle.X));

        if (job.Kind == PullKind.Reposition)
        {
            job.Phase = JobPhase.Completed;
            shuttle.Phase = ShuttlePhase.Idle;
            shuttle.JobId = null;
            Log(now, $"{Name(shuttle.Id)} is back at its home position and has released the shared zone.");
            Dispatch(now);
            return;
        }

        if (!_byAddress.TryGetValue(job.Address, out var shaft))
        {
            return;
        }

        shaft.TransferLocked = true;
        shaft.FrozenDischarge = shaft.Count;
        shaft.TransferStart = now;
        shaft.TransferEnd = now + Math.Max(0, _project.Layout.TransferDurationSeconds);
        shuttle.Phase = ShuttlePhase.Transferring;
        job.Phase = JobPhase.Transferring;
        if (shaft.TriggerTime is double triggered && now >= _warmup)
        {
            double wait = Math.Max(0, now - triggered);
            _waitSum += wait;
            _waitCount++;
            if (wait > _waitMax)
            {
                _waitMax = wait;
            }
        }

        Log(now, $"{Name(shuttle.Id)} arrived at {shaft.Address} and is pulling {shaft.FrozenDischarge} crates.");
        Dispatch(now);
    }

    private void Finish(PullJob job, double now)
    {
        if (job.Phase != JobPhase.Transferring || job.Shuttle is not ShuttleId shuttleId)
        {
            return;
        }

        if (!_byAddress.TryGetValue(job.Address, out var shaft))
        {
            return;
        }

        var shuttle = Shuttle(shuttleId);
        int removed = Math.Min(shaft.FrozenDischarge, shaft.Count);
        if (_articles.TryGetValue(job.ArticleId, out var article))
        {
            double pallets = article.Article.CratesPerPallet > 0
                ? removed / (double)article.Article.CratesPerPallet
                : 0;
            if (now >= _warmup)
            {
                article.MeasuredPallets += pallets;
            }
        }

        shuttle.BusySeconds += Measure(shaft.TransferStart, now);
        shaft.Count = 0;
        shaft.FrozenDischarge = 0;
        shaft.TransferLocked = false;
        shaft.TriggerTime = null;
        shaft.ActiveJobId = null;
        shaft.PullCount++;
        if (now >= _warmup)
        {
            shaft.MeasuredPullCount++;
            shuttle.MeasuredPulls++;
        }

        shuttle.Pulls++;
        shuttle.Phase = ShuttlePhase.Idle;
        shuttle.JobId = null;
        shuttle.InMotion = false;
        job.Phase = JobPhase.Completed;
        _interlock.Hold(shuttle.Id, Interval.Point(shuttle.X));
        TrimJobs();
        Log(now, $"Pull finished at {shaft.Address}. {removed} crates left the shaft.");
        ApplyPolicy(shaft, now);
        Dispatch(now);
    }

    private void Fail(PullJob job, double now, string reason)
    {
        if (job.Shuttle is ShuttleId shuttleId)
        {
            var shuttle = Shuttle(shuttleId);
            ClearBlocked(shuttle, now);
            shuttle.Phase = ShuttlePhase.Idle;
            shuttle.JobId = null;
            shuttle.InMotion = false;
            _interlock.Hold(shuttle.Id, Interval.Point(shuttle.X));
        }

        if (_byAddress.TryGetValue(job.Address, out var shaft))
        {
            shaft.TransferLocked = false;
            shaft.ActiveJobId = null;
        }

        job.Phase = JobPhase.Failed;
        Log(now, $"Pull {job.Id} failed. {reason}");
        Dispatch(now);
    }

    private PullJob? SelectJob(ShuttleRuntime shuttle, double now, bool skipShared)
    {
        foreach (var job in Pending(now))
        {
            if (!_byAddress.TryGetValue(job.Address, out var shaft))
            {
                continue;
            }

            if (!_geometry.ShuttleCanServe(shuttle.Id, shaft.Zone))
            {
                continue;
            }

            if (skipShared && shaft.Zone == ZoneKind.Shared)
            {
                continue;
            }

            if (_interlock.Conflicts(shuttle.Id, Interval.Span(shuttle.X, shaft.X)))
            {
                continue;
            }

            return job;
        }

        return null;
    }

    private bool HasOnlyConflictingWork(ShuttleRuntime shuttle)
    {
        bool any = false;
        foreach (var job in _jobs.Where(job => job.Phase == JobPhase.Pending))
        {
            if (!_byAddress.TryGetValue(job.Address, out var shaft) || !_geometry.ShuttleCanServe(shuttle.Id, shaft.Zone))
            {
                continue;
            }

            any = true;
            if (!_interlock.Conflicts(shuttle.Id, Interval.Span(shuttle.X, shaft.X)))
            {
                return false;
            }
        }

        return any;
    }

    private void StartPull(ShuttleRuntime shuttle, PullJob job, double now)
    {
        var shaft = _byAddress[job.Address];
        ClearBlocked(shuttle, now);
        var sweep = Interval.Span(shuttle.X, shaft.X);
        _interlock.Hold(shuttle.Id, sweep);
        double travel = Travel(shuttle, shaft.X, shaft.Y);
        job.Phase = JobPhase.Traveling;
        job.Shuttle = shuttle.Id;
        job.ToX = shaft.X;
        job.ToY = shaft.Y;
        job.ToLevel = shaft.Address.Level;
        shuttle.Phase = ShuttlePhase.Traveling;
        shuttle.JobId = job.Id;
        ArmMotion(shuttle, now, shaft.X, shaft.Y);
        PullJobsSent++;
        Log(now, $"{Name(shuttle.Id)} is traveling to {shaft.Address} for {ArticleName(job.ArticleId)}.");
        _hardware.SendPullJob(new PullJobCommand
        {
            JobId = job.Id,
            Kind = PullKind.Discharge,
            Shuttle = shuttle.Id,
            Shaft = shaft.Address,
            TravelSeconds = travel,
            TransferSeconds = Math.Max(0, _project.Layout.TransferDurationSeconds),
            FromX = shuttle.MotionFromX,
            FromY = shuttle.MotionFromY,
            ToX = shaft.X,
            ToY = shaft.Y,
            ToLevel = shaft.Address.Level
        });
    }

    private bool TryYield(ShuttleRuntime shuttle, double now)
    {
        var settings = _project.Shuttle(shuttle.Id);
        if (!InsideShared(shuttle.X) || Math.Abs(shuttle.X - settings.Home.XMeters) < 0.05)
        {
            return false;
        }

        var other = Other(shuttle);
        bool otherNeedsShared = _jobs.Any(job =>
            job.Phase == JobPhase.Pending
            && _byAddress.TryGetValue(job.Address, out var shaft)
            && shaft.Zone == ZoneKind.Shared
            && _geometry.ShuttleCanServe(other.Id, shaft.Zone)
            && _interlock.Conflicts(other.Id, Interval.Span(other.X, shaft.X)));
        if (!otherNeedsShared && other.Phase != ShuttlePhase.Blocked)
        {
            return false;
        }

        double homeY = _geometry.LevelY(Math.Clamp(settings.Home.Level, 1, Math.Max(1, _geometry.Levels)));
        var sweep = Interval.Span(shuttle.X, settings.Home.XMeters);
        if (_interlock.Conflicts(shuttle.Id, sweep))
        {
            return false;
        }

        var job = new PullJob
        {
            Id = $"Y{++_sequence}",
            Kind = PullKind.Reposition,
            Phase = JobPhase.Traveling,
            Shuttle = shuttle.Id,
            Created = now,
            ToX = settings.Home.XMeters,
            ToY = homeY,
            ToLevel = settings.Home.Level
        };
        _jobs.Add(job);
        ClearBlocked(shuttle, now);
        _interlock.Hold(shuttle.Id, sweep);
        double travel = Travel(shuttle, settings.Home.XMeters, homeY);
        shuttle.Phase = ShuttlePhase.Traveling;
        shuttle.JobId = job.Id;
        ArmMotion(shuttle, now, settings.Home.XMeters, homeY);
        PullJobsSent++;
        Log(now, $"{Name(shuttle.Id)} is leaving the shared zone so the other shuttle can enter.");
        _hardware.SendPullJob(new PullJobCommand
        {
            JobId = job.Id,
            Kind = PullKind.Reposition,
            Shuttle = shuttle.Id,
            TravelSeconds = travel,
            TransferSeconds = 0,
            FromX = shuttle.MotionFromX,
            FromY = shuttle.MotionFromY,
            ToX = settings.Home.XMeters,
            ToY = homeY,
            ToLevel = settings.Home.Level
        });
        return true;
    }

    private void MaybeEnqueue(ShaftRuntime shaft, double now)
    {
        if (shaft.ArticleId is null || shaft.ActiveJobId is not null || shaft.TransferLocked)
        {
            return;
        }

        if (!_articles.TryGetValue(shaft.ArticleId, out var article))
        {
            return;
        }

        if (shaft.Count < article.Article.PullTriggerCrates)
        {
            return;
        }

        shaft.TriggerTime ??= now;
        var job = new PullJob
        {
            Id = $"P{++_sequence}",
            Kind = PullKind.Discharge,
            Address = shaft.Address,
            ArticleId = shaft.ArticleId,
            Phase = JobPhase.Pending,
            Created = now
        };
        shaft.ActiveJobId = job.Id;
        _jobs.Add(job);
        Log(now, $"Shaft {shaft.Address} reached the pull threshold for {article.Article.Name} ({shaft.Count} crates).");
    }

    private void ApplyPolicy(ShaftRuntime shaft, double now)
    {
        int recent = ReassignmentsInLastHour(now);
        string? next = AutomaticAssignmentPolicy.Suggest(
            shaft,
            Shafts,
            _articles,
            _project.Operation,
            Mode,
            now,
            id => ObservedPerHour(id, now),
            recent);
        if (next is null || next == shaft.ArticleId)
        {
            return;
        }

        string previous = shaft.ArticleId ?? "(empty)";
        shaft.ArticleId = next;
        ApplyCapacity(shaft);
        shaft.ReassignNotBefore = now + Math.Max(0, _project.Operation.ReassignmentCooldownSeconds);
        _reassignmentTimes.Enqueue(now);
        ReassignmentCount++;
        Log(now, $"Empty shaft {shaft.Address} changed from {previous} to {ArticleName(next)}.");
    }

    private ShaftRuntime? SelectShaft(string articleId)
    {
        return Shafts
            .Where(shaft => shaft.ArticleId == articleId && !shaft.TransferLocked && shaft.Count < shaft.Capacity)
            .OrderBy(shaft => shaft.Capacity <= 0 ? 1 : shaft.Count / (double)shaft.Capacity)
            .ThenBy(shaft => shaft.Count)
            .ThenBy(shaft => shaft.Address)
            .FirstOrDefault();
    }

    private IEnumerable<PullJob> Pending(double now) =>
        _jobs.Where(job => job.Phase == JobPhase.Pending)
            .OrderByDescending(job => Priority(job, now))
            .ThenBy(job => job.Created)
            .ThenBy(job => job.Id, StringComparer.Ordinal);

    private double Priority(PullJob job, double now)
    {
        if (!_byAddress.TryGetValue(job.Address, out var shaft) || !_articles.TryGetValue(job.ArticleId, out var article))
        {
            return 0;
        }

        double fill = shaft.Capacity <= 0 ? 1 : shaft.Count / (double)shaft.Capacity;
        double abc = article.Article.Class switch
        {
            AbcClass.A => 3,
            AbcClass.B => 2,
            _ => 1
        };
        double overflow = shaft.Count >= Math.Max(1, shaft.Capacity) ? 1 : 0;
        return overflow * 1000 + fill * 100 + abc * 10 + Math.Max(0, now - job.Created) * 0.01;
    }

    private void ApplyCapacity(ShaftRuntime shaft)
    {
        shaft.Capacity = shaft.ArticleId is not null && _articles.TryGetValue(shaft.ArticleId, out var article)
            ? _geometry.CapacityFor(article.Article)
            : 0;
    }

    private void ArmMotion(ShuttleRuntime shuttle, double now, double toX, double toY)
    {
        shuttle.InMotion = true;
        shuttle.MotionStart = now;
        shuttle.MotionFromX = shuttle.X;
        shuttle.MotionFromY = shuttle.Y;
        shuttle.MotionToX = toX;
        shuttle.MotionToY = toY;
    }

    private double Travel(ShuttleRuntime shuttle, double toX, double toY)
    {
        var settings = _project.Shuttle(shuttle.Id);
        return TravelTime.Simultaneous(toX - shuttle.X, toY - shuttle.Y, settings);
    }

    private double ActiveTransferStart(ShuttleRuntime shuttle)
    {
        var job = _jobs.FirstOrDefault(candidate => candidate.Id == shuttle.JobId);
        if (job is not null && _byAddress.TryGetValue(job.Address, out var shaft))
        {
            return shaft.TransferStart;
        }

        return shuttle.MotionStart;
    }

    private bool InsideShared(double x)
    {
        var span = _geometry.ZoneSpan(ZoneKind.Shared);
        return span.End > span.Start && x >= span.Start && x <= span.End;
    }

    private void MarkBlocked(ShuttleRuntime shuttle, double now)
    {
        if (shuttle.BlockedSince is null)
        {
            shuttle.BlockedSince = now;
            Log(now, $"{Name(shuttle.Id)} is waiting. The shared zone is reserved by the other shuttle.");
        }

        shuttle.Phase = ShuttlePhase.Blocked;
    }

    private void ClearBlocked(ShuttleRuntime shuttle, double now)
    {
        if (shuttle.BlockedSince is double since)
        {
            shuttle.BlockedSeconds += Measure(since, now);
            shuttle.BlockedSince = null;
        }
    }

    private double Measure(double start, double end)
    {
        double from = Math.Max(start, _warmup);
        return end > from ? end - from : 0;
    }

    private int ReassignmentsInLastHour(double now)
    {
        double cutoff = now - 3600;
        while (_reassignmentTimes.Count > 0 && _reassignmentTimes.Peek() < cutoff)
        {
            _reassignmentTimes.Dequeue();
        }

        return _reassignmentTimes.Count;
    }

    private void TrimArrivals(ArticleRuntime article, double now)
    {
        double cutoff = now - Math.Max(1, _project.Operation.ObservationWindowSeconds);
        while (article.Arrivals.Count > 0 && article.Arrivals.Peek() < cutoff)
        {
            article.Arrivals.Dequeue();
        }
    }

    private void TrimJobs()
    {
        int completed = _jobs.Count(job => job.Phase is JobPhase.Completed or JobPhase.Failed);
        if (completed <= 40)
        {
            return;
        }

        int extra = completed - 40;
        _jobs.RemoveAll(job => extra-- > 0 && job.Phase is JobPhase.Completed or JobPhase.Failed);
    }

    private ShuttleRuntime Shuttle(ShuttleId id) => Shuttles.First(shuttle => shuttle.Id == id);

    private ShuttleRuntime Other(ShuttleRuntime shuttle) =>
        Shuttle(shuttle.Id == ShuttleId.Shuttle1 ? ShuttleId.Shuttle2 : ShuttleId.Shuttle1);

    private static string Name(ShuttleId id) => id == ShuttleId.Shuttle1 ? "Shuttle 1" : "Shuttle 2";

    private string ArticleName(string? id) =>
        id is not null && _articles.TryGetValue(id, out var article) ? article.Article.Name : id ?? "(empty)";

    private void Log(double now, string message)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, now));
        _events.Add($"{time:hh\\:mm\\:ss}  {message}");
        if (_events.Count > 400)
        {
            _events.RemoveRange(0, _events.Count - 400);
        }
    }
}
