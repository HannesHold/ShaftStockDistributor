using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Desktop.Abstractions;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;
using SkiaSharp;

namespace ShaftStockDistributor.Desktop.ViewModels;

public partial class LiveDemoViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IOptimizerService _optimizer;
    private readonly IProjectStore _store;
    private SimulationSession? _session;
    private DistributorProject? _project;
    private ShaftAddress? _selected;
    private double _lastSample = -1;

    public LiveDemoViewModel(IMessenger messenger, IUiClock clock, IOptimizerService optimizer, IProjectStore store)
    {
        _messenger = messenger;
        _optimizer = optimizer;
        _store = store;
        PalletSeries = new ISeries[]
        {
            Line("Pallets", new SKColor(76, 141, 255), PalletHistory)
        };
        UtilSeries = new ISeries[]
        {
            Line(UiText.Shuttle1, new SKColor(76, 141, 255), Util1History),
            Line(UiText.Shuttle2, new SKColor(240, 162, 2), Util2History)
        };
        TimeAxes = new[] { Axis(UiText.SimTime) };
        PalletAxes = new[] { Axis(UiText.PalletsPerHour) };
        UtilAxes = new[] { Axis("%") };
        messenger.Register<ProjectChangedMessage>(this, (_, message) => AcceptProject(message.Project));
        _ = clock.Every(TimeSpan.FromMilliseconds(33), OnTick);
    }

    public double HorizonSeconds { get; set; } = 8 * 3600;

    public ObservableCollection<double> PalletHistory { get; } = new();

    public ObservableCollection<double> Util1History { get; } = new();

    public ObservableCollection<double> Util2History { get; } = new();

    public ObservableCollection<ArticleChoice> ArticleChoices { get; } = new();

    public ObservableCollection<string> Scenarios { get; } = new();

    public ISeries[] PalletSeries { get; }

    public ISeries[] UtilSeries { get; }

    public Axis[] TimeAxes { get; }

    public Axis[] PalletAxes { get; }

    public Axis[] UtilAxes { get; }

    [ObservableProperty] private WarehouseSnapshot? _snapshot;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _speed = 30;
    [ObservableProperty] private int _seed = 1;
    [ObservableProperty] private bool _showHeatmap;
    [ObservableProperty] private string _status = UiText.DemoHint;
    [ObservableProperty] private string _palletsPerHourText = "0.0";
    [ObservableProperty] private string _totalPalletsText = "0";
    [ObservableProperty] private string _offeredText = "0.0";
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

    public async Task<DistributorProject> BuildDemoAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        Status = UiText.OptimizerRunning;
        try
        {
            var project = _store.CreateExample();
            var optimized = await _optimizer.OptimizeAsync(project, 180, null, cancellationToken);
            ProjectGrid.ReplaceAssignments(project, optimized.Assignments);
            return project;
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
            Mode = _project.AssignmentMode
        });
        PalletHistory.Clear();
        Util1History.Clear();
        Util2History.Clear();
        _lastSample = -1;
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
        PalletsPerHourText = kpis.PalletsPerHour.ToString("0.0");
        TotalPalletsText = kpis.Pallets.ToString("0.0");
        OfferedText = kpis.OfferedPalletsPerHour.ToString("0.0");
        Util1Text = kpis.Utilization1.ToString("P0");
        Util2Text = kpis.Utilization2.ToString("P0");
        WaitText = $"{kpis.AveragePullWaitSeconds:0} s";
        BlockedText = $"{kpis.BlockedSeconds:0} s";
        LostText = kpis.LostCrates.ToString();
        PullsText = $"{kpis.PullsShuttle1} / {kpis.PullsShuttle2}";
        TimeText = TimeSpan.FromSeconds(Math.Max(0, snapshot.TimeSeconds)).ToString(@"hh\:mm\:ss");
        SharedLocked = snapshot.SharedZoneLocked;
        ShuttleWaiting = snapshot.AnyShuttleBlocked;
        if (_lastSample < 0 || snapshot.TimeSeconds - _lastSample >= 60)
        {
            _lastSample = snapshot.TimeSeconds;
            PalletHistory.Add(kpis.PalletsPerHour);
            Util1History.Add(kpis.Utilization1 * 100);
            Util2History.Add(kpis.Utilization2 * 100);
            Trim(PalletHistory);
            Trim(Util1History);
            Trim(Util2History);
        }

        _messenger.Send(new SimulationUpdatedMessage { Snapshot = snapshot });
    }

    private static void Trim(ObservableCollection<double> values)
    {
        while (values.Count > 80)
        {
            values.RemoveAt(0);
        }
    }

    private static LineSeries<double> Line(string name, SKColor color, ObservableCollection<double> values) => new()
    {
        Name = name,
        Values = values,
        GeometrySize = 0,
        Fill = null,
        LineSmoothness = 0.3,
        Stroke = new SolidColorPaint(color, 2)
    };

    private static Axis Axis(string name) => new()
    {
        Name = name,
        NamePaint = new SolidColorPaint(new SKColor(180, 196, 214)),
        LabelsPaint = new SolidColorPaint(new SKColor(154, 171, 188)),
        SeparatorsPaint = new SolidColorPaint(new SKColor(46, 58, 74)),
        TextSize = 11,
        NameTextSize = 12
    };
}
