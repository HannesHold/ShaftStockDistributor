using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ShaftStockDistributor.Core;
using ShaftStockDistributor.Desktop.Abstractions;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.Services;

public sealed class JsonProjectStore : IProjectStore
{
    public DistributorProject Load(string path) => ProjectSerializer.Load(path);

    public void Save(string path, DistributorProject project) => ProjectSerializer.Save(path, project);

    public DistributorProject CreateExample() => ExampleWarehouse.Create();

    public DistributorProject CreateBlank() => ExampleWarehouse.CreateBlank();
}

public sealed class AvaloniaFileDialogs : IFileDialogs
{
    public Task<string?> OpenProjectAsync() => PickOpen("json");

    public Task<string?> SaveProjectAsync(string suggestedName) => PickSave(suggestedName, "json");

    public Task<string?> SaveTextAsync(string suggestedName, string extension) => PickSave(suggestedName, extension);

    private static async Task<string?> PickOpen(string extension)
    {
        var window = Window();
        if (window is null)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = UiText.Open,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Project") { Patterns = new[] { "*." + extension } }
            }
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private static async Task<string?> PickSave(string suggestedName, string extension)
    {
        var window = Window();
        if (window is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = UiText.Save,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(extension) { Patterns = new[] { "*." + extension } }
            }
        });
        return file?.TryGetLocalPath();
    }

    private static Window? Window() =>
        global::Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}

public sealed class AvaloniaUiInvoker : IUiInvoker
{
    public Task InvokeAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var source = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action();
                source.SetResult();
            }
            catch (Exception exception)
            {
                source.SetException(exception);
            }
        });
        return source.Task;
    }
}

public sealed class AvaloniaUiClock : IUiClock
{
    public IDisposable Every(TimeSpan interval, Action callback)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => callback();
        timer.Start();
        return new TimerStop(timer);
    }

    private sealed class TimerStop : IDisposable
    {
        private readonly DispatcherTimer _timer;

        public TimerStop(DispatcherTimer timer) => _timer = timer;

        public void Dispose() => _timer.Stop();
    }
}

public sealed class OptimizerService : IOptimizerService
{
    public Task<OptimizationResult> OptimizeAsync(
        DistributorProject project,
        int? iterations,
        IProgress<OptimizeProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => AnnealingOptimizer.Run(project, progress, cancellationToken, iterations), cancellationToken);
}

public sealed class StudyService : IStudyService
{
    public Task<StudyResult> RunAsync(
        DistributorProject project,
        StudyRequest request,
        IProgress<StudyProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => ScenarioStudyRunner.Run(project, request, progress, cancellationToken), cancellationToken);
}
