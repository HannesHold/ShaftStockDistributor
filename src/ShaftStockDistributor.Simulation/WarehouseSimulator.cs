using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Simulation;

/// <summary>
/// Discrete-event simulation of crate arrivals, shaft filling, and two shuttles.
/// Shared-zone collision avoidance: a shuttle that will touch the shared zone, or the safety margin
/// beside it, must hold the zone lock. Only one shuttle holds that lock. Paths that do not enter the
/// keep-out run freely, but a move is refused when the occupied segment ranges would come within the
/// safety distance. The trace records the reserved segment range of a travelling shuttle, so a shuttle
/// cannot cross a span the other shuttle is using.
/// Horizontal separation is what matters: the shuttles share the rear of the racks and can meet even
/// on different levels. Levels still affect travel time.
/// </summary>
public static class WarehouseSimulator
{
    public static SimulationResult Run(
        Layout layout,
        ArticleSet articles,
        Assignment assignment,
        SimulationOptions? options = null)
    {
        options ??= new SimulationOptions();
        if (options.Hours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Simulation horizon must be positive.");
        }

        return new Engine(layout, articles, assignment, options).Run();
    }

    private enum ShuttleMode
    {
        Idle,
        Traveling,
        Transferring,
        Blocked
    }

    private enum EventKind
    {
        Arrival,
        TravelComplete,
        TransferComplete
    }

    private readonly record struct EventKey(double Time, long Sequence) : IComparable<EventKey>
    {
        public int CompareTo(EventKey other)
        {
            var time = Time.CompareTo(other.Time);
            return time != 0 ? time : Sequence.CompareTo(other.Sequence);
        }
    }

    private readonly record struct SimEvent(EventKind Kind, int Subject);

    private sealed class ShaftState
    {
        public required int Index { get; init; }

        public required Shaft Shaft { get; init; }

        public required string ArticleId { get; init; }

        public int Capacity { get; init; }

        public int Threshold { get; init; }

        public int Count { get; set; }

        public bool PullPending { get; set; }

        public bool Transferring { get; set; }

        public bool OverflowLatched { get; set; }

        public double WaitingSince { get; set; }

        public int Pulls { get; set; }
    }

    private sealed class ShuttleAgent
    {
        public required int Id { get; init; }

        public required ShuttlePose Home { get; init; }

        public ShuttleMode Mode { get; set; }

        public int Segment { get; set; }

        public int Level { get; set; }

        public int Shaft { get; set; }

        public int DestSegment { get; set; }

        public int DestLevel { get; set; }

        public int DestShaft { get; set; }

        public int? Job { get; set; }

        public bool HoldsLock { get; set; }

        public double BlockStarted { get; set; }

        public double OperationStarted { get; set; }

        public double FreeAt { get; set; }

        public double BusySeconds { get; set; }

        public double BlockedSeconds { get; set; }

        public int Pulls { get; set; }

        public List<int> Queue { get; } = [];
    }

    private sealed class Engine
    {
        private readonly Layout _layout;
        private readonly SimulationOptions _options;
        private readonly double _horizon;
        private readonly double _transfer;
        private readonly int _safety;
        private readonly int _keepStart;
        private readonly int _keepEnd;
        private readonly Random _random;
        private readonly PriorityQueue<SimEvent, EventKey> _events = new();
        private readonly List<ShaftState> _shafts = [];
        private readonly Dictionary<string, List<int>> _shaftsByArticle = [];
        private readonly ShuttleAgent _shuttle1;
        private readonly ShuttleAgent _shuttle2;
        private readonly List<ShuttleTraceSample> _trace = [];
        private readonly Dictionary<string, int> _overflows = [];
        private readonly Dictionary<string, int> _cursors = [];
        private long _sequence;
        private int? _lockHolder;
        private int _arrivals;
        private int _accepted;
        private int _rejected;
        private int _collisions;
        private string? _firstCollision;
        private double _waitSum;
        private double _waitMax;
        private int _waitCount;

        public Engine(Layout layout, ArticleSet articles, Assignment assignment, SimulationOptions options)
        {
            _layout = layout;
            _options = options;
            _horizon = options.Hours * 3600.0;
            _transfer = layout.Kinematics.TransferDurationSeconds;
            _safety = layout.SafetyDistanceSegments;
            (_keepStart, _keepEnd) = layout.SharedKeepOut();
            _random = new Random(options.Seed);
            _shuttle1 = CreateShuttle(1, layout.Shuttle1Home);
            _shuttle2 = CreateShuttle(2, layout.Shuttle2Home);

            var index = 0;
            foreach (var shaft in layout.Shafts)
            {
                if (!assignment.TryGet(shaft.Location, out var assigned))
                {
                    continue;
                }

                var article = articles.Find(assigned.ArticleId);
                if (article is null)
                {
                    continue;
                }

                var state = new ShaftState
                {
                    Index = index,
                    Shaft = shaft,
                    ArticleId = article.Id,
                    Capacity = Math.Max(1, layout.EffectiveCapacity(shaft, article)),
                    Threshold = Math.Max(1, article.PullThreshold)
                };
                _shafts.Add(state);
                if (!_shaftsByArticle.TryGetValue(article.Id, out var list))
                {
                    list = [];
                    _shaftsByArticle[article.Id] = list;
                }

                list.Add(index);
                index++;
            }

            var articleIndex = 0;
            foreach (var article in articles.Items.OrderBy(article => article.Id, StringComparer.Ordinal))
            {
                var rate = InflowResolver.Resolve(articles).GetValueOrDefault(article.Id);
                if (rate <= 0)
                {
                    continue;
                }

                Schedule(new SimEvent(EventKind.Arrival, articleIndex), SampleInterarrival(rate));
                articleIndex++;
            }

            _articleOrder = articles.Items
                .Where(article => InflowResolver.Resolve(articles).GetValueOrDefault(article.Id) > 0)
                .OrderBy(article => article.Id, StringComparer.Ordinal)
                .ToList();
            _rates = _articleOrder.ToDictionary(
                article => article.Id,
                article => InflowResolver.Resolve(articles)[article.Id]);
        }

        private readonly List<Article> _articleOrder;
        private readonly Dictionary<string, double> _rates;

        public SimulationResult Run()
        {
            RecordTrace(0);
            NoteCollision(0);

            while (_events.TryDequeue(out var simEvent, out var key))
            {
                if (key.Time > _horizon)
                {
                    break;
                }

                switch (simEvent.Kind)
                {
                    case EventKind.Arrival:
                        OnArrival(simEvent.Subject, key.Time);
                        break;
                    case EventKind.TravelComplete:
                        OnTravelComplete(ShuttleById(simEvent.Subject), key.Time);
                        break;
                    case EventKind.TransferComplete:
                        OnTransferComplete(ShuttleById(simEvent.Subject), key.Time);
                        break;
                }

                NoteCollision(key.Time);
                RecordTrace(key.Time);
                if (_events.Count > 5_000_000)
                {
                    throw new InvalidOperationException("Simulation event queue grew without bound.");
                }
            }

            CloseOpenIntervals(_horizon);

            var horizon = _horizon <= 0 ? 1 : _horizon;
            return new SimulationResult
            {
                Hours = _options.Hours,
                Seed = _options.Seed,
                PullsShuttle1 = _shuttle1.Pulls,
                PullsShuttle2 = _shuttle2.Pulls,
                UtilizationShuttle1 = _shuttle1.BusySeconds / horizon,
                UtilizationShuttle2 = _shuttle2.BusySeconds / horizon,
                BlockedSecondsShuttle1 = _shuttle1.BlockedSeconds,
                BlockedSecondsShuttle2 = _shuttle2.BlockedSeconds,
                BusySecondsShuttle1 = _shuttle1.BusySeconds,
                BusySecondsShuttle2 = _shuttle2.BusySeconds,
                AveragePullWaitSeconds = _waitCount == 0 ? 0 : _waitSum / _waitCount,
                MaxPullWaitSeconds = _waitMax,
                PullCount = _waitCount,
                Arrivals = _arrivals,
                AcceptedCrates = _accepted,
                RejectedCrates = _rejected,
                OverflowEventsByArticle = _overflows,
                ShaftPulls = _shafts.Select(shaft => new ShaftPullKpi(
                    shaft.Shaft.Location.Segment,
                    shaft.Shaft.Location.Level,
                    shaft.Shaft.Location.Shaft,
                    shaft.ArticleId,
                    shaft.Pulls,
                    shaft.Count)).ToList(),
                CollisionViolations = _collisions,
                FirstCollision = _firstCollision,
                Trace = _options.RecordTrace ? _trace : []
            };
        }

        private void OnArrival(int articleIndex, double now)
        {
            if (articleIndex < 0 || articleIndex >= _articleOrder.Count)
            {
                return;
            }

            var article = _articleOrder[articleIndex];
            _arrivals++;
            ShaftState? chosen = null;
            if (_shaftsByArticle.TryGetValue(article.Id, out var indexes))
            {
                chosen = ChooseShaft(article.Id, indexes);
            }

            if (chosen is null)
            {
                _rejected++;
            }
            else
            {
                chosen.Count++;
                _accepted++;
                if (chosen.Count >= chosen.Threshold && !chosen.PullPending && !chosen.Transferring)
                {
                    RequestPull(chosen, now);
                }

                if (chosen.Count >= chosen.Capacity && !chosen.OverflowLatched)
                {
                    chosen.OverflowLatched = true;
                    _overflows[chosen.ArticleId] = _overflows.GetValueOrDefault(chosen.ArticleId) + 1;
                }
            }

            var next = now + SampleInterarrival(_rates[article.Id]);
            if (next <= _horizon)
            {
                Schedule(new SimEvent(EventKind.Arrival, articleIndex), next);
            }
        }

        /// <summary>
        /// Round-robin across shafts that are still filling. A shaft already waiting for a pull is skipped
        /// so its siblings keep receiving. If every shaft is waiting, crates continue round-robin onto
        /// whichever of them still has free space. The cursor makes the split deterministic.
        /// </summary>
        private ShaftState? ChooseShaft(string articleId, List<int> indexes)
        {
            var count = indexes.Count;
            if (count == 0)
            {
                return null;
            }

            var start = _cursors.GetValueOrDefault(articleId);
            ShaftState? fallback = null;
            var fallbackCursor = start;
            for (var step = 0; step < count; step++)
            {
                var position = (start + step) % count;
                var candidate = _shafts[indexes[position]];
                if (candidate.Transferring || candidate.Count >= candidate.Capacity)
                {
                    continue;
                }

                if (!candidate.PullPending)
                {
                    _cursors[articleId] = (position + 1) % count;
                    return candidate;
                }

                if (fallback is null)
                {
                    fallback = candidate;
                    fallbackCursor = (position + 1) % count;
                }
            }

            if (fallback is not null)
            {
                _cursors[articleId] = fallbackCursor;
            }

            return fallback;
        }

        private void RequestPull(ShaftState shaft, double now)
        {
            shaft.PullPending = true;
            shaft.WaitingSince = now;
            var shuttle = ChooseShuttle(shaft, now);
            shuttle.Queue.Add(shaft.Index);
            if (shuttle.Mode is ShuttleMode.Idle or ShuttleMode.Blocked)
            {
                TryDispatch(shuttle, now);
            }
        }

        private ShuttleAgent ChooseShuttle(ShaftState shaft, double now)
        {
            switch (shaft.Shaft.Zone)
            {
                case Zone.Shuttle1Only:
                    return _shuttle1;
                case Zone.Shuttle2Only:
                    return _shuttle2;
                default:
                    var left = AvailabilityScore(_shuttle1, shaft, now);
                    var right = AvailabilityScore(_shuttle2, shaft, now);
                    return left <= right ? _shuttle1 : _shuttle2;
            }
        }

        private double AvailabilityScore(ShuttleAgent shuttle, ShaftState shaft, double now)
        {
            var ready = shuttle.Mode == ShuttleMode.Idle ? now : Math.Max(now, shuttle.FreeAt);
            if (shuttle.Mode == ShuttleMode.Blocked)
            {
                ready = now + _transfer;
            }

            var pose = shuttle.Mode == ShuttleMode.Traveling
                ? new ShuttlePose(shuttle.DestSegment, shuttle.DestLevel, shuttle.DestShaft)
                : new ShuttlePose(shuttle.Segment, shuttle.Level, shuttle.Shaft);
            var travel = _layout.TravelSeconds(pose, shaft.Shaft);
            var backlog = shuttle.Queue.Count * (_transfer + 20);
            return ready + travel + backlog;
        }

        private void TryDispatch(ShuttleAgent shuttle, double now)
        {
            if (shuttle.Mode is ShuttleMode.Traveling or ShuttleMode.Transferring)
            {
                return;
            }

            var ordered = shuttle.Queue
                .OrderBy(index => _layout.TravelSeconds(Pose(shuttle), _shafts[index].Shaft))
                .ThenBy(index => index)
                .ToList();
            foreach (var index in ordered)
            {
                var shaft = _shafts[index];
                var segment = shaft.Shaft.Location.Segment;
                if (!CanStart(shuttle, segment))
                {
                    continue;
                }

                shuttle.Queue.Remove(index);
                StartTravel(shuttle, shaft.Shaft.Location, index, now);
                return;
            }

            if (shuttle.Queue.Count == 0 && !AtHome(shuttle))
            {
                if (CanStart(shuttle, shuttle.Home.Segment))
                {
                    StartTravel(shuttle, shuttle.Home, null, now);
                    return;
                }
            }

            if (shuttle.Queue.Count > 0 || (!AtHome(shuttle) && !CanStart(shuttle, shuttle.Home.Segment)))
            {
                Block(shuttle, now);
                return;
            }

            shuttle.Mode = ShuttleMode.Idle;
        }

        private void StartTravel(ShuttleAgent shuttle, ShaftLocation destination, int? job, double now) =>
            StartTravel(shuttle, new ShuttlePose(destination.Segment, destination.Level, destination.Shaft), job, now);

        private void StartTravel(ShuttleAgent shuttle, ShuttlePose destination, int? job, double now)
        {
            if (shuttle.Mode == ShuttleMode.Blocked)
            {
                shuttle.BlockedSeconds += Math.Max(0, now - shuttle.BlockStarted);
            }

            var origin = Pose(shuttle);
            shuttle.Mode = ShuttleMode.Traveling;
            shuttle.DestSegment = destination.Segment;
            shuttle.DestLevel = destination.Level;
            shuttle.DestShaft = destination.Shaft;
            shuttle.Job = job;
            shuttle.OperationStarted = now;
            var seconds = _layout.TravelSeconds(origin, destination);
            shuttle.FreeAt = now + seconds;

            var released = false;
            if (IntersectsKeepOut(shuttle.Segment, destination.Segment))
            {
                shuttle.HoldsLock = true;
                _lockHolder = shuttle.Id;
            }
            else if (shuttle.HoldsLock)
            {
                shuttle.HoldsLock = false;
                if (_lockHolder == shuttle.Id)
                {
                    _lockHolder = null;
                }

                released = true;
            }

            Schedule(new SimEvent(EventKind.TravelComplete, shuttle.Id), shuttle.FreeAt);
            if (released)
            {
                WakeOther(shuttle, now);
            }
        }

        private void OnTravelComplete(ShuttleAgent shuttle, double now)
        {
            if (shuttle.Mode != ShuttleMode.Traveling)
            {
                return;
            }

            shuttle.BusySeconds += Math.Max(0, now - shuttle.OperationStarted);
            shuttle.Segment = shuttle.DestSegment;
            shuttle.Level = shuttle.DestLevel;
            shuttle.Shaft = shuttle.DestShaft;

            if (shuttle.Job is int job)
            {
                var shaft = _shafts[job];
                shuttle.Mode = ShuttleMode.Transferring;
                shuttle.OperationStarted = now;
                shuttle.FreeAt = now + _transfer;
                shaft.Transferring = true;
                var wait = Math.Max(0, now - shaft.WaitingSince);
                _waitSum += wait;
                _waitCount++;
                if (wait > _waitMax)
                {
                    _waitMax = wait;
                }

                Schedule(new SimEvent(EventKind.TransferComplete, shuttle.Id), shuttle.FreeAt);
                if (OutsideKeepOut(shuttle.Segment) && shuttle.HoldsLock)
                {
                    ReleaseLock(shuttle, now, wake: true);
                }

                return;
            }

            shuttle.Mode = ShuttleMode.Idle;
            shuttle.Job = null;
            TryDispatch(shuttle, now);
            if (shuttle.Mode == ShuttleMode.Idle && OutsideKeepOut(shuttle.Segment) && shuttle.HoldsLock)
            {
                ReleaseLock(shuttle, now, wake: true);
            }
        }

        private void OnTransferComplete(ShuttleAgent shuttle, double now)
        {
            if (shuttle.Mode != ShuttleMode.Transferring || shuttle.Job is not int job)
            {
                return;
            }

            var shaft = _shafts[job];
            shuttle.BusySeconds += Math.Max(0, now - shuttle.OperationStarted);
            shaft.Count = 0;
            shaft.PullPending = false;
            shaft.Transferring = false;
            shaft.OverflowLatched = false;
            shaft.Pulls++;
            shuttle.Pulls++;
            shuttle.Mode = ShuttleMode.Idle;
            shuttle.Job = null;
            TryDispatch(shuttle, now);
            if (shuttle.Mode == ShuttleMode.Idle && OutsideKeepOut(shuttle.Segment) && shuttle.HoldsLock)
            {
                ReleaseLock(shuttle, now, wake: true);
            }
        }

        private bool CanStart(ShuttleAgent shuttle, int destinationSegment)
        {
            if (!LegalSegment(shuttle, destinationSegment))
            {
                return false;
            }

            var proposedStart = Math.Min(shuttle.Segment, destinationSegment);
            var proposedEnd = Math.Max(shuttle.Segment, destinationSegment);
            var other = Other(shuttle);
            var (otherStart, otherEnd) = Occupied(other);
            if (ShuttleClearance.RangesConflict(proposedStart, proposedEnd, otherStart, otherEnd, _safety))
            {
                return false;
            }

            if (!IntersectsKeepOut(shuttle.Segment, destinationSegment))
            {
                return true;
            }

            if (shuttle.HoldsLock)
            {
                return true;
            }

            if (_lockHolder is not null)
            {
                return false;
            }

            return ClearOfKeepOut(other);
        }

        private bool LegalSegment(ShuttleAgent shuttle, int segment)
        {
            if (segment < 0 || segment >= _layout.SegmentCount)
            {
                return false;
            }

            if (_layout.Zones.SharedCount == 0)
            {
                return shuttle.Id == 1
                    ? segment < _layout.Zones.Shuttle2Start
                    : segment >= _layout.Zones.SharedStart;
            }

            return shuttle.Id == 1
                ? segment <= _layout.Zones.SharedEnd
                : segment >= _layout.Zones.SharedStart;
        }

        private void Block(ShuttleAgent shuttle, double now)
        {
            if (shuttle.Mode != ShuttleMode.Blocked)
            {
                shuttle.Mode = ShuttleMode.Blocked;
                shuttle.BlockStarted = now;
            }
        }

        private void ReleaseLock(ShuttleAgent shuttle, double now, bool wake)
        {
            shuttle.HoldsLock = false;
            if (_lockHolder == shuttle.Id)
            {
                _lockHolder = null;
            }

            if (wake)
            {
                WakeOther(shuttle, now);
            }
        }

        private void WakeOther(ShuttleAgent shuttle, double now)
        {
            var other = Other(shuttle);
            if (other.Mode == ShuttleMode.Blocked)
            {
                TryDispatch(other, now);
            }
        }

        private void NoteCollision(double time)
        {
            var (leftStart, leftEnd) = Occupied(_shuttle1);
            var (rightStart, rightEnd) = Occupied(_shuttle2);
            if (!ShuttleClearance.RangesConflict(leftStart, leftEnd, rightStart, rightEnd, _safety))
            {
                return;
            }

            _collisions++;
            _firstCollision ??=
                $"Collision at t={time:0.###}s: shuttle 1 segments {leftStart}-{leftEnd} (level {_shuttle1.Level + 1}), "
                + $"shuttle 2 segments {rightStart}-{rightEnd} (level {_shuttle2.Level + 1}), safety {_safety}.";
        }

        private void RecordTrace(double time)
        {
            if (!_options.RecordTrace)
            {
                return;
            }

            var (leftStart, leftEnd) = Occupied(_shuttle1);
            var (rightStart, rightEnd) = Occupied(_shuttle2);
            _trace.Add(new ShuttleTraceSample(
                time,
                _shuttle1.Segment,
                _shuttle1.Level,
                leftStart,
                leftEnd,
                _shuttle2.Segment,
                _shuttle2.Level,
                rightStart,
                rightEnd));
        }

        private void CloseOpenIntervals(double horizon)
        {
            Close(_shuttle1, horizon);
            Close(_shuttle2, horizon);
        }

        private static void Close(ShuttleAgent shuttle, double horizon)
        {
            if (shuttle.Mode is ShuttleMode.Traveling or ShuttleMode.Transferring)
            {
                shuttle.BusySeconds += Math.Max(0, horizon - shuttle.OperationStarted);
            }
            else if (shuttle.Mode == ShuttleMode.Blocked)
            {
                shuttle.BlockedSeconds += Math.Max(0, horizon - shuttle.BlockStarted);
            }
        }

        private (int Start, int End) Occupied(ShuttleAgent shuttle)
        {
            if (shuttle.Mode == ShuttleMode.Traveling)
            {
                return (Math.Min(shuttle.Segment, shuttle.DestSegment), Math.Max(shuttle.Segment, shuttle.DestSegment));
            }

            return (shuttle.Segment, shuttle.Segment);
        }

        private bool IntersectsKeepOut(int from, int to)
        {
            if (_keepStart > _keepEnd)
            {
                return false;
            }

            var start = Math.Min(from, to);
            var end = Math.Max(from, to);
            return end >= _keepStart && start <= _keepEnd;
        }

        private bool OutsideKeepOut(int segment) =>
            _keepStart > _keepEnd || segment < _keepStart || segment > _keepEnd;

        private bool ClearOfKeepOut(ShuttleAgent shuttle)
        {
            var (start, end) = Occupied(shuttle);
            if (_keepStart > _keepEnd)
            {
                return true;
            }

            return end < _keepStart || start > _keepEnd;
        }

        private bool AtHome(ShuttleAgent shuttle) =>
            shuttle.Segment == shuttle.Home.Segment
            && shuttle.Level == shuttle.Home.Level
            && shuttle.Shaft == shuttle.Home.Shaft;

        private ShuttleAgent Other(ShuttleAgent shuttle) => shuttle.Id == 1 ? _shuttle2 : _shuttle1;

        private ShuttleAgent ShuttleById(int id) => id == 1 ? _shuttle1 : _shuttle2;

        private static ShuttlePose Pose(ShuttleAgent shuttle) => new(shuttle.Segment, shuttle.Level, shuttle.Shaft);

        private static ShuttleAgent CreateShuttle(int id, ShuttlePose home) => new()
        {
            Id = id,
            Home = home,
            Mode = ShuttleMode.Idle,
            Segment = home.Segment,
            Level = home.Level,
            Shaft = home.Shaft,
            DestSegment = home.Segment,
            DestLevel = home.Level,
            DestShaft = home.Shaft
        };

        private void Schedule(SimEvent simEvent, double time)
        {
            _sequence++;
            _events.Enqueue(simEvent, new EventKey(time, _sequence));
        }

        private double SampleInterarrival(double cratesPerHour)
        {
            var unit = 1.0 - _random.NextDouble();
            if (unit < 1e-12)
            {
                unit = 1e-12;
            }

            return -Math.Log(unit) * (3600.0 / cratesPerHour);
        }
    }
}
