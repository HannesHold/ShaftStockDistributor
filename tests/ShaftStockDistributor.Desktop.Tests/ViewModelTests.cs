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
    public void Applying_an_article_inflow_updates_the_project_and_the_live_arrival_rate()
    {
        var shell = Harness.CreateShell();
        var article = shell.Project.Articles.First(item => item.Id == "EX-A-WATER");
        shell.ShowPageCommand.Execute("Articles");
        shell.Articles.Selected = article;
        double original = article.InflowCratesPerHour;
        Assert.True(original > 0);

        shell.Articles.Draft.Inflow = 0;
        shell.Articles.ApplyDraftCommand.Execute(null);
        int quiet = CratesOf(shell, article.Id);
        shell.Live.AdvanceSeconds(30);
        int afterQuiet = CratesOf(shell, article.Id);

        shell.Articles.Selected = shell.Project.FindArticle(article.Id);
        shell.Articles.Draft.Inflow = 36000;
        shell.Articles.ApplyDraftCommand.Execute(null);
        Assert.Equal(36000, shell.Project.FindArticle(article.Id)!.InflowCratesPerHour);
        var shown = shell.Live.Snapshot!.ArticleFlows.First(flow => flow.Id == article.Id);
        Assert.Equal(36000, shown.InflowCratesPerHour);
        shell.Live.AdvanceSeconds(30);
        int afterFast = CratesOf(shell, article.Id);

        Assert.True(afterQuiet - quiet < 5, $"inflow 0 still accepted crates ({quiet} -> {afterQuiet})");
        Assert.True(afterFast - afterQuiet > 40, $"inflow 36000 did not speed arrivals ({afterQuiet} -> {afterFast})");
        Assert.Equal(article.Id, shell.Articles.Selected?.Id);
        Assert.Equal(36000, shell.Articles.Draft.Inflow);
        Assert.Contains(article.Name, shell.Articles.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_keeps_an_inflow_typed_into_the_article_row()
    {
        var shell = Harness.CreateShell();
        var article = shell.Project.Articles[0];
        shell.ShowPageCommand.Execute("Articles");
        shell.Articles.Selected = article;
        double original = article.InflowCratesPerHour;
        article.InflowCratesPerHour = 4321;

        shell.Articles.ApplyDraftCommand.Execute(null);

        Assert.Equal(4321, shell.Project.FindArticle(article.Id)!.InflowCratesPerHour);
        Assert.NotEqual(original, shell.Project.FindArticle(article.Id)!.InflowCratesPerHour);
        Assert.Equal(4321, shell.Live.Snapshot!.ArticleFlows.First(flow => flow.Id == article.Id).InflowCratesPerHour);
        Assert.Contains(article.Name, shell.Articles.Status, StringComparison.Ordinal);
    }

    private static int CratesOf(ShellViewModel shell, string articleId)
    {
        var snapshot = shell.Live.Snapshot ?? throw new InvalidOperationException("No snapshot.");
        int stored = snapshot.Shafts.Where(shaft => shaft.ArticleId == articleId).Sum(shaft => shaft.Count);
        int lost = snapshot.ArticleFlows.Where(flow => flow.Id == articleId).Sum(flow => flow.LostCrates);
        return stored + lost;
    }

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
        shell.Layout.Shuttles[0].HorizontalSpeed = 0;
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

    [Fact]
    public async Task Run_simulation_keeps_layout_articles_and_assignment()
    {
        var shell = Harness.CreateShell(optimizer: new ExplodingOptimizer());
        var project = shell.Project;
        shell.Layout.SegmentCount = 8;
        shell.Layout.ApplyCommand.Execute(null);
        project.Articles[0].Name = "Custom returnable crate";
        var kept = project.Assignments.First(slot => slot.Segment == 1 && slot.Level == 1 && slot.Shaft == 1);
        kept.ArticleId = project.Articles[0].Id;
        kept.Pinned = true;
        string signature = AssignmentSignature(project);

        await shell.StartDemoCommand.ExecuteAsync(null);

        Assert.Same(project, shell.Project);
        Assert.Equal(8, project.Layout.SegmentCount);
        Assert.Equal("Custom returnable crate", project.Articles[0].Name);
        Assert.Equal(signature, AssignmentSignature(project));
        Assert.Equal("Live", shell.Page);
        Assert.DoesNotContain(project.Assignments, slot => slot.Segment > 8);
    }

    [Fact]
    public async Task Run_simulation_reoptimizes_the_current_project_when_the_assignment_does_not_fit()
    {
        var shell = Harness.CreateShell(optimizer: new FakeOptimizer());
        var project = shell.Project;
        shell.Layout.SegmentCount = 4;
        shell.Layout.ApplyCommand.Execute(null);
        project.Articles[0].Name = "Custom returnable crate";
        project.Assignments.Add(new ShaftAssignment { Segment = 40, Level = 1, Shaft = 1, ArticleId = "missing-article" });
        int articles = project.Articles.Count;

        await shell.StartDemoCommand.ExecuteAsync(null);

        Assert.Same(project, shell.Project);
        Assert.Equal(4, project.Layout.SegmentCount);
        Assert.Equal("Custom returnable crate", project.Articles[0].Name);
        Assert.Equal(articles, project.Articles.Count);
        Assert.Equal(4 * project.Layout.LevelsPerSegment * project.Layout.ShaftsPerLevel, project.Assignments.Count);
        Assert.DoesNotContain(project.Assignments, slot => slot.Segment == 40 || slot.ArticleId == "missing-article");
        Assert.Equal(UiText.ReoptimizedForLayout, shell.Status);
        Assert.Equal(UiText.ReoptimizedForLayout, shell.Live.Status);
        Assert.False(shell.Live.IsBusy);
    }

    [Fact]
    public void Load_example_asks_before_discarding_unsaved_changes()
    {
        var shell = Harness.CreateShell();
        Assert.False(shell.HasUnsavedChanges);
        shell.LoadExampleCommand.Execute(null);
        Assert.False(shell.LoadExampleConfirmationVisible);
        Assert.Equal(12, shell.Project.Layout.SegmentCount);

        shell.Layout.SegmentCount = 5;
        shell.Layout.ApplyCommand.Execute(null);
        shell.Project.Articles[0].Name = "Custom returnable crate";
        Assert.True(shell.HasUnsavedChanges);

        shell.LoadExampleCommand.Execute(null);
        Assert.True(shell.LoadExampleConfirmationVisible);
        Assert.Equal(5, shell.Project.Layout.SegmentCount);
        Assert.Equal("Custom returnable crate", shell.Project.Articles[0].Name);

        shell.CancelLoadExampleCommand.Execute(null);
        Assert.False(shell.LoadExampleConfirmationVisible);
        Assert.Equal(5, shell.Project.Layout.SegmentCount);
        Assert.Equal("Custom returnable crate", shell.Project.Articles[0].Name);

        shell.LoadExampleCommand.Execute(null);
        shell.ConfirmLoadExampleCommand.Execute(null);
        Assert.False(shell.LoadExampleConfirmationVisible);
        Assert.False(shell.HasUnsavedChanges);
        Assert.Equal(12, shell.Project.Layout.SegmentCount);
        Assert.Equal(ExampleWarehouse.ProjectName, shell.Project.Name);
        Assert.NotEqual("Custom returnable crate", shell.Project.Articles[0].Name);
    }

    [Fact]
    public void Apply_layout_rebuilds_geometry_without_replacing_the_project()
    {
        var shell = Harness.CreateShell();
        var project = shell.Project;
        var kept = project.Assignments.First(slot => slot.Segment == 1 && slot.Level == 1 && slot.Shaft == 1);
        kept.ArticleId = project.Articles[0].Id;
        kept.Pinned = true;
        shell.Layout.SegmentCount = 8;
        shell.Layout.ApplyCommand.Execute(null);

        Assert.Same(project, shell.Project);
        Assert.Equal(ExampleWarehouse.ProjectName, project.Name);
        Assert.Equal(8, project.Layout.SegmentCount);
        Assert.Equal(8 * project.Layout.LevelsPerSegment * project.Layout.ShaftsPerLevel, project.Assignments.Count);
        Assert.Contains(project.Assignments, slot => slot.Segment == 1 && slot.Level == 1 && slot.Shaft == 1 && slot.ArticleId == project.Articles[0].Id && slot.Pinned);
        Assert.DoesNotContain(project.Assignments, slot => slot.Segment > 8);
    }

    [Fact]
    public async Task Scenario_reset_and_optimizer_keep_the_open_project()
    {
        var shell = Harness.CreateShell(optimizer: new FakeOptimizer());
        var project = shell.Project;
        var ids = project.Articles.Select(article => article.Id).ToArray();
        double before = project.FindArticle("EX-A-HALF-LITER")!.InflowCratesPerHour;
        shell.Live.SelectedScenario = project.Scenarios[1].Name;
        shell.Live.ApplyScenarioCommand.Execute(null);

        Assert.Same(project, shell.Project);
        Assert.Equal(project.Scenarios[1].Name, project.ActiveScenarioName);
        Assert.Equal(ids, project.Articles.Select(article => article.Id));
        Assert.NotEqual(before, project.FindArticle("EX-A-HALF-LITER")!.InflowCratesPerHour);
        Assert.Equal(12, project.Layout.SegmentCount);

        shell.Live.PlayCommand.Execute(null);
        shell.Live.ResetPlaybackCommand.Execute(null);
        Assert.Same(project, shell.Project);
        Assert.Equal("00:00:00", shell.Live.TimeText);
        Assert.Equal(ids, project.Articles.Select(article => article.Id));

        shell.Layout.SegmentCount = 6;
        shell.Layout.ApplyCommand.Execute(null);
        await shell.Optimizer.RunCommand.ExecuteAsync(null);
        Assert.Same(project, shell.Project);
        Assert.Equal(6, project.Layout.SegmentCount);
        Assert.Equal(ids, project.Articles.Select(article => article.Id));
        Assert.DoesNotContain(project.Assignments, slot => slot.Segment > 6);
    }

    [Fact]
    public void Generating_articles_keeps_the_project_and_clears_the_assignment()
    {
        var shell = Harness.CreateShell();
        var project = shell.Project;
        shell.Articles.GenerateCount = 12;
        shell.Articles.PercentA = 25;
        shell.Articles.PercentB = 25;
        shell.Articles.PercentC = 50;
        shell.Articles.ReplaceExisting = true;
        shell.Articles.SeedText = "7";
        shell.Articles.RequestGenerateCommand.Execute(null);
        Assert.True(shell.Articles.ReplaceConfirmationVisible);
        Assert.Contains(project.Articles, article => article.Id == "EX-A-HALF-LITER");

        shell.Articles.CancelGenerateCommand.Execute(null);
        Assert.False(shell.Articles.ReplaceConfirmationVisible);
        Assert.Contains(project.Articles, article => article.Id == "EX-A-HALF-LITER");

        shell.Articles.RequestGenerateCommand.Execute(null);
        shell.Articles.ConfirmGenerateCommand.Execute(null);
        Assert.False(shell.Articles.ReplaceConfirmationVisible);
        Assert.Same(project, shell.Project);
        Assert.Equal(ExampleWarehouse.ProjectName, project.Name);
        Assert.Equal(12, project.Layout.SegmentCount);
        Assert.Equal(12, project.Articles.Count);
        Assert.Equal(3, project.Articles.Count(article => article.Class == AbcClass.A));
        Assert.Equal(3, project.Articles.Count(article => article.Class == AbcClass.B));
        Assert.Equal(6, project.Articles.Count(article => article.Class == AbcClass.C));
        Assert.DoesNotContain(project.Articles, article => article.Id.StartsWith("EX-", StringComparison.Ordinal));
        Assert.All(project.Assignments, slot => Assert.Null(slot.ArticleId));
        Assert.All(project.Assignments, slot => Assert.False(slot.Pinned));
        Assert.False(project.IsExampleData);
        Assert.Contains("Generated 12", shell.Articles.Status, StringComparison.Ordinal);
        Assert.Contains(project.CrateTypes, type => project.Articles.Any(article => article.CratesPerPallet == type.CratesPerPallet));
    }

    [Fact]
    public void Generator_appends_without_asking_and_rejects_a_bad_split()
    {
        var shell = Harness.CreateShell();
        int before = shell.Project.Articles.Count;
        shell.Articles.ReplaceExisting = false;
        shell.Articles.GenerateCount = 4;
        shell.Articles.PercentA = 50;
        shell.Articles.PercentB = 50;
        shell.Articles.PercentC = 0;
        shell.Articles.SeedText = "3";
        shell.Articles.RequestGenerateCommand.Execute(null);

        Assert.False(shell.Articles.ReplaceConfirmationVisible);
        Assert.Equal(before + 4, shell.Project.Articles.Count);
        Assert.Contains(shell.Project.Articles, article => article.Id == "EX-A-HALF-LITER");
        Assert.Contains(shell.Project.Articles, article => article.Id == "A-001");
        Assert.All(shell.Project.Assignments, slot => Assert.Null(slot.ArticleId));
        Assert.Equal(12, shell.Project.Layout.SegmentCount);
        Assert.Equal(ExampleWarehouse.ProjectName, shell.Project.Name);

        int after = shell.Project.Articles.Count;
        shell.Articles.PercentA = 10;
        shell.Articles.PercentB = 10;
        shell.Articles.PercentC = 10;
        shell.Articles.RequestGenerateCommand.Execute(null);
        Assert.Equal(UiText.GeneratorPercentSum, shell.Articles.Status);
        Assert.Equal(after, shell.Project.Articles.Count);

        shell.Articles.PercentA = 50;
        shell.Articles.PercentB = 50;
        shell.Articles.PercentC = 0;
        shell.Articles.SeedText = "nope";
        shell.Articles.RequestGenerateCommand.Execute(null);
        Assert.Equal(UiText.GeneratorSeed, shell.Articles.Status);
        Assert.Equal(after, shell.Project.Articles.Count);
    }

    private static string AssignmentSignature(DistributorProject project) =>
        string.Join("|", project.Assignments.Select(slot => $"{slot.Segment}.{slot.Level}.{slot.Shaft}:{slot.ArticleId}:{slot.Pinned}"));

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

    private sealed class ExplodingOptimizer : IOptimizerService
    {
        public Task<OptimizationResult> OptimizeAsync(
            DistributorProject project,
            int? iterations,
            IProgress<OptimizeProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The optimizer should not run for a plan that still fits.");
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
            var live = new LiveDemoViewModel(messenger, clock ?? new ManualClock(), optimizer);
            var layout = new LayoutEditorViewModel(messenger);
            var articles = new ArticlesViewModel(messenger);
            var optimizerView = new OptimizerViewModel(messenger, optimizer);
            var study = new StudyViewModel(messenger, new StudyService(), dialogs);
            var operation = new OperationViewModel(messenger);
            return new ShellViewModel(live, layout, articles, optimizerView, study, operation, messenger, store, dialogs);
        }
    }
}
