using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShaftStockDistributor.Desktop.ViewModels;

namespace ShaftStockDistributor.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Headless tests render their own window and must not open a second one.</summary>
    public static bool SkipMainWindow { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (SkipMainWindow)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(AppServices.Configure)
            .Build();
        Services = host.Services;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                DataContext = Services.GetRequiredService<ShellViewModel>()
            };
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
