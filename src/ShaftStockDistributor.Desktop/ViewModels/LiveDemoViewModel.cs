using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Desktop.Abstractions;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.ViewModels;

public partial class LiveDemoViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IOptimizerService _optimizer;
    private SimulationSession? _session;
    private DistributorProject? _project;
    private ShaftAddress? _selected;
    private double _lastSample = -1;
    private readonly PalletRateWindow _rate = new();
    private readonly Queue<double> _sampleTimes = new();

    public LiveDemoViewModel(IMessenger messenger, IUiClock clock, IOptimizerService optimizer)
    {
        _messenger = messenger;
        _optimizer = optimizer;
        messenger.Register<ProjectChangedMessage>(this, (_, message) => AcceptProject(message.Project));
        _ = clock.Every(TimeSpan.FromMilliseconds(33), OnTick);
    }

    public double HorizonSeconds { get; set; } = 8 * 3600;

    public ObservableCollection<double> PalletHistory { get; } = new();

    public ObservableCollection<ArticleChoice> ArticleChoices { get; } = new();

    public ObservableCollection<string> Scenarios { get; } = new();

    public ObservableCollection<LegendItem> Legend { get; } = new();

    [ObservableProperty] private WarehouseSnapshot? _snapshot;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _speed = 30;
    [ObservableProperty] private int _seed = 1;
    [ObservableProperty] private bool _showHeatmap;
    [ObservableProperty] private string _status = UiText.DemoHint;
    [ObservableProperty] private string _palletsPerHourText = "—";
    [ObservableProperty] private string _rateCaption = "";
    [ObservableProperty] private string _openingNote = "";
    [ObservableProperty] private string _openingIncludedNote = "";
    [ObservableProperty] private bool _hasOpeningInventory;
    [ObservableProperty] private IReadOnlyList<double> _rateSamples = Array.Empty<double>();
    [ObservableProperty] private double _offeredRate;
    [ObservableProperty] private double _chartSpanMinutes;
    [ObservableProperty] private double _rateWindowMinutes = 20;
    [ObservableProperty] private string _totalPalletsText = "0";
    [ObservableProperty] private string _offeredText = "0.0";
    [ObservableProperty] private string _theoreticalText = "0.0";
    [ObservableProperty] private string _util1Text = "0%";
    [ObservableProperty] private string _util2Text = "0%";
    [ObservableProperty] private string _waitText = "0 s";
    [ObservableProperty] private string _blockedText = "0 s";
    [ObservableProperty] private string _lostText = "0";
    [ObservableProperty] private string _timeText = "00:00:00";
    [ObservableProperty] private string _pullsText = "0 / 0";
    [ObservableProperty] private string _selectedSummary = UiText.ClickShaft;
    [ObservableProperty] private ArticleChoice? _selectedChoice;
    [ObservableProperty] private ShaftAddress? _selectedAddress;
    [ObservableProperty] private bool _selectedPinned;
    [ObservableProperty] private string? _selectedScenario;
    [ObservableProperty] private bool _sharedLocked;
    [ObservableProperty] private bool _shuttleWaiting;

    /// <summary>
    /// When the stored plan points outside the layout or at an unknown article, optimize this project.
    /// Returns true when a new plan was written. The example warehouse is never loaded here.
    /// </summary>
    public async Task<bool> RepairAssignmentIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_project is null || AssignmentFit.MatchesLayout(_project))
        {
            EnsureSession();
            return false;
        }

        IsBusy = true;
        Status = UiText.OptimizerRunning;
        try
        {
            foreach (var slot in _project.Assignments)
            {
                if (!string.IsNullOrEmpty(slot.ArticleId) && _project.FindArticle(slot.ArticleId) is null)
                {
                    slot.ArticleId = null;
                    slot.Pinned = false;
                }
            }

            ProjectGrid.EnsureFullGrid(_project);
            int iterations = Math.Max(1, _project.Optimizer.Iterations);
            var optimized = await _optimizer.OptimizeAsync(_project, iterations, null, cancellationToken);
            ProjectGrid.ReplaceAssignments(_project, optimized.Assignments);
            AssignmentPlan.RememberInflows(_project);
            _messenger.Send(new ProjectChangedMessage { Project = _project });
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Play()
    {
        EnsureSession();
        IsRunning = true;
        Status = UiText.StatusReady;
    }

    [RelayCommand]
    private void Pause() => IsRunning = false;

    [RelayCommand]
    private void ResetPlayback()
    {
        IsRunning = false;
        RestartSession();
        Status = UiText.PlaybackReset;
    }

    [RelayCommand]
    private void Step()
    {
        IsRunning = false;
        EnsureSession();
        if (_session is null)
        {
            return;
        }

        double next = _session.NextEventTime ?? (_session.Now + 1);
        _session.AdvanceTo(Math.Max(_session.Now, next));
        Publish();
    }

    [RelayCommand]
    private void SelectShaft(ShaftAddress address)
    {
        _selected = address;
        SelectedAddress = address;
        var slot = _project?.Assignments.FirstOrDefault(item => item.Address == address);
        var runtime = _session?.Controller.Shafts.FirstOrDefault(item => item.Address == address);
        int count = runtime?.Count ?? 0;
        int capacity = runtime?.Capacity ?? 0;
        SelectedPinned = runtime?.Pinned ?? slot?.Pinned ?? false;
        string? articleId = runtime?.ArticleId ?? slot?.ArticleId;
        SelectedChoice = ArticleChoices.FirstOrDefault(choice => choice.Id == articleId);
        SelectedSummary = $"{address} · {count}/{capacity}";
    }

    [RelayCommand]
    private void ApplyShaft()
    {
        if (_selected is not ShaftAddress address || _project is null)
        {
            return;
        }

        var slot = _project.Assignments.FirstOrDefault(item => item.Address == address);
        if (slot is null)
        {
            return;
        }

        string? articleId = string.IsNullOrEmpty(SelectedChoice?.Id) ? null : SelectedChoice!.Id;
        if (_session is not null)
        {
            bool accepted = _session.Controller.SetShaftAssignment(address, articleId, SelectedPinned);
            if (!accepted && slot.ArticleId != articleId)
            {
                Status = UiText.CannotReassignFilled;
                articleId = slot.ArticleId;
            }
        }

        slot.ArticleId = articleId;
        slot.Pinned = SelectedPinned;
        if (!IsRunning)
        {
            RestartSession();
        }
        else
        {
            Publish();
        }
    }

    [RelayCommand]
    private void ApplyScenario()
    {
        if (_project is null || SelectedScenario is null)
        {
            return;
        }

        var scenario = _project.Scenarios.FirstOrDefault(item => item.Name == SelectedScenario);
        if (scenario is null)
        {
            return;
        }

        InflowAllocator.Apply(_project, scenario);
        IsRunning = false;
        RestartSession();
        Status = UiText.PlaybackReset;
    }

    public void SetMode(AssignmentMode mode)
    {
        _session?.Controller.SetMode(mode, _session.Now);
        if (_project is not null)
        {
            _project.AssignmentMode = mode;
        }
    }

    public void AdvanceSeconds(double seconds)
    {
        EnsureSession();
        if (_session is null)
        {
            return;
        }

        _session.AdvanceTo(_session.Now + Math.Max(0, seconds));
        Publish();
    }

    private void OnTick()
    {
        if (!IsRunning || _session is null)
        {
            return;
        }

        double step = 0.033 * Math.Clamp(Speed, 1, 400);
        double target = Math.Min(HorizonSeconds, _session.Now + step);
        _session.AdvanceTo(target);
        Publish();
        if (_session.Now >= HorizonSeconds)
        {
            IsRunning = false;
        }
    }

    private void AcceptProject(DistributorProject project)
    {
        _project = project;
        Scenarios.Clear();
        foreach (var scenario in project.Scenarios)
        {
            Scenarios.Add(scenario.Name);
        }

        SelectedScenario = project.ActiveScenarioName ?? project.Scenarios.FirstOrDefault()?.Name;
        TheoreticalText = TheoreticalThroughput.PalletsPerHour(project).ToString("0.0");
        ArticleChoices.Clear();
        ArticleChoices.Add(new ArticleChoice { Id = "", Name = UiText.ClearArticle });
        foreach (var article in project.Articles)
        {
            ArticleChoices.Add(new ArticleChoice { Id = article.Id, Name = article.Name });
        }

        IsRunning = false;
        RestartSession();
    }

    private void EnsureSession()
    {
        if (_session is null && _project is not null)
        {
            RestartSession();
        }
    }

    private void RestartSession()
    {
        if (_project is null)
        {
            return;
        }

        _session = SimulationSession.Start(_project.Clone(), new SimulationOptions
        {
            DurationSeconds = HorizonSeconds,
            WarmupSeconds = 0,
            Seed = Seed,
            Mode = _project.AssignmentMode,
            WarmStart = true
        });
        PalletHistory.Clear();
        _sampleTimes.Clear();
        _rate.Clear();
        _lastSample = -1;
        RateSamples = Array.Empty<double>();
        ChartSpanMinutes = 0;
        Publish();
    }

    private void Publish()
    {
        if (_session is null)
        {
            return;
        }

        var snapshot = _session.Capture();
        Snapshot = snapshot;
        var kpis = snapshot.Kpis;
        _rate.Observe(snapshot.TimeSeconds, kpis.SteadyPallets);
        var reading = _rate.Read(snapshot.TimeSeconds, WindowSeconds);
        ApplyRate(reading, kpis);
        SharedLocked = snapshot.SharedZoneLocked;
        ShuttleWaiting = snapshot.AnyShuttleBlocked;
        if (!reading.WarmingUp && (_lastSample < 0 || snapshot.TimeSeconds - _lastSample >= 25))
        {
            _lastSample = snapshot.TimeSeconds;
            PalletHistory.Add(reading.PalletsPerHour);
            _sampleTimes.Enqueue(snapshot.TimeSeconds);
            while (PalletHistory.Count > 80)
            {
                PalletHistory.RemoveAt(0);
                _sampleTimes.Dequeue();
            }

            RateSamples = PalletHistory.ToArray();
            ChartSpanMinutes = _sampleTimes.Count == 0 ? 0 : (snapshot.TimeSeconds - _sampleTimes.Peek()) / 60.0;
        }

        Legend.Clear();
        foreach (var flow in snapshot.ArticleFlows)
        {
            Legend.Add(new LegendItem { Name = flow.Name, ColorHex = flow.ColorHex });
        }

        _messenger.Send(new SimulationUpdatedMessage { Snapshot = snapshot });
    }

    private double WindowSeconds => Math.Clamp(RateWindowMinutes, 5, 180) * 60.0;

    partial void OnRateWindowMinutesChanged(double value)
    {
        double clamped = Math.Clamp(value, 5, 180);
        if (Math.Abs(clamped - value) > 1e-6)
        {
            RateWindowMinutes = clamped;
            return;
        }

        RebuildChart();
        if (_session is not null)
        {
            var reading = _rate.Read(_session.Now, WindowSeconds);
            var kpis = _session.ToResult();
            ApplyRate(reading, kpis);
        }
    }

    private void ApplyRate(RateReading reading, SimulationResult kpis)
    {
        OfferedRate = kpis.OfferedPalletsPerHour;
        OfferedText = kpis.OfferedPalletsPerHour.ToString("0.0", CultureInfo.InvariantCulture);
        TotalPalletsText = kpis.Pallets.ToString("0.0", CultureInfo.InvariantCulture);
        HasOpeningInventory = kpis.OpeningPallets > 0.05;
        OpeningNote = HasOpeningInventory ? UiText.OpeningExcluded : "";
        OpeningIncludedNote = HasOpeningInventory
            ? string.Format(CultureInfo.InvariantCulture, UiText.OpeningIncluded, kpis.OpeningPallets)
            : "";
        if (reading.WarmingUp)
        {
            PalletsPerHourText = UiText.RateWarmingUp;
            RateCaption = string.Format(CultureInfo.InvariantCulture, UiText.RateCumulative, reading.PalletsPerHour);
        }
        else
        {
            PalletsPerHourText = reading.PalletsPerHour.ToString("0.0", CultureInfo.InvariantCulture);
            RateCaption = string.Format(CultureInfo.InvariantCulture, UiText.RateWindowAverage, RateWindowMinutes);
        }

        Util1Text = kpis.Utilization1.ToString("P0", CultureInfo.InvariantCulture);
        Util2Text = kpis.Utilization2.ToString("P0", CultureInfo.InvariantCulture);
        WaitText = $"{kpis.AveragePullWaitSeconds:0} s";
        BlockedText = $"{kpis.BlockedSeconds:0} s";
        LostText = kpis.LostCrates.ToString(CultureInfo.InvariantCulture);
        PullsText = $"{kpis.PullsShuttle1} / {kpis.PullsShuttle2}";
        if (_session is not null)
        {
            TimeText = TimeSpan.FromSeconds(Math.Max(0, _session.Now)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    private void RebuildChart()
    {
        PalletHistory.Clear();
        _sampleTimes.Clear();
        _lastSample = -1;
        double window = WindowSeconds;
        foreach (var point in _rate.Points)
        {
            if (point.Time < window)
            {
                continue;
            }

            if (_lastSample >= 0 && point.Time - _lastSample < 25)
            {
                continue;
            }

            _lastSample = point.Time;
            PalletHistory.Add(_rate.Read(point.Time, window).PalletsPerHour);
            _sampleTimes.Enqueue(point.Time);
        }

        while (PalletHistory.Count > 80)
        {
            PalletHistory.RemoveAt(0);
            _sampleTimes.Dequeue();
        }

        RateSamples = PalletHistory.ToArray();
        ChartSpanMinutes = _sampleTimes.Count == 0 ? 0 : (_sampleTimes.Last() - _sampleTimes.Peek()) / 60.0;
    }
}

public sealed class LegendItem
{
    public required string Name { get; init; }

    public required string ColorHex { get; init; }
}
