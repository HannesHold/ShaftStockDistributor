using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShaftStockDistributor.Desktop;
using ShaftStockDistributor.Desktop.ViewModels;

namespace ShaftStockDistributor.Desktop.Tests;

public sealed class ScreenshotTests
{
    [AvaloniaFact]
    public async Task Renders_every_page_at_both_desktop_sizes()
    {
        var host = Host.CreateDefaultBuilder().ConfigureServices(AppServices.Configure).Build();
        var shell = host.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        await Resize(window, 1280, 800);

        await shell.StartDemoCommand.ExecuteAsync(null);
        shell.Live.PauseCommand.Execute(null);
        shell.Live.ResetPlaybackCommand.Execute(null);
        for (int step = 0; step < 40; step++)
        {
            shell.Live.AdvanceSeconds(60);
            var snapshot = shell.Live.Snapshot;
            bool moving = snapshot?.Shuttles.Any(shuttle => shuttle.Phase is "Traveling" or "Transferring" or "Waiting") == true;
            bool pastWindow = snapshot is not null && snapshot.TimeSeconds >= 35 * 60;
            if (moving && pastWindow && shell.Live.PalletHistory.Count > 0 && snapshot!.Kpis.Pallets > 0)
            {
                break;
            }
        }

        var live = shell.Live.Snapshot;
        Assert.NotNull(live);
        Assert.Contains(live.Shafts, shaft => shaft.VisualFill > 0.2);
        Assert.Contains(live.Shuttles, shuttle => shuttle.Phase is "Traveling" or "Transferring" or "Waiting");
        Assert.Contains(shell.Live.PalletHistory, value => value > 0.05);
        Assert.NotEqual(UiText.RateWarmingUp, shell.Live.PalletsPerHourText);
        Assert.Contains("average", shell.Live.RateCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(UiText.OpeningExcluded, shell.Live.OpeningNote);
        Assert.Contains("opening", shell.Live.OpeningIncludedNote, StringComparison.OrdinalIgnoreCase);
        Assert.True(live.Kpis.OpeningPallets > 0);
        Assert.True(live.Kpis.SteadyPallets < live.Kpis.Pallets);
        double.TryParse(shell.Live.PalletsPerHourText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double shownRate);
        Assert.True(shownRate > 5 && shownRate < 120, $"shown rate {shell.Live.PalletsPerHourText}");
        await SettleAsync();
        Save(window, "live-demo.png", 1280);

        await CapturePage(shell, window, "Layout", "layout.png");
        await CapturePage(shell, window, "Articles", "articles.png");
        await CapturePage(shell, window, "Optimizer", "optimizer.png");
        await CapturePage(shell, window, "Operation", "operation.png");

        shell.ShowPageCommand.Execute("Study");
        shell.Study.Iterations = 40;
        await shell.Study.RunCommand.ExecuteAsync(null);
        Assert.NotEmpty(shell.Study.Rows);
        Assert.NotEmpty(shell.Study.Saturations);
        Assert.Contains("Max sustainable", shell.Study.Saturations[0].Detail, StringComparison.Ordinal);
        await SettleAsync();
        Save(window, "scenario-study.png", 1280);

        await Resize(window, 1920, 1080);
        shell.ShowPageCommand.Execute("Live");
        await SettleAsync();
        Save(window, "live-demo-wide.png", 1920);
        shell.ShowPageCommand.Execute("Study");
        await SettleAsync();
        Save(window, "scenario-study-wide.png", 1920);
    }

    private static async Task CapturePage(ShellViewModel shell, Window window, string page, string fileName)
    {
        shell.ShowPageCommand.Execute(page);
        await SettleAsync();
        Save(window, fileName, 1280);
    }

    private static async Task Resize(Window window, int width, int height)
    {
        window.Width = width;
        window.Height = height;
        await SettleAsync();
    }

    private static async Task SettleAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
    }

    private static Bitmap Save(Window window, string fileName, int expectedWidth)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Headless capture returned no frame.");
        string directory = Path.Combine(RepoRoot(), "docs", "screenshots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        frame.Save(path);
        Assert.True(new FileInfo(path).Length > 10_000, fileName);
        Assert.True(frame.PixelSize.Width >= expectedWidth - 40, $"{fileName} width {frame.PixelSize.Width}");
        Assert.True(UniqueColors(frame) > 12, fileName);
        return frame;
    }

    private static int UniqueColors(Bitmap bitmap)
    {
        int width = bitmap.PixelSize.Width;
        int height = bitmap.PixelSize.Height;
        int stride = width * 4;
        var bytes = new byte[stride * height];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), bytes.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var colors = new HashSet<int>();
        for (int y = 0; y < height; y += 6)
        {
            for (int x = 0; x < width; x += 6)
            {
                int index = y * stride + x * 4;
                int key = (bytes[index] / 16 << 8) | (bytes[index + 1] / 16 << 4) | (bytes[index + 2] / 16);
                colors.Add(key);
            }
        }

        return colors.Count;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
