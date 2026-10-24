using CommunityToolkit.Mvvm.Messaging;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Desktop.Abstractions;
using ShaftStockDistributor.Desktop.Services;
using ShaftStockDistributor.Desktop.ViewModels;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.Tests;

public sealed class ViewModelTests
{
    [Fact]
    public void Mode_change_waits_for_confirmation()
    {
        var shell = Harness.CreateShell();
        Assert.Equal(AssignmentMode.Automatic, shell.Project.AssignmentMode);
        shell.RequestModeChangeCommand.Execute(null);
        Assert.True(shell.ModeConfirmationVisible);
        Assert.Equal(AssignmentMode.Automatic, shell.Project.AssignmentMode);
        Assert.Equal(UiText.ModeAutomatic, shell.ModeText);

        shell.CancelModeChangeCommand.Execute(null);
        Assert.False(shell.ModeConfirmationVisible);
        Assert.Equal(AssignmentMode.Automatic, shell.Project.AssignmentMode);

        shell.RequestModeChangeCommand.Execute(null);
        shell.ConfirmModeChangeCommand.Execute(null);
        Assert.False(shell.ModeConfirmationVisible);
        Assert.Equal(AssignmentMode.Manual, shell.Project.AssignmentMode);
        Assert.Equal(UiText.ModeManual, shell.ModeText);
    }

    [Fact]
    public void Article_draft_rejects_a_blank_name()
    {
        var draft = new ArticleDraft();
        draft.Id = "";
        draft.Name = "";
        draft.Validate();
        Assert.True(draft.HasErrors);
    }

    [Fact]
    public void Layout_rejects_a_non_positive_shuttle_speed()
    {
        var shell = Harness.CreateShell();
        shell.Layout.SegmentCount = 4;
        shell.Layout.Shuttle1.HorizontalSpeed = 0;
        shell.Layout.ApplyCommand.Execute(null);
        Assert.Equal(12, shell.Project.Layout.SegmentCount);
        Assert.Equal(UiText.Validation_Positive, shell.Layout.Status);
    }

    [Fact]
    public void Playback_play_pause_step_and_reset()
    {
        var clock = new ManualClock();
        var shell = Harness.CreateShell(clock);
        Assert.False(shell.Live.IsRunning);
        Assert.Equal("00:00:00", shell.Live.TimeText);

        shell.Live.PlayCommand.Execute(null);
        Assert.True(shell.Live.IsRunning);
        clock.Pulse();
        Assert.NotNull(shell.Live.Snapshot);
        Assert.True(shell.Live.Snapshot.TimeSeconds > 0);
        for (int tick = 0; tick < 40; tick++)
        {
            clock.Pulse();
        }

        Assert.NotEqual("00:00:00", shell.Live.TimeText);
        string advanced = shell.Live.TimeText;

        shell.Live.PauseCommand.Execute(null);
        clock.Pulse();
        Assert.False(shell.Live.IsRunning);
        Assert.Equal(advanced, shell.Live.TimeText);

        shell.Live.StepCommand.Execute(null);
        shell.Live.ResetPlaybackCommand.Execute(null);
        Assert.False(shell.Live.IsRunning);
        Assert.Equal("00:00:00", shell.Live.TimeText);
        Assert.Equal(UiText.PlaybackReset, shell.Live.Status);
    }

    [Fact]
    public async Task Optimizer_view_model_records_before_and_after_cost()
    {
        var shell = Harness.CreateShell(optimizer: new FakeOptimizer());
        await shell.Optimizer.RunCommand.ExecuteAsync(null);
        Assert.Contains("total", shell.Optimizer.BeforeText, StringComparison.Ordinal);
        Assert.Contains("total", shell.Optimizer.NaiveText, StringComparison.Ordinal);
        Assert.Contains("total", shell.Optimizer.AfterText, StringComparison.Ordinal);
        Assert.Equal("4", shell.Optimizer.ChangedText);
        Assert.False(shell.Optimizer.IsBusy);
    }

    [Fact]
    public void Study_view_model_shows_rows_and_saturation()
    {
        var shell = Harness.CreateShell();
        shell.Study.Show(new StudyResult
        {
            Points = new[]
            {
                Point("EXAMPLE balanced 70/20/10", 600, 12, 12, 0),
                Point("EXAMPLE balanced 70/20/10", 1200, 24, 18, 4)
            },
            Means = new[]
            {
                Point("EXAMPLE balanced 70/20/10", 600, 12, 12, 0),
                Point("EXAMPLE balanced 70/20/10", 1200, 24, 18, 4)
            },
            Saturations = new[]
            {
                new ScenarioSaturation
                {
                    Scenario = "EXAMPLE balanced 70/20/10",
                    Mode = AssignmentMode.Automatic,
                    Found = true,
                    SaturatedAtCratesPerHour = 1200,
                    MaxSustainablePalletsPerHour = 12,
                    Reason = "Lost crates were above zero."
                }
            }
        });

        Assert.Equal(2, shell.Study.Rows.Count);
        Assert.Single(shell.Study.Saturations);
        Assert.Contains(shell.Study.StudySeries, series => series.Name?.Contains("achieved", StringComparison.Ordinal) == true);
        Assert.Contains("1200", shell.Study.Saturations[0].Detail, StringComparison.Ordinal);
    }

    private static StudyPoint Point(string scenario, double load, double offered, double achieved, int lost) => new()
    {
        Scenario = scenario,
        Mode = AssignmentMode.Automatic,
        LoadCratesPerHour = load,
        OfferedPalletsPerHour = offered,
        AchievedPalletsPerHour = achieved,
        LostCrates = lost
    };

    private sealed class ManualClock : IUiClock
    {
        private readonly List<Action> _callbacks = new();

        public IDisposable Every(TimeSpan interval, Action callback)
        {
            _callbacks.Add(callback);
            return new Noop();
        }

        public void Pulse()
        {
            foreach (var callback in _callbacks.ToArray())
            {
                callback();
            }
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeDialogs : IFileDialogs
    {
        public Task<string?> OpenProjectAsync() => Task.FromResult<string?>(null);

        public Task<string?> SaveProjectAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> SaveTextAsync(string suggestedName, string extension) => Task.FromResult<string?>(null);
    }

    private sealed class FakeOptimizer : IOptimizerService
    {
        public Task<OptimizationResult> OptimizeAsync(
            DistributorProject project,
            int? iterations,
            IProgress<OptimizeProgress>? progress,
            CancellationToken cancellationToken)
        {
            var slots = ProjectGrid.Materialize(project);
            var before = AssignmentCostModel.Evaluate(project, slots);
            var naive = AssignmentCostModel.Evaluate(project, AssignmentBuilder.Naive(project));
            return Task.FromResult(new OptimizationResult
            {
                Assignments = slots,
                Before = before,
                Naive = naive,
                Optimized = before,
                ChangedShafts = 4
            });
        }
    }

    private static class Harness
    {
        public static ShellViewModel CreateShell(IUiClock? clock = null, IOptimizerService? optimizer = null)
        {
            var messenger = new WeakReferenceMessenger();
            var store = new JsonProjectStore();
            var dialogs = new FakeDialogs();
            optimizer ??= new OptimizerService();
            var live = new LiveDemoViewModel(messenger, clock ?? new ManualClock(), optimizer, store);
            var layout = new LayoutEditorViewModel(messenger);
            var articles = new ArticlesViewModel(messenger);
            var optimizerView = new OptimizerViewModel(messenger, optimizer);
            var study = new StudyViewModel(messenger, new StudyService(), dialogs);
            var operation = new OperationViewModel(messenger);
            return new ShellViewModel(live, layout, articles, optimizerView, study, operation, messenger, store, dialogs);
        }
    }
}
