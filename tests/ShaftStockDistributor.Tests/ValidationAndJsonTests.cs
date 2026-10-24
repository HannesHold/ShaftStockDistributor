using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Tests;

public class ValidationAndJsonTests
{
    [Fact]
    public void Sample_documents_round_trip_and_keep_one_based_homes()
    {
        var directory = Directory.CreateTempSubdirectory("ssd-json-");
        try
        {
            var layoutPath = Path.Combine(directory.FullName, "layout.json");
            var articlePath = Path.Combine(directory.FullName, "articles.json");
            WarehouseFiles.Save(layoutPath, WarehouseMapper.ToLayout(SampleWarehouse.Layout()));
            WarehouseFiles.Save(articlePath, WarehouseMapper.ToArticles(SampleWarehouse.Articles()));

            var layout = WarehouseFiles.LoadLayout(layoutPath);
            var articles = WarehouseFiles.LoadArticles(articlePath);
            var json = File.ReadAllText(layoutPath);

            Assert.Contains("\"segment\": 12", json, StringComparison.Ordinal);
            Assert.Equal(12, articles.Items.Count);
            Assert.True(layout.ExampleData);
            Assert.Equal(0, layout.Shuttle1Home.Segment);
            Assert.Equal(11, layout.Shuttle2Home.Segment);
            Assert.Equal(8, layout.Shuttle2Home.Shaft);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Optimized_sample_assignment_is_valid()
    {
        var layout = SampleWarehouse.CreateLayout();
        var articles = SampleWarehouse.CreateArticles();
        var parameters = SampleWarehouse.CreateParameters();
        parameters.Annealing.Iterations = 50;
        var result = ShaftStockDistributor.Optimizer.AnnealingOptimizer.Optimize(layout, articles, parameters);

        var messages = WarehouseValidator.Validate(layout, articles, result.Assignment);

        Assert.DoesNotContain(messages, message => message.IsError);
        Assert.All(articles.Items, article => Assert.True(result.Assignment.CountFor(article.Id) >= 1));
    }

    [Fact]
    public void Missing_shaft_and_short_capacity_are_errors()
    {
        var layout = SampleWarehouse.CreateLayout();
        var articles = SampleWarehouse.CreateArticles();
        var assignment = new Assignment();
        assignment.Set(new ShaftLocation(0, 0, 0), "BEER-20-05", pinned: false);
        articles.Find("BEER-20-05")!.ShaftCapacity = 10;

        var messages = WarehouseValidator.Validate(layout, articles, assignment);

        Assert.Contains(messages, message => message.IsError && message.Text.Contains("no shaft", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.IsError && message.Text.Contains("BEER-20-05", StringComparison.Ordinal) && message.Text.Contains("capacity", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_article_is_an_error()
    {
        var layout = SampleWarehouse.CreateLayout();
        var articles = SampleWarehouse.CreateArticles();
        var assignment = new Assignment();
        foreach (var article in articles.Items)
        {
            assignment.Set(new ShaftLocation(0, 0, articles.Items.IndexOf(article)), article.Id, pinned: false);
        }

        assignment.Set(new ShaftLocation(1, 0, 0), "DOES-NOT-EXIST", pinned: true);
        var messages = WarehouseValidator.Validate(layout, articles, assignment);

        Assert.Contains(messages, message => message.IsError && message.Text.Contains("DOES-NOT-EXIST", StringComparison.Ordinal));
    }
}
