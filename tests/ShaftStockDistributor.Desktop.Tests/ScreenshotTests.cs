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
    public async Task Renders_live_demo_and_scenario_study()
    {
        var host = Host.CreateDefaultBuilder().ConfigureServices(AppServices.Configure).Build();
        var shell = host.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow
        {
            DataContext = shell,
            Width = 1440,
            Height = 900
        };
        window.Show();

        await shell.StartDemoCommand.ExecuteAsync(null);
        shell.Live.PauseCommand.Execute(null);
        shell.Live.ResetPlaybackCommand.Execute(null);
        for (int step = 0; step < 24; step++)
        {
            shell.Live.AdvanceSeconds(45);
            var snapshot = shell.Live.Snapshot;
            bool moving = snapshot?.Shuttles.Any(shuttle => shuttle.Phase is "Traveling" or "Transferring" or "Waiting") == true;
            bool charted = shell.Live.PalletHistory.Any(value => value > 0.05);
            if (moving && charted && snapshot!.Kpis.Pallets > 0)
            {
                break;
            }
        }

        var live = shell.Live.Snapshot;
        Assert.NotNull(live);
        Assert.Contains(live.Shafts, shaft => shaft.VisualFill > 0.2);
        Assert.Contains(live.Shuttles, shuttle => shuttle.Phase is "Traveling" or "Transferring" or "Waiting");
        Assert.Contains(shell.Live.PalletHistory, value => value > 0.05);
        Assert.NotEqual("0.0", shell.Live.PalletsPerHourText);
        await SettleAsync();
        var liveFrame = Save(window, "live-demo.png");
        Assert.True(liveFrame.PixelSize.Width >= 1200, "The live window should render at the desktop size.");
        Assert.True(UniqueColors(liveFrame) > 12, "The live demo frame should show the rack, not a flat fill.");

        shell.ShowPageCommand.Execute("Study");
        shell.Study.Iterations = 40;
        await shell.Study.RunCommand.ExecuteAsync(null);
        Assert.NotEmpty(shell.Study.Rows);
        Assert.NotEmpty(shell.Study.Saturations);
        Assert.NotEmpty(shell.Study.StudySeries);
        await SettleAsync();
        var studyFrame = Save(window, "scenario-study.png");
        Assert.True(studyFrame.PixelSize.Width >= 1200);
        Assert.True(UniqueColors(studyFrame) > 12, "The study frame should show the chart and table.");
    }

    private static async Task SettleAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static Bitmap Save(Window window, string fileName)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Headless capture returned no frame.");
        string directory = Path.Combine(RepoRoot(), "docs", "screenshots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        frame.Save(path);
        Assert.True(new FileInfo(path).Length > 10_000);
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
