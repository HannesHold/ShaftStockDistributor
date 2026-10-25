using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using ShaftStockDistributor.Desktop.Abstractions;
using ShaftStockDistributor.Desktop.Services;
using ShaftStockDistributor.Desktop.ViewModels;

namespace ShaftStockDistributor.Desktop;

public static class AppServices
{
    public static void Configure(IServiceCollection services)
    {
        services.AddSingleton<IMessenger, WeakReferenceMessenger>();
        services.AddSingleton<IProjectStore, JsonProjectStore>();
        services.AddSingleton<IFileDialogs, AvaloniaFileDialogs>();
        services.AddSingleton<IUiInvoker, AvaloniaUiInvoker>();
        services.AddSingleton<IUiClock, AvaloniaUiClock>();
        services.AddSingleton<IOptimizerService, OptimizerService>();
        services.AddSingleton<IStudyService, StudyService>();
        services.AddSingleton<ConfigurationLock>();
        services.AddSingleton<LiveDemoViewModel>();
        services.AddSingleton<LayoutEditorViewModel>();
        services.AddSingleton<ArticlesViewModel>();
        services.AddSingleton<OptimizerViewModel>();
        services.AddSingleton<StudyViewModel>();
        services.AddSingleton<OperationViewModel>();
        services.AddSingleton<ShellViewModel>();
    }
}
