using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShaftStockDistributor.Desktop;
using ShaftStockDistributor.Desktop.Controls;
using ShaftStockDistributor.Desktop.ViewModels;

namespace ShaftStockDistributor.Desktop.Tests;

public sealed class TooltipTests
{
    [AvaloniaFact]
    public void Every_button_has_a_tooltip()
    {
        var host = Host.CreateDefaultBuilder().ConfigureServices(AppServices.Configure).Build();
        var shell = host.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow { DataContext = shell };
        window.Show();

        var missing = new List<string>();
        int seen = 0;
        foreach (string page in new[] { "Live", "Layout", "Articles", "Optimizer", "Study", "Operation" })
        {
            shell.ShowPageCommand.Execute(page);
            Dispatcher.UIThread.RunJobs();
            foreach (var button in window.GetLogicalDescendants().OfType<Button>())
            {
                seen++;
                string tip = TipText(ToolTip.GetTip(button));
                if (string.IsNullOrWhiteSpace(tip))
                {
                    missing.Add(page + ": " + button.Content);
                }
            }

            if (page == "Live")
            {
                var aisle = window.GetLogicalDescendants().OfType<WarehouseCanvas>().Single();
                Assert.False(string.IsNullOrWhiteSpace(TipText(ToolTip.GetTip(aisle))));
            }
        }

        Assert.True(missing.Count == 0, string.Join("; ", missing));
        Assert.True(seen >= 45, $"only {seen} buttons were realized");
    }

    private static string TipText(object? tip) => tip switch
    {
        string text => text,
        TextBlock block => block.Text ?? "",
        _ => ""
    };
}
