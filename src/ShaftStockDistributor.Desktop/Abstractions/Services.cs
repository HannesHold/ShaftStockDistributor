using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Desktop.Abstractions;

public interface IProjectStore
{
    DistributorProject Load(string path);

    void Save(string path, DistributorProject project);

    DistributorProject CreateExample();

    DistributorProject CreateBlank();
}

public interface IFileDialogs
{
    Task<string?> OpenProjectAsync();

    Task<string?> SaveProjectAsync(string suggestedName);

    Task<string?> SaveTextAsync(string suggestedName, string extension);
}

public interface IUiInvoker
{
    Task InvokeAsync(Action action);
}

public interface IUiClock
{
    IDisposable Every(TimeSpan interval, Action callback);
}

public interface IOptimizerService
{
    Task<OptimizationResult> OptimizeAsync(
        DistributorProject project,
        int? iterations,
        IProgress<OptimizeProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IStudyService
{
    Task<StudyResult> RunAsync(
        DistributorProject project,
        StudyRequest request,
        IProgress<StudyProgress>? progress,
        CancellationToken cancellationToken);
}
