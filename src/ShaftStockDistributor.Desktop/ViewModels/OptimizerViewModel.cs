using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Desktop.Abstractions;

namespace ShaftStockDistributor.Desktop.ViewModels;

public partial class OptimizerViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IOptimizerService _optimizer;
    private DistributorProject? _project;

    public OptimizerViewModel(IMessenger messenger, IOptimizerService optimizer)
    {
        _messenger = messenger;
        _optimizer = optimizer;
        messenger.Register<ProjectChangedMessage>(this, (_, message) => _project = message.Project);
    }

    [ObservableProperty] private int _iterations = 400;
    [ObservableProperty] private double _expectedWait = 90;
    [ObservableProperty] private double _travelWeight = 1;
    [ObservableProperty] private double _imbalanceWeight = 1.2;
    [ObservableProperty] private double _middleWeight = 0.8;
    [ObservableProperty] private double _penaltyWeight = 4;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _beforeText = "—";
    [ObservableProperty] private string _naiveText = "—";
    [ObservableProperty] private string _afterText = "—";
    [ObservableProperty] private string _changedText = "0";
    [ObservableProperty] private string _status = "";

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_project is null)
        {
            return;
        }

        IsBusy = true;
        Status = UiText.OptimizerRunning;
        var settings = _project.Optimizer;
        settings.Iterations = Math.Max(0, Iterations);
        settings.ExpectedPullWaitSeconds = ExpectedWait;
        settings.TravelWeight = TravelWeight;
        settings.ImbalanceWeight = ImbalanceWeight;
        settings.MiddleTrafficWeight = MiddleWeight;
        settings.PenaltyWeight = PenaltyWeight;
        var progress = new Progress<ShaftStockDistributor.Optimizer.OptimizeProgress>(update =>
        {
            Progress = update.Iterations == 0 ? 0 : (double)update.Iteration / update.Iterations;
            AfterText = update.BestCost.ToString("0.000");
        });
        try
        {
            var result = await _optimizer.OptimizeAsync(_project, settings.Iterations, progress, cancellationToken);
            BeforeText = result.Before.ToString();
            NaiveText = result.Naive.ToString();
            AfterText = result.Optimized.ToString();
            ChangedText = result.ChangedShafts.ToString();
            ProjectGrid.ReplaceAssignments(_project, result.Assignments);
            Status = UiText.PlaybackReset;
            _messenger.Send(new ProjectChangedMessage { Project = _project });
        }
        catch (OperationCanceledException)
        {
            Status = UiText.Cancel;
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }
}
