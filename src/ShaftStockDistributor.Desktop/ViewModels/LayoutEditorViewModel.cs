using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Desktop.ViewModels;

public partial class ShuttleDraft : ObservableValidator
{
    public int Number { get; set; } = 1;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(1, 80, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private int _segmentFrom = 1;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(1, 80, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private int _segmentTo = 1;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(0.01, 20, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private double _horizontalSpeed = 0.3;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(0.01, 20, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private double _verticalSpeed = 0.3;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(0, 20, ErrorMessageResourceName = nameof(UiText.Validation_NonNegative), ErrorMessageResourceType = typeof(UiText))]
    private double _horizontalAcceleration = 0.2;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(0, 20, ErrorMessageResourceName = nameof(UiText.Validation_NonNegative), ErrorMessageResourceType = typeof(UiText))]
    private double _verticalAcceleration = 0.2;

    [ObservableProperty] private double _homeX;
    [ObservableProperty] private int _homeLevel = 1;
    [ObservableProperty] private double _handoverX;
    [ObservableProperty] private int _handoverLevel = 1;

    public string Title => "Shuttle " + Number.ToString();

    public void Validate() => ValidateAllProperties();

    public void Load(ShuttleSettings settings)
    {
        Number = settings.Id.Number;
        SegmentFrom = settings.SegmentFrom;
        SegmentTo = settings.SegmentTo;
        HorizontalSpeed = settings.HorizontalSpeedMetersPerSecond;
        VerticalSpeed = settings.VerticalSpeedMetersPerSecond;
        HorizontalAcceleration = settings.HorizontalAccelerationMetersPerSecondSquared;
        VerticalAcceleration = settings.VerticalAccelerationMetersPerSecondSquared;
        HomeX = settings.Home.XMeters;
        HomeLevel = settings.Home.Level;
        HandoverX = settings.Handover.XMeters;
        HandoverLevel = settings.Handover.Level;
    }

    public ShuttleSettings ToSettings() => new()
    {
        Id = new ShuttleId(Number),
        SegmentFrom = SegmentFrom,
        SegmentTo = SegmentTo,
        HorizontalSpeedMetersPerSecond = HorizontalSpeed,
        VerticalSpeedMetersPerSecond = VerticalSpeed,
        HorizontalAccelerationMetersPerSecondSquared = HorizontalAcceleration,
        VerticalAccelerationMetersPerSecondSquared = VerticalAcceleration,
        Home = new ShuttlePose { XMeters = HomeX, Level = HomeLevel },
        Handover = new ShuttlePose { XMeters = HandoverX, Level = HandoverLevel }
    };
}

public partial class LayoutEditorViewModel : ObservableValidator
{
    private readonly IMessenger _messenger;
    private DistributorProject? _project;

    public LayoutEditorViewModel(IMessenger messenger)
    {
        _messenger = messenger;
        messenger.Register<ProjectChangedMessage>(this, (_, message) => Load(message.Project));
    }

    public ObservableCollection<ShuttleDraft> Shuttles { get; } = new();

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(1, 80, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private int _segmentCount = 12;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(1, 12, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private int _levels = 5;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(1, 20, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private int _shaftsPerLevel = 9;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(0.5, 200, ErrorMessageResourceName = nameof(UiText.Validation_Positive), ErrorMessageResourceType = typeof(UiText))]
    private double _shaftLength = 20;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [System.ComponentModel.DataAnnotations.Range(0, 5, ErrorMessageResourceName = nameof(UiText.Validation_NonNegative), ErrorMessageResourceType = typeof(UiText))]
    private double _crateGap;

    [ObservableProperty] private double _pitch = 0.45;
    [ObservableProperty] private double _levelHeight = 0.55;
    [ObservableProperty] private double _segmentGap = 0.35;
    [ObservableProperty] private double _shaftWidth;
    [ObservableProperty] private double _shaftHeight;
    [ObservableProperty] private double _transfer = 12;
    [ObservableProperty] private double _safety = 1.5;
    [ObservableProperty] private string _status = "";

    [RelayCommand]
    private void Apply()
    {
        ValidateAllProperties();
        foreach (var shuttle in Shuttles)
        {
            shuttle.Validate();
        }

        if (HasErrors || Shuttles.Any(shuttle => shuttle.HasErrors) || _project is null)
        {
            Status = UiText.Validation_Positive;
            return;
        }

        var layout = _project.Layout;
        layout.SegmentCount = SegmentCount;
        layout.LevelsPerSegment = Levels;
        layout.ShaftsPerLevel = ShaftsPerLevel;
        layout.ShaftLengthMeters = ShaftLength;
        layout.CrateGapMeters = CrateGap;
        layout.ShaftPitchMeters = Pitch;
        layout.LevelHeightMeters = LevelHeight;
        layout.SegmentGapMeters = SegmentGap;
        layout.ShaftWidthMeters = ShaftWidth <= 0 ? null : ShaftWidth;
        layout.ShaftClearHeightMeters = ShaftHeight <= 0 ? null : ShaftHeight;
        layout.TransferDurationSeconds = Transfer;
        layout.SafetyDistanceMeters = Safety;
        _project.Shuttles = Shuttles.Select(row => row.ToSettings()).ToList();
        ProjectGrid.EnsureFullGrid(_project);
        var errors = ProjectValidator.Validate(_project)
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .ToList();
        Status = errors.Count == 0 ? UiText.PlaybackReset : errors[0].Message;
        _messenger.Send(new ProjectChangedMessage { Project = _project });
    }

    [RelayCommand]
    private void AddShuttle() => Refit(Shuttles.Count + 1, even: true);

    [RelayCommand]
    private void RemoveShuttle()
    {
        if (Shuttles.Count <= 1)
        {
            Status = UiText.ShuttleMinimum;
            return;
        }

        Refit(Shuttles.Count - 1, even: true);
    }

    [RelayCommand]
    private void EvenSplit() => Refit(Math.Max(1, Shuttles.Count), even: true);

    [RelayCommand]
    private void TwoShuttleThirds()
    {
        var probe = DraftProject();
        ShuttleLayout.ApplyTwoShuttleThirds(probe, resetMotion: true);
        ReplaceRows(probe.Shuttles);
        Status = UiText.ThirdsPresetApplied;
    }

    private void Refit(int count, bool even)
    {
        int limit = Math.Max(1, SegmentCount);
        if (count > limit)
        {
            Status = string.Format(System.Globalization.CultureInfo.InvariantCulture, UiText.ShuttleLimit, limit);
            return;
        }

        if (count < 1)
        {
            Status = UiText.ShuttleMinimum;
            return;
        }

        var probe = DraftProject();
        if (even)
        {
            ShuttleLayout.ApplyEvenSplit(probe, count, resetHomes: true);
        }

        ReplaceRows(probe.Shuttles);
        Status = string.Format(System.Globalization.CultureInfo.InvariantCulture, UiText.EvenSplitApplied, probe.Shuttles.Count);
    }

    private DistributorProject DraftProject()
    {
        var project = new DistributorProject
        {
            Layout = new LayoutSettings
            {
                SegmentCount = Math.Max(1, SegmentCount),
                LevelsPerSegment = Math.Max(1, Levels),
                ShaftsPerLevel = Math.Max(1, ShaftsPerLevel),
                ShaftPitchMeters = Pitch <= 0 ? 0.45 : Pitch,
                LevelHeightMeters = LevelHeight <= 0 ? 0.55 : LevelHeight,
                SegmentGapMeters = Math.Max(0, SegmentGap),
                SafetyDistanceMeters = Math.Max(0, Safety),
                ShaftLengthMeters = ShaftLength <= 0 ? 20 : ShaftLength
            }
        };
        foreach (var row in Shuttles)
        {
            project.Shuttles.Add(row.ToSettings());
        }

        return project;
    }

    private void ReplaceRows(IEnumerable<ShuttleSettings> shuttles)
    {
        Shuttles.Clear();
        foreach (var shuttle in shuttles.OrderBy(item => item.Id))
        {
            var row = new ShuttleDraft();
            row.Load(shuttle);
            Shuttles.Add(row);
        }
    }

    private void Load(DistributorProject project)
    {
        _project = project;
        var layout = project.Layout;
        SegmentCount = layout.SegmentCount;
        Levels = layout.LevelsPerSegment;
        ShaftsPerLevel = layout.ShaftsPerLevel;
        ShaftLength = layout.ShaftLengthMeters;
        CrateGap = layout.CrateGapMeters;
        Pitch = layout.ShaftPitchMeters;
        LevelHeight = layout.LevelHeightMeters;
        SegmentGap = layout.SegmentGapMeters;
        ShaftWidth = layout.ShaftWidthMeters ?? 0;
        ShaftHeight = layout.ShaftClearHeightMeters ?? 0;
        Transfer = layout.TransferDurationSeconds;
        Safety = layout.SafetyDistanceMeters;
        ReplaceRows(project.Shuttles);
    }
}
