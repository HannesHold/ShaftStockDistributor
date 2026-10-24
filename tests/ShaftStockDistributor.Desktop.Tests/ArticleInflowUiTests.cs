using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShaftStockDistributor.Desktop;
using ShaftStockDistributor.Desktop.ViewModels;

namespace ShaftStockDistributor.Desktop.Tests;

public sealed class ArticleInflowUiTests
{
    [AvaloniaFact]
    public async Task Typing_an_inflow_and_pressing_Apply_updates_the_article()
    {
        var host = Host.CreateDefaultBuilder().ConfigureServices(AppServices.Configure).Build();
        var shell = host.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow { DataContext = shell, Width = 1280, Height = 900 };
        window.Show();
        shell.ShowPageCommand.Execute("Articles");
        await SettleAsync();

        var article = shell.Project.Articles[0];
        var box = window.GetVisualDescendants().OfType<TextBox>().First(item => item.Name == "ArticleInflowBox");
        var button = window.GetVisualDescendants().OfType<Button>().First(item => item.Name == "ArticleApplyButton");
        box.BringIntoView();
        button.BringIntoView();
        await SettleAsync();

        box.Focus();
        box.Text = "4321";
        await SettleAsync();
        double draftAfterTyping = shell.Articles.Draft.Inflow;

        var point = button.TranslatePoint(new Point(8, 8), window) ?? throw new InvalidOperationException("Apply button is not on screen.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await SettleAsync();

        double projectInflow = shell.Project.FindArticle(article.Id)!.InflowCratesPerHour;
        Assert.True(
            projectInflow == 4321,
            $"Apply left inflow at {projectInflow}. Draft after typing was {draftAfterTyping}. Box text is '{box.Text}'. Status: {shell.Articles.Status}");
        Assert.Contains(article.Name, shell.Articles.Status, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task An_open_grid_inflow_edit_is_committed_when_Apply_is_pressed()
    {
        var host = Host.CreateDefaultBuilder().ConfigureServices(AppServices.Configure).Build();
        var shell = host.Services.GetRequiredService<ShellViewModel>();
        var window = new MainWindow { DataContext = shell, Width = 1280, Height = 900 };
        window.Show();
        shell.ShowPageCommand.Execute("Articles");
        await SettleAsync();

        var article = shell.Project.Articles[0];
        var grid = window.GetVisualDescendants().OfType<DataGrid>().First(item => item.Name == "ArticleGrid");
        grid.SelectedIndex = 0;
        grid.CurrentColumn = grid.Columns.First(column => Equals(column.Header, "Inflow"));
        grid.Focus();
        await SettleAsync();
        Assert.True(grid.BeginEdit());
        var editor = grid.GetVisualDescendants().OfType<TextBox>().First(item => item.Name != "ArticleInflowBox");
        editor.Text = "4321";
        await SettleAsync();

        var button = window.GetVisualDescendants().OfType<Button>().First(item => item.Name == "ArticleApplyButton");
        button.BringIntoView();
        await SettleAsync();
        var point = button.TranslatePoint(new Point(8, 8), window) ?? throw new InvalidOperationException("Apply button is not on screen.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await SettleAsync();

        Assert.Equal(4321, shell.Project.FindArticle(article.Id)!.InflowCratesPerHour);
        Assert.Equal(4321, shell.Live.Snapshot!.ArticleFlows.First(flow => flow.Id == article.Id).InflowCratesPerHour);
        Assert.Contains(article.Name, shell.Articles.Status, StringComparison.Ordinal);
    }

    private static async Task SettleAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
    }
}
