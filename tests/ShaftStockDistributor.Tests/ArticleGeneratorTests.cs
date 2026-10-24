using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Tests;

public class ArticleGeneratorTests
{
    private static ArticleGenerationRequest Request(
        int count = 100,
        double a = 20,
        double b = 30,
        double c = 50,
        int? seed = 1,
        bool replace = true,
        double total = 1800,
        IReadOnlyList<CrateType>? types = null) => new()
    {
        Count = count,
        PercentA = a,
        PercentB = b,
        PercentC = c,
        Seed = seed,
        ReplaceExisting = replace,
        TotalCratesPerHour = total,
        CrateTypes = types ?? CrateTypeCatalog.CreateStandard()
    };

    [Theory]
    [InlineData(100, 20, 30, 50, 20, 30, 50)]
    [InlineData(10, 33, 33, 34, 3, 3, 4)]
    [InlineData(7, 10, 20, 70, 1, 1, 5)]
    [InlineData(1, 20, 30, 50, 0, 0, 1)]
    [InlineData(100, 33.33, 33.33, 33.34, 33, 33, 34)]
    public void Class_counts_use_largest_remainder_and_sum_to_the_request(
        int count, double a, double b, double c, int expectA, int expectB, int expectC)
    {
        var split = ArticleGenerator.SplitCounts(count, a, b, c);
        Assert.Equal((expectA, expectB, expectC), split);
        Assert.Equal(count, split.A + split.B + split.C);
    }

    [Fact]
    public void The_same_seed_repeats_the_catalog_and_another_seed_does_not()
    {
        var request = Request(count: 30, seed: 42);
        var first = ArticleGenerator.Generate(request);
        var second = ArticleGenerator.Generate(request);
        var other = ArticleGenerator.Generate(request with { Seed = 99 });

        Assert.True(first.Succeeded);
        Assert.Equal(Signature(first), Signature(second));
        Assert.NotEqual(Signature(first), Signature(other));
    }

    [Fact]
    public void Every_a_mover_has_higher_demand_than_every_b_and_every_b_than_every_c()
    {
        var result = ArticleGenerator.Generate(Request());
        Assert.True(result.Succeeded);
        Assert.Equal(100, result.Articles.Count);
        var types = CrateTypeCatalog.CreateStandard();
        Assert.All(result.Articles, article =>
        {
            Assert.False(string.IsNullOrWhiteSpace(article.Id));
            Assert.False(string.IsNullOrWhiteSpace(article.Name));
            Assert.Contains(types, type =>
                type.CratesPerPallet == article.CratesPerPallet
                && type.Dimensions.LengthMillimeters == article.Dimensions.LengthMillimeters
                && type.Dimensions.WidthMillimeters == article.Dimensions.WidthMillimeters
                && type.Dimensions.HeightMillimeters == article.Dimensions.HeightMillimeters);
        });

        double minA = result.Articles.Where(article => article.Class == AbcClass.A).Min(article => article.InflowCratesPerHour);
        double maxB = result.Articles.Where(article => article.Class == AbcClass.B).Max(article => article.InflowCratesPerHour);
        double minB = result.Articles.Where(article => article.Class == AbcClass.B).Min(article => article.InflowCratesPerHour);
        double maxC = result.Articles.Where(article => article.Class == AbcClass.C).Max(article => article.InflowCratesPerHour);
        Assert.True(minA > maxB, $"A {minA} vs B {maxB}");
        Assert.True(minB > maxC, $"B {minB} vs C {maxC}");
        Assert.Equal(1800, result.Articles.Sum(article => article.InflowCratesPerHour), 3);
        Assert.NotEqual(1, result.Articles[0].Dimensions.LengthMillimeters);
        result.Articles[0].Dimensions.LengthMillimeters = 1;
        Assert.NotEqual(1, types[0].Dimensions.LengthMillimeters);
    }

    [Theory]
    [InlineData(0, 20, 30, 50, ArticleGenerationError.Count)]
    [InlineData(2001, 20, 30, 50, ArticleGenerationError.Count)]
    [InlineData(10, -1, 51, 50, ArticleGenerationError.NegativePercent)]
    [InlineData(10, 20, 30, 40, ArticleGenerationError.PercentSum)]
    public void Invalid_requests_are_rejected(int count, double a, double b, double c, ArticleGenerationError error)
    {
        var request = Request(count, a, b, c);
        Assert.Equal(error, ArticleGenerator.Validate(request));
        var result = ArticleGenerator.Generate(request);
        Assert.False(result.Succeeded);
        Assert.Empty(result.Articles);
    }

    [Fact]
    public void An_empty_crate_catalog_is_rejected()
    {
        var request = Request(types: []);
        Assert.Equal(ArticleGenerationError.NoCrateTypes, ArticleGenerator.Validate(request));
    }

    [Fact]
    public void Applying_a_catalog_keeps_the_layout_and_clears_the_assignment()
    {
        var project = ExampleWarehouse.Create();
        project.Name = "User warehouse";
        project.Layout.SegmentCount = 6;
        ProjectGrid.EnsureFullGrid(project);
        project.Assignments[0].ArticleId = project.Articles[0].Id;
        project.Assignments[0].Pinned = true;
        project.Scenarios[0].ArticleWeights["EX-A-HALF-LITER"] = 2;
        int scenarios = project.Scenarios.Count;

        var result = ArticleGenerator.Apply(project, Request(count: 10, a: 20, b: 30, c: 50, seed: 4));

        Assert.True(result.Succeeded);
        Assert.Equal(10, result.Articles.Count);
        Assert.Equal("User warehouse", project.Name);
        Assert.Equal(6, project.Layout.SegmentCount);
        Assert.Equal(10, project.Articles.Count);
        Assert.Equal(scenarios, project.Scenarios.Count);
        Assert.DoesNotContain(project.Articles, article => article.Id.StartsWith("EX-", StringComparison.Ordinal));
        Assert.All(project.Assignments, slot =>
        {
            Assert.Null(slot.ArticleId);
            Assert.False(slot.Pinned);
        });
        Assert.False(project.IsExampleData);
        Assert.Empty(project.Scenarios[0].ArticleWeights);
        Assert.False(AssignmentFit.MatchesLayout(project) && project.Assignments.Any(slot => slot.ArticleId is not null));
    }

    [Fact]
    public void An_address_outside_the_layout_does_not_match_and_is_reported()
    {
        var project = ExampleWarehouse.Create();
        Assert.True(AssignmentFit.MatchesLayout(project));
        project.Assignments.Add(new ShaftAssignment { Segment = 40, Level = 1, Shaft = 1, ArticleId = "missing" });
        Assert.False(AssignmentFit.MatchesLayout(project));
        var issues = ProjectValidator.Validate(project);
        Assert.Contains(issues, issue => issue.Code == ValidationCodes.AddressOutsideLayout && issue.Severity == ValidationSeverity.Error);
    }

    private static string Signature(ArticleGenerationResult result) =>
        string.Join("|", result.Articles.Select(article =>
            $"{article.Id}:{article.Name}:{article.Class}:{article.InflowCratesPerHour}:{article.Dimensions.LengthMillimeters}:{article.CratesPerPallet}"));
}
