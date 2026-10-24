using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Tests;

public class ZoneTests
{
    [Theory]
    [InlineData(1, ZoneKind.Shuttle1Only)]
    [InlineData(4, ZoneKind.Shuttle1Only)]
    [InlineData(5, ZoneKind.Shared)]
    [InlineData(8, ZoneKind.Shared)]
    [InlineData(9, ZoneKind.Shuttle2Only)]
    [InlineData(12, ZoneKind.Shuttle2Only)]
    public void Twelve_segments_split_into_thirds(int segment, ZoneKind zone)
    {
        var geometry = new LayoutGeometry(new LayoutSettings { SegmentCount = 12 });
        Assert.Equal(zone, geometry.ZoneOfSegment(segment));
    }

    [Fact]
    public void Remainder_segments_stay_in_the_shared_middle()
    {
        var geometry = new LayoutGeometry(new LayoutSettings { SegmentCount = 10 });
        Assert.Equal(ZoneKind.Shuttle1Only, geometry.ZoneOfSegment(3));
        Assert.Equal(ZoneKind.Shared, geometry.ZoneOfSegment(4));
        Assert.Equal(ZoneKind.Shared, geometry.ZoneOfSegment(7));
        Assert.Equal(ZoneKind.Shuttle2Only, geometry.ZoneOfSegment(8));
    }

    [Fact]
    public void Fewer_than_three_segments_are_fully_shared()
    {
        var geometry = new LayoutGeometry(new LayoutSettings { SegmentCount = 2 });
        Assert.Equal(ZoneKind.Shared, geometry.ZoneOfSegment(1));
        Assert.Equal(ZoneKind.Shared, geometry.ZoneOfSegment(2));
    }
}

public class CapacityAndTravelTests
{
    [Fact]
    public void Shaft_capacity_uses_flow_axis_and_gap()
    {
        var geometry = new LayoutGeometry(new LayoutSettings { ShaftLengthMeters = 20, CrateGapMeters = 0.1 });
        var article = new Article
        {
            Dimensions = new CrateDimensions
            {
                LengthMillimeters = 400,
                WidthMillimeters = 200,
                HeightMillimeters = 300,
                FlowAxis = FlowAxis.Width
            }
        };

        Assert.Equal(0.2, article.Dimensions.FlowLengthMeters, 3);
        Assert.Equal(66, geometry.CapacityFor(article));
    }

    [Fact]
    public void Worked_example_shaft_count_is_three()
    {
        // Example numbers, not a measured customer case.
        // 20 m shaft, 0.40 m crate, no gap -> 50 crates.
        // Pallet 40 + offset 5 = 45, headroom 5.
        // 600 crates/h during a 90 s wait = 15 crates -> ceil(15 / 5) = 3 shafts.
        int capacity = (int)Math.Floor(20.0 / 0.40);
        Assert.Equal(50, capacity);
        Assert.Equal(3, ShaftCountCalculator.Recommend(600, 40, 5, capacity, 90));
    }

    [Fact]
    public void Simultaneous_axes_use_the_slower_one()
    {
        var shuttle = new ShuttleSettings
        {
            HorizontalSpeedMetersPerSecond = 0.3,
            VerticalSpeedMetersPerSecond = 0.3,
            HorizontalAccelerationMetersPerSecondSquared = 0,
            VerticalAccelerationMetersPerSecondSquared = 0
        };

        Assert.Equal(10, TravelTime.Simultaneous(3, 0.3, shuttle), 6);

        double triangular = TravelTime.Axis(0.3, 0.3, 0.2);
        Assert.Equal(2.0 * Math.Sqrt(0.3 / 0.2), triangular, 6);
        double end = TravelTime.Position(1, 1.3, 0.3, 0.2, triangular);
        Assert.Equal(1.3, end, 6);
    }

    [Fact]
    public void Example_upper_bound_uses_shaft_to_shaft_travel_and_stays_above_the_sweep()
    {
        double bound = TheoreticalThroughput.PalletsPerHour(ExampleWarehouse.Create());
        Assert.True(bound > 113.8, $"upper bound {bound:0.0} is below the balanced study peak");
        Assert.True(bound < 300, $"upper bound {bound:0.0} collapsed toward a transfer-only ceiling");
    }
}

public class ProjectFileTests
{
    [Fact]
    public void Example_project_round_trips_and_warns_about_the_oversized_crate()
    {
        var project = ExampleWarehouse.Create();
        Assert.True(project.IsExampleData);
        Assert.Equal(0.3, project.Shuttle(ShuttleId.Shuttle1).HorizontalSpeedMetersPerSecond);
        Assert.Equal(0.3, project.Shuttle(ShuttleId.Shuttle2).VerticalSpeedMetersPerSecond);
        Assert.NotEqual(
            project.Shuttle(ShuttleId.Shuttle1).Home.XMeters,
            project.Shuttle(ShuttleId.Shuttle2).Home.XMeters);

        string json = ProjectSerializer.Serialize(project);
        var loaded = ProjectSerializer.Deserialize(json);
        Assert.Equal(project.Articles.Count, loaded.Articles.Count);
        Assert.Equal(project.Assignments.Count, loaded.Assignments.Count);
        Assert.Equal(project.Scenarios[1].ShareA, loaded.Scenarios[1].ShareA);
        Assert.Equal(
            project.Shuttle(ShuttleId.Shuttle2).HorizontalAccelerationMetersPerSecondSquared,
            loaded.Shuttle(ShuttleId.Shuttle2).HorizontalAccelerationMetersPerSecondSquared);

        var issues = ProjectValidator.Validate(loaded);
        Assert.Contains(issues, issue => issue.Code == ValidationCodes.PullNeverTriggers && issue.Severity == ValidationSeverity.Warning);
        Assert.Contains(issues, issue => issue.Code == ValidationCodes.CrossSection);
        Assert.DoesNotContain(issues, issue => issue.Severity == ValidationSeverity.Error);
        Assert.Equal(0, loaded.FindArticle("EX-C-OVERSIZE")!.InflowCratesPerHour);
    }

    [Fact]
    public void Scenario_shares_are_split_across_the_class()
    {
        var project = ExampleWarehouse.Create();
        var scenario = project.Scenarios[0];
        var flows = InflowAllocator.Compute(project.Articles, scenario);
        double classA = project.Articles.Where(article => article.Class == AbcClass.A).Sum(article => flows[article.Id]);
        Assert.Equal(scenario.TotalCratesPerHour * 0.70, classA, 3);
        Assert.Equal(0, flows["EX-C-OVERSIZE"]);
    }
}
