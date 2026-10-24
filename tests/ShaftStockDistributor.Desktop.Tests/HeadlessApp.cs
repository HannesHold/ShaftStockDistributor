using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ShaftStockDistributor.Desktop;

[assembly: AvaloniaTestApplication(typeof(ShaftStockDistributor.Desktop.Tests.HeadlessApp))]

namespace ShaftStockDistributor.Desktop.Tests;

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.SkipMainWindow = true;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
    }
}
