using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Desktop.Controls;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.ViewModels;

public sealed class OpenLinkedProjectMessage
{
    public required DistributorProject Project { get; init; }
}

public sealed class ResultDonut
{
    public required string Title { get; init; }

    public required string Center { get; init; }

    public required string Tip { get; init; }

    public required IReadOnlyList<DonutSlice> Slices { get; init; }
}

public sealed class ResultShuttleRow
{
    public required string Shuttle { get; init; }

    public required string Pulls { get; init; }

    public required string Utilization { get; init; }
}

public sealed class ResultStudyRow
{
    public required string Scenario { get; init; }

    public required string Mode { get; init; }

    public required string Load { get; init; }

    public required string Offered { get; init; }

    public required string Achieved { get; init; }

    public required string Lost { get; init; }

    public required string Blocked { get; init; }
}

public partial class ResultViewModel : ObservableObject
{
    private static readonly string[] Palette = { "#4C8DFF", "#F0A202", "#3DDC97", "#E85D75", "#7C5CFF", "#5AA9E6" };

    private readonly IMessenger _messenger;
    private readonly ConfigurationLock _lock;
    private SimulationResultDocument? _document;

    public ResultViewModel(IMessenger messenger, ConfigurationLock configurationLock)
    {
        _messenger = messenger;
        _lock = configurationLock;
        _lock.PropertyChanged += (_, _) => OpenLinkedProjectCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<ResultDonut> Donuts { get; } = new();

    public ObservableCollection<ResultShuttleRow> Shuttles { get; } = new();

    public ObservableCollection<ResultStudyRow> StudyRows { get; } = new();

    public ObservableCollection<string> Events { get; } = new();

    public ObservableCollection<string> Saturations { get; } = new();

    [ObservableProperty] private bool _isLoaded;

    public bool IsEmpty => !IsLoaded;

    partial void OnIsLoadedChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    [ObservableProperty] private bool _hasSeries;
    [ObservableProperty] private bool _hasStudy;
    [ObservableProperty] private bool _hasUtilization;
    [ObservableProperty] private string _headline = UiText.ResultEmpty;
    [ObservableProperty] private string _detail = UiText.ResultRecorded;
    [ObservableProperty] private string _offeredText = "—";
    [ObservableProperty] private string _achievedText = "—";
    [ObservableProperty] private string _pullsText = "—";
    [ObservableProperty] private string _lostText = "—";
    [ObservableProperty] private string _waitText = "—";
    [ObservableProperty] private string _blockedText = "—";
    [ObservableProperty] private IReadOnlyList<double> _rateSamples = Array.Empty<double>();
    [ObservableProperty] private double _offeredRate;
    [ObservableProperty] private double _chartSpanMinutes;
    [ObservableProperty] private IReadOnlyList<BarPoint> _utilizationBars = Array.Empty<BarPoint>();
    [ObservableProperty] private IReadOnlyList<LoadPoint> _loadPoints = Array.Empty<LoadPoint>();

    public void Show(SimulationResultDocument document)
    {
        _document = document;
        var summary = document.Summary;
        IsLoaded = true;
        HasSeries = document.Series.Count > 0;
        HasStudy = document.Study is not null && document.Study.Means.Count > 0;
        HasUtilization = summary.Shuttles.Count > 0;
        Headline = string.IsNullOrWhiteSpace(document.Project.Name) ? UiText.NavResults : document.Project.Name;
        Detail = Describe(document);
        OfferedText = summary.OfferedPalletsPerHour.ToString("0.0", CultureInfo.InvariantCulture);
        AchievedText = summary.AchievedPalletsPerHour.ToString("0.0", CultureInfo.InvariantCulture);
        PullsText = summary.Shuttles.Count == 0
            ? "0"
            : string.Join(" / ", summary.Shuttles.Select(shuttle => shuttle.Pulls.ToString(CultureInfo.InvariantCulture)));
        LostText = summary.LostCrates.ToString(CultureInfo.InvariantCulture);
        WaitText = summary.AveragePullWaitSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        BlockedText = summary.BlockedSeconds.ToString("0", CultureInfo.InvariantCulture) + " s";
        OfferedRate = summary.OfferedPalletsPerHour;
        RateSamples = document.Series.Select(sample => sample.SteadyPalletsPerHour).ToArray();
        ChartSpanMinutes = document.Series.Count == 0
            ? 0
            : Math.Max(0, (document.Series[^1].TimeHours - document.Series[0].TimeHours) * 60.0);
        UtilizationBars = summary.Shuttles.Select((shuttle, index) => new BarPoint
        {
            Label = "Shuttle " + shuttle.Number.ToString(CultureInfo.InvariantCulture),
            Value = shuttle.Utilization,
            ColorHex = Palette[index % Palette.Length]
        }).ToArray();

        Shuttles.Clear();
        foreach (var shuttle in summary.Shuttles)
        {
            Shuttles.Add(new ResultShuttleRow
            {
                Shuttle = "Shuttle " + shuttle.Number.ToString(CultureInfo.InvariantCulture),
                Pulls = shuttle.Pulls.ToString(CultureInfo.InvariantCulture),
                Utilization = shuttle.Utilization.ToString("P0", CultureInfo.InvariantCulture)
            });
        }

        Events.Clear();
        foreach (string line in document.Events)
        {
            Events.Add(line);
        }

        Donuts.Clear();
        Donuts.Add(PullDonut(summary));
        Donuts.Add(BusyDonut(summary));
        var demand = DemandDonut(document);
        if (demand is not null)
        {
            Donuts.Add(demand);
        }

        StudyRows.Clear();
        Saturations.Clear();
        LoadPoints = Array.Empty<LoadPoint>();
        if (document.Study is not null)
        {
            int color = 0;
            var loads = new List<LoadPoint>();
            foreach (var group in document.Study.Means.GroupBy(point => point.Scenario + " · " + point.Mode))
            {
                string hex = Palette[color % Palette.Length];
                color++;
                foreach (var point in group)
                {
                    loads.Add(new LoadPoint
                    {
                        Series = group.Key,
                        Load = point.LoadCratesPerHour,
                        Achieved = point.AchievedPalletsPerHour,
                        Lost = point.LostCrates,
                        ColorHex = hex
                    });
                }
            }

            LoadPoints = loads;
            foreach (var point in document.Study.Means)
            {
                StudyRows.Add(new ResultStudyRow
                {
                    Scenario = point.Scenario,
                    Mode = point.Mode,
                    Load = point.LoadCratesPerHour.ToString("0", CultureInfo.InvariantCulture),
                    Offered = point.OfferedPalletsPerHour.ToString("0.0", CultureInfo.InvariantCulture),
                    Achieved = point.AchievedPalletsPerHour.ToString("0.0", CultureInfo.InvariantCulture),
                    Lost = point.LostCrates.ToString(CultureInfo.InvariantCulture),
                    Blocked = point.BlockedSeconds.ToString("0", CultureInfo.InvariantCulture)
                });
            }

            foreach (var item in document.Study.Saturations)
            {
                string at = item.Found
                    ? item.SaturatedAtCratesPerHour?.ToString("0", CultureInfo.InvariantCulture) + " crates/h"
                    : UiText.NotReached;
                Saturations.Add($"{item.Scenario} · {item.Mode}: {UiText.Sustainable} {item.MaxSustainablePalletsPerHour:0.0} {UiText.PalletsUnit}. {at}. {item.Reason}");
            }
        }

        OpenLinkedProjectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenLinked))]
    private void OpenLinkedProject()
    {
        var project = _document?.ReadProject();
        if (project is null)
        {
            return;
        }

        _messenger.Send(new OpenLinkedProjectMessage { Project = project });
    }

    private bool CanOpenLinked() => _document?.HasSnapshot == true && _lock.CanMutate;

    private static ResultDonut PullDonut(ResultSummary summary)
    {
        int total = summary.Shuttles.Sum(shuttle => shuttle.Pulls);
        IReadOnlyList<DonutSlice> slices = total == 0
            ? new[] { new DonutSlice { Label = UiText.ResultNoPulls, Value = 1, ColorHex = "#7E8C9A" } }
            : summary.Shuttles.Select((shuttle, index) => new DonutSlice
            {
                Label = "Shuttle " + shuttle.Number.ToString(CultureInfo.InvariantCulture),
                Value = shuttle.Pulls,
                ColorHex = Palette[index % Palette.Length]
            }).ToArray();
        return new ResultDonut
        {
            Title = UiText.ResultPullDonut,
            Center = total.ToString(CultureInfo.InvariantCulture),
            Tip = UiText.TipPullDonut,
            Slices = slices
        };
    }

    private static ResultDonut BusyDonut(ResultSummary summary)
    {
        double busy = summary.Shuttles.Count == 0 ? 0 : summary.Shuttles.Average(shuttle => Math.Clamp(shuttle.Utilization, 0, 1));
        double idle = Math.Max(0, 1 - busy);
        return new ResultDonut
        {
            Title = UiText.ResultBusyDonut,
            Center = busy.ToString("P0", CultureInfo.InvariantCulture),
            Tip = UiText.TipBusyDonut,
            Slices = new[]
            {
                new DonutSlice { Label = UiText.ResultBusy, Value = busy, ColorHex = "#4C8DFF" },
                new DonutSlice { Label = UiText.ResultIdle, Value = idle <= 1e-9 ? 0 : idle, ColorHex = "#7E8C9A" }
            }
        };
    }

    private static ResultDonut? DemandDonut(SimulationResultDocument document)
    {
        var project = document.ReadProject();
        if (project is null || project.Articles.Count == 0)
        {
            return null;
        }

        double a = project.Articles.Where(article => article.Class == AbcClass.A).Sum(article => Math.Max(0, article.InflowCratesPerHour));
        double b = project.Articles.Where(article => article.Class == AbcClass.B).Sum(article => Math.Max(0, article.InflowCratesPerHour));
        double c = project.Articles.Where(article => article.Class == AbcClass.C).Sum(article => Math.Max(0, article.InflowCratesPerHour));
        if (a + b + c <= 1e-9)
        {
            return null;
        }

        return new ResultDonut
        {
            Title = UiText.ResultDemandDonut,
            Center = "ABC",
            Tip = UiText.TipDemandDonut,
            Slices = new[]
            {
                new DonutSlice { Label = "A", Value = a, ColorHex = "#E85D75" },
                new DonutSlice { Label = "B", Value = b, ColorHex = "#F0A202" },
                new DonutSlice { Label = "C", Value = c, ColorHex = "#4C8DFF" }
            }
        };
    }

    private static string Describe(SimulationResultDocument document)
    {
        string kind = document.Kind == "study" ? UiText.ResultStudyKind : UiText.ResultRunKind;
        string scenario = string.IsNullOrWhiteSpace(document.Run.ScenarioName) ? "" : " · " + document.Run.ScenarioName;
        return string.Create(CultureInfo.InvariantCulture, $"{kind}{scenario} · {document.Run.Mode} · seed {document.Run.Seed} · {document.Run.DurationHours:0.##} h, warm-up {document.Run.WarmupHours:0.##} h. {UiText.ResultRecorded}");
    }
}
