using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Desktop.ViewModels;

public partial class ShuttleDraft : ObservableValidator
{
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

    public void Validate() => ValidateAllProperties();

    public void Load(ShuttleSettings settings)
    {
        HorizontalSpeed = settings.HorizontalSpeedMetersPerSecond;
        VerticalSpeed = settings.VerticalSpeedMetersPerSecond;
        HorizontalAcceleration = settings.HorizontalAccelerationMetersPerSecondSquared;
        VerticalAcceleration = settings.VerticalAccelerationMetersPerSecondSquared;
        HomeX = settings.Home.XMeters;
        HomeLevel = settings.Home.Level;
        HandoverX = settings.Handover.XMeters;
        HandoverLevel = settings.Handover.Level;
    }

    public void Write(ShuttleSettings settings)
    {
        settings.HorizontalSpeedMetersPerSecond = HorizontalSpeed;
        settings.VerticalSpeedMetersPerSecond = VerticalSpeed;
        settings.HorizontalAccelerationMetersPerSecondSquared = HorizontalAcceleration;
        settings.VerticalAccelerationMetersPerSecondSquared = VerticalAcceleration;
        settings.Home.XMeters = HomeX;
        settings.Home.Level = HomeLevel;
        settings.Handover.XMeters = HandoverX;
        settings.Handover.Level = HandoverLevel;
    }
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

    public ShuttleDraft Shuttle1 { get; } = new();

    public ShuttleDraft Shuttle2 { get; } = new();

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
        Shuttle1.Validate();
        Shuttle2.Validate();
        if (HasErrors || Shuttle1.HasErrors || Shuttle2.HasErrors || _project is null)
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
        ProjectGrid.EnsureDefaultShuttles(_project);
        Shuttle1.Write(_project.Shuttle(ShuttleId.Shuttle1));
        Shuttle2.Write(_project.Shuttle(ShuttleId.Shuttle2));
        ProjectGrid.EnsureFullGrid(_project);
        Status = UiText.PlaybackReset;
        _messenger.Send(new ProjectChangedMessage { Project = _project });
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
        if (project.Shuttles.Any(shuttle => shuttle.Id == ShuttleId.Shuttle1))
        {
            Shuttle1.Load(project.Shuttle(ShuttleId.Shuttle1));
        }

        if (project.Shuttles.Any(shuttle => shuttle.Id == ShuttleId.Shuttle2))
        {
            Shuttle2.Load(project.Shuttle(ShuttleId.Shuttle2));
        }
    }
}
