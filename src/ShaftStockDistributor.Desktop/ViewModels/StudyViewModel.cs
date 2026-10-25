using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Desktop.Abstractions;
using ShaftStockDistributor.Simulation;
using SkiaSharp;

namespace ShaftStockDistributor.Desktop.ViewModels;

public sealed class StudyRow
{
    public required string Scenario { get; init; }
    public required string Mode { get; init; }
    public required string Load { get; init; }
    public required string Offered { get; init; }
    public required string Achieved { get; init; }
    public required string Utilization { get; init; }
    public required string Lost { get; init; }
    public required string Blocked { get; init; }
}

public sealed class SaturationRow
{
    public required string Title { get; init; }

    public required string Detail { get; init; }

    public string Reason { get; init; } = "";
}

public partial class StudyViewModel : ObservableObject
{
    private static readonly SKColor[] Palette =
    {
        new(76, 141, 255),
        new(240, 162, 2),
        new(61, 220, 151),
        new(232, 93, 117),
        new(124, 92, 255),
        new(90, 169, 230)
    };

    private readonly IMessenger _messenger;
    private readonly IStudyService _study;
    private readonly IFileDialogs _dialogs;
    private readonly ConfigurationLock _lock;
    private DistributorProject? _project;

    public StudyViewModel(IMessenger messenger, IStudyService study, IFileDialogs dialogs, ConfigurationLock configurationLock)
    {
        _messenger = messenger;
        _study = study;
        _dialogs = dialogs;
        _lock = configurationLock;
        StudyXAxes = new[] { DarkAxis(UiText.Inflow) };
        StudyYAxes = new[] { DarkAxis(UiText.PalletsPerHour) };
        messenger.Register<ProjectChangedMessage>(this, (_, message) => Load(message.Project));
        _lock.PropertyChanged += (_, _) =>
        {
            AddScenarioCommand.NotifyCanExecuteChanged();
            RemoveScenarioCommand.NotifyCanExecuteChanged();
        };
    }

    public ConfigurationLock Lock => _lock;

    private bool CanMutate() => _lock.CanMutate;

    public ObservableCollection<InflowScenario> Scenarios { get; } = new();

    public ObservableCollection<StudyRow> Rows { get; } = new();

    public ObservableCollection<SaturationRow> Saturations { get; } = new();

    public Axis[] StudyXAxes { get; }

    public Axis[] StudyYAxes { get; }

    [ObservableProperty] private ISeries[] _studySeries = Array.Empty<ISeries>();
    [ObservableProperty] private StudyResult? _result;
    [ObservableProperty] private InflowScenario? _selectedScenario;
    [ObservableProperty] private double _minLoad = 300;
    [ObservableProperty] private double _maxLoad = 4000;
    [ObservableProperty] private double _stepLoad = 500;
    [ObservableProperty] private int _replications = 3;
    [ObservableProperty] private double _durationHours = 2.5;
    [ObservableProperty] private double _warmupHours = 1;
    [ObservableProperty] private bool _reoptimize = true;
    [ObservableProperty] private bool _compareModes;
    [ObservableProperty] private double _achievedRatio = 0.90;
    [ObservableProperty] private bool _lostRule = true;
    [ObservableProperty] private string _utilizationCeiling = "";
    [ObservableProperty] private int _iterations = 80;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "";

    partial void OnSelectedScenarioChanged(InflowScenario? value)
    {
        if (value is null)
        {
            return;
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void AddScenario()
    {
        if (_project is null)
        {
            return;
        }

        var scenario = new InflowScenario { Name = "Scenario " + (_project.Scenarios.Count + 1), TotalCratesPerHour = 1800 };
        _project.Scenarios.Add(scenario);
        Scenarios.Add(scenario);
        SelectedScenario = scenario;
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private void RemoveScenario()
    {
        if (_project is null || SelectedScenario is null)
        {
            return;
        }

        _project.Scenarios.Remove(SelectedScenario);
        Scenarios.Remove(SelectedScenario);
        SelectedScenario = Scenarios.FirstOrDefault();
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_project is null)
        {
            return;
        }

        IsBusy = true;
        Status = UiText.StudyRunning;
        double? ceiling = double.TryParse(UtilizationCeiling, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
        var request = new StudyRequest
        {
            MinCratesPerHour = MinLoad,
            MaxCratesPerHour = MaxLoad,
            StepCratesPerHour = Math.Max(1, StepLoad),
            Replications = Math.Max(1, Replications),
            DurationHours = Math.Max(0.02, DurationHours),
            WarmupHours = Math.Max(0, WarmupHours),
            ReoptimizePerScenario = Reoptimize,
            CompareAssignmentModes = CompareModes,
            Mode = _project.AssignmentMode,
            OptimizerIterations = Math.Max(0, Iterations),
            BaseSeed = _project.Study.BaseSeed,
            Thresholds = new SaturationSettings
            {
                AchievedRatio = AchievedRatio,
                TreatLostCratesAsSaturation = LostRule,
                UtilizationCeiling = ceiling
            }
        };
        var progress = new Progress<StudyProgress>(update =>
        {
            Progress = update.Total == 0 ? 0 : (double)update.Completed / update.Total;
            Status = update.Message;
        });
        try
        {
            var result = await _study.RunAsync(_project, request, progress, cancellationToken);
            Show(result);
            Status = UiText.StatusReady;
        }
        catch (OperationCanceledException)
        {
            Status = UiText.Cancel;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Result is null)
        {
            return;
        }

        string? path = await _dialogs.SaveTextAsync("study-results.csv", "csv");
        if (path is null)
        {
            return;
        }

        File.WriteAllText(path, ScenarioStudyRunner.ToCsv(Result));
        Status = UiText.StudyExported;
    }

    public void Show(StudyResult result)
    {
        Result = result;
        Rows.Clear();
        foreach (var point in result.Means)
        {
            Rows.Add(new StudyRow
            {
                Scenario = point.Scenario,
                Mode = point.Mode.ToString(),
                Load = point.LoadCratesPerHour.ToString("0"),
                Offered = point.OfferedPalletsPerHour.ToString("0.0"),
                Achieved = point.AchievedPalletsPerHour.ToString("0.0"),
                Utilization = point.UtilizationByShuttle.Count == 0
                    ? $"{point.Utilization1:P0} / {point.Utilization2:P0}"
                    : string.Join(" / ", point.UtilizationByShuttle.Select(value => value.ToString("P0"))),
                Lost = point.LostCrates.ToString(),
                Blocked = point.BlockedSeconds.ToString("0")
            });
        }

        Saturations.Clear();
        foreach (var item in result.Saturations)
        {
            string at = item.Found ? $"{item.SaturatedAtCratesPerHour:0} crates/h" : UiText.NotReached;
            Saturations.Add(new SaturationRow
            {
                Title = $"{item.Scenario} · {item.Mode}",
                Detail = $"{UiText.Sustainable} {item.MaxSustainablePalletsPerHour:0.0} {UiText.PalletsUnit} · {at}",
                Reason = item.Reason
            });
        }

        var series = new List<ISeries>();
        int color = 0;
        foreach (var group in result.Means.GroupBy(point => point.Scenario + " " + point.Mode))
        {
            var paint = Palette[color % Palette.Length];
            color++;
            var ordered = group.OrderBy(point => point.LoadCratesPerHour).ToList();
            series.Add(new LineSeries<ObservablePoint>
            {
                Name = group.Key + " achieved",
                Values = ordered.Select(point => new ObservablePoint(point.LoadCratesPerHour, point.AchievedPalletsPerHour)).ToList(),
                GeometrySize = 10,
                Fill = null,
                Stroke = new SolidColorPaint(paint, 3.5f),
                GeometryStroke = new SolidColorPaint(paint, 2),
                GeometryFill = new SolidColorPaint(paint)
            });
            series.Add(new LineSeries<ObservablePoint>
            {
                Name = group.Key + " offered",
                Values = ordered.Select(point => new ObservablePoint(point.LoadCratesPerHour, point.OfferedPalletsPerHour)).ToList(),
                GeometrySize = 0,
                Fill = null,
                Stroke = new SolidColorPaint(paint, 1.8f) { PathEffect = new DashEffect(new float[] { 6, 5 }) }
            });
        }

        if (result.TheoreticalPalletsPerHour > 0 && result.Means.Count > 0)
        {
            double minLoad = result.Means.Min(point => point.LoadCratesPerHour);
            double maxLoad = result.Means.Max(point => point.LoadCratesPerHour);
            var capacityPaint = new SolidColorPaint(new SKColor(244, 247, 251), 2.2f)
            {
                PathEffect = new DashEffect(new float[] { 2, 4 })
            };
            series.Add(new LineSeries<ObservablePoint>
            {
                Name = UiText.TheoreticalCapacity,
                Values = new[]
                {
                    new ObservablePoint(minLoad, result.TheoreticalPalletsPerHour),
                    new ObservablePoint(maxLoad, result.TheoreticalPalletsPerHour)
                },
                GeometrySize = 0,
                Fill = null,
                LineSmoothness = 0,
                Stroke = capacityPaint
            });
        }

        foreach (var item in result.Saturations.Where(item => item.Found && item.SaturatedAtCratesPerHour is not null))
        {
            var match = result.Means.FirstOrDefault(point =>
                point.Scenario == item.Scenario
                && point.Mode == item.Mode
                && Math.Abs(point.LoadCratesPerHour - item.SaturatedAtCratesPerHour!.Value) < 1e-6);
            series.Add(new ScatterSeries<ObservablePoint>
            {
                Name = UiText.Saturation,
                Values = new[] { new ObservablePoint(item.SaturatedAtCratesPerHour!.Value, match?.AchievedPalletsPerHour ?? 0) },
                GeometrySize = 16,
                Fill = new SolidColorPaint(new SKColor(232, 93, 117)),
                Stroke = new SolidColorPaint(SKColors.White, 2)
            });
        }

        StudySeries = series.ToArray();
    }

    private void Load(DistributorProject project)
    {
        _project = project;
        MinLoad = project.Study.MinCratesPerHour;
        MaxLoad = project.Study.MaxCratesPerHour;
        StepLoad = project.Study.StepCratesPerHour;
        Replications = project.Study.Replications;
        DurationHours = project.Study.DurationHours;
        WarmupHours = project.Study.WarmupHours;
        Reoptimize = project.Study.ReoptimizePerScenario;
        CompareModes = project.Study.CompareAssignmentModes;
        AchievedRatio = project.Study.AchievedRatioThreshold;
        LostRule = project.Study.TreatLostCratesAsSaturation;
        UtilizationCeiling = project.Study.UtilizationCeiling?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        Scenarios.Clear();
        foreach (var scenario in project.Scenarios)
        {
            Scenarios.Add(scenario);
        }

        SelectedScenario = Scenarios.FirstOrDefault();
    }

    private static Axis DarkAxis(string name) => new()
    {
        Name = name,
        NamePaint = new SolidColorPaint(new SKColor(180, 196, 214)),
        LabelsPaint = new SolidColorPaint(new SKColor(154, 171, 188)),
        SeparatorsPaint = new SolidColorPaint(new SKColor(46, 58, 74)),
        TextSize = 11,
        NameTextSize = 12
    };
}
